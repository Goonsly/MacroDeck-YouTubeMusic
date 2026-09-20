# YouTube Music ↔ Macro Deck — V1 Design Specification

Date: 2026-09-20
Status: Approved for implementation

## 1. Purpose

Control YouTube Music playback from a Macro Deck 2 button, and keep a Macro Deck
variable in sync with the browser's real playback state.

The existing WebNowPlaying plugin does not reliably detect `music.youtube.com` on
this machine. This project replaces that dependency with a direct integration
that talks to the YouTube Music tab itself. It does not use Windows global media
controls, and it does not touch ordinary `youtube.com`.

## 2. Scope

V1 delivers phases 1, 2, 3 and 5 of the original build plan:

- connection and detection
- playback controls (play, pause, play/pause, next, previous)
- state synchronisation in both directions
- reliability under refresh, restart and tab churn

Track metadata (title, artist, album, position, duration) is **out of scope for
V1**. It is specified as a follow-up in section 12 so that V1 does not carry the
fragility of DOM scraping before the control path is proven.

## 3. Components

Three components, each with one responsibility.

### 3.1 Content script (`extension/content.js`)

Runs only on `https://music.youtube.com/*`.

Owns the page. It locates the player's `video` element, subscribes to its media
events, and treats `!video.paused && !video.ended` as the single source of truth
for playback state. It executes commands by acting on the page. It reports state
changes to the service worker and receives commands from it.

It has no knowledge of WebSockets, tokens, or Macro Deck.

### 3.2 Service worker (`extension/background.js`)

Owns the connection. It holds the WebSocket to the plugin, authenticates,
reconnects on failure, and keeps itself alive with a periodic ping. It decides
which YouTube Music tab is authoritative when more than one is open, and relays
state upward and commands downward.

It has no knowledge of the YouTube Music DOM.

### 3.3 Macro Deck plugin (`plugin/`)

Owns Macro Deck. It runs the WebSocket server, enforces authentication,
translates received state into Macro Deck variables, and translates Macro Deck
button presses into outgoing commands.

It has no knowledge of the YouTube Music DOM.

## 4. Transport

WebSocket over loopback. The plugin is the server; the extension's service
worker is the client.

| Property | Value |
| --- | --- |
| Bind address | `127.0.0.1` only — never `0.0.0.0` |
| Default port | `8975` (configurable in the plugin and in the extension options) |
| Server library | Fleck |
| Direction | Extension dials out to the plugin |

The extension dials out rather than listening, so startup order does not matter:
whichever process starts second establishes the connection.

### 4.1 Why the WebSocket lives in the service worker

A content script on an `https://` page connecting to a plaintext `ws://` endpoint
sits in mixed-content territory. The service worker's origin is
`chrome-extension://`, which has no such restriction, so the socket lives there
and the content script communicates with it through extension messaging.

### 4.2 Service worker lifetime

A Manifest V3 service worker is terminated after roughly 30 seconds of
inactivity. WebSocket traffic resets that timer, so the worker sends a ping every
20 seconds. Without this the socket dies silently and Macro Deck stops receiving
state updates while appearing connected.

## 5. Security

Binding to `127.0.0.1` is necessary but not sufficient. WebSocket connections are
not subject to CORS, so any web page in any browser can open a socket to a
loopback port. Without authentication, an arbitrary website could read what the
user is listening to and control their playback.

Two checks, both enforced during the handshake, before any message is processed:

1. **Origin allow-list.** The connection's `Origin` header must be a
   `chrome-extension://` origin. When the plugin's allow-list names specific
   extension IDs, the origin must be one of them.

   The list starts empty, because the published extension ID does not exist
   until the extension is published, and an unpacked development build has a
   different ID anyway. An empty list means *any Chrome extension presenting the
   correct token*, and still means *no website, ever*. Once the published ID is
   known it is added to the list and the check narrows to that one extension.
2. **Shared token.** The plugin generates a random token on first run and
   displays it in its Macro Deck configuration dialog. The user pastes it into
   the extension's options page once. The extension sends it in its first
   message; the plugin closes any connection that does not present a matching
   token within 5 seconds.

