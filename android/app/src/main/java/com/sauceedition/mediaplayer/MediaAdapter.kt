package com.sauceedition.mediaplayer

import android.content.Context
import android.content.res.ColorStateList
import android.graphics.Bitmap
import android.media.MediaMetadataRetriever
import android.net.Uri
import android.os.Build
import android.util.LruCache
import android.util.Size
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ImageView
import android.widget.TextView
import androidx.recyclerview.widget.RecyclerView
import java.util.concurrent.Executors

/** The Recent, Videos and Music lists on the start screen, with a thumbnail of each video and each song's cover. */
class MediaAdapter(
    private val onClick: (Int) -> Unit,
    private val onLongClick: (Int) -> Unit,
) : RecyclerView.Adapter<MediaAdapter.Holder>() {

    var entries: List<Entry> = emptyList()
        set(value) {
            field = value
            @Suppress("NotifyDataSetChanged")
            notifyDataSetChanged()
        }

    private val thumbs = LruCache<String, Bitmap>(120)
    /** Things Android had no picture for, so they aren't asked again. */
    private val noThumb = HashSet<String>()
    private val loader = Executors.newFixedThreadPool(2)

    class Holder(v: View) : RecyclerView.ViewHolder(v) {
        val icon: ImageView = v.findViewById(R.id.icon)
        val title: TextView = v.findViewById(R.id.title)
        val detail: TextView = v.findViewById(R.id.detail)

        init {
            icon.clipToOutline = true // rounded corners for the pictures too
        }
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int) =
        Holder(LayoutInflater.from(parent.context).inflate(R.layout.item_media, parent, false))

    override fun getItemCount() = entries.size

    override fun onBindViewHolder(h: Holder, position: Int) {
        val e = entries[position]
        h.title.text = e.item.title
        h.detail.text = e.detail
        h.detail.visibility = if (e.detail.isEmpty()) View.GONE else View.VISIBLE
        val key = e.thumbnail?.toString()
        h.icon.tag = key
        val cached = key?.let { thumbs.get(it) }
        if (cached != null) showPicture(h.icon, cached) else showSymbol(h.icon, e)
        if (cached == null && key != null && key !in noThumb) {
            val context = h.itemView.context.applicationContext
            val uri = e.thumbnail
            loader.execute {
                val bmp = runCatching { load(context, uri, e.isVideo) }.getOrNull()
                h.icon.post {
                    if (bmp == null) noThumb += key else thumbs.put(key, bmp)
                    if (h.icon.tag == key && bmp != null) showPicture(h.icon, bmp)
                }
            }
        }
        h.itemView.setOnClickListener { h.bindingAdapterPosition.takeIf { it >= 0 }?.let(onClick) }
        h.itemView.setOnLongClickListener { h.bindingAdapterPosition.takeIf { it >= 0 }?.let(onLongClick); true }
    }

    private fun showSymbol(v: ImageView, e: Entry) {
        val pad = (10 * v.resources.displayMetrics.density).toInt()
        v.setPadding(pad, pad, pad, pad)
        v.scaleType = ImageView.ScaleType.FIT_CENTER
        v.imageTintList = ColorStateList.valueOf(v.context.getColor(R.color.accent))
        v.setImageResource(
            when {
                MediaKinds.isStreamAddress(e.item.uri) -> R.drawable.ic_link
                e.isVideo -> R.drawable.ic_movie
                else -> R.drawable.ic_music
            }
        )
    }

    private fun showPicture(v: ImageView, bmp: Bitmap) {
        v.setPadding(0, 0, 0, 0)
        v.scaleType = ImageView.ScaleType.CENTER_CROP
        v.imageTintList = null
        v.setImageBitmap(bmp)
    }

    /** Android's own thumbnail (Android 10+), else a frame of the video or the song's embedded cover. */
    private fun load(context: Context, uri: Uri, video: Boolean): Bitmap? {
        if (Build.VERSION.SDK_INT >= 29) {
            return context.contentResolver.loadThumbnail(uri, Size(192, 192), null)
        }
        val r = MediaMetadataRetriever()
        try {
            r.setDataSource(context, uri)
            val bmp = if (video) r.getFrameAtTime(1_000_000) else r.embeddedPicture?.let {
                android.graphics.BitmapFactory.decodeByteArray(it, 0, it.size, android.graphics.BitmapFactory.Options().apply { inSampleSize = 4 })
            }
            return bmp?.let { Bitmap.createScaledBitmap(it, 192, 192 * it.height / it.width.coerceAtLeast(1), true) }
        } finally {
            r.release()
        }
    }

    fun close() = loader.shutdownNow()
}
