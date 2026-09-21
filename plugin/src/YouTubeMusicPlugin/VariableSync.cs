using KeystoneDigital.YouTubeMusic.Server;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Variables;

namespace KeystoneDigital.YouTubeMusic;

/// <summary>
/// Writes observed browser state into Macro Deck variables.
///
/// The browser is the only source of truth. Nothing here is ever called because
/// a command was sent — only because the browser reported what actually
/// happened.
/// </summary>
internal sealed class VariableSync : IDisposable
{
    public const string ConnectedVariable = "youtube_music_connected";
    public const string PlayingVariable = "youtube_music_playing";
    public const string VolumeVariable = "youtube_music_volume";
    public const string MutedVariable = "youtube_music_muted";
    public const string ShuffleVariable = "youtube_music_shuffle";
    public const string RepeatVariable = "youtube_music_repeat";

    /// <summary>Every variable this plugin owns, for logging on startup.</summary>
    public static readonly string[] AllVariables =
    {
        ConnectedVariable, PlayingVariable, VolumeVariable,
        MutedVariable, ShuffleVariable, RepeatVariable,
    };

    /// <summary>
    /// How long to wait before writing a change, so a burst collapses into one
    /// write. A track change fires pause then play within milliseconds; without
    /// this, Macro Deck swaps the button icon twice in quick succession.
    /// </summary>
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(200);

    private readonly MacroDeckPlugin _plugin;
    private readonly object _gate = new();
    private readonly TimeSpan _debounce;
    private readonly Action<string, object> _write;
    private readonly System.Threading.Timer _timer;

    private PlayerState _pending = PlayerState.Disconnected;
    private bool _flushScheduled;
    private bool _forceNextFlush;

    private static bool _noMacroDeckHost;

    /// <summary>What was last written, per variable name.</summary>
    private readonly Dictionary<string, object> _lastWritten = new(StringComparer.Ordinal);

    public VariableSync(MacroDeckPlugin plugin)
        : this(plugin, DefaultDebounce, null)
    {
    }

