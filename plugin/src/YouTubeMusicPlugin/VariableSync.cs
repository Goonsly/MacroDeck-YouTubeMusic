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
internal sealed class VariableSync
{
    public const string ConnectedVariable = "youtube_music_connected";
    public const string PlayingVariable = "youtube_music_playing";

    private readonly MacroDeckPlugin _plugin;
    private readonly object _gate = new();

    private bool? _lastConnected;
    private bool? _lastPlaying;

    public VariableSync(MacroDeckPlugin plugin)
    {
        _plugin = plugin;
    }

    /// <summary>
    /// Logs what already exists so that variable ownership behaviour is visible
    /// in the Macro Deck log, then writes a known-good starting state.
    /// </summary>
    public void Initialize()
    {
        foreach (var name in new[] { ConnectedVariable, PlayingVariable })
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
        Set(connected: false, playing: false, force: true);
    }

    public void Set(bool connected, bool playing) => Set(connected, playing, force: false);

    /// <summary>Called when the browser disappears for any reason.</summary>
    public void Clear() => Set(connected: false, playing: false, force: true);

    private void Set(bool connected, bool playing, bool force)
    {
        bool connectedChanged;
        bool playingChanged;

        lock (_gate)
        {
            connectedChanged = force || _lastConnected != connected;
            playingChanged = force || _lastPlaying != playing;

            if (!connectedChanged && !playingChanged)
            {
                return;
            }

            _lastConnected = connected;
            _lastPlaying = playing;
        }

        // Only write what actually moved. Every write makes Macro Deck repaint
        // the buttons bound to that variable, and a repaint we do not need is a
        // repaint that can go wrong.
        OnUiThread(() =>
        {
            if (connectedChanged)
            {
                Write(ConnectedVariable, connected);
            }

            if (playingChanged)
            {
                Write(PlayingVariable, playing);
            }
        });

        MacroDeckLogger.Verbose(_plugin, "State: connected={0} playing={1}", connected, playing);
    }

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
        var window = SuchByte.MacroDeck.MacroDeck.MainWindow;

        if (window is null || window.IsDisposed || !window.IsHandleCreated)
        {
            // Too early, or Macro Deck is running without its window. Nothing is
            // painting, so writing here is safe.
            write();
            return;
        }

        try
        {
            if (window.InvokeRequired)
            {
                window.BeginInvoke(write);
            }
            else
            {
                write();
            }
        }
        catch (Exception exception)
        {
            // The window can be torn down between the checks above and the call.
            MacroDeckLogger.Warning(_plugin, "Falling back to a direct variable write: {0}", exception.Message);
            write();
        }
    }

    private void Write(string name, bool value)
    {
        try
        {
            VariableManager.SetValue(name, value, VariableType.Bool, _plugin, Array.Empty<string>());
        }
        catch (Exception exception)
        {
            MacroDeckLogger.Error(_plugin, "Could not set variable '{0}': {1}", name, exception.Message);
        }
    }
}
