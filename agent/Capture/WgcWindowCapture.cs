using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

namespace Lore.Agent.Capture;

/// <summary>Captures a window's pixels via Windows.Graphics.Capture (WGC) — the modern path
/// that can grab GPU-composited and fullscreen (DirectX) windows that GDI
/// <see cref="OcrTextExtractor"/>'s <c>PrintWindow</c> renders as a black frame. This is what
/// lets OCR read fullscreen games, hardware-accelerated apps, remote-desktop sessions, and
/// WebGL/canvas surfaces that expose no UI Automation text.
///
/// <para>Deliberately total, like the other platform seams: any failure — WGC unsupported on
/// the OS, a window that refuses capture (secure/DRM content sets WDA_EXCLUDEFROMCAPTURE and
/// stays black <em>by design</em>, which we want), device loss, or a frame that never arrives —
/// yields <c>null</c> so the caller falls back to the GDI path rather than throwing. The
/// Direct3D device is built once and reused; a frame pool + session are created per capture
/// (the cadence is low — dwell- and recapture-gated).</para>
///
/// <para>Native surface, not exercisable headlessly. The D3D device is created by hand
/// (<c>D3D11CreateDevice</c> → <c>CreateDirect3D11DeviceFromDXGIDevice</c>) and the capture item
/// is obtained through the WGC interop factory, so no third-party native dependency is added
/// (constitution §1/§8). Every raw COM pointer is released in a finally.</para></summary>
public sealed partial class WgcWindowCapture : IDisposable
{
    private const uint D3D11CreateDeviceBgraSupport = 0x20; // D3D11_CREATE_DEVICE_BGRA_SUPPORT
    private const uint D3D11SdkVersion = 7;                  // D3D11_SDK_VERSION
    private const int DriverTypeHardware = 1;                // D3D_DRIVER_TYPE_HARDWARE
    private const int DriverTypeWarp = 5;                    // D3D_DRIVER_TYPE_WARP (software)

    private static readonly Guid IidIDxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid IidIGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");

    // One frame is all we need; give the pool a moment to produce it before falling back.
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromMilliseconds(700);

    private readonly bool _supported = GraphicsCaptureSession.IsSupported();
    private readonly Lazy<IDirect3DDevice?> _device;
    private readonly ILogger<WgcWindowCapture> _logger;

    // One-time diagnostics so /system/log shows whether WGC is actually feeding OCR, without
    // logging on every capture tick.
    private int _statusLogged;
    private int _outcomeLogged;

