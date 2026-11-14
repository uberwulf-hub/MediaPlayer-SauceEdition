// Plays with libVLC (VLC's own playback engine, as on Windows) and keeps playing when the app is left: music carries
// on with the screen off, with a notification and lock-screen controls (a media session) for pause, next and
// previous. The screens bind to it and draw the picture into its player.
package com.sauceedition.mediaplayer

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.ServiceInfo
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.media.AudioManager
import android.media.MediaMetadataRetriever
import android.net.Uri
import android.os.Binder
import android.os.Bundle
import android.os.Process
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.os.ParcelFileDescriptor
import android.support.v4.media.MediaBrowserCompat
import android.support.v4.media.MediaMetadataCompat
import android.support.v4.media.session.MediaSessionCompat
import android.support.v4.media.session.PlaybackStateCompat
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import androidx.media.AudioAttributesCompat
import androidx.media.AudioFocusRequestCompat
import androidx.media.AudioManagerCompat
import androidx.media.MediaBrowserServiceCompat
import org.videolan.libvlc.LibVLC
import org.videolan.libvlc.Media
import org.videolan.libvlc.MediaPlayer
import org.videolan.libvlc.interfaces.IMedia
import org.videolan.libvlc.util.VLCVideoLayout
import java.io.File
import java.util.concurrent.Executors

class PlaybackService : MediaBrowserServiceCompat() {

    interface Listener {
        /** Playing, paused, a new item, a new list, the picture appearing: redraw everything. */
        fun onChanged() {}
        fun onTime(timeMs: Long, lengthMs: Long) {}
        fun onError(message: String) {}
    }

    inner class LocalBinder : Binder() {
        val service: PlaybackService get() = this@PlaybackService
    }

    private val binder = LocalBinder()
    private val main = Handler(Looper.getMainLooper())
    private lateinit var prefs: Prefs
    private lateinit var libVLC: LibVLC
    lateinit var player: MediaPlayer
        private set
    private lateinit var session: MediaSessionCompat
    private lateinit var audio: AudioManager
    private val listeners = LinkedHashSet<Listener>()
    private var openFile: ParcelFileDescriptor? = null
    private var pendingSeek = 0L
    private var foreground = false
    private var bound = false
    /** Android Auto (or another media browser) is connected. */
    private var browsed = false
    private var hiddenVideoTrack = Int.MIN_VALUE
    private var artwork: Bitmap? = null
    private var artworkFor: String? = null

    val playlist = Playlist()
    var hasVideo = false
        private set
    var ended = false
        private set
    var buffering = false
        private set
    val isPlaying: Boolean get() = player.isPlaying
    val current: Item? get() = playlist.current
    val timeMs: Long get() = player.time.coerceAtLeast(0)
    val lengthMs: Long get() = player.length.coerceAtLeast(0)

    override fun onCreate() {
        super.onCreate()
        prefs = Prefs(this)
        audio = getSystemService(Context.AUDIO_SERVICE) as AudioManager
        libVLC = LibVLC(this, arrayListOf("--http-reconnect", "--audio-time-stretch"))
        player = MediaPlayer(libVLC)
        player.setEventListener(::onEvent)
        playlist.loop = prefs.loop
        playlist.shuffle = prefs.shuffle

        session = MediaSessionCompat(this, "MediaPlayerSE").apply {
            setCallback(object : MediaSessionCompat.Callback() {
                override fun onPlay() = if (current == null) playFromSearch("") else play()
                override fun onPlayFromMediaId(mediaId: String?, extras: Bundle?) = playFromMediaId(mediaId ?: "")
                override fun onPlayFromSearch(query: String?, extras: Bundle?) = playFromSearch(query ?: "")
                override fun onPause() = pause()
                override fun onSkipToNext() = next()
                override fun onSkipToPrevious() = previous()
                override fun onSeekTo(pos: Long) = seekTo(pos)
                override fun onFastForward() = skip(10_000)
                override fun onRewind() = skip(-10_000)
                override fun onStop() = stopPlaying()
            })
            setSessionActivity(openPlayerIntent())
        }
        // Android Auto (and other media browsers) control playback through this session
        sessionToken = session.sessionToken

        if (Build.VERSION.SDK_INT >= 26) {
            val channel = NotificationChannel(CHANNEL, getString(R.string.channel_playing), NotificationManager.IMPORTANCE_LOW)
            channel.setShowBadge(false)
            (getSystemService(NOTIFICATION_SERVICE) as NotificationManager).createNotificationChannel(channel)
        }
    }

