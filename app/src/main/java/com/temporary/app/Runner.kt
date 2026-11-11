package com.temporary.app

import android.content.Context
import android.content.Intent
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/** Runs the switch-off work. Used by the alarm receiver, the keep-alive service and the test button. */
object Runner {

    private val lock = Any()

    /**
     * Called when the timer is due. Only the first caller does the work
     * (both the alarm and the service may call this), later calls do nothing.
     */
    fun runDue(ctx: Context): Boolean {
        val prefs = Scheduler.prefs(ctx)
        synchronized(lock) {
            if (prefs.getLong("target", 0L) <= 0L) return false
            prefs.edit().putLong("target", 0L).commit()
        }
        applyNow(
            ctx,
            prefs.getBoolean("wifi", false),
            prefs.getBoolean("data", false),
            prefs.getBoolean("bt", false),
            waitForShizuku = true
        )
        ctx.stopService(Intent(ctx, TimerService::class.java))
        return true
    }

    fun applyNow(
        ctx: Context,
        wifi: Boolean,
        data: Boolean,
        bt: Boolean,
        waitForShizuku: Boolean
    ): String {
        // If the process was just started by the alarm, give Shizuku a moment to connect.
        if (waitForShizuku) {
            var tries = 0
            while (!Switcher.binderAlive() && tries < 20) {
                Thread.sleep(200)
                tries++
            }
        }
        val report = Switcher.apply(ctx, wifi, data, bt)
        val time = SimpleDateFormat("HH:mm", Locale.US).format(Date())
        Scheduler.prefs(ctx).edit()
            .putString("last", "آخر تنفيذ $time — $report")
            .apply()
        return report
    }
}
