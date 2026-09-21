using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using KeystoneDigital.YouTubeMusic.Server;
using SuchByte.MacroDeck.Plugins;

namespace KeystoneDigital.YouTubeMusic.Tests;

/// <summary>
/// Exercises the plugin's WebSocket server without Macro Deck and without
/// Chrome. It covers the rules that are easy to get wrong and impossible to see
/// by hand: origin rejection, token rejection, handshake timeout, state
/// coercion and command dispatch.
///
/// Run with: dotnet run --project plugin\test\ProtocolTests
/// </summary>
internal static class Program
{
    private const string Token = "testtoken0123456789abcdef";
    private const string GoodOrigin = "chrome-extension://abcdefghijklmnopabcdefghijklmnop";
    private const int Port = 8976;

    private static int _failures;

    private static async Task<int> Main()
    {
        var plugin = new StubPlugin();
        using var server = new YtmServer(plugin);

        PlayerState? lastState = null;
        var stateSignal = new SemaphoreSlim(0);
        var lostSignal = new SemaphoreSlim(0);

        server.StateReceived += state =>
        {
            lastState = state;
            stateSignal.Release();
        };

        server.ClientLost += () => lostSignal.Release();

        await RunAsync("every action type is public", () =>
        {
            // Macro Deck instantiates action types by reflection and refuses
            // anything that is not public: "Only public types can be processed."
            // An internal action still counts toward the plugin's action total,
            // so the action list simply renders empty with no error in the UI.
            var actionTypes = typeof(YouTubeMusicPlugin).Assembly
                .GetTypes()
                .Where(type => typeof(PluginAction).IsAssignableFrom(type) && !type.IsAbstract)
                .ToList();

            Check(actionTypes.Count == 13, $"thirteen action types found, got {actionTypes.Count}");

            foreach (var type in actionTypes)
            {
                Check(type.IsPublic, $"{type.Name} is public");
            }

            return Task.CompletedTask;
        });

        await RunAsync("a burst of state changes collapses into one write", async () =>
        {
            var writes = new List<(string Name, object Value)>();
            using var sync = new VariableSync(
                plugin,
                TimeSpan.FromMilliseconds(80),
                (name, value) => { lock (writes) writes.Add((name, value)); });

            // A track change looks like this: playing, paused, playing again,
            // all within a few milliseconds.
            sync.Set(Playing(true));
            sync.Set(Playing(false));
            sync.Set(Playing(true));
            sync.Set(Playing(false));

            await Task.Delay(400);

            lock (writes)
            {
                // connected, playing, muted, volume, shuffle, repeat — written
                // once each, not once per change.
                Check(writes.Count == 6, $"six writes for four changes, got {writes.Count}");
                Check(
                    writes.Any(w => w.Name == "youtube_music_connected" && (bool)w.Value),
                    "connected written as true");
                Check(
                    writes.Any(w => w.Name == "youtube_music_playing" && !(bool)w.Value),
                    "playing written as the final value, false");
                Check(
                    writes.Any(w => w.Name == "youtube_music_volume" && (int)w.Value == 40),
                    "volume written as 40");
                Check(
                    writes.Any(w => w.Name == "youtube_music_repeat" && (string)w.Value == "all"),
                    "repeat written as 'all'");
            }
        });

        await RunAsync("a first report still writes every readable field", async () =>
        {
            var writes = new List<(string Name, object Value)>();
            using var sync = new VariableSync(
                plugin,
                TimeSpan.FromMilliseconds(40),
                (name, value) => { lock (writes) writes.Add((name, value)); });

            sync.Set(Playing(true));
            await Task.Delay(250);

            lock (writes)
            {
                foreach (var name in VariableSync.AllVariables)
                {
                    Check(writes.Any(w => w.Name == name), $"{name} written");
                }
            }
        });

        await RunAsync("unreadable values leave their variables alone", async () =>
        {
            var writes = new List<(string Name, object Value)>();
            using var sync = new VariableSync(
                plugin,
                TimeSpan.FromMilliseconds(40),
                (name, value) => { lock (writes) writes.Add((name, value)); });

            // Volume, shuffle and repeat unreadable: a YouTube Music redesign
            // must not overwrite a good value with a misleading one.
            sync.Set(new PlayerState(true, true, null, false, null, null));
            await Task.Delay(250);

            lock (writes)
            {
                Check(
                    writes.All(w => w.Name is not ("youtube_music_volume" or "youtube_music_shuffle" or "youtube_music_repeat")),
                    "no write for volume, shuffle or repeat");
                Check(
                    writes.Any(w => w.Name == "youtube_music_playing"),
                    "playing still written");
            }
        });

        await RunAsync("disconnection is written immediately", async () =>
        {
            var writes = new List<(string Name, object Value)>();
            using var sync = new VariableSync(
                plugin,
                TimeSpan.FromMilliseconds(5000),
                (name, value) => { lock (writes) writes.Add((name, value)); });

            sync.Clear();

            // No delay: losing the browser must not wait out the debounce.
            lock (writes)
            {
                Check(writes.Count == 3, $"connected, playing and muted written at once, got {writes.Count}");
                Check(writes.All(w => w.Value is bool and false), "all written as false");
            }

            await Task.CompletedTask;
        });

        server.Start(Port, Token, Array.Empty<string>());

        await RunAsync("a website origin is refused", async () =>
        {
            using var client = await OpenAsync("https://evil.example", expectFailure: true);
            Check(client is null || !await SawWelcomeAsync(client!), "no welcome for a website origin");
        });

        await RunAsync("a wrong token is refused", async () =>
        {
            using var client = await OpenAsync(GoodOrigin);
            await SendAsync(client!, new { type = "hello", token = "wrong", version = "1.0.0" });
            var message = await ReceiveAsync(client!);
            Check(message.GetProperty("type").GetString() == "error", "error frame returned");
            Check(message.GetProperty("reason").GetString() == "bad_token", "reason is bad_token");
        });

        await RunAsync("the correct token is accepted", async () =>
        {
            using var client = await OpenAsync(GoodOrigin);
            await SendAsync(client!, new { type = "hello", token = Token, version = "1.0.0" });
            var message = await ReceiveAsync(client!);
            Check(message.GetProperty("type").GetString() == "welcome", "welcome frame returned");
        });

        await RunAsync("reported state reaches the plugin", async () =>
        {
            using var client = await OpenAsync(GoodOrigin);
            await AuthenticateAsync(client!);

            lastState = null;
            await SendAsync(client!, new
            {
                type = "state",
                connected = true,
                playing = true,
                volume = 65,
                muted = false,
                shuffle = true,
                repeat = "one",
            });
            Check(await stateSignal.WaitAsync(2000), "state event raised");
            Check(
                lastState == new PlayerState(true, true, 65, false, true, "one"),
                $"every field arrived, got {Describe(lastState)}");
        });

        await RunAsync("an out-of-range volume and an unknown repeat mode are dropped", async () =>
        {
            using var client = await OpenAsync(GoodOrigin);
            await AuthenticateAsync(client!);

            lastState = null;
            await SendAsync(client!, new
            {
                type = "state",
                connected = true,
                playing = true,
                volume = 250,
                repeat = "sideways",
            });
            Check(await stateSignal.WaitAsync(2000), "state event raised");
            Check(lastState?.Volume is null, $"volume rejected, got {Describe(lastState)}");
            Check(lastState?.Repeat is null, "unknown repeat mode rejected");
        });

        await RunAsync("nothing survives a disconnected report", async () =>
        {
            using var client = await OpenAsync(GoodOrigin);
            await AuthenticateAsync(client!);

            lastState = null;
            await SendAsync(client!, new
            {
                type = "state",
                connected = false,
                playing = true,
                volume = 80,
                muted = true,
                shuffle = true,
                repeat = "all",
            });
            Check(await stateSignal.WaitAsync(2000), "state event raised");
            Check(
                lastState == PlayerState.Disconnected,
                $"everything coerced to disconnected, got {Describe(lastState)}");
        });

        await RunAsync("commands reach the browser", async () =>
        {
            using var client = await OpenAsync(GoodOrigin);
            await AuthenticateAsync(client!);

            server.SendCommand("next");

            var message = await ReceiveUntilAsync(client!, "command");
            Check(message.GetProperty("command").GetString() == "next", "command is 'next'");
        });

        await RunAsync("a silent client is closed after the handshake timeout", async () =>
        {
            using var client = await OpenAsync(GoodOrigin);
            var message = await ReceiveUntilAsync(client!, "error", timeoutMs: 9000);
            Check(message.GetProperty("reason").GetString() == "timeout", "reason is timeout");
        });

        await RunAsync("losing the client raises ClientLost", async () =>
        {
            var client = await OpenAsync(GoodOrigin);
            await AuthenticateAsync(client!);

            // Drain the ClientLost signal left by earlier tests.
            while (lostSignal.CurrentCount > 0) await lostSignal.WaitAsync(0);

            await client!.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            client.Dispose();

            Check(await lostSignal.WaitAsync(3000), "ClientLost raised");
        });

        await RunAsync("the allow-list rejects an unlisted extension", async () =>
        {
            server.Start(Port, Token, new[] { "onlythisextensionidonlythisextens" });

            using var client = await OpenAsync(GoodOrigin, expectFailure: true);
            Check(client is null || !await SawWelcomeAsync(client!), "unlisted extension gets no welcome");
        });

        server.Stop();

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "All protocol tests passed." : $"{_failures} check(s) failed.");
        return _failures == 0 ? 0 : 1;
    }

    /* ------------------------------------------------------------- helpers */

    /// <summary>A connected player at a fixed volume, playing or not.</summary>
    private static PlayerState Playing(bool playing) =>
        new(Connected: true, Playing: playing, Volume: 40, Muted: false, Shuffle: true, Repeat: "all");

    private static string Describe(PlayerState? state) =>
        state is null
            ? "no state"
            : $"connected={state.Value.Connected} playing={state.Value.Playing} volume={state.Value.Volume?.ToString() ?? "null"} muted={state.Value.Muted} shuffle={state.Value.Shuffle?.ToString() ?? "null"} repeat={state.Value.Repeat ?? "null"}";

    private static async Task RunAsync(string name, Func<Task> body)
    {
        Console.WriteLine($"- {name}");
        try
        {
            await body();
        }
        catch (Exception exception)
        {
            _failures++;
            Console.WriteLine($"    FAIL {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void Check(bool condition, string what)
    {
        if (condition)
        {
            Console.WriteLine($"    ok   {what}");
            return;
        }

        _failures++;
        Console.WriteLine($"    FAIL {what}");
    }

    private static async Task<ClientWebSocket?> OpenAsync(string origin, bool expectFailure = false)
    {
        var client = new ClientWebSocket();
        client.Options.SetRequestHeader("Origin", origin);

        try
        {
            await client.ConnectAsync(new Uri($"ws://127.0.0.1:{Port}"), CancellationToken.None);
            return client;
        }
        catch when (expectFailure)
        {
            client.Dispose();
            return null;
        }
    }

    private static async Task AuthenticateAsync(ClientWebSocket client)
    {
        await SendAsync(client, new { type = "hello", token = Token, version = "1.0.0" });
        var message = await ReceiveUntilAsync(client, "welcome");
        if (message.GetProperty("type").GetString() != "welcome")
        {
            throw new InvalidOperationException("authentication did not produce a welcome");
        }
    }

    private static async Task<bool> SawWelcomeAsync(ClientWebSocket client)
    {
        try
        {
            await SendAsync(client, new { type = "hello", token = Token, version = "1.0.0" });
            var message = await ReceiveAsync(client, timeoutMs: 1500);
            return message.GetProperty("type").GetString() == "welcome";
        }
        catch
        {
            return false;
        }
    }

    private static async Task SendAsync(ClientWebSocket client, object payload)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        await client.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<JsonElement> ReceiveAsync(ClientWebSocket client, int timeoutMs = 3000)
    {
        using var timeout = new CancellationTokenSource(timeoutMs);
        var buffer = new byte[8192];
        var result = await client.ReceiveAsync(buffer, timeout.Token);

        if (result.MessageType == WebSocketMessageType.Close)
        {
            throw new InvalidOperationException($"socket closed: {result.CloseStatus} {result.CloseStatusDescription}");
        }

        var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>Skips ping frames, which arrive on their own schedule.</summary>
    private static async Task<JsonElement> ReceiveUntilAsync(ClientWebSocket client, string type, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            var remaining = (int)Math.Max(250, (deadline - DateTime.UtcNow).TotalMilliseconds);
            var message = await ReceiveAsync(client, remaining);

            if (message.GetProperty("type").GetString() == type)
            {
                return message;
            }
        }

        throw new TimeoutException($"no '{type}' frame within {timeoutMs} ms");
    }

    /// <summary>Stands in for the real plugin; the server only needs it for logging.</summary>
    private sealed class StubPlugin : MacroDeckPlugin
    {
        public override void Enable()
        {
        }
    }
}
