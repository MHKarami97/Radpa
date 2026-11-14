using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Radpa.Application.Reports;
using Radpa.Domain.Services;

namespace Radpa.UI.Controls;

public sealed class HorizontalBarItem
{
    public required string Label { get; init; }
    public required double PrimaryValue { get; init; }
    public double SecondaryValue { get; init; }
    public required string ValueText { get; init; }
}

/// <summary>
/// Lightweight, AOT-safe horizontal bar chart supporting RTL FlowDirection, Vazirmatn rendering, and stacked values.
/// </summary>
public sealed class HorizontalBarChartControl : Control
{
    protected override bool BypassFlowDirectionPolicies => true;

    public static readonly StyledProperty<IReadOnlyList<HorizontalBarItem>?> ItemsProperty =
        AvaloniaProperty.Register<HorizontalBarChartControl, IReadOnlyList<HorizontalBarItem>?>(nameof(Items));

    public static readonly StyledProperty<IBrush?> PrimaryBrushProperty =
        AvaloniaProperty.Register<HorizontalBarChartControl, IBrush?>(nameof(PrimaryBrush));

    public static readonly StyledProperty<IBrush?> SecondaryBrushProperty =
        AvaloniaProperty.Register<HorizontalBarChartControl, IBrush?>(nameof(SecondaryBrush));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<HorizontalBarChartControl, IBrush?>(nameof(LabelBrush));

    static HorizontalBarChartControl()
    {
        AffectsRender<HorizontalBarChartControl>(ItemsProperty, PrimaryBrushProperty, SecondaryBrushProperty, LabelBrushProperty, FlowDirectionProperty);
    }

    public IReadOnlyList<HorizontalBarItem>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public IBrush? PrimaryBrush
    {
        get => GetValue(PrimaryBrushProperty);
        set => SetValue(PrimaryBrushProperty, value);
    }

    public IBrush? SecondaryBrush
    {
        get => GetValue(SecondaryBrushProperty);
        set => SetValue(SecondaryBrushProperty, value);
    }

    public IBrush? LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var items = Items;
        if (items is null || items.Count == 0 || Bounds.Width < 40 || Bounds.Height < 20)
        {
            return;
        }

        var typeface = new Typeface(new FontFamily("avares://Radpa/Assets/Fonts#Vazirmatn UI"));
        var textBrush = LabelBrush ?? Brushes.Gainsboro;
        var primaryBrush = PrimaryBrush ?? new SolidColorBrush(Color.Parse("#38E8C8"));
        var secondaryBrush = SecondaryBrush ?? new SolidColorBrush(Color.Parse("#60A5FA"));
        var trackBrush = new SolidColorBrush(Color.FromArgb(40, 148, 163, 184));

        var maxVal = items.Max(i => i.PrimaryValue + i.SecondaryValue);
        if (maxVal <= 0)
        {
            maxVal = 1;
        }

        var isRtl = FlowDirection == FlowDirection.RightToLeft;
        var rowHeight = Math.Min(32.0, Bounds.Height / items.Count);
        var labelWidth = Math.Min(150.0, Bounds.Width * 0.34);
        var valueWidth = Math.Min(95.0, Bounds.Width * 0.22);
        var barAreaWidth = Math.Max(30.0, Bounds.Width - labelWidth - valueWidth - 20);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var y = i * rowHeight;

            var labelText = new FormattedText(
                item.Label,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                12,
                textBrush)
            {
                MaxTextWidth = labelWidth,
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
                TextAlignment = isRtl ? TextAlignment.Right : TextAlignment.Left,
            };

            var valText = new FormattedText(
                item.ValueText,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                11,
                textBrush)
            {
                MaxTextWidth = valueWidth,
                MaxLineCount = 1,
                TextAlignment = isRtl ? TextAlignment.Left : TextAlignment.Right,
            };

            var barX = isRtl ? valueWidth + 8 : labelWidth + 8;
            var barY = y + (rowHeight - 10) / 2.0;

            context.DrawRectangle(trackBrush, null, new Rect(barX, barY, barAreaWidth, 10), 4, 4);

            var primaryW = barAreaWidth * (item.PrimaryValue / maxVal);
            var secondaryW = barAreaWidth * (item.SecondaryValue / maxVal);

