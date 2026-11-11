package com.temporary.app

import android.app.Activity
import android.content.ComponentName
import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import android.view.View
import android.widget.Button
import android.widget.CheckBox
import android.widget.EditText
import android.widget.RadioButton
import android.widget.TextView
import android.widget.TimePicker
import android.widget.Toast
import java.util.Calendar
import java.util.Locale

class MainActivity : Activity() {

    private lateinit var tvAccess: TextView
    private lateinit var btnAccess: Button
    private lateinit var cbWifi: CheckBox
    private lateinit var cbData: CheckBox
    private lateinit var cbBt: CheckBox
    private lateinit var rbAfter: RadioButton
    private lateinit var rbAt: RadioButton
    private lateinit var panelAfter: View
    private lateinit var etMinutes: EditText
    private lateinit var timePicker: TimePicker
    private lateinit var tvStatus: TextView
    private lateinit var tvLast: TextView

    private val handler = Handler(Looper.getMainLooper())
    private val ticker = object : Runnable {
        override fun run() {
            refreshStatus()
            handler.postDelayed(this, 1000)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        tvAccess = findViewById(R.id.tvAccess)
        btnAccess = findViewById(R.id.btnAccess)
        cbWifi = findViewById(R.id.cbWifi)
        cbData = findViewById(R.id.cbData)
        cbBt = findViewById(R.id.cbBt)
        rbAfter = findViewById(R.id.rbAfter)
        rbAt = findViewById(R.id.rbAt)
        panelAfter = findViewById(R.id.panelAfter)
        etMinutes = findViewById(R.id.etMinutes)
        timePicker = findViewById(R.id.timePicker)
        tvStatus = findViewById(R.id.tvStatus)
        tvLast = findViewById(R.id.tvLast)

        timePicker.setIs24HourView(true)

        val prefs = Scheduler.prefs(this)
        cbWifi.isChecked = prefs.getBoolean("wifi", true)
        cbData.isChecked = prefs.getBoolean("data", false)
        cbBt.isChecked = prefs.getBoolean("bt", false)

        rbAfter.setOnClickListener { updateMode() }
        rbAt.setOnClickListener { updateMode() }

        findViewById<Button>(R.id.btn5).setOnClickListener { etMinutes.setText("5") }
        findViewById<Button>(R.id.btn15).setOnClickListener { etMinutes.setText("15") }
        findViewById<Button>(R.id.btn30).setOnClickListener { etMinutes.setText("30") }
        findViewById<Button>(R.id.btn60).setOnClickListener { etMinutes.setText("60") }

        findViewById<Button>(R.id.btnStart).setOnClickListener { startTimer() }
        findViewById<Button>(R.id.btnCancel).setOnClickListener { cancelTimer() }
        findViewById<Button>(R.id.btnTest).setOnClickListener { testNow() }

        btnAccess.setOnClickListener {
            try {
                startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS))
            } catch (t: Throwable) {
                toast("افتح الإعدادات ← إمكانية الوصول يدوياً")
            }
        }

        updateMode()
    }

    override fun onResume() {
        super.onResume()
        refreshAccess()
        handler.post(ticker)
    }

    override fun onPause() {
        handler.removeCallbacks(ticker)
        super.onPause()
    }

    private fun updateMode() {
        val after = rbAfter.isChecked
        panelAfter.visibility = if (after) View.VISIBLE else View.GONE
        timePicker.visibility = if (after) View.GONE else View.VISIBLE
    }

    private fun accessibilityEnabled(): Boolean {
        val enabled = Settings.Secure.getString(
            contentResolver,
            Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES
        ) ?: return false
        val component = ComponentName(this, ToggleService::class.java)
        val full = component.flattenToString()
        val short = component.flattenToShortString()
        return enabled.split(':').any { it.equals(full, true) || it.equals(short, true) }
    }

    private fun refreshAccess() {
        if (accessibilityEnabled()) {
            tvAccess.text = "خدمة إمكانية الوصول شغالة ✓"
            btnAccess.visibility = View.GONE
        } else {
            tvAccess.text = "لازم تفعّل خدمة «مؤقت القط» من إعدادات إمكانية الوصول (الخدمات المنزّلة). " +
                "لو الخيار رمادي في أندرويد 13+: معلومات التطبيق ← ⋮ ← السماح بالإعدادات المقيّدة."
            btnAccess.visibility = View.VISIBLE
        }
    }

    private fun startTimer() {
        val wifi = cbWifi.isChecked
        val data = cbData.isChecked
        val bt = cbBt.isChecked
        if (!wifi && !data && !bt) {
            toast("اختر شي واحد على الأقل")
            return
        }
        if (!accessibilityEnabled()) {
            toast("فعّل خدمة إمكانية الوصول أول")
            return
        }

        val now = System.currentTimeMillis()
        val target: Long
        if (rbAfter.isChecked) {
            val minutes = etMinutes.text.toString().toLongOrNull() ?: 0L
            if (minutes <= 0L) {
                toast("اكتب عدد دقائق صحيح")
                return
            }
            target = now + minutes * 60_000L
        } else {
            val cal = Calendar.getInstance().apply {
                set(Calendar.HOUR_OF_DAY, timePicker.hour)
                set(Calendar.MINUTE, timePicker.minute)
                set(Calendar.SECOND, 0)
                set(Calendar.MILLISECOND, 0)
            }
            if (cal.timeInMillis <= now) cal.add(Calendar.DAY_OF_YEAR, 1)
            target = cal.timeInMillis
        }

        Scheduler.prefs(this).edit()
            .putBoolean("wifi", wifi)
            .putBoolean("data", data)
            .putBoolean("bt", bt)
            .putLong("target", target)
            .apply()
        Scheduler.schedule(this, target)
        refreshStatus()
        toast("تم تشغيل المؤقت")
    }

    private fun cancelTimer() {
        Scheduler.cancel(this)
        Scheduler.prefs(this).edit().putLong("target", 0L).commit()
        refreshStatus()
        toast("انلغى المؤقت")
    }

    /** Runs the switch-off right now, so you can see whether it works on your phone. */
    private fun testNow() {
        val wifi = cbWifi.isChecked
        val data = cbData.isChecked
        val bt = cbBt.isChecked
        if (!wifi && !data && !bt) {
            toast("اختر شي واحد على الأقل")
            return
        }
        if (!accessibilityEnabled()) {
            toast("فعّل خدمة إمكانية الوصول أول")
            return
        }
        tvLast.text = "جاري التجربة... لا تلمس الشاشة"
        val app = applicationContext
        Thread {
            val report = Runner.applyNow(app, wifi, data, bt)
            runOnUiThread {
                toast(report)
                refreshStatus()
            }
        }.start()
    }

    private fun refreshStatus() {
        val prefs = Scheduler.prefs(this)
        val target = prefs.getLong("target", 0L)
        val left = target - System.currentTimeMillis()
        tvStatus.text = if (target > 0L && left > 0L) {
            val s = left / 1000
            String.format(Locale.US, "يتبقى %02d:%02d:%02d", s / 3600, (s % 3600) / 60, s % 60)
        } else {
            "ما في مؤقت شغال"
        }
        val last = prefs.getString("last", "") ?: ""
        if (!tvLast.text.toString().startsWith("جاري")) tvLast.text = last
    }

    private fun toast(msg: String) = Toast.makeText(this, msg, Toast.LENGTH_LONG).show()
}
