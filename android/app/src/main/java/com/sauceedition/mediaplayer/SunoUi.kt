// Opening a Suno link from the start screen, the link box or Share: reads the song page off the main thread, then
// plays it, or explains how to download the song when Suno keeps it to itself.
package com.sauceedition.mediaplayer

import android.app.Activity
import android.content.ActivityNotFoundException
import android.content.Intent
import android.net.Uri
import android.widget.Toast
import androidx.appcompat.app.AlertDialog

object SunoUi {
    fun open(activity: Activity, link: String, onPlayable: (Item) -> Unit, onGiveUp: () -> Unit = {}) {
        Toast.makeText(activity, R.string.loading, Toast.LENGTH_SHORT).show()
        Thread {
            val result = SunoLinks.resolve(link)
            activity.runOnUiThread {
                if (activity.isFinishing || activity.isDestroyed) return@runOnUiThread
                when (result) {
                    is SunoLinks.Result.Playable -> onPlayable(result.item)
                    is SunoLinks.Result.NotAllowed -> AlertDialog.Builder(activity)
                        .setTitle(R.string.suno_not_allowed_title)
                        .setMessage(activity.getString(R.string.suno_not_allowed, result.title ?: activity.getString(R.string.this_song)))
                        .setPositiveButton(R.string.open_suno_app) { _, _ -> openSuno(activity, link) }
                        .setNegativeButton(R.string.close, null)
                        .setOnDismissListener { onGiveUp() }
                        .show()
                    SunoLinks.Result.Unreachable -> {
                        Toast.makeText(activity, R.string.suno_unreachable, Toast.LENGTH_LONG).show()
                        onGiveUp()
                    }
                }
            }
        }.start()
    }

    /** The Suno app when it's installed (at the song, if there is one), else suno.com in the browser. */
    fun openSuno(activity: Activity, link: String? = null) {
        val app = activity.packageManager.getLaunchIntentForPackage(SUNO_PACKAGE)
        val intent = when {
            link != null -> Intent(Intent.ACTION_VIEW, Uri.parse(link)).also { i ->
                if (app != null) i.setPackage(SUNO_PACKAGE)
            }
            app != null -> app
            else -> Intent(Intent.ACTION_VIEW, Uri.parse("https://suno.com/me"))
        }
        try {
            activity.startActivity(intent)
        } catch (e: ActivityNotFoundException) {
            runCatching { activity.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(link ?: "https://suno.com/me"))) }
        }
    }

    private const val SUNO_PACKAGE = "com.suno.android"
}
