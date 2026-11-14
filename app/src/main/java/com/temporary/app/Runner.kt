package com.temporary.app

import android.content.Context
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/** Glue between the alarm / test button and the accessibility service. */
object Runner {

    private val lock = Any()

    /** Only the first caller gets true for a given timer. */
    fun claim(ctx: Context): Boolean {
        val prefs = Scheduler.prefs(ctx)
        synchronized(lock) {
            if (prefs.getLong("target", 0L) <= 0L) return false
            prefs.edit().putLong("target", 0L).commit()
            return true
        }
    }

    /** After a cold start by the alarm the system needs a moment to connect the accessibility service. */
    fun waitForService(maxMs: Long = 5000L): ToggleService? {
        val end = System.currentTimeMillis() + maxMs
        var svc = ToggleService.instance
        while (svc == null && System.currentTimeMillis() < end) {
            Thread.sleep(200)
            svc = ToggleService.instance
        }
        return svc
    }

    fun execute(ctx: Context) {
        val p = Scheduler.prefs(ctx)
        applyNow(
            ctx,
            p.getBoolean("wifi", false),
            p.getBoolean("data", false),
            p.getBoolean("bt", false),
            p.getBoolean("screen", false)
        )
    }

    fun applyNow(ctx: Context, wifi: Boolean, data: Boolean, bt: Boolean, screen: Boolean): String {
        val svc = waitForService()
        val report = if (svc == null) {
            "خدمة إمكانية الوصول مو شغالة، فعّلها من الإعدادات"
        } else {
            svc.turnOff(wifi, data, bt, screen)
        }
        val time = SimpleDateFormat("HH:mm", Locale.US).format(Date())
        Scheduler.prefs(ctx).edit()
            .putString("last", "آخر تنفيذ $time — $report")
            .apply()
        return report
    }
}
