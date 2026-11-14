// What Android Auto (the car's screen) can browse: Recent and all Songs, as tabs. Each playable item's id is
// "<list>|<address>", so playing one plays on through the list it was chosen from. Music only: the car shows no
// video.
package com.sauceedition.mediaplayer

import android.content.Context
import android.net.Uri
import android.support.v4.media.MediaBrowserCompat.MediaItem
import android.support.v4.media.MediaDescriptionCompat
import java.util.Locale

object CarLibrary {
    const val ROOT = "root"
    const val RECENT = "recent"
    const val SONGS = "songs"
    const val SEARCH = "search:"

    fun children(context: Context, parentId: String): List<MediaItem> = when (parentId) {
        ROOT -> listOf(
            folder(RECENT, context.getString(R.string.tab_recent)),
            folder(SONGS, context.getString(R.string.car_songs)),
        )
        RECENT -> recentMusic(context).map { playable(it, RECENT) }
        SONGS -> songs(context).map { playable(it, SONGS) }.ifEmpty { listOf(notice(context)) }
        else -> emptyList()
    }

    /** The list an id came from, and where the id is in it (-1 if it's gone). */
    fun listFor(context: Context, mediaId: String): Pair<List<Item>, Int> {
        val parent = mediaId.substringBefore('|')
        val uri = mediaId.substringAfter('|')
        val list = when {
            parent == RECENT -> recentMusic(context)
            parent == SONGS -> songs(context)
            parent.startsWith(SEARCH) -> search(context, parent.removePrefix(SEARCH))
            else -> emptyList()
        }
        val i = list.indexOfFirst { it.uri == uri }
        return if (i >= 0) list to i else listOf(Item(uri, MediaKinds.displayName(Uri.decode(uri)))) to 0
    }

    /** Songs whose title, artist or album has every word of the query; all songs for an empty query. */
    fun search(context: Context, query: String): List<Item> {
        val all = DeviceMedia.musicEntries(context)
        val words = query.lowercase(Locale.getDefault()).split(Regex("\\s+")).filter { it.isNotBlank() }
        if (words.isEmpty()) return all.map { it.item }
        return all.filter { e ->
            val text = (e.item.title + " " + e.detail).lowercase(Locale.getDefault())
            words.all { text.contains(it) }
        }.map { it.item }
    }

    private fun songs(context: Context): List<Item> = DeviceMedia.musicEntries(context).map { it.item }

    /** Recent things that are sound: songs, radio and other addresses, not videos. */
    private fun recentMusic(context: Context): List<Item> = Prefs(context).recent().filter { isSound(it.uri) }

    fun isSound(uri: String): Boolean {
        val u = Uri.decode(uri).lowercase(Locale.ROOT)
        return u.contains("/audio/") || u.contains("audio:") || MediaKinds.isAudio(u) || MediaKinds.isStreamAddress(uri)
    }

    fun playable(item: Item, parent: String): MediaItem = MediaItem(
        MediaDescriptionCompat.Builder().setMediaId("$parent|${item.uri}").setTitle(item.title).build(),
        MediaItem.FLAG_PLAYABLE
    )

    private fun folder(id: String, title: String) = MediaItem(
        MediaDescriptionCompat.Builder().setMediaId(id).setTitle(title).build(), MediaItem.FLAG_BROWSABLE
    )

    /** No songs to show (or not allowed to look): say what to do on the phone. */
    private fun notice(context: Context) = MediaItem(
        MediaDescriptionCompat.Builder().setMediaId("notice").setTitle(context.getString(R.string.car_no_songs))
            .setSubtitle(context.getString(R.string.car_no_songs_detail)).build(),
        0
    )
}
