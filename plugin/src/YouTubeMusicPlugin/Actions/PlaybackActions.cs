using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.Plugins;

namespace KeystoneDigital.YouTubeMusic.Actions;

/// <summary>
/// Shared plumbing for the playback actions. Each action does one thing: hand a
/// command to the server. It never touches a variable — the variable changes
/// only when the browser reports what actually happened.
/// </summary>
public abstract class PlaybackAction : PluginAction
{
    protected abstract string Command { get; }

    public override bool CanConfigure => false;

    public override void Trigger(string clientId, ActionButton actionButton)
    {
        YouTubeMusicPlugin.Instance?.Server.SendCommand(Command);
    }
}

public sealed class PlayPauseAction : PlaybackAction
{
    public override string Name => "Play / Pause";

    public override string Description => "Toggles YouTube Music playback.";

    protected override string Command => "play_pause";
}

public sealed class PlayAction : PlaybackAction
{
    public override string Name => "Play";

    public override string Description => "Starts YouTube Music playback.";

    protected override string Command => "play";
}

public sealed class PauseAction : PlaybackAction
{
    public override string Name => "Pause";

    public override string Description => "Pauses YouTube Music playback.";

    protected override string Command => "pause";
}

public sealed class NextAction : PlaybackAction
{
    public override string Name => "Next";

    public override string Description => "Skips to the next track.";

    protected override string Command => "next";
}

public sealed class PreviousAction : PlaybackAction
{
    public override string Name => "Previous";

    public override string Description => "Goes back to the previous track.";

    protected override string Command => "previous";
}
