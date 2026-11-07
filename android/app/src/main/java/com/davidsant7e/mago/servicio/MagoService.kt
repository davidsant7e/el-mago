package com.davidsant7e.mago.servicio

import android.annotation.SuppressLint
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.graphics.PixelFormat
import android.graphics.drawable.GradientDrawable
import android.os.Build
import android.os.IBinder
import android.provider.Settings
import android.util.TypedValue
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.WindowManager
import android.widget.TextView
import com.davidsant7e.mago.R
import com.davidsant7e.mago.red.Almacen
import com.davidsant7e.mago.red.Enlace
import com.davidsant7e.mago.red.Estado
import com.davidsant7e.mago.ui.MainActivity
import com.davidsant7e.mago.ui.TecladoActivity
import com.davidsant7e.mago.ui.Vistas
import kotlin.math.abs

/**
 * Mantiene la conexión con el PC y la burbuja flotante encima de las otras apps.
 * Tocar la burbuja abre el teclado; arrastrarla la mueve (se pega al borde).
 */
class MagoService : Service() {

    private var burbuja: TextView? = null
    private var wm: WindowManager? = null
    private lateinit var almacen: Almacen
    private val oyente: (Estado) -> Unit = { e -> pintar(e); notificar(e) }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        almacen = Almacen(this)
        wm = getSystemService(WindowManager::class.java)
        crearCanal()
        val n = notificacion(Enlace.estado)
        if (Build.VERSION.SDK_INT >= 34) startForeground(NOTI, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE)
        else startForeground(NOTI, n)
        Enlace.usar(this, "burbuja")
        Enlace.oir(oyente)
        corriendo = true
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACCION_DETENER -> { almacen.burbuja = false; stopSelf(); return START_NOT_STICKY }
            ACCION_OCULTAR -> quitarBurbuja()
            else -> if (almacen.burbuja) mostrarBurbuja()
        }
        return START_STICKY
    }

    override fun onDestroy() {
        corriendo = false
        Enlace.dejarDeOir(oyente)
        Enlace.soltar("burbuja")
        quitarBurbuja()
        super.onDestroy()
    }

    // ---------- Burbuja ----------

    @SuppressLint("ClickableViewAccessibility")
    private fun mostrarBurbuja() {
        if (burbuja != null || !Settings.canDrawOverlays(this)) return
        val tam = Vistas.dp(this, 54)
        val v = TextView(this).apply {
            text = "⌨"
            gravity = Gravity.CENTER
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 24f)
            setTextColor(Vistas.DURAZNO)
            contentDescription = "Teclado del PC"
            elevation = Vistas.dp(this@MagoService, 6).toFloat()
        }
        val lp = WindowManager.LayoutParams(
            tam, tam,
            WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
            PixelFormat.TRANSLUCENT
        ).apply {
            gravity = Gravity.TOP or Gravity.START
            val ancho = resources.displayMetrics.widthPixels
            x = if (almacen.burbujaX < 0) ancho - tam - Vistas.dp(this@MagoService, 4) else almacen.burbujaX.coerceIn(0, ancho - tam)
            y = almacen.burbujaY.coerceIn(0, resources.displayMetrics.heightPixels - tam)
        }
        var x0 = 0; var y0 = 0; var tx = 0f; var ty = 0f; var movio = false
        v.setOnTouchListener { _, ev ->
            when (ev.action) {
                MotionEvent.ACTION_DOWN -> { x0 = lp.x; y0 = lp.y; tx = ev.rawX; ty = ev.rawY; movio = false; true }
                MotionEvent.ACTION_MOVE -> {
                    val dx = ev.rawX - tx; val dy = ev.rawY - ty
                    if (abs(dx) > 12 || abs(dy) > 12) movio = true
                    if (movio) { lp.x = x0 + dx.toInt(); lp.y = y0 + dy.toInt(); runCatching { wm?.updateViewLayout(v, lp) } }
                    true
                }
                MotionEvent.ACTION_UP -> {
                    if (movio) {
                        val ancho = resources.displayMetrics.widthPixels
                        lp.x = if (lp.x + tam / 2 < ancho / 2) 0 else ancho - tam // se pega al borde
                        runCatching { wm?.updateViewLayout(v, lp) }
                        almacen.burbujaX = lp.x; almacen.burbujaY = lp.y
                    } else abrirTeclado()
                    true
                }
                else -> false
            }
        }
        runCatching { wm?.addView(v, lp); burbuja = v }
        pintar(Enlace.estado)
    }

    private fun quitarBurbuja() {
        burbuja?.let { runCatching { wm?.removeView(it) } }
        burbuja = null
    }

    private fun pintar(e: Estado) {
        val v = burbuja ?: return
        val anillo = when {
            e.listo -> Vistas.VERDE
            e is Estado.Conectando || e is Estado.Apagado -> Vistas.GRIS
            else -> Vistas.DURAZNO
        }
        v.background = GradientDrawable().apply {
            shape = GradientDrawable.OVAL
            setColor(Vistas.NOCHE)
            setStroke(Vistas.dp(this@MagoService, 3), anillo)
        }
        v.alpha = if (e.listo) 1f else 0.75f
    }

    private fun abrirTeclado() {
        startActivity(Intent(this, TecladoActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP))
    }

    // ---------- Notificación ----------

    private fun crearCanal() {
        val nm = getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(NotificationChannel(CANAL, "El Mago activo", NotificationManager.IMPORTANCE_LOW).apply {
            description = "Muestra si el celular está conectado al PC"
            setShowBadge(false)
        })
    }

    private fun notificacion(e: Estado): Notification {
        val (texto, _) = Vistas.describir(e)
        val pi = { i: Intent, code: Int, act: Boolean ->
            if (act) PendingIntent.getActivity(this, code, i, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
            else PendingIntent.getService(this, code, i, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        }
        val abrir = pi(Intent(this, MainActivity::class.java), 1, true)
        val teclado = pi(Intent(this, TecladoActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK), 2, true)
        val detener = pi(Intent(this, MagoService::class.java).setAction(ACCION_DETENER), 3, false)
        return Notification.Builder(this, CANAL)
            .setSmallIcon(R.drawable.ic_notificacion)
            .setContentTitle("El Mago")
            .setContentText(texto.lineSequence().first())
            .setStyle(Notification.BigTextStyle().bigText(texto))
            .setContentIntent(abrir)
            .setOngoing(true)
            .setShowWhen(false)
            .addAction(Notification.Action.Builder(null, "Teclado", teclado).build())
            .addAction(Notification.Action.Builder(null, "Detener", detener).build())
            .build()
    }

    private fun notificar(e: Estado) {
        runCatching { getSystemService(NotificationManager::class.java).notify(NOTI, notificacion(e)) }
    }

    companion object {
        private const val CANAL = "mago"
        private const val NOTI = 7
        const val ACCION_DETENER = "com.davidsant7e.mago.DETENER"
        const val ACCION_OCULTAR = "com.davidsant7e.mago.OCULTAR"
        @Volatile var corriendo = false
            private set

        fun iniciar(c: Context) {
            c.startForegroundService(Intent(c, MagoService::class.java))
        }

        fun detener(c: Context) {
            c.startService(Intent(c, MagoService::class.java).setAction(ACCION_DETENER))
        }
    }
}
