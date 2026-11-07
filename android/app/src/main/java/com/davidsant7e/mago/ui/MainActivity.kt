package com.davidsant7e.mago.ui

import android.Manifest
import android.app.Activity
import android.app.AlertDialog
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import android.text.InputType
import android.view.View
import android.view.ViewGroup
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Switch
import android.widget.TextView
import android.widget.Toast
import com.davidsant7e.mago.red.Almacen
import com.davidsant7e.mago.red.Enlace
import com.davidsant7e.mago.red.Estado
import com.davidsant7e.mago.red.Pc
import com.davidsant7e.mago.red.Protocolo
import com.davidsant7e.mago.red.Red
import com.davidsant7e.mago.servicio.MagoService
import com.davidsant7e.mago.ui.Vistas.dp

/** Pantalla principal: estado, emparejar el PC, burbuja y botón para abrir el teclado. */
class MainActivity : Activity() {

    private lateinit var almacen: Almacen
    private lateinit var lblEstado: TextView
    private lateinit var punto: View
    private lateinit var lblPc: TextView
    private lateinit var lblWifi: TextView
    private lateinit var swBurbuja: Switch
    private lateinit var lblPermiso: TextView
    private val main = Handler(Looper.getMainLooper())
    private val oyente: (Estado) -> Unit = { e -> mostrarEstado(e) }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        almacen = Almacen(this)
        setContentView(construir())
    }

    private fun construir(): View {
        val c = this
        val col = Vistas.columna(c).apply { setPadding(dp(c, 20), dp(c, 28), dp(c, 20), dp(c, 28)) }

        col.addView(Vistas.texto(c, "I · EL MAGO", 26f, Vistas.DURAZNO, true).apply { letterSpacing = 0.1f })
        col.addView(Vistas.margen(Vistas.texto(c, "Escribe en tu PC desde el celular, por el Wi-Fi de la casa.", 15f, Vistas.CREMA), c, arriba = 4, abajo = 18))

        // Estado
        col.addView(Vistas.etiqueta(c, "Estado"))
        val fe = Vistas.fila(c).apply { background = Vistas.fondo(c, Vistas.NOCHE_CLARA, 14); setPadding(dp(c, 14), dp(c, 12), dp(c, 14), dp(c, 12)) }
        punto = View(c).apply { layoutParams = LinearLayout.LayoutParams(dp(c, 12), dp(c, 12)).apply { marginEnd = dp(c, 10) } }
        lblEstado = Vistas.texto(c, "", 15f).apply { layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f) }
        fe.addView(punto); fe.addView(lblEstado)
        col.addView(Vistas.margen(fe, c, arriba = 6, abajo = 6))
        lblWifi = Vistas.texto(c, "", 13f, Vistas.GRIS)
        col.addView(Vistas.margen(lblWifi, c, abajo = 18))

        // PC
        col.addView(Vistas.etiqueta(c, "Tu PC"))
        lblPc = Vistas.texto(c, "", 15f)
        col.addView(Vistas.margen(lblPc, c, arriba = 6, abajo = 8))
        val fpc = Vistas.fila(c)
        fpc.addView(Vistas.boton(c, "Buscar PC", true) { buscar() })
        fpc.addView(Vistas.margen(Vistas.boton(c, "Escribir IP", false) { pedirIp() }, c, izq = 8).also { (it.layoutParams as LinearLayout.LayoutParams).width = ViewGroup.LayoutParams.WRAP_CONTENT })
        fpc.addView(Vistas.margen(Vistas.boton(c, "Olvidar", false) { olvidar() }, c, izq = 8).also { (it.layoutParams as LinearLayout.LayoutParams).width = ViewGroup.LayoutParams.WRAP_CONTENT })
        (fpc.getChildAt(0).layoutParams as? LinearLayout.LayoutParams)?.width = ViewGroup.LayoutParams.WRAP_CONTENT
        col.addView(Vistas.margen(fpc, c, abajo = 22))

        // Burbuja
        col.addView(Vistas.etiqueta(c, "Burbuja flotante"))
        swBurbuja = Switch(c).apply {
            text = "Mostrar ⌨ encima de las otras apps"
            setTextColor(Vistas.BLANCO)
            textSize = 15f
            setOnCheckedChangeListener { _, v -> cambiarBurbuja(v) }
        }
        col.addView(Vistas.margen(swBurbuja, c, arriba = 6))
        lblPermiso = Vistas.texto(c, "", 13f, Vistas.DURAZNO).apply {
            setOnClickListener { pedirPermisoBurbuja() }
        }
        col.addView(Vistas.margen(lblPermiso, c, abajo = 22))

        col.addView(Vistas.boton(c, "Abrir teclado", true) {
            startActivity(Intent(c, TecladoActivity::class.java))
        })

        col.addView(Vistas.margen(Vistas.texto(c,
            "En el PC abre El Mago (Windows) y deja al frente la ventana donde quieres escribir.\n" +
                "No escribe en ventanas abiertas como administrador ni con la sesión bloqueada.\n" +
                "Usa solo redes de confianza: lo que escribes viaja por tu Wi-Fi.",
            13f, Vistas.GRIS), c, arriba = 24))

        return ScrollView(c).apply { setBackgroundColor(Vistas.NOCHE); addView(col) }
    }

    override fun onResume() {
        super.onResume()
        Enlace.usar(this, "pantalla")
        Enlace.oir(oyente)
        refrescar()
    }

    override fun onPause() {
        Enlace.dejarDeOir(oyente)
        Enlace.soltar("pantalla")
        super.onPause()
    }

    private fun refrescar() {
        val pc = almacen.pc
        lblPc.text = if (pc == null) "Ninguno todavía: pulsa «Buscar PC» con El Mago abierto en el PC."
            else "${pc.nombre} · ${pc.ip}" + (if (almacen.llave == null) "\nFalta emparejar con el PIN" else "")
        val wifi = Red.wifi(this)
        val ip = wifi?.let { Red.miIp(this, it)?.first?.hostAddress }
        lblWifi.text = if (wifi == null) "Este celular no está en un Wi-Fi." else "Wi-Fi conectado" + (ip?.let { " · este celular: $it" } ?: "")
        val permiso = Settings.canDrawOverlays(this)
        swBurbuja.setOnCheckedChangeListener(null)
        swBurbuja.isChecked = almacen.burbuja && permiso && MagoService.corriendo
        swBurbuja.setOnCheckedChangeListener { _, v -> cambiarBurbuja(v) }
        lblPermiso.text = if (permiso) "" else "Toca aquí para permitir «Mostrar sobre otras apps»"
        lblPermiso.visibility = if (permiso) View.GONE else View.VISIBLE
        if (almacen.burbuja && permiso && !MagoService.corriendo && almacen.pc != null) MagoService.iniciar(this)
    }

    private fun mostrarEstado(e: Estado) {
        val (t, color) = Vistas.describir(e)
        lblEstado.text = t
        punto.background = Vistas.fondo(this, color, 6)
        refrescarSuave()
    }

    private fun refrescarSuave() {
        main.removeCallbacksAndMessages(null)
        main.postDelayed({ if (!isFinishing) refrescar() }, 300)
    }

    // ---------- Burbuja ----------

    private fun cambiarBurbuja(activar: Boolean) {
        if (activar) {
            if (!Settings.canDrawOverlays(this)) { pedirPermisoBurbuja(); swBurbuja.isChecked = false; return }
            if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
                requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 1)
            }
            almacen.burbuja = true
            MagoService.iniciar(this)
        } else {
            almacen.burbuja = false
            if (MagoService.corriendo) MagoService.detener(this)
        }
    }

    private fun pedirPermisoBurbuja() {
        Toast.makeText(this, "Busca «El Mago» y activa «Permitir mostrar sobre otras apps»", Toast.LENGTH_LONG).show()
        startActivity(Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:$packageName")))
    }

    // ---------- Emparejar ----------

    private fun buscar() {
        if (Red.wifi(this) == null) { aviso("Primero conecta el celular al Wi-Fi de la casa."); return }
        val espera = AlertDialog.Builder(this).setMessage("Buscando El Mago en la red…").setCancelable(false).show()
        Thread {
            val lista = runCatching { Red.buscar(this) }.getOrDefault(emptyList())
            runOnUiThread {
                espera.dismiss()
                if (lista.isEmpty()) {
                    AlertDialog.Builder(this)
                        .setTitle("No encontré ningún PC")
                        .setMessage("Revisa que:\n• El Mago esté abierto en el PC (ícono junto al reloj).\n• El PC y el celular estén en el mismo Wi-Fi.\n• Windows haya permitido El Mago en el firewall.\n\nTambién puedes escribir la IP que muestra la ventana de El Mago.")
                        .setPositiveButton("Escribir IP") { _, _ -> pedirIp() }
                        .setNegativeButton("Cerrar", null)
                        .show()
                } else if (lista.size == 1) pedirPin(lista[0])
                else AlertDialog.Builder(this)
                    .setTitle("Elige tu PC")
                    .setItems(lista.map { "${it.nombre}  ·  ${it.ip}" }.toTypedArray()) { _, i -> pedirPin(lista[i]) }
                    .show()
            }
        }.start()
    }

    private fun pedirIp() {
        val campo = EditText(this).apply { hint = "192.168.1.142"; inputType = InputType.TYPE_CLASS_PHONE }
        AlertDialog.Builder(this)
            .setTitle("IP del PC")
            .setMessage("Está en la ventana de El Mago, en «Este PC».")
            .setView(contenedor(campo))
            .setPositiveButton("Seguir") { _, _ ->
                val d = Protocolo.leerDireccion(campo.text.toString())
                if (d == null) aviso("Esa IP no es válida (ejemplo: 192.168.1.142)")
                else pedirPin(Pc("PC", d.first, d.second, ""))
            }
            .setNegativeButton("Cancelar", null)
            .show()
    }

    private fun pedirPin(pc: Pc) {
        val campo = EditText(this).apply { hint = "123456"; inputType = InputType.TYPE_CLASS_NUMBER }
        AlertDialog.Builder(this)
            .setTitle("Emparejar con ${pc.nombre}")
            .setMessage("Escribe el PIN de 6 cifras que muestra El Mago en el PC.")
            .setView(contenedor(campo))
            .setPositiveButton("Emparejar") { _, _ -> emparejar(pc, campo.text.toString().filter { it.isDigit() }) }
            .setNegativeButton("Cancelar", null)
            .show()
    }

    private fun emparejar(pc: Pc, pin: String) {
        if (pin.length != 6) { aviso("El PIN tiene 6 cifras."); return }
        val red = Red.wifi(this) ?: run { aviso("Sin Wi-Fi."); return }
        Thread {
            val r = runCatching {
                var llave: String? = null
                val s = Red.abrir(this, red, pc, almacen, null, pin) { llave = it }
                val nombre = s.pcNombre.ifBlank { pc.nombre }
                runCatching { s.enviar(Protocolo.ADIOS) }
                s.cerrar()
                Pc(nombre, pc.ip, pc.puerto, s.pcId) to llave!!
            }
            runOnUiThread {
                r.onSuccess { (p, llave) ->
                    almacen.pc = p
                    almacen.llave = llave
                    aviso("Listo: emparejado con ${p.nombre}")
                    Enlace.despertar()
                    if (Settings.canDrawOverlays(this) && almacen.burbuja) MagoService.iniciar(this)
                    refrescar()
                }.onFailure { e ->
                    aviso(when (e) {
                        is Red.Rechazo -> e.motivo
                        is java.net.ConnectException -> "El PC no responde. ¿El Mago está abierto y permitido en el firewall?"
                        is java.net.SocketTimeoutException -> "El PC no contesta. ¿Está en el mismo Wi-Fi?"
                        else -> e.message ?: "No se pudo emparejar"
                    })
                }
            }
        }.start()
    }

    private fun olvidar() {
        if (almacen.pc == null) return
        AlertDialog.Builder(this)
            .setMessage("¿Olvidar el PC? Tendrás que emparejar otra vez con el PIN.")
            .setPositiveButton("Olvidar") { _, _ ->
                almacen.pc = null
                Enlace.despertar()
                refrescar()
            }
            .setNegativeButton("Cancelar", null)
            .show()
    }

    private fun contenedor(v: View): View = LinearLayout(this).apply {
        setPadding(dp(this@MainActivity, 22), dp(this@MainActivity, 4), dp(this@MainActivity, 22), 0)
        addView(v, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
    }

    private fun aviso(m: String) = Toast.makeText(this, m, Toast.LENGTH_LONG).show()
}
