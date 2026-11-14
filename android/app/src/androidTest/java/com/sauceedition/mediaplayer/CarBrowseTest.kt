package com.sauceedition.mediaplayer

import android.content.ComponentName
import android.os.Handler
import android.os.Looper
import android.support.v4.media.MediaBrowserCompat
import android.support.v4.media.session.MediaControllerCompat
import android.support.v4.media.session.PlaybackStateCompat
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

/** Connects the way Android Auto does (a media browser), browses, and plays a song from the Songs tab. Needs a
 * song on the device and the music permission:
 * adb shell pm grant com.sauceedition.mediaplayer android.permission.READ_EXTERNAL_STORAGE (READ_MEDIA_AUDIO on 13+) */
@RunWith(AndroidJUnit4::class)
class CarBrowseTest {
    private val context = InstrumentationRegistry.getInstrumentation().targetContext
    private val main = Handler(Looper.getMainLooper())

    @Test
    fun browseAndPlayLikeTheCar() {
        val connected = CountDownLatch(1)
        lateinit var browser: MediaBrowserCompat
        main.post {
            browser = MediaBrowserCompat(context, ComponentName(context, PlaybackService::class.java),
                object : MediaBrowserCompat.ConnectionCallback() {
                    override fun onConnected() = connected.countDown()
                }, null)
            browser.connect()
        }
        assertTrue("connects", connected.await(60, TimeUnit.SECONDS))

        val root = children(browser, browser.root)
        assertEquals(listOf(CarLibrary.RECENT, CarLibrary.SONGS), root.map { it.mediaId })
        val songs = children(browser, CarLibrary.SONGS)
        assertTrue("has songs: ${songs.map { it.description.title }}", songs.isNotEmpty() && songs[0].isPlayable)

        val playing = CountDownLatch(1)
        main.post {
            val controller = MediaControllerCompat(context, browser.sessionToken)
            controller.registerCallback(object : MediaControllerCompat.Callback() {
                override fun onPlaybackStateChanged(state: PlaybackStateCompat?) {
                    if (state?.state == PlaybackStateCompat.STATE_PLAYING) playing.countDown()
                }
            })
            controller.transportControls.playFromMediaId(songs[0].mediaId, null)
        }
        assertTrue("plays the song", playing.await(90, TimeUnit.SECONDS))
        main.post { browser.disconnect() }
    }

    private fun children(browser: MediaBrowserCompat, id: String): List<MediaBrowserCompat.MediaItem> {
        val done = CountDownLatch(1)
        var items: List<MediaBrowserCompat.MediaItem> = emptyList()
        main.post {
            browser.subscribe(id, object : MediaBrowserCompat.SubscriptionCallback() {
                override fun onChildrenLoaded(parentId: String, children: MutableList<MediaBrowserCompat.MediaItem>) {
                    items = children
                    browser.unsubscribe(id)
                    done.countDown()
                }
            })
        }
        assertTrue("loads $id", done.await(60, TimeUnit.SECONDS))
        return items
    }
}
