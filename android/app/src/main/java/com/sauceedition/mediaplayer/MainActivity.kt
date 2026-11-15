// The start screen: open files, a folder or an address; Recent, the phone's Videos and Music; and a bar for what's
// playing, which leads back to the player.
package com.sauceedition.mediaplayer

import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.net.Uri
import android.os.Bundle
import android.os.IBinder
import android.text.InputType
import android.view.View
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.ImageButton
import android.widget.ImageView
import android.widget.TextView
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.appcompat.widget.PopupMenu
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import java.util.concurrent.Executors

class MainActivity : AppCompatActivity(), PlaybackService.Listener {

    private enum class Tab { RECENT, VIDEOS, MUSIC }

    private lateinit var prefs: Prefs
    private lateinit var adapter: MediaAdapter
    private lateinit var emptyBox: View
    private lateinit var emptyText: TextView
    private lateinit var allowButton: View
    private lateinit var nowBar: View
    private var tab = Tab.RECENT
    private var service: PlaybackService? = null
    private var pendingPlay: (() -> Unit)? = null
    private val background = Executors.newSingleThreadExecutor()
    private var loadToken = 0

    private val connection = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName?, binder: IBinder?) {
            val s = (binder as PlaybackService.LocalBinder).service
            service = s
            s.addListener(this@MainActivity)
            pendingPlay?.invoke()
            pendingPlay = null
        }

        override fun onServiceDisconnected(name: ComponentName?) {
            service = null
        }
    }

    private val pickFiles = registerForActivityResult(ActivityResultContracts.OpenMultipleDocuments()) { uris ->
        if (uris.isNullOrEmpty()) return@registerForActivityResult
        uris.forEach { keepAccess(it) }
        val named = uris.map { it to DeviceMedia.fileNameOf(this, it) }
        // a subtitle file picked along with a video goes with it
        val subs = named.filter { MediaKinds.isSubtitle(it.second) }
        val items = named.filter { !MediaKinds.isSubtitle(it.second) }.map { (uri, file) ->
            val base = MediaKinds.displayName(file)
            val sub = subs.firstOrNull { DeviceMedia.matchesSubtitle(base, it.second) }
            Item(uri.toString(), base, sub?.first?.toString())
        }
        if (items.isNotEmpty()) play(items, 0)
    }

    private val pickFolder = registerForActivityResult(ActivityResultContracts.OpenDocumentTree()) { tree ->
        if (tree == null) return@registerForActivityResult
        keepAccess(tree)
        Toast.makeText(this, R.string.loading, Toast.LENGTH_SHORT).show()
        background.execute {
            val items = runCatching { DeviceMedia.folder(this, tree) }.getOrDefault(emptyList())
            runOnUiThread {
                if (items.isEmpty()) Toast.makeText(this, R.string.no_media_in_folder, Toast.LENGTH_LONG).show()
                else play(items, 0)
            }
        }
    }

    private val askPermission = registerForActivityResult(ActivityResultContracts.RequestPermission()) { load() }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)
        prefs = Prefs(this)
        val root = findViewById<View>(R.id.root)
        ViewCompat.setOnApplyWindowInsetsListener(root) { v, insets ->
            val bars = insets.getInsets(WindowInsetsCompat.Type.systemBars() or WindowInsetsCompat.Type.displayCutout())
            v.setPadding(bars.left, bars.top, bars.right, bars.bottom)
            WindowInsetsCompat.CONSUMED
        }

        adapter = MediaAdapter(::onEntryClick, ::onEntryLongClick)
        findViewById<RecyclerView>(R.id.list).apply {
            layoutManager = LinearLayoutManager(this@MainActivity)
            adapter = this@MainActivity.adapter
        }
        emptyBox = findViewById(R.id.empty)
        emptyText = findViewById(R.id.emptyText)
        allowButton = findViewById(R.id.allow)
        nowBar = findViewById(R.id.nowPlaying)

        findViewById<View>(R.id.openFiles).setOnClickListener { pickFiles.launch(PICK_TYPES) }
        findViewById<View>(R.id.openFolder).setOnClickListener { pickFolder.launch(null) }
        findViewById<View>(R.id.openDrive).setOnClickListener { openDrive() }
        findViewById<View>(R.id.openSuno).setOnClickListener { showSunoHelp() }
        findViewById<View>(R.id.tabRecent).setOnClickListener { showTab(Tab.RECENT) }
        findViewById<View>(R.id.tabVideos).setOnClickListener { showTab(Tab.VIDEOS) }
        findViewById<View>(R.id.tabMusic).setOnClickListener { showTab(Tab.MUSIC) }
        findViewById<View>(R.id.menu).setOnClickListener(::showMenu)
        allowButton.setOnClickListener {
            askPermission.launch(if (tab == Tab.VIDEOS) DeviceMedia.videoPermission() else DeviceMedia.musicPermission())
        }
        nowBar.setOnClickListener { startActivity(Intent(this, PlayerActivity::class.java)) }
        findViewById<View>(R.id.nowPlay).setOnClickListener { service?.togglePlay() }
        findViewById<View>(R.id.nowNext).setOnClickListener { service?.next() }

        tab = savedInstanceState?.getString("tab")?.let { runCatching { Tab.valueOf(it) }.getOrNull() } ?: Tab.RECENT
        showTab(tab)
    }

    override fun onSaveInstanceState(outState: Bundle) {
        super.onSaveInstanceState(outState)
        outState.putString("tab", tab.name)
    }

    override fun onStart() {
        super.onStart()
        bindService(Intent(this, PlaybackService::class.java), connection, Context.BIND_AUTO_CREATE)
        if (tab == Tab.RECENT) load() // resume points may have moved
    }

    override fun onStop() {
        service?.removeListener(this)
        service = null
        unbindService(connection)
        super.onStop()
    }

    override fun onDestroy() {
        background.shutdownNow()
        adapter.close()
        super.onDestroy()
    }

    // ---- lists

    private fun showTab(t: Tab) {
        tab = t
        findViewById<View>(R.id.tabRecent).isSelected = t == Tab.RECENT
        findViewById<View>(R.id.tabVideos).isSelected = t == Tab.VIDEOS
        findViewById<View>(R.id.tabMusic).isSelected = t == Tab.MUSIC
        load()
    }

    private fun load() {
        val t = tab
        val token = ++loadToken
        if (t == Tab.RECENT) {
            show(recentEntries(), getString(R.string.nothing_recent), false)
            return
        }
        val permission = if (t == Tab.VIDEOS) DeviceMedia.videoPermission() else DeviceMedia.musicPermission()
        if (!DeviceMedia.allowed(this, permission)) {
            show(emptyList(), getString(if (t == Tab.VIDEOS) R.string.allow_videos else R.string.allow_music), true)
            return
        }
        show(emptyList(), getString(R.string.loading), false)
        background.execute {
            val entries = runCatching { if (t == Tab.VIDEOS) DeviceMedia.videos(this) else DeviceMedia.music(this) }
                .getOrDefault(emptyList())
            runOnUiThread {
                if (token == loadToken && !isDestroyed) {
                    show(entries, getString(if (t == Tab.VIDEOS) R.string.no_videos else R.string.no_music), false)
                }
            }
        }
    }

    private fun recentEntries() = prefs.recent().map { item ->
        val left = prefs.resumeAt(item.uri)
        val detail = when {
            left > 0 -> getString(R.string.left_at, MediaKinds.formatTime(left))
            MediaKinds.isStreamAddress(item.uri) -> item.uri
            else -> ""
        }
        val uri = Uri.parse(item.uri)
        Entry(item, detail, !item.uri.contains("audio"), uri.takeIf { it.authority == "media" })
    }

    private fun show(entries: List<Entry>, emptyMessage: String, showAllow: Boolean) {
        adapter.entries = entries
        emptyBox.visibility = if (entries.isEmpty()) View.VISIBLE else View.GONE
        emptyText.text = emptyMessage
        allowButton.visibility = if (showAllow) View.VISIBLE else View.GONE
    }

    private fun onEntryClick(position: Int) {
        val entries = adapter.entries
        when (tab) {
            // songs and videos play on through their list from the one tapped (Next goes down the list); a recent
            // item plays on its own
            Tab.MUSIC, Tab.VIDEOS -> play(entries.map { it.item }, position)
            else -> play(listOf(entries[position].item), 0)
        }
    }

    private fun onEntryLongClick(position: Int) {
        if (tab != Tab.RECENT) return
        val item = adapter.entries[position].item
        AlertDialog.Builder(this)
            .setTitle(item.title)
            .setItems(arrayOf(getString(R.string.remove_recent))) { _, _ ->
                prefs.removeRecent(item.uri)
                load()
            }
            .show()
    }

    // ---- opening

    private fun play(items: List<Item>, start: Int) {
        // started as well as bound, so music carries on when this screen closes
        startService(Intent(this, PlaybackService::class.java))
        val go = {
            service?.playList(items, start)
            startActivity(Intent(this, PlayerActivity::class.java))
        }
        if (service != null) go() else pendingPlay = go
    }

    private fun askAddress() {
        val input = EditText(this).apply {
            hint = getString(R.string.address_hint)
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI
            isSingleLine = true
        }
        val box = FrameLayout(this).apply {
            val pad = (20 * resources.displayMetrics.density).toInt()
            setPadding(pad, pad / 2, pad, 0)
            addView(input)
        }
        val dialog = AlertDialog.Builder(this)
            .setTitle(R.string.address_title)
            .setView(box)
            .setPositiveButton(R.string.open, null)
            .setNegativeButton(R.string.cancel, null)
            .create()
        dialog.setOnShowListener {
            dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener {
                val address = MediaKinds.normaliseAddress(input.text.toString())
                if (address == null) {
                    input.error = getString(R.string.address_bad)
                } else {
                    dialog.dismiss()
                    if (SunoLinks.isSunoLink(address)) SunoUi.open(this, address, { play(listOf(it), 0) })
                    else play(listOf(Item(address, MediaKinds.displayName(address))), 0)
                }
            }
        }
        dialog.show()
    }

    /** Google Drive is in Android's file picker (☰ > Drive); the first time, say where. */
    private fun openDrive() {
        if (prefs.driveTipShown) {
            pickFiles.launch(PICK_TYPES)
            return
        }
        AlertDialog.Builder(this)
            .setTitle(R.string.drive_tip_title)
            .setMessage(R.string.drive_tip)
            .setPositiveButton(R.string.got_it) { _, _ ->
                prefs.driveTipShown = true
                pickFiles.launch(PICK_TYPES)
            }
            .setNegativeButton(R.string.cancel, null)
            .show()
    }

    private fun showSunoHelp() {
        AlertDialog.Builder(this)
            .setTitle(R.string.suno_title)
            .setMessage(R.string.suno_help)
            .setPositiveButton(R.string.open_suno_app) { _, _ -> SunoUi.openSuno(this) }
            .setNeutralButton(R.string.paste_link) { _, _ -> askAddress() }
            .setNegativeButton(R.string.show_music) { _, _ -> showTab(Tab.MUSIC) }
            .show()
    }

    private fun keepAccess(uri: Uri) {
        // so Recent can open it again after the phone restarts
        runCatching { contentResolver.takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION) }
    }

    private fun showMenu(anchor: View) {
        val menu = PopupMenu(this, anchor)
        menu.menu.add(0, 4, 0, R.string.open_address)
        val bg = menu.menu.add(0, 1, 1, R.string.preferences).setCheckable(true).setChecked(prefs.videoInBackground)
        val hide = menu.menu.add(0, 5, 2, R.string.hide_whatsapp).setCheckable(true).setChecked(prefs.hideChatApps)
        menu.menu.add(0, 6, 2, R.string.play_screen_off)
        menu.menu.add(0, 2, 2, R.string.clear_recent)
        menu.menu.add(0, 3, 3, R.string.about)
        menu.setOnMenuItemClickListener {
            when (it.itemId) {
                1 -> { prefs.videoInBackground = !bg.isChecked }
                2 -> { prefs.clearRecent(); load() }
                4 -> askAddress()
                5 -> { prefs.hideChatApps = !hide.isChecked; load() }
                6 -> Battery.ask(this)
                3 -> AlertDialog.Builder(this)
                    .setTitle(R.string.app_name)
                    .setMessage(getString(R.string.about_text, BuildConfig.VERSION_NAME))
                    .setPositiveButton(R.string.close, null)
                    .show()
            }
            true
        }
        menu.show()
    }

    // ---- the now-playing bar

    override fun onChanged() {
        val s = service ?: return
        val item = s.current
        if (item == null || s.ended) {
            nowBar.visibility = View.GONE
            return
        }
        nowBar.visibility = View.VISIBLE
        findViewById<TextView>(R.id.nowTitle).text = s.title
        findViewById<TextView>(R.id.nowDetail).text = s.artist ?: if (s.isPlaying) getString(R.string.play) else getString(R.string.pause)
        findViewById<ImageView>(R.id.nowIcon).setImageResource(if (s.hasVideo) R.drawable.ic_movie else R.drawable.ic_music)
        findViewById<ImageButton>(R.id.nowPlay).apply {
            setImageResource(if (s.isPlaying) R.drawable.ic_pause else R.drawable.ic_play)
            contentDescription = getString(if (s.isPlaying) R.string.pause else R.string.play)
        }
    }

    override fun onError(message: String) {
        Toast.makeText(this, message, Toast.LENGTH_LONG).show()
    }

    companion object {
        val PICK_TYPES = arrayOf(
            "video/*", "audio/*", "application/ogg", "application/octet-stream", "application/x-matroska",
            "application/vnd.apple.mpegurl", "application/x-mpegurl", "audio/x-scpls", "application/x-subrip", "text/*"
        )
    }
}
