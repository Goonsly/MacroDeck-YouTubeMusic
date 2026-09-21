# Implementation notes

Verified facts about the host environment, recorded so that later work does not
have to rediscover them.

## Macro Deck plugin API (version 41, Macro Deck 2.15.1)

Extracted from `D:\MacroDeck\Macro Deck 2.dll` via `MetadataLoadContext`.

### `SuchByte.MacroDeck.Plugins.MacroDeckPlugin` (abstract)

| Member | Signature |
| --- | --- |
| `Enable()` | abstract, called once when Macro Deck loads the plugin |
| `Actions` | `List<PluginAction>`, settable |
| `CanConfigure` | virtual `bool` |
| `OpenConfigurator()` | virtual |
| `Description` | virtual `string` |
| `Icon` | virtual `Image` |

### `SuchByte.MacroDeck.Plugins.PluginAction` (abstract)

| Member | Signature |
| --- | --- |
| `Name` | **abstract** `string` — must be overridden |
| `Description` | **abstract** `string` — must be overridden |
| `Trigger(string clientId, ActionButton actionButton)` | abstract |
| `CanConfigure` | virtual `bool` |
| `GetActionConfigControl(ActionConfigurator)` | virtual |

### `SuchByte.MacroDeck.Variables.VariableManager` (static)

```csharp
static void SetValue(string name, object value, VariableType type,
                     MacroDeckPlugin plugin, bool save);
static void SetValue(string name, object value, VariableType type,
                     MacroDeckPlugin plugin, string[] suggestions);
static void SetValue(string name, object value, VariableType type,
                     MacroDeckPlugin plugin, string[] suggestions, bool save);
static Variable GetVariable(MacroDeckPlugin plugin, string variableName);
static List<Variable> GetVariables(MacroDeckPlugin plugin);
static void DeleteVariable(string name);
static Variable[] Variables { get; }
```

`VariableType` = `Integer | Float | String | Bool`.

`Variable` exposes `Name`, `Value` (string), `Creator`, `Type`, `VariableType`,
`Suggestions`.

### `SuchByte.MacroDeck.Plugins.PluginConfiguration` (static)

```csharp
static void SetValue(MacroDeckPlugin plugin, string key, string value);
static string GetValue(MacroDeckPlugin plugin, string key);
```

Used for the port, the token and the allowed extension IDs.

### `SuchByte.MacroDeck.Logging.MacroDeckLogger` (static)

`Verbose / Debug / Information / Warning / Error`, each with a
`(MacroDeckPlugin plugin, string template, params object[] values)` overload.
Logs land in `%AppData%\Macro Deck\logs`.

### UI base classes

`SuchByte.MacroDeck.GUI.CustomControls.DialogForm` (themed `Form`),
`ButtonPrimary`, `PlaceHolderTextBox`. WinForms, so the plugin targets
`net10.0-windows` with `UseWindowsForms`.

## Variable ownership — open question, resolved at runtime

`VariableManager.SetValue` takes the owning plugin and `Variable` carries a
`Creator` field. The user created `youtube_music_playing` manually before this
project existed, so its creator is not this plugin.

The plugin does not guess. On `Enable()` it calls `VariableManager.Variables`,
finds any existing variable with our names, and logs the observed `Creator` and
`Type`. `AdoptOrCreate` then writes through `SetValue` and re-reads the variable
to confirm the value landed.

**Resolved on the first run against Macro Deck 2.15.1, 2026-09-20.** From the log:

```
Variable 'youtube_music_playing' already exists (creator 'User', type 'Bool');
  it will be reused, not duplicated.
...
Variable 'youtube_music_playing' already exists (creator 'YouTubeMusicPlugin', type 'Bool');
  it will be reused, not duplicated.
```

- Existing creator before the plugin ran: `User`.
- After the plugin wrote to it, the creator became `YouTubeMusicPlugin`.
- No duplicate variable was created.

So `VariableManager.SetValue` adopts a user-created variable and transfers
ownership to the writing plugin. The hand-made `youtube_music_playing` works
as-is, and the fallback in `docs/INSTALL.md` is not needed.

## Action types must be public

Macro Deck instantiates `PluginAction` subclasses by reflection and rejects
non-public ones:

```
System.InvalidOperationException: ...PlayPauseAction is inaccessible due to its
protection level. Only public types can be processed.
```

The failure is quiet in the UI: the plugin still reports "5 actions", but the
action list renders empty. The exception appears only in the Macro Deck log.
`ProtocolTests` now asserts that every concrete `PluginAction` in the assembly is
public, so this cannot come back.

## Update check errors are expected

```
[MacroDeck] Failed to check for updates for KeystoneDigital.YouTubeMusic
```