            if (isRtl)
            {
                var rightEdge = barX + barAreaWidth;
                if (primaryW > 0.5)
                {
                    context.DrawRectangle(primaryBrush, null, new Rect(rightEdge - primaryW, barY, primaryW, 10), 4, 4);
                }

                if (secondaryW > 0.5)
                {
                    context.DrawRectangle(secondaryBrush, null, new Rect(rightEdge - primaryW - secondaryW, barY, secondaryW, 10), 4, 4);
                }

                context.DrawText(labelText, new Point(Bounds.Width - labelWidth, y + 4));
                context.DrawText(valText, new Point(0, y + 5));
            }
            else
            {
                if (primaryW > 0.5)
                {
                    context.DrawRectangle(primaryBrush, null, new Rect(barX, barY, primaryW, 10), 4, 4);
                }

                if (secondaryW > 0.5)
                {
                    context.DrawRectangle(secondaryBrush, null, new Rect(barX + primaryW, barY, secondaryW, 10), 4, 4);
                }

                context.DrawText(labelText, new Point(0, y + 4));
                context.DrawText(valText, new Point(Bounds.Width - valText.Width, y + 5));
            }
        }
    }
}

/// <summary>
/// Lightweight AOT-safe vertical timeline chart showing active duration buckets.
/// </summary>
public sealed class TimelineChartControl : Control
{
    protected override bool BypassFlowDirectionPolicies => true;

    public static readonly StyledProperty<IReadOnlyList<TimelineBucket>?> BucketsProperty =
        AvaloniaProperty.Register<TimelineChartControl, IReadOnlyList<TimelineBucket>?>(nameof(Buckets));

    public static readonly StyledProperty<bool> UsePersianDigitsProperty =
        AvaloniaProperty.Register<TimelineChartControl, bool>(nameof(UsePersianDigits));

    public static readonly StyledProperty<IBrush?> BarBrushProperty =
        AvaloniaProperty.Register<TimelineChartControl, IBrush?>(nameof(BarBrush));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<TimelineChartControl, IBrush?>(nameof(LabelBrush));

    static TimelineChartControl()
    {
        AffectsRender<TimelineChartControl>(BucketsProperty, UsePersianDigitsProperty, BarBrushProperty, LabelBrushProperty, FlowDirectionProperty);
    }

    public IReadOnlyList<TimelineBucket>? Buckets
    {
        get => GetValue(BucketsProperty);
        set => SetValue(BucketsProperty, value);
    }

    public bool UsePersianDigits
    {
        get => GetValue(UsePersianDigitsProperty);
        set => SetValue(UsePersianDigitsProperty, value);
    }

    public IBrush? BarBrush
    {
        get => GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public IBrush? LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var buckets = Buckets;
        if (buckets is null || buckets.Count == 0 || Bounds.Width < 40 || Bounds.Height < 40)
        {
            return;
        }

        var typeface = new Typeface(new FontFamily("avares://Radpa/Assets/Fonts#Vazirmatn UI"));
        var barBrush = BarBrush ?? new SolidColorBrush(Color.Parse("#38E8C8"));
        var textBrush = LabelBrush ?? Brushes.Gainsboro;

        var maxSeconds = Math.Max(1L, buckets.Max(b => b.ActiveSeconds));
        var chartHeight = Bounds.Height - 24;
        var slotW = Bounds.Width / buckets.Count;
        var barW = Math.Max(4.0, slotW * 0.65);

        for (var i = 0; i < buckets.Count; i++)
        {
            var b = buckets[i];
            var h = chartHeight * (b.ActiveSeconds / (double)maxSeconds);
            var x = i * slotW + (slotW - barW) / 2.0;
            var y = chartHeight - h;

            if (h > 1)
            {
                context.DrawRectangle(barBrush, null, new Rect(x, y, barW, h), 3, 3);
            }

            if (buckets.Count <= 12 || i % Math.Max(1, buckets.Count / 6) == 0)
            {
                var label = b.BucketStartUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
                if (UsePersianDigits)
                {
                    label = NumberFormatter.ToPersianDigits(label);
                }

                var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, textBrush);
                context.DrawText(ft, new Point(x, chartHeight + 4));
            }
        }
    }
}
