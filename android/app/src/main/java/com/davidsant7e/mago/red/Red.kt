package com.davidsant7e.mago.red

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import org.json.JSONObject
import java.io.BufferedReader
import java.io.InputStreamReader
import java.io.OutputStreamWriter
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket
import java.net.SocketTimeoutException

/** Utilidades de red: siempre por el Wi-Fi (aunque los datos móviles estén encendidos). */
object Red {

    /** La red Wi-Fi conectada, o null. */
    fun wifi(c: Context): Network? {
        val cm = c.getSystemService(ConnectivityManager::class.java) ?: return null
        @Suppress("DEPRECATION")
        return cm.allNetworks.firstOrNull { n ->
            cm.getNetworkCapabilities(n)?.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) == true
        }
    }

    /** IPv4 del celular en el Wi-Fi y su prefijo (ej. 192.168.1.37 /24). */
    fun miIp(c: Context, n: Network): Pair<Inet4Address, Int>? {
        val cm = c.getSystemService(ConnectivityManager::class.java) ?: return null
        val la = cm.getLinkProperties(n)?.linkAddresses ?: return null
        val a = la.firstOrNull { it.address is Inet4Address } ?: return null
        return (a.address as Inet4Address) to a.prefixLength
    }

    /** Socket TCP por el Wi-Fi con tiempos de espera razonables. */
    fun conectar(n: Network, ip: String, puerto: Int, espera: Int = 3000): Socket {
        val s = n.socketFactory.createSocket()
        try {
            s.tcpNoDelay = true
            s.connect(InetSocketAddress(InetAddress.getByName(ip), puerto), espera)
            return s
        } catch (e: Exception) {
            runCatching { s.close() }
            throw e
        }
    }

    /**
     * Busca PCs con El Mago abierto: pregunta por difusión en la red y junta respuestas durante [ms].
     * Si el router aísla los equipos (red de invitados), no encontrará nada: entonces se escribe la IP a mano.
     */
    fun buscar(c: Context, ms: Int = 1500): List<Pc> {
        val n = wifi(c) ?: return emptyList()
        val encontrados = LinkedHashMap<String, Pc>()
        DatagramSocket().use { s ->
            n.bindSocket(s)
            s.broadcast = true
            s.soTimeout = 250
            val datos = Protocolo.PREGUNTA.toByteArray(Charsets.UTF_8)
            val destinos = ArrayList<InetAddress>()
            destinos += InetAddress.getByName("255.255.255.255")
            miIp(c, n)?.let { (ip, pre) -> destinos += InetAddress.getByAddress(Protocolo.difusion(ip.address, pre)) }
            val fin = System.currentTimeMillis() + ms
            var envios = 0
            val buf = ByteArray(2048)
            while (System.currentTimeMillis() < fin) {
                if (envios < 3) { // tres veces por si se pierde algún paquete
                    destinos.forEach { d -> runCatching { s.send(DatagramPacket(datos, datos.size, d, Protocolo.PUERTO_DESCUBRIR)) } }
                    envios++
                }
                try {
                    val pk = DatagramPacket(buf, buf.size)
                    s.receive(pk)
                    val j = JSONObject(String(pk.data, 0, pk.length, Charsets.UTF_8))
                    if (j.optString("t") != "pc") continue
                    val ip = pk.address.hostAddress ?: continue
                    val pc = Pc(j.optString("nombre", "PC").take(40), ip, j.optInt("puerto", Protocolo.PUERTO_TCP), j.optString("id"))
                    encontrados[pc.id.ifEmpty { ip }] = pc
                } catch (_: SocketTimeoutException) {
                } catch (_: Exception) {
                }
            }
        }
        return encontrados.values.toList()
    }

    class Rechazo(val motivo: String) : Exception(motivo)

    /** Conexión abierta y autenticada con el PC. */
    class Sesion(val socket: Socket, val entrada: BufferedReader, val salida: OutputStreamWriter, val pcId: String, val pcNombre: String) {
        fun enviar(linea: String) = synchronized(salida) { salida.write(linea); salida.write("\n"); salida.flush() }
        fun cerrar() = runCatching { socket.close() }
    }

    /**
     * Abre la conexión y responde el reto. Con [pin] empareja (devuelve la llave nueva en [alEmparejar]);
     * si no, usa la [llave] guardada.
     */
    fun abrir(c: Context, n: Network, pc: Pc, almacen: Almacen, llave: String?, pin: String?, alEmparejar: (String) -> Unit = {}): Sesion {
        val s = conectar(n, pc.ip, pc.puerto)
        try {
            s.soTimeout = 8000
            val ent = BufferedReader(InputStreamReader(s.getInputStream(), Charsets.UTF_8))
            val sal = OutputStreamWriter(s.getOutputStream(), Charsets.UTF_8)
            val reto = JSONObject(ent.readLine() ?: throw Rechazo("El PC cerró la conexión"))
            if (reto.optString("t") != "reto") throw Rechazo("Eso no parece El Mago")
            val n0 = reto.getString("n")
            val (tipo, clave) = if (pin != null) "par" to pin else "auth" to (llave ?: throw Rechazo("no-emparejado"))
            sal.write(Protocolo.autenticar(tipo, almacen.idCelular, almacen.nombreCelular, Protocolo.firma(clave, n0)) + "\n")
            sal.flush()
            val r = JSONObject(ent.readLine() ?: throw Rechazo("El PC cerró la conexión"))
            when (r.optString("t")) {
                "ok" -> if (pin != null) alEmparejar(r.getString("llave"))
                "rechazo" -> throw Rechazo(r.optString("m", "Rechazado"))
                else -> throw Rechazo("Respuesta desconocida")
            }
            return Sesion(s, ent, sal, reto.optString("id"), reto.optString("pc", pc.nombre))
        } catch (e: Exception) {
            runCatching { s.close() }
            throw e
        }
    }
}
