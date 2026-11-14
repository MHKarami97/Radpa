using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Radpa.Application.Options;

namespace Radpa.UI.Localization;

/// <summary>
/// Zero-reflection, AOT-safe strongly typed localization provider supporting runtime Persian (RTL) and English (LTR) switching.
/// </summary>
public sealed partial class Localizer : ObservableObject
{
    private UiLanguage _language = UiLanguage.Persian;

    public UiLanguage Language
    {
        get => _language;
        set
        {
            if (SetProperty(ref _language, value))
            {
                OnPropertyChanged(string.Empty);
            }
        }
    }

    public bool IsPersian => _language == UiLanguage.Persian;

    public FlowDirection FlowDirection => IsPersian ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public string AppTitle => IsPersian ? "ردپا | پایشگر محلی برنامه‌ها" : "Radpa | Local App Footprint Monitor";
    public string AppShortName => IsPersian ? "ردپا" : "Radpa";
    public string NavDashboard => IsPersian ? "داشبورد" : "Dashboard";
    public string NavPrivacy => IsPersian ? "ممیزی حریم خصوصی" : "Privacy Auditor";
    public string NavInsights => IsPersian ? "تحلیل و قوانین" : "Insights & Rules";
    public string NavFocus => IsPersian ? "تمرکز و پروژه‌ها" : "Focus & Projects";
    public string NavSettings => IsPersian ? "تنظیمات" : "Settings";

    public string RangeToday => IsPersian ? "امروز" : "Today";
    public string Range7Days => IsPersian ? "۷ روز اخیر" : "Last 7 Days";
    public string Range30Days => IsPersian ? "۳۰ روز اخیر" : "Last 30 Days";
    public string RangeCustom => IsPersian ? "بازه دلخواه" : "Custom Range";
    public string SearchWatermark => IsPersian ? "جستجوی نام یا مسیر برنامه..." : "Search app name or path...";
    public string RefreshButton => IsPersian ? "بروزرسانی" : "Refresh";

