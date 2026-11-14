// Asking Android (and Samsung's battery saver) to let Media Player keep running with the screen off, so a sleep sound
// or a long playlist isn't stopped to save battery.
package com.sauceedition.mediaplayer

import android.annotation.SuppressLint
import android.app.Activity
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.PowerManager
import android.provider.Settings
import android.widget.Toast
import androidx.appcompat.app.AlertDialog

object Battery {
    fun unrestricted(context: Context): Boolean =
        (context.getSystemService(Context.POWER_SERVICE) as PowerManager).isIgnoringBatteryOptimizations(context.packageName)

    /** From the menu: explain, then show Android's "allow" question (or say it's already allowed). */
    fun ask(activity: Activity) {
        if (unrestricted(activity)) {
            Toast.makeText(activity, R.string.battery_ok, Toast.LENGTH_LONG).show()
            return
        }
        AlertDialog.Builder(activity)
            .setTitle(R.string.battery_title)
            .setMessage(R.string.battery_text)
            .setPositiveButton(R.string.continue_) { _, _ -> openSetting(activity) }
            .setNegativeButton(R.string.cancel, null)
            .show()
    }

    /** Once, the first time something is set to loop (a sleep sound): offer it unless it's already allowed. */
    fun offerOnce(activity: Activity) {
        val prefs = Prefs(activity)
        if (prefs.batteryAsked || unrestricted(activity)) return
        prefs.batteryAsked = true
        ask(activity)
    }

    @SuppressLint("BatteryLife") // a media player that must not be stopped mid-song is what this is for
    private fun openSetting(activity: Activity) {
        val direct = Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:" + activity.packageName))
        try {
            activity.startActivity(direct)
        } catch (e: Exception) {
            runCatching {
                activity.startActivity(Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:" + activity.packageName)))
            }
        }
    }
}
