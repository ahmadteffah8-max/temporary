package com.temporary.app

import android.annotation.SuppressLint
import android.bluetooth.BluetoothAdapter
import android.content.Context
import android.net.wifi.WifiManager
import android.os.Build
import android.provider.Settings

/** Reads the real on/off state of Wi-Fi, mobile data and Bluetooth (no special permission needed). */
object State {

    fun wifiOn(ctx: Context): Boolean = try {
        val wm = ctx.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
        val s = wm.wifiState
        s == WifiManager.WIFI_STATE_ENABLED || s == WifiManager.WIFI_STATE_ENABLING
    } catch (t: Throwable) {
        true
    }

    fun dataOn(ctx: Context): Boolean = try {
        val cr = ctx.contentResolver
        val v = Settings.Global.getInt(cr, "mobile_data", -1)
        if (v >= 0) {
            v == 1
        } else {
            Settings.Global.getInt(cr, "mobile_data1", 0) == 1 ||
                Settings.Global.getInt(cr, "mobile_data2", 0) == 1
        }
    } catch (t: Throwable) {
        true
    }

    fun btOn(ctx: Context): Boolean = try {
        Settings.Global.getInt(ctx.contentResolver, "bluetooth_on", 0) != 0
    } catch (t: Throwable) {
        true
    }
}

/** The old direct APIs. They still work on old Android and are tried first because they are quick. */
object Direct {

    @Suppress("DEPRECATION")
    fun wifiOff(ctx: Context): Boolean {
        if (Build.VERSION.SDK_INT >= 29) return false
        return try {
            val wm = ctx.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
            wm.setWifiEnabled(false)
        } catch (t: Throwable) {
            false
        }
    }

    @SuppressLint("MissingPermission")
    @Suppress("DEPRECATION")
    fun bluetoothOff(): Boolean {
        if (Build.VERSION.SDK_INT >= 31) return false
        return try {
            val adapter = BluetoothAdapter.getDefaultAdapter()
            if (adapter == null) false else adapter.disable()
        } catch (t: Throwable) {
            false
        }
    }
}
