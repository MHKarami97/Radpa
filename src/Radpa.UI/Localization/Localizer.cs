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

    public string AppTitle => IsPersian ? "ردپا — پایشگر محلی برنامه‌ها" : "Radpa — Local App Footprint Monitor";
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

    public string PrivacyTitle => IsPersian ? "ممیزی حریم خصوصی شبکه" : "Network Privacy Auditor";
    public string PrivacySubtitle => IsPersian
        ? "مشاهده مقصدهای ارتباطی هر برنامه به تفکیک آی‌پی، دامنه و رابط شبکه (مقصدهای جدید برجسته می‌شوند)."
        : "Inspect where each app sends or receives data by endpoint, resolved domain, and interface.";
    public string PrivacyFirstTimeOnly => IsPersian ? "فقط مقصدهای اولین بار" : "First-time destinations only";
    public string ColInterface => IsPersian ? "رابط" : "Interface";
    public string ColRemoteIp => IsPersian ? "آی‌پی مقصد" : "Remote IP";
    public string ColDomain => IsPersian ? "دامنه" : "Domain";
    public string ColFirstSeen => IsPersian ? "اولین مشاهده" : "First Seen";
    public string BadgeNewDestination => IsPersian ? "جدید" : "NEW";

    public string InsightsAnomalies => IsPersian ? "هشدارهای ناهنجاری (میانه / MAD)" : "Anomaly Alerts (Rolling Median / MAD)";
    public string InsightsHealthBreakdown => IsPersian ? "جزئیات امتیاز سلامت برنامه‌ها" : "App Health Score Breakdown";
    public string InsightsRulesEngine => IsPersian ? "موتور قوانین هشدار آپلود" : "Upload Alert Rules Engine";
    public string InsightsPeriodCompare => IsPersian ? "مقایسه دو بازه زمانی" : "Compare Two Periods";
    public string BtnRunCompare => IsPersian ? "مقایسه دوره‌ها" : "Compare Periods";
    public string BtnAddRule => IsPersian ? "افزودن قانون" : "Add Rule";

    public string FocusTimerTitle => IsPersian ? "جلسه تمرکز" : "Focus Session Timer";
    public string FocusStart => IsPersian ? "شروع تمرکز" : "Start Focus";
    public string FocusStop => IsPersian ? "پایان جلسه" : "Stop Session";
    public string FocusDistractingApps => IsPersian ? "برنامه‌های حواس‌پرت‌کن (جداشده با ویرگول)" : "Distracting apps (comma-separated)";
    public string FocusMinutes => IsPersian ? "مدت (دقیقه)" : "Duration (minutes)";
    public string ProjectsTitle => IsPersian ? "برچسب‌گذاری پروژه‌ها" : "Project Time Tagging";

    public string SettingsGeneral => IsPersian ? "عمومی و ظاهر" : "General & Appearance";
    public string SettingsLanguage => IsPersian ? "زبان برنامه" : "Language";
    public string SettingsTheme => IsPersian ? "پوسته" : "Theme";
    public string SettingsPersianDigits => IsPersian ? "نمایش اعداد فارسی (۰-۹)" : "Use Persian digits (۰-۹)";
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
    public string SettingsCheckUpdate => IsPersian ? "بررسی بروزرسانی گیت‌هاب" : "Check GitHub Updates";
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
