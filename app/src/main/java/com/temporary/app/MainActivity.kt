package com.temporary.app

import android.animation.Animator
import android.animation.AnimatorListenerAdapter
import android.animation.ObjectAnimator
import android.animation.PropertyValuesHolder
import android.animation.TimeInterpolator
import android.animation.ValueAnimator
import android.app.Activity
import android.app.TimePickerDialog
import android.app.admin.DevicePolicyManager
import android.content.ComponentName
import android.content.Intent
import android.graphics.drawable.GradientDrawable
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import android.text.Editable
import android.text.TextWatcher
import android.transition.AutoTransition
import android.transition.Transition
import android.transition.TransitionManager
import android.view.View
import android.view.animation.AccelerateDecelerateInterpolator
import android.view.animation.AnimationUtils
import android.view.animation.DecelerateInterpolator
import android.view.animation.LinearInterpolator
import android.view.animation.OvershootInterpolator
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputMethodManager
import android.widget.EditText
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import java.util.Calendar
import java.util.Locale

class MainActivity : Activity() {

    // ---- views ----
    private lateinit var column: LinearLayout
    private lateinit var headerRow: View
    private lateinit var star: ImageView
    private lateinit var bannerAccess: View
    private lateinit var btnAccess: View
    private lateinit var ringCard: View
    private lateinit var dotStatus: View
    private lateinit var tvState: TextView
    private lateinit var ring: RingView
    private lateinit var tvBig: TextView
    private lateinit var tvSub: TextView
    private lateinit var tvTargets: TextView
    private lateinit var controls: View
    private lateinit var tiles: List<View>
    private lateinit var segContainer: View
    private lateinit var segIndicator: View
    private lateinit var segAfter: TextView
    private lateinit var segAt: TextView
    private lateinit var panelAfter: View
    private lateinit var panelAt: View
    private lateinit var chips: List<TextView>
    private lateinit var etMinutes: EditText
    private lateinit var btnPickTime: View
    private lateinit var tvPickTime: TextView
    private lateinit var btnMain: TextView
    private lateinit var bottomArea: View
    private lateinit var btnTest: TextView
    private lateinit var tvLast: TextView

    // ---- colors ----
    private var cAccent = 0
    private var cText = 0
    private var cText2 = 0
    private var cOnAccent = 0
    private var cDanger = 0
    private var cOk = 0
    private var cIdle = 0
    private var cAmber = 0

    // ---- state ----
    private val keys = arrayOf("wifi", "data", "bt", "screen")
    private val names = arrayOf("الواي فاي", "البيانات", "البلوتث", "الشاشة")
    private val labels = arrayOf("الواي فاي", "بيانات الجوال", "البلوتث", "إطفاء الشاشة")
    private val icons = intArrayOf(
        R.drawable.ic_wifi, R.drawable.ic_data, R.drawable.ic_bluetooth, R.drawable.ic_screen
    )
    private val tileIds = intArrayOf(R.id.tileWifi, R.id.tileData, R.id.tileBt, R.id.tileScreen)
    private val sel = booleanArrayOf(true, false, false, false)

    private var modeAfter = true
    private var pickHour = 0
    private var pickMinute = 0
    private var running = false
    private var testing = false
    private var lastShown: String? = null

    private var starAnim: ObjectAnimator? = null
    private var dotAnim: ObjectAnimator? = null
    private var ringAnim: ValueAnimator? = null

    private val handler = Handler(Looper.getMainLooper())
    private val ticker = object : Runnable {
        override fun run() {
            tick()
            handler.postDelayed(this, 1000)
        }
    }

    // =====================================================================================
    // lifecycle
    // =====================================================================================

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        cAccent = color(R.color.accent)
        cText = color(R.color.text)
        cText2 = color(R.color.text2)
        cOnAccent = color(R.color.on_accent)
        cDanger = color(R.color.danger)
        cOk = color(R.color.ok)
        cIdle = color(R.color.idle_dot)
        cAmber = color(R.color.amber)

        bindViews()

