package com.davidsant7e.mago.red

import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

/**
 * Reglas compartidas con el programa de Windows (windows/src/Protocolo.cs).
 * Solo Kotlin puro: se prueba sin Android.
 */
object Protocolo {
    const val VERSION = 1
    const val PUERTO_TCP = 47800
    const val PUERTO_DESCUBRIR = 47801
    const val PREGUNTA = "MAGO?v1"
    const val MAX_TEXTO = 8000

    /** HMAC-SHA256(clave, reto) en hexadecimal minúsculo. Igual que en Windows. */
    fun firma(clave: String, reto: String): String {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(clave.toByteArray(Charsets.UTF_8), "HmacSHA256"))
        return mac.doFinal(reto.toByteArray(Charsets.UTF_8)).joinToString("") { "%02x".format(it) }
    }

    /** Escapa una cadena para JSON (sin depender de org.json, para poder probarlo en el JVM). */
    fun json(s: String): String {
        val sb = StringBuilder("\"")
        for (c in s) {
            when (c) {
                '"' -> sb.append("\\\"")
                '\\' -> sb.append("\\\\")
                '\n' -> sb.append("\\n")
                '\r' -> sb.append("\\r")
                '\t' -> sb.append("\\t")
                else -> if (c < ' ') sb.append("\\u%04x".format(c.code)) else sb.append(c)
            }
        }
        return sb.append('"').toString()
    }

    fun texto(s: String) = """{"t":"texto","s":${json(s)}}"""
    fun tecla(k: String, n: Int = 1) = """{"t":"tecla","k":${json(k)},"n":$n}"""
    fun combo(k: String) = """{"t":"combo","k":${json(k)}}"""
    const val PING = """{"t":"ping"}"""
    const val ADIOS = """{"t":"adios"}"""

    fun autenticar(tipo: String, id: String, nombre: String, firma: String) =
        """{"t":${json(tipo)},"id":${json(id)},"nombre":${json(nombre)},"h":${json(firma)}}"""

    /**
     * Mensajes para pasar de lo que el PC ya tiene ([antes]) a lo que hay ahora en el campo ([ahora]).
     * Supone que el cursor del PC está al final de [antes].
     * Cuenta en caracteres reales (un emoji = una tecla ⌫ / ←), no en mitades UTF-16.
     * Devuelve partes de máximo [MAX_TEXTO] caracteres.
     */
    fun cambios(antes: String, ahora: String): List<String> {
        if (antes == ahora) return emptyList()
        var p = 0
        val min = minOf(antes.length, ahora.length)
        while (p < min && antes[p] == ahora[p]) p++
        if (p > 0 && Character.isHighSurrogate(antes[p - 1])) p-- // no partir un emoji
        var s = 0
        while (s < min - p && antes[antes.length - 1 - s] == ahora[ahora.length - 1 - s]) s++
        if (s > 0 && Character.isLowSurrogate(antes[antes.length - s])) s--
        val quitado = antes.substring(p, antes.length - s)
        val nuevo = ahora.substring(p, ahora.length - s)
        val izq = antes.codePointCount(antes.length - s, antes.length)
        val borrar = quitado.codePointCount(0, quitado.length)

        val partes = trozos(nuevo)
        val msgs = ArrayList<String>()
        if (izq == 0) {
            if (borrar > 0) msgs += tecla("backspace", borrar)
            partes.forEach { msgs += texto(it) }
        } else {
            // El PC mueve el cursor, borra, escribe y vuelve al final en un solo mensaje.
            val primero = partes.firstOrNull() ?: ""
            if (partes.size <= 1) {
                msgs += """{"t":"edicion","izq":$izq,"borrar":$borrar,"s":${json(primero)}}"""
            } else {
                msgs += tecla("left", izq)
                if (borrar > 0) msgs += tecla("backspace", borrar)
                partes.forEach { msgs += texto(it) }
                msgs += tecla("right", izq)
            }
        }
        return msgs
    }

    private fun trozos(s: String): List<String> {
        if (s.isEmpty()) return emptyList()
        val r = ArrayList<String>()
        var i = 0
        while (i < s.length) {
            var f = minOf(s.length, i + MAX_TEXTO)
            if (f < s.length && Character.isHighSurrogate(s[f - 1])) f--
            r += s.substring(i, f)
            i = f
        }
        return r
    }

    /** Dirección de difusión de una red IPv4 (ej. 192.168.1.37/24 → 192.168.1.255). */
    fun difusion(ip: ByteArray, prefijo: Int): ByteArray {
        require(ip.size == 4 && prefijo in 0..32)
        val n = ((ip[0].toInt() and 0xff) shl 24) or ((ip[1].toInt() and 0xff) shl 16) or
            ((ip[2].toInt() and 0xff) shl 8) or (ip[3].toInt() and 0xff)
        val mascara = if (prefijo == 0) 0 else (-1 shl (32 - prefijo))
        val b = n or mascara.inv()
        return byteArrayOf((b ushr 24).toByte(), (b ushr 16).toByte(), (b ushr 8).toByte(), b.toByte())
    }

    /** ¿Es una IPv4 válida escrita a mano? (acepta "192.168.1.5" o "192.168.1.5:47800") */
    fun leerDireccion(s: String): Pair<String, Int>? {
        val t = s.trim()
        val partes = t.split(":")
        if (partes.size > 2) return null
        val ip = partes[0]
        val octetos = ip.split(".")
        if (octetos.size != 4 || octetos.any { o -> o.isEmpty() || o.length > 3 || !o.all { it.isDigit() } || o.toInt() > 255 }) return null
        val puerto = if (partes.size == 2) partes[1].toIntOrNull() ?: return null else PUERTO_TCP
        if (puerto !in 1..65535) return null
        return ip to puerto
    }
}
