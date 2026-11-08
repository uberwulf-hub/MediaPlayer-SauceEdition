# Credits

**Media Player (Sauce Edition)** by Andrew Saulls. The window, its pictures and the icon are made for this app.

It plays media with **libVLC**, the playback engine of the VLC media player, made by VideoLAN. Media Player (Sauce
Edition) isn't made by or affiliated with VideoLAN; VLC is a trademark of VideoLAN.

## Third-party software inside the release

| What | Author | Licence | Where |
|---|---|---|---|
| libVLC 3.0.24 (`libvlc\win-x64`: libvlc.dll, libvlccore.dll and the plugins) | VideoLAN and the VLC authors | libVLC and libvlccore: LGPL-2.1-or-later; some plugins are GPL-2.0-or-later | https://www.videolan.org/vlc/libvlc.html (`licenses\LGPL-2.1.txt`, `licenses\GPL-2.0.txt`) |
| LibVLCSharp and LibVLCSharp.WPF 3.10.2 | VideoLAN and the LibVLCSharp authors | LGPL-2.1 | https://code.videolan.org/videolan/LibVLCSharp (`licenses\LGPL-2.1.txt`) |
| .NET runtime and WPF | .NET Foundation and Contributors | MIT | https://github.com/dotnet/runtime (`licenses\dotnet-LICENSE.txt`) |

The `licenses` folder is copied next to `MediaPlayerSE.exe` in every release.

### libVLC's source

libVLC is used unchanged, as VideoLAN builds it (the NuGet package `VideoLAN.LibVLC.Windows` 3.0.24). Its complete
source is VLC 3.0's, from VideoLAN: https://download.videolan.org/pub/videolan/vlc/ and
https://code.videolan.org/videolan/vlc (branch 3.0.x). libVLC and LibVLCSharp are separate libraries loaded at run
time, so they can be replaced with other builds of the same version.
