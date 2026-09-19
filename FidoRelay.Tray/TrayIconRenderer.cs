using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using FidoRelay.Core.Models;

namespace FidoRelay.Tray;

/// <summary>
/// Draws the tray glyph at runtime rather than shipping .ico assets, so the icon can encode live
/// data: the fill colour is the activity state and the surrounding arc is session quota used.
/// This is the tray-side analogue of the ESP32 display.
/// </summary>
internal static class TrayIconRenderer
{
    // Tray icons are requested at the small-icon size; 32px covers the common 150%/200% DPI scales.
    private const int Size = 32;

    private static readonly Color IdleColor = Color.FromArgb(0x4C, 0x8B, 0xF5);
    private static readonly Color WorkingColor = Color.FromArgb(0x3F, 0xB9, 0x50);
    private static readonly Color WaitingColor = Color.FromArgb(0xF5, 0xA6, 0x23);
    private static readonly Color UnknownColor = Color.FromArgb(0x8A, 0x8A, 0x8A);
    private static readonly Color TrackColor = Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF);
    private static readonly Color QuotaColor = Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF);
    private static readonly Color QuotaHighColor = Color.FromArgb(0xF0, 0xE5, 0x48, 0x48);

    // DllImport rather than LibraryImport: the source generator would require AllowUnsafeBlocks
    // across the whole project for one blittable call, which is a poor trade.
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>
    /// Renders an icon for the given state. The caller owns the returned <see cref="Icon"/> and must
    /// pass it to <see cref="Release"/> once the tray has stopped using it.
    /// </summary>
    public static Icon Render(ActivityState activity, double? sessionUsedPercentage)
    {
        using var bitmap = new Bitmap(Size, Size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            var fill = activity switch
            {
                ActivityState.Working => WorkingColor,
                ActivityState.Waiting => WaitingColor,
                ActivityState.Idle => IdleColor,
                _ => UnknownColor,
            };

            var body = new Rectangle(6, 6, Size - 13, Size - 13);
            using (var brush = new SolidBrush(fill))
            {
                graphics.FillEllipse(brush, body);
            }

            DrawQuotaArc(graphics, sessionUsedPercentage);
        }

        return CloneFromBitmap(bitmap);
    }

    /// <summary>An arc around the glyph showing how much of the five-hour window is spent.</summary>
    private static void DrawQuotaArc(Graphics graphics, double? sessionUsedPercentage)
    {
        var ring = new Rectangle(2, 2, Size - 5, Size - 5);

        using (var trackPen = new Pen(TrackColor, 3f))
        {
            graphics.DrawEllipse(trackPen, ring);
        }

        if (sessionUsedPercentage is not { } used)
        {
            return;
        }

        var sweep = (float)(Math.Clamp(used, 0, 100) / 100d * 360d);
        if (sweep <= 0)
        {
            return;
        }

        using var pen = new Pen(used >= 80 ? QuotaHighColor : QuotaColor, 3f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        // Start at twelve o'clock and fill clockwise, the way a gauge reads.
        graphics.DrawArc(pen, ring, -90f, sweep);
    }

    // GetHicon hands out an unmanaged handle that Icon does not own, so clone into a managed icon
    // and destroy the handle immediately; otherwise every redraw leaks a GDI object.
    private static Icon CloneFromBitmap(Bitmap bitmap)
    {
        var handle = bitmap.GetHicon();
        try
        {
            using var unmanaged = Icon.FromHandle(handle);
            return (Icon)unmanaged.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public static void Release(Icon? icon) => icon?.Dispose();
}
