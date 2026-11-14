package com.sauceedition.mediaplayer

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class MediaKindsTest {
    @Test
    fun kindsOfFile() {
        assertTrue(MediaKinds.isVideo("Holiday.MKV"))
        assertTrue(MediaKinds.isAudio("/music/song.flac"))
        assertTrue(MediaKinds.isMedia("clip.webm"))
        assertFalse(MediaKinds.isMedia("notes.txt"))
        assertFalse(MediaKinds.isMedia("no extension"))
        assertFalse(MediaKinds.isMedia("folder.mp4/file"))
        assertTrue(MediaKinds.isSubtitle("Film.en.srt"))
        assertTrue(MediaKinds.isPlaylist("radio.m3u8"))
        assertEquals("mp3", MediaKinds.extension("https://x.org/a/song.mp3?token=1"))
    }

    @Test
    fun addresses() {
        assertTrue(MediaKinds.isStreamAddress("https://example.com/live.m3u8"))
        assertTrue(MediaKinds.isStreamAddress("rtsp://camera.local/stream"))
        assertFalse(MediaKinds.isStreamAddress("content://media/external/video/media/5"))
        assertFalse(MediaKinds.isStreamAddress("https://"))
        assertEquals("https://example.com/radio.mp3", MediaKinds.normaliseAddress("  example.com/radio.mp3 "))
        assertEquals("rtmp://host/app", MediaKinds.normaliseAddress("rtmp://host/app"))
        assertNull(MediaKinds.normaliseAddress("not an address"))
        assertNull(MediaKinds.normaliseAddress("hello"))
        assertNull(MediaKinds.normaliseAddress("content://x/y"))
        assertNull(MediaKinds.normaliseAddress(""))
    }

    @Test
    fun namesToShow() {
        assertEquals("Holiday 2024", MediaKinds.displayName("/storage/Movies/Holiday 2024.mp4"))
        assertEquals("song one.mp3", MediaKinds.displayName("https://x.org/music/song%20one.mp3"))
        assertEquals("radio.example.com/live", MediaKinds.displayName("https://radio.example.com/live"))
        assertEquals("example.com", MediaKinds.displayName("https://example.com/"))
        assertEquals("README", MediaKinds.displayName("README"))
        assertEquals("", MediaKinds.displayName(null))
    }

    @Test
    fun naturalOrder() {
        val sorted = listOf("Track 10.mp3", "track 2.mp3", "Track 1.mp3", "Track 02b.mp3", "Intro.mp3")
            .sortedWith(MediaKinds.naturalOrder)
        assertEquals(listOf("Intro.mp3", "Track 1.mp3", "track 2.mp3", "Track 02b.mp3", "Track 10.mp3"), sorted)
    }

    @Test
    fun times() {
        assertEquals("0:00", MediaKinds.formatTime(-5))
        assertEquals("0:05", MediaKinds.formatTime(5_400))
        assertEquals("3:07", MediaKinds.formatTime(187_000))
        assertEquals("1:02:03", MediaKinds.formatTime(3_723_000))
    }

    @Test
    fun resumePoints() {
        val tenMinutes = 600_000L
        assertEquals(0, MediaKinds.resumePoint(60_000, 120_000)) // too short to remember
        assertEquals(0, MediaKinds.resumePoint(5_000, tenMinutes)) // barely started
        assertEquals(0, MediaKinds.resumePoint(tenMinutes - 5_000, tenMinutes)) // as good as finished
        assertEquals(240_000, MediaKinds.resumePoint(240_000, tenMinutes))
    }

    @Test
    fun subtitlesBesideVideos() {
        assertTrue(DeviceMedia.matchesSubtitle("Film", "Film.srt"))
        assertTrue(DeviceMedia.matchesSubtitle("Film", "film.en.srt"))
        assertTrue(DeviceMedia.matchesSubtitle("Film", "Film.English.ass"))
        assertFalse(DeviceMedia.matchesSubtitle("Film", "Film 2.srt"))
        assertFalse(DeviceMedia.matchesSubtitle("Film", "Filmography.srt"))
    }
}
