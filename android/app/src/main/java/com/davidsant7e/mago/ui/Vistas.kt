package com.davidsant7e.mago.ui

import android.content.Context
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import com.davidsant7e.mago.red.Estado

/** Colores y piezas de la interfaz, en el duotono de los Arcanos. Sin librerías: vistas de Android. */
object Vistas {
    val NOCHE = Color.rgb(0x1f, 0x2a, 0x5c)
    val NOCHE_CLARA = Color.rgb(0x2b, 0x38, 0x73)
    val DURAZNO = Color.rgb(0xf4, 0xa5, 0x82)
    val CREMA = Color.rgb(0xfb, 0xe9, 0xdf)
    val VERDE = Color.rgb(0x9d, 0xe0, 0xad)
    val GRIS = Color.rgb(0x8a, 0x93, 0xb8)
    val BLANCO = Color.WHITE

    fun dp(c: Context, v: Int): Int =
        TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v.toFloat(), c.resources.displayMetrics).toInt()

    fun fondo(c: Context, color: Int, radio: Int = 16, borde: Int? = null): GradientDrawable =
        GradientDrawable().apply {
            setColor(color)
            cornerRadius = dp(c, radio).toFloat()
            if (borde != null) setStroke(dp(c, 1), borde)
        }

    fun texto(c: Context, t: CharSequence, sp: Float = 15f, color: Int = BLANCO, negrita: Boolean = false): TextView =
        TextView(c).apply {
            text = t
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp)
            setTextColor(color)
            if (negrita) setTypeface(typeface, Typeface.BOLD)
        }

    fun etiqueta(c: Context, t: String): TextView =
        texto(c, t.uppercase(), 11f, DURAZNO, negrita = true).apply { letterSpacing = 0.08f }

    fun boton(c: Context, t: String, principal: Boolean, alPulsar: () -> Unit): Button =
        Button(c).apply {
            text = t
            isAllCaps = false
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 14f)
            setTextColor(if (principal) NOCHE else DURAZNO)
            background = fondo(c, if (principal) DURAZNO else Color.TRANSPARENT, 20, DURAZNO)
            minHeight = dp(c, 40)
            minimumHeight = dp(c, 40)
            setPadding(dp(c, 16), 0, dp(c, 16), 0)
            stateListAnimator = null
            setOnClickListener { alPulsar() }
        }

    /** Tecla pequeña de la fila del teclado (⌫, ⏎, ←…). */
    fun tecla(c: Context, t: String, alPulsar: () -> Unit): TextView =
        texto(c, t, 15f, CREMA, negrita = true).apply {
            gravity = Gravity.CENTER
            background = fondo(c, NOCHE_CLARA, 10)
            minWidth = dp(c, 44)
            setPadding(dp(c, 10), dp(c, 10), dp(c, 10), dp(c, 10))
            isClickable = true
            setOnClickListener { alPulsar() }
        }

    fun columna(c: Context): LinearLayout = LinearLayout(c).apply { orientation = LinearLayout.VERTICAL }

    fun fila(c: Context): LinearLayout = LinearLayout(c).apply {
        orientation = LinearLayout.HORIZONTAL
        gravity = Gravity.CENTER_VERTICAL
    }

    fun margen(v: View, c: Context, arriba: Int = 0, abajo: Int = 0, izq: Int = 0, der: Int = 0): View {
        val lp = (v.layoutParams as? LinearLayout.LayoutParams)
            ?: LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT)
        lp.setMargins(dp(c, izq), dp(c, arriba), dp(c, der), dp(c, abajo))
        v.layoutParams = lp
        return v
    }

    /** Texto y color para mostrar el estado de la conexión. */
    fun describir(e: Estado): Pair<String, Int> = when (e) {
        Estado.Apagado -> "Apagado" to GRIS
        Estado.SinWifi -> "Sin Wi-Fi: conéctate a la red de la casa" to DURAZNO
        Estado.SinPc -> "Falta elegir el PC" to DURAZNO
        Estado.NoEmparejado -> "El PC no reconoce este celular: empareja otra vez con el PIN" to DURAZNO
        is Estado.Conectando -> ("Buscando ${e.pc}…" + (e.detalle?.let { "\n$it" } ?: "")) to GRIS
        is Estado.Conectado -> if (e.pausado) "${e.pc}: en pausa (desde el PC)" to DURAZNO
            else ("Conectado a ${e.pc}" + (if (e.ventana.isNotBlank()) "\nEscribiendo en: ${e.ventana}" else "")) to VERDE
    }
}
