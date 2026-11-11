package com.temporary.app

import android.annotation.SuppressLint
import android.bluetooth.BluetoothAdapter
import android.content.Context
import android.content.pm.PackageManager
import android.net.wifi.WifiManager
import android.os.Build
import rikka.shizuku.Shizuku

/**
 * Turns Wi-Fi / mobile data / Bluetooth off.
 *
 * Main path: shell commands through Shizuku (works on every Android version Shizuku supports).
 * Fallback: the old public APIs, which still work for Wi-Fi up to Android 9 and
 * for Bluetooth up to Android 11 when Shizuku is not running.
 */
object Switcher {

    const val NOT_RUNNING = 0
    const val NEED_PERMISSION = 1
    const val READY = 2

    /** Short description of the last failure, shown to the user in the report. */
    @Volatile
    private var lastErr = ""

    fun binderAlive(): Boolean = try {
        Shizuku.pingBinder()
    } catch (t: Throwable) {
        false
    }

    fun state(): Int = try {
        when {
            !Shizuku.pingBinder() -> NOT_RUNNING
            Shizuku.isPreV11() -> NOT_RUNNING
            Shizuku.checkSelfPermission() == PackageManager.PERMISSION_GRANTED -> READY
            else -> NEED_PERMISSION
        }
    } catch (t: Throwable) {
        NOT_RUNNING
    }

    private fun describe(t: Throwable): String {
        val root = t.cause ?: t
        return (root.javaClass.simpleName + ": " + (root.message ?: "")).take(80)
    }

    /** Runs a shell command as the Shizuku user (shell/root). Returns true when the exit code is 0. */
    private fun sh(cmd: String): Boolean = try {
        // newProcess is not part of Shizuku's public API any more, so call it by reflection.
        val m = Shizuku::class.java.getDeclaredMethod(
            "newProcess",
            Array<String>::class.java,
            Array<String>::class.java,
            String::class.java
        )
        m.isAccessible = true
        val p = m.invoke(null, arrayOf("sh", "-c", cmd), null, null) as Process
        p.inputStream.bufferedReader().readText()
        val err = p.errorStream.bufferedReader().readText().trim()
        val code = p.waitFor()
        if (code != 0) lastErr = "exit $code ${err.take(60)}".trim()
        code == 0
    } catch (t: Throwable) {
        lastErr = describe(t)
        false
    }

    @Suppress("DEPRECATION")
    private fun wifiLegacy(ctx: Context): Boolean {
        if (Build.VERSION.SDK_INT >= 29) {
            lastErr = "أندرويد 10+ يمنع الطريقة العادية"
            return false
        }
        return try {
            val wm = ctx.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
            if (!wm.isWifiEnabled) {
                true
            } else {
                val ok = wm.setWifiEnabled(false)
                if (!ok) lastErr = "النظام رفض الأمر"
                ok
            }
        } catch (t: Throwable) {
            lastErr = describe(t)
            false
        }
    }

    @SuppressLint("MissingPermission")
    @Suppress("DEPRECATION")
    private fun bluetoothLegacy(): Boolean {
        if (Build.VERSION.SDK_INT >= 31) {
            lastErr = "أندرويد 12+ يمنع الطريقة العادية"
            return false
        }
        return try {
            val a = BluetoothAdapter.getDefaultAdapter()
            if (a == null) {
                lastErr = "ما في بلوتث"
                false
            } else if (!a.isEnabled) {
                true
            } else {
                val ok = a.disable()
                if (!ok) lastErr = "النظام رفض الأمر"
                ok
            }
        } catch (t: Throwable) {
            lastErr = describe(t)
            false
        }
    }

    /** Applies the choice and returns a short Arabic report. */
    fun apply(ctx: Context, wifi: Boolean, data: Boolean, bt: Boolean): String {
        val viaShizuku = state() == READY
        val ok = mutableListOf<String>()
        val failed = mutableListOf<String>()

        fun record(name: String, success: Boolean) {
            if (success) {
                ok.add(name)
            } else {
                failed.add("$name (${lastErr.ifBlank { "سبب غير معروف" }})")
            }
        }

        if (wifi) {
            lastErr = ""
            record("الواي فاي", (viaShizuku && sh("svc wifi disable")) || wifiLegacy(ctx))
        }
        if (data) {
            lastErr = ""
            if (!viaShizuku) lastErr = "يحتاج Shizuku شغال"
            record("البيانات", viaShizuku && sh("svc data disable"))
        }
        if (bt) {
            lastErr = ""
            val shellOk = viaShizuku &&
                sh("svc bluetooth disable || cmd bluetooth_manager disable")
            record("البلوتث", shellOk || bluetoothLegacy())
        }

        val sb = StringBuilder()
        if (ok.isNotEmpty()) sb.append("انطفى: ").append(ok.joinToString("، "))
        if (failed.isNotEmpty()) {
            if (sb.isNotEmpty()) sb.append(" | ")
            sb.append("ما اشتغل: ").append(failed.joinToString("، "))
        }
        return sb.toString()
    }
}