The token is generated per installation. A token compiled into a publicly
distributed extension would provide no protection.

Nothing is transmitted off the machine. There is no cloud service, no telemetry,
and no listening history leaves the loopback interface.

## 6. Wire protocol

JSON text frames. See `docs/PROTOCOL.md` for the full message catalogue.

Summary:

- Extension → plugin: `hello`, `state`, `ping`, `pong`
- Plugin → extension: `welcome`, `error`, `command`, `ping`, `pong`

The protocol is deliberately small. It carries no versioned negotiation beyond a
version string in `hello`, which the plugin logs but does not enforce in V1.

## 7. State model

### 7.1 Source of truth

The browser is authoritative. The plugin never infers playback state from the
command it just sent. It updates `youtube_music_playing` only when the extension
reports an observed media event.

Sending Play therefore produces two steps: the command goes out, and the variable
changes only once the page confirms playback actually started. If playback fails
to start, the variable correctly stays `false`.

### 7.2 Macro Deck variables

V1 creates and maintains two:

| Variable | Type | Meaning |
| --- | --- | --- |
| `youtube_music_connected` | Bool | A YouTube Music tab is present and reporting |
| `youtube_music_playing` | Bool | The player is actually playing right now |

`youtube_music_connected` is `true` only when an authenticated extension is
connected **and** it reports at least one YouTube Music tab. An extension
connected with no YouTube Music tab open reports `connected: false`.

### 7.3 Variable ownership

The user created `youtube_music_playing` manually in Macro Deck before this
project began. Whether a plugin can update a user-created variable, or whether
Macro Deck's API forks a separate plugin-owned variable with the same name, must
be verified against plugin API 41 as the first task of implementation. The
verification result is recorded in `docs/NOTES.md`.

If the API requires plugin ownership, the plugin adopts the existing variable if
possible, and otherwise documents the one-time manual step for the user. It never
silently creates a duplicate.

### 7.4 Disconnection

When the socket drops, when the last YouTube Music tab closes, or when the
extension reports no tab, the plugin sets:

```
youtube_music_connected = false
youtube_music_playing   = false
```

Playing is forced false because an unobserved player must not leave a stale
`true` on a Macro Deck button.

## 8. Tab selection

Several YouTube Music tabs may be open. The service worker keeps a registry of
tabs whose content script has reported in, and selects one authoritative tab:

1. If exactly one tab reports `playing: true`, that tab wins.
2. If several report `playing: true`, the most recently activated of those wins.
3. If none is playing, the most recently activated YouTube Music tab wins.

Commands are sent **only** to the authoritative tab. They are never broadcast.

## 9. Playback control

| Command | Implementation |
| --- | --- |
| `play` | `video.play()` on the media element |
| `pause` | `video.pause()` on the media element |
| `play_pause` | Branch on the current media state |
| `next` | Click the player's next-track control |
| `previous` | Click the player's previous-track control |

Play and pause act on the media element directly because that is the most
reliable path and is immune to UI markup changes. Next and previous have no
media-element equivalent and must act on the player controls; those selectors are
isolated in one module (`extension/player.js`) so that a YouTube Music redesign
touches exactly one file.

Controls must work while Chrome is minimised, unfocused or behind another
window. Nothing in this design depends on focus.

## 10. Reconnection and resilience

The service worker reconnects with backoff: 1s, 2s, 4s, capped at 10s, retrying
indefinitely. Every one of the following is handled by the same mechanism:

| Event | Behaviour |
| --- | --- |
| Chrome starts before Macro Deck | Extension retries until the server appears |
| Macro Deck starts before Chrome | Extension connects on browser start |
| YouTube Music refreshed | Content script re-registers; state resent |
| Tab closed | Worker drops it from the registry; may set `connected: false` |
| Tab reopened | Registers as a new tab |
| Connection lost | Backoff retry; plugin clears variables meanwhile |
| Song changes automatically | Media events fire; state resent |
| Macro Deck restarts | Socket closes, extension reconnects |

