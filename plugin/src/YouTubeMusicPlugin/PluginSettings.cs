using System.Security.Cryptography;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;

namespace KeystoneDigital.YouTubeMusic;

/// <summary>
/// Port, token and allowed extension IDs, persisted through Macro Deck's own
/// plugin configuration store.
/// </summary>
internal sealed class PluginSettings
{
    public const int DefaultPort = 8975;

    private const string KeyPort = "port";
    private const string KeyToken = "token";
    private const string KeyAllowedExtensionIds = "allowedExtensionIds";

    private readonly MacroDeckPlugin _plugin;

    public PluginSettings(MacroDeckPlugin plugin)
    {
        _plugin = plugin;
        Port = ReadPort();
        Token = ReadOrCreateToken();
        AllowedExtensionIds = ReadAllowedExtensionIds();
    }

    public int Port { get; private set; }

    public string Token { get; private set; }

    /// <summary>
    /// Chrome extension IDs permitted to connect. An empty list means any
    /// <c>chrome-extension://</c> origin is accepted; no website ever is.
    /// </summary>
    public IReadOnlyList<string> AllowedExtensionIds { get; private set; }

    public void SetPort(int port)
    {
        if (port is < 1024 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1024 and 65535.");
        }

        Port = port;
        PluginConfiguration.SetValue(_plugin, KeyPort, port.ToString());
    }

    public void SetAllowedExtensionIds(IEnumerable<string> ids)
    {
        var cleaned = ids
            .Select(id => id.Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        AllowedExtensionIds = cleaned;
        PluginConfiguration.SetValue(_plugin, KeyAllowedExtensionIds, string.Join(",", cleaned));
    }

    /// <summary>Replaces the token with a freshly generated one.</summary>
    public string RegenerateToken()
    {
        Token = GenerateToken();
        PluginConfiguration.SetValue(_plugin, KeyToken, Token);
        return Token;
    }

    private int ReadPort()
    {
        var stored = PluginConfiguration.GetValue(_plugin, KeyPort);
        if (int.TryParse(stored, out var port) && port is >= 1024 and <= 65535)
        {
            return port;
        }

        return DefaultPort;
    }

    private string ReadOrCreateToken()
    {
        var stored = PluginConfiguration.GetValue(_plugin, KeyToken);
        if (!string.IsNullOrWhiteSpace(stored))
        {
            return stored.Trim();
        }

        var token = GenerateToken();
        PluginConfiguration.SetValue(_plugin, KeyToken, token);
        MacroDeckLogger.Information(_plugin, "Generated a new connection token.");
        return token;
    }

    private IReadOnlyList<string> ReadAllowedExtensionIds()
    {
        var stored = PluginConfiguration.GetValue(_plugin, KeyAllowedExtensionIds);
        if (string.IsNullOrWhiteSpace(stored))
        {
            return Array.Empty<string>();
        }

        return stored
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GenerateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}
