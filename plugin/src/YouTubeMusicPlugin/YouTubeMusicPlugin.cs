using KeystoneDigital.YouTubeMusic.Actions;
using KeystoneDigital.YouTubeMusic.GUI;
using KeystoneDigital.YouTubeMusic.Server;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;

namespace KeystoneDigital.YouTubeMusic;

/// <summary>
/// Macro Deck plugin that controls YouTube Music in Chrome through a companion
/// browser extension, and mirrors the browser's real playback state into Macro
/// Deck variables.
/// </summary>
public class YouTubeMusicPlugin : MacroDeckPlugin
{
    private VariableSync? _variables;
    private PluginSettings? _settings;
    private YtmServer? _server;

    public static YouTubeMusicPlugin? Instance { get; private set; }

    internal YtmServer Server => _server ?? throw new InvalidOperationException("Plugin is not enabled yet.");

    internal PluginSettings Settings => _settings ?? throw new InvalidOperationException("Plugin is not enabled yet.");

    public override bool CanConfigure => true;

    public override void Enable()
    {
        Instance = this;

        Actions = new List<PluginAction>
        {
            new PlayPauseAction(),
            new PlayAction(),
            new PauseAction(),
            new NextAction(),
            new PreviousAction(),
            new ShuffleAction(),
            new RepeatAction(),
            new RepeatOffAction(),
            new RepeatAllAction(),
            new RepeatOneAction(),
            new VolumeUpAction(),
            new VolumeDownAction(),
            new MuteAction(),
        };

        _settings = new PluginSettings(this);
        _variables = new VariableSync(this);
        _variables.Initialize();

        _server = new YtmServer(this);
        _server.StateReceived += OnStateReceived;
        _server.ClientLost += OnClientLost;

        StartServer();
    }

    /// <summary>Restarts the listener, picking up a changed port.</summary>
    internal void RestartServer()
    {
        StartServer();
    }

    private void StartServer()
    {
        if (_server is null || _settings is null)
        {
            return;
        }

        try
        {
            _server.Start(_settings.Port, _settings.Token, _settings.AllowedExtensionIds);
        }
        catch (Exception exception)
        {
            MacroDeckLogger.Error(
                this,
                "YouTube Music plugin could not start its listener on port {0}: {1}",
                _settings.Port,
                exception.Message);
        }

        // Whatever the outcome, nothing is connected until a browser says so.
        _variables?.Clear();
    }

    private void OnStateReceived(PlayerState state)
    {
        _variables?.Set(state);
    }

    private void OnClientLost()
    {
        // An unobserved player must never leave a stale 'playing' on a button.
        _variables?.Clear();
    }

    public override void OpenConfigurator()
    {
        using var dialog = new ConfiguratorDialog(this);
        dialog.ShowDialog();
    }
}
