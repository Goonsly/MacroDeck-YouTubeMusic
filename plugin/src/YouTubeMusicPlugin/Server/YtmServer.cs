using System.Collections.Concurrent;
using Fleck;
using FleckLogLevel = Fleck.LogLevel;
using ThreadingTimer = System.Threading.Timer;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;

namespace KeystoneDigital.YouTubeMusic.Server;

/// <summary>
/// Loopback WebSocket server the Chrome extension connects to.
///
/// Bound to 127.0.0.1 only. Two checks run before any state is accepted: the
/// handshake Origin must be a permitted chrome-extension origin, and the first
/// frame must present the configured token. WebSocket connections are not
/// subject to CORS, so without these any web page could reach this port.
/// </summary>
internal sealed class YtmServer : IDisposable
{
    private const string ProtocolVersion = "1.0.0";
    private const string ChromeExtensionScheme = "chrome-extension://";
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(20);
    private const int ClosePolicyViolation = 1008;

    private readonly MacroDeckPlugin _plugin;
    private readonly object _gate = new();

    /// <summary>Connections that have not presented a valid token yet.</summary>
    private readonly ConcurrentDictionary<IWebSocketConnection, ThreadingTimer> _pending = new();

    private WebSocketServer? _server;
    private IWebSocketConnection? _client;
    private System.Timers.Timer? _pingTimer;

    private string _token = string.Empty;
    private IReadOnlyList<string> _allowedExtensionIds = Array.Empty<string>();

    public YtmServer(MacroDeckPlugin plugin)
    {
        _plugin = plugin;
    }

    /// <summary>Raised with (connected, playing) whenever the browser reports state.</summary>
    public event Action<bool, bool>? StateReceived;

    /// <summary>Raised when the authenticated client goes away.</summary>
    public event Action? ClientLost;

    public bool IsClientConnected
    {
        get
        {
            lock (_gate)
            {
                return _client is { IsAvailable: true };
            }
        }
    }

    public int Port { get; private set; }

    public void Start(int port, string token, IReadOnlyList<string> allowedExtensionIds)
    {
        Stop();

        Port = port;
        _token = token;
        _allowedExtensionIds = allowedExtensionIds;

        FleckLog.LogAction = (level, message, exception) =>
        {
            switch (level)
            {
                case FleckLogLevel.Error:
                    MacroDeckLogger.Error(_plugin, "WebSocket: {0} {1}", message, exception?.Message ?? string.Empty);
                    break;
                case FleckLogLevel.Warn:
                    MacroDeckLogger.Warning(_plugin, "WebSocket: {0}", message);
                    break;
                default:
                    MacroDeckLogger.Verbose(_plugin, "WebSocket: {0}", message);
                    break;
            }
        };

        try
        {
            // 127.0.0.1 only. Never 0.0.0.0 — that would expose playback control
            // to the whole LAN.
            var server = new WebSocketServer($"ws://127.0.0.1:{port}")
            {
                RestartAfterListenError = true,
            };

            server.Start(OnConnection);
            _server = server;

            _pingTimer = new System.Timers.Timer(PingInterval.TotalMilliseconds) { AutoReset = true };
            _pingTimer.Elapsed += (_, _) => SendToClient(Json.Ping());
            _pingTimer.Start();

            MacroDeckLogger.Information(_plugin, "Listening on ws://127.0.0.1:{0}", port);
        }
        catch (Exception exception)
        {
            MacroDeckLogger.Error(_plugin, "Could not listen on port {0}: {1}", port, exception.Message);
            throw;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _pingTimer?.Stop();
            _pingTimer?.Dispose();
            _pingTimer = null;

            foreach (var timer in _pending.Values)
            {
                timer.Dispose();
            }

            _pending.Clear();

            CloseQuietly(_client);
            _client = null;

            _server?.Dispose();
            _server = null;
        }
    }

    public void SendCommand(string command)
    {
        if (!IsClientConnected)
        {
            MacroDeckLogger.Warning(_plugin, "Command '{0}' dropped: no browser connected.", command);
            return;
        }

        MacroDeckLogger.Verbose(_plugin, "Sending command '{0}'.", command);
        SendToClient(Json.Command(command));
    }

    private void OnConnection(IWebSocketConnection socket)
    {
        socket.OnOpen = () => OnOpen(socket);
        socket.OnClose = () => OnClose(socket);
        socket.OnMessage = message => OnMessage(socket, message);
        socket.OnError = exception =>
            MacroDeckLogger.Verbose(_plugin, "Connection error: {0}", exception.Message);
    }

    private void OnOpen(IWebSocketConnection socket)
    {
        var origin = socket.ConnectionInfo.Origin ?? string.Empty;

        if (!IsOriginAllowed(origin))
        {
            MacroDeckLogger.Warning(_plugin, "Rejected a connection from origin '{0}'.", origin);
            Reject(socket, ErrorReasons.BadOrigin);
            return;
        }

        // A client that never authenticates must not hold the connection open.
        var timer = new ThreadingTimer(_ => OnHandshakeTimeout(socket), null, HandshakeTimeout, Timeout.InfiniteTimeSpan);
        _pending[socket] = timer;
    }

