package com.temporary.app

import android.app.admin.DeviceAdminReceiver

/**
 * Only used on Android older than 9, where the accessibility service cannot lock the screen.
 * It holds a single policy: force-lock.
 */
class AdminReceiver : DeviceAdminReceiver()
