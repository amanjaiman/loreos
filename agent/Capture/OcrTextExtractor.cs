using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Lore.Agent.Capture;

/// <summary>The fallback path: when UI Automation exposes no text, capture the window's
/// pixels and run the OS-native <see cref="OcrEngine"/> over them. Chosen over a
/// third-party engine because it ships with Windows (no extra dependency, no key, nothing
/// leaves the machine — constitution §1/§5). Like the other extractors it is total: a
/// capture or recognition failure yields <see cref="ExtractedText.Empty"/>, and if the
/// machine has no OCR language pack the extractor is simply a no-op.
///
/// <para>This is platform glue — GDI capture plus WinRT OCR — that can't be exercised
/// headlessly; the tested behavior is the fallback wiring in
/// <see cref="CompositeTextExtractor"/>. The native surface is kept tight and every GDI
/// handle is released in a finally.</para></summary>
public sealed partial class OcrTextExtractor : ITextExtractor
{
    private const uint PrintWindowFullContent = 0x00000002; // PW_RENDERFULLCONTENT
    private const uint DibRgbColors = 0;
    private const ushort BiRgb = 0;
    private const ushort BitsPerPixel = 32;

    // The OS OCR engine for the user's languages, created once. Null when no OCR language
    // pack is installed — in which case this extractor always yields Empty.
    private readonly Lazy<OcrEngine?> _engine = new(
        OcrEngine.TryCreateFromUserProfileLanguages, LazyThreadSafetyMode.ExecutionAndPublication);

    public async Task<ExtractedText> ExtractAsync(
        WindowSnapshot window, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        OcrEngine? engine = _engine.Value;
        if (window.IsEmpty || engine is null)
        {
            return ExtractedText.Empty;
        }

        try
        {
            using SoftwareBitmap? bitmap = CaptureWindow(new IntPtr(window.Handle));
            if (bitmap is null)
            {
                return ExtractedText.Empty;
            }

            OcrResult result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(result.Text)
                ? ExtractedText.Empty
                : new ExtractedText(result.Text, ExtractionSource.Ocr);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // GDI/WinRT surface many failure modes; none should kill the loop
        catch (Exception)
#pragma warning restore CA1031
        {
            return ExtractedText.Empty;
        }
    }

    /// <summary>Render the window into an off-screen bitmap and hand back a BGRA
    /// <see cref="SoftwareBitmap"/>, or <c>null</c> if it can't be captured. Every GDI
    /// object created here is freed before returning.</summary>
    private static SoftwareBitmap? CaptureWindow(IntPtr handle)
    {
        if (!GetWindowRect(handle, out RECT rect))
        {
            return null;
        }

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        IntPtr windowDc = GetWindowDC(handle);
        if (windowDc == IntPtr.Zero)
        {
            return null;
        }

        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(windowDc);
            bitmap = CreateCompatibleBitmap(windowDc, width, height);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                return null;
            }

            IntPtr previous = SelectObject(memoryDc, bitmap);
            bool rendered = PrintWindow(handle, memoryDc, PrintWindowFullContent);
            SelectObject(memoryDc, previous);
            if (!rendered)
            {
                return null;
            }

            return ReadPixels(windowDc, bitmap, width, height);
        }
        finally
        {
            if (bitmap != IntPtr.Zero)
            {
                _ = DeleteObject(bitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                _ = DeleteDC(memoryDc);
            }

            _ = ReleaseDC(handle, windowDc);
        }
    }

    private static SoftwareBitmap? ReadPixels(IntPtr dc, IntPtr bitmap, int width, int height)
    {
        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // negative => top-down rows, matching SoftwareBitmap's layout
            biPlanes = 1,
            biBitCount = BitsPerPixel,
            biCompression = BiRgb,
        };

        long pixelCount = (long)width * height;
        if (pixelCount > Array.MaxLength / 4)
        {
            return null;
        }

        byte[] pixels = new byte[pixelCount * 4];
        int scanLines = GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref header, DibRgbColors);
        if (scanLines == 0)
        {
            return null;
        }

        return SoftwareBitmap.CreateCopyFromBuffer(
            pixels.AsBuffer(), BitmapPixelFormat.Bgra8, width, height);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr GetWindowDC(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr hObject);

    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetDIBits(
        IntPtr hdc, IntPtr hbm, uint start, uint cLines, [Out] byte[] lpvBits,
        ref BITMAPINFOHEADER lpbmi, uint usage);
}