    // Android calls onBind once for the first screen to bind and onUnbind once when the last one lets go. Android
    // Auto binds separately, as a media browser (with MediaBrowserService's action).
    override fun onBind(intent: Intent?): IBinder? {
        if (intent?.action == SERVICE_INTERFACE) {
            browsed = true
            return super.onBind(intent)
        }
        bound = true
        return binder
    }

    override fun onRebind(intent: Intent?) {
        if (intent?.action == SERVICE_INTERFACE) browsed = true else bound = true
    }

    override fun onUnbind(intent: Intent?): Boolean {
        if (intent?.action == SERVICE_INTERFACE) browsed = false else bound = false
        stopIfIdle()
        return true
    }

    // ---- Android Auto: what the car's screen can browse, and playing from it or by voice

    override fun onGetRoot(clientPackageName: String, clientUid: Int, rootHints: Bundle?): BrowserRoot? {
        // this app, the system, and the car and assistant apps; others don't get the music list
        val ok = clientUid == Process.myUid() || clientUid == Process.SYSTEM_UID || clientPackageName in BROWSERS
        return if (ok) BrowserRoot(CarLibrary.ROOT, null) else null
    }

    override fun onLoadChildren(parentId: String, result: Result<MutableList<MediaBrowserCompat.MediaItem>>) {
        result.detach()
        opener.execute {
            val items = runCatching { CarLibrary.children(this, parentId) }.getOrDefault(emptyList())
            main.post { result.sendResult(items.toMutableList()) }
        }
    }

    override fun onSearch(query: String, extras: Bundle?, result: Result<MutableList<MediaBrowserCompat.MediaItem>>) {
        result.detach()
        opener.execute {
            val items = runCatching { CarLibrary.search(this, query) }.getOrDefault(emptyList())
            main.post { result.sendResult(items.map { CarLibrary.playable(it, CarLibrary.SEARCH + query) }.toMutableList()) }
        }
    }

    private fun playFromMediaId(mediaId: String) {
        opener.execute {
            val (list, index) = runCatching { CarLibrary.listFor(this, mediaId) }.getOrDefault(emptyList<Item>() to -1)
            main.post { if (index >= 0) { keepRunning(); playList(list, index) } }
        }
    }

    /** "Play <song or artist> on Media Player"; an empty search carries on, or plays all the songs. */
    fun playFromSearch(query: String, done: (Boolean) -> Unit = {}) {
        if (query.isBlank() && current != null) {
            keepRunning()
            play()
            done(true)
            return
        }
        opener.execute {
            val list = runCatching { CarLibrary.search(this, query) }.getOrDefault(emptyList())
            main.post {
                if (list.isNotEmpty()) {
                    keepRunning()
                    playList(list, 0)
                }
                done(list.isNotEmpty())
            }
        }
    }

    /** Started (not only bound), so the music carries on when the car lets go of the app. */
    private fun keepRunning() {
        val i = Intent(this, PlaybackService::class.java).setAction(ACTION_FOREGROUND)
        try {
            ContextCompat.startForegroundService(this, i)
        } catch (e: Exception) {
            Log.w(TAG, "can't start in the foreground", e)
            runCatching { startService(i) }
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_TOGGLE -> togglePlay()
            ACTION_NEXT -> next()
            ACTION_PREVIOUS -> previous()
            ACTION_DISMISS -> { if (!isPlaying) stopPlaying() }
            // startForegroundService needs startForeground within seconds, whatever happens next
            ACTION_FOREGROUND -> if (!foreground) {
                try {
                    ServiceCompat.startForeground(
                        this, NOTIFICATION_ID, buildNotification(),
                        if (Build.VERSION.SDK_INT >= 29) ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PLAYBACK else 0
                    )
                    foreground = true
                    postedKey = null
                } catch (e: Exception) {
                    Log.w(TAG, "startForeground refused", e)
                }
                if (!isPlaying && !opening) main.postDelayed({ updateNotification() }, 3000)
            }
        }
        return START_NOT_STICKY
    }

    override fun onTaskRemoved(rootIntent: Intent?) {
        // swiped away from the recent apps: music that's playing carries on, anything else stops
        if (!isPlaying) stopPlaying()
        super.onTaskRemoved(rootIntent)
    }

