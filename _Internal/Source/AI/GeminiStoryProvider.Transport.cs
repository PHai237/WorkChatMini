using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace WorkChatMini;

public sealed partial class GeminiStoryProvider
{
    private static readonly TimeSpan GeminiConnectTimeout = TimeSpan.FromSeconds(8);
    private static int _transportLogged;

    private static HttpClient CreateHttpClientSafe()
    {
        try
        {
            return CreatePreferredHttpClient();
        }
        catch (Exception ex)
        {
            // A transport optimization must never make the entire Gemini provider unusable.
            // Some managed VPN/proxy stacks expose a socket configuration that rejects one of
            // the SocketsHttpHandler options during construction. Fall back to the platform
            // handler and keep the provider alive; the full exception remains in diagnostics.
            AppLogger.Error("STORY-AI",
                "Gemini IPv4-preferred transport initialization failed; using platform transport", ex);
            return CreatePlatformFallbackHttpClient();
        }
    }

    private static HttpClient CreatePreferredHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = GeminiConnectTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(90),
            MaxConnectionsPerServer = 4,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectCallback = ConnectPreferIpv4Async
        };

        if (Interlocked.Exchange(ref _transportLogged, 1) == 0)
        {
            AppLogger.Info("STORY-AI",
                "Gemini transport configured | mode=IPv4-preferred | connectTimeout=8s | pooledLifetime=5m | pooledIdle=90s | maxConnections=4");
        }

        return CreateClient(handler);
    }

    private static HttpClient CreatePlatformFallbackHttpClient()
    {
        try
        {
            var handler = new SocketsHttpHandler
            {
                ConnectTimeout = GeminiConnectTimeout,
                PooledConnectionLifetime = TimeSpan.FromMinutes(3),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(60),
                MaxConnectionsPerServer = 4,
                AutomaticDecompression = DecompressionMethods.All
            };

            AppLogger.Warn("STORY-AI",
                "Gemini transport configured | mode=platform-fallback | IPv4 preference unavailable for this session");
            return CreateClient(handler);
        }
        catch (Exception ex)
        {
            // Last-resort construction path: keep AI functional even if the managed VPN/proxy
            // rejects SocketsHttpHandler tuning on this machine. HttpClient's platform defaults
            // still respect the user's networking policy and the request-level timeouts.
            AppLogger.Error("STORY-AI",
                "Gemini platform transport initialization failed; using default HttpClient", ex);
            return new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }
    }

    private static HttpClient CreateClient(SocketsHttpHandler handler) =>
        new(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

    private static async ValueTask<Stream> ConnectPreferIpv4Async(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        IPAddress[] resolved;
        try
        {
            resolved = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            throw new HttpRequestException($"DNS lookup failed for {context.DnsEndPoint.Host}.", ex);
        }

        IPAddress[] ipv4 = resolved.Where(address => address.AddressFamily == AddressFamily.InterNetwork).ToArray();
        IPAddress[] candidates = ipv4.Length > 0 ? ipv4 : resolved;
        if (candidates.Length == 0)
            throw new HttpRequestException($"DNS returned no addresses for {context.DnsEndPoint.Host}.");

        if (ipv4.Length == 0)
        {
            AppLogger.Warn("STORY-AI",
                $"Gemini transport found no IPv4 address; falling back to available address families | host={context.DnsEndPoint.Host}");
        }

        Exception? lastError = null;
        foreach (IPAddress address in candidates.Take(6))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };

            try
            {
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCts.CancelAfter(TimeSpan.FromSeconds(3));
                await socket.ConnectAsync(
                    new IPEndPoint(address, context.DnsEndPoint.Port),
                    attemptCts.Token).ConfigureAwait(false);

                AppLogger.Info("STORY-AI",
                    $"Gemini transport connected | family={address.AddressFamily} | elapsedMs={stopwatch.ElapsedMilliseconds}");
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                socket.Dispose();
                lastError = new TimeoutException("Gemini TCP connection attempt timed out.");
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                lastError = ex;
            }
        }

        throw new HttpRequestException(
            $"Could not establish a connection to {context.DnsEndPoint.Host}:{context.DnsEndPoint.Port} using the preferred address family.",
            lastError);
    }
}
