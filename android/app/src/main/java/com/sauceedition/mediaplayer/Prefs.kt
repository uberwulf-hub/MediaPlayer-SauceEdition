// What the app remembers on the phone: recent files, where each long file was left, looping, shuffling and the
// picture size. Kept in the app's own SharedPreferences, which Android removes with the app.
package com.sauceedition.mediaplayer

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject

class Prefs(context: Context) {
    private val sp = context.applicationContext.getSharedPreferences("settings", Context.MODE_PRIVATE)

    var loop: Loop
        get() = runCatching { Loop.valueOf(sp.getString("loop", null) ?: "OFF") }.getOrDefault(Loop.OFF)
        set(v) = sp.edit().putString("loop", v.name).apply()

    var shuffle: Boolean
        get() = sp.getBoolean("shuffle", false)
        set(v) = sp.edit().putBoolean("shuffle", v).apply()

    /** Index into PlayerActivity's picture sizes (best fit, fill, 16:9, 4:3, original). */
    var scale: Int
        get() = sp.getInt("scale", 0)
        set(v) = sp.edit().putInt("scale", v).apply()

    /** Whether a video carries on as sound when the app is left (music always does). */
    var videoInBackground: Boolean
        get() = sp.getBoolean("videoInBackground", false)
        set(v) = sp.edit().putBoolean("videoInBackground", v).apply()

    /** Leave WhatsApp's videos and songs out of the Videos and Music lists (on unless switched off). */
    var hideChatApps: Boolean
        get() = sp.getBoolean("hideChatApps", true)
        set(v) = sp.edit().putBoolean("hideChatApps", v).apply()

    /** The "keep playing with the screen off" question has been offered once. */
    var batteryAsked: Boolean
        get() = sp.getBoolean("batteryAsked", false)
        set(v) = sp.edit().putBoolean("batteryAsked", v).apply()

    /** The "where Drive is in the picker" tip has been shown. */
    var driveTipShown: Boolean
        get() = sp.getBoolean("driveTipShown", false)
        set(v) = sp.edit().putBoolean("driveTipShown", v).apply()

    // ---- recent files: newest first, at most 30
    fun recent(): List<Item> = readItems("recent")

    fun addRecent(item: Item) {
        val list = recent().filter { it.uri != item.uri }.toMutableList()
        list.add(0, item)
        writeItems("recent", list.take(MAX_RECENT))
    }

    fun removeRecent(uri: String) = writeItems("recent", recent().filter { it.uri != uri })

    fun clearRecent() = writeItems("recent", emptyList())

    // ---- where each long file was left: at most 300, the oldest forgotten first
    fun resumeAt(uri: String): Long = runCatching { JSONObject(sp.getString("resume", "{}")!!).optLong(uri, 0) }.getOrDefault(0)

    fun setResume(uri: String, positionMs: Long) {
        val o = runCatching { JSONObject(sp.getString("resume", "{}")!!) }.getOrDefault(JSONObject())
        o.remove(uri)
        if (positionMs > 0) o.put(uri, positionMs) // JSONObject keeps the order things were put in
        while (o.length() > MAX_RESUME) o.remove(o.keys().next())
        sp.edit().putString("resume", o.toString()).apply()
    }

    private fun readItems(key: String): List<Item> = runCatching {
        val a = JSONArray(sp.getString(key, "[]"))
        (0 until a.length()).map { a.getJSONObject(it) }.map { Item(it.getString("uri"), it.optString("title"), it.optString("subtitle").ifEmpty { null }) }
    }.getOrDefault(emptyList())

    private fun writeItems(key: String, items: List<Item>) {
        val a = JSONArray()
        items.forEach { a.put(JSONObject().put("uri", it.uri).put("title", it.title).putOpt("subtitle", it.subtitle)) }
        sp.edit().putString(key, a.toString()).apply()
    }

    companion object {
        const val MAX_RECENT = 30
        const val MAX_RESUME = 300
    }
}
