package com.sauceedition.mediaplayer

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ImageView
import android.widget.TextView
import androidx.recyclerview.widget.RecyclerView

/** The Recent, Videos and Music lists on the start screen. */
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

    class Holder(v: View) : RecyclerView.ViewHolder(v) {
        val icon: ImageView = v.findViewById(R.id.icon)
        val title: TextView = v.findViewById(R.id.title)
        val detail: TextView = v.findViewById(R.id.detail)
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int) =
        Holder(LayoutInflater.from(parent.context).inflate(R.layout.item_media, parent, false))

    override fun getItemCount() = entries.size

    override fun onBindViewHolder(h: Holder, position: Int) {
        val e = entries[position]
        h.title.text = e.item.title
        h.detail.text = e.detail
        h.detail.visibility = if (e.detail.isEmpty()) View.GONE else View.VISIBLE
        h.icon.setImageResource(
            when {
                MediaKinds.isStreamAddress(e.item.uri) -> R.drawable.ic_link
                e.isVideo -> R.drawable.ic_movie
                else -> R.drawable.ic_music
            }
        )
        h.itemView.setOnClickListener { h.bindingAdapterPosition.takeIf { it >= 0 }?.let(onClick) }
        h.itemView.setOnLongClickListener { h.bindingAdapterPosition.takeIf { it >= 0 }?.let(onLongClick); true }
    }
}