    public string KpiTotalActive => IsPersian ? "مجموع زمان فعال" : "Total Active Time";
    public string KpiTotalSent => IsPersian ? "مجموع ارسال شبکه" : "Total Sent";
    public string KpiTotalReceived => IsPersian ? "مجموع دریافت شبکه" : "Total Received";
    public string KpiTrackedApps => IsPersian ? "برنامه‌های رصدشده" : "Tracked Apps";
    public string ChartTimelineTitle => IsPersian ? "روند فعالیت و ترافیک شبکه" : "Activity & Network Timeline";
    public string ChartTopAppsTitle => IsPersian ? "پرمصرف‌ترین برنامه‌ها (زمان فعال)" : "Top Applications (Active Time)";
    public string ChartNetworkInterfacesTitle => IsPersian ? "ترافیک شبکه به تفکیک رابط (ارسال / دریافت)" : "Network Traffic by Interface (Sent / Recv)";
    public string InitialDataCollectingTitle => IsPersian ? "در حال پایش و جمع‌آوری داده‌های اولیه..." : "Collecting initial telemetry data...";
    public string InitialDataCollectingMessage => IsPersian
        ? "لطفاً چند لحظه صبر کنید؛ اولین بازه نمونه‌برداری فعالیت برنامه‌ها در حال ثبت و ذخیره‌سازی محلی است."
        : "Please wait a moment; the first telemetry batch is being recorded and stored locally.";
    public string AdminRequiredBadge => IsPersian ? "نیازمند اجرای برنامه با دسترسی Administrator" : "Administrator privilege required";
    public string AdminRequiredDescription => IsPersian
        ? "جهت پایش تفکیکی بسته‌های ارسالی/دریافتی شبکه، برنامه را به‌صورت Run as Administrator اجرا کنید یا ماژول شبکه را در تنظیمات فعال نمایید."
        : "To monitor network sent/received traffic, run as Administrator or enable the network module in Settings.";
    public string AdminRequiredKpiBadge => IsPersian ? "نیازمند دسترسی ادمین" : "Admin required";
    public string ViewDetails => IsPersian ? "جزئیات" : "Details";
    public string BackToDashboard => IsPersian ? "بازگشت به داشبورد" : "Back to Dashboard";
    public string AppDetailTitle => IsPersian ? "شناسنامه و تحلیل عملکرد برنامه" : "Application Footprint & Analytics";
    public string ChartTimelineSubtitle => IsPersian ? "توزیع زمانی ساعات فعالیت در طول روز" : "Hourly distribution of active application time";
    public string TimelineUnitBadge => IsPersian ? "زمان بر حسب دقیقه در هر ساعت" : "Duration in minutes per hour";
    public string AppDetailDestinationsTitle => IsPersian ? "مقصدهای شبکه رصدشده برای این برنامه" : "Network destinations observed for this app";
    public string AppDetailResourceMetrics => IsPersian ? "شاخص‌های مصرف منابع سیستم" : "Resource Consumption Metrics";
    public string AppDetailNetworkMetrics => IsPersian ? "شاخص‌های تبادل داده شبکه" : "Network Data Exchange Metrics";
    public string AppDetailDurations => IsPersian ? "تفکیک زمان‌های استفاده" : "Usage Time Breakdown";
    public string AppDetailHealthBreakdown => IsPersian ? "تفکیک امتیاز سلامت برنامه" : "Health Score Breakdown";
    public string SettingsAdminActiveTitle => IsPersian ? "وضعیت اجرا: دارای دسترسی مدیر (Administrator Mode)" : "Status: Running as Administrator";
    public string SettingsAdminActiveDesc => IsPersian
        ? "برنامه در حال حاضر با دسترسی کامل Administrator اجرا شده است. پایش ترافیک شبکه (ارسال و دریافت) و تمام امکانات پیشرفته بدون محدودیت فعال هستند."
        : "Application is currently running with full Administrator privileges. Network sent/received tracking and all advanced monitors are fully active.";
    public string SettingsStandardUserTitle => IsPersian ? "وضعیت اجرا: کاربر استاندارد (Standard User)" : "Status: Standard User Mode";
    public string SettingsStandardUserDesc => IsPersian
        ? "جهت فعال‌سازی پایش دقیق و تفکیکی بسته‌های شبکه، تیک «رصد دقیق شبکه» را بزنید یا برنامه را با Run as Administrator اجرا کنید."
        : "To monitor per-app network traffic, enable Detailed Network Tracking or launch Radpa with Run as Administrator.";

    public string ColApp => IsPersian ? "برنامه" : "Application";
    public string ColOpen => IsPersian ? "زمان باز" : "Open";
    public string ColActive => IsPersian ? "زمان فعال" : "Active";
    public string ColOnScreen => IsPersian ? "در صفحه" : "On-Screen";
    public string ColCpu => IsPersian ? "پردازنده" : "CPU";
    public string ColRam => IsPersian ? "رم (اوج)" : "RAM (Peak)";
    public string ColDisk => IsPersian ? "دیسک (خواندن/نوشتن)" : "Disk (R/W)";
    public string ColGpu => IsPersian ? "گرافیک / VRAM" : "GPU / VRAM";
    public string ColSent => IsPersian ? "ارسال" : "Sent";
    public string ColReceived => IsPersian ? "دریافت" : "Received";
    public string ColHealth => IsPersian ? "سلامت" : "Health";