    public WgcWindowCapture(ILogger<WgcWindowCapture> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _device = new Lazy<IDirect3DDevice?>(TryCreateDevice, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Whether WGC is available on this OS at all (Windows 10 1809+).</summary>
    public bool IsSupported => _supported;

    /// <summary>Capture <paramref name="hwnd"/> to a BGRA <see cref="SoftwareBitmap"/>, or
    /// <c>null</c> if WGC is unsupported or the capture fails for any reason (the caller then
    /// falls back to GDI).</summary>
    public async Task<SoftwareBitmap?> CaptureAsync(IntPtr hwnd, CancellationToken cancellationToken = default)
    {
        if (!_supported || hwnd == IntPtr.Zero)
        {
            return null;
        }

        IDirect3DDevice? device = _device.Value;
        if (Interlocked.Exchange(ref _statusLogged, 1) == 0)
        {
            _logger.LogInformation(
                "WGC capture: supported={Supported}, device={Device}",
                _supported, device is null ? "unavailable" : "ready");
        }

        if (device is null)
        {
            return null;
        }

        GraphicsCaptureItem? item = TryCreateItemForWindow(hwnd);
        if (item is null || item.Size.Width <= 0 || item.Size.Height <= 0)
        {
            return null;
        }

        var completion = new TaskCompletionSource<SoftwareBitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Direct3D11CaptureFramePool? pool = null;
        GraphicsCaptureSession? session = null;

        void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            Direct3D11CaptureFrame? frame;
            try
            {
                frame = sender.TryGetNextFrame();
            }
#pragma warning disable CA1031 // a bad frame just means no capture this round → fall back
            catch (Exception)
#pragma warning restore CA1031
            {
                completion.TrySetResult(null);
                return;
            }

            if (frame is null)
            {
                return; // nothing yet; wait for the next FrameArrived
            }

            // Copy off the GPU surface, THEN dispose the frame — the async copy reads the surface,
            // so disposing it first would race the read.
            SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface).AsTask().ContinueWith(
                copy =>
                {
                    frame.Dispose();
                    completion.TrySetResult(
                        copy.Status == TaskStatus.RanToCompletion ? copy.Result : null);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        try
        {
            // Free-threaded: FrameArrived fires on the thread pool, so this works from the
            // capture loop's BackgroundService thread with no DispatcherQueue.
            pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
            pool.FrameArrived += OnFrameArrived;

            session = pool.CreateCaptureSession(item);
            SuppressCaptureChrome(session);
            session.StartCapture();

            SoftwareBitmap? captured = await completion.Task.WaitAsync(FrameTimeout, cancellationToken).ConfigureAwait(false);
            if (Interlocked.Exchange(ref _outcomeLogged, 1) == 0)
            {
                _logger.LogInformation(
                    "WGC first outcome: {Result} ({Width}x{Height})",
                    captured is null ? "surface copy failed → GDI fallback" : "frame captured",
                    item.Size.Width, item.Size.Height);
            }

            return captured;
        }
        catch (TimeoutException)
        {
            if (Interlocked.Exchange(ref _outcomeLogged, 1) == 0)
            {
                _logger.LogInformation("WGC first outcome: no frame within {Timeout}ms → GDI fallback", FrameTimeout.TotalMilliseconds);
            }

            return null; // no frame arrived in time → fall back to GDI
        }
        catch (OperationCanceledException)
        {
            throw; // honor shutdown rather than masking it as a capture miss
        }
#pragma warning disable CA1031 // any other WGC failure degrades to the GDI fallback (criterion 6)
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
        finally
        {
            if (pool is not null)
            {
                pool.FrameArrived -= OnFrameArrived;
            }

            session?.Dispose();
            pool?.Dispose();
        }
    }

    public void Dispose()
    {
        if (_device.IsValueCreated)
        {
            _device.Value?.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    // Hide the cursor from the frame (OCR noise) and suppress the capture border. The border
    // property (IsBorderRequired) is Windows 11 (10.0.22000+) only and absent from this TFM's
    // projection, so it is set by reflection when present and ignored on Windows 10.
    private static void SuppressCaptureChrome(GraphicsCaptureSession session)
    {
        try
        {
            session.IsCursorCaptureEnabled = false;
            typeof(GraphicsCaptureSession).GetProperty("IsBorderRequired")?.SetValue(session, false);
        }
#pragma warning disable CA1031 // best-effort; a missing/guarded property must not fail the capture
        catch (Exception)
#pragma warning restore CA1031
        {
            // Windows 10 (no border property) or a capability-gated setter: leave defaults.
        }
    }

    // Build a reusable WinRT Direct3D device. Prefer the GPU; fall back to WARP (software) so a
    // GPU-less/headless machine can still capture. Returns null if D3D itself is unavailable.
    private static IDirect3DDevice? TryCreateDevice()
    {
        IntPtr d3dDevice = IntPtr.Zero, context = IntPtr.Zero, dxgiDevice = IntPtr.Zero, inspectable = IntPtr.Zero;
        try
        {
            int hr = D3D11CreateDevice(
                IntPtr.Zero, DriverTypeHardware, IntPtr.Zero, D3D11CreateDeviceBgraSupport,
                IntPtr.Zero, 0, D3D11SdkVersion, out d3dDevice, out _, out context);
            if (hr < 0)
            {
                hr = D3D11CreateDevice(
                    IntPtr.Zero, DriverTypeWarp, IntPtr.Zero, D3D11CreateDeviceBgraSupport,
                    IntPtr.Zero, 0, D3D11SdkVersion, out d3dDevice, out _, out context);
            }

            if (hr < 0 || d3dDevice == IntPtr.Zero)
            {
                return null;
            }

            Guid dxgiIid = IidIDxgiDevice;
            if (Marshal.QueryInterface(d3dDevice, ref dxgiIid, out dxgiDevice) < 0 || dxgiDevice == IntPtr.Zero)
            {
                return null;
            }

            if (CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out inspectable) < 0 || inspectable == IntPtr.Zero)
            {
                return null;
            }

            // FromAbi adds its own reference; the raw pointers below are released here and the
            // returned WinRT device keeps the underlying D3D/DXGI device alive.
            return MarshalInspectable<IDirect3DDevice>.FromAbi(inspectable);
        }
#pragma warning disable CA1031 // no D3D device → WGC simply unavailable, caller falls back
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
        finally
        {
            if (inspectable != IntPtr.Zero)
            {
                _ = Marshal.Release(inspectable);
            }

            if (dxgiDevice != IntPtr.Zero)
            {
                _ = Marshal.Release(dxgiDevice);
            }

            if (context != IntPtr.Zero)
            {
                _ = Marshal.Release(context);
            }

            if (d3dDevice != IntPtr.Zero)
            {
                _ = Marshal.Release(d3dDevice);
            }
        }
    }

    // Obtain a GraphicsCaptureItem for an HWND via the WGC interop activation factory. WinRT
    // exposes no projected way to do this from a window handle, so we go through the interop
    // COM interface, exactly as the platform intends.
    private static GraphicsCaptureItem? TryCreateItemForWindow(IntPtr hwnd)
    {
        const string RuntimeClass = "Windows.Graphics.Capture.GraphicsCaptureItem";
        IntPtr classId = IntPtr.Zero, factory = IntPtr.Zero;
        try
        {
            if (WindowsCreateString(RuntimeClass, RuntimeClass.Length, out classId) < 0)
            {
                return null;
            }

            if (RoGetActivationFactory(classId, IidIGraphicsCaptureItemInterop, out factory) < 0
                || factory == IntPtr.Zero)
            {
                return null;
            }

            var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory);
            Guid itemIid = typeof(GraphicsCaptureItem).GUID;
            IntPtr itemAbi = interop.CreateForWindow(hwnd, ref itemIid);
            if (itemAbi == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return GraphicsCaptureItem.FromAbi(itemAbi);
            }
            finally
            {
                _ = Marshal.Release(itemAbi);
            }
        }
#pragma warning disable CA1031 // an uncapturable window → fall back to GDI
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
        finally
        {
            if (factory != IntPtr.Zero)
            {
                _ = Marshal.Release(factory);
            }

            if (classId != IntPtr.Zero)
            {
                _ = WindowsDeleteString(classId);
            }
        }
    }

    // The WGC interop factory interface: turns a window/monitor handle into a capture item.
    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, [In] ref Guid iid);

        IntPtr CreateForMonitor(IntPtr monitor, [In] ref Guid iid);
    }

    [LibraryImport("d3d11.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int D3D11CreateDevice(
        IntPtr pAdapter, int driverType, IntPtr software, uint flags,
        IntPtr pFeatureLevels, uint featureLevels, uint sdkVersion,
        out IntPtr ppDevice, out int pFeatureLevel, out IntPtr ppImmediateContext);

    [LibraryImport("d3d11.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [LibraryImport("combase.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int WindowsDeleteString(IntPtr hstring);

    [LibraryImport("combase.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RoGetActivationFactory(IntPtr activatableClassId, in Guid iid, out IntPtr factory);
}
