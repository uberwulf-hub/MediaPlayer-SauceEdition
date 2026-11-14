// Finding things to play on the phone: its videos and music (Android's media library), the files in a folder the
// person picked, and the name of a file another app handed over.
package com.sauceedition.mediaplayer

import android.Manifest
import android.content.ContentUris
import android.content.Context
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.provider.DocumentsContract
import android.provider.MediaStore
import android.provider.OpenableColumns
import androidx.core.content.ContextCompat

/** A row in the Videos or Music list: the item, and a line under its name (length, artist). */
data class Entry(val item: Item, val detail: String, val isVideo: Boolean)

object DeviceMedia {

    fun videoPermission(): String =
        if (Build.VERSION.SDK_INT >= 33) Manifest.permission.READ_MEDIA_VIDEO else Manifest.permission.READ_EXTERNAL_STORAGE

    fun musicPermission(): String =
        if (Build.VERSION.SDK_INT >= 33) Manifest.permission.READ_MEDIA_AUDIO else Manifest.permission.READ_EXTERNAL_STORAGE

    fun allowed(context: Context, permission: String) =
        ContextCompat.checkSelfPermission(context, permission) == PackageManager.PERMISSION_GRANTED

    /** The phone's songs, or none when Media Player hasn't been allowed to see them (Android Auto can't ask). */
    fun musicEntries(context: Context): List<Entry> =
        if (allowed(context, musicPermission())) music(context) else emptyList()

    /** Every video, newest first. */
    fun videos(context: Context): List<Entry> {
        val out = ArrayList<Entry>()
        val cols = arrayOf(MediaStore.Video.Media._ID, MediaStore.Video.Media.DISPLAY_NAME, MediaStore.Video.Media.DURATION)
        context.contentResolver.query(
            MediaStore.Video.Media.EXTERNAL_CONTENT_URI, cols, null, null, MediaStore.Video.Media.DATE_ADDED + " DESC"
        )?.use { c ->
            while (c.moveToNext()) {
                val uri = ContentUris.withAppendedId(MediaStore.Video.Media.EXTERNAL_CONTENT_URI, c.getLong(0))
                val name = c.getString(1) ?: continue
                out += Entry(Item(uri.toString(), MediaKinds.displayName(name)), MediaKinds.formatTime(c.getLong(2)), true)
            }
        }
        return out
    }

    /** Every song (not ringtones or recordings), by artist then album then track. */
    fun music(context: Context): List<Entry> {
        val out = ArrayList<Entry>()
        val cols = arrayOf(
            MediaStore.Audio.Media._ID, MediaStore.Audio.Media.TITLE, MediaStore.Audio.Media.ARTIST,
            MediaStore.Audio.Media.DURATION, MediaStore.Audio.Media.DISPLAY_NAME
        )
        context.contentResolver.query(
            MediaStore.Audio.Media.EXTERNAL_CONTENT_URI, cols, MediaStore.Audio.Media.IS_MUSIC + " != 0", null,
            "${MediaStore.Audio.Media.ARTIST} COLLATE NOCASE, ${MediaStore.Audio.Media.ALBUM} COLLATE NOCASE, ${MediaStore.Audio.Media.TRACK}"
        )?.use { c ->
            while (c.moveToNext()) {
                val uri = ContentUris.withAppendedId(MediaStore.Audio.Media.EXTERNAL_CONTENT_URI, c.getLong(0))
                val title = c.getString(1)?.takeIf { it.isNotBlank() } ?: MediaKinds.displayName(c.getString(4))
                val artist = c.getString(2)?.takeIf { it.isNotBlank() && it != MediaStore.UNKNOWN_STRING }
                val time = MediaKinds.formatTime(c.getLong(3))
                out += Entry(Item(uri.toString(), title), if (artist != null) "$artist · $time" else time, false)
            }
        }
        return out
    }

    /** Every video and song in a folder picked with Android's folder picker and its subfolders, in natural order
     * (files of a folder first, then each subfolder), with a subtitle file of the same name beside each video. */
    fun folder(context: Context, tree: Uri, max: Int = 5000): List<Item> {
        val out = ArrayList<Item>()
        walk(context, tree, DocumentsContract.getTreeDocumentId(tree), out, max, 0)
        return out
    }

    private class Doc(val id: String, val name: String, val isDir: Boolean)

    private fun walk(context: Context, tree: Uri, docId: String, out: MutableList<Item>, max: Int, depth: Int) {
        if (depth > 8 || out.size >= max) return
        val children = DocumentsContract.buildChildDocumentsUriUsingTree(tree, docId)
        val docs = ArrayList<Doc>()
        try {
            context.contentResolver.query(
                children,
                arrayOf(DocumentsContract.Document.COLUMN_DOCUMENT_ID, DocumentsContract.Document.COLUMN_DISPLAY_NAME, DocumentsContract.Document.COLUMN_MIME_TYPE),
                null, null, null
            )?.use { c ->
                while (c.moveToNext()) {
                    val name = c.getString(1) ?: continue
                    if (name.startsWith(".")) continue
                    docs += Doc(c.getString(0), name, c.getString(2) == DocumentsContract.Document.MIME_TYPE_DIR)
                }
            }
        } catch (e: Exception) {
            return
        }
        val byName = { d: Doc -> d.name }
        val files = docs.filter { !it.isDir }.sortedWith(compareBy(MediaKinds.naturalOrder, byName))
        val subtitles = files.filter { MediaKinds.isSubtitle(it.name) }
        for (f in files) {
            if (out.size >= max) return
            if (!MediaKinds.isMedia(f.name)) continue
            val base = MediaKinds.displayName(f.name)
            val sub = if (MediaKinds.isVideo(f.name)) subtitles.firstOrNull { matchesSubtitle(base, it.name) } else null
            out += Item(
                DocumentsContract.buildDocumentUriUsingTree(tree, f.id).toString(), base,
                sub?.let { DocumentsContract.buildDocumentUriUsingTree(tree, it.id).toString() }
            )
        }
        for (d in docs.filter { it.isDir }.sortedWith(compareBy(MediaKinds.naturalOrder, byName))) {
            walk(context, tree, d.id, out, max, depth + 1)
        }
    }

    /** "Film.srt", "Film.en.srt" and "Film.English.srt" go with "Film.mkv". */
    fun matchesSubtitle(videoBase: String, subtitleName: String): Boolean {
        val b = MediaKinds.displayName(subtitleName)
        return b.equals(videoBase, ignoreCase = true) ||
            (b.startsWith("$videoBase.", ignoreCase = true) && b.length - videoBase.length in 3..12 && !b.substring(videoBase.length + 1).contains('.'))
    }

    /** The file name (with its extension) of something another app or the picker handed over. */
    fun fileNameOf(context: Context, uri: Uri): String {
        if (uri.scheme == "content") {
            runCatching {
                context.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { c ->
                    if (c.moveToFirst()) c.getString(0)?.takeIf { it.isNotBlank() }?.let { return it }
                }
            }
        }
        return Uri.decode(uri.toString()).trimEnd('/').substringAfterLast('/')
    }

    /** The name to show for it: the file name without its extension. */
    fun nameOf(context: Context, uri: Uri): String = MediaKinds.displayName(fileNameOf(context, uri))
}