    // Privacy Auditor
    public string PrivacyTitle => IsPersian ? "ممیزی حریم خصوصی شبکه" : "Network Privacy Auditor";
    public string PrivacySubtitle => IsPersian
        ? "مشاهده مقصدهای ارتباطی هر برنامه به تفکیک آی‌پی، دامنه و رابط شبکه (مقصدهای جدید برجسته می‌شوند)."
        : "Inspect where each app sends or receives data by endpoint, resolved domain, and interface.";
    public string PrivacyGuideTitle => IsPersian ? "راهنمای ممیزی حریم خصوصی و امنیت ارتباطات شبکه" : "Network Privacy Auditor Guide";
    public string PrivacyGuidePoints => IsPersian
        ? "این ماژول کلیه اتصالات ورودی و خروجی نرم‌افزارهای سیستم را به‌صورت ۱۰۰٪ محلی و آفلاین ممیزی می‌کند:\n• شناسایی مقصدهای مشکوک و بدافزارها: بررسی دقیق اینکه هر برنامه دقیقاً به چه آدرس آی‌پی (IP) و چه نام دامنه‌ای (Domain) در اینترنت متصل شده است.\n• مقصدهای اولین بار (نشان «جدید»): چنانچه نرم‌افزاری برای اولین بار به مقصدی متصل شود، نشان «جدید» درج می‌گردد تا رفتارهای غیرعادی سریعاً کشف شوند.\n• تفکیک رابط شبکه: مشخص می‌کند تبادل داده روی کدام رابط (وای‌فای، کابل اترنت، وی‌پی‌ان یا لوپ‌بک) انجام شده است."
        : "This module provides a 100% offline, local audit of outbound and inbound network connections:\n• Inspect remote IP endpoints and resolved domain hostnames to verify data security and rule out spyware.\n• First-time destinations are tagged with a 'NEW' badge so unexpected connections stand out immediately.\n• View interface breakdown (Wi-Fi, Ethernet, VPN, Loopback) for all network sockets.";
    public string PrivacyFirstTimeOnly => IsPersian ? "فقط مقصدهای اولین بار" : "First-time destinations only";
    public string ColInterface => IsPersian ? "رابط شبکه" : "Interface";
    public string ColRemoteIp => IsPersian ? "آی‌پی مقصد" : "Remote IP";
    public string ColDomain => IsPersian ? "نام دامنه" : "Domain";
    public string ColFirstSeen => IsPersian ? "اولین مشاهده" : "First Seen";
    public string BadgeNewDestination => IsPersian ? "جدید" : "NEW";

