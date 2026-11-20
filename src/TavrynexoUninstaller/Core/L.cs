using System.Windows.Markup;

namespace Tavrynexo.Core;

/// <summary>Tiny localization layer (English / Arabic). Language is chosen at startup.</summary>
public static class L
{
    public static bool IsArabic { get; private set; }

    public static void Init(string lang) => IsArabic = lang == "ar";

    public static string Get(string key) =>
        D.TryGetValue(key, out var v) ? (IsArabic ? v.ar : v.en) : key;

    public static string F(string key, params object[] args) =>
        string.Format(Get(key), args);

    private static readonly Dictionary<string, (string en, string ar)> D = new()
    {
        // Navigation
        ["NavPrograms"] = ("Programs", "البرامج"),
        ["NavStore"] = ("Store Apps", "تطبيقات المتجر"),
        ["NavStartup"] = ("Startup", "بدء التشغيل"),
        ["NavJunk"] = ("Junk Cleaner", "تنظيف الملفات"),
        ["NavSettings"] = ("Settings & About", "الإعدادات وحول"),
        ["Tagline"] = ("Deep uninstall. Zero leftovers.", "إزالة عميقة. بدون بقايا."),

        // Common
        ["Search"] = ("Search...", "بحث..."),
        ["Refresh"] = ("Refresh", "تحديث"),
        ["Cancel"] = ("Cancel", "إلغاء"),
        ["Close"] = ("Close", "إغلاق"),
        ["Loading"] = ("Loading...", "جارٍ التحميل..."),
        ["AppTitle"] = ("Tavrynexo Uninstaller", "Tavrynexo Uninstaller"),
        ["Error"] = ("Error", "خطأ"),
        ["Info"] = ("Information", "معلومة"),
        ["Warning"] = ("Warning", "تنبيه"),

        // Programs
        ["ProgramsTitle"] = ("Installed Programs", "البرامج المثبتة"),
        ["ColName"] = ("Name", "الاسم"),
        ["ColPublisher"] = ("Publisher", "الناشر"),
        ["ColSize"] = ("Size", "الحجم"),
        ["ColVersion"] = ("Version", "الإصدار"),
        ["ColDate"] = ("Installed", "تاريخ التثبيت"),
        ["BtnUninstall"] = ("Uninstall", "إزالة"),
        ["BtnForce"] = ("Force Remove", "إزالة إجبارية"),
        ["BtnOpenFolder"] = ("Open Folder", "فتح المجلد"),
        ["ProgramsCount"] = ("{0} programs  ·  {1}", "{0} برنامج  ·  {1}"),
        ["SelectFirst"] = ("Select at least one program first.", "اختر برنامجاً واحداً على الأقل أولاً."),
        ["ConfirmUninstall"] = ("Uninstall the following?\n\n{0}\n\nThe program's own uninstaller runs first, then Tavrynexo scans for leftovers.",
                                "هل تريد إزالة البرامج التالية؟\n\n{0}\n\nسيعمل مُزيل البرنامج الأصلي أولاً ثم يفحص Tavrynexo البقايا."),
        ["ConfirmForce"] = ("FORCE REMOVE skips the program's uninstaller and deletes its folder, registry entries, services and tasks.\n\nUse it only for broken or half-removed programs.\n\n{0}\n\nContinue?",
                            "الإزالة الإجبارية تتجاوز مُزيل البرنامج وتحذف مجلده ومفاتيحه وخدماته ومهامه.\n\nاستخدمها فقط للبرامج التالفة أو المحذوفة جزئياً.\n\n{0}\n\nمتابعة؟"),
        ["CreatingRestore"] = ("Creating a system restore point...", "جارٍ إنشاء نقطة استعادة للنظام..."),
        ["Uninstalling"] = ("Running uninstaller: {0}", "جارٍ تشغيل المُزيل: {0}"),
        ["Scanning"] = ("Scanning leftovers: {0}", "جارٍ فحص البقايا: {0}"),
        ["StillInstalled"] = ("\"{0}\" still appears to be installed (the uninstall may have been cancelled or failed), so no leftover scan was done for it to protect your files.\n\nIf its uninstaller is broken, use Force Remove.",
                              "ما زال \"{0}\" يظهر كمثبّت (ربما أُلغيت الإزالة أو فشلت) لذلك لم يتم فحص البقايا له لحماية ملفاتك.\n\nإذا كان مُزيله تالفاً فاستخدم الإزالة الإجبارية."),
        ["NoUninstaller"] = ("\"{0}\" has no uninstall command registered. Use Force Remove to clean it manually.",
                             "\"{0}\" لا يملك أمر إزالة مسجل. استخدم الإزالة الإجبارية لتنظيفه يدوياً."),
        ["NoLeftovers"] = ("Uninstall finished. No leftovers were found. Your system is clean.", "اكتملت الإزالة. لم يتم العثور على بقايا. نظامك نظيف."),
        ["NoFolder"] = ("No install folder is registered for this program.", "لا يوجد مجلد تثبيت مسجل لهذا البرنامج."),

        // Store
        ["StoreTitle"] = ("Microsoft Store Apps", "تطبيقات Microsoft Store"),
        ["BtnRemoveStore"] = ("Remove App", "إزالة التطبيق"),
        ["ConfirmStore"] = ("Remove these Store apps?\n\n{0}", "إزالة تطبيقات المتجر التالية؟\n\n{0}"),
        ["RemovingStore"] = ("Removing: {0}", "جارٍ الإزالة: {0}"),
        ["StoreFailed"] = ("Could not remove: {0}\n\n{1}", "تعذرت إزالة: {0}\n\n{1}"),

        // Startup
        ["StartupTitle"] = ("Startup Programs", "برامج بدء التشغيل"),
        ["StartupHint"] = ("Untick to disable an entry without deleting it. Changes apply at next sign-in.", "ألغِ التحديد لتعطيل العنصر دون حذفه. تُطبّق التغييرات عند تسجيل الدخول التالي."),
        ["ColCommand"] = ("Command", "الأمر"),
        ["ColLocation"] = ("Location", "المصدر"),
        ["BtnDeleteEntry"] = ("Delete", "حذف"),
        ["ConfirmDeleteEntry"] = ("Delete startup entry \"{0}\" permanently?", "حذف عنصر بدء التشغيل \"{0}\" نهائياً؟"),

        // Junk
        ["JunkTitle"] = ("Junk Cleaner", "تنظيف الملفات غير الضرورية"),
        ["BtnAnalyze"] = ("Analyze", "تحليل"),
        ["BtnClean"] = ("Clean Selected", "تنظيف المحدد"),
        ["JunkTotal"] = ("Selected: {0}", "المحدد: {0}"),
        ["JunkDone"] = ("Cleaning finished. Freed about {0}. Files in use were skipped.", "اكتمل التنظيف. تم تحرير حوالي {0}. تم تخطي الملفات المستخدمة."),
        ["JunkUserTemp"] = ("User temporary files", "الملفات المؤقتة للمستخدم"),
        ["JunkWinTemp"] = ("Windows temporary files", "الملفات المؤقتة لويندوز"),
        ["JunkWer"] = ("Windows error reports", "تقارير أخطاء ويندوز"),
        ["JunkChrome"] = ("Google Chrome cache", "ذاكرة Chrome المؤقتة"),
        ["JunkEdge"] = ("Microsoft Edge cache", "ذاكرة Edge المؤقتة"),
        ["JunkFirefox"] = ("Mozilla Firefox cache", "ذاكرة Firefox المؤقتة"),
        ["JunkD3D"] = ("DirectX shader cache", "ذاكرة DirectX للتظليل"),
        ["JunkRecycle"] = ("Recycle Bin", "سلة المحذوفات"),

        // Settings
        ["SettingsTitle"] = ("Settings & About", "الإعدادات وحول البرنامج"),
        ["Language"] = ("Language", "اللغة"),
        ["OptRestore"] = ("Create a system restore point before uninstalling", "إنشاء نقطة استعادة قبل الإزالة"),
        ["OptBackup"] = ("Back up registry keys (.reg) before deleting them", "نسخ احتياطي لمفاتيح السجل (.reg) قبل حذفها"),
        ["BtnOpenBackups"] = ("Open backups folder", "فتح مجلد النسخ الاحتياطية"),
        ["BtnOpenLogs"] = ("Open logs folder", "فتح مجلد السجلات"),
        ["RestartNote"] = ("Restart the app to apply the new language.", "أعد تشغيل البرنامج لتطبيق اللغة الجديدة."),
        ["AboutText"] = ("Tavrynexo Uninstaller removes programs completely: it runs the original uninstaller, then hunts down leftover folders, files, registry keys, services and scheduled tasks. Every deletion is reviewed by you first, protected system locations are blocked, and registry keys are backed up.",
                         "يزيل Tavrynexo Uninstaller البرامج بالكامل: يشغّل المُزيل الأصلي ثم يبحث عن المجلدات والملفات ومفاتيح السجل والخدمات والمهام المتبقية. كل عملية حذف تراجعها بنفسك أولاً، والمواقع المحمية للنظام محظورة، ومفاتيح السجل تُنسخ احتياطياً."),
        ["Version"] = ("Version {0}", "الإصدار {0}"),

        // Leftover window
        ["LeftoverTitle"] = ("Leftovers Found", "البقايا التي تم العثور عليها"),
        ["LeftoverSub"] = ("{0} items found. Review them, untick anything you want to keep.", "تم العثور على {0} عنصر. راجعها وألغِ تحديد أي شيء تريد الاحتفاظ به."),
        ["LeftoverHint"] = ("High confidence items are pre-selected. Medium confidence items are not: check them yourself before deleting.",
                            "العناصر عالية الثقة محددة مسبقاً. العناصر متوسطة الثقة غير محددة: تحقق منها بنفسك قبل الحذف."),
        ["BtnSelectAll"] = ("Select all", "تحديد الكل"),
        ["BtnSelectNone"] = ("Select none", "إلغاء التحديد"),
        ["BtnDeleteSelected"] = ("Delete selected", "حذف المحدد"),
        ["ColKind"] = ("Type", "النوع"),
        ["ColPath"] = ("Location", "المسار"),
        ["ColConfidence"] = ("Confidence", "الثقة"),
        ["ConfHigh"] = ("High", "عالية"),
        ["ConfMedium"] = ("Medium", "متوسطة"),
        ["KindFile"] = ("File", "ملف"),
        ["KindFolder"] = ("Folder", "مجلد"),
        ["KindRegKey"] = ("Registry key", "مفتاح سجل"),
        ["KindRegValue"] = ("Registry value", "قيمة سجل"),
        ["KindService"] = ("Service", "خدمة"),
        ["KindTask"] = ("Scheduled task", "مهمة مجدولة"),
        ["ConfirmDeleteLeftovers"] = ("Permanently delete {0} selected items?\n\nRegistry keys are backed up first (if enabled). Files and folders are deleted permanently.",
                                      "حذف {0} عنصر محدد نهائياً؟\n\nتُنسخ مفاتيح السجل احتياطياً أولاً (إذا كان الخيار مفعلاً). الملفات والمجلدات تُحذف نهائياً."),
        ["Deleting"] = ("Deleting leftovers...", "جارٍ حذف البقايا..."),
        ["DeleteSummary"] = ("Deleted: {0}\nFailed: {1}\nScheduled for removal at next restart: {2}\n\nRegistry backups: {3}",
                             "تم حذف: {0}\nفشل: {1}\nمجدول للحذف عند إعادة التشغيل: {2}\n\nنسخ السجل الاحتياطية: {3}"),
        ["SelectedCount"] = ("{0} selected  ·  {1}", "{0} محدد  ·  {1}"),
        ["FailedList"] = ("\n\nCould not delete:\n{0}", "\n\nتعذر حذف:\n{0}"),
    };
}

[MarkupExtensionReturnType(typeof(string))]
public class TExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public TExtension() { }
    public TExtension(string key) { Key = key; }
    public override object ProvideValue(IServiceProvider serviceProvider) => L.Get(Key);
}
