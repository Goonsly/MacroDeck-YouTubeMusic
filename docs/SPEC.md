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

V1 delivered phases 1, 2, 3 and 5 of the original build plan:

- connection and detection
- playback controls (play, pause, play/pause, next, previous)
- state synchronisation in both directions
- reliability under refresh, restart and tab churn

All of it passed acceptance testing on 2026-09-20.

**V1.1 adds** shuffle, repeat and sound, once the control path had been proven:

- shuffle toggle
- repeat: a cycle action plus three explicit modes
- volume up, volume down, mute
- four new variables: volume, muted, shuffle, repeat

Track metadata (title, artist, album, position, duration) remains **out of
scope**, for the same reason as before: it is the most DOM-dependent part and
buys the least. Section 12 keeps the design for it.

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

Six, in two tiers of reliability:

| Variable | Type | Meaning | Source |
| --- | --- | --- | --- |
| `youtube_music_connected` | Bool | A YouTube Music tab is present and reporting | the extension itself |
| `youtube_music_playing` | Bool | The player is actually playing right now | media element |
| `youtube_music_volume` | Integer | Player volume, 0–100 | media element |
| `youtube_music_muted` | Bool | Player is muted | media element |
| `youtube_music_shuffle` | Bool | Shuffle is on | player controls |
| `youtube_music_repeat` | String | `off`, `all` or `one` | player controls |

The first four come from the media element and the extension's own bookkeeping,
so they are as reliable as the connection. The last two are read from YouTube
Music's markup and are the ones a redesign can break.

**A value that cannot be read is not written.** The extension sends null, and the
plugin leaves that variable at its last value rather than writing a misleading
one — a stale volume reading beats a sudden `0`, and a stale shuffle state beats
a false `off`.

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
youtube_music_muted     = false
```

These three are forced, because an unobserved player must not leave a stale
`true` on a Macro Deck button. Volume, shuffle and repeat keep their last values:
they describe settings rather than activity, and blanking them would be inventing
a state nobody reported.

## 8. Tab selection

Several YouTube Music tabs may be open. The service worker keeps a registry of
tabs whose content script has reported in, and selects one authoritative tab:

1. If exactly one tab reports `playing: true`, that tab wins.
2. If several report `playing: true`, the most recently activated of those wins.
3. If none is playing, the most recently activated YouTube Music tab wins.

Commands are sent **only** to the authoritative tab. They are never broadcast.

## 9. Player control

| Command | Implementation |
| --- | --- |
| `play` | `video.play()` on the media element |
| `pause` | `video.pause()` on the media element |
| `play_pause` | Branch on the current media state |
| `next` | Click the player's next-track control |
| `previous` | Click the player's previous-track control |
| `volume_up` / `volume_down` | `video.volume` ± 0.05, clamped to 0–1 |
| `mute` | Toggle `video.muted` |
| `shuffle` | Click the player's shuffle control |
| `repeat` | Click the player's repeat control once |
| `repeat_off` / `repeat_all` / `repeat_one` | Read the mode, then click until it matches |

Anything the media element can do, it does: play, pause, volume and mute are
immune to markup changes. Raising the volume on a muted player unmutes it, which
is what every other media control does.

The rest must act on YouTube Music's own controls, and those selectors are
isolated in one module (`extension/player.js`) so a redesign touches exactly one
file.

The explicit repeat modes need to know where they are starting, so they read the
mode first and click at most twice — the cycle is three long. If the mode cannot
be read they do nothing and log it, rather than clicking blindly and landing
somewhere random. The plain `repeat` cycle action needs no such knowledge and
keeps working regardless.

Mute applies to the YouTube Music tab only. It does not touch the Windows
mixer, so it cannot silence anything else the user is doing.

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

### 11.1 V1.1 additions

- Shuffle toggles, and `youtube_music_shuffle` follows it.
- Repeat cycles off → all → one, and `youtube_music_repeat` follows it.
- Repeat Off, Repeat All and Repeat One each land on their mode from any
  starting point.
- Volume Up and Volume Down move `youtube_music_volume` by 5 per press and stop
  at 0 and 100.
- Mute toggles `youtube_music_muted` and silences YouTube Music only.
- Raising the volume while muted unmutes.
- Changing shuffle, repeat or volume inside YouTube Music updates Macro Deck
  within about two seconds.
- If shuffle or repeat cannot be read, playback, volume and mute keep working.

## 12. Deferred

Track metadata: `youtube_music_title`, `youtube_music_artist`,
`youtube_music_album`, `youtube_music_position`, `youtube_music_duration`.

Note on sources: a content script runs in an isolated world and cannot read
`navigator.mediaSession.metadata` as set by the page. Metadata therefore comes
from the player DOM, or from a script injected into the main world. The original
plan's ordering — Media Session first — is not implementable from a content
script and is corrected here.

Also still deferred: like and dislike. Shuffle and repeat shipped in V1.1.

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
