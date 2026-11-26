using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;

namespace EchoKey.Core;

/// <summary>Runtime language switching (English LTR / Arabic RTL). XAML binds with {Binding [key], Source={x:Static core:Loc.I}}.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc I { get; } = new();
    Loc() { }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;

    string _lang = "en";

    public string Language
    {
        get => _lang;
        set
        {
            if (value != "ar") value = "en";
            if (_lang == value) return;
            _lang = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FlowDirection)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRtl)));
            Changed?.Invoke();
        }
    }

    public bool IsRtl => _lang == "ar";
    public FlowDirection FlowDirection => IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        var d = IsRtl ? Ar : En;
        if (d.TryGetValue(key, out var s)) return s;
        return En.TryGetValue(key, out var e) ? e : key;
    }

    public string Format(string key, params object?[] args) => string.Format(Get(key), args);

    /// <summary>Keys present in one language but missing in the other (used by the unit tests).</summary>
    public static List<string> MissingTranslations()
    {
        var missing = new List<string>();
        foreach (var k in En.Keys) if (!Ar.ContainsKey(k)) missing.Add("ar:" + k);
        foreach (var k in Ar.Keys) if (!En.ContainsKey(k)) missing.Add("en:" + k);
        return missing;
    }

    static readonly Dictionary<string, string> En = new()
    {
        ["soundboard"] = "Soundboard",
        ["hotkeys"] = "Hotkeys",
        ["stopAll"] = "Stop all",
        ["settings"] = "Settings",
        ["addSounds"] = "Add sounds",
        ["dropHint"] = "Drag & drop audio files here\nMP3 · WAV · OGG · OGA · FLAC",
        ["all"] = "All sounds",
        ["uncategorized"] = "Uncategorized",
        ["categories"] = "Categories",
        ["newCategory"] = "New category",
        ["categoryName"] = "Category name",
        ["soundName"] = "Sound name",
        ["rename"] = "Rename",
        ["delete"] = "Delete",
        ["preview"] = "Preview on my headphones",
        ["play"] = "Play",
        ["setHotkey"] = "Set hotkey",
        ["changeHotkey"] = "Change hotkey",
        ["removeHotkey"] = "Remove hotkey",
        ["moveEarlier"] = "Move earlier",
        ["moveLater"] = "Move later",
        ["moveTo"] = "Move to: {0}",
        ["micVolume"] = "Microphone",
        ["sbVolume"] = "Soundboard",
        ["micTip"] = "Mute / unmute the real microphone (the Soundboard keeps working)",
        ["volume"] = "Volume",
        ["playingNow"] = "Playing: {0}",
        ["cableOk"] = "Virtual mic ready: {0}",
        ["cableMissing"] = "VB-CABLE not found – click for help",
        ["cableHelpTitle"] = "Install VB-CABLE",
        ["cableHelp"] =
            "EchoKey sends sounds to a virtual microphone using VB-CABLE (free).\n\n" +
            "1. Download VB-CABLE from https://vb-audio.com/Cable/\n" +
            "2. Extract the ZIP, right-click VBCABLE_Setup_x64.exe → Run as administrator → Install Driver.\n" +
            "3. Restart Windows.\n" +
            "4. In your game / app (Discord, OBS, …) choose \"CABLE Output (VB-Audio Virtual Cable)\" as the microphone.\n" +
            "5. In EchoKey › Settings choose your real microphone. The output (CABLE Input) is selected automatically.\n\n" +
            "EchoKey never changes your Windows sound settings.",
        ["language"] = "Language",
        ["realMic"] = "Real microphone",
        ["outputDevice"] = "Output device (virtual cable)",
        ["defaultDevice"] = "(System default)",
        ["noMic"] = "(No microphone)",
        ["autoOutput"] = "(Auto: VB-CABLE)",
        ["startWithWindows"] = "Start with Windows",
        ["closeToTray"] = "Keep running in the tray when the window is closed",
        ["latency"] = "Output buffer: {0} ms",
        ["latencyHint"] = "Lower = less delay. Increase it if you hear crackling.",
        ["refresh"] = "Refresh devices",
        ["save"] = "Save",
        ["cancel"] = "Cancel",
        ["pressKey"] = "Press the shortcut you want…",
        ["hotkeyFor"] = "Hotkey for “{0}”",
        ["conflict"] = "This shortcut is already used by “{0}”.",
        ["registerFail"] = "Could not register {0}. Another program may already use it.",
        ["importFailed"] = "Could not import “{0}”: {1}",
        ["tooLong"] = "longer than 5 minutes",
        ["playFailed"] = "Could not play “{0}”: {1}",
        ["engineError"] = "Audio error: {0}",
        ["noOutput"] = "No output device. Install VB-CABLE or choose an output device in Settings.",
        ["micIsCable"] = "The selected microphone is the virtual cable. Choose your real microphone in Settings.",
        ["micFailed"] = "Microphone unavailable: {0}",
        ["trayOpen"] = "Open EchoKey",
        ["trayExit"] = "Exit",
        ["stopAllHotkey"] = "Stop-all hotkey",
        ["monitorEnabled"] = "Hear the sounds on my speakers / headphones (your microphone is never sent there)",
        ["monitorDevice"] = "My speakers / headphones",
        ["micNeedsCable"] = "The microphone is not sent because the output is not a virtual cable (it would echo in your speakers). Choose VB-CABLE as the output.",
        ["monitorIsCable"] = "The speakers device cannot be the virtual cable.",
        ["monitorError"] = "Speakers error: {0}",
        ["confirmDelete"] = "Delete “{0}” from the library?",
        ["confirmDeleteCategory"] = "Delete the category “{0}”? Its sounds are kept (moved to Uncategorized).",
    };

    static readonly Dictionary<string, string> Ar = new()
    {
        ["soundboard"] = "لوحة الأصوات",
        ["hotkeys"] = "الاختصارات",
        ["stopAll"] = "إيقاف الكل",
        ["settings"] = "الإعدادات",
        ["addSounds"] = "إضافة أصوات",
        ["dropHint"] = "اسحب وأفلت الملفات الصوتية هنا\nMP3 · WAV · OGG · OGA · FLAC",
        ["all"] = "كل الأصوات",
        ["uncategorized"] = "بدون تصنيف",
        ["categories"] = "التصنيفات",
        ["newCategory"] = "تصنيف جديد",
        ["categoryName"] = "اسم التصنيف",
        ["soundName"] = "اسم الصوت",
        ["rename"] = "إعادة تسمية",
        ["delete"] = "حذف",
        ["preview"] = "معاينة على سماعتي",
        ["play"] = "تشغيل",
        ["setHotkey"] = "تعيين اختصار",
        ["changeHotkey"] = "تغيير الاختصار",
        ["removeHotkey"] = "إزالة الاختصار",
        ["moveEarlier"] = "تقديم",
        ["moveLater"] = "تأخير",
        ["moveTo"] = "نقل إلى: {0}",
        ["micVolume"] = "الميكروفون",
        ["sbVolume"] = "الأصوات",
        ["micTip"] = "كتم / تشغيل الميكروفون الحقيقي (لوحة الأصوات تبقى تعمل)",
        ["volume"] = "مستوى الصوت",
        ["playingNow"] = "قيد التشغيل: {0}",
        ["cableOk"] = "الميكروفون الافتراضي جاهز: {0}",
        ["cableMissing"] = "VB-CABLE غير مثبت – اضغط للمساعدة",
        ["cableHelpTitle"] = "تثبيت VB-CABLE",
        ["cableHelp"] =
            "يرسل EchoKey الأصوات إلى ميكروفون افتراضي عبر VB-CABLE (مجاني).\n\n" +
            "1. حمّل VB-CABLE من https://vb-audio.com/Cable/\n" +
            "2. فك ضغط الملف، ثم انقر بزر الماوس الأيمن على VBCABLE_Setup_x64.exe ← تشغيل كمسؤول ← Install Driver.\n" +
            "3. أعد تشغيل Windows.\n" +
            "4. في اللعبة / البرنامج (Discord وOBS وغيرها) اختر \"CABLE Output (VB-Audio Virtual Cable)\" كميكروفون.\n" +
            "5. في EchoKey ‹ الإعدادات اختر ميكروفونك الحقيقي. جهاز الإخراج (CABLE Input) يُختار تلقائيًا.\n\n" +
            "لا يغيّر EchoKey إعدادات الصوت في Windows أبدًا.",
        ["language"] = "اللغة",
        ["realMic"] = "الميكروفون الحقيقي",
        ["outputDevice"] = "جهاز الإخراج (الكابل الافتراضي)",
        ["defaultDevice"] = "(الافتراضي في النظام)",
        ["noMic"] = "(بدون ميكروفون)",
        ["autoOutput"] = "(تلقائي: VB-CABLE)",
        ["startWithWindows"] = "التشغيل مع بدء Windows",
        ["closeToTray"] = "الاستمرار بالعمل في الخلفية عند إغلاق النافذة",
        ["latency"] = "حجم مخزن الإخراج: {0} مللي ثانية",
        ["latencyHint"] = "الأقل = تأخير أقل. ارفعه إذا سمعت تقطيعًا.",
        ["refresh"] = "تحديث الأجهزة",
        ["save"] = "حفظ",
        ["cancel"] = "إلغاء",
        ["pressKey"] = "اضغط على الاختصار المطلوب…",
        ["hotkeyFor"] = "اختصار «{0}»",
        ["conflict"] = "هذا الاختصار مستخدم بالفعل للصوت «{0}».",
        ["registerFail"] = "تعذّر تسجيل الاختصار {0}. قد يكون مستخدمًا في برنامج آخر.",
        ["importFailed"] = "تعذّر استيراد «{0}»: {1}",
        ["tooLong"] = "أطول من 5 دقائق",
        ["playFailed"] = "تعذّر تشغيل «{0}»: {1}",
        ["engineError"] = "خطأ في الصوت: {0}",
        ["noOutput"] = "لا يوجد جهاز إخراج. ثبّت VB-CABLE أو اختر جهاز إخراج من الإعدادات.",
        ["micIsCable"] = "الميكروفون المختار هو الكابل الافتراضي. اختر ميكروفونك الحقيقي من الإعدادات.",
        ["micFailed"] = "الميكروفون غير متاح: {0}",
        ["trayOpen"] = "فتح EchoKey",
        ["trayExit"] = "إغلاق البرنامج",
        ["stopAllHotkey"] = "اختصار إيقاف الكل",
        ["monitorEnabled"] = "سماع الأصوات على سماعاتي (لا يُرسل الميكروفون إليها أبدًا)",
        ["monitorDevice"] = "سماعاتي",
        ["micNeedsCable"] = "لم يُرسل الميكروفون لأن جهاز الإخراج ليس كابلًا افتراضيًا (سيرتد صوتك في سماعاتك). اختر VB-CABLE كجهاز إخراج.",
        ["monitorIsCable"] = "لا يمكن أن يكون جهاز السماعات هو الكابل الافتراضي.",
        ["monitorError"] = "خطأ في السماعات: {0}",
        ["confirmDelete"] = "حذف «{0}» من المكتبة؟",
        ["confirmDeleteCategory"] = "حذف التصنيف «{0}»؟ ستبقى أصواته (تنتقل إلى «بدون تصنيف»).",
    };
}