    /// <param name="write">
    /// Injected by the tests so that debouncing can be exercised without Macro
    /// Deck. Production passes null and writes through <see cref="VariableManager"/>.
    /// </param>
    internal VariableSync(MacroDeckPlugin plugin, TimeSpan debounce, Action<string, object>? write)
    {
        _plugin = plugin;
        _debounce = debounce;
        _write = write ?? Write;
        _timer = new System.Threading.Timer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Logs what already exists so that variable ownership behaviour is visible
    /// in the Macro Deck log, then writes a known-good starting state.
    /// </summary>
    public void Initialize()
    {
        foreach (var name in AllVariables)
        {
            var existing = VariableManager.Variables
                .FirstOrDefault(variable => string.Equals(variable.Name, name, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                MacroDeckLogger.Information(_plugin, "Variable '{0}' does not exist yet; it will be created.", name);
                continue;
            }

            MacroDeckLogger.Information(
                _plugin,
                "Variable '{0}' already exists (creator '{1}', type '{2}'); it will be reused, not duplicated.",
                name,
                existing.Creator ?? "unknown",
                existing.Type ?? "unknown");
        }

        // Until a browser reports in, nothing is connected and nothing is playing.
        Set(PlayerState.Disconnected, force: true);
    }

    public void Set(PlayerState state) => Set(state, force: false);

    /// <summary>Called when the browser disappears for any reason.</summary>
    public void Clear() => Set(PlayerState.Disconnected, force: true);

    private void Set(PlayerState state, bool force)
    {
        lock (_gate)
        {
            _pending = state;
            _forceNextFlush |= force;

            if (!force)
            {
                // Trailing debounce: the first change schedules a flush, later
                // changes inside the window only update what will be written. A
                // flush is therefore guaranteed within one debounce interval,
                // however hard the state flaps.
                if (_flushScheduled)
                {
                    return;
                }

                _flushScheduled = true;
                _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
                return;
            }

            // Connect, disconnect and startup are not worth delaying.
            _flushScheduled = false;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        Flush();
    }

    private void Flush()
    {
        PlayerState state;
        bool force;

        lock (_gate)
        {
            _flushScheduled = false;
            state = _pending;
            force = _forceNextFlush;
            _forceNextFlush = false;
        }

        var writes = new List<(string Name, object Value)>();

        Stage(writes, ConnectedVariable, state.Connected, force);
        Stage(writes, PlayingVariable, state.Playing, force);
        Stage(writes, MutedVariable, state.Muted, force);

        // A null means the browser could not read that value. Leave the previous
        // value in place rather than writing a lie: a stale volume reading is
        // less misleading than a sudden 0.
        if (state.Volume is { } volume) Stage(writes, VolumeVariable, volume, force);
        if (state.Shuffle is { } shuffle) Stage(writes, ShuffleVariable, shuffle, force);
        if (state.Repeat is { } repeat) Stage(writes, RepeatVariable, repeat, force);

        if (writes.Count == 0)
        {
            return;
        }

        OnUiThread(() =>
        {
            foreach (var (name, value) in writes)
            {
                _write(name, value);
            }
        });

        MacroDeckLogger.Verbose(
            _plugin,
            "State: connected={0} playing={1} volume={2} muted={3} shuffle={4} repeat={5}",
            state.Connected,
            state.Playing,
            state.Volume?.ToString() ?? "unknown",
            state.Muted,
            state.Shuffle?.ToString() ?? "unknown",
            state.Repeat ?? "unknown");
    }

    /// <summary>
    /// Queues a write only when the value actually moved. Every write makes
    /// Macro Deck repaint the buttons bound to that variable, and a repaint we
    /// do not need is a repaint that can go wrong.
    /// </summary>
    private void Stage(List<(string Name, object Value)> writes, string name, object value, bool force)
    {
        lock (_gate)
        {
            if (!force && _lastWritten.TryGetValue(name, out var previous) && previous.Equals(value))
            {
                return;
            }

            _lastWritten[name] = value;
        }

        writes.Add((name, value));
    }

    public void Dispose() => _timer.Dispose();

    /// <summary>
    /// Runs the write on Macro Deck's UI thread.
    ///
    /// Setting a variable makes Macro Deck repaint every button bound to it. Doing
    /// that from a socket thread races the painter, and GDI+ answers with
    /// "Parameter is not valid" out of RoundedButton.OnPaint — the button then
    /// renders as a broken-image tile until it is redrawn.
    /// </summary>
    private void OnUiThread(Action write)
    {
        if (_noMacroDeckHost)
        {
            write();
            return;
        }

        try
        {
            var window = SuchByte.MacroDeck.MacroDeck.MainWindow;

            if (window is null || window.IsDisposed || !window.IsHandleCreated)
            {
                // Too early, or Macro Deck is running without its window.
                // Nothing is painting, so writing here is safe.
                write();
                return;
            }

            if (window.InvokeRequired)
            {
                window.BeginInvoke(write);
            }
            else
            {
                write();
            }
        }
        catch (TypeInitializationException)
        {
            // Running outside Macro Deck, as the protocol tests do. There is no
            // UI thread to marshal onto, and no painter to race.
            _noMacroDeckHost = true;
            write();
        }
        catch (Exception exception)
        {
            // The window can be torn down between the checks above and the call.
            MacroDeckLogger.Warning(_plugin, "Falling back to a direct variable write: {0}", exception.Message);
            write();
        }
    }

    private void Write(string name, object value)
    {
        var type = value switch
        {
            bool => VariableType.Bool,
            int => VariableType.Integer,
            _ => VariableType.String,
        };

        try
        {
            VariableManager.SetValue(name, value, type, _plugin, Array.Empty<string>());
        }
        catch (Exception exception)
        {
            MacroDeckLogger.Error(_plugin, "Could not set variable '{0}': {1}", name, exception.Message);
        }
    }
}
