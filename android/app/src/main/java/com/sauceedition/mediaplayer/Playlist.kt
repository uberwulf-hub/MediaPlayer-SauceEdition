// The list being played: what's next and before, looping (the whole list or one item) and shuffling. Plain Kotlin,
// checked by the unit tests.
package com.sauceedition.mediaplayer

/** One thing to play: a content:// or file:// address of a file, or a network address, and the address of the
 * subtitle file beside it, if there is one. */
data class Item(val uri: String, val title: String, val subtitle: String? = null)

enum class Loop { OFF, ALL, ONE }

class Playlist(private val random: java.util.Random = java.util.Random()) {
    private val list = ArrayList<Item>()
    /** The play order when shuffling: indices into [list]. */
    private val order = ArrayList<Int>()

    var index = -1
        private set
    var loop = Loop.OFF
    var shuffle = false
        set(value) {
            if (field == value) return
            field = value
            reorder()
        }

    val items: List<Item> get() = list
    val size get() = list.size
    val current: Item? get() = list.getOrNull(index)

    fun set(items: List<Item>, start: Int) {
        list.clear()
        list.addAll(items)
        index = if (list.isEmpty()) -1 else start.coerceIn(0, list.size - 1)
        reorder()
    }

    fun add(items: List<Item>) {
        if (items.isEmpty()) return
        list.addAll(items)
        if (index < 0) index = 0
        reorder()
    }

    /** Puts [items] in place of the current one (a playlist file or a web page that turned out to hold several). */
    fun replaceCurrent(items: List<Item>) {
        if (index < 0 || items.isEmpty()) return
        list.removeAt(index)
        list.addAll(index, items)
        reorder()
    }

    fun remove(at: Int) {
        if (at !in list.indices) return
        list.removeAt(at)
        when {
            list.isEmpty() -> index = -1
            at < index -> index--
            at == index && index >= list.size -> index = list.size - 1
        }
        reorder()
    }

    fun jump(to: Int): Item? {
        if (to !in list.indices) return null
        index = to
        return list[to]
    }

    /** The item after this one, or null at the end. [auto] is true when the last one finished by itself, so "loop
     * one" repeats it; pressing Next always moves on. */
    fun next(auto: Boolean): Item? {
        if (index < 0) return null
        if (auto && loop == Loop.ONE) return list[index]
        val pos = order.indexOf(index)
        val nextPos = when {
            pos + 1 < order.size -> pos + 1
            loop != Loop.OFF -> 0
            else -> return null
        }
        index = order[nextPos]
        return list[index]
    }

    /** The item before this one (wrapping when looping), or null at the start. */
    fun previous(): Item? {
        if (index < 0) return null
        val pos = order.indexOf(index)
        val prevPos = when {
            pos > 0 -> pos - 1
            loop != Loop.OFF -> order.size - 1
            else -> return null
        }
        index = order[prevPos]
        return list[index]
    }

    val hasNext: Boolean get() = index >= 0 && (loop != Loop.OFF || order.indexOf(index) < order.size - 1)
    val hasPrevious: Boolean get() = index >= 0 && (loop != Loop.OFF || order.indexOf(index) > 0)

    private fun reorder() {
        order.clear()
        order.addAll(list.indices)
        if (shuffle && list.size > 1) {
            // the current item goes first, the rest in a random order
            order.remove(index.coerceAtLeast(0))
            order.shuffle(random)
            order.add(0, index.coerceAtLeast(0))
        }
    }
}
