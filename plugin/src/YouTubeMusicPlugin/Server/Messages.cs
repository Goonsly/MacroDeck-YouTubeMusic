using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeystoneDigital.YouTubeMusic.Server;

/// <summary>Message types defined in docs/PROTOCOL.md.</summary>
internal static class MessageTypes
{
    public const string Hello = "hello";
    public const string State = "state";
    public const string Welcome = "welcome";
    public const string Error = "error";
    public const string Command = "command";
    public const string Ping = "ping";
    public const string Pong = "pong";
}

internal static class ErrorReasons
{
    public const string BadToken = "bad_token";
    public const string BadOrigin = "bad_origin";
    public const string Timeout = "timeout";
    public const string Malformed = "malformed";
}

internal sealed class IncomingMessage
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("extension")]
    public string? Extension { get; set; }

    [JsonPropertyName("connected")]
    public bool? Connected { get; set; }

    [JsonPropertyName("playing")]
    public bool? Playing { get; set; }

    /// <summary>0-100, or null when the browser could not read it.</summary>
    [JsonPropertyName("volume")]
    public int? Volume { get; set; }

    [JsonPropertyName("muted")]
    public bool? Muted { get; set; }

    /// <summary>Null when the page's shuffle control could not be read.</summary>
    [JsonPropertyName("shuffle")]
    public bool? Shuffle { get; set; }

    /// <summary>"off", "all", "one", or null when it could not be read.</summary>
    [JsonPropertyName("repeat")]
    public string? Repeat { get; set; }
}

/// <summary>
/// One snapshot of what the browser reports. A null means the browser could not
/// read that value; the plugin leaves the matching variable alone rather than
/// writing something misleading.
/// </summary>
internal readonly record struct PlayerState(
    bool Connected,
    bool Playing,
    int? Volume,
    bool Muted,
    bool? Shuffle,
    string? Repeat)
{
    public static PlayerState Disconnected => new(false, false, null, false, null, null);
}

internal static class Json
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static IncomingMessage? Parse(string text) =>
        JsonSerializer.Deserialize<IncomingMessage>(text, Options);

    public static string Welcome(string version) =>
        JsonSerializer.Serialize(new { type = MessageTypes.Welcome, version }, Options);

    public static string Error(string reason, bool fatal) =>
        JsonSerializer.Serialize(new { type = MessageTypes.Error, reason, fatal }, Options);

    public static string Command(string command) =>
        JsonSerializer.Serialize(new { type = MessageTypes.Command, command }, Options);

    public static string Ping() => JsonSerializer.Serialize(new { type = MessageTypes.Ping }, Options);

    public static string Pong() => JsonSerializer.Serialize(new { type = MessageTypes.Pong }, Options);
}
