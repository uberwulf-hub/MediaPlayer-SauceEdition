// Suno song links (suno.com/song/<id>, or the suno.com/s/<code> links its Share button makes). Suno only lets other
// apps play a song's audio when the song page gives a real audio address; for most songs it now says "forbidden",
// and then the person is told to download the song as MP3 in Suno instead. The page reading is plain Kotlin and
// unit-tested; fetching it needs the network (call it off the main thread).
package com.sauceedition.mediaplayer

import java.net.HttpURLConnection
import java.net.URL
import java.util.Locale

object SunoLinks {

    /** suno.com, www.suno.com, app.suno.ai (older links) */
    fun isSunoLink(text: String?): Boolean {
        val t = text?.trim()?.lowercase(Locale.ROOT) ?: return false
        if (!t.startsWith("http://") && !t.startsWith("https://")) return false
        val host = t.substringAfter("://").substringBefore('/').substringBefore('?')
        return host == "suno.com" || host.endsWith(".suno.com") || host == "suno.ai" || host.endsWith(".suno.ai")
    }

    sealed class Result {
        data class Playable(val item: Item) : Result()
        /** Suno didn't give the audio (the song is private, or Suno only plays it on its own site and app). */
        data class NotAllowed(val title: String?) : Result()
        object Unreachable : Result()
    }

    /** What a song page says: its title, and its audio address if Suno gives one. */
    data class Page(val title: String?, val audioUrl: String?)

    fun parsePage(html: String): Page {
        val title = Regex("""<meta[^>]+property="og:title"[^>]+content="([^"]*)"""").find(html)?.groupValues?.get(1)
            ?.let(::unescape)?.trim()?.takeIf { it.isNotEmpty() }
        // the page's data holds "audio_url":"https://..." (escaped as \"audio_url\":\"...\" inside a script)
        val audio = Regex("""audio_url\\?"\s*:\s*\\?"(https?:(?:\\/|[^"\\])+)""").findAll(html)
            .map { it.groupValues[1].replace("\\/", "/") }
            .firstOrNull { usableAudio(it) }
            ?: Regex("""<meta[^>]+property="og:audio(?::url)?"[^>]+content="(https?:[^"]+)"""").find(html)
                ?.groupValues?.get(1)?.let(::unescape)?.takeIf { usableAudio(it) }
        return Page(title, audio)
    }

    /** Not Suno's "forbidden" placeholder or its silent filler clip. */
    fun usableAudio(url: String): Boolean {
        val u = url.lowercase(Locale.ROOT)
        return !u.contains("/forbidden") && !u.contains("/sil-") && (u.contains(".mp3") || u.contains(".m4a") || u.contains("audio"))
    }

    private fun unescape(s: String) = s.replace("&amp;", "&").replace("&quot;", "\"").replace("&#x27;", "'")
        .replace("&#39;", "'").replace("&lt;", "<").replace("&gt;", ">")

    /** Reads the song page (following Share's short-link redirect) and says whether it can be played. */
    fun resolve(link: String): Result {
        val (finalUrl, html) = try {
            fetch(link.trim())
        } catch (e: Exception) {
            return Result.Unreachable
        }
        val page = parsePage(html)
        // a short link that's gone lands on Suno's home page: that title isn't a song's
        val title = page.title.takeIf { finalUrl.contains("/song/") }
        val audio = page.audioUrl ?: return Result.NotAllowed(title)
        return Result.Playable(Item(audio, title ?: MediaKinds.displayName(audio)))
    }

    /** The page's final address (after redirects) and its text. */
    private fun fetch(start: String): Pair<String, String> {
        var url = start
        repeat(5) {
            val c = URL(url).openConnection() as HttpURLConnection
            c.instanceFollowRedirects = false
            c.connectTimeout = 10_000
            c.readTimeout = 15_000
            c.setRequestProperty("User-Agent", "Mozilla/5.0 (Linux; Android) MediaPlayerSE")
            try {
                val code = c.responseCode
                if (code in 300..399) {
                    url = URL(URL(url), c.getHeaderField("Location") ?: error("no redirect")).toString()
                    return@repeat
                }
                if (code != 200) error("HTTP $code")
                return url to c.inputStream.bufferedReader().use { it.readText() }
            } finally {
                c.disconnect()
            }
        }
        error("too many redirects")
    }
}
