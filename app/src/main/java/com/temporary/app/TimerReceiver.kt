package com.temporary.app

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

class TimerReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent) {
        val pending = goAsync()
        val app = context.applicationContext

        Thread {
            try {
                Runner.runDue(app)
            } finally {
                pending.finish()
            }
        }.start()
    }
}
