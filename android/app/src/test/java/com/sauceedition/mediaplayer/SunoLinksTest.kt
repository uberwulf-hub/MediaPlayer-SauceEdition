package com.sauceedition.mediaplayer

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class SunoLinksTest {
    @Test
    fun recognisesSunoLinks() {
        assertTrue(SunoLinks.isSunoLink("https://suno.com/song/c1a16429-f3fe-4d99-bdcf-61ee13ed6f00"))
        assertTrue(SunoLinks.isSunoLink("https://suno.com/s/AbCd1234"))
        assertTrue(SunoLinks.isSunoLink("https://app.suno.ai/song/x"))
        assertFalse(SunoLinks.isSunoLink("https://notsuno.com/song/x"))
        assertFalse(SunoLinks.isSunoLink("suno.com/song/x"))
        assertFalse(SunoLinks.isSunoLink(null))
    }

    // what Suno's song pages look like now (November 2026): the audio is "forbidden" to other apps
    private val forbiddenPage = """<meta property="og:title" content="Together in Respect"/>
        <script>self.__next_f.push([1,"{\"audio_url\":\"https://studio-api.prod.suno.com/api/forbidden\"}"])</script>
        <audio src="https://cdn-o.suno.com/sil-100.mp3"></audio>"""

    @Test
    fun aForbiddenSongHasATitleButNoAudio() {
        val page = SunoLinks.parsePage(forbiddenPage)
        assertEquals("Together in Respect", page.title)
        assertNull(page.audioUrl)
    }

    @Test
    fun aPlayableSongGivesItsAudio() {
        val html = """<meta property="og:title" content="Rock &amp; Roll"/>
            {\"audio_url\":\"https://studio-api.prod.suno.com/api/forbidden\"}
            {\"audio_url\":\"https:\/\/cdn1.suno.ai\/abc.mp3\"}"""
        val page = SunoLinks.parsePage(html)
        assertEquals("Rock & Roll", page.title)
        assertEquals("https://cdn1.suno.ai/abc.mp3", page.audioUrl)
    }
}
