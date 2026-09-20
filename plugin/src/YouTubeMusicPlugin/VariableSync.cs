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
        lock (_gate)
        {
            if (!force && _lastConnected == connected && _lastPlaying == playing)
            {
                return;
            }

            _lastConnected = connected;
            _lastPlaying = playing;
        }

        Write(ConnectedVariable, connected);
        Write(PlayingVariable, playing);

        MacroDeckLogger.Verbose(_plugin, "State: connected={0} playing={1}", connected, playing);
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