    override fun onDestroy() {
        saveResume()
        shouldPlay = false
        abandonFocus()
        unregisterNoisy()
        player.setEventListener(null)
        player.stop()
        player.release()
        libVLC.release()
        closeFile()
        session.release()
        tagReader.shutdownNow()
        opener.shutdownNow()
        main.removeCallbacksAndMessages(null)
        super.onDestroy()
    }

    fun addListener(l: Listener) {
        listeners += l
        l.onChanged()
    }

    fun removeListener(l: Listener) {
        listeners -= l
    }

    // ---- what to play

    fun playList(items: List<Item>, start: Int) {
        if (items.isEmpty()) return
        saveResume()
        failures = 0
        playlist.set(items, start)
        startItem(playlist.current)
    }

    fun enqueue(items: List<Item>) {
        val wasEmpty = playlist.size == 0
        playlist.add(items)
        if (wasEmpty) startItem(playlist.current) else changed()
    }

    fun jump(index: Int) {
        saveResume()
        failures = 0
        startItem(playlist.jump(index) ?: return)
    }

    fun removeAt(index: Int) {
        val wasCurrent = index == playlist.index
        playlist.remove(index)
        if (wasCurrent) {
            if (playlist.current != null) startItem(playlist.current) else stopPlaying()
        } else changed()
    }

    /** True while a file is being fetched before it can play (a Google Drive file downloading). */
    var opening = false
        private set
    private var openToken = 0
    private val opener = Executors.newSingleThreadExecutor()

    private fun startItem(item: Item?) {
        skipAfterError?.let { main.removeCallbacks(it) }
        skipAfterError = null
        val token = ++openToken
        if (item == null) {
            opening = false
            stopPlaying()
            return
        }
        val uri = Uri.parse(item.uri)
        if (uri.scheme != "content") {
            opening = false
            playMedia(item, Media(libVLC, uri), null)
            return
        }
        // content:// addresses (what Android's file picker gives) are opened here and handed to libVLC as a file
        // descriptor. Opening one from Google Drive downloads it first, so it happens off the main thread.
        if (uri.authority !in LOCAL_PROVIDERS) {
            saveResume()
            player.stop()
            opening = true
            changed()
        }
        opener.execute {
            val pfd = try {
                contentResolver.openFileDescriptor(uri, "r")
            } catch (e: Exception) {
                Log.w(TAG, "can't open ${item.uri}", e)
                null
            }
            main.post {
                if (token != openToken) {
                    runCatching { pfd?.close() }
                    return@post
                }
                opening = false
                if (pfd == null) error(getString(R.string.cant_open, item.title))
                else playMedia(item, Media(libVLC, pfd.fileDescriptor), pfd)
            }
        }
    }

    private fun playMedia(item: Item, media: Media, pfd: ParcelFileDescriptor?) {
        oldFile = openFile
        openFile = pfd
        media.setHWDecoderEnabled(true, false)
        repeating = playlist.loop == Loop.ONE && !MediaKinds.isStreamAddress(item.uri)
        if (repeating) media.addOption(":input-repeat=65535")
        player.media = media
        media.release()
        closeOldFile()
        hasVideo = false
        ended = false
        hiddenVideoTrack = Int.MIN_VALUE
        if (restartAt >= 0) {
            pendingSeek = restartAt
            forceSeek = true
            restartAt = -1
        } else {
            pendingSeek = prefs.resumeAt(item.uri)
            forceSeek = false
        }
        artwork = null
        artworkFor = null
        readTags(item)
        if (requestFocus()) {
            player.play()
            shouldPlay = true
        }
        item.subtitle?.let { addSubtitle(Uri.parse(it), select = true) }
        prefs.addRecent(item)
        changed()
    }

    /** libVLC can't read content:// addresses for subtitles, so the file is copied into the app's cache first. */
    fun addSubtitle(uri: Uri, select: Boolean): Boolean = try {
        val local = if (uri.scheme == "content") {
            val name = (MediaKinds.extension(uri.lastPathSegment).ifEmpty { "srt" }).let { "subtitle-${System.nanoTime()}.$it" }
            val dir = File(cacheDir, "subtitles").apply { mkdirs() }
            dir.listFiles()?.filter { System.currentTimeMillis() - it.lastModified() > 86_400_000 }?.forEach { it.delete() }
            val f = File(dir, name)
            contentResolver.openInputStream(uri)!!.use { input -> f.outputStream().use { input.copyTo(it) } }
            Uri.fromFile(f)
        } else uri
        player.addSlave(IMedia.Slave.Type.Subtitle, local, select)
    } catch (e: Exception) {
        Log.w(TAG, "can't add subtitles $uri", e)
        false
    }