        val prefs = Scheduler.prefs(this)
        for (i in 0..3) sel[i] = prefs.getBoolean(keys[i], i == 0)
        modeAfter = prefs.getBoolean("mode_after", true)
        val defaultTime = Calendar.getInstance().apply { add(Calendar.MINUTE, 30) }
        pickHour = prefs.getInt("pick_h", defaultTime.get(Calendar.HOUR_OF_DAY))
        pickMinute = prefs.getInt("pick_m", defaultTime.get(Calendar.MINUTE))
        etMinutes.setText(prefs.getString("minutes", "30"))

        setupTiles()
        setupChips()
        setupSegments()
        setupActions()

        tvPickTime.text = String.format(Locale.US, "%02d:%02d", pickHour, pickMinute)
        panelAfter.visibility = if (modeAfter) View.VISIBLE else View.GONE
        panelAt.visibility = if (modeAfter) View.GONE else View.VISIBLE
        styleSegments(false)
        syncChips(false)

        running = prefs.getLong("target", 0L) > 0L
        styleMain(running)
        lockControls(false)
        refreshStateLabels()
        if (running) {
            ring.progress = fraction()
            updateRunningTexts()
        } else {
            updateIdlePreview()
        }
        showLast(prefs.getString("last", "") ?: "", false)

