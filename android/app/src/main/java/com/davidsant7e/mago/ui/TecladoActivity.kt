package com.davidsant7e.mago.ui

import android.app.Activity
import android.os.Bundle
import android.text.Editable
import android.text.InputType
import android.text.TextWatcher
import android.view.Gravity
import android.view.KeyEvent
import android.view.View
import android.view.ViewGroup
import android.view.WindowManager
import android.widget.EditText
import android.widget.HorizontalScrollView
import android.widget.LinearLayout
import android.widget.Switch
import android.widget.TextView
import android.widget.Toast
import com.davidsant7e.mago.red.Almacen
import com.davidsant7e.mago.red.Enlace
import com.davidsant7e.mago.red.Estado
import com.davidsant7e.mago.red.Protocolo
import com.davidsant7e.mago.ui.Vistas.dp

/**
 * El teclado: una ventanita abajo con un campo de texto. Lo que escribes (con tu teclado de siempre,
 * dictado por voz incluido) aparece en la ventana activa del PC.
 *
 * En vivo: cada cambio se manda al instante. Si el autocorrector cambia una palabra de atrás,
 * el PC mueve el cursor, borra y escribe lo justo (ver Protocolo.cambios).
 */
class TecladoActivity : Activity() {

    private lateinit var almacen: Almacen
    private lateinit var campo: EditText
    private lateinit var lblEstado: TextView
    private lateinit var punto: View
    private lateinit var btnEnviar: TextView

    /** Lo que el PC ya tiene escrito de este campo. */
    private var base = ""
    private var ignorar = false