On every successful connect the extension sends its current full state, so the
plugin never needs to request a resync.

## 11. Acceptance criteria

V1 is complete when all of these pass by manual test:

- Macro Deck detects when YouTube Music is available.
- `youtube_music_connected` changes correctly as tabs open and close.
- `youtube_music_playing` reflects the real player state, not the last command.
- Play, Pause, Play/Pause, Next and Previous all work from a Macro Deck button.
- Controls work while Chrome is unfocused or minimised.
- Pausing directly inside YouTube Music updates Macro Deck.
- Starting playback directly inside YouTube Music updates Macro Deck.
- Reloading YouTube Music reconnects automatically.
- Restarting Chrome reconnects automatically.
- Restarting Macro Deck reconnects automatically.
- Rapid skipping does not desynchronise the variables.
- A song ending and advancing automatically keeps the variables correct.
- Two YouTube Music tabs do not both receive commands.
- No interaction with ordinary `youtube.com`.
- No dependence on WebNowPlaying.
- No dependence on Windows global media controls.
- A connection presenting a wrong token or an unlisted origin is refused.

## 12. Deferred to V2

Track metadata: `youtube_music_title`, `youtube_music_artist`,
`youtube_music_album`, `youtube_music_position`, `youtube_music_duration`.

Note on sources: a content script runs in an isolated world and cannot read
`navigator.mediaSession.metadata` as set by the page. Metadata therefore comes
from the player DOM, or from a script injected into the main world. The original
plan's ordering — Media Session first — is not implementable from a content
script and is corrected here.

Also deferred: shuffle, repeat, like and dislike actions.

## 13. Distribution

### 13.1 Chrome extension

Published to the Chrome Web Store. Consequences that shape the build:

- The published extension ID is fixed, which makes the origin check reliable. An
  unpacked development build has a different ID, so the plugin accepts a
  configurable list of allowed IDs.
- Manifest V3 forbids remotely hosted code. All JavaScript ships in the package.
- The source is plain JavaScript with no build step and no minification, which
  keeps review straightforward.
- A privacy policy URL is required even though no data leaves the machine.
- The listing must state that the companion Macro Deck plugin is required, or
  reviewers will treat the extension as non-functional on install.
- One-time developer registration fee applies; review typically takes days.

Submission happens after V1 passes acceptance testing locally as an unpacked
extension, not before.

### 13.2 Macro Deck plugin

Installed by copying the built folder into:

```
C:\Users\<user>\AppData\Roaming\Macro Deck\plugins\KeystoneDigital.YouTubeMusic\
```

`scripts/deploy.ps1` performs this. Publication to the Macro Deck extension store
is a possible follow-up, not part of V1.

## 14. Build environment

| Item | Value |
| --- | --- |
| Macro Deck | 2.15.1 (`D:\MacroDeck`) |
| Target framework | `net10.0-windows` |
| Plugin API version | 41 |
| Reference assembly | `D:\MacroDeck\Macro Deck 2.dll` |
| SDK | .NET 10 SDK |
| Package ID | `KeystoneDigital.YouTubeMusic` |

The reference to `Macro Deck 2.dll` is compile-time only. The DLL is not
redistributed with the plugin, because Macro Deck already loads its own copy.

## 15. Testing approach

The three components are separable, so each is exercised independently before
integration:

- **Plugin without extension:** `plugin/test/ProtocolTests` drives the server
  directly — it starts the real listener, connects with a `ClientWebSocket`, and
  checks origin rejection, token rejection, the handshake timeout, state
  coercion, command dispatch and client loss. It needs neither Macro Deck nor
  Chrome, and `scripts/build.ps1` runs it on every build.
- **Extension without plugin:** the service worker's reconnect loop is observable
  in the extension's console with no server running.
- **Content script without either:** playback detection and control can be
  verified from the page console.
- **Integration:** the acceptance criteria in section 11, run by hand.
