package com.sauceedition.mediaplayer

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.Random

class PlaylistTest {
    private fun items(n: Int) = (1..n).map { Item("file:///$it.mp3", "$it") }

    @Test
    fun goesThroughInOrderAndStopsAtTheEnd() {
        val p = Playlist()
        p.set(items(3), 0)
        assertEquals("1", p.current?.title)
        assertEquals("2", p.next(auto = true)?.title)
        assertEquals("3", p.next(auto = true)?.title)
        assertFalse(p.hasNext)
        assertNull(p.next(auto = true))
        assertEquals("3", p.current?.title)
        assertEquals("2", p.previous()?.title)
    }

    @Test
    fun loopsTheListOrOneItem() {
        val p = Playlist()
        p.set(items(2), 1)
        p.loop = Loop.ALL
        assertEquals("1", p.next(auto = true)?.title)
        assertEquals("2", p.previous()?.title) // wraps backwards too
        p.loop = Loop.ONE
        val here = p.current
        assertEquals(here, p.next(auto = true)) // finishing repeats it
        assertTrue(p.next(auto = false) != here) // Next still moves on
    }

    @Test
    fun shufflePlaysEachOnceStartingWithTheCurrentOne() {
        val p = Playlist(Random(42))
        p.set(items(10), 4)
        p.shuffle = true
        val seen = mutableListOf(p.current!!.title)
        while (true) seen += (p.next(auto = true) ?: break).title
        assertEquals("5", seen.first())
        assertEquals((1..10).map { "$it" }.toSet(), seen.toSet())
        assertEquals(10, seen.size)
    }

    @Test
    fun removingKeepsTheCurrentItem() {
        val p = Playlist()
        p.set(items(4), 2)
        p.remove(0)
        assertEquals("3", p.current?.title)
        p.remove(p.index) // the current one: the next takes its place
        assertEquals("4", p.current?.title)
        p.remove(p.index) // the last: the one before
        assertEquals("2", p.current?.title)
        p.remove(0)
        assertNull(p.current)
        assertEquals(-1, p.index)
    }

    @Test
    fun aPlaylistFileOpensAsWhatItHolds() {
        val p = Playlist()
        p.set(listOf(Item("a", "a"), Item("list.m3u", "list"), Item("z", "z")), 1)
        p.replaceCurrent(listOf(Item("b", "b"), Item("c", "c")))
        assertEquals(listOf("a", "b", "c", "z"), p.items.map { it.title })
        assertEquals("b", p.current?.title)
        assertEquals("c", p.next(auto = true)?.title)
    }

    @Test
    fun addingToAnEmptyListStartsAtTheFirst() {
        val p = Playlist()
        p.add(items(2))
        assertEquals(0, p.index)
        p.jump(1)
        assertEquals("2", p.current?.title)
        assertNull(p.jump(7))
    }
}
