# Changelog

What changes in each version, in plain words. Newest first.

## [Unreleased]

## [0.4.1] - 2026-11-14

### Fixed

- Windows: the first start after an update no longer looks like nothing is happening for half a minute. A small "Getting Media Player ready" window shows straight away while the playback engine makes its list of parts (once per update), then the player opens.

## [0.4.0] - 2026-11-14

### Added

- Android: a Drive button picks songs and videos from Google Drive (Android's file picker, with a tip the first time on where Drive is).
- Android Auto: Media Player is a music app on the car's screen (Recent, Songs, playback controls, and "play <song> on Media Player"). As it isn't from the Play Store, Android Auto needs Unknown sources turned on once in its developer settings.
- Android: a Suno button explains saving Suno songs to the phone (they then show in Music), opens the Suno app, and plays Suno links when Suno allows it; sharing a Suno link to Media Player works too. The web and radio link box moved to the ⋮ menu.

### Fixed

- Android: opening a file from Google Drive no longer freezes the player while it downloads.

- Android: the player's buttons (loop, previous, back, play, forward, next, mix up) all fit on narrower phones such as the Galaxy S23; loop and mix up were cut off at the edges.

## [0.3.0] - 2026-11-14

### Added

- Media Player for Android: the same playback engine as on Windows, so nearly any video, music file or stream plays. Open files, a whole folder or an address, or pick from the phone's Videos and Music; Open with and Share from other apps.
- On Android: carries on where you left off, music keeps playing with the screen off (with a notification and lock-screen controls), and the player has subtitles, sound tracks, speeds, picture sizes, loop, mix up and a playlist.

## [0.2.1] - 2026-11-10

### Fixed

- Starts much faster: the first start now saves a list of the playback engine's parts, so later starts no longer open
  all of its 300-odd files (which Windows' virus checker could stretch to 20 seconds or more).

## [0.2.0] - 2026-11-08

### Added

- Opening one song or video brings the others in its folder into the playlist, so Next and Previous go through the folder (Preferences can turn this off).
- "Make it my default player": the start page asks once, and Help or Preferences open Windows' own Default apps page for Media Player and show, as you choose, which kinds of files now open with it.

### Fixed

- Clicking a point on the seek bar now plays from there: the bar used to jump back to where it was playing when you let go.
- A rare crash when asking the player whether it was playing while it was still opening a file.

## [0.1.0] - 2026-11-08

### Added

- Media Player (Sauce Edition) is its own app now, with its own download in SauceStore. It plays with VLC's own playback engine, so nearly any video, music file, stream or home-made disc plays, with nothing else to install.
- A simple dark window: big controls, a seek bar that shows the time under the pointer, a playlist beside the picture (drag to reorder, find, sort, save), full screen with controls that hide by themselves, a minimal view, and the same keyboard shortcuts as VLC.
- Carries on where you left off in long files, and remembers your volume, recent files and settings. The first start brings across App Hub's Media Player's recent files, resume points, volume and YouTube searches (App Hub's own settings are left as they are).
- Subtitles beside a video load by themselves (also from a Subs folder), and you can add more, move them sooner or later, and make them bigger.
- Sound up to 200%, sound tracks, sound devices, stereo modes, evening out loud and quiet parts, and music visualisations; picture aspect, crop, zoom, deinterlacing, rotate and flip, snapshots, always on top, A-B repeat, frame by frame and speeds from 0.25x to 4x.
- An equalizer with presets, picture controls (brightness, contrast, colour), sound and subtitle timing, and media information with live statistics.
- Save a copy in another format: MP4, MP3, M4A, FLAC, OGG or OGV, without stopping what you're watching.
- Play a disc: audio CDs, video CDs, and home-made DVDs and Blu-ray discs (no disc menus; shop-bought, copy-protected discs can't be played).
- Watch online: search YouTube in the app; YouTube, Netflix and Prime Video open in their own Edge window.
- Preferences, keyboard shortcuts (F1), About, and Explorer's "Open with" (it never changes your default apps).
