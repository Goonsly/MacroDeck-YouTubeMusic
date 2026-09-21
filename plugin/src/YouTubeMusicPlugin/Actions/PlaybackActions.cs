using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.Plugins;

namespace KeystoneDigital.YouTubeMusic.Actions;

/// <summary>
/// Shared plumbing for the player actions. Each action does one thing: hand a
/// command to the server. It never touches a variable — the variable changes
/// only when the browser reports what actually happened.
///
/// Must be public: Macro Deck instantiates action types by reflection and
/// refuses non-public ones with "Only public types can be processed."
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

public sealed class ShuffleAction : PlaybackAction
{
    public override string Name => "Shuffle";

    public override string Description => "Toggles shuffle.";

    protected override string Command => "shuffle";
}

public sealed class RepeatAction : PlaybackAction
{
    public override string Name => "Repeat";

    public override string Description => "Cycles repeat: off, all, one.";

    protected override string Command => "repeat";
}

public sealed class RepeatOffAction : PlaybackAction
{
    public override string Name => "Repeat Off";

    public override string Description => "Turns repeat off.";

    protected override string Command => "repeat_off";
}

public sealed class RepeatAllAction : PlaybackAction
{
    public override string Name => "Repeat All";

    public override string Description => "Repeats the whole queue.";

    protected override string Command => "repeat_all";
}

public sealed class RepeatOneAction : PlaybackAction
{
    public override string Name => "Repeat One";

    public override string Description => "Repeats the current track.";

    protected override string Command => "repeat_one";
}

public sealed class VolumeUpAction : PlaybackAction
{
    public override string Name => "Volume Up";

    public override string Description => "Raises the YouTube Music volume by 5%.";

    protected override string Command => "volume_up";
}

public sealed class VolumeDownAction : PlaybackAction
{
    public override string Name => "Volume Down";

    public override string Description => "Lowers the YouTube Music volume by 5%.";

    protected override string Command => "volume_down";
}

public sealed class MuteAction : PlaybackAction
{
    public override string Name => "Mute";

    public override string Description => "Toggles mute for YouTube Music only.";

    protected override string Command => "mute";
}
