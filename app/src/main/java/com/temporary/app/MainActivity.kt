package com.temporary.app

import android.app.Activity
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.View
import android.widget.Button
import android.widget.CheckBox
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.RadioButton
import android.widget.TextView
import android.widget.TimePicker
import android.widget.Toast
import rikka.shizuku.Shizuku
import java.util.Calendar
import java.util.Locale

class MainActivity : Activity() {

    private lateinit var tvShizuku: TextView
    private lateinit var rowShizukuButtons: LinearLayout
    private lateinit var btnPermission: Button
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

    private val permissionListener =
        Shizuku.OnRequestPermissionResultListener { _, _ -> runOnUiThread { refreshShizuku() } }
    private val binderListener =
        Shizuku.OnBinderReceivedListener { runOnUiThread { refreshShizuku() } }
    private val deadListener =
        Shizuku.OnBinderDeadListener { runOnUiThread { refreshShizuku() } }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        tvShizuku = findViewById(R.id.tvShizuku)
        rowShizukuButtons = findViewById(R.id.rowShizukuButtons)
        btnPermission = findViewById(R.id.btnPermission)
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

        btnPermission.setOnClickListener {
            try {
                Shizuku.requestPermission(100)
            } catch (t: Throwable) {
                toast("Shizuku مو شغال")
            }
        }
        findViewById<Button>(R.id.btnOpenShizuku).setOnClickListener {
            val intent = packageManager.getLaunchIntentForPackage("moe.shizuku.privileged.api")
            if (intent != null) startActivity(intent) else toast("Shizuku مو منزّل")
        }

        Shizuku.addRequestPermissionResultListener(permissionListener)
        Shizuku.addBinderReceivedListenerSticky(binderListener)
        Shizuku.addBinderDeadListener(deadListener)

        updateMode()
        refreshShizuku()
    }

    override fun onResume() {
        super.onResume()
        refreshShizuku()
        handler.post(ticker)
    }

    override fun onPause() {
        handler.removeCallbacks(ticker)
        super.onPause()
    }

    override fun onDestroy() {
        Shizuku.removeRequestPermissionResultListener(permissionListener)
        Shizuku.removeBinderReceivedListener(binderListener)
        Shizuku.removeBinderDeadListener(deadListener)
        super.onDestroy()
    }

    private fun updateMode() {
        val after = rbAfter.isChecked
        panelAfter.visibility = if (after) View.VISIBLE else View.GONE
        timePicker.visibility = if (after) View.GONE else View.VISIBLE
    }

    private fun refreshShizuku() {
        when (Switcher.state()) {
            Switcher.READY -> {
                tvShizuku.text = "Shizuku شغال ✓"
                rowShizukuButtons.visibility = View.GONE
            }
            Switcher.NEED_PERMISSION -> {
                tvShizuku.text = "Shizuku شغال، لكن التطبيق يحتاج صلاحية"
                rowShizukuButtons.visibility = View.VISIBLE
                btnPermission.visibility = View.VISIBLE
            }
            else -> {
                tvShizuku.text = "Shizuku مو شغال — شغّله أول (الواي فاي والبلوتث يشتغلون بدونه في أندرويد القديم، البيانات لا)"
                rowShizukuButtons.visibility = View.VISIBLE
                btnPermission.visibility = View.GONE
            }
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

        if (data && Switcher.state() != Switcher.READY) {
            toast("تنبيه: البيانات ما تنطفي بدون Shizuku")
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
        Scheduler.prefs(this).edit().putLong("target", 0L).apply()
        refreshStatus()
        toast("انلغى المؤقت")
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
        tvLast.text = prefs.getString("last", "") ?: ""
    }

    private fun toast(msg: String) = Toast.makeText(this, msg, Toast.LENGTH_SHORT).show()
}