    private var oldFile: ParcelFileDescriptor? = null

    private fun closeOldFile() {
        runCatching { oldFile?.close() }
        oldFile = null
    }

    private fun closeFile() {
        closeOldFile()
        runCatching { openFile?.close() }
        openFile = null
    }

    // ---- controls

    fun play() {
        if (playlist.current == null) return
        if (ended || !player.hasMedia()) {
            startItem(playlist.current)
            return
        }
        if (requestFocus()) {
            player.play()
            shouldPlay = true
        }
    }

    fun pause() {
        if (player.isPlaying) player.pause()
        shouldPlay = false
        saveResume()
    }

    fun togglePlay() = if (player.isPlaying) pause() else play()

    fun stopPlaying() {
        saveResume()
        shouldPlay = false
        player.stop()
        abandonFocus()
        ended = true
        changed()
        stopForegroundNow(true)
        stopIfIdle()
    }

    fun next() {
        saveResume()
        playlist.next(auto = false)?.let { startItem(it) }
    }

    /** Back to the start of this one if it's been playing more than 3 seconds, as most players do; else the one before. */
    fun previous() {
        if (timeMs > 3000 && !ended) {
            seekTo(0)
            return
        }
        saveResume()
        playlist.previous()?.let { startItem(it) } ?: seekTo(0)
    }

    fun seekTo(ms: Long) {
        if (!player.isSeekable) return
        val len = lengthMs
        player.time = if (len > 0) ms.coerceIn(0, len - 500) else ms.coerceAtLeast(0)
        updateSession()
    }

    fun skip(deltaMs: Long) = seekTo(timeMs + deltaMs)

    var loop: Loop
        get() = playlist.loop
        set(v) {
            playlist.loop = v
            prefs.loop = v
            val c = current
            // libVLC is repeating this file by itself (loop one): to stop repeating it has to be opened again,
            // carrying on from the same moment
            if (repeating && v != Loop.ONE && c != null && !ended) {
                val wasPlaying = player.isPlaying
                restartAt = timeMs
                startItem(c)
                if (!wasPlaying) pauseWhenStarted = true
            }
            changed()
        }

    /** The file now playing was opened with libVLC's own repeat (loop one): seamless, no gap or reopening. */
    private var repeating = false
    /** Open the next item at this moment instead of where it was left (-1: as usual). */
    private var restartAt = -1L
    private var forceSeek = false
    private var pauseWhenStarted = false

    // ---- keeping the phone awake while it plays (screen off, falling asleep to a loop)

    private var wakeLock: android.os.PowerManager.WakeLock? = null
    private var wifiLock: android.net.wifi.WifiManager.WifiLock? = null

    /** True from pressing play until pausing, stopping or the end: the gaps between items count as playing. */
    private var shouldPlay = false
        set(v) {
            field = v
            updateLocks()
        }

    private fun updateLocks() {
        if (shouldPlay) {
            val wl = wakeLock ?: (getSystemService(POWER_SERVICE) as android.os.PowerManager)
                .newWakeLock(android.os.PowerManager.PARTIAL_WAKE_LOCK, "MediaPlayerSE:playing")
                .apply { setReferenceCounted(false) }.also { wakeLock = it }
            if (!wl.isHeld) wl.acquire(12 * 60 * 60 * 1000L) // a safety limit: 12 hours
            val streaming = current?.uri?.let { MediaKinds.isStreamAddress(it) } == true
            if (streaming) {
                @Suppress("DEPRECATION")
                val wf = wifiLock ?: (applicationContext.getSystemService(WIFI_SERVICE) as android.net.wifi.WifiManager)
                    .createWifiLock(android.net.wifi.WifiManager.WIFI_MODE_FULL_HIGH_PERF, "MediaPlayerSE:streaming")
                    .apply { setReferenceCounted(false) }.also { wifiLock = it }
                if (!wf.isHeld) wf.acquire()
            } else wifiLock?.let { if (it.isHeld) it.release() }
        } else {
            wakeLock?.let { if (it.isHeld) it.release() }
            wifiLock?.let { if (it.isHeld) it.release() }
        }
    }

    var shuffle: Boolean
        get() = playlist.shuffle
        set(v) {
            playlist.shuffle = v
            prefs.shuffle = v
            changed()
        }

