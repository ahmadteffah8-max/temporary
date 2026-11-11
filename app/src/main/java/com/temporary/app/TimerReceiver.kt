package com.temporary.app

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

class TimerReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent) {
        val pending = goAsync()
        val app = context.applicationContext

        Thread {
            try {
                val prefs = Scheduler.prefs(app)

                // If the process was just started by the alarm, give Shizuku a moment to connect.
                var tries = 0
                while (!Switcher.binderAlive() && tries < 30) {
                    Thread.sleep(200)
                    tries++
                }

                val report = Switcher.apply(
                    app,
                    prefs.getBoolean("wifi", false),
                    prefs.getBoolean("data", false),
                    prefs.getBoolean("bt", false)
                )
                val time = SimpleDateFormat("HH:mm", Locale.US).format(Date())

                prefs.edit()
                    .putLong("target", 0L)
                    .putString("last", "آخر تنفيذ $time — $report")
                    .apply()
            } finally {
                pending.finish()
            }
        }.start()
    }
}
