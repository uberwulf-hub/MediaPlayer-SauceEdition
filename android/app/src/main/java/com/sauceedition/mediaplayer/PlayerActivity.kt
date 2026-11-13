// The player: the picture (or, for music, its cover and name), a seek bar, play/pause, previous/next, 10-second
// skips, loop and mix up, subtitles and sound tracks, speed, picture size and the playlist. The controls hide by
// themselves during a video; a tap brings them back, a double tap on the left or right skips 10 seconds.
// Also what other apps open ("Open with" a video, music file or address, or Share to it).
package com.sauceedition.mediaplayer

import android.annotation.SuppressLint
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.net.Uri
import android.os.Bundle
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.view.GestureDetector
import android.view.MotionEvent
import android.view.View
import android.view.WindowManager
import android.widget.AdapterView
import android.widget.ImageButton
import android.widget.ImageView
import android.widget.SeekBar
import android.widget.TextView
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.appcompat.widget.PopupMenu
import androidx.core.content.IntentCompat
import androidx.core.view.ViewCompat
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import org.videolan.libvlc.MediaPlayer
import org.videolan.libvlc.util.VLCVideoLayout
import java.util.Locale

class PlayerActivity : AppCompatActivity(), PlaybackService.Listener {

    private lateinit var prefs: Prefs
    private lateinit var root: View
    private lateinit var video: VLCVideoLayout
    private lateinit var controls: View
    private lateinit var musicPanel: View
    private lateinit var art: ImageView
    private lateinit var titleView: TextView
    private lateinit var timeView: TextView
    private lateinit var lengthView: TextView
    private lateinit var seek: SeekBar
    private lateinit var playPause: ImageButton
    private lateinit var loopButton: ImageButton
    private lateinit var shuffleButton: ImageButton
    private lateinit var osd: TextView
    private lateinit var bufferingView: View

    private var service: PlaybackService? = null
    private var bound = false
    private var opening: List<Item>? = null
    private var seeking = false
    private var controlsShown = true
    private val handler = Handler(Looper.getMainLooper())
    private val hideControls = Runnable { setControls(false) }
    private val hideOsd = Runnable { osd.visibility = View.GONE }