    var rate: Float
        get() = player.rate
        set(v) {
            player.rate = v
            updateSession()
        }

    // ---- picture

    fun attachViews(layout: VLCVideoLayout) {
        if (hiddenVideoTrack != Int.MIN_VALUE) {
            player.setVideoTrack(hiddenVideoTrack)
            hiddenVideoTrack = Int.MIN_VALUE
        }
        player.attachViews(layout, null, true, false)
    }

    fun detachViews() {
        player.detachViews()
    }

    /** Leaving the app during a video: pause it, or (if Preferences say so) carry on with the sound only. */
    fun leaveVideo(keepSound: Boolean) {
        if (!hasVideo) return
        if (!keepSound) {
            pause()
        } else if (player.isPlaying) {
            hiddenVideoTrack = player.videoTrack
            player.setVideoTrack(-1)
        }
    }

    // ---- libVLC's events (they arrive on the main thread)

    private fun onEvent(e: MediaPlayer.Event) {
        when (e.type) {
            MediaPlayer.Event.Playing -> {
                buffering = false
                failures = 0
                if (pauseWhenStarted) { pauseWhenStarted = false; player.pause(); shouldPlay = false }
                changed()
            }
            MediaPlayer.Event.Paused -> changed()
            MediaPlayer.Event.Stopped -> changed()
            MediaPlayer.Event.Buffering -> {
                val b = e.buffering < 100f
                if (b != buffering) { buffering = b; changed() }
            }
            MediaPlayer.Event.Vout -> {
                val v = e.voutCount > 0
                if (v != hasVideo && hiddenVideoTrack == Int.MIN_VALUE) { hasVideo = v; changed() }
            }
            MediaPlayer.Event.LengthChanged -> {
                if (pendingSeek > 0 && (forceSeek || e.lengthChanged >= MediaKinds.RESUME_MIN_LENGTH) && player.isSeekable) {
                    player.time = pendingSeek.coerceAtMost(e.lengthChanged - 1000)
                }
                pendingSeek = 0
                forceSeek = false
                // libVLC re-estimates the length of some files (MP3s) several times a second: the seek bar gets it
                // with the time; the lock screen only when it really moved
                if (kotlin.math.abs(e.lengthChanged - sessionLength) > 1000) updateSession()
            }
            MediaPlayer.Event.TimeChanged -> {
                val t = e.timeChanged
                val l = lengthMs
                listeners.toList().forEach { it.onTime(t, l) }
            }
            MediaPlayer.Event.ESAdded -> changed() // tracks and titles become known
            MediaPlayer.Event.EndReached -> onEnded()
            MediaPlayer.Event.EncounteredError -> {
                val title = current?.title.orEmpty()
                error(getString(R.string.cant_play, title))
            }
        }
    }

    private fun onEnded() {
        val item = current ?: return
        // A playlist file or a web page (a YouTube link, for example) opens as a list of what's really in it.
        val found = subItems()
        if (found.isNotEmpty()) {
            playlist.replaceCurrent(found)
            startItem(playlist.current)
            return
        }
        prefs.setResume(item.uri, 0)
        val next = playlist.next(auto = true)
        if (next != null) {
            startItem(next)
        } else {
            shouldPlay = false
            ended = true
            abandonFocus()
            changed()
            stopForegroundNow(false)
        }
    }

    private fun subItems(): List<Item> {
        val m = player.media ?: return emptyList()
        try {
            val subs = m.subItems() ?: return emptyList()
            try {
                return (0 until subs.count).mapNotNull { i ->
                    val s = subs.getMediaAt(i) ?: return@mapNotNull null
                    try {
                        val u = s.uri?.toString() ?: return@mapNotNull null
                        Item(u, s.getMeta(IMedia.Meta.Title)?.takeIf { it.isNotBlank() } ?: MediaKinds.displayName(u))
                    } finally {
                        s.release()
                    }
                }
            } finally {
                subs.release()
            }
        } finally {
            m.release()
        }
    }

    private var failures = 0
    private var skipAfterError: Runnable? = null

    private fun error(message: String) {
        listeners.toList().forEach { it.onError(message) }
        // carry on with the next one, as on Windows; stop at the end, or when nothing in the list will play
        failures++
        val before = playlist.index
        val next = if (failures < playlist.size) playlist.next(auto = false) else null
        if (next != null && playlist.index != before) {
            skipAfterError = Runnable { skipAfterError = null; startItem(next) }.also { main.postDelayed(it, 1500) }
        } else {
            shouldPlay = false
            ended = true
            abandonFocus()
            changed()
        }
    }

