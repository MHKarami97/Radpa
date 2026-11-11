using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Radpa.Domain.Services;
using Radpa.Domain.ValueObjects;

namespace Radpa.Application.Reports;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DashboardSnapshot))]
public partial class ReportExportJsonContext : JsonSerializerContext;

/// <summary>
/// AOT-safe exporter generating CSV, JSON (via source generation), and printable self-contained HTML reports.
/// </summary>
public static class ReportExporter
{
    public static string ToCsv(DashboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var sb = new StringBuilder();
        sb.AppendLine("AppName,ExecutablePath,OpenSeconds,ActiveSeconds,OnScreenSeconds,AvgCpuPercent,PeakCpuPercent,PeakRamBytes,DiskReadBytes,DiskWriteBytes,AvgGpuPercent,PeakVramBytes,NetworkSentBytes,NetworkReceivedBytes,HealthScore");

        foreach (var a in snapshot.Apps)
        {
            sb.Append(EscapeCsv(a.DisplayName)).Append(',')
              .Append(EscapeCsv(a.ExecutablePath)).Append(',')
              .Append(((long)a.OpenDuration.TotalSeconds).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(((long)a.ActiveDuration.TotalSeconds).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(((long)a.OnScreenDuration.TotalSeconds).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(a.AverageCpuPercent.ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
              .Append(a.PeakCpuPercent.ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
              .Append(a.PeakWorkingSetBytes.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(a.DiskReadBytes.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(a.DiskWriteBytes.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(a.AverageGpuPercent?.ToString("0.0", CultureInfo.InvariantCulture) ?? "N/A").Append(',')
              .Append(a.PeakGpuVramBytes?.ToString(CultureInfo.InvariantCulture) ?? "N/A").Append(',')
              .Append(a.NetworkSentBytes.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(a.NetworkReceivedBytes.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(a.HealthScore.TotalScore.ToString("0.0", CultureInfo.InvariantCulture))
              .AppendLine();
        }

        return sb.ToString();
    }

    public static string ToJson(DashboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(snapshot, ReportExportJsonContext.Default.DashboardSnapshot);
    }

    public static string ToHtmlReport(DashboardSnapshot snapshot, bool persianRtl, bool usePersianDigits, string title)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var dir = persianRtl ? "rtl" : "ltr";
        var lang = persianRtl ? "fa" : "en";
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<html lang=\"{lang}\" dir=\"{dir}\">");
        sb.AppendLine("<head><meta charset=\"utf-8\"/>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<title>{WebUtility.HtmlEncode(title)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:'Vazirmatn','Segoe UI',sans-serif;background:#0f172a;color:#e2e8f0;margin:24px;}");
        sb.AppendLine("h1{color:#38e8c8;} table{width:100%;border-collapse:collapse;margin-top:16px;font-size:13px;}");
        sb.AppendLine("th,td{border:1px solid #334155;padding:8px 10px;text-align:start;} th{background:#1e293b;}");
        sb.AppendLine(".kpi{display:inline-block;background:#1e293b;padding:12px 18px;border-radius:8px;margin-inline-end:12px;}");
        sb.AppendLine("@media print{body{background:#fff;color:#000;} th{background:#eee;}}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<h1>{WebUtility.HtmlEncode(title)}</h1>");

        var totalActiveStr = NumberFormatter.FormatDuration(snapshot.TotalActive, usePersianDigits, persianRtl);
        var totalSentStr = NumberFormatter.FormatBytes(snapshot.TotalNetworkSent, usePersianDigits, persianRtl);
        var totalRecvStr = NumberFormatter.FormatBytes(snapshot.TotalNetworkReceived, usePersianDigits, persianRtl);

        sb.AppendLine(CultureInfo.InvariantCulture, $"<div class=\"kpi\">Active: <strong>{WebUtility.HtmlEncode(totalActiveStr)}</strong></div>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<div class=\"kpi\">Sent: <strong>{WebUtility.HtmlEncode(totalSentStr)}</strong></div>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<div class=\"kpi\">Received: <strong>{WebUtility.HtmlEncode(totalRecvStr)}</strong></div>");

        sb.AppendLine("<table><thead><tr>");
        sb.AppendLine(persianRtl
            ? "<th>برنامه</th><th>زمان فعال</th><th>در صفحه</th><th>CPU</th><th>RAM</th><th>GPU</th><th>ارسال</th><th>دریافت</th><th>امتیاز سلامت</th>"
            : "<th>Application</th><th>Active</th><th>On-Screen</th><th>CPU</th><th>RAM</th><th>GPU</th><th>Sent</th><th>Received</th><th>Health</th>");
        sb.AppendLine("</tr></thead><tbody>");

        foreach (var app in snapshot.Apps)
        {
            var active = NumberFormatter.FormatDuration(app.ActiveDuration, usePersianDigits, persianRtl);
            var onScreen = NumberFormatter.FormatDuration(app.OnScreenDuration, usePersianDigits, persianRtl);
            var cpu = NumberFormatter.FormatPercent(app.AverageCpuPercent, usePersianDigits);
            var ram = NumberFormatter.FormatBytes(app.PeakWorkingSetBytes, usePersianDigits, persianRtl);
            var gpu = NumberFormatter.FormatPercent(app.AverageGpuPercent, usePersianDigits);
            var sent = NumberFormatter.FormatBytes(app.NetworkSentBytes, usePersianDigits, persianRtl);
            var recv = NumberFormatter.FormatBytes(app.NetworkReceivedBytes, usePersianDigits, persianRtl);
            var score = NumberFormatter.FormatNumber(app.HealthScore.TotalScore, "0.0", usePersianDigits);

            sb.Append("<tr>")
              .Append(CultureInfo.InvariantCulture, $"<td>{WebUtility.HtmlEncode(app.DisplayName)}</td>")
              .Append(CultureInfo.InvariantCulture, $"<td>{WebUtility.HtmlEncode(active)}</td>")
              .Append(CultureInfo.InvariantCulture, $"<td>{WebUtility.HtmlEncode(onScreen)}</td>")
              .Append(CultureInfo.InvariantCulture, $"<td>{WebUtility.HtmlEncode(cpu)}</td>")
              .Append(CultureInfo.InvariantCulture, $"<td>{WebUtility.HtmlEncode(ram)}</td>")
              .Append(CultureInfo.InvariantCulture, $"<td>{WebUtility.HtmlEncode(gpu)}</td>")
              .Append(CultureInfo.InvariantCulture, $"<td>{WebUtility.HtmlEncode(sent)}</td>")
              .Append(CultureInfo.InvariantCulture, $"<td>{WebUtility.HtmlEncode(recv)}</td>")
              .Append(CultureInfo.InvariantCulture, $"<td>{WebUtility.HtmlEncode(score)} ({WebUtility.HtmlEncode(app.HealthScore.Grade)})</td>")
              .AppendLine("</tr>");
        }

        sb.AppendLine("</tbody></table></body></html>");
        return sb.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
