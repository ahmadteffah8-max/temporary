package com.temporary.app

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.SweepGradient
import android.util.AttributeSet
import android.view.View

/** A thin progress ring: a faint track plus a gradient arc that starts at the top and runs clockwise. */
class RingView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null
) : View(context, attrs) {

    private val stroke = 10f * resources.displayMetrics.density

    private val trackPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeWidth = stroke
        color = 0xFF1B1B22.toInt()
    }

    private val arcPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeWidth = stroke
        strokeCap = Paint.Cap.ROUND
    }

    private val rect = RectF()

    /** 0f = empty, 1f = full circle. */
    var progress: Float = 0f
        set(value) {
            field = value.coerceIn(0f, 1f)
            invalidate()
        }

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) {
        super.onSizeChanged(w, h, oldw, oldh)
        val half = stroke / 2f
        rect.set(half, half, w - half, h - half)
        arcPaint.shader = SweepGradient(
            w / 2f,
            h / 2f,
            intArrayOf(0xFF6EA8FF.toInt(), 0xFF9A7CFF.toInt(), 0xFF6EA8FF.toInt()),
            null
        )
    }

    override fun onDraw(canvas: Canvas) {
        canvas.drawOval(rect, trackPaint)
        if (progress > 0.002f) {
            canvas.save()
            // The sweep gradient and the arc both start at 3 o'clock; turn them to start at 12.
            canvas.rotate(-90f, width / 2f, height / 2f)
            canvas.drawArc(rect, 0f, 360f * progress, false, arcPaint)
            canvas.restore()
        }
    }
}
