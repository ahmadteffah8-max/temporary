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
        p.errorStream.bufferedReader().readText()
        p.waitFor() == 0
    } catch (t: Throwable) {
        false
    }

    @Suppress("DEPRECATION")
    private fun wifiLegacy(ctx: Context): Boolean {
        if (Build.VERSION.SDK_INT >= 29) return false
        return try {
            val wm = ctx.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
            if (wm.isWifiEnabled) wm.setWifiEnabled(false) else true
        } catch (t: Throwable) {
            false
        }
    }

    @SuppressLint("MissingPermission")
    @Suppress("DEPRECATION")
    private fun bluetoothLegacy(): Boolean {
        if (Build.VERSION.SDK_INT >= 31) return false
        return try {
            val a = BluetoothAdapter.getDefaultAdapter() ?: return false
            if (a.isEnabled) a.disable() else true
        } catch (t: Throwable) {
            false
        }
    }

    /** Applies the choice and returns a short Arabic report. */
    fun apply(ctx: Context, wifi: Boolean, data: Boolean, bt: Boolean): String {
        val viaShizuku = state() == READY
        val ok = mutableListOf<String>()
        val failed = mutableListOf<String>()

        fun record(name: String, success: Boolean) {
            if (success) ok.add(name) else failed.add(name)
        }

        if (wifi) {
            record("الواي فاي", (viaShizuku && sh("svc wifi disable")) || wifiLegacy(ctx))
        }
        if (data) {
            record("البيانات", viaShizuku && sh("svc data disable"))
        }
        if (bt) {
            val shellOk = viaShizuku &&
                sh("svc bluetooth disable || cmd bluetooth_manager disable")
            record("البلوتث", shellOk || bluetoothLegacy())
        }

        val sb = StringBuilder()
        if (ok.isNotEmpty()) sb.append("انطفى: ").append(ok.joinToString("، "))
        if (failed.isNotEmpty()) {
            if (sb.isNotEmpty()) sb.append(" | ")
            sb.append("ما اشتغل: ").append(failed.joinToString("، "))
            if (!viaShizuku) sb.append(" (Shizuku مو شغال)")
        }
        return sb.toString()
    }
}
