using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FFmpegVideoPlayer.Core;

/// <summary>
/// Exposes a small set of media resources on a random loopback-only HTTP endpoint.
/// FFmpeg can then use ordinary byte-range requests while the backing streams remain
/// remote and are never written to disk.
/// </summary>
public sealed class LoopbackMediaSession : IAsyncDisposable, IDisposable
{
    private const int DefaultMaximumHeaderBytes = 32 * 1024;
    private const int DefaultMaximumConnections = 16;
    private static readonly TimeSpan DefaultHeaderTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultUpstreamOpenTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultReadIdleTimeout = TimeSpan.FromSeconds(30);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TcpListener _listener;
    private readonly IReadOnlyDictionary<string, MediaResource> _resources;
    private readonly string _routePrefix;
    private readonly TimeSpan _headerTimeout;
    private readonly TimeSpan _upstreamOpenTimeout;
    private readonly TimeSpan _readIdleTimeout;
    private readonly int _maximumHeaderBytes;
    private readonly SemaphoreSlim _connectionLimit;
    private readonly ConcurrentDictionary<int, Task> _requestTasks = new();
    private readonly Task _acceptTask;
    private int _nextRequestId;
    private int _disposed;

    public LoopbackMediaSession(
        string primaryFileName,
        IEnumerable<MediaResource> resources,
        TimeSpan? headerTimeout = null,
        TimeSpan? upstreamOpenTimeout = null,
        TimeSpan? readIdleTimeout = null,
        int maximumHeaderBytes = DefaultMaximumHeaderBytes,
        int maximumConnections = DefaultMaximumConnections)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryFileName);
        ArgumentNullException.ThrowIfNull(resources);
        if (maximumHeaderBytes < 1024)
            throw new ArgumentOutOfRangeException(nameof(maximumHeaderBytes));
        if (maximumConnections <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumConnections));

        _headerTimeout = ValidateTimeout(headerTimeout ?? DefaultHeaderTimeout, nameof(headerTimeout));
        _upstreamOpenTimeout = ValidateTimeout(
            upstreamOpenTimeout ?? DefaultUpstreamOpenTimeout,
            nameof(upstreamOpenTimeout));
        _readIdleTimeout = ValidateTimeout(readIdleTimeout ?? DefaultReadIdleTimeout, nameof(readIdleTimeout));
        _maximumHeaderBytes = maximumHeaderBytes;
        _connectionLimit = new SemaphoreSlim(maximumConnections, maximumConnections);

        _resources = resources
            .ToDictionary(resource => resource.FileName, StringComparer.Ordinal);
        if (!_resources.ContainsKey(primaryFileName))
            throw new ArgumentException("The primary file must be present in resources.", nameof(resources));
        _routePrefix = "/" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant() + "/";
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();

        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        FileName = primaryFileName;
        PlaybackUrl =
            $"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}{_routePrefix}{Uri.EscapeDataString(primaryFileName)}";
        _acceptTask = AcceptRequestsAsync(_shutdown.Token);
    }

    /// <summary>Creates a DASH manifest session without writing media to disk.</summary>
    public LoopbackMediaSession(
        string manifestFileName,
        string manifest,
        IEnumerable<MediaResource> mediaResources,
        TimeSpan? headerTimeout = null,
        TimeSpan? upstreamOpenTimeout = null,
        TimeSpan? readIdleTimeout = null)
        : this(
            manifestFileName,
            AppendManifest(manifestFileName, manifest, mediaResources),
            headerTimeout,
            upstreamOpenTimeout,
            readIdleTimeout)
    {
    }

    public string PlaybackUrl { get; }
    public string FileName { get; }

    public void Dispose() => DisposeCoreAsync().GetAwaiter().GetResult();

    public ValueTask DisposeAsync() => new(DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _shutdown.Cancel();
        _listener.Stop();
        await ObserveShutdownAsync(_acceptTask).ConfigureAwait(false);
        await Task.WhenAll(_requestTasks.Values.Select(ObserveShutdownAsync)).ConfigureAwait(false);
        _connectionLimit.Dispose();
        _shutdown.Dispose();
    }

    private async Task AcceptRequestsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _connectionLimit.WaitAsync(cancellationToken).ConfigureAwait(false);
                TcpClient? client = null;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    var requestId = Interlocked.Increment(ref _nextRequestId);
                    var requestTask = HandleTrackedRequestAsync(client, cancellationToken);
                    client = null; // Ownership transferred to the request task.
                    _requestTasks[requestId] = requestTask;
                    _ = requestTask.ContinueWith(
                        completedTask =>
                        {
                            _requestTasks.TryRemove(requestId, out var removedTask);
                            _ = completedTask;
                            _ = removedTask;
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
                catch
                {
                    client?.Dispose();
                    _connectionLimit.Release();
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Trace.TraceError($"[LoopbackMediaSession] Accept failed: {ex}");
        }
    }

    private async Task HandleTrackedRequestAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            await HandleRequestAsync(client, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _connectionLimit.Release();
        }
    }

    private async Task HandleRequestAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            var timeoutPhase = "request headers";
            try
            {
                client.NoDelay = true;
                await using var network = client.GetStream();
                using var reader = new StreamReader(
                    network,
                    Encoding.ASCII,
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 1024,
                    leaveOpen: true);

                string requestLine;
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using (var headerTimeout = CreateTimeout(cancellationToken, _headerTimeout))
                {
                    requestLine = await reader.ReadLineAsync(headerTimeout.Token).ConfigureAwait(false) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(requestLine))
                        return;

                    var headerBytes = requestLine.Length + 2;

                    while (await reader.ReadLineAsync(headerTimeout.Token).ConfigureAwait(false) is { Length: > 0 } line)
                    {
                        headerBytes += line.Length + 2;
                        if (headerBytes > _maximumHeaderBytes)
                        {
                            await WriteStatusAsync(network, 431, "Request Header Fields Too Large", cancellationToken)
                                .ConfigureAwait(false);
                            return;
                        }

                        var separator = line.IndexOf(':');
                        if (separator > 0)
                            headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
                    }
                }

                var requestParts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (requestParts.Length < 2 || requestParts[0] is not ("GET" or "HEAD"))
                {
                    await WriteStatusAsync(network, 405, "Method Not Allowed", cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                var requestTarget = requestParts[1];
                var queryIndex = requestTarget.IndexOf('?');
                var requestPath = Uri.UnescapeDataString(
                    queryIndex >= 0 ? requestTarget[..queryIndex] : requestTarget);

                if (!requestPath.StartsWith(_routePrefix, StringComparison.Ordinal)
                    || !_resources.TryGetValue(requestPath[_routePrefix.Length..], out var resource))
                {
                    await WriteStatusAsync(network, 404, "Not Found", cancellationToken).ConfigureAwait(false);
                    return;
                }

                var range = ParseRange(headers.GetValueOrDefault("Range"), resource.Length);
                if (range is null)
                {
                    await WriteRangeNotSatisfiableAsync(network, resource.Length, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                var (start, end, isPartial) = range.Value;
                if (requestParts[0] == "HEAD")
                {
                    await WriteResourceHeadersAsync(
                            network,
                            resource.ContentType,
                            resource.Length,
                            start,
                            end,
                            isPartial,
                            cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                timeoutPhase = "upstream media open";
                await using var source = await OpenResourceAsync(
                        resource,
                        _upstreamOpenTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!source.CanSeek)
                    throw new InvalidOperationException("Media stream must support seeking.");

                source.Position = start;
                await WriteResourceHeadersAsync(
                        network,
                        resource.ContentType,
                        resource.Length,
                        start,
                        end,
                        isPartial,
                        cancellationToken)
                    .ConfigureAwait(false);

                timeoutPhase = "upstream media read";
                await CopyExactlyAsync(
                        source,
                        network,
                        end - start + 1,
                        _readIdleTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException)
            {
                Trace.TraceWarning($"[LoopbackMediaSession] Request timed out during {timeoutPhase}.");
            }
            catch (IOException)
            {
                // FFmpeg closes probe/range connections as soon as it has enough data.
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[LoopbackMediaSession] Request failed: {ex}");
            }
        }
    }

    private static IEnumerable<MediaResource> AppendManifest(
        string manifestFileName,
        string manifest,
        IEnumerable<MediaResource> mediaResources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest);
        ArgumentNullException.ThrowIfNull(mediaResources);
        var manifestBytes = Encoding.UTF8.GetBytes(manifest);
        return mediaResources.Append(new MediaResource(
            manifestFileName,
            "application/dash+xml",
            manifestBytes.LongLength,
            _ => ValueTask.FromResult<Stream>(new MemoryStream(manifestBytes, writable: false))));
    }

    private static async Task ObserveShutdownAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static TimeSpan ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(parameterName, "Timeout must be greater than zero.");

        return timeout;
    }

    private static CancellationTokenSource CreateTimeout(
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        return timeoutSource;
    }

    private static async ValueTask<Stream> OpenResourceAsync(
        MediaResource resource,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var openTimeout = CreateTimeout(cancellationToken, timeout);
        var openTask = resource.OpenAsync(openTimeout.Token).AsTask();
        try
        {
            return await openTask.WaitAsync(openTimeout.Token).ConfigureAwait(false);
        }
        catch
        {
            _ = DisposeLateStreamAsync(openTask);
            throw;
        }
    }

    private static async Task DisposeLateStreamAsync(Task<Stream> openTask)
    {
        try
        {
            await using var stream = await openTask.ConfigureAwait(false);
        }
        catch
        {
            // The request already ended. Observe any late fault from a non-cooperative
            // resource factory, and dispose a stream if it completes after the timeout.
        }
    }

    private static (long Start, long End, bool IsPartial)? ParseRange(string? value, long length)
    {
        if (length <= 0)
            return null;

        if (string.IsNullOrWhiteSpace(value))
            return (0, length - 1, false);

        if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)
            || value.Contains(',', StringComparison.Ordinal))
            return null;

        var parts = value[6..].Split('-', 2);
        if (parts.Length != 2)
            return null;

        long start;
        long end;
        if (parts[0].Length == 0)
        {
            if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var suffixLength)
                || suffixLength <= 0)
                return null;

            suffixLength = Math.Min(suffixLength, length);
            start = length - suffixLength;
            end = length - 1;
        }
        else
        {
            if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out start)
                || start < 0
                || start >= length)
                return null;

            if (parts[1].Length == 0)
            {
                end = length - 1;
            }
            else if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out end)
                     || end < start)
            {
                return null;
            }

            end = Math.Min(end, length - 1);
        }

        return (start, end, true);
    }

    private static async Task WriteResourceHeadersAsync(
        Stream destination,
        string contentType,
        long totalLength,
        long start,
        long end,
        bool isPartial,
        CancellationToken cancellationToken)
    {
        var status = isPartial ? "206 Partial Content" : "200 OK";
        var contentRange = isPartial
            ? $"Content-Range: bytes {start.ToString(CultureInfo.InvariantCulture)}-{end.ToString(CultureInfo.InvariantCulture)}/{totalLength.ToString(CultureInfo.InvariantCulture)}\r\n"
            : string.Empty;
        var header =
            $"HTTP/1.1 {status}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {(end - start + 1).ToString(CultureInfo.InvariantCulture)}\r\n" +
            contentRange +
            "Accept-Ranges: bytes\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n\r\n";

        await destination.WriteAsync(Encoding.ASCII.GetBytes(header), cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteRangeNotSatisfiableAsync(
        Stream destination,
        long length,
        CancellationToken cancellationToken)
    {
        var header =
            "HTTP/1.1 416 Range Not Satisfiable\r\n" +
            $"Content-Range: bytes */{length.ToString(CultureInfo.InvariantCulture)}\r\n" +
            "Content-Length: 0\r\n" +
            "Connection: close\r\n\r\n";
        await destination.WriteAsync(Encoding.ASCII.GetBytes(header), cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteStatusAsync(
        Stream destination,
        int statusCode,
        string reason,
        CancellationToken cancellationToken)
    {
        var header =
            $"HTTP/1.1 {statusCode.ToString(CultureInfo.InvariantCulture)} {reason}\r\n" +
            "Content-Length: 0\r\n" +
            "Connection: close\r\n\r\n";
        await destination.WriteAsync(Encoding.ASCII.GetBytes(header), cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyExactlyAsync(
        Stream source,
        Stream destination,
        long bytesToCopy,
        TimeSpan readIdleTimeout,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        while (bytesToCopy > 0)
        {
            var requested = (int)Math.Min(buffer.Length, bytesToCopy);
            int read;
            using (var readTimeout = CreateTimeout(cancellationToken, readIdleTimeout))
            {
                var readTask = source.ReadAsync(buffer.AsMemory(0, requested), readTimeout.Token).AsTask();
                try
                {
                    read = await readTask.WaitAsync(readTimeout.Token).ConfigureAwait(false);
                }
                catch
                {
                    _ = ObserveLateReadAsync(readTask);
                    throw;
                }
            }

            if (read == 0)
                throw new EndOfStreamException("Media stream ended before the requested range was complete.");

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            bytesToCopy -= read;
        }
    }

    private static async Task ObserveLateReadAsync(Task<int> readTask)
    {
        try
        {
            await readTask.ConfigureAwait(false);
        }
        catch
        {
            // The connection has already closed; only observe a late read failure.
        }
    }
}
