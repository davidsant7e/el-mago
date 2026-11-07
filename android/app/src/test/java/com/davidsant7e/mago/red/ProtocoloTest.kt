package com.davidsant7e.mago.red

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ProtocoloTest {

    /** Simula el editor del PC: aplica los mensajes como lo haría Windows (cursor al final al empezar). */
    private class EditorPc(texto: String) {
        val cps = texto.codePoints().toArray().toMutableList()
        var cursor = cps.size
        override fun toString() = String(cps.toIntArray(), 0, cps.size)

        fun aplicar(msg: String) {
            val t = campo(msg, "t")
            when (t) {
                "texto" -> escribir(campo(msg, "s")!!)
                "tecla" -> tecla(campo(msg, "k")!!, numero(msg, "n") ?: 1)
                "edicion" -> {
                    val izq = numero(msg, "izq")!!
                    tecla("left", izq); tecla("backspace", numero(msg, "borrar")!!)
                    escribir(campo(msg, "s")!!); tecla("right", izq)
                }
                else -> error("tipo $t")
            }
        }

        private fun escribir(s: String) { s.codePoints().forEach { cps.add(cursor++, it) } }
        private fun tecla(k: String, n: Int) = repeat(n) {
            when (k) {
                "left" -> if (cursor > 0) cursor--
                "right" -> if (cursor < cps.size) cursor++
                "backspace" -> if (cursor > 0) cps.removeAt(--cursor)
                else -> error(k)
            }
        }

        // JSON mínimo para la prueba (los mensajes los genera Protocolo).
        private fun campo(m: String, k: String): String? {
            val i = m.indexOf("\"$k\":\"").takeIf { it >= 0 } ?: return null
            val sb = StringBuilder(); var j = i + k.length + 4
            while (m[j] != '"') {
                if (m[j] == '\\') {
                    j++
                    when (m[j]) {
                        'n' -> sb.append('\n'); 'r' -> sb.append('\r'); 't' -> sb.append('\t')
                        'u' -> { sb.append(m.substring(j + 1, j + 5).toInt(16).toChar()); j += 4 }
                        else -> sb.append(m[j])
                    }
                } else sb.append(m[j])
                j++
            }
            return sb.toString()
        }
        private fun numero(m: String, k: String): Int? =
            Regex("\"$k\":(\\d+)").find(m)?.groupValues?.get(1)?.toInt()
    }

    private fun comprobar(antes: String, ahora: String) {
        val pc = EditorPc(antes)
        Protocolo.cambios(antes, ahora).forEach { pc.aplicar(it) }
        assertEquals("de «$antes» a «$ahora»", ahora, pc.toString())
        assertEquals("cursor al final", pc.cps.size, pc.cursor)
    }

    @Test fun firmaIgualQueWindows() {
        // El mismo vector está en windows/tests/Pruebas.cs
        assertEquals("f6ced6f4883ffc0981a6b9945819f680102b43097ad8ef7a0df9bde06fb3d2e4", Protocolo.firma("123456", "abc"))
    }

    @Test fun escribirAlFinal() {
        assertEquals(listOf("""{"t":"texto","s":"a"}"""), Protocolo.cambios("", "a"))
        comprobar("Hola", "Hola mundo")
    }

    @Test fun borrarAlFinal() {
        assertEquals(listOf("""{"t":"tecla","k":"backspace","n":2}"""), Protocolo.cambios("Hola", "Ho"))
        comprobar("Hola mundo", "")
    }

    @Test fun autocorrectorCambiaPalabra() {
        comprobar("ke tal", "qué tal")   // Gboard corrige al principio
        comprobar("hola komo estas", "hola como estas")
        comprobar("aaa", "aXa")
        comprobar("abcabc", "abc")
    }

    @Test fun emojisYSaltos() {
        comprobar("hola 😀", "hola")
        comprobar("😀😀", "😀🎉😀")
        comprobar("línea 1\nlínea 2", "línea 1\nlínea dos")
        comprobar("ñandú", "Ñandú")
        val m = Protocolo.cambios("x😀", "x")
        assertEquals(listOf("""{"t":"tecla","k":"backspace","n":1}"""), m) // un emoji = un ⌫
    }

    @Test fun textoLargoEnPartes() {
        val largo = "a".repeat(Protocolo.MAX_TEXTO * 2 + 5)
        val m = Protocolo.cambios("", largo)
        assertEquals(3, m.size)
        comprobar("", largo)
        comprobar("fin", largo + "fin")
    }

    @Test fun pruebaAlAzar() {
        val r = java.util.Random(7)
        val letras = "ab😀\nñ "
        fun azar(): String {
            val sb = StringBuilder()
            repeat(r.nextInt(12)) {
                val i = r.nextInt(6)
                sb.append(if (i == 2) "😀" else letras[if (i > 2) i + 1 else i].toString())
            }
            return sb.toString()
        }
        repeat(3000) { comprobar(azar(), azar()) }
    }

    @Test fun jsonEscapado() {
        assertEquals("\"a\\\"b\\\\c\\n\\u0001\"", Protocolo.json("a\"b\\c\n\u0001"))
    }

    @Test fun difusion() {
        assertTrue(Protocolo.difusion(byteArrayOf(192.toByte(), 168.toByte(), 1, 37), 24)
            .contentEquals(byteArrayOf(192.toByte(), 168.toByte(), 1, 255.toByte())))
        assertTrue(Protocolo.difusion(byteArrayOf(10, 0, 5, 9), 16)
            .contentEquals(byteArrayOf(10, 0, 255.toByte(), 255.toByte())))
    }

    @Test fun direccionManual() {
        assertEquals("192.168.1.5" to 47800, Protocolo.leerDireccion(" 192.168.1.5 "))
        assertEquals("10.0.0.2" to 5000, Protocolo.leerDireccion("10.0.0.2:5000"))
        assertNull(Protocolo.leerDireccion("192.168.1"))
        assertNull(Protocolo.leerDireccion("192.168.1.300"))
        assertNull(Protocolo.leerDireccion("pc-de-casa"))
    }
}