    private val oyente: (Estado) -> Unit = { e -> mostrarEstado(e) }
    private val aviso: (String) -> Unit = { m -> Toast.makeText(this, m, Toast.LENGTH_SHORT).show() }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        almacen = Almacen(this)
        window.setGravity(Gravity.BOTTOM)
        window.setLayout(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
        window.setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_STATE_ALWAYS_VISIBLE or WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE)
        setContentView(construir())
    }

    private fun construir(): View {
        val c = this
        val raiz = Vistas.columna(c).apply {
            background = Vistas.fondo(c, Vistas.NOCHE, 20)
            setPadding(dp(c, 14), dp(c, 12), dp(c, 14), dp(c, 12))
        }

        // Encabezado: estado + cerrar
        val cab = Vistas.fila(c)
        punto = View(c).apply { layoutParams = LinearLayout.LayoutParams(dp(c, 10), dp(c, 10)).apply { marginEnd = dp(c, 8) } }
        lblEstado = Vistas.texto(c, "", 13f, Vistas.CREMA).apply {
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            maxLines = 2
        }
        val cerrar = Vistas.texto(c, "✕", 18f, Vistas.DURAZNO, true).apply {
            setPadding(dp(c, 12), dp(c, 4), dp(c, 4), dp(c, 4))
            setOnClickListener { finish() }
        }
        cab.addView(punto); cab.addView(lblEstado); cab.addView(cerrar)
        raiz.addView(cab)

        // Campo
        campo = EditText(c).apply {
            hint = "Escribe aquí: aparece en el PC"
            setHintTextColor(Vistas.GRIS)
            setTextColor(Vistas.BLANCO)
            textSize = 17f
            background = Vistas.fondo(c, Vistas.NOCHE_CLARA, 12)
            setPadding(dp(c, 12), dp(c, 10), dp(c, 12), dp(c, 10))
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_MULTI_LINE or InputType.TYPE_TEXT_FLAG_CAP_SENTENCES
            minLines = 2
            maxLines = 5
            addTextChangedListener(object : TextWatcher {
                override fun beforeTextChanged(s: CharSequence?, a: Int, b: Int, d: Int) {}
                override fun onTextChanged(s: CharSequence?, a: Int, b: Int, d: Int) {}
                override fun afterTextChanged(s: Editable?) { if (!ignorar && almacen.enVivo) sincronizar() }
            })
            // ⌫ del teclado del celular con el campo vacío: borra en el PC.
            setOnKeyListener { _, codigo, ev ->
                if (codigo == KeyEvent.KEYCODE_DEL && ev.action == KeyEvent.ACTION_DOWN && text.isEmpty()) {
                    mandar(Protocolo.tecla("backspace")); true
                } else false
            }
        }
        raiz.addView(Vistas.margen(campo, c, arriba = 10))

        // Fila de teclas especiales
        val teclas = Vistas.fila(c)
        fun t(etq: String, f: () -> Unit) {
            teclas.addView(Vistas.margen(Vistas.tecla(c, etq, f), c, der = 6).also {
                (it.layoutParams as LinearLayout.LayoutParams).width = ViewGroup.LayoutParams.WRAP_CONTENT
            })
        }
        t("⏎") { enter() }
        t("⌫") { mandar(Protocolo.tecla("backspace")) }
        t("Tab") { mandar(Protocolo.tecla("tab")) }
        t("Esc") { mandar(Protocolo.tecla("esc")) }
        t("←") { mandar(Protocolo.tecla("left")) }
        t("→") { mandar(Protocolo.tecla("right")) }
        t("↑") { mandar(Protocolo.tecla("up")) }
        t("↓") { mandar(Protocolo.tecla("down")) }
        t("Inicio") { mandar(Protocolo.tecla("home")) }
        t("Fin") { mandar(Protocolo.tecla("end")) }
        t("Supr") { mandar(Protocolo.tecla("delete")) }
        t("Ctrl+Z") { mandar(Protocolo.combo("ctrl+z")) }
        t("Ctrl+A") { mandar(Protocolo.combo("ctrl+a")) }
        t("Ctrl+C") { mandar(Protocolo.combo("ctrl+c")) }
        t("Ctrl+V") { mandar(Protocolo.combo("ctrl+v")) }
        t("Ctrl+S") { mandar(Protocolo.combo("ctrl+s")) }
        t("Alt+Tab") { mandar(Protocolo.combo("alt+tab")) }
        val scroll = HorizontalScrollView(c).apply { isHorizontalScrollBarEnabled = false; addView(teclas) }
        raiz.addView(Vistas.margen(scroll, c, arriba = 10))

        // Modo
        val pie = Vistas.fila(c)
        val vivo = Switch(c).apply {
            text = "En vivo"
            setTextColor(Vistas.CREMA)
            isChecked = almacen.enVivo
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            setOnCheckedChangeListener { _, v -> almacen.enVivo = v; modo() }
        }
        val limpiar = Vistas.texto(c, "Limpiar", 14f, Vistas.DURAZNO, true).apply {
            setPadding(dp(c, 12), dp(c, 8), dp(c, 12), dp(c, 8))
            setOnClickListener { reiniciar() }
        }
        btnEnviar = Vistas.boton(c, "Enviar", true) { enviarCampo(false) }
        pie.addView(vivo); pie.addView(limpiar); pie.addView(btnEnviar)
        raiz.addView(Vistas.margen(pie, c, arriba = 8))
        modo()
        return raiz
    }

    private fun modo() {
        btnEnviar.visibility = if (almacen.enVivo) View.GONE else View.VISIBLE
        if (almacen.enVivo) { base = ""; reiniciar() } // empezar limpio para no reenviar lo de antes
    }

    /** En vivo: manda al PC la diferencia entre lo que tiene y lo que hay ahora. */
    private fun sincronizar() {
        val ahora = campo.text.toString()
        if (ahora == base) return
        if (!Enlace.estado.listo) return // se manda al reconectar (con el próximo cambio)
        if (Enlace.enviarTodo(Protocolo.cambios(base, ahora))) base = ahora
        if (base.length > 3000 && campo.selectionEnd == ahora.length) reiniciar() // no acumular texto viejo
    }

    private fun enviarCampo(conEnter: Boolean) {
        val s = campo.text.toString()
        if (!Enlace.estado.listo) { aviso("Sin conexión con el PC"); return }
        val msgs = ArrayList<String>()
        if (almacen.enVivo) msgs += Protocolo.cambios(base, s) else if (s.isNotEmpty()) msgs += Protocolo.cambios("", s)
        if (conEnter) msgs += Protocolo.tecla("enter")
        if (Enlace.enviarTodo(msgs)) reiniciar()
    }

    private fun enter() = enviarCampo(true)

    private fun mandar(m: String) {
        if (almacen.enVivo) sincronizar()
        if (!Enlace.enviar(m)) { aviso("Sin conexión con el PC"); return }
        // Una tecla especial mueve el cursor del PC: lo escrito hasta aquí queda fijo allá.
        if (almacen.enVivo) reiniciar()
    }

    /** Vacía el campo sin borrar nada en el PC. */
    private fun reiniciar() {
        ignorar = true
        campo.setText("")
        base = ""
        ignorar = false
    }

    private fun mostrarEstado(e: Estado) {
        val (t, color) = Vistas.describir(e)
        lblEstado.text = t
        punto.background = Vistas.fondo(this, color, 5)
        campo.alpha = if (e.listo) 1f else 0.6f
        if (e.listo && almacen.enVivo) sincronizar() // lo escrito sin conexión se manda ahora
    }

    override fun onResume() {
        super.onResume()
        Enlace.rapido = true
        Enlace.usar(this, "teclado")
        Enlace.oir(oyente)
        Enlace.oirAvisos(aviso)
        campo.requestFocus()
    }

    override fun onPause() {
        Enlace.dejarDeOir(oyente)
        Enlace.dejarAvisos(aviso)
        Enlace.rapido = false
        Enlace.soltar("teclado")
        super.onPause()
    }
}