    // Insights & Rules
    public string InsightsAnomalies => IsPersian ? "هشدارهای ناهنجاری (میانه / MAD)" : "Anomaly Alerts (Rolling Median / MAD)";
    public string InsightsHealthBreakdown => IsPersian ? "جزئیات امتیاز سلامت برنامه‌ها" : "App Health Score Breakdown";
    public string InsightsRulesEngine => IsPersian ? "موتور قوانین هشدار آپلود" : "Upload Alert Rules Engine";
    public string InsightsPeriodCompare => IsPersian ? "مقایسه دو بازه زمانی" : "Compare Two Periods";
    public string BtnRunCompare => IsPersian ? "مقایسه دوره‌ها" : "Compare Periods";
    public string BtnAddRule => IsPersian ? "افزودن قانون" : "Add Rule";
    public string InsightsGuideTitle => IsPersian ? "راهنمای تحلیل هوشمند و قوانین سیستم" : "Intelligent Insights & Rules Guide";
    public string InsightsGuideAnomalies => IsPersian
        ? "• هشدارهای ناهنجاری: تشخیص خودکار جهش‌های نامتعارف مصرف پردازنده، حافظه رم یا ترافیک توسط الگوریتم آماری میانه متحرک (MAD)."
        : "• Anomalies: Automatic detection of unusual CPU, RAM, or network spikes using rolling median (MAD) statistics.";
    public string InsightsGuideHealth => IsPersian
        ? "• نمره سلامت: ارزیابی ۱۰۰ نمره‌ای رفتار بهینه نرم‌افزار بر اساس مصرف منابع (۶۰٪)، پاکیزگی شبکه (۲۵٪) و سرعت راه‌اندازی (۱۵٪)."
        : "• Health Score: 100-point efficiency rating evaluating CPU/RAM efficiency (60%), network footprint (25%), and startup overhead (15%).";
    public string InsightsGuideRules => IsPersian
        ? "• موتور قوانین ترافیک: تعیین سقف مجاز آپلود در ساعت برای نرم‌افزارهای خاص (جهت نظارت بر مصرف اینترنت و پیشگیری از نشت داده)."
        : "• Upload Rules: Set hourly upload caps for specific applications to control bandwidth and prevent exfiltration.";
    public string InsightsGuidePeriod => IsPersian
        ? "• مقایسه دوره‌ها: سنجش تغییرات مصرف و زمان کارکرد برنامه‌ها بین دو بازه زمانی (مثلاً هفته اخیر نسبت به هفته قبل)."
        : "• Period Compare: Compare active duration and resource footprint between two time windows to evaluate trends.";
    public string InsightsAnomaliesEmpty => IsPersian
        ? "✅ هیچ ناهنجاری مصرفی ثبت نشده است؛ رفتار کلیه نرم‌افزارها در وضعیت نرمال و استاندارد قرار دارد."
        : "No anomalies detected; all applications are operating within normal thresholds.";
    public string ColHealthTotal => IsPersian ? "نمره کل" : "Total Score";
    public string ColHealthResource => IsPersian ? "منابع (۶۰٪)" : "Resources (60%)";
    public string ColHealthNetwork => IsPersian ? "شبکه (۲۵٪)" : "Network (25%)";
    public string ColHealthStartup => IsPersian ? "راه‌اندازی (۱۵٪)" : "Startup (15%)";
    public string ColHealthGrade => IsPersian ? "رتبه" : "Grade";
    public string ColCompareBefore => IsPersian ? "دوره قبل" : "Previous";
    public string ColCompareAfter => IsPersian ? "دوره جاری" : "Current";
    public string ColCompareDeltaActive => IsPersian ? "تغییر فعالیت (Δ)" : "Active Δ";
    public string ColCompareDeltaCpu => IsPersian ? "تغییر پردازنده (Δ)" : "CPU Δ";
    public string ColCompareDeltaNet => IsPersian ? "تغییر شبکه (Δ)" : "Network Δ";
    public string RuleNamePlaceholder => IsPersian ? "نام قانون (مثلاً سقف آپلود)" : "Rule name";
    public string RuleAppPatternPlaceholder => IsPersian ? "الگوی برنامه (*telegram*)" : "App pattern (*)";
    public string RuleLimitUnit => IsPersian ? "مگابایت در ساعت" : "MB / hour";
    public string RuleDescriptionHint => IsPersian
        ? "با تعریف الگوهایی مانند *telegram* یا chrome.exe و تعیین سقف مگابایت، در صورت خروج داده بیش از حد در هر ساعت هشدار دریافت کنید."
        : "Define process patterns (e.g. *telegram* or chrome.exe) and maximum hourly upload to receive alerts on excess traffic.";

    // Focus & Projects
    public string FocusTimerTitle => IsPersian ? "جلسه تمرکز" : "Focus Session Timer";
    public string FocusStart => IsPersian ? "شروع تمرکز" : "Start Focus";
    public string FocusStop => IsPersian ? "پایان جلسه" : "Stop Session";
    public string FocusDistractingApps => IsPersian ? "برنامه‌های حواس‌پرت‌کن (جداشده با ویرگول)" : "Distracting apps (comma-separated)";
    public string FocusMinutes => IsPersian ? "مدت (دقیقه)" : "Duration (minutes)";
    public string ProjectsTitle => IsPersian ? "برچسب‌گذاری پروژه‌ها" : "Project Time Tagging";
    public string FocusGuideTitle => IsPersian ? "راهنمای جلسه تمرکز (Deep Work) و مدیریت پروژه‌ها" : "Focus Sessions & Project Tagging Guide";
    public string FocusGuideSession => IsPersian
        ? "• جلسات تمرکز عمیق: مشابه تکنیک پومودورو، برای کار بدون حواس‌پرتی زمان جلسه را تعیین کرده و برنامه‌های مخل تمرکز (مانند پیام‌رسان‌ها یا شبکه‌های اجتماعی) را در کادر بنویسید. در حین جلسه، در صورت باز شدن این برنامه‌ها هشدار داده می‌شود و در پایان، گزارش کامل بازدهی جلسه ارائه خواهد شد."
        : "• Focus Sessions: Similar to Pomodoro, set your deep work duration and list distracting apps. You will be alerted if distracting apps are accessed, followed by a productivity summary at the end.";
    public string FocusGuideProjects => IsPersian
        ? "• برچسب‌گذاری و تفکیک پروژه‌ها: با ثبت نام پروژه و الگوی پنجره یا نام فرایند (مانند *Radpa* یا code.exe یا *.fig*)، ردپا زمان‌های کار شما روی هر پروژه را به‌طور خودکار تفکیک و در جدول محاسبه می‌کند."
        : "• Project Tagging: Map window titles or process names (e.g. *Radpa*, code.exe) to projects to automatically aggregate time spent per project.";
    public string ProjectNamePlaceholder => IsPersian ? "نام پروژه (مثلاً توسعه رادپا)" : "Project name";
    public string ProjectPatternPlaceholder => IsPersian ? "الگوی پنجره یا فرایند (*Radpa*)" : "Process or window pattern";
    public string ColProjectName => IsPersian ? "پروژه" : "Project";
    public string ColProjectActive => IsPersian ? "زمان فعال (کار مفید)" : "Active Time";
    public string ColProjectOnScreen => IsPersian ? "زمان روی مانیتور" : "On-Screen Time";
    public string ProjectRuleHint => IsPersian
        ? "با تعریف الگوی پنجره یا نام فایل اجرایی (مثلاً *Figma* یا code.exe)، زمان‌های کارکرد به‌طور هوشمند به نام پروژه اختصاص می‌یابند."
        : "Define window titles or executable patterns to automatically classify active work time under the project.";

