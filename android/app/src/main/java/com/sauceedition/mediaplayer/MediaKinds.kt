// Which files are media, names to show, natural order ("2" before "10"), times as text and when to remember where
// something was left. Plain Kotlin (no Android), so the unit tests check it on any computer.
package com.sauceedition.mediaplayer

import java.util.Locale

object MediaKinds {
    val VIDEO = setOf(
        "3g2", "3gp", "amv", "asf", "avi", "divx", "dv", "f4v", "flv", "h264", "hevc", "m1v", "m2t", "m2ts", "m2v",
        "m4v", "mkv", "mov", "mp2v", "mp4", "mpe", "mpeg", "mpg", "mpv2", "mts", "mxf", "nsv", "nuv", "ogm", "ogv", "qt",
        "rm", "rmvb", "tod", "ts", "tts", "vob", "vro", "webm", "wmv", "wtv", "y4m"
    )
    val AUDIO = setOf(
        "aac", "ac3", "aif", "aifc", "aiff", "alac", "amr", "ape", "au", "caf", "dts", "eac3", "flac", "m4a", "m4b",
        "m4p", "mid", "midi", "mka", "mod", "mp1", "mp2", "mp3", "mpa", "mpc", "oga", "ogg", "opus", "ra", "s3m", "spx",
        "tta", "voc", "w64", "wav", "wma", "wv", "xm"
    )
    val SUBTITLES = setOf("srt", "vtt", "ass", "ssa", "sub", "idx", "smi", "sami", "mpl", "usf", "ttml", "dfxp")
    val PLAYLISTS = setOf("m3u", "m3u8", "pls")
    val STREAM_SCHEMES = setOf("http", "https", "rtsp", "rtmp", "rtmps", "mms", "mmsh", "udp", "rtp", "ftp", "smb")

    fun extension(name: String?): String {
        val n = name ?: return ""
        val clean = n.substringBefore('?').substringBefore('#')
        val slash = clean.lastIndexOf('/')
        val dot = clean.lastIndexOf('.')
        return if (dot > slash && dot < clean.length - 1) clean.substring(dot + 1).lowercase(Locale.ROOT) else ""
    }

    fun isVideo(name: String?) = extension(name) in VIDEO
    fun isAudio(name: String?) = extension(name) in AUDIO
    fun isMedia(name: String?) = extension(name).let { it in VIDEO || it in AUDIO }
    fun isSubtitle(name: String?) = extension(name) in SUBTITLES
    fun isPlaylist(name: String?) = extension(name) in PLAYLISTS

    /** True for a network address that can be played ("https://...", "rtsp://..."), not for files. */
    fun isStreamAddress(text: String?): Boolean {
        val t = text?.trim() ?: return false
        val i = t.indexOf("://")
        if (i < 2) return false
        return t.substring(0, i).lowercase(Locale.ROOT) in STREAM_SCHEMES && t.length > i + 3
    }

    /** What a person typed into "Open an address": adds https:// to "example.com/radio.mp3"; null if it isn't one. */
    fun normaliseAddress(text: String?): String? {
        val t = text?.trim().orEmpty()
        if (t.isEmpty() || t.any { it.isWhitespace() }) return null
        if (isStreamAddress(t)) return t
        if (t.contains("://")) return null
        val host = t.substringBefore('/')
        return if (host.contains('.') && !host.startsWith('.') && !host.endsWith('.')) "https://$t" else null
    }

    /** The name to show: a file name without its extension, or the last part of an address (else its host). */
    fun displayName(nameOrAddress: String?): String {
        val s = nameOrAddress?.trim().orEmpty()
        if (s.isEmpty()) return ""
        if (s.contains("://")) {
            val path = s.substringAfter("://").substringBefore('?').substringBefore('#')
            val host = path.substringBefore('/')
            val last = decode(path.trimEnd('/').substringAfterLast('/', ""))
            return if (last.isNotEmpty() && last.contains('.') && last != host) last else path.trimEnd('/')
        }
        val name = s.substringAfterLast('/')
        val dot = name.lastIndexOf('.')
        return if (dot > 0 && extension(name).isNotEmpty()) name.substring(0, dot) else name
    }

    private fun decode(s: String): String = try {
        java.net.URLDecoder.decode(s.replace("+", "%2B"), "UTF-8")
    } catch (e: IllegalArgumentException) {
        s
    }

    /** Explorer-like order: digits compare as numbers ("Track 2" before "Track 10"), letters ignore case. */
    val naturalOrder: Comparator<String> = Comparator { a, b -> naturalCompare(a, b) }

    fun naturalCompare(a: String, b: String): Int {
        var i = 0
        var j = 0
        while (i < a.length && j < b.length) {
            val ca = a[i]
            val cb = b[j]
            if (ca.isDigit() && cb.isDigit()) {
                val si = i
                val sj = j
                while (i < a.length && a[i].isDigit()) i++
                while (j < b.length && b[j].isDigit()) j++
                val na = a.substring(si, i).trimStart('0')
                val nb = b.substring(sj, j).trimStart('0')
                if (na.length != nb.length) return na.length - nb.length
                val c = na.compareTo(nb)
                if (c != 0) return c
            } else {
                val c = ca.lowercaseChar().compareTo(cb.lowercaseChar())
                if (c != 0) return c
                i++
                j++
            }
        }
        val rest = (a.length - i) - (b.length - j)
        return if (rest != 0) rest else a.compareTo(b)
    }

    /** 0:05, 3:07, 1:02:03. Unknown or negative times show as 0:00. */
    fun formatTime(ms: Long): String {
        val total = if (ms <= 0) 0 else ms / 1000
        val h = total / 3600
        val m = (total % 3600) / 60
        val s = total % 60
        return if (h > 0) String.format(Locale.ROOT, "%d:%02d:%02d", h, m, s)
        else String.format(Locale.ROOT, "%d:%02d", m, s)
    }

    /** Things longer than three minutes carry on where they were left, as on Windows. */
    const val RESUME_MIN_LENGTH = 3 * 60 * 1000L

    /** Where to start next time: the position, or 0 for "from the start" (short, barely begun or nearly finished). */
    fun resumePoint(positionMs: Long, lengthMs: Long): Long {
        if (lengthMs < RESUME_MIN_LENGTH) return 0
        if (positionMs < 10_000 || positionMs > lengthMs - 15_000) return 0
        return positionMs
    }
}
