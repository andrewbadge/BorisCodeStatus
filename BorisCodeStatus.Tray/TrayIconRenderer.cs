using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using BorisCodeStatus.Core.Models;

namespace BorisCodeStatus.Tray;

/// <summary>
/// Draws the tray glyph at runtime rather than shipping .ico assets, so the icon can encode live
/// data: the 8-bit dog is the activity state and the surrounding arc is session quota used.
/// This is the tray-side analogue of the ESP32 display.
/// </summary>
internal static class TrayIconRenderer
{
    // Tray icons are requested at the small-icon size; 32px covers the common 150%/200% DPI scales.
    private const int Size = 32;

    /// <summary>Top-left of the 20px dog on the 32px canvas — centred, leaving room for the ring.</summary>
    private const int DogOrigin = (Size - DogSprites.Size) / 2;

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
    public static Icon Render(DogState dog, double? sessionUsedPercentage)
    {
        using var bitmap = new Bitmap(Size, Size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);

            // The ring first: it is antialiased and the dog is not, so drawing the sprite last
            // keeps its edges hard even where the two meet.
            DrawQuotaArc(graphics, sessionUsedPercentage);
        }

        DrawDog(bitmap, dog);

        return CloneFromBitmap(bitmap);
    }

    /// <summary>
    /// Blits the sprite a pixel at a time. SetPixel rather than a scaled DrawImage precisely
    /// because there is no interpolation to get wrong — pixel art survives only at 1:1.
    /// </summary>
    private static void DrawDog(Bitmap bitmap, DogState dog)
    {
        var sprite = DogSprites.For(dog);

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

                bitmap.SetPixel(DogOrigin + x, DogOrigin + y, DogSprites.Palette[index]);
            }
        }
    }

    /// <summary>An arc around the glyph showing how much of the five-hour window is spent.</summary>
    private static void DrawQuotaArc(Graphics graphics, double? sessionUsedPercentage)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        // Sits just outside the dog's corners. Widening the ring or growing the sprite from here
        // makes the two overlap, and the ring then clips the ears and the accent block.
        var ring = new Rectangle(1, 1, Size - 3, Size - 3);

        using (var trackPen = new Pen(TrackColor, 2.5f))
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

        using var pen = new Pen(used >= 80 ? QuotaHighColor : QuotaColor, 2.5f)
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
