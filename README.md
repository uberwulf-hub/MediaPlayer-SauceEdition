# Media Player (Sauce Edition)

A video and music player for Windows that plays nearly anything, with a simple, friendly window. It plays with
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

`dotnet run --project tests\MediaPlayerSE.Tests -c Release [-- group,group]` runs the checks: files, settings
(including App Hub's old settings), playlist, the engine (real playback of test files made on the spot, with libVLC's
silent outputs), the window off the screen, and the dialogs. Nothing touches your settings, plays a sound or opens a
window on the screen.

`MediaPlayerSE.exe --snapshot <file.png> --scene Welcome|Playing|Music|Playlist|Osd|Notice|Fullscreen|Minimal` draws
the window off the screen with made-up media.

`tools\Release.ps1 -Version X.Y.Z` runs the checks, publishes a self-contained 64-bit build, zips it as
`MediaPlayerSE-windows.zip` and makes the GitHub release that SauceStore lists (`-WhatIf` builds the zip only).
`tools\Make-Icon.ps1` draws the icon.

## Layout

```
src\MediaPlayerSauce\
  App.xaml(.cs)            start-up, one copy at a time, --snapshot
  Core\                    settings (and App Hub's old ones), playlist, files and times, safe saving, Open with
  Engine\Player.cs         libVLC: playing, tracks, sound, picture, equalizer, rebuilding with new start-up options
  Online\                  YouTube search and Edge windows
  UI\                      the window (MainWindow.*), the dialogs (Dialogs.*), the theme, snapshots
tests\MediaPlayerSE.Tests  the checks
tools\                     Release.ps1, Make-Icon.ps1
licenses\                  LGPL-2.1, GPL-2.0, .NET's MIT licence (copied into every release)
```

## Not tried yet

Everything by hand on a real screen: the picture itself (only off-screen pictures and the silent engine were
checked), sound through real speakers, dragging and dropping, full screen on a second monitor, media keys, the
taskbar buttons, real discs in a real drive, network streams, and the Edge windows.
