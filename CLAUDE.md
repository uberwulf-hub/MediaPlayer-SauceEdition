# Media Player (Sauce Edition): notes for Claude

Read [README.md](README.md) first. Windows app (WPF + LibVLCSharp, `src/`, checks in `tests/`) and Android app
(Kotlin + libVLC for Android, `android/`), listed in SauceStore through the latest GitHub release (`saucestore.json`:
Windows zip + Android APK; keep `credits` complete).

- **Releases reach everyone who installed the app: never run the Release workflow unless Andrew asks for that
  release.** Releasing is GitHub Actions only, like Bored Games: bump `<Version>` in
  `src/MediaPlayerSauce/MediaPlayerSauce.csproj` (it also sets Android's versionName / versionCode), write
  `release-notes/v<version>.md` (lines start with "Added", "Fixed" or "Known issue:") and CHANGELOG.md, push to main,
  then run `Release` with `v<version>` on main.
- **Signing is GitHub's** (the same as Sahuarita Smash): `android.yml` signs the apk with, first found, the
  `ANDROID_KEYSTORE_*` secrets, or `keys/mediaplayerse-release.keystore.enc` (the key the first release made, locked
  with the `BACKUP_PASSPHRASE` secret by `store-android-key.yml`, which also deletes the cached copy; it is the backup
  too), or, only while the repository is private, the Actions cache. The repository is public since 2026-11-15: never
  cache, upload or commit an unlocked key, and never change the key (phones would have to uninstall). The Windows zip
  is unsigned. `build.yml` runs the checks and builds test copies on every push; it never releases.
- Android: `./gradlew testDebugUnitTest lintDebug assembleDebug` in `android/` (`-Pemulator` adds x86_64 for an
  emulator); `CarBrowseTest` (androidTest) connects like Android Auto. Windows: `dotnet run --project
  tests/MediaPlayerSE.Tests -c Release` (Windows only; GitHub's Windows build runs it).