    private val connection = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName?, binder: IBinder?) {
            val s = (binder as PlaybackService.LocalBinder).service
            service = s
            s.attachViews(video)
            s.player.videoScale = SCALES[prefs.scale.coerceIn(SCALES.indices)]
            opening?.let { s.playList(it, 0) }
            opening = null
            if (s.current == null) {
                finish()
                return
            }
            s.addListener(this@PlayerActivity)
            onTime(s.timeMs, s.lengthMs)
        }

        override fun onServiceDisconnected(name: ComponentName?) {
            service = null
        }
    }

    private val pickSubtitle = registerForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri == null) return@registerForActivityResult
        val ok = service?.addSubtitle(uri, select = true) == true
        Toast.makeText(this, if (ok) R.string.subtitles_added else R.string.cant_add_subtitles, Toast.LENGTH_SHORT).show()
    }

    private val pickMore = registerForActivityResult(ActivityResultContracts.OpenMultipleDocuments()) { uris ->
        if (uris.isNullOrEmpty()) return@registerForActivityResult
        uris.forEach { runCatching { contentResolver.takePersistableUriPermission(it, Intent.FLAG_GRANT_READ_URI_PERMISSION) } }
        service?.enqueue(uris.map { Item(it.toString(), DeviceMedia.nameOf(this, it)) })
    }

    @SuppressLint("ClickableViewAccessibility")
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        WindowCompat.setDecorFitsSystemWindows(window, false)
        setContentView(R.layout.activity_player)
        prefs = Prefs(this)
        root = findViewById(R.id.playerRoot)
        video = findViewById(R.id.video)
        controls = findViewById(R.id.controls)
        musicPanel = findViewById(R.id.musicPanel)
        art = findViewById(R.id.art)
        titleView = findViewById(R.id.title)
        timeView = findViewById(R.id.time)
        lengthView = findViewById(R.id.length)
        seek = findViewById(R.id.seek)
        playPause = findViewById(R.id.playPause)
        loopButton = findViewById(R.id.loop)
        shuffleButton = findViewById(R.id.shuffle)
        osd = findViewById(R.id.osd)
        bufferingView = findViewById(R.id.buffering)

        ViewCompat.setOnApplyWindowInsetsListener(controls) { v, insets ->
            val bars = insets.getInsets(WindowInsetsCompat.Type.systemBars() or WindowInsetsCompat.Type.displayCutout())
            v.setPadding(bars.left, bars.top, bars.right, bars.bottom)
            insets
        }

        findViewById<View>(R.id.back).setOnClickListener { finish() }
        playPause.setOnClickListener { service?.togglePlay(); poke() }
        findViewById<View>(R.id.previous).setOnClickListener { service?.previous(); poke() }
        findViewById<View>(R.id.next).setOnClickListener { service?.next(); poke() }
        findViewById<View>(R.id.rewind).setOnClickListener { skip(-10_000) }
        findViewById<View>(R.id.forward).setOnClickListener { skip(10_000) }
        loopButton.setOnClickListener { cycleLoop() }
        shuffleButton.setOnClickListener { toggleShuffle() }
        findViewById<View>(R.id.subtitles).setOnClickListener { chooseTrack(subtitles = true) }
        findViewById<View>(R.id.audioTrack).setOnClickListener { chooseTrack(subtitles = false) }
        findViewById<View>(R.id.playlist).setOnClickListener { showPlaylist() }
        findViewById<View>(R.id.more).setOnClickListener(::showMore)

        seek.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(bar: SeekBar, progress: Int, fromUser: Boolean) {
                if (fromUser) timeView.text = MediaKinds.formatTime(progress.toLong())
            }

            override fun onStartTrackingTouch(bar: SeekBar) {
                seeking = true
                handler.removeCallbacks(hideControls)
            }

            override fun onStopTrackingTouch(bar: SeekBar) {
                seeking = false
                service?.seekTo(bar.progress.toLong())
                poke()
            }
        })

        val gestures = GestureDetector(this, object : GestureDetector.SimpleOnGestureListener() {
            override fun onDown(e: MotionEvent) = true
            override fun onSingleTapConfirmed(e: MotionEvent): Boolean {
                setControls(!controlsShown)
                return true
            }

            override fun onDoubleTap(e: MotionEvent): Boolean {
                val third = root.width / 3f
                when {
                    e.x < third -> skip(-10_000)
                    e.x > 2 * third -> skip(10_000)
                    else -> service?.togglePlay()
                }
                return true
            }
        })
        // a slow press is still a tap: there's nothing to do on a long press here
        gestures.setIsLongpressEnabled(false)
        root.setOnTouchListener { _, e -> gestures.onTouchEvent(e) }

        opening = itemsFrom(intent)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        val items = itemsFrom(intent) ?: return
        val s = service
        if (s != null) {
            startService(Intent(this, PlaybackService::class.java))
            s.playList(items, 0)
        } else opening = items
    }

    /** What another app opened with or shared to Media Player: a file, or an address. */
    private fun itemsFrom(intent: Intent?): List<Item>? {
        val uris = when (intent?.action) {
            Intent.ACTION_VIEW -> listOfNotNull(intent.data)
            Intent.ACTION_SEND -> listOfNotNull(
                IntentCompat.getParcelableExtra(intent, Intent.EXTRA_STREAM, Uri::class.java)
                    ?: intent.getStringExtra(Intent.EXTRA_TEXT)?.let { text ->
                        text.split(Regex("\\s+")).firstNotNullOfOrNull { MediaKinds.normaliseAddress(it) }?.let(Uri::parse)
                    }
            )
            Intent.ACTION_SEND_MULTIPLE ->
                IntentCompat.getParcelableArrayListExtra(intent, Intent.EXTRA_STREAM, Uri::class.java).orEmpty()
            else -> emptyList()
        }
        if (uris.isEmpty()) return null
        // only once: not again when the screen is made again
        setIntent(Intent(this, PlayerActivity::class.java))
        return uris.map { Item(it.toString(), DeviceMedia.nameOf(this, it)) }
    }

    override fun onStart() {
        super.onStart()
        // started as well as bound, so music carries on when this screen closes
        if (opening != null) startService(Intent(this, PlaybackService::class.java))
        bound = bindService(Intent(this, PlaybackService::class.java), connection, Context.BIND_AUTO_CREATE)
    }

    override fun onStop() {
        handler.removeCallbacksAndMessages(null)
        service?.let {
            it.removeListener(this)
            it.leaveVideo(keepSound = prefs.videoInBackground)
            it.detachViews()
        }
        service = null
        if (bound) unbindService(connection)
        bound = false
        super.onStop()
    }

    // ---- showing what's playing

    override fun onChanged() {
        val s = service ?: return
        val playing = s.isPlaying
        playPause.setImageResource(if (playing) R.drawable.ic_pause else R.drawable.ic_play)
        playPause.contentDescription = getString(if (playing) R.string.pause else R.string.play)
        val title = s.title
        titleView.text = title
        musicPanel.visibility = if (s.hasVideo) View.GONE else View.VISIBLE
        if (!s.hasVideo) {
            findViewById<TextView>(R.id.musicTitle).text = title
            findViewById<TextView>(R.id.musicArtist).text = s.artist.orEmpty()
            val bitmap = s.artwork()
            if (bitmap != null) {
                art.setPadding(0, 0, 0, 0)
                art.imageTintList = null
                art.setImageBitmap(bitmap)
            } else {
                val pad = (56 * resources.displayMetrics.density).toInt()
                art.setPadding(pad, pad, pad, pad)
                art.setImageResource(R.drawable.ic_music)
                art.imageTintList = getColorStateList(R.color.accent)
            }
        }
        bufferingView.visibility = if (s.buffering && playing) View.VISIBLE else View.GONE
        loopButton.setImageResource(if (s.loop == Loop.ONE) R.drawable.ic_repeat_one else R.drawable.ic_repeat)
        loopButton.alpha = if (s.loop == Loop.OFF) 0.45f else 1f
        shuffleButton.alpha = if (s.shuffle) 1f else 0.45f
        findViewById<View>(R.id.next).alpha = if (s.playlist.hasNext) 1f else 0.45f
        if (playing && s.hasVideo) window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        else window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        if (s.ended) {
            seek.progress = seek.max
            timeView.text = lengthView.text
        }
        poke()
    }

    override fun onTime(timeMs: Long, lengthMs: Long) {
        if (seeking) return
        val len = lengthMs.coerceAtMost(Int.MAX_VALUE.toLong()).toInt()
        seek.isEnabled = len > 0 && service?.player?.isSeekable == true
        seek.max = len.coerceAtLeast(1)
        seek.progress = timeMs.coerceAtMost(Int.MAX_VALUE.toLong()).toInt()
        timeView.text = MediaKinds.formatTime(timeMs)
        lengthView.text = if (lengthMs > 0) MediaKinds.formatTime(lengthMs) else "–"
    }

    override fun onError(message: String) {
        Toast.makeText(this, message, Toast.LENGTH_LONG).show()
    }

    /** Shows the controls; during a video they hide again after a few seconds. */
    private fun poke() {
        handler.removeCallbacks(hideControls)
        val s = service
        if (!controlsShown) setControls(true)
        if (s != null && s.isPlaying && s.hasVideo && !seeking) handler.postDelayed(hideControls, 3500)
    }

    private fun setControls(show: Boolean) {
        val s = service
        val hide = !show && s != null && s.hasVideo
        controlsShown = !hide
        controls.animate().alpha(if (hide) 0f else 1f).setDuration(180).withEndAction {
            controls.visibility = if (hide) View.INVISIBLE else View.VISIBLE
        }.start()
        if (!hide) controls.visibility = View.VISIBLE
        val bars = WindowInsetsControllerCompat(window, root)
        bars.systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        if (hide) bars.hide(WindowInsetsCompat.Type.systemBars()) else bars.show(WindowInsetsCompat.Type.systemBars())
        if (!hide) {
            handler.removeCallbacks(hideControls)
            if (s != null && s.isPlaying && s.hasVideo) handler.postDelayed(hideControls, 3500)
        }
    }

    private fun showOsd(text: String) {
        osd.text = text
        osd.visibility = View.VISIBLE
        handler.removeCallbacks(hideOsd)
        handler.postDelayed(hideOsd, 900)
    }

    private fun skip(ms: Long) {
        service?.skip(ms)
        showOsd(if (ms < 0) "− ${-ms / 1000} s" else "+ ${ms / 1000} s")
        if (controlsShown) poke()
    }

    private fun cycleLoop() {
        val s = service ?: return
        s.loop = when (s.loop) { Loop.OFF -> Loop.ALL; Loop.ALL -> Loop.ONE; Loop.ONE -> Loop.OFF }
        showOsd(getString(when (s.loop) { Loop.OFF -> R.string.loop_off; Loop.ALL -> R.string.loop_all; Loop.ONE -> R.string.loop_one }))
    }

    private fun toggleShuffle() {
        val s = service ?: return
        s.shuffle = !s.shuffle
        showOsd(getString(if (s.shuffle) R.string.shuffle_on else R.string.shuffle_off))
    }

    // ---- dialogs

    private fun chooseTrack(subtitles: Boolean) {
        val s = service ?: return
        val p = s.player
        val tracks = (if (subtitles) p.spuTracks else p.audioTracks).orEmpty()
        val builder = AlertDialog.Builder(this).setTitle(if (subtitles) R.string.subtitles else R.string.sound_track)
        if (tracks.isEmpty()) {
            builder.setMessage(R.string.no_tracks)
        } else {
            val current = if (subtitles) p.spuTrack else p.audioTrack
            builder.setSingleChoiceItems(tracks.map { it.name }.toTypedArray(), tracks.indexOfFirst { it.id == current }) { d, which ->
                if (subtitles) p.setSpuTrack(tracks[which].id) else p.setAudioTrack(tracks[which].id)
                d.dismiss()
            }
        }
        if (subtitles) builder.setNeutralButton(R.string.add_subtitles) { _, _ -> pickSubtitle.launch(arrayOf("*/*")) }
        builder.setNegativeButton(R.string.close, null).show()
    }

    private fun showPlaylist() {
        val s = service ?: return
        val pl = s.playlist
        val names = pl.items.mapIndexed { i, item -> (if (i == pl.index) "▶  " else "") + item.title }.toTypedArray()
        val dialog = AlertDialog.Builder(this)
            .setTitle(getString(R.string.playlist) + " (" + pl.size + ")")
            .setItems(names) { _, which -> s.jump(which) }
            .setNeutralButton(R.string.add_to_playlist) { _, _ -> pickMore.launch(MainActivity.PICK_TYPES) }
            .setNegativeButton(R.string.close, null)
            .create()
        dialog.show()
        dialog.listView?.apply {
            setSelection((pl.index - 2).coerceAtLeast(0))
            onItemLongClickListener = AdapterView.OnItemLongClickListener { _, _, which, _ ->
                val item = pl.items.getOrNull(which) ?: return@OnItemLongClickListener true
                AlertDialog.Builder(this@PlayerActivity)
                    .setMessage(getString(R.string.remove_from_playlist, item.title))
                    .setPositiveButton(R.string.remove) { _, _ ->
                        dialog.dismiss()
                        s.removeAt(which)
                        if (s.current != null) showPlaylist()
                    }
                    .setNegativeButton(R.string.cancel, null)
                    .show()
                true
            }
        }
    }

    private fun showMore(anchor: View) {
        val menu = PopupMenu(this, anchor)
        menu.menu.add(0, 1, 0, R.string.speed)
        menu.menu.add(0, 2, 1, R.string.picture_size)
        menu.menu.add(0, 3, 2, R.string.add_subtitles)
        menu.menu.add(0, 4, 3, R.string.media_info)
        menu.menu.add(0, 5, 4, R.string.video_in_background).setCheckable(true).setChecked(prefs.videoInBackground)
        menu.setOnMenuItemClickListener {
            when (it.itemId) {
                1 -> chooseSpeed()
                2 -> choosePictureSize()
                3 -> pickSubtitle.launch(arrayOf("*/*"))
                4 -> showInfo()
                5 -> prefs.videoInBackground = !prefs.videoInBackground
            }
            true
        }
        menu.show()
        handler.removeCallbacks(hideControls)
    }

    private fun chooseSpeed() {
        val s = service ?: return
        val names = SPEEDS.map { if (it == 1f) "1× (normal)" else String.format(Locale.ROOT, "%s×", it.toString().removeSuffix(".0")) }
        val current = SPEEDS.indexOfFirst { kotlin.math.abs(it - s.rate) < 0.01f }
        AlertDialog.Builder(this).setTitle(R.string.speed)
            .setSingleChoiceItems(names.toTypedArray(), current) { d, which ->
                s.rate = SPEEDS[which]
                showOsd(names[which])
                d.dismiss()
            }
            .setNegativeButton(R.string.close, null).show()
    }

    private fun choosePictureSize() {
        val s = service ?: return
        AlertDialog.Builder(this).setTitle(R.string.picture_size)
            .setSingleChoiceItems(R.array.picture_sizes, prefs.scale.coerceIn(SCALES.indices)) { d, which ->
                prefs.scale = which
                s.player.videoScale = SCALES[which]
                showOsd(resources.getStringArray(R.array.picture_sizes)[which])
                d.dismiss()
            }
            .setNegativeButton(R.string.close, null).show()
    }

    private fun showInfo() {
        val s = service ?: return
        val item = s.current ?: return
        val lines = listOfNotNull(
            getString(R.string.info_title) to s.title,
            s.artist?.let { getString(R.string.info_artist) to it },
            s.album?.let { getString(R.string.info_album) to it },
            getString(R.string.info_length) to MediaKinds.formatTime(s.lengthMs),
            getString(R.string.info_address) to Uri.decode(item.uri),
        )
        AlertDialog.Builder(this).setTitle(R.string.media_info)
            .setMessage(lines.joinToString("\n\n") { "${it.first}\n${it.second}" })
            .setPositiveButton(R.string.close, null).show()
    }

    companion object {
        val SCALES = arrayOf(
            MediaPlayer.ScaleType.SURFACE_BEST_FIT, MediaPlayer.ScaleType.SURFACE_FILL, MediaPlayer.ScaleType.SURFACE_16_9,
            MediaPlayer.ScaleType.SURFACE_4_3, MediaPlayer.ScaleType.SURFACE_ORIGINAL
        )
        val SPEEDS = floatArrayOf(0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f, 3f, 4f)
    }
}
