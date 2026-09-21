# Wire protocol

Version 1. JSON text frames over a WebSocket on `ws://127.0.0.1:8975`.

The plugin is the server. The extension's service worker is the client.

Every frame is a JSON object with a `type` field. Unknown types are ignored by
both sides rather than treated as errors, so that a newer peer can add message
types without breaking an older one.

## Handshake

```
extension                        plugin
    |                              |
    |------- TCP + WS upgrade ---->|   Origin header checked here
    |                              |   mismatch -> close 1008, no frames sent
    |------- hello --------------->|   token checked here
    |                              |   mismatch -> error + close 1008
    |<------ welcome --------------|
    |------- state --------------->|   full current state, unprompted
    |                              |
```

A client that does not send `hello` within 5 seconds of connecting is closed.

## Extension → plugin

### `hello`

First frame after connecting. Always.

```json
{
  "type": "hello",
  "token": "3f9c1a7e8b4d2056",
  "version": "1.0.0"
}
```

| Field | Type | Notes |
| --- | --- | --- |
| `token` | string | Must equal the plugin's configured token |
| `version` | string | Extension version; logged, not enforced in V1 |

### `state`

Sent on connect, and thereafter whenever anything in it changes. Never sent on a
timer.

```json
{
  "type": "state",
  "connected": true,
  "playing": false,
  "volume": 65,
  "muted": false,
  "shuffle": true,
  "repeat": "all"
}
```

| Field | Type | Notes |
| --- | --- | --- |
| `connected` | bool | At least one YouTube Music tab is present and reporting |
| `playing` | bool | The authoritative tab is actually playing |
| `volume` | int \| null | 0-100, or null when it could not be read |
| `muted` | bool | The player is muted |
| `shuffle` | bool \| null | Null when the shuffle control could not be read |
| `repeat` | string \| null | `off`, `all`, `one`, or null when it could not be read |

When `connected` is `false`, every other field is reset: `playing` and `muted`
are `false`, the rest are null.

**Null means "unknown", not "zero".** The plugin leaves the matching Macro Deck
variable at its previous value rather than writing a misleading one. Volume and
mute are read from the media element and are effectively always available;
shuffle and repeat are read from YouTube Music's own controls, and those are the
fields that go null when the page changes.

The plugin rejects a `volume` outside 0-100 and a `repeat` that is not one of
the three modes, treating each as null.

A later version may add `title`, `artist`, `album`, `position` and `duration`.
Both sides ignore fields they do not recognise.

### `ping` and `pong`

Neither carries a payload.

```json
{ "type": "ping" }
{ "type": "pong" }
```

The extension sends `ping` every 20 seconds and replies `pong` to the plugin's
`ping`. The plugin does the same in reverse. Traffic in either direction is what
keeps the Manifest V3 service worker from being terminated, so both sides ping
independently rather than relying on the peer's timer.

## Plugin → extension

### `welcome`

Authentication succeeded. The extension may now send state.

```json
{ "type": "welcome", "version": "1.0.0" }
```

### `error`

Authentication failed, or a frame was rejected. The plugin closes the socket
immediately after sending this when `fatal` is true.

```json
{ "type": "error", "reason": "bad_token", "fatal": true }
```

| `reason` | Meaning |
| --- | --- |
| `bad_token` | Token missing or wrong |
| `bad_origin` | Origin not in the allow-list (usually closed before any frame) |
| `timeout` | No `hello` within 5 seconds |
| `malformed` | Frame was not valid JSON |

### `command`

A Macro Deck button was pressed.

```json
{ "type": "command", "command": "play_pause" }
```

| `command` | Effect |
| --- | --- |
| `play` | Start playback |
| `pause` | Stop playback |
| `play_pause` | Toggle, based on observed state |
| `next` | Next track |
| `previous` | Previous track |
| `shuffle` | Toggle shuffle |
| `repeat` | Cycle repeat: off → all → one |
| `repeat_off` | Set repeat off |
| `repeat_all` | Set repeat to the whole queue |
| `repeat_one` | Set repeat to the current track |
| `volume_up` | Raise volume by 5% |
| `volume_down` | Lower volume by 5% |
| `mute` | Toggle mute |

The three explicit repeat commands read the current mode and click the control
until it matches, at most twice. If the mode cannot be read they do nothing and
log it — `repeat` still works, because cycling needs no starting knowledge.

The extension routes the command to the authoritative tab only. The plugin does
not change any variable as a result of sending a command — it waits for the
`state` frame that follows.

### `ping` and `pong`

Liveness probe, sent every 20 seconds, and the reply to the extension's own
`ping`. Its real job is keeping the Manifest V3 service worker from being
terminated for inactivity.

```json
{ "type": "ping" }
{ "type": "pong" }
```

## Close codes

| Code | Meaning |
| --- | --- |
| `1000` | Normal shutdown |
| `1008` | Policy violation: bad origin, bad token, or handshake timeout |

## Internal extension messaging

Not part of the wire protocol, but the same vocabulary is used between the
content script and the service worker, so that the worker can forward without
translating.

Content script → worker:

```json
{ "type": "tab_state", "playing": true, "volume": 65, "muted": false, "shuffle": true, "repeat": "all" }
```

Worker → content script:

```json
{ "type": "command", "command": "next" }
```