    // ---- what the media says about itself

    fun meta(which: Int): String? {
        val m = player.media ?: return null
        return try { m.getMeta(which)?.takeIf { it.isNotBlank() } } finally { m.release() }
    }

    /** Tags read by Android's own reader: libVLC can't read them from a content:// file it's handed as a stream. */
    private class Tags(val title: String?, val artist: String?, val album: String?, val art: Bitmap?)

    private var tags: Tags? = null
    private var tagsFor: String? = null
    private val tagReader = Executors.newSingleThreadExecutor()

    private fun readTags(item: Item) {
        tags = null
        tagsFor = item.uri
        if (MediaKinds.isStreamAddress(item.uri)) return
        tagReader.execute {
            val t = runCatching {
                val r = MediaMetadataRetriever()
                try {
                    val u = Uri.parse(item.uri)
                    if (u.scheme == "content") r.setDataSource(this, u) else r.setDataSource(u.path)
                    fun get(key: Int) = r.extractMetadata(key)?.trim()?.takeIf { it.isNotEmpty() }
                    Tags(
                        get(MediaMetadataRetriever.METADATA_KEY_TITLE), get(MediaMetadataRetriever.METADATA_KEY_ARTIST),
                        get(MediaMetadataRetriever.METADATA_KEY_ALBUM), r.embeddedPicture?.let { decodeSmall(it) }
                    )
                } finally {
                    r.release()
                }
            }.getOrNull()
            main.post {
                if (tagsFor == item.uri && t != null) {
                    tags = t
                    changed()
                }
            }
        }
    }

    /** The title to show: for music, the tag's title when the file has one; else the item's own name. */
    val title: String
        get() {
            val c = current ?: return ""
            if (hasVideo) return c.title
            return tags?.title ?: meta(IMedia.Meta.Title)?.takeIf { hasTagTitle(it) } ?: c.title
        }

    private fun hasTagTitle(t: String): Boolean {
        val c = current ?: return false
        // libVLC fills in the file name (or fd://N) when there's no tag
        return !t.startsWith("fd:") && t != c.title && !c.uri.endsWith(t) && !t.contains("://")
    }

    val artist: String? get() = tags?.artist ?: meta(IMedia.Meta.Artist)
    val album: String? get() = tags?.album ?: meta(IMedia.Meta.Album)

    private fun artworkBitmap(): Bitmap? {
        tags?.art?.let { return it }
        val url = meta(IMedia.Meta.ArtworkURL)
        if (url == artworkFor) return artwork
        artworkFor = url
        artwork = url?.let { Uri.parse(it) }?.takeIf { it.scheme == "file" }?.path?.let { p ->
            runCatching { decodeSmall(File(p)) }.getOrNull()
        }
        return artwork
    }

    fun artwork(): Bitmap? = artworkBitmap()

    private fun decodeSmall(f: File): Bitmap? = decodeSmall(f.readBytes())

    private fun decodeSmall(bytes: ByteArray): Bitmap? {
        val o = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(bytes, 0, bytes.size, o)
        var sample = 1
        while (o.outWidth / sample > 1024 || o.outHeight / sample > 1024) sample *= 2
        return BitmapFactory.decodeByteArray(bytes, 0, bytes.size, BitmapFactory.Options().apply { inSampleSize = sample })
    }

    // ---- sound focus: pause for calls and other apps, and when headphones come out

    private var resumeOnGain = false
    private var focusRequest: AudioFocusRequestCompat? = null

    private val focusListener = AudioManager.OnAudioFocusChangeListener { change ->
        when (change) {
            AudioManager.AUDIOFOCUS_LOSS -> { resumeOnGain = false; pause(); abandonFocus() }
            AudioManager.AUDIOFOCUS_LOSS_TRANSIENT -> { resumeOnGain = player.isPlaying; pause() }
            AudioManager.AUDIOFOCUS_LOSS_TRANSIENT_CAN_DUCK -> player.volume = 30
            AudioManager.AUDIOFOCUS_GAIN -> {
                player.volume = 100
                if (resumeOnGain) { resumeOnGain = false; player.play(); shouldPlay = true }
            }
        }
    }

