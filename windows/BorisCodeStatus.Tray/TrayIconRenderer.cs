using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using BorisCodeStatus.Core.Models;
using BorisCodeStatus.Core.State;

namespace BorisCodeStatus.Tray;

/// <summary>
/// Draws the tray glyph at runtime rather than shipping .ico assets, so the icon can encode live
/// data: the 8-bit dog is the activity state and the arc behind it is session quota used.
/// This is the tray-side analogue of the ESP32 display.
/// </summary>
internal static class TrayIconRenderer
{
    private static readonly Color TrackColor = Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF);
    // Opaque-ish so the colour reads against both light and dark taskbars.
    private static readonly Color QuotaLowColor = Color.FromArgb(0xF0, 0x5C, 0xC2, 0x6A);
    private static readonly Color QuotaMidColor = Color.FromArgb(0xF0, 0xF5, 0xB9, 0x42);
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
    public static Icon Render(DogState dog, double? sessionUsedPercentage, Pet pet = Pet.Dog, int frame = 0)
    {
        // Drawn at the exact size the tray shows, so Windows never resamples it: a 32px canvas
        // shrunk into a 16px slot halved the dog and blurred it. The process is per-monitor
        // DPI aware, so this is the primary display's real small-icon size.
        var size = SystemInformation.SmallIconSize.Width;

        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);

            // The ring first and the dog over it: the dog fills the icon, so the ring shows only
            // in the gaps — between the ears, beside the chin — as the other tray icons leave no
            // room for a ring around the outside. Antialiased ring, hard-edged dog.
            DrawQuotaArc(graphics, size, sessionUsedPercentage);
        }

        DrawDog(bitmap, dog, pet, frame);

        return CloneFromBitmap(bitmap);
    }

    /// <summary>
    /// Blits the sprite as solid blocks of whole pixels — never an interpolated scale, which is
    /// what destroys pixel art. The design's 32px and 64px icons are exactly the 16px one doubled
    /// and quadrupled, so an integer scale reproduces them.
    /// </summary>
    // ponytail: integer scales of the 16px sprite only, so at 125%/150% (20/24px slots) the dog
    // stays 16px with a margin. Transcribe the design's distinct 24px sprites if that matters.
    private static void DrawDog(Bitmap bitmap, DogState dog, Pet pet, int frame)
    {
        var (sprite, palette) = DogSprites.Tray(dog, pet, frame);
        var scale = Math.Max(1, bitmap.Width / DogSprites.Size);
        var origin = (bitmap.Width - DogSprites.Size * scale) / 2;

        for (var y = 0; y < DogSprites.Size; y++)
        {
            var row = sprite[y];
            for (var x = 0; x < DogSprites.Size; x++)
            {
                var index = DogSprites.IndexOf(row[x]);
                if (index == 0)
                {
                    continue;
                }

                var colour = palette[index];
                for (var dy = 0; dy < scale; dy++)
                {
                    for (var dx = 0; dx < scale; dx++)
                    {
                        bitmap.SetPixel(origin + x * scale + dx, origin + y * scale + dy, colour);
                    }
                }
            }
        }
    }

    /// <summary>An arc behind the glyph showing how much of the five-hour window is spent.</summary>
    private static void DrawQuotaArc(Graphics graphics, int size, double? sessionUsedPercentage)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        // An eighth of the icon: 2px in a 16px slot, 4px at 32px. Inset by half its width so the
        // stroke runs right to the canvas edge without being clipped by it.
        var width = size / 8f;
        var ring = new RectangleF(width / 2, width / 2, size - width, size - width);

        using (var trackPen = new Pen(TrackColor, width))
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

        using var pen = new Pen(QuotaColorFor(used), width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        // Start at twelve o'clock and fill clockwise, the way a gauge reads.
        graphics.DrawArc(pen, ring, -90f, sweep);
    }

    /// <summary>Green below 50%, amber from 50% to 75% inclusive, red above 75%.</summary>
    internal static Color QuotaColorFor(double used) => used switch
    {
        < 50 => QuotaLowColor,
        <= 75 => QuotaMidColor,
        _ => QuotaHighColor,
    };

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