    // Settings
    public string SettingsGeneral => IsPersian ? "عمومی و ظاهر" : "General & Appearance";
    public string SettingsLanguage => IsPersian ? "زبان برنامه" : "Language";
    public string SettingsTheme => IsPersian ? "پوسته" : "Theme";
    public string SettingsPersianDigits => IsPersian ? "نمایش اعداد فارسی (۰-۹)" : "Use Persian digits (۰-۹)";
    public string SettingsNotificationsEnabled => IsPersian ? "نمایش اعلان‌ها و هشدارهای سیستم (نوتیفیکیشن)" : "Enable alert and system notifications";
    public string SettingsIdleThreshold => IsPersian ? "آستانه بی‌کاری (ثانیه)" : "Idle threshold (seconds)";
    public string SettingsSamplingInterval => IsPersian ? "بازه نمونه‌برداری (ثانیه، حداقل ۲)" : "Sampling interval (seconds, min 2)";
    public string SettingsRetentionDays => IsPersian ? "نگهداری داده (روز)" : "Data retention (days)";
    public string SettingsStartWithWindows => IsPersian ? "اجرای خودکار با ویندوز (کاربر جاری)" : "Start with Windows (current user)";
    public string SettingsMinimizeToTray => IsPersian ? "بستن پنجره به سینی سیستم (ادامه جمع‌آوری در پس‌زمینه)" : "Close UI to tray (keep collector running)";
    public string SettingsPortableMode => IsPersian ? "حالت قابل‌حمل (ذخیره داده کنار فایل اجرایی)" : "Portable mode (store data next to executable)";
    public string SettingsRecordTitles => IsPersian ? "ثبت عنوان پنجره‌ها (خاموش به‌صورت پیش‌فرض)" : "Record window titles (off by default)";
    public string SettingsDetailedNetwork => IsPersian ? "رصد دقیق شبکه (نیازمند دسترسی مدیر via UAC)" : "Detailed network monitoring (requires Admin UAC)";
    public string SettingsNetworkAdminExplanation => IsPersian
        ? "برای ثبت دقیق بایت‌های ارسالی و دریافتی هر برنامه، ماژول Radpa.NetCollector با دسترسی مدیر (UAC) اجرا می‌شود. ردپا هرگز در شروع کار دسترسی مدیر نمی‌خواهد."
        : "Detailed per-app network tracking launches Radpa.NetCollector with Administrator rights via UAC. Normal features work without admin.";
    public string SettingsEncryption => IsPersian ? "رمزگذاری فیلدهای حساس (AES-GCM + DPAPI)" : "Encrypt sensitive fields (AES-GCM + DPAPI)";
    public string SettingsGroupingRules => IsPersian ? "قوانین گروه‌بندی چندفرایندی" : "Multi-Process Grouping Rules";
    public string SettingsDataAndExport => IsPersian ? "خروجی، نگهداری و پایش خودکار" : "Export, Data Controls & Self-Monitor";
    public string SettingsCheckUpdate => IsPersian ? "بررسی نسخه جدید گیت‌هاب" : "Check GitHub Updates";
    public string SettingsUpdatesTitle => IsPersian ? "بروزرسانی نرم‌افزار و مخزن" : "Software Updates & Releases";
    public string SettingsUpdatesDesc => IsPersian
        ? "بررسی خودکار و دستی نسخه‌های جدید منتشرشده در مخزن گیت‌هاب برنامه."
        : "Automatic and manual checks for new Radpa releases on GitHub.";
    public string SettingsAutoUpdateCheck => IsPersian ? "بررسی خودکار نسخه‌های جدید در گیت‌هاب" : "Check for updates automatically";
    public string SettingsCurrentVersionPrefix => IsPersian ? "نسخه جاری: " : "Current version: ";

