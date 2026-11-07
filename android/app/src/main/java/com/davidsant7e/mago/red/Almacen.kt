package com.davidsant7e.mago.red

import android.content.Context
import android.os.Build
import android.provider.Settings
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyStore
import java.util.UUID
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/** El PC emparejado. */
data class Pc(val nombre: String, val ip: String, val puerto: Int, val id: String)

/**
 * Lo que la app recuerda: el PC elegido y la llave que ese PC le dio al emparejar.
 * La llave se guarda cifrada con el Android Keystore (no sale del teléfono en claro).
 */
class Almacen(context: Context) {
    private val c = context.applicationContext
    private val p = c.getSharedPreferences("mago", Context.MODE_PRIVATE)

    /** Identificador de este celular para el PC (al azar, se crea una vez). */
    val idCelular: String
        get() = p.getString("id", null) ?: UUID.randomUUID().toString().replace("-", "").also { p.edit().putString("id", it).apply() }

    val nombreCelular: String
        get() = Settings.Global.getString(c.contentResolver, Settings.Global.DEVICE_NAME)?.takeIf { it.isNotBlank() }
            ?: "${Build.MANUFACTURER.replaceFirstChar { it.uppercase() }} ${Build.MODEL}"

    var pc: Pc?
        get() {
            val ip = p.getString("pc_ip", null) ?: return null
            return Pc(p.getString("pc_nombre", "PC") ?: "PC", ip, p.getInt("pc_puerto", Protocolo.PUERTO_TCP), p.getString("pc_id", "") ?: "")
        }
        set(v) {
            val e = p.edit()
            if (v == null) e.remove("pc_ip").remove("pc_nombre").remove("pc_puerto").remove("pc_id").remove("llave")
            else e.putString("pc_ip", v.ip).putString("pc_nombre", v.nombre).putInt("pc_puerto", v.puerto).putString("pc_id", v.id)
            e.apply()
        }

    var llave: String?
        get() = p.getString("llave", null)?.let { runCatching { descifrar(it) }.getOrNull() }
        set(v) { p.edit().apply { if (v == null) remove("llave") else putString("llave", cifrar(v)) }.apply() }

    var burbuja: Boolean
        get() = p.getBoolean("burbuja", true)
        set(v) = p.edit().putBoolean("burbuja", v).apply()

    /** true = cada letra va al PC al instante; false = se manda al pulsar Enviar. */
    var enVivo: Boolean
        get() = p.getBoolean("en_vivo", true)
        set(v) = p.edit().putBoolean("en_vivo", v).apply()

    var burbujaX: Int
        get() = p.getInt("bx", -1)
        set(v) = p.edit().putInt("bx", v).apply()
    var burbujaY: Int
        get() = p.getInt("by", 300)
        set(v) = p.edit().putInt("by", v).apply()

    // ---------- Cifrado con el Keystore ----------

    private fun clave(): SecretKey {
        val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (ks.getKey(ALIAS, null) as? SecretKey)?.let { return it }
        val gen = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        gen.init(
            KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .build()
        )
        return gen.generateKey()
    }

    private fun cifrar(s: String): String {
        val ci = Cipher.getInstance("AES/GCM/NoPadding")
        ci.init(Cipher.ENCRYPT_MODE, clave())
        val datos = ci.iv + ci.doFinal(s.toByteArray(Charsets.UTF_8))
        return Base64.encodeToString(datos, Base64.NO_WRAP)
    }

    private fun descifrar(s: String): String {
        val datos = Base64.decode(s, Base64.NO_WRAP)
        val ci = Cipher.getInstance("AES/GCM/NoPadding")
        ci.init(Cipher.DECRYPT_MODE, clave(), GCMParameterSpec(128, datos, 0, 12))
        return String(ci.doFinal(datos, 12, datos.size - 12), Charsets.UTF_8)
    }

    private companion object { const val ALIAS = "mago-llave" }
}
