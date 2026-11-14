package com.temporary.app

import android.accessibilityservice.AccessibilityService
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.PowerManager
import android.provider.Settings
import android.view.accessibility.AccessibilityEvent
import android.view.accessibility.AccessibilityNodeInfo

/**
 * Accessibility service that turns Wi-Fi / mobile data / Bluetooth off by pressing the real
 * buttons for you: first in the quick-settings panel, then in the Settings screens.
 * After every attempt it checks the real state of the radio, so it never turns something ON by mistake.
 */
class ToggleService : AccessibilityService() {

    override fun onServiceConnected() {
        super.onServiceConnected()
        instance = this
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) {}

    override fun onInterrupt() {}

    override fun onUnbind(intent: Intent?): Boolean {
        if (instance === this) instance = null
        return super.onUnbind(intent)
    }

    override fun onDestroy() {
        if (instance === this) instance = null
        super.onDestroy()
    }

    private enum class Target(val label: String, val keys: List<String>) {
        WIFI("الواي فاي", listOf("wifi", "wlan", "وايفاي", "وايفاى")),
        DATA(
            "البيانات",
            listOf(
                "mobiledata", "cellulardata", "mobilenetwork",
                "بياناتالجوال", "بياناتالهاتف", "بياناتالمحمول", "بياناتالخلوي", "بياناتالجوّال"
            )
        ),
        BT("البلوتث", listOf("bluetooth", "بلوتوث", "بلوتث", "بلوتوس"))
    }

    /** Runs the whole job and returns a short Arabic report. Blocking, call it from a worker thread. */
    @Synchronized
    @Suppress("DEPRECATION")
    fun turnOff(wifi: Boolean, data: Boolean, bt: Boolean, screen: Boolean): String {
        val ctx = applicationContext
        val pm = getSystemService(Context.POWER_SERVICE) as PowerManager
        val screenWasOn = pm.isInteractive

        val wanted = listOf(Target.WIFI to wifi, Target.DATA to data, Target.BT to bt)
            .filter { it.second }
            .map { it.first }
        val errors = HashMap<Target, String>()
        val ok = mutableListOf<String>()
        val failed = mutableListOf<String>()
        var wakeLock: PowerManager.WakeLock? = null
        var woke = false

        try {
            // Quick direct attempt (works on old Android and needs no screen).
            var tried = false
            if (Target.WIFI in wanted && State.wifiOn(ctx)) {
                Direct.wifiOff(ctx)
                tried = true
            }
            if (Target.BT in wanted && State.btOn(ctx)) {
                Direct.bluetoothOff()
                tried = true
            }
            if (tried) pause(1200)

            // Only wake the screen when something still has to be pressed by hand.
            val needUi = wanted.filter { isOn(it) }
            if (needUi.isNotEmpty()) {
                try {
                    wakeLock = pm.newWakeLock(
                        PowerManager.SCREEN_BRIGHT_WAKE_LOCK or PowerManager.ACQUIRE_CAUSES_WAKEUP,
                        "temporary:toggle"
                    )
                    wakeLock.acquire(120_000L)
                } catch (e: Throwable) {
                    // continue without it
                }
                if (!screenWasOn) {
                    woke = true
                    pause(1500)
                }
                for (t in needUi) {
                    errors[t] = viaUi(t)
                }
            }

            for (t in wanted) {
                if (!isOn(t)) {
                    ok.add(t.label)
                } else {
                    failed.add("${t.label} (${errors[t].orEmpty().ifBlank { "ما اشتغل" }})")
                }
            }
        } catch (e: Throwable) {
            failed.add("خطأ: ${e.javaClass.simpleName}")
        } finally {
            try {
                closeUi()
                if (wakeLock != null && wakeLock.isHeld) wakeLock.release()
            } catch (e: Throwable) {
                // ignore
            }
        }

        // The screen goes last, after everything else is done (and after the wake lock is released).
        if (screen) {
            if (!screenWasOn && !woke) {
                ok.add("الشاشة (كانت مطفية)")
            } else {
                val err = lockScreen()
                if (err.isEmpty()) ok.add("الشاشة") else failed.add("الشاشة ($err)")
            }
        } else if (woke) {
            // We turned the screen on only to do the work, so put it back to sleep.
            lockScreen()
        }

        val sb = StringBuilder()
        if (ok.isNotEmpty()) sb.append("انطفى: ").append(ok.joinToString("، "))
        if (failed.isNotEmpty()) {
            if (sb.isNotEmpty()) sb.append(" | ")
            sb.append("ما اشتغل: ").append(failed.joinToString(" ؛ "))
        }
        return sb.toString()
    }

