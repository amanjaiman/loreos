using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Lore.Agent.Hosting;
using Lore.Agent.Memory;

namespace Lore.Agent.Tests.Hosting;

public sealed class RemoteEngineTests
{
    [Fact]
    public void Remote_base_address_is_remote_url_with_a_trailing_slash()
    {
        var noSlash = new MemorydOptions { Engine = "remote", RemoteUrl = new Uri("http://host:9000/mem") };
        Assert.Equal("http://host:9000/mem/", noSlash.BaseAddress.AbsoluteUri);

        var withSlash = new MemorydOptions { Engine = "remote", RemoteUrl = new Uri("http://host:9000/") };
        Assert.Equal("http://host:9000/", withSlash.BaseAddress.AbsoluteUri);

        var embedded = new MemorydOptions { Engine = "embedded", Port = 7843 };
        Assert.Equal("http://127.0.0.1:7843/", embedded.BaseAddress.AbsoluteUri);
    }

    [Fact]
    public async Task Remote_engine_routes_the_client_to_the_remote_server_with_no_code_change()
    {
        using var remote = new StandInRemoteMemoryd();

        // Exactly the wiring Program.cs uses: BaseAddress comes from config alone.
        var options = new MemorydOptions { Engine = "remote", RemoteUrl = remote.BaseUrl };
        using var http = new HttpClient { BaseAddress = options.BaseAddress };
        var client = new MemorydClient(http);

        IReadOnlyList<MemoryRecord> results = await client.SearchAsync("anything", "u1");

        Assert.Single(results);
        Assert.Equal("from remote", results[0].Memory);
        Assert.Contains("POST /memories/search", remote.Requests);
    }

    /// <summary>A minimal real HTTP server standing in for a user-hosted mem0.</summary>
    private sealed class StandInRemoteMemoryd : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly List<string> _requests = [];
        private readonly Task _loop;

        public StandInRemoteMemoryd()
        {
            BaseUrl = new Uri($"http://127.0.0.1:{FreePort()}/");
            _listener.Prefixes.Add(BaseUrl.AbsoluteUri);
            _listener.Start();
            _loop = Task.Run(ServeAsync);
        }

        public Uri BaseUrl { get; }

        public IReadOnlyList<string> Requests => _requests;

        public void Dispose()
        {
            if (_listener.IsListening)
            {
                _listener.Stop();
            }

            _listener.Close();
        }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (HttpListenerException)
                {
                    break; // listener stopped
                }
                catch (ObjectDisposedException)
                {
                    break; // listener disposed
                }

                string path = context.Request.Url!.AbsolutePath;
                _requests.Add($"{context.Request.HttpMethod} {path}");
                string json = path switch
                {
                    "/health" => """{"status":"ok"}""",
                    "/memories/search" => """{"results":[{"id":"r1","memory":"from remote"}]}""",
                    _ => """{"results":[]}""",
                };

                byte[] buffer = Encoding.UTF8.GetBytes(json);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer);
                context.Response.Close();
            }
        }

        private static int FreePort()
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }
    }
}
