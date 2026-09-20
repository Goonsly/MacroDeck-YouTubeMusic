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