        segContainer.post { sizeIndicator() }
        if (savedInstanceState == null) prepareEntrance()
    }

    override fun onResume() {
        super.onResume()
        refreshAccess()
        startStar()
        if (running) startDotPulse()
        handler.post(ticker)
    }

    override fun onPause() {
        handler.removeCallbacks(ticker)
        stopStar()
        stopDotPulse()
        ringAnim?.cancel()
        super.onPause()
    }

    // =====================================================================================
    // setup
    // =====================================================================================

    private fun bindViews() {
        column = findViewById(R.id.column)
        headerRow = findViewById(R.id.headerRow)
        star = findViewById(R.id.imgStar)
        bannerAccess = findViewById(R.id.bannerAccess)
        btnAccess = findViewById(R.id.btnAccess)
        ringCard = findViewById(R.id.ringCard)
        dotStatus = findViewById(R.id.dotStatus)
        tvState = findViewById(R.id.tvState)
        ring = findViewById(R.id.ring)
        tvBig = findViewById(R.id.tvBig)
        tvSub = findViewById(R.id.tvSub)
        tvTargets = findViewById(R.id.tvTargets)
        controls = findViewById(R.id.controls)
        tiles = tileIds.map { findViewById<View>(it) }
        segContainer = findViewById(R.id.segContainer)
        segIndicator = findViewById(R.id.segIndicator)
        segAfter = findViewById(R.id.segAfter)
        segAt = findViewById(R.id.segAt)
        panelAfter = findViewById(R.id.panelAfter)
        panelAt = findViewById(R.id.panelAt)
        chips = listOf(
            findViewById(R.id.chip5), findViewById(R.id.chip15), findViewById(R.id.chip30),
            findViewById(R.id.chip60), findViewById(R.id.chip120)
        )
        etMinutes = findViewById(R.id.etMinutes)
        btnPickTime = findViewById(R.id.btnPickTime)
        tvPickTime = findViewById(R.id.tvPickTime)
        btnMain = findViewById(R.id.btnMain)
        bottomArea = findViewById(R.id.bottomArea)
        btnTest = findViewById(R.id.btnTest)
        tvLast = findViewById(R.id.tvLast)
    }

    private fun setupTiles() {
        for (i in 0..3) {
            val v = tiles[i]
            v.findViewById<ImageView>(R.id.tileIcon).setImageResource(icons[i])
            v.findViewById<TextView>(R.id.tileLabel).text = labels[i]
            styleTile(v, sel[i], false)
            v.setOnClickListener { onTileClick(i) }
        }
    }

    private fun setupChips() {
        for (chip in chips) {
            chip.setOnClickListener {
                if (locked()) {
                    shake(controls)
                    return@setOnClickListener
                }
                bounce(chip)
                val value = chip.tag as String
                etMinutes.setText(value)
                etMinutes.setSelection(value.length)
                etMinutes.clearFocus()
                hideKeyboard()
            }
        }
        etMinutes.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
            override fun afterTextChanged(s: Editable?) {
                syncChips(true)
                updateIdlePreview()
            }
        })
        etMinutes.setOnEditorActionListener { v, actionId, _ ->
            if (actionId == EditorInfo.IME_ACTION_DONE) {
                v.clearFocus()
                hideKeyboard()
                true
            } else {
                false
            }
        }
    }

    private fun setupSegments() {
        segAfter.setOnClickListener { setMode(true) }
        segAt.setOnClickListener { setMode(false) }
        btnPickTime.setOnClickListener {
            if (locked()) {
                shake(controls)
                return@setOnClickListener
            }
            TimePickerDialog(
                this,
                R.style.DialogTheme,
                { _, h, m ->
                    pickHour = h
                    pickMinute = m
                    tvPickTime.text = String.format(Locale.US, "%02d:%02d", h, m)
                    bounce(btnPickTime)
                    updateIdlePreview()
                },
                pickHour,
                pickMinute,
                true
            ).show()
        }
    }

    private fun setupActions() {
        btnAccess.setOnClickListener {
            try {
                startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS))
            } catch (t: Throwable) {
                toast("افتح الإعدادات ← إمكانية الوصول يدوياً")
            }
        }
        btnMain.setOnClickListener {
            bounce(btnMain)
            if (running) cancelTimer() else startTimer()
        }
        btnTest.setOnClickListener {
            bounce(btnTest)
            testNow()
        }
    }

    // =====================================================================================
    // actions
    // =====================================================================================

    private fun onTileClick(i: Int) {
        if (locked()) {
            shake(controls)
            return
        }
        sel[i] = !sel[i]
        styleTile(tiles[i], sel[i], true)
        bounce(tiles[i])
        Scheduler.prefs(this).edit().putBoolean(keys[i], sel[i]).apply()
        if (i == 3 && sel[3] && needsAdmin()) askAdmin()
        updateIdlePreview()
    }

    private fun setMode(after: Boolean) {
        if (locked()) {
            shake(controls)
            return
        }
        if (after == modeAfter) return
        modeAfter = after
        hideKeyboard()
        TransitionManager.beginDelayedTransition(column, soft())
        panelAfter.visibility = if (after) View.VISIBLE else View.GONE
        panelAt.visibility = if (after) View.GONE else View.VISIBLE
        styleSegments(true)
        updateIdlePreview()
    }

    private fun startTimer() {
        if (locked()) return
        if (sel.none { it }) {
            toast("اختر شي واحد على الأقل")
            shake(controls)
            return
        }
        if (!accessibilityEnabled()) {
            toast("فعّل خدمة إمكانية الوصول أول")
            shake(bannerAccess)
            return
        }
        if (sel[3] && needsAdmin()) {
            toast("فعّل «مدير الجهاز» عشان يقدر يقفل الشاشة")
            askAdmin()
            return
        }
        val target = computeTarget()
        if (target == null) {
            toast("اكتب عدد دقائق صحيح")
            shake(etMinutes)
            return
        }

        hideKeyboard()
        val now = System.currentTimeMillis()
        val e = Scheduler.prefs(this).edit()
        for (i in 0..3) e.putBoolean(keys[i], sel[i])
        e.putString("minutes", etMinutes.text.toString())
            .putInt("pick_h", pickHour)
            .putInt("pick_m", pickMinute)
            .putBoolean("mode_after", modeAfter)
            .putLong("start", now)
            .putLong("target", target)
            .commit()
        Scheduler.schedule(this, target)
        applyRunning(true, true)
        tick()
    }

    private fun cancelTimer() {
        Scheduler.cancel(this)
        Scheduler.prefs(this).edit().putLong("target", 0L).commit()
        applyRunning(false, true)
    }

    /** Runs the switch-off right now, so you can see whether it works on your phone. */
    private fun testNow() {
        if (locked()) return
        if (sel.none { it }) {
            toast("اختر شي واحد على الأقل")
            shake(controls)
            return
        }
        if (!accessibilityEnabled()) {
            toast("فعّل خدمة إمكانية الوصول أول")
            shake(bannerAccess)
            return
        }
        if (sel[3] && needsAdmin()) {
            toast("فعّل «مدير الجهاز» عشان يقدر يقفل الشاشة")
            askAdmin()
            return
        }
        val w = sel[0]
        val d = sel[1]
        val b = sel[2]
        val s = sel[3]
        testing = true
        lockControls(true)
        refreshStateLabels()
        val app = applicationContext
        Thread {
            val report = Runner.applyNow(app, w, d, b, s)
            runOnUiThread {
                testing = false
                lockControls(true)
                refreshStateLabels()
                showLast(Scheduler.prefs(this).getString("last", "") ?: "", true)
                toast(report)
            }
        }.start()
    }

    // =====================================================================================
    // state -> UI
    // =====================================================================================

    private fun locked() = running || testing

    private fun applyRunning(r: Boolean, animate: Boolean) {
        running = r
        if (animate) TransitionManager.beginDelayedTransition(column, soft())
        refreshStateLabels()
        swapMain(r, animate)
        lockControls(animate)
        if (r) {
            updateRunningTexts()
            startDotPulse()
            if (animate) {
                animateRing(0f, fraction(), 700L, DecelerateInterpolator(1.6f))
            } else {
                ring.progress = fraction()
            }
        } else {
            stopDotPulse()
            if (animate) {
                animateRing(ring.progress, 0f, 450L, DecelerateInterpolator(1.6f))
            } else {
                ring.progress = 0f
            }
            updateIdlePreview()
        }
    }

    private fun tick() {
        val prefs = Scheduler.prefs(this)
        val target = prefs.getLong("target", 0L)
        val nowRunning = target > 0L
        if (nowRunning != running) applyRunning(nowRunning, true)

        if (running) {
            updateRunningTexts()
            if (ringAnim?.isRunning != true) ring.progress = fraction()
        } else if (ringAnim?.isRunning != true) {
            ring.progress = 0f
        }
        refreshStateLabels()
        if (!testing) showLast(prefs.getString("last", "") ?: "", true)
    }

    private fun updateRunningTexts() {
        val prefs = Scheduler.prefs(this)
        val target = prefs.getLong("target", 0L)
        val left = (target - System.currentTimeMillis()).coerceAtLeast(0L)
        val s = left / 1000
        tvBig.text = String.format(Locale.US, "%02d:%02d:%02d", s / 3600, (s % 3600) / 60, s % 60)
        tvSub.text = "المتبقي"
        val cal = Calendar.getInstance().apply { timeInMillis = target }
        val at = String.format(
            Locale.US, "%02d:%02d", cal.get(Calendar.HOUR_OF_DAY), cal.get(Calendar.MINUTE)
        )
        val picked = pickedNames(prefs)
        tvTargets.text = "ينطفي الساعة $at\n$picked"
    }

    private fun pickedNames(prefs: android.content.SharedPreferences): String {
        val list = ArrayList<String>()
        for (i in 0..3) if (prefs.getBoolean(keys[i], false)) list.add(names[i])
        return if (list.isEmpty()) "—" else list.joinToString(" · ")
    }

    private fun updateIdlePreview() {
        if (running) return
        if (modeAfter) {
            val m = etMinutes.text.toString().toLongOrNull() ?: 0L
            val total = m * 60L
            tvBig.text = String.format(
                Locale.US, "%02d:%02d:%02d", total / 3600, (total % 3600) / 60, total % 60
            )
            tvSub.text = "المدة"
        } else {
            tvBig.text = String.format(Locale.US, "%02d:%02d", pickHour, pickMinute)
            tvSub.text = if (isTomorrow()) "الوقت المحدد · غداً" else "الوقت المحدد · اليوم"
        }
        val list = ArrayList<String>()
        for (i in 0..3) if (sel[i]) list.add(names[i])
        tvTargets.text = if (list.isEmpty()) "اختر شي واحد على الأقل" else list.joinToString(" · ")
    }

    private fun refreshStateLabels() {
        val text: String
        val dot: Int
        when {
            testing -> {
                text = "جاري التجربة… لا تلمس الشاشة"
                dot = cAmber
            }
            running -> {
                val left = Scheduler.prefs(this).getLong("target", 0L) - System.currentTimeMillis()
                text = if (left <= 0L) "جاري التنفيذ…" else "المؤقت شغال"
                dot = cOk
            }
            else -> {
                text = "جاهز"
                dot = cIdle
            }
        }
        if (tvState.text.toString() != text) tvState.text = text
        (dotStatus.background.mutate() as GradientDrawable).setColor(dot)
    }

    private fun showLast(text: String, animate: Boolean) {
        if (text == lastShown) return
        lastShown = text
        if (animate) TransitionManager.beginDelayedTransition(column, soft())
        if (text.isBlank()) {
            tvLast.visibility = View.GONE
        } else {
            tvLast.text = text
            tvLast.visibility = View.VISIBLE
        }
    }

    private fun lockControls(animate: Boolean) {
        val a = if (locked()) 0.4f else 1f
        etMinutes.isEnabled = !locked()
        btnTest.animate().cancel()
        if (animate) {
            controls.animate().alpha(a).setDuration(300L).start()
            btnTest.animate().alpha(if (locked()) 0.35f else 1f).setDuration(300L).start()
        } else {
            controls.alpha = a
            btnTest.alpha = if (locked()) 0.35f else 1f
        }
    }

    private fun fraction(): Float {
        val prefs = Scheduler.prefs(this)
        val target = prefs.getLong("target", 0L)
        val start = prefs.getLong("start", 0L)
        val total = (target - start).coerceAtLeast(1L)
        val left = (target - System.currentTimeMillis()).coerceAtLeast(0L)
        return (left.toFloat() / total.toFloat()).coerceIn(0f, 1f)
    }

    private fun refreshAccess() {
        val enabled = accessibilityEnabled()
        val wantVisible = !enabled
        val isVisible = bannerAccess.visibility == View.VISIBLE
        if (wantVisible != isVisible) {
            TransitionManager.beginDelayedTransition(column, soft())
            bannerAccess.visibility = if (wantVisible) View.VISIBLE else View.GONE
        }
    }

    // =====================================================================================
    // small styling helpers
    // =====================================================================================

    private fun styleTile(v: View, selected: Boolean, animate: Boolean) {
        val icon = v.findViewById<ImageView>(R.id.tileIcon)
        val label = v.findViewById<TextView>(R.id.tileLabel)
        val iconTo = if (selected) cAccent else cText2
        val labelTo = if (selected) cText else cText2
        v.isSelected = selected
        if (!animate) {
            icon.setColorFilter(iconTo)
            label.setTextColor(labelTo)
            return
        }
        val iconFrom = if (selected) cText2 else cAccent
        val labelFrom = if (selected) cText2 else cText
        val a = ValueAnimator.ofFloat(0f, 1f)
        a.duration = 220L
        val ev = android.animation.ArgbEvaluator()
        a.addUpdateListener {
            val f = it.animatedFraction
            icon.setColorFilter(ev.evaluate(f, iconFrom, iconTo) as Int)
            label.setTextColor(ev.evaluate(f, labelFrom, labelTo) as Int)
        }
        a.start()
    }

    private fun syncChips(animate: Boolean) {
        val current = etMinutes.text.toString()
        for (chip in chips) {
            val on = (chip.tag as String) == current
            if (animate && chip.isSelected == on) continue
            chip.isSelected = on
            val to = if (on) cOnAccent else cText2
            if (!animate) {
                chip.setTextColor(to)
            } else {
                val from = if (on) cText2 else cOnAccent
                val a = ValueAnimator.ofArgb(from, to)
                a.duration = 200L
                a.addUpdateListener { chip.setTextColor(it.animatedValue as Int) }
                a.start()
            }
        }
    }

    private fun styleSegments(animate: Boolean) {
        val onColor = cText
        val offColor = cText2
        val afterTo = if (modeAfter) onColor else offColor
        val atTo = if (modeAfter) offColor else onColor
        if (!animate) {
            segAfter.setTextColor(afterTo)
            segAt.setTextColor(atTo)
        } else {
            val afterFrom = if (modeAfter) offColor else onColor
            val atFrom = if (modeAfter) onColor else offColor
            val a = ValueAnimator.ofFloat(0f, 1f)
            a.duration = 240L
            val ev = android.animation.ArgbEvaluator()
            a.addUpdateListener {
                val f = it.animatedFraction
                segAfter.setTextColor(ev.evaluate(f, afterFrom, afterTo) as Int)
                segAt.setTextColor(ev.evaluate(f, atFrom, atTo) as Int)
            }
            a.start()
        }
        moveIndicator(animate)
    }

    /** Gives the indicator the width of one segment, then puts it on the selected one. */
    private fun sizeIndicator() {
        val w = segAfter.width - dp(8)
        if (w <= 0) return
        val lp = segIndicator.layoutParams
        lp.width = w
        segIndicator.layoutParams = lp
        segIndicator.post { moveIndicator(false) }
    }

    private fun moveIndicator(animate: Boolean) {
        val target = if (modeAfter) segAfter else segAt
        val x = (target.left + dp(4)).toFloat()
        if (animate) {
            segIndicator.animate().x(x).setDuration(280L).setInterpolator(fastOut()).start()
        } else {
            segIndicator.animate().cancel()
            segIndicator.x = x
        }
    }

    private fun styleMain(run: Boolean) {
        if (run) {
            btnMain.setBackgroundResource(R.drawable.bg_danger)
            btnMain.text = "إلغاء المؤقت"
            btnMain.setTextColor(cDanger)
        } else {
            btnMain.setBackgroundResource(R.drawable.bg_primary)
            btnMain.text = "شغّل المؤقت"
            btnMain.setTextColor(cOnAccent)
        }
    }

    private fun swapMain(run: Boolean, animate: Boolean) {
        if (!animate) {
            styleMain(run)
            return
        }
        btnMain.animate().cancel()
        btnMain.animate().alpha(0f).scaleX(0.96f).scaleY(0.96f).setDuration(110L)
            .withEndAction {
                styleMain(run)
                btnMain.animate().alpha(1f).scaleX(1f).scaleY(1f).setDuration(200L).start()
            }.start()
    }

    // =====================================================================================
    // animations
    // =====================================================================================

    private fun soft(): Transition {
        val t = AutoTransition()
        t.setDuration(280L)
        t.setInterpolator(fastOut())
        return t
    }

    private fun fastOut(): TimeInterpolator =
        AnimationUtils.loadInterpolator(this, android.R.interpolator.fast_out_slow_in)

    private fun animateRing(from: Float, to: Float, ms: Long, interp: TimeInterpolator) {
        ringAnim?.cancel()
        val a = ValueAnimator.ofFloat(from, to)
        a.duration = ms
        a.interpolator = interp
        a.addUpdateListener { ring.progress = it.animatedValue as Float }
        a.addListener(object : AnimatorListenerAdapter() {
            override fun onAnimationEnd(animation: Animator) {
                // make sure the ring ends on the exact value even if animations are disabled
                if (!running) ring.progress = 0f
            }
        })
        ringAnim = a
        a.start()
    }

    private fun startStar() {
        if (starAnim?.isRunning == true) return
        val a = ObjectAnimator.ofPropertyValuesHolder(
            star,
            PropertyValuesHolder.ofFloat(View.TRANSLATION_Y, 0f, -dpf(6)),
            PropertyValuesHolder.ofFloat(View.ROTATION, -3f, 3f)
        )
        a.duration = 2600L
        a.repeatCount = ValueAnimator.INFINITE
        a.repeatMode = ValueAnimator.REVERSE
        a.interpolator = AccelerateDecelerateInterpolator()
        starAnim = a
        a.start()
    }

    private fun stopStar() {
        starAnim?.cancel()
        star.translationY = 0f
        star.rotation = 0f
    }

    private fun startDotPulse() {
        if (dotAnim?.isRunning == true) return
        val a = ObjectAnimator.ofFloat(dotStatus, View.ALPHA, 1f, 0.25f)
        a.duration = 900L
        a.repeatCount = ValueAnimator.INFINITE
        a.repeatMode = ValueAnimator.REVERSE
        a.interpolator = AccelerateDecelerateInterpolator()
        dotAnim = a
        a.start()
    }

    private fun stopDotPulse() {
        dotAnim?.cancel()
        dotStatus.alpha = 1f
    }

    /** Hides the sections, then slides them in one after the other when the screen opens. */
    private fun prepareEntrance() {
        val parts = listOf(headerRow, bannerAccess, ringCard, controls, btnMain, bottomArea)
        val offset = dpf(22)
        for (v in parts) {
            v.alpha = 0f
            v.translationY = offset
        }
        column.post {
            parts.forEachIndexed { index, v ->
                val endAlpha = if (v === controls && locked()) 0.4f else 1f
                v.animate()
                    .alpha(endAlpha)
                    .translationY(0f)
                    .setStartDelay(60L * index)
                    .setDuration(420L)
                    .setInterpolator(DecelerateInterpolator(1.8f))
                    .start()
            }
        }
    }

    private fun bounce(v: View) {
        v.animate().cancel()
        v.animate().scaleX(0.95f).scaleY(0.95f).setDuration(90L).withEndAction {
            v.animate().scaleX(1f).scaleY(1f).setDuration(220L)
                .setInterpolator(OvershootInterpolator(2.2f)).start()
        }.start()
    }

    private fun shake(v: View) {
        ObjectAnimator.ofFloat(
            v, View.TRANSLATION_X, 0f, -dpf(10), dpf(10), -dpf(6), dpf(6), 0f
        ).setDuration(380L).start()
    }

    // =====================================================================================
    // misc
    // =====================================================================================

    private fun computeTarget(): Long? {
        val now = System.currentTimeMillis()
        return if (modeAfter) {
            val minutes = etMinutes.text.toString().toLongOrNull() ?: return null
            if (minutes <= 0L) null else now + minutes * 60_000L
        } else {
            val cal = Calendar.getInstance().apply {
                set(Calendar.HOUR_OF_DAY, pickHour)
                set(Calendar.MINUTE, pickMinute)
                set(Calendar.SECOND, 0)
                set(Calendar.MILLISECOND, 0)
            }
            if (cal.timeInMillis <= now) cal.add(Calendar.DAY_OF_YEAR, 1)
            cal.timeInMillis
        }
    }

    private fun isTomorrow(): Boolean {
        val cal = Calendar.getInstance().apply {
            set(Calendar.HOUR_OF_DAY, pickHour)
            set(Calendar.MINUTE, pickMinute)
            set(Calendar.SECOND, 0)
            set(Calendar.MILLISECOND, 0)
        }
        return cal.timeInMillis <= System.currentTimeMillis()
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

    /** On Android older than 9 the screen can only be locked through device admin. */
    private fun needsAdmin(): Boolean = Build.VERSION.SDK_INT < 28 && !Direct.adminActive(this)

    private fun askAdmin() {
        try {
            val i = Intent(DevicePolicyManager.ACTION_ADD_DEVICE_ADMIN)
                .putExtra(
                    DevicePolicyManager.EXTRA_DEVICE_ADMIN,
                    ComponentName(this, AdminReceiver::class.java)
                )
                .putExtra(
                    DevicePolicyManager.EXTRA_ADD_EXPLANATION,
                    "يستعمله التطبيق فقط لقفل الشاشة عند انتهاء المؤقت."
                )
            startActivity(i)
        } catch (t: Throwable) {
            toast("ما قدرت أفتح شاشة مدير الجهاز")
        }
    }

    private fun hideKeyboard() {
        try {
            val imm = getSystemService(INPUT_METHOD_SERVICE) as InputMethodManager
            imm.hideSoftInputFromWindow(etMinutes.windowToken, 0)
        } catch (t: Throwable) {
            // ignore
        }
    }

    private fun color(id: Int): Int = resources.getColor(id, theme)

    private fun dp(v: Int): Int = (v * resources.displayMetrics.density).toInt()

    private fun dpf(v: Int): Float = v * resources.displayMetrics.density

    private fun toast(msg: String) = Toast.makeText(this, msg, Toast.LENGTH_LONG).show()
}
