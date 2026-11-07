package com.davidsant7e.mago.red

import android.content.Context
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import android.util.Log
import org.json.JSONObject
import java.util.concurrent.CopyOnWriteArraySet
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit

/** Estado de la conexión con el PC (lo muestran la burbuja, la notificación y las pantallas). */
sealed class Estado {
    object Apagado : Estado()
    object SinWifi : Estado()
    object SinPc : Estado()
    data class Conectando(val pc: String, val detalle: String?) : Estado()
    data class Conectado(val pc: String, val ventana: String, val pausado: Boolean) : Estado()
    object NoEmparejado : Estado()

    val listo get() = this is Conectado && !pausado
}

/**
 * Mantiene la conexión con el PC mientras alguien la usa (la burbuja o el teclado).
 * Reconecta sola si se cae el Wi-Fi o el PC se duerme. Un solo hilo de trabajo.
 */
object Enlace {
    private const val TAG = "ElMago"
    private val main = Handler(Looper.getMainLooper())
    private val oyentes = CopyOnWriteArraySet<(Estado) -> Unit>()
    private val avisos = CopyOnWriteArraySet<(String) -> Unit>()
    private val usuarios = HashSet<String>()
    private val cola = LinkedBlockingQueue<String>(2000)
    private var hilo: Thread? = null
    @Volatile private var sesion: Red.Sesion? = null
    @Volatile var estado: Estado = Estado.Apagado
        private set

    /** Con el teclado abierto se revisa el PC cada 4 s; solo con la burbuja, cada 20 s. */
    @Volatile var rapido = false

    fun oir(f: (Estado) -> Unit) { oyentes += f; main.post { f(estado) } }
    fun dejarDeOir(f: (Estado) -> Unit) { oyentes -= f }
    fun oirAvisos(f: (String) -> Unit) { avisos += f }
    fun dejarAvisos(f: (String) -> Unit) { avisos -= f }

    private fun poner(e: Estado) {
        if (e == estado) return
        estado = e
        Log.i(TAG, "Estado: $e")
        main.post { oyentes.forEach { it(e) } }
    }

    private fun avisar(m: String) = main.post { avisos.forEach { it(m) } }

    /** [quien] empieza a usar la conexión ("burbuja", "teclado"). */
    @Synchronized fun usar(c: Context, quien: String) {
        usuarios += quien
        if (hilo?.isAlive != true) {
            val ctx = c.applicationContext
            hilo = Thread({ bucle(ctx) }, "Mago-enlace").also { it.isDaemon = true; it.start() }
        }
        despertar()
    }

    @Synchronized fun soltar(quien: String) {
        usuarios -= quien
        if (usuarios.isEmpty()) {
            hilo?.interrupt()
            hilo = null
            sesion?.let { s -> Thread { runCatching { s.enviar(Protocolo.ADIOS) }; s.cerrar() }.start() }
            poner(Estado.Apagado)
        }
    }

    @Synchronized private fun hayUsuarios() = usuarios.isNotEmpty()

    /** Vuelve a intentar ya (por ejemplo, tras emparejar o al abrir el teclado). */
    fun despertar() { cola.offer(DESPERTAR) }

    /** Encola un mensaje para el PC. false si ahora no hay conexión lista. */
    fun enviar(linea: String): Boolean {
        if (!estado.listo || sesion == null) return false
        return cola.offer(linea)
    }

    fun enviarTodo(lineas: List<String>): Boolean {
        if (!estado.listo || sesion == null) return false
        lineas.forEach { if (!cola.offer(it)) return false }
        return true
    }

    private fun bucle(c: Context) {
        val almacen = Almacen(c)
        val pm = c.getSystemService(PowerManager::class.java)
        var espera = 1000L
        while (!Thread.currentThread().isInterrupted && hayUsuarios()) {
            try {
                // Pantalla apagada y teclado cerrado: no gastar batería.
                if (!rapido && pm?.isInteractive == false) { esperar(5000); continue }
                val red = Red.wifi(c)
                if (red == null) { poner(Estado.SinWifi); esperar(3000); continue }
                val pc = almacen.pc
                val llave = almacen.llave
                if (pc == null || llave == null) { poner(if (pc == null) Estado.SinPc else Estado.NoEmparejado); esperar(5000); continue }

                poner(Estado.Conectando(pc.nombre, null))
                val s = try {
                    Red.abrir(c, red, pc, almacen, llave, null)
                } catch (e: Red.Rechazo) {
                    if (e.motivo == "no-emparejado" || e.motivo == "llave-invalida") {
                        almacen.llave = null
                        poner(Estado.NoEmparejado)
                        continue
                    }
                    throw e
                }
                sesion = s
                espera = 1000L
                cola.clear()
                poner(Estado.Conectado(s.pcNombre, "", false))
                Log.i(TAG, "Conectado a ${pc.ip}")
                val lector = Thread({ leer(s) }, "Mago-lector").apply { isDaemon = true; start() }
                s.enviar(Protocolo.PING)
                // Escribe lo que llegue a la cola; si no llega nada, hace ping para saber si el PC sigue ahí.
                while (lector.isAlive && hayUsuarios() && !Thread.currentThread().isInterrupted) {
                    val m = cola.poll(if (rapido) 4 else 20, TimeUnit.SECONDS)
                    if (!rapido && pm?.isInteractive == false) break
                    when (m) {
                        null, DESPERTAR -> s.enviar(Protocolo.PING)
                        else -> s.enviar(m)
                    }
                }
                s.cerrar()
                sesion = null
                lector.join(1000)
            } catch (_: InterruptedException) {
                break
            } catch (e: Exception) {
                sesion?.cerrar(); sesion = null
                val pc = almacen.pc
                Log.w(TAG, "Sin conexión: ${e.message}")
                poner(Estado.Conectando(pc?.nombre ?: "PC", motivo(e)))
                esperar(espera)
                espera = (espera * 2).coerceAtMost(10_000L)
            }
        }
        sesion?.cerrar(); sesion = null
    }

    private fun leer(s: Red.Sesion) {
        try {
            s.socket.soTimeout = 45_000
            while (true) {
                val l = s.entrada.readLine() ?: break
                val j = runCatching { JSONObject(l) }.getOrNull() ?: continue
                when (j.optString("t")) {
                    "pong" -> poner(Estado.Conectado(s.pcNombre, j.optString("ventana"), j.optBoolean("pausado")))
                    "error" -> avisar(j.optString("m"))
                    "rechazo" -> { avisar(j.optString("m")); break }
                }
            }
        } catch (_: Exception) {
        } finally {
            s.cerrar()
            despertar()
        }
    }

    private fun esperar(ms: Long) { cola.poll(ms, TimeUnit.MILLISECONDS) }

    private fun motivo(e: Exception): String = when {
        e is java.net.ConnectException -> "El PC no responde: ¿El Mago está abierto allí?"
        e is java.net.SocketTimeoutException -> "El PC no contesta (¿dormido o en otra red?)"
        e is java.net.NoRouteToHostException -> "No se llega al PC por este Wi-Fi"
        e is Red.Rechazo -> e.motivo
        else -> e.message ?: e.javaClass.simpleName
    }

    private const val DESPERTAR = "\u0000despertar"
}
