using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using GetMan.Models;

namespace GetMan.Services;

public enum StreamDirection
{
    /// <summary>Sent by GetMan.</summary>
    Out,
    /// <summary>Received from the server.</summary>
    In,
    /// <summary>Connection state and errors, so the log reads as one story.</summary>
    System
}

/// <summary>One line in the message log.</summary>
public class StreamMessage
{
    public DateTime Time { get; set; } = DateTime.Now;
    public StreamDirection Direction { get; set; }
    public string Text { get; set; } = string.Empty;
    public int SizeBytes { get; set; }
    public bool IsBinary { get; set; }
    public bool IsError { get; set; }

    /// <summary>Server-sent events only.</summary>
    public string EventName { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
}

/// <summary>
/// A long-lived connection: a WebSocket or a server-sent event stream. Both keep reading in the
/// background and raise <see cref="Message"/> for every frame, so the view only has to append.
/// </summary>
public abstract class StreamSession : IDisposable
{
    private CancellationTokenSource _cts;

    public event Action<StreamMessage> Message;
    public event Action<bool> ConnectedChanged;

    public bool IsConnected { get; private set; }

    protected CancellationToken Token => _cts?.Token ?? CancellationToken.None;

    public static StreamSession For(RequestProtocol protocol) => protocol switch
    {
        RequestProtocol.WebSocket => new WebSocketStreamSession(),
        RequestProtocol.Sse => new SseStreamSession(),
        _ => null
    };

    public async Task ConnectAsync(PreparedRequest request)
    {
        if (IsConnected) return;

        _cts = new CancellationTokenSource();
        try
        {
            await OpenAsync(request, _cts.Token).ConfigureAwait(false);
            SetConnected(true);
            Emit(new StreamMessage
            {
                Direction = StreamDirection.System,
                Text = Loc.T("s.stream_connected", request.Url)
            });

            // Not awaited: reading runs until the far end or the user stops it.
            _ = Task.Run(() => ReadLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            SetConnected(false);
            Fail(ex);
        }
    }

    public virtual Task SendAsync(string text) => Task.CompletedTask;

    /// <summary>False for a stream that only ever receives, so the view can hide its composer.</summary>
    public virtual bool CanSend => false;

    public async Task DisconnectAsync()
    {
        if (!IsConnected) return;
        try { _cts?.Cancel(); } catch { }
        try { await CloseAsync().ConfigureAwait(false); } catch { }

        SetConnected(false);
        Emit(new StreamMessage { Direction = StreamDirection.System, Text = Loc.T("s.stream_disconnected") });
    }

    protected abstract Task OpenAsync(PreparedRequest request, CancellationToken ct);
    protected abstract Task ReadAsync(CancellationToken ct);
    protected abstract Task CloseAsync();

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            await ReadAsync(ct).ConfigureAwait(false);

            // The far end hung up rather than the user disconnecting.
            if (!ct.IsCancellationRequested && IsConnected)
            {
                SetConnected(false);
                Emit(new StreamMessage
                {
                    Direction = StreamDirection.System,
                    Text = Loc.T("s.stream_closed_by_server")
                });
            }
        }
        catch (OperationCanceledException)
        {
            // The user disconnected; DisconnectAsync has already said so.
        }
        catch (Exception ex)
        {
            SetConnected(false);
            Fail(ex);
        }
    }

    protected void Emit(StreamMessage message) => Message?.Invoke(message);

    protected void Fail(Exception ex) => Emit(new StreamMessage
    {
        Direction = StreamDirection.System,
        IsError = true,
        Text = ex.Message
    });

    private void SetConnected(bool value)
    {
        if (IsConnected == value) return;
        IsConnected = value;
        ConnectedChanged?.Invoke(value);
    }

