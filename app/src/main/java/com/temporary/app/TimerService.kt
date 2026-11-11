package com.temporary.app

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Keeps the app process alive while a timer is pending (some phones kill background apps and
 * drop their alarms). It also acts as a backup: if the alarm did not fire, it runs the job itself.
 */
class TimerService : Service() {

    private val handler = Handler(Looper.getMainLooper())

    private val check = object : Runnable {
        override fun run() {
            val target = Scheduler.prefs(this@TimerService).getLong("target", 0L)
            if (target <= 0L) {
                stopSelf()
                return
            }
            if (System.currentTimeMillis() >= target + 3000L) {
                val app = applicationContext
                Thread { Runner.runDue(app) }.start()
            }
            handler.postDelayed(this, 10_000L)
        }
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (!startInForeground()) {
            stopSelf()
            return START_NOT_STICKY
        }
        handler.removeCallbacks(check)
        handler.post(check)
        return START_STICKY
    }

    override fun onDestroy() {
        handler.removeCallbacksAndMessages(null)
        super.onDestroy()
    }

    @Suppress("DEPRECATION")
    private fun startInForeground(): Boolean {
        return try {
            val nm = getSystemService(NOTIFICATION_SERVICE) as NotificationManager
            if (Build.VERSION.SDK_INT >= 26) {
                nm.createNotificationChannel(
                    NotificationChannel(CHANNEL, "المؤقت", NotificationManager.IMPORTANCE_LOW)
                )
            }
            val open = PendingIntent.getActivity(
                this,
                3,
                Intent(this, MainActivity::class.java),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
            )
            val target = Scheduler.prefs(this).getLong("target", 0L)
            val timeText = SimpleDateFormat("HH:mm", Locale.US).format(Date(target))

            val builder = if (Build.VERSION.SDK_INT >= 26) {
                Notification.Builder(this, CHANNEL)
            } else {
                Notification.Builder(this)
            }
            val notification = builder
                .setSmallIcon(android.R.drawable.ic_lock_idle_alarm)
                .setContentTitle("مؤقت القط شغال")
                .setContentText("ينطفي الساعة $timeText")
                .setContentIntent(open)
                .setOngoing(true)
                .build()

            if (Build.VERSION.SDK_INT >= 34) {
                startForeground(1, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE)
            } else {
                startForeground(1, notification)
            }
            true
        } catch (t: Throwable) {
            false
        }
    }

    companion object {
        private const val CHANNEL = "timer"
    }
}