The plugin is not published in the Macro Deck extension store, so there is
nothing to check against. Harmless.

## Origin allow-list behaviour

The specification calls for an allow-list of `chrome-extension://<id>` origins.
Until the extension is published, its ID is not known, and an unpacked build gets
a different ID than the published one.

Implemented rule:

- Empty allow-list → accept any origin beginning `chrome-extension://`, reject
  everything else. No website can ever connect, and the token is still required.
- Non-empty allow-list → accept only those exact extension IDs.

This keeps first-run working without weakening the protection that matters. The
configuration dialog states which mode is active.

An unpacked extension's ID is derived from the absolute path of its folder, so it
stays stable as long as `extension\` is not moved.

## Chrome service worker lifetime

Manifest V3 terminates an idle service worker after about 30 seconds. WebSocket
traffic resets that timer. Both sides therefore ping every 20 seconds, and the
extension additionally registers a 1-minute `chrome.alarms` alarm that
re-establishes the connection if the worker was terminated anyway.

## Media Session metadata

`navigator.mediaSession.metadata` set by the page is not visible to a content
script, which runs in an isolated world. V2 metadata must come from the player
DOM or from a script injected into the main world. This corrects the ordering in
the original build plan.

## Variable writes must happen on the UI thread

Observed 2026-09-20 during live testing. A button bound to
`youtube_music_playing` painted as a broken-image tile, and the Macro Deck log
showed:

```
[ERR] [MacroDeck] Unhandled thread exception
System.ArgumentException: Parameter is not valid.
   at System.Drawing.Graphics.DrawImage(Image image, Rectangle rect)
   at SuchByte.MacroDeck.GUI.CustomControls.RoundedButton.OnPaint(PaintEventArgs pe)
```

Setting a variable makes Macro Deck repaint every button bound to it. The plugin
was writing from a Fleck socket thread, so the repaint raced the WinForms
painter and GDI+ rejected the shared `Bitmap`.

`VariableSync` now marshals writes through `SuchByte.MacroDeck.MacroDeck.MainWindow`
(`BeginInvoke` when `InvokeRequired`), falling back to a direct write when there
is no window yet. It also writes only the variable that actually changed rather
than both every time.

## State writes are debounced by 200 ms

The paint crash above survived moving writes onto the UI thread, so the root
cause is inside Macro Deck's own button painter, not this plugin. It is not
fixable from here; it can only be triggered less often.

A track change fires `pause` then `play` within milliseconds, which made Macro
Deck swap the bound button's icon several times in quick succession. `VariableSync`
now coalesces changes over a 200 ms trailing window: the first change schedules a
flush, later changes inside the window only update what will be written. A write
is therefore guaranteed within one interval no matter how hard the state flaps.

Connect, disconnect and startup bypass the debounce and write immediately —
losing the browser must not wait out a timer.

`MacroDeck.MainWindow` is untouchable outside Macro Deck: its static constructor
throws `TypeInitializationException`. `OnUiThread` catches that once, remembers
it, and writes directly, which is what lets `ProtocolTests` exercise `VariableSync`.

## Acceptance testing

All acceptance criteria in `SPEC.md` §11 passed on 2026-09-20 against Macro Deck
2.15.1 and Chrome on Windows 10, including the cases most likely to break:

- Next and Previous, which depend on YouTube Music's player markup.
- Two YouTube Music tabs open: only the active one responds to commands.
- Controls while Chrome is minimised.
- Pausing in the browser updating the deck, and reconnection after reload,
  browser restart and Macro Deck restart.

## YouTube Music player-bar markup, verified 2026-09-21

Read from the live page rather than assumed. Three corrections to the first
implementation of shuffle and repeat:

| What | Where it actually lives |
| --- | --- |
| Control elements | `yt-icon-button.shuffle` / `.repeat` / `.next-button` / `.previous-button`, **not** `tp-yt-paper-icon-button` |
| Shuffle state | `ytmusic-player-bar[shuffle-on]` — a bare attribute on the **player bar** |
| Repeat state | `ytmusic-player-bar[repeat-mode]` = `NONE` \| `ALL` \| `ONE` |

The shuffle button itself carries no `aria-pressed`, and its `title` stays
"Shuffle" whether shuffle is on or off — so reading the button can never work.
That was the bug: `shuffleState()` returned null forever, the extension sent
null, and the plugin correctly left the variable alone.

The repeat button's `title` and `label` name the mode **currently in effect**
("Repeat off", "Repeat all", "Repeat one"), not the one the next click will
select. The first implementation assumed the opposite and mapped the fallback a
step backwards round the cycle.

Selector lists now try `yt-icon-button` first, then the old
`tp-yt-paper-icon-button`, then the bare class. The bare class is why clicking
worked all along while the state never updated.
