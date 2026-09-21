# Privacy policy

**Youtube Music Controller for Macro Deck**

Last updated: 2026-09-20

## What this extension does

It reads whether the YouTube Music player in your browser is playing or paused,
and sends that to the Macro Deck application running on the same computer, so a
Macro Deck button can show the right icon. It also receives play, pause, next and
previous commands from Macro Deck and applies them to the YouTube Music tab.

## What is collected

Nothing is collected.

The only information the extension handles is:

- whether a YouTube Music tab is open
- whether that tab is currently playing

Both are sent only to `127.0.0.1`, the loopback address of your own computer, and
only to the Macro Deck plugin that accompanies this extension.

## What is not done

- No data is sent to any server on the internet.
- No data is sent to the developer.
- No analytics, telemetry or crash reporting.
- No listening history, account information, cookies or page content is read,
  stored or transmitted.
- Nothing is sold or shared with third parties, because nothing is collected.

## Storage

The extension stores two settings in Chrome's local extension storage on your
computer: the port number and the connection token used to reach the Macro Deck
plugin. These never leave the device.

## Permissions

| Permission | Why |
| --- | --- |
| `https://music.youtube.com/*` | Read the player state and operate the player controls |
| `storage` | Remember the port and token you entered |
| `alarms` | Re-establish the local connection if Chrome suspends the extension |

## Contact

Raise an issue in the project repository.
