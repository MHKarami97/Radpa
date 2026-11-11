using System.Globalization;

namespace Radpa.Domain.Services;

/// <summary>
/// Formats metrics (durations, bytes, percentages) cleanly with optional Persian digits (۰-۹)
/// and LTR directional isolation for numeric units in RTL contexts.
/// </summary>
public static class NumberFormatter
{
    public static string FormatBytes(long bytes, bool usePersianDigits, bool persianLocale = false)
    {
        var abs = Math.Abs(bytes);
        string unit;
        double scaled;
        if (abs >= 1024L * 1024 * 1024)
        {
            scaled = bytes / (1024.0 * 1024 * 1024);
            unit = persianLocale ? "گیگابایت" : "GB";
        }
        else if (abs >= 1024L * 1024)
        {
            scaled = bytes / (1024.0 * 1024);
            unit = persianLocale ? "مگابایت" : "MB";
        }
        else if (abs >= 1024L)
        {
            scaled = bytes / 1024.0;
            unit = persianLocale ? "کیلوبایت" : "KB";
        }
        else
        {
            var raw = bytes.ToString(CultureInfo.InvariantCulture);
            var formattedInt = usePersianDigits ? ToPersianDigits(raw) : raw;
            return persianLocale ? $"{formattedInt} بایت" : $"\u200E{formattedInt} B";
        }

        var numStr = scaled.ToString("0.##", CultureInfo.InvariantCulture);
        if (usePersianDigits)
        {
            numStr = ToPersianDigits(numStr).Replace('.', '٫');
        }

        return persianLocale ? $"{numStr} {unit}" : $"\u200E{numStr} {unit}";
    }

    public static string FormatDuration(TimeSpan duration, bool usePersianDigits, bool persianLocale = false)
    {
        var totalSeconds = Math.Max(0, (long)duration.TotalSeconds);
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;

        string raw;
        if (hours > 0)
        {
            raw = persianLocale
                ? $"{hours} ساعت و {minutes} دقیقه"
                : $"{hours}h {minutes}m";
        }
        else if (minutes > 0)
        {
            raw = persianLocale
                ? $"{minutes} دقیقه و {seconds} ثانیه"
                : $"{minutes}m {seconds}s";
        }
        else
        {
            raw = persianLocale ? $"{seconds} ثانیه" : $"{seconds}s";
        }

        return usePersianDigits ? ToPersianDigits(raw) : raw;
    }

    public static string FormatPercent(double? value, bool usePersianDigits, string naText = "N/A")
    {
        if (value is null)
        {
            return naText;
        }

        var num = value.Value.ToString("0.0", CultureInfo.InvariantCulture);
        if (usePersianDigits)
        {
            return ToPersianDigits(num).Replace('.', '٫') + "٪";
        }

        return $"\u200E{num}%";
    }

    public static string FormatNumber(double value, string format, bool usePersianDigits)
    {
        var raw = value.ToString(format, CultureInfo.InvariantCulture);
        return usePersianDigits ? ToPersianDigits(raw).Replace('.', '٫') : raw;
    }

    public static string FormatInteger(long value, bool usePersianDigits)
    {
        var raw = value.ToString(CultureInfo.InvariantCulture);
        return usePersianDigits ? ToPersianDigits(raw) : raw;
    }

    public static string ToPersianDigits(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        return string.Create(input.Length, input, static (chars, src) =>
        {
            for (var i = 0; i < src.Length; i++)
            {
                var ch = src[i];
                chars[i] = ch is >= '0' and <= '9' ? (char)('۰' + (ch - '0')) : ch;
            }
        });
    }
}