    private fun requestFocus(): Boolean {
        if (focusRequest != null) return true
        val req = AudioFocusRequestCompat.Builder(AudioManagerCompat.AUDIOFOCUS_GAIN)
            .setAudioAttributes(
                AudioAttributesCompat.Builder()
                    .setUsage(AudioAttributesCompat.USAGE_MEDIA)
                    .setContentType(AudioAttributesCompat.CONTENT_TYPE_MOVIE)
                    .build()
            )
            .setOnAudioFocusChangeListener(focusListener, main)
            .setWillPauseWhenDucked(false)
            .build()
        val granted = AudioManagerCompat.requestAudioFocus(audio, req) == AudioManager.AUDIOFOCUS_REQUEST_GRANTED
        if (granted) focusRequest = req
        registerNoisy()
        return granted
    }

    private fun abandonFocus() {
        focusRequest?.let { AudioManagerCompat.abandonAudioFocusRequest(audio, it) }
        focusRequest = null
    }

    private var noisyRegistered = false
    private val noisy = object : BroadcastReceiver() {
        override fun onReceive(c: Context, i: Intent) {
            if (i.action == AudioManager.ACTION_AUDIO_BECOMING_NOISY) pause()
        }
    }

    private fun registerNoisy() {
        if (noisyRegistered) return
        ContextCompat.registerReceiver(this, noisy, IntentFilter(AudioManager.ACTION_AUDIO_BECOMING_NOISY), ContextCompat.RECEIVER_NOT_EXPORTED)
        noisyRegistered = true
    }

    private fun unregisterNoisy() {
        if (noisyRegistered) runCatching { unregisterReceiver(noisy) }
        noisyRegistered = false
    }

    // ---- remembering where things were left

    fun saveResume() {
        val item = current ?: return
        if (!player.hasMedia() || ended) return
        val len = lengthMs
        if (len <= 0) return
        prefs.setResume(item.uri, MediaKinds.resumePoint(timeMs, len))
    }

    // ---- the notification and the media session

    private fun changed() {
        listeners.toList().forEach { it.onChanged() }
        updateSession()
        updateNotification()
    }

    private var sessionLength = 0L

    private fun updateSession() {
        val playing = player.isPlaying
        val actions = PlaybackStateCompat.ACTION_PLAY or PlaybackStateCompat.ACTION_PAUSE or
            PlaybackStateCompat.ACTION_PLAY_PAUSE or PlaybackStateCompat.ACTION_STOP or
            PlaybackStateCompat.ACTION_SKIP_TO_NEXT or PlaybackStateCompat.ACTION_SKIP_TO_PREVIOUS or
            PlaybackStateCompat.ACTION_PLAY_FROM_MEDIA_ID or PlaybackStateCompat.ACTION_PLAY_FROM_SEARCH or
            (if (player.isSeekable) PlaybackStateCompat.ACTION_SEEK_TO or PlaybackStateCompat.ACTION_FAST_FORWARD or PlaybackStateCompat.ACTION_REWIND else 0L)
        val state = when {
            current == null -> PlaybackStateCompat.STATE_NONE
            buffering && playing -> PlaybackStateCompat.STATE_BUFFERING
            playing -> PlaybackStateCompat.STATE_PLAYING
            ended -> PlaybackStateCompat.STATE_STOPPED
            else -> PlaybackStateCompat.STATE_PAUSED
        }
        session.setPlaybackState(
            PlaybackStateCompat.Builder().setActions(actions)
                .setState(state, timeMs, if (playing) player.rate else 0f).build()
        )
        val c = current
        if (c != null) {
            val md = MediaMetadataCompat.Builder()
                .putString(MediaMetadataCompat.METADATA_KEY_TITLE, title)
                .putString(MediaMetadataCompat.METADATA_KEY_ARTIST, artist)
                .putLong(MediaMetadataCompat.METADATA_KEY_DURATION, lengthMs.also { sessionLength = it })
            artworkBitmap()?.let { md.putBitmap(MediaMetadataCompat.METADATA_KEY_ALBUM_ART, it) }
            session.setMetadata(md.build())
        }
        session.isActive = c != null && !ended
    }

