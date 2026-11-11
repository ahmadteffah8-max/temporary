package com.temporary.app

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

class TimerReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent) {
        val pending = goAsync()
        val app = context.applicationContext

        Thread {
            var finished = false
            try {
                if (Runner.claim(app)) {
                    // Make sure the accessibility service is connected, then free the receiver:
                    // the real work takes longer than a receiver is allowed to run.
                    Runner.waitForService()
                    pending.finish()
                    finished = true
                    Runner.execute(app)
                }
            } finally {
                if (!finished) pending.finish()
            }
        }.start()
    }
}
