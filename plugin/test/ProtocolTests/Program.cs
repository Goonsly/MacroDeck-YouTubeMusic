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

        (bool connected, bool playing)? lastState = null;
        var stateSignal = new SemaphoreSlim(0);
        var lostSignal = new SemaphoreSlim(0);

        server.StateReceived += (connected, playing) =>
        {
            lastState = (connected, playing);
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

            Check(actionTypes.Count == 5, $"five action types found, got {actionTypes.Count}");

            foreach (var type in actionTypes)
            {
                Check(type.IsPublic, $"{type.Name} is public");
            }

            return Task.CompletedTask;
        });

        await RunAsync("a burst of state changes collapses into one write", async () =>
        {
            var writes = new List<(string Name, bool Value)>();
            using var sync = new VariableSync(
                plugin,
                TimeSpan.FromMilliseconds(80),
                (name, value) => { lock (writes) writes.Add((name, value)); });

            // A track change looks like this: playing, paused, playing again,
            // all within a few milliseconds.
            sync.Set(connected: true, playing: true);
            sync.Set(connected: true, playing: false);
            sync.Set(connected: true, playing: true);
            sync.Set(connected: true, playing: false);

            await Task.Delay(400);

            lock (writes)
            {
                Check(writes.Count == 2, $"two writes for four changes, got {writes.Count}");
                Check(
                    writes.Any(w => w.Name == "youtube_music_connected" && w.Value),
                    "connected written as true");
                Check(
                    writes.Any(w => w.Name == "youtube_music_playing" && !w.Value),
                    "playing written as the final value, false");
            }
        });

        await RunAsync("disconnection is written immediately", async () =>
        {
            var writes = new List<(string Name, bool Value)>();
            using var sync = new VariableSync(
                plugin,
                TimeSpan.FromMilliseconds(5000),
                (name, value) => { lock (writes) writes.Add((name, value)); });

            sync.Clear();

            // No delay: losing the browser must not wait out the debounce.
            lock (writes)
            {
                Check(writes.Count == 2, $"both variables written at once, got {writes.Count}");
                Check(writes.All(w => !w.Value), "both written as false");
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
            await SendAsync(client!, new { type = "state", connected = true, playing = true });
            Check(await stateSignal.WaitAsync(2000), "state event raised");
            Check(lastState == (true, true), $"state is connected+playing, got {Describe(lastState)}");
        });

        await RunAsync("playing cannot be true while disconnected", async () =>
        {
            using var client = await OpenAsync(GoodOrigin);
            await AuthenticateAsync(client!);

            lastState = null;
            await SendAsync(client!, new { type = "state", connected = false, playing = true });
            Check(await stateSignal.WaitAsync(2000), "state event raised");
            Check(lastState == (false, false), $"playing coerced to false, got {Describe(lastState)}");
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

    private static string Describe((bool connected, bool playing)? state) =>
        state is null ? "no state" : $"connected={state.Value.connected} playing={state.Value.playing}";

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