    private void OnHandshakeTimeout(IWebSocketConnection socket)
    {
        if (!_pending.ContainsKey(socket))
        {
            return;
        }

        MacroDeckLogger.Warning(_plugin, "Connection closed: no token within {0} seconds.", HandshakeTimeout.TotalSeconds);
        Reject(socket, ErrorReasons.Timeout);
    }

    private void OnMessage(IWebSocketConnection socket, string text)
    {
        IncomingMessage? message;

        try
        {
            message = Json.Parse(text);
        }
        catch (Exception exception)
        {
            MacroDeckLogger.Warning(_plugin, "Malformed frame: {0}", exception.Message);
            Reject(socket, ErrorReasons.Malformed);
            return;
        }

        if (message?.Type is null)
        {
            return;
        }

        switch (message.Type)
        {
            case MessageTypes.Hello:
                HandleHello(socket, message);
                break;

            case MessageTypes.State:
                HandleState(socket, message);
                break;

            case MessageTypes.Ping:
                if (IsAuthenticated(socket))
                {
                    socket.Send(Json.Pong());
                }

                break;

            case MessageTypes.Pong:
                break;

            default:
                // Forward compatibility: ignore unknown types rather than failing.
                break;
        }
    }

    private void HandleHello(IWebSocketConnection socket, IncomingMessage message)
    {
        if (!_pending.TryRemove(socket, out var timer))
        {
            // Either already authenticated, or never passed the origin check.
            return;
        }

        timer.Dispose();

        if (string.IsNullOrEmpty(_token) ||
            !string.Equals(message.Token, _token, StringComparison.Ordinal))
        {
            MacroDeckLogger.Warning(_plugin, "Rejected a connection: wrong token.", Array.Empty<object>());
            Reject(socket, ErrorReasons.BadToken);
            return;
        }

        IWebSocketConnection? replaced;

        lock (_gate)
        {
            replaced = _client;
            _client = socket;
        }

        if (replaced is not null && !ReferenceEquals(replaced, socket))
        {
            // Last connection wins. A stale socket from a reloaded extension
            // must not keep receiving commands.
            MacroDeckLogger.Information(_plugin, "Replacing an earlier browser connection.");
            CloseQuietly(replaced);
        }

        MacroDeckLogger.Information(
            _plugin,
            "Browser connected (protocol {0}, extension {1}).",
            message.Version ?? "unknown",
            message.Extension ?? "unknown");

        socket.Send(Json.Welcome(ProtocolVersion));
    }

    private void HandleState(IWebSocketConnection socket, IncomingMessage message)
    {
        if (!IsAuthenticated(socket))
        {
            MacroDeckLogger.Warning(_plugin, "Ignored state from an unauthenticated connection.", Array.Empty<object>());
            return;
        }

        var connected = message.Connected ?? false;

        // A disconnected browser cannot be playing. Guard here so a malformed
        // frame can never leave a stale 'playing' on a Macro Deck button.
        var playing = connected && (message.Playing ?? false);

        StateReceived?.Invoke(connected, playing);
    }

    private void OnClose(IWebSocketConnection socket)
    {
        if (_pending.TryRemove(socket, out var timer))
        {
            timer.Dispose();
        }

        var wasClient = false;

        lock (_gate)
        {
            if (ReferenceEquals(_client, socket))
            {
                _client = null;
                wasClient = true;
            }
        }

        if (wasClient)
        {
            MacroDeckLogger.Information(_plugin, "Browser disconnected.");
            ClientLost?.Invoke();
        }
    }

    private bool IsAuthenticated(IWebSocketConnection socket)
    {
        lock (_gate)
        {
            return ReferenceEquals(_client, socket);
        }
    }

    private bool IsOriginAllowed(string origin)
    {
        if (!origin.StartsWith(ChromeExtensionScheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // An empty allow-list still excludes every website; it only means we do
        // not yet know which extension ID to expect. See docs/NOTES.md.
        if (_allowedExtensionIds.Count == 0)
        {
            return true;
        }

        var id = origin[ChromeExtensionScheme.Length..].TrimEnd('/');
        return _allowedExtensionIds.Any(allowed => string.Equals(allowed, id, StringComparison.OrdinalIgnoreCase));
    }

    private void Reject(IWebSocketConnection socket, string reason)
    {
        try
        {
            if (socket.IsAvailable)
            {
                socket.Send(Json.Error(reason, fatal: true));
            }
        }
        catch
        {
            // The socket may already be gone; closing below is what matters.
        }

        if (_pending.TryRemove(socket, out var timer))
        {
            timer.Dispose();
        }

        try
        {
            socket.Close(ClosePolicyViolation);
        }
        catch
        {
            // Already closed.
        }
    }

    private void SendToClient(string payload)
    {
        IWebSocketConnection? socket;

        lock (_gate)
        {
            socket = _client;
        }

        if (socket is not { IsAvailable: true })
        {
            return;
        }

        try
        {
            socket.Send(payload);
        }
        catch (Exception exception)
        {
            MacroDeckLogger.Warning(_plugin, "Send failed: {0}", exception.Message);
        }
    }

    private static void CloseQuietly(IWebSocketConnection? socket)
    {
        try
        {
            socket?.Close();
        }
        catch
        {
            // Nothing useful to do if a dying socket refuses to close.
        }
    }

    public void Dispose() => Stop();
}