    private fun openPlayerIntent(): PendingIntent = PendingIntent.getActivity(
        this, 0, Intent(this, PlayerActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
    )

    private fun action(action: String, code: Int): PendingIntent = PendingIntent.getService(
        this, code, Intent(this, PlaybackService::class.java).setAction(action),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
    )

    private fun buildNotification(): Notification {
        val playing = player.isPlaying
        return NotificationCompat.Builder(this, CHANNEL)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(title)
            .setContentText(artist ?: getString(R.string.app_name))
            .setLargeIcon(artworkBitmap())
            .setContentIntent(openPlayerIntent())
            .setDeleteIntent(action(ACTION_DISMISS, 4))
            .setOnlyAlertOnce(true)
            .setSilent(true)
            .setShowWhen(false)
            .setOngoing(playing)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
            .setCategory(NotificationCompat.CATEGORY_TRANSPORT)
            .addAction(R.drawable.ic_previous, getString(R.string.previous), action(ACTION_PREVIOUS, 1))
            .addAction(
                if (playing) R.drawable.ic_pause else R.drawable.ic_play,
                getString(if (playing) R.string.pause else R.string.play), action(ACTION_TOGGLE, 2)
            )
            .addAction(R.drawable.ic_next, getString(R.string.next), action(ACTION_NEXT, 3))
            .setStyle(
                androidx.media.app.NotificationCompat.MediaStyle()
                    .setMediaSession(session.sessionToken)
                    .setShowActionsInCompactView(0, 1, 2)
            )
            .build()
    }

    /** What the notification on show says, so it's posted again only when that changes (Android mutes apps that
     * post too often). */
    private var postedKey: List<Any?>? = null

    private fun updateNotification() {
        if (current == null || ended) {
            stopForegroundNow(true)
            return
        }
        val playing = player.isPlaying
        // A paused video with the app open needs no notification; music, or anything left playing, gets one.
        if (!playing && !foreground) {
            if (bound || browsed) return
        }
        val key = listOf(current?.uri, title, artist, playing, artworkBitmap()?.hashCode())
        if (key == postedKey && (playing == foreground)) return
        postedKey = key
        val n = buildNotification()
        if (playing && !foreground) {
            try {
                ServiceCompat.startForeground(
                    this, NOTIFICATION_ID, n,
                    if (Build.VERSION.SDK_INT >= 29) ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PLAYBACK else 0
                )
                foreground = true
            } catch (e: Exception) {
                // Android 12+ may refuse while the app is in the background; the notification still shows
                Log.w(TAG, "startForeground refused", e)
                notifySafely(n)
            }
        } else if (!playing && foreground) {
            ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_DETACH)
            foreground = false
            notifySafely(n)
        } else {
            notifySafely(n)
        }
    }

    private fun notifySafely(n: Notification) {
        try {
            (getSystemService(NOTIFICATION_SERVICE) as NotificationManager).notify(NOTIFICATION_ID, n)
        } catch (e: SecurityException) {
            Log.w(TAG, "no notification permission", e)
        }
    }

    private fun stopForegroundNow(remove: Boolean) {
        if (foreground) {
            ServiceCompat.stopForeground(this, if (remove) ServiceCompat.STOP_FOREGROUND_REMOVE else ServiceCompat.STOP_FOREGROUND_DETACH)
            foreground = false
        }
        if (remove) {
            (getSystemService(NOTIFICATION_SERVICE) as NotificationManager).cancel(NOTIFICATION_ID)
            postedKey = null
        }
    }

    private fun stopIfIdle() {
        if (!bound && !browsed && !player.isPlaying) {
            stopForegroundNow(true)
            stopSelf()
        }
    }

    companion object {
        private const val TAG = "MediaPlayerSE"
        private const val CHANNEL = "playing"
        private const val NOTIFICATION_ID = 1
        /** Content providers that open their files at once (the phone's own storage and Downloads). */
        private val LOCAL_PROVIDERS = setOf(
            "media", "com.android.externalstorage.documents", "com.android.providers.media.documents",
            "com.android.providers.downloads.documents", "com.android.providers.downloads"
        )
        const val ACTION_FOREGROUND = "com.sauceedition.mediaplayer.FOREGROUND"
        /** Android Auto, Android Automotive, Google Assistant and the system's media controls. */
        private val BROWSERS = setOf(
            "com.google.android.projection.gearhead", "com.google.android.carassistant",
            "com.google.android.googlequicksearchbox", "com.android.car.media", "com.google.android.car.media",
            "com.android.systemui", "com.google.android.wearable.app"
        )
        const val ACTION_TOGGLE = "com.sauceedition.mediaplayer.TOGGLE"
        const val ACTION_NEXT = "com.sauceedition.mediaplayer.NEXT"
        const val ACTION_PREVIOUS = "com.sauceedition.mediaplayer.PREVIOUS"
        const val ACTION_DISMISS = "com.sauceedition.mediaplayer.DISMISS"
    }
}
