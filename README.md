# Media Player (Sauce Edition)

A video and music player for Windows and Android that plays nearly anything, with a simple, friendly window. It plays with
**libVLC**, the playback engine of the VLC media player, so the formats VLC plays play here too, with nothing else to
install. It grew out of App Hub's Media Player (which used Windows' own decoders and needed Store extensions for many
formats).

Media Player (Sauce Edition) isn't made by or affiliated with VideoLAN; VLC is a trademark of VideoLAN.

## What it does

- **Plays** files (MP4, MKV, AVI, MOV, WebM, WMV, MPEG and TS, FLV, OGG and many more; MP3, FLAC, AAC, WAV, Opus,
  WMA, ALAC, APE and more), folders (everything in them, in Explorer's order), playlists (.m3u, .m3u8, .pls), network
  streams (http, https, rtsp, rtmp, mms, udp, internet radio), audio CDs, video CDs, and home-made DVDs and Blu-ray
  discs (shop-bought, copy-protected discs can't be played, and disc menus aren't available).
- **Playlist** beside the picture: drag files in or out, reorder by dragging, find, sort, mix up, save as .m3u8; loop
  the whole list or one item, shuffle.
- **Carries on where you left off** in anything longer than three minutes (Home starts over), and remembers recent
  files.
- **Subtitles**: files beside the video load by themselves (same name, the name plus a language like `.en.srt`, or a
  `Subs` folder); add more, pick the track, move them sooner or later (G / H), make them bigger.
- **Sound**: up to 200%, sound tracks, sound devices, stereo modes, an equalizer with presets, evening out loud and
  quiet parts, visualisations for music, sound sooner or later (J / K), adding a separate sound file.
- **Picture**: full screen (the controls hide by themselves), aspect ratio, crop, zoom, deinterlacing, rotate and
  flip, brightness / contrast / colour, snapshots at full size (Pictures\Media Player), always on top, fitting the
  window to the video.
- **More**: speeds from 0.25x to 4x, A-B repeat, frame by frame, jump to a time, chapters and titles, media
  information and statistics, converting or saving a copy in another format, Explorer's "Open with", taskbar buttons,
  the same keyboard shortcuts as VLC (F1 lists them), a minimal view, and a bigger or smaller interface (Ctrl+Plus /
  Minus).
- **Watch online**: YouTube search in the app; YouTube, Netflix and Prime Video play in their own Edge window
  (YouTube only plays in a browser, and Netflix and Prime Video are copy-protected).

## On Android

The Android app (`android\`, Android 7.0 and newer) plays with libVLC too, so the same formats play:

- **Open** files (several at once), a whole folder (in natural order, with a subtitle file of the same name loaded
  beside each video), files from **Google Drive** (the Drive button: Android's file picker, ☰ > Drive) or, from the ⋮
  menu, a network address (http, https, rtsp, rtmp, mms, udp, internet radio, playlists, YouTube links); the
  **Suno** button explains saving Suno songs as MP3 (Suno doesn't let other apps stream them, so they then show in
  Music), opens the Suno app, and plays a Suno link or share when Suno allows it (`SunoLinks.kt` reads the song
  page); the **Videos** and **Music** lists show what's on the phone (it asks to see them the first time); **Recent**
  remembers what was played and where it was left.
- **Open with** and **Share** from other apps (Files, the browser, a chat).
- **Player**: a seek bar, 10-second skips (or a double tap on the left or right), previous and next, loop (the list
  or one), mix up, the playlist (add, jump, remove), subtitles and sound tracks (and adding a subtitle file), speeds
  from 0.25x to 4x, picture size (best fit, fill, 16:9, 4:3, original) and media information. The controls hide by
  themselves during a video; the screen stays on while one plays.
- **Carries on where you left off** in anything longer than three minutes, as on Windows.
- **Music keeps playing** with the screen off or in another app, with a notification and lock-screen controls
  (headphone buttons work too); it pauses for calls and when headphones come out. A video pauses when the app is
  left, or (if More > Keep playing as sound is ticked) carries on as sound only.

- **Android Auto** (a car's screen, like the Kia Sorento's): Media Player shows up as a music app with **Recent** and
  **Songs** to browse, the usual playback controls, and "Hey Google, play <song or artist> on Media Player". Android
  Auto only lists apps from the Play Store unless it's told otherwise, so once on the phone: open Android Auto's
  settings, tap **Version** ten times (yes to developer settings), then ⋮ > **Developer settings** > tick **Unknown
  sources**; then in **Customize launcher**, tick Media Player. `CarLibrary.kt` is what the car browses;
  `CarBrowseTest` (in `app/src/androidTest`) connects the way the car does and plays a song.

Not possible: Amazon Music (its songs are copy-protected and only play in Amazon's app). Not on Android yet: the
equalizer and effects, converting, discs, Watch online's Edge windows and App Hub's old settings.

## Saved things

`%APPDATA%\MediaPlayerSE\settings.json` keeps the volume, recent files, where each file was left, the equalizer and
the other settings. It's saved safely (a temporary file first, the five versions before it kept as
`settings.json.bak1`..`bak5`); a file that can't be read is set aside as `settings.json.unreadable-<time>` and never
saved over, and newer versions' fields are kept. The first start brings across App Hub's Media Player's volume,
recent files, resume points, last folder and YouTube searches from `%APPDATA%\MediaPlayer\settings.json`, which is
only read. Errors go to `%LOCALAPPDATA%\MediaPlayerSE\errors.log`. Snapshots go to `Pictures\Media Player`.

## Command line

`MediaPlayerSE.exe [files, folders, playlists or addresses...]`; `--enqueue` adds them to the playlist of the copy
already open instead of playing them; `--online` opens Watch online (`--source Netflix|Prime`,
`--online-search "words"`); `--register-open-with` / `--unregister-open-with` (add `--all-users` for everyone, as an
administrator) add or remove it in Explorer's "Open with" (never changing your default apps). A second start hands
its files to the copy already open.

## Building and checking

Needs the .NET 10 SDK. `dotnet build src\MediaPlayerSauce` builds it (libVLC comes from the
`VideoLAN.LibVLC.Windows` NuGet package into `libvlc\win-x64`).

The Android app needs JDK 17 or newer and the Android SDK (Android Studio, or SauceStore's
`tools/Install-AndroidTools.ps1`): open `android` in Android Studio and press Run, or `cd android` and
`./gradlew testDebugUnitTest lintDebug assembleDebug` for the checks, lint and a debug .apk. libVLC comes from
`org.videolan.android:libvlc-all` on Maven Central. Its version comes from `<Version>` in `MediaPlayerSauce.csproj`
(1.2.3 becomes versionCode 10203), so one bump covers both.

`dotnet run --project tests\MediaPlayerSE.Tests -c Release [-- group,group]` runs the checks: files, settings
(including App Hub's old settings), playlist, the engine (real playback of test files made on the spot, with libVLC's
silent outputs), the window off the screen, and the dialogs. Nothing touches your settings, plays a sound or opens a
window on the screen.

`MediaPlayerSE.exe --snapshot <file.png> --scene Welcome|Playing|Music|Playlist|Osd|Notice|Fullscreen|Minimal` draws
the window off the screen with made-up media.

`tools\Make-Icon.ps1` draws the icon.

## Releasing to SauceStore

Releases are built, signed and published by GitHub Actions (the same setup as Bored Games), so they can be started
from anywhere, including a phone.

1. Bump `<Version>` in `src\MediaPlayerSauce\MediaPlayerSauce.csproj` (it sets the Windows version and Android's
   `versionName`/`versionCode`).
2. Write what changed in `release-notes\v<version>.md`, one change per line. SauceStore's Version history tags lines
   by their first word: "Added …" (New), "Fixed …" (Fixed), "Known issue: …" (Known bug). Put the same changes in
   `CHANGELOG.md` under `## [<version>] - <date>` (it goes inside the Windows zip).
3. Commit and push to main, then on GitHub go to **Actions → Release → Run workflow** and type the version (for
   example `v0.3.0`). `.\tools\Release.ps1` does the same from a PC.

The workflow checks the version matches the project, runs the Windows checks and the Android checks, publishes the
self-contained 64-bit Windows build (one MediaPlayerSE.exe plus the libvlc folder), checks it starts, zips it as
`MediaPlayerSE-windows.zip`, builds and signs `MediaPlayerSE-android.apk`, and creates the release. SauceStore picks
it up within about 10 minutes.

**Android signing key** (the same as Sahuarita Smash): GitHub made it on the first release (0.3.0). The repository is
public since 2026-11-15 (free Actions minutes), so the key can't stay in the Actions cache, where other people's pull
requests can reach it. Once, after adding the `BACKUP_PASSPHRASE` repository secret (a long random passphrase from a
password manager), run **Actions → Keep the Android key in the repository**: it locks the key with that passphrase,
commits it to `keys/` (the locked file is also the backup) and deletes the cached copy. Every release unlocks it on
GitHub's machine only. Always sign with this key: Android only installs an update signed with the same key.

**Windows:** the app is unsigned, as before.

`"rightsConfirmed": true` in `saucestore.json` is your statement that the release passed SauceStore's
`docs/CONTENT-POLICY.md`; the workflow refuses to run when it's false.

`.github/workflows/build.yml` runs the checks on every push and builds a debug .apk and the Windows app folder for
quick testing (download them from the run's *Artifacts*). Don't publish those; use the Release workflow.

## Layout

```
src\MediaPlayerSauce\
  App.xaml(.cs)            start-up, one copy at a time, --snapshot
  Core\                    settings (and App Hub's old ones), playlist, files and times, safe saving, Open with
  Engine\Player.cs         libVLC: playing, tracks, sound, picture, equalizer, rebuilding with new start-up options
  Online\                  YouTube search and Edge windows
  UI\                      the window (MainWindow.*), the dialogs (Dialogs.*), the theme, snapshots
tests\MediaPlayerSE.Tests  the checks
android\                   the Android app (Kotlin): PlaybackService (libVLC, the notification, playing on with the
                           screen off), MainActivity (start screen), PlayerActivity (the player), MediaKinds and
                           Playlist (plain Kotlin, with unit tests in app\src\test)
release-notes\             what each release changed, for SauceStore's Version history
tools\                     Release.ps1 (starts the Release workflow), Make-Icon.ps1
licenses\                  LGPL-2.1, GPL-2.0, .NET's MIT licence (copied into every release)
```

## Not tried yet

On Android: a real phone. The app was run on an Android 11 emulator (start screen, lists, a video playing and
carrying on where it was left, music with its cover and the notification after leaving the app, Open with), but not
the notification's own buttons, Share, folders, network addresses, subtitles, rotation or a real phone's speed.

Everything by hand on a real screen: the picture itself (only off-screen pictures and the silent engine were
checked), sound through real speakers, dragging and dropping, full screen on a second monitor, media keys, the
taskbar buttons, real discs in a real drive, network streams, and the Edge windows.
