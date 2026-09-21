# Install and first run

Two halves have to be installed: the Macro Deck plugin and the Chrome extension.
Neither does anything without the other.

## 1. Build

Requires the .NET 10 SDK and Macro Deck 2.15 or newer.

```powershell
cd "E:\Projects\Youtube Music Controller"
.\scripts\build.ps1
```

This builds the plugin, runs the protocol tests, and writes a zipped extension to
`artifacts\`.

If Macro Deck is not installed at `D:\MacroDeck`:

```powershell
dotnet build plugin\src\YouTubeMusicPlugin -c Release -p:MacroDeckPath="C:\Path\To\Macro Deck"
```

## 2. Install the Macro Deck plugin

Close Macro Deck first — it holds the plugin DLL open while running.

```powershell
.\scripts\deploy.ps1
```

This copies the build into:

```
%AppData%\Macro Deck\plugins\KeystoneDigital.YouTubeMusic\
```

Start Macro Deck. The plugin appears under **Extensions** as *Youtube Music Controller*.

Macro Deck 2.15 also ships an installer for extension zip files, reachable from
its Extensions view. If your build offers it, pointing it at the release zip
saves extracting by hand. The manual copy above always works.

## 3. Get the token

In Macro Deck: **Extensions → Youtube Music Controller → Configure**.

Press **Copy**. That token is what lets the browser talk to the plugin, and it is
generated fresh for this installation.

## 4. Load the Chrome extension

While the extension is unpublished, load it unpacked:

1. Open `chrome://extensions`.
2. Turn on **Developer mode** (top right).
3. Click **Load unpacked**.
4. Select `E:\Projects\Youtube Music Controller\extension`.

Chrome derives an unpacked extension's ID from that folder's path, so the ID
stays the same as long as the folder is not moved. Moving or renaming the folder
gives the extension a new ID: re-load it from the new path and paste the token
again.

## 5. Point the extension at Macro Deck

1. On the extension's card, click **Details → Extension options**.
2. Paste the token.
3. Leave the port at `8975` unless it was changed in Macro Deck.
4. Click **Save**.

Open `https://music.youtube.com` and play something. In Macro Deck's variables
view, `youtube_music_connected`, `youtube_music_playing`, `youtube_music_volume`,
`youtube_music_muted`, `youtube_music_shuffle` and `youtube_music_repeat` should
now follow the player.

## 6. Optional: lock the connection to this extension

The plugin already refuses every website. To narrow it further to this one
extension:

1. Copy the extension's ID from `chrome://extensions`.
2. Paste it into **Allowed Chrome extension IDs** in the plugin's configuration.
3. Save.

## 6b. Optional: install the icon pack

The pack lives at
[Goonsly/MacroDeck-YoutubeIcons](https://github.com/Goonsly/MacroDeck-YoutubeIcons)
and a submission to the Macro Deck extension store is pending. Once it is
accepted, install it from inside Macro Deck instead of by hand.

Until then, each release here includes `Goonsly.YoutubeIcons-<version>.zip` —
play, pause, play/pause, next and previous at 350×350 in YouTube red. Extract it
into:

```
%AppData%\Macro Deck\iconpacks\Goonsly.YoutubeIcons\
```

Restart Macro Deck. The icons appear under **Youtube Icons** in the icon
selector. They are cosmetic; any icon pack works just as well.

## 7. Build a button

In Macro Deck's button editor:

- **On Press → Youtube Music Controller → Play / Pause**
- For the icon, bind the button state to the variable `youtube_music_playing`:
  `False` shows the play icon, `True` shows the pause icon.

Thirteen actions are available, grouped by what they touch:

| Group | Actions |
| --- | --- |
| Playback | Play / Pause, Play, Pause, Next, Previous |
| Queue | Shuffle, Repeat, Repeat Off, Repeat All, Repeat One |
| Sound | Volume Up, Volume Down, Mute |

**Repeat** cycles off → all → one, like clicking it in YouTube Music. The three
explicit ones land on that mode from wherever you are.

**Volume** moves 5% per press. **Mute** silences YouTube Music only, not the rest
of Windows.

Buttons worth binding to state as well:

| Button | Bind to | Shows |
| --- | --- | --- |
| Shuffle | `youtube_music_shuffle` | on or off |
| Mute | `youtube_music_muted` | muted or not |
| Repeat | `youtube_music_repeat` | `off`, `all` or `one` — use it as a label rather than a two-state icon |

## If something does not work

| Symptom | Where to look |
| --- | --- |
| Variables never change | Macro Deck log (`%AppData%\Macro Deck\logs`) — look for `Listening on ws://127.0.0.1:8975` |
| Extension shows nothing | `chrome://extensions` → *service worker* → Console |
| `Rejected a connection: wrong token` | Token in the extension options does not match the plugin |
| `Rejected a connection from origin` | The extension ID is not in the allow-list |
| Controls work but state lags | The content script console on the YouTube Music tab |
| Plugin missing after deploy | Macro Deck was running during the copy; close it and deploy again |

### About the existing `youtube_music_playing` variable

This variable was created by hand in Macro Deck before the plugin existed. The
plugin reuses it rather than creating a duplicate, and logs what it found on
startup:

```
Variable 'youtube_music_playing' already exists (creator '...', type '...');
it will be reused, not duplicated.
```

If Macro Deck turns out to refuse writes from a plugin to a variable someone else
created, delete the manual variable once in the variables view and restart Macro
Deck. The plugin recreates it and owns it from then on. Record the outcome in
`docs/NOTES.md`.