    public string SettingsSelfMonitorTitle => IsPersian ? "شناسنامه و مصرف منابع خود نرم‌افزار (Self-Monitor)" : "Radpa Self-Monitor & Footprint";
    public string SettingsSelfMonitorDesc => IsPersian
        ? "پایش زنده و شفاف مصرف پردازنده، حافظه رم و پایگاه داده توسط خود ردپا در سیستم شما."
        : "Live, transparent resource footprint and memory consumption of Radpa on your machine.";
    public string SettingsSelfCpu => IsPersian ? "پردازنده ردپا" : "Radpa CPU";
    public string SettingsSelfRam => IsPersian ? "رم در حال استفاده (Working Set)" : "Working Set RAM";
    public string SettingsSelfPrivate => IsPersian ? "حافظه اختصاصی (Private)" : "Private Memory";
    public string SettingsSelfDb => IsPersian ? "حجم پایگاه داده" : "Database Size";
    public string SettingsSelfThreads => IsPersian ? "تعداد نخ‌ها" : "Threads";
    public string SettingsSelfPid => IsPersian ? "شناسه فرآیند (PID)" : "Process ID";
    public string SettingsSelfNetworkHelper => IsPersian ? "ماژول ناظر شبکه" : "Network Helper";
    public string SettingsHelperActive => IsPersian ? "فعال (دسترسی مدیر)" : "Active (Elevated)";
    public string SettingsHelperInactive => IsPersian ? "غیرفعال (کاربر استاندارد)" : "Inactive (Standard)";
    public string SettingsBtnRefreshSelf => IsPersian ? "بروزرسانی مصرف" : "Refresh Metrics";
    public string SettingsDataControlsTitle => IsPersian ? "کنترل داده‌ها، خروجی و پشتیبان‌گیری" : "Data Controls, Export & Maintenance";

    public string BtnSaveSettings => IsPersian ? "ذخیره تنظیمات" : "Save Settings";
    public string BtnExportCsv => IsPersian ? "خروجی CSV" : "Export CSV";
    public string BtnExportJson => IsPersian ? "خروجی JSON" : "Export JSON";
    public string BtnExportHtml => IsPersian ? "گزارش هفتگی HTML" : "Weekly HTML Report";
    public string BtnVacuumDb => IsPersian ? "بهینه‌سازی پایگاه داده (VACUUM)" : "Vacuum Database";
    public string BtnDeleteAll => IsPersian ? "حذف تمام داده‌ها" : "Delete All Data";
    public string SelfMonitorTitle => IsPersian ? "مصرف منابع خود ردپا" : "Radpa Self-Monitor";
    public string LocalPrivacyBadge => IsPersian ? "۱۰۰٪ محلی • بدون حساب و تله‌متری" : "100% Local • No Telemetry";
    public string NotAvailable => IsPersian ? "ناموجود" : "N/A";
    public string UpdateBannerSkip => IsPersian ? "نادیده گرفتن این نسخه" : "Skip this version";
    public string UpdateBannerOpen => IsPersian ? "مشاهده نسخه جدید" : "Open Release";
}
