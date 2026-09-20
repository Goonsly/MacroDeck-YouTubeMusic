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

Record the observed behaviour here after the first run against real Macro Deck:

- `youtube_music_playing` existing creator: _to be filled in on first run_
- Value written by the plugin visible in the Macro Deck variables view: _to be filled in_
- Duplicate variable created: _to be filled in_

If Macro Deck refuses to let a plugin write a user-created variable, the fallback
is documented in `docs/INSTALL.md`: delete the manual variable once and let the
plugin create it.

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