    /** Locks the screen (turns it off). Returns an empty string on success, or the reason it failed. */
    private fun lockScreen(): String {
        return if (Build.VERSION.SDK_INT >= 28) {
            if (performGlobalAction(GLOBAL_ACTION_LOCK_SCREEN)) "" else "النظام رفض أمر القفل"
        } else if (Direct.lockWithAdmin(applicationContext)) {
            ""
        } else {
            "فعّل «مدير الجهاز» للتطبيق (أندرويد أقدم من 9)"
        }
    }

    private fun isOn(t: Target): Boolean = when (t) {
        Target.WIFI -> State.wifiOn(applicationContext)
        Target.DATA -> State.dataOn(applicationContext)
        Target.BT -> State.btOn(applicationContext)
    }

    /** Tries the quick-settings panel, then the Settings screens. Returns the reason if it failed. */
    private fun viaUi(t: Target): String {
        var err = ""

        // 1) Quick settings panel
        try {
            performGlobalAction(GLOBAL_ACTION_QUICK_SETTINGS)
            pause(1200)
            val tile = waitFor(3500L) { findTile(t) }
            if (tile != null) {
                tile.performAction(AccessibilityNodeInfo.ACTION_CLICK)
                if (t == Target.DATA) confirmDialog()
                pause(1500)
                if (!isOn(t)) {
                    closeUi()
                    return ""
                }
                err = "ضغطت الزر في الإعدادات السريعة وما انطفى"
            } else {
                err = "ما لقيت الزر في الإعدادات السريعة. شفت: ${labels()}"
            }
        } catch (e: Throwable) {
            err = e.javaClass.simpleName
        }
        closeUi()

        // 2) Settings screens
        for (action in actionsFor(t)) {
            try {
                startActivity(Intent(action).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
                pause(1800)
                val sw = waitFor(3000L) { findSwitch(t) }
                if (sw != null) {
                    clickNode(sw)
                    if (t == Target.DATA) confirmDialog()
                    pause(1500)
                    if (!isOn(t)) {
                        closeUi()
                        return ""
                    }
                    err = "ضغطت المفتاح في الإعدادات وما انطفى"
                } else {
                    err = "ما لقيت المفتاح في الإعدادات. شفت: ${labels()}"
                }
            } catch (e: Throwable) {
                err = e.javaClass.simpleName
            }
            closeUi()
        }
        return err
    }

    private fun actionsFor(t: Target): List<String> {
        val list = ArrayList<String>()
        when (t) {
            Target.WIFI -> {
                list.add(Settings.ACTION_WIFI_SETTINGS)
                if (Build.VERSION.SDK_INT >= 29) list.add(Settings.Panel.ACTION_INTERNET_CONNECTIVITY)
            }
            Target.DATA -> {
                list.add(Settings.ACTION_DATA_ROAMING_SETTINGS)
                list.add(Settings.ACTION_NETWORK_OPERATOR_SETTINGS)
                if (Build.VERSION.SDK_INT >= 29) list.add(Settings.Panel.ACTION_INTERNET_CONNECTIVITY)
            }
            Target.BT -> list.add(Settings.ACTION_BLUETOOTH_SETTINGS)
        }
        return list
    }

    // ---------- finding things on screen ----------

    private fun norm(s: CharSequence?): String {
        if (s == null) return ""
        val sb = StringBuilder()
        for (c in s) {
            if (Character.isLetterOrDigit(c)) sb.append(c.lowercaseChar())
        }
        return sb.toString()
    }

    private fun roots(): List<AccessibilityNodeInfo> {
        val list = ArrayList<AccessibilityNodeInfo>()
        try {
            for (w in windows) {
                val r = w.root
                if (r != null) list.add(r)
            }
        } catch (e: Throwable) {
            // some devices do not expose the window list
        }
        val active = rootInActiveWindow
        if (active != null) list.add(active)
        return list
    }

    private fun allNodes(root: AccessibilityNodeInfo, limit: Int = 800): List<AccessibilityNodeInfo> {
        val out = ArrayList<AccessibilityNodeInfo>()
        val queue = ArrayDeque<AccessibilityNodeInfo>()
        queue.add(root)
        while (queue.isNotEmpty() && out.size < limit) {
            val n = queue.removeFirst()
            out.add(n)
            for (i in 0 until n.childCount) {
                val child = n.getChild(i)
                if (child != null) queue.add(child)
            }
        }
        return out
    }

    private fun clickableAncestor(n: AccessibilityNodeInfo): AccessibilityNodeInfo? {
        var c: AccessibilityNodeInfo? = n
        var depth = 0
        while (c != null && !c.isClickable && depth < 5) {
            c = c.parent
            depth++
        }
        return if (c != null && c.isClickable) c else null
    }

    private fun findTile(t: Target): AccessibilityNodeInfo? {
        val keys = t.keys.map { norm(it) }
        for (root in roots()) {
            for (n in allNodes(root)) {
                val label = norm(n.text) + "|" + norm(n.contentDescription)
                if (keys.none { label.contains(it) }) continue
                val c = clickableAncestor(n)
                if (c != null) return c
            }
        }
        return null
    }

    private fun rowText(n: AccessibilityNodeInfo): String {
        val scope = n.parent?.parent ?: n.parent ?: return ""
        val sb = StringBuilder()
        for (x in allNodes(scope, 60)) {
            sb.append(norm(x.text)).append(norm(x.contentDescription))
        }
        return sb.toString()
    }

    private fun findSwitch(t: Target): AccessibilityNodeInfo? {
        val keys = t.keys.map { norm(it) }
        var fallback: AccessibilityNodeInfo? = null
        for (root in roots()) {
            for (n in allNodes(root)) {
                val cls = n.className?.toString() ?: ""
                val isSwitch = n.isCheckable || cls.contains("Switch") || cls.contains("ToggleButton")
                if (!isSwitch || !n.isChecked) continue
                if (fallback == null) fallback = n
                val row = rowText(n)
                if (keys.any { row.contains(it) }) return n
            }
        }
        // For Wi-Fi and Bluetooth pages the main switch is the first one; for data we need an exact match.
        return if (t == Target.DATA) null else fallback
    }

    private fun clickNode(n: AccessibilityNodeInfo) {
        val target = clickableAncestor(n) ?: n
        target.performAction(AccessibilityNodeInfo.ACTION_CLICK)
    }

    /** Presses the positive button of a confirmation dialog ("Turn off mobile data?"), if one shows up. */
    private fun confirmDialog() {
        repeat(6) {
            for (root in roots()) {
                val buttons = root.findAccessibilityNodeInfosByViewId("android:id/button1")
                for (b in buttons) {
                    if (b.isClickable) {
                        b.performAction(AccessibilityNodeInfo.ACTION_CLICK)
                        return
                    }
                }
            }
            pause(300)
        }
    }

    /** Backs out of the quick-settings panel / Settings screen we opened. */
    private fun closeUi() {
        repeat(3) {
            val pkg = rootInActiveWindow?.packageName?.toString() ?: ""
            if (pkg == "com.android.systemui" ||
                pkg.contains("settings", ignoreCase = true) ||
                pkg.contains("bluetooth", ignoreCase = true)
            ) {
                performGlobalAction(GLOBAL_ACTION_BACK)
                pause(500)
            } else {
                return
            }
        }
    }

    /** A few visible button names, used only to explain failures in the report. */
    private fun labels(): String {
        val seen = LinkedHashSet<String>()
        for (root in roots()) {
            for (n in allNodes(root, 300)) {
                if (!n.isClickable) continue
                val id = n.viewIdResourceName ?: ""
                if (id.contains("notification", true) || id.contains("row", true)) continue
                val text = (n.text ?: n.contentDescription)?.toString()?.trim() ?: ""
                if (text.isEmpty()) continue
                seen.add(text.take(14))
                if (seen.size >= 10) return seen.joinToString("، ")
            }
        }
        return if (seen.isEmpty()) "لا شي" else seen.joinToString("، ")
    }

    private fun pause(ms: Long) {
        try {
            Thread.sleep(ms)
        } catch (e: InterruptedException) {
            // ignore
        }
    }

    private fun <T> waitFor(timeoutMs: Long, block: () -> T?): T? {
        val end = System.currentTimeMillis() + timeoutMs
        while (true) {
            val v = try {
                block()
            } catch (e: Throwable) {
                null
            }
            if (v != null) return v
            if (System.currentTimeMillis() >= end) return null
            pause(300)
        }
    }

    companion object {
        @Volatile
        var instance: ToggleService? = null
    }
}
