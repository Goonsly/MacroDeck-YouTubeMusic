# Chrome Web Store listing

Everything the submission form asks for, written out. Copy and paste when
uploading `artifacts\youtube-music-for-macro-deck-extension.zip`.

Submit only after the acceptance criteria in [SPEC.md](SPEC.md) §11 pass.

## Before you start

- A Google account and a one-time USD 5 developer registration fee.
- Review usually takes a few days. Extensions that touch a loopback port are
  sometimes reviewed more slowly than average.
- The privacy policy must be live at a public URL before submitting. The copy in
  this repository is public and serves that purpose; enabling GitHub Pages would
  give it a prettier URL but is not required.

## Item details

**Name**

```
YouTube Music for Macro Deck
```

**Short description** (132 characters maximum)

```
Control YouTube Music from your Macro Deck, with button state that follows the real player.
```

**Category:** Workflow & Planning

**Language:** English

**Detailed description**

```
Control YouTube Music from a Macro Deck button, and see the real playback state
on the button itself.

Requires the companion Macro Deck plugin. The extension does nothing on its own.
Get the plugin here: https://github.com/Goonsly/MacroDeck-YouTubeMusic

WHAT IT DOES

- Play, Pause, Play/Pause, Next and Previous, from your deck
- Macro Deck variables that follow the browser, not the last button press
- Pause the music in the browser and the deck button updates immediately
- Works while Chrome is minimised, unfocused or behind another window
- Reconnects on its own after a tab reload, a browser restart or a Macro Deck
  restart

HOW IT WORKS

The extension talks to the Macro Deck plugin over 127.0.0.1, your own computer's
loopback address. Nothing is sent to the internet. There is no account, no cloud
service and no telemetry.

The connection is protected by a token that the plugin generates on your machine
and you paste into this extension once, and by an origin check that refuses any
website.

SETUP

1. Install the Macro Deck plugin from the link above.
2. In Macro Deck: Extensions, YouTube Music, Configure. Copy the token.
3. Open this extension's options and paste the token.
4. Open music.youtube.com and press play.

NOT AFFILIATED with YouTube, Google or Macro Deck.
```

## Privacy practices

**Single purpose**

```
Bridge YouTube Music playback control and playback state to the Macro Deck
application running on the same computer.
```

**Permission justifications**

| Permission | Justification |
| --- | --- |
| `host_permissions: https://music.youtube.com/*` | The extension reads the playback state of the YouTube Music player and operates its playback controls. It runs on no other site. |
| `storage` | Stores the port number and the connection token the user enters, so they do not have to re-enter them. Both stay on the device. |
| `alarms` | Re-establishes the local connection to Macro Deck after Chrome suspends the extension's service worker for inactivity. |

**Remote code:** No. All JavaScript is included in the package.

**Data usage declarations:** none of the categories apply. Select no collection
for every category, then certify:

- Not being sold to third parties
- Not being used or transferred for purposes unrelated to the item's single purpose
- Not being used or transferred to determine creditworthiness or for lending

**Privacy policy URL**

```
https://github.com/Goonsly/MacroDeck-YouTubeMusic/blob/master/docs/PRIVACY.md
```

## Graphics needed

| Asset | Size | Status |
| --- | --- | --- |
| Store icon | 128×128 PNG | `extension/icons/icon128.png` |
| Screenshot 1 | 1280×800 or 640×400 | Needed: a Macro Deck deck with the YouTube Music buttons |
| Screenshot 2 | 1280×800 or 640×400 | Needed: the extension options page with the token filled in — blur or fake the token |
| Small promo tile | 440×280 | Optional |

At least one screenshot is required. Two is better: one showing the deck, one
showing setup.

## After it is published

1. Copy the published extension ID from the Web Store dashboard.
2. Put it in the plugin's **Allowed Chrome extension IDs** field, and set it as
   the default in `PluginSettings` so new installs are locked down out of the box.
3. Copy the item's public key from the dashboard into the unpacked build's
   `manifest.json` as `"key"`, so the development build and the published build
   share one ID.
4. Update the install guide: users install from the Web Store, not unpacked.