    public virtual void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
        _cts = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Headers a handshake sets for itself. Passing one of these through would make the socket
    /// library throw rather than politely ignore it.
    /// </summary>
    protected static bool IsReservedHandshakeHeader(string name) =>
        name.StartsWith("Sec-WebSocket-", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "Connection", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "Upgrade", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A WebSocket connection, text or binary, in both directions.</summary>
public sealed class WebSocketStreamSession : StreamSession
{
    private ClientWebSocket _socket;

    public override bool CanSend => true;

    /// <summary>
    /// ws:// and wss:// are the schemes on the wire, but people paste the http:// URL they were
    /// given, so both are accepted.
    /// </summary>
    public static Uri ToWebSocketUri(string url)
    {
        var text = (url ?? string.Empty).Trim();
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            text = "ws://" + text[7..];
        else if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            text = "wss://" + text[8..];
        else if (!text.Contains("://", StringComparison.Ordinal))
            text = "wss://" + text;

        return new Uri(text, UriKind.Absolute);
    }

    protected override async Task OpenAsync(PreparedRequest request, CancellationToken ct)
    {
        _socket = new ClientWebSocket();

        foreach (var header in request.Headers)
        {
            if (string.IsNullOrWhiteSpace(header.Key) || IsReservedHandshakeHeader(header.Key)) continue;
            try { _socket.Options.SetRequestHeader(header.Key, header.Value); } catch { }
        }

        foreach (var protocol in (request.Settings.WsSubprotocols ?? string.Empty)
                 .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _socket.Options.AddSubProtocol(protocol);

        if (!request.Settings.VerifySsl)
            _socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        await _socket.ConnectAsync(ToWebSocketUri(request.Url), ct).ConfigureAwait(false);
    }

    public override async Task SendAsync(string text)
    {
        if (_socket is not { State: WebSocketState.Open }) return;

        var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, Token).ConfigureAwait(false);

        Emit(new StreamMessage
        {
            Direction = StreamDirection.Out,
            Text = text ?? string.Empty,
            SizeBytes = bytes.Length
        });
    }

    protected override async Task ReadAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var frame = new MemoryStream();

        while (!ct.IsCancellationRequested && _socket is { State: WebSocketState.Open })
        {
            var result = await _socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                Emit(new StreamMessage
                {
                    Direction = StreamDirection.System,
                    Text = Loc.T("s.stream_close_frame", (int)(result.CloseStatus ?? WebSocketCloseStatus.Empty),
                        result.CloseStatusDescription ?? string.Empty)
                });
                return;
            }

            // A message can arrive across several frames, so nothing is emitted until it ends.
            frame.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;

            var bytes = frame.ToArray();
            frame.SetLength(0);

            Emit(new StreamMessage
            {
                Direction = StreamDirection.In,
                IsBinary = result.MessageType == WebSocketMessageType.Binary,
                SizeBytes = bytes.Length,
                Text = result.MessageType == WebSocketMessageType.Binary
                    ? Loc.T("s.stream_binary_frame", bytes.Length)
                    : Encoding.UTF8.GetString(bytes)
            });
        }
    }

    protected override async Task CloseAsync()
    {
        if (_socket == null) return;
        if (_socket.State == WebSocketState.Open)
        {
            // A short timeout of its own: a server that never answers the close handshake must not
            // hold the button.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed by GetMan", timeout.Token)
                    .ConfigureAwait(false);
            }
            catch { }
        }
        _socket.Dispose();
        _socket = null;
    }

    public override void Dispose()
    {
        _socket?.Dispose();
        _socket = null;
        base.Dispose();
    }
}

/// <summary>A server-sent event stream: one long GET whose body never ends.</summary>
public sealed class SseStreamSession : StreamSession
{
    private HttpClient _client;
    private Stream _stream;
    private readonly SseParser _parser = new();

    protected override async Task OpenAsync(PreparedRequest request, CancellationToken ct)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = request.Settings.FollowRedirects,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };

        if (!request.Settings.VerifySsl)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        // No client timeout: the whole point of the stream is that it stays open.
        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

        var message = new HttpRequestMessage(HttpMethod.Get, request.Url);
        foreach (var header in request.Headers)
        {
            if (string.IsNullOrWhiteSpace(header.Key)) continue;
            message.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        if (!request.HasHeader("Accept")) message.Headers.TryAddWithoutValidation("Accept", "text/event-stream");

        // ResponseHeadersRead, or reading the body would wait for an end that never comes.
        var response = await _client
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            Emit(new StreamMessage
            {
                Direction = StreamDirection.System,
                Text = Loc.T("s.stream_unexpected_content_type", contentType ?? "-")
            });

        _stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
    }

    protected override async Task ReadAsync(CancellationToken ct)
    {
        using var reader = new StreamReader(_stream, Encoding.UTF8);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line == null) return;   // the server closed the stream

            var message = _parser.Feed(line);
            if (message != null) Emit(message);
        }
    }

    protected override Task CloseAsync()
    {
        _stream?.Dispose();
        _stream = null;
        _client?.Dispose();
        _client = null;
        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _stream?.Dispose();
        _client?.Dispose();
        base.Dispose();
    }
}
