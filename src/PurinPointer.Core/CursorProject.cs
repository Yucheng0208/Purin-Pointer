namespace PurinPointer.Core;

/// <summary>A Windows pointer slot, named after its value under <c>HKCU\Control Panel\Cursors</c>.</summary>
public sealed record CursorRole(string RegistryName, string DefaultFile)
{
    /// <summary>Localisation key for the role's display name.</summary>
    public string NameKey => "role." + RegistryName;

    /// <summary>The value the "Windows Default" scheme stores (empty means the built-in cursor).</summary>
    public string DefaultValue => DefaultFile.Length == 0 ? "" : @"%SystemRoot%\cursors\" + DefaultFile;

    /// <summary>All 17 roles, in the order Windows stores them in a scheme string.</summary>
    public static readonly IReadOnlyList<CursorRole> All =
    [
        new("Arrow", "aero_arrow.cur"),
        new("Help", "aero_helpsel.cur"),
        new("AppStarting", "aero_working.ani"),
        new("Wait", "aero_busy.ani"),
        new("Crosshair", ""),
        new("IBeam", ""),
        new("NWPen", "aero_pen.cur"),
        new("No", "aero_unavail.cur"),
        new("SizeNS", "aero_ns.cur"),
        new("SizeWE", "aero_ew.cur"),
        new("SizeNWSE", "aero_nwse.cur"),
        new("SizeNESW", "aero_nesw.cur"),
        new("SizeAll", "aero_move.cur"),
        new("UpArrow", "aero_up.cur"),
        new("Hand", "aero_link.cur"),
        new("Pin", "aero_pin.cur"),
        new("Person", "aero_person.cur"),
    ];

    public static CursorRole Get(string registryName) => All.First(r => r.RegistryName == registryName);
}

/// <summary>
/// The editable state of one cursor: the imported frames, the background-removal result,
/// the output canvas size and the hotspot. The hotspot is kept in source-image pixels so it
/// stays on the same spot of the picture when the canvas size changes.
/// </summary>
public sealed class CursorProject
{
    public static readonly int[] CanvasSizes = [32, 48, 64, 96, 128, 256];
    public const int MaxWorkingSize = 512;

    public IReadOnlyList<CursorFrame> Original { get; }
    public List<CursorFrame> Frames { get; private set; }
    public string SourceName { get; }
    public int CanvasSize { get; set; } = 64;
    public double HotspotX { get; set; }
    public double HotspotY { get; set; }

    /// <summary>Playback speed; 2.0 plays twice as fast.</summary>
    public double Speed { get; set; } = 1.0;

    public int SourceWidth => Frames[0].Image.Width;
    public int SourceHeight => Frames[0].Image.Height;
    public bool IsAnimated => Frames.Count > 1;

    public CursorProject(IReadOnlyList<CursorFrame> frames, string sourceName, double hotspotX = 0, double hotspotY = 0)
    {
        if (frames.Count == 0) throw new ArgumentException("No frames.", nameof(frames));
        Frames = NormaliseSizes(frames);
        // Very large pictures are shrunk once on import so editing stays responsive;
        // the biggest Windows cursor is 256 px, so nothing visible is lost.
        int side = Math.Max(Frames[0].Image.Width, Frames[0].Image.Height);
        if (side > MaxWorkingSize)
        {
            double k = (double)MaxWorkingSize / side;
            int w = Math.Max(1, (int)Math.Round(Frames[0].Image.Width * k));
            int h = Math.Max(1, (int)Math.Round(Frames[0].Image.Height * k));
            Frames = Frames.Select(f => f with { Image = Resampler.Resize(f.Image, w, h) }).ToList();
            hotspotX *= k;
            hotspotY *= k;
        }
        Original = Frames.ToList();
        SourceName = sourceName;
        HotspotX = Math.Clamp(hotspotX, 0, SourceWidth - 1);
        HotspotY = Math.Clamp(hotspotY, 0, SourceHeight - 1);
        // Start at the source's own size when it is a standard cursor size, otherwise 64 px.
        int largest = Math.Max(SourceWidth, SourceHeight);
        CanvasSize = CanvasSizes.Contains(largest) ? largest : Math.Min(64, CanvasSizes.Last(s => s <= Math.Max(32, largest)));
    }

    public Placement Placement => Resampler.Place(SourceWidth, SourceHeight, CanvasSize);

    public (int X, int Y) CanvasHotspot
    {
        get
        {
            var (x, y) = Placement.SourceToCanvas(HotspotX, HotspotY);
            return (Math.Clamp((int)Math.Round(x), 0, CanvasSize - 1), Math.Clamp((int)Math.Round(y), 0, CanvasSize - 1));
        }
    }

    public void SetCanvasHotspot(double x, double y)
    {
        var (sx, sy) = Placement.CanvasToSource(x, y);
        HotspotX = Math.Clamp(sx, 0, SourceWidth - 1);
        HotspotY = Math.Clamp(sy, 0, SourceHeight - 1);
    }

    public void RemoveEdgeBackground(int tolerance) =>
        Frames = Frames.Select(f => f with { Image = BackgroundRemover.RemoveEdgeBackground(f.Image, tolerance) }).ToList();

    /// <summary>Magic wand at a canvas position on the given frame, replayed on every frame.</summary>
    public bool MagicWandAtCanvas(int frameIndex, double canvasX, double canvasY, int tolerance)
    {
        var (sx, sy) = Placement.CanvasToSource(canvasX, canvasY);
        int x = (int)Math.Floor(sx), y = (int)Math.Floor(sy);
        var seedImage = Frames[Math.Clamp(frameIndex, 0, Frames.Count - 1)].Image;
        if (x < 0 || y < 0 || x >= seedImage.Width || y >= seedImage.Height) return false;
        int o = (y * seedImage.Width + x) * 4;
        if (seedImage.Pixels[o + 3] == 0) return false;
        var color = (seedImage.Pixels[o], seedImage.Pixels[o + 1], seedImage.Pixels[o + 2]);
        Frames = Frames.Select(f => f with { Image = BackgroundRemover.MagicWand(f.Image, x, y, color, tolerance) }).ToList();
        return true;
    }

    public void RevertBackground() => Frames = Original.ToList();

    /// <summary>The frames exactly as they will be written: scaled onto the canvas, speed applied.</summary>
    public List<CursorFrame> Render() =>
        Frames.Select(f => new CursorFrame(Resampler.FitToCanvas(f.Image, CanvasSize),
                                           Math.Max(16, (int)Math.Round(f.DelayMs / Speed)))).ToList();

    /// <summary>Encodes to .cur for a still image or .ani for an animation.</summary>
    public (byte[] Data, string Extension) Encode()
    {
        var frames = Render();
        var (hx, hy) = CanvasHotspot;
        return frames.Count == 1
            ? (CurWriter.Write(frames[0].Image, hx, hy), ".cur")
            : (AniWriter.Write(frames, hx, hy), ".ani");
    }

    /// <summary>Frames of different sizes (e.g. a folder of mixed images) are padded to the largest.</summary>
    static List<CursorFrame> NormaliseSizes(IReadOnlyList<CursorFrame> frames)
    {
        int w = frames.Max(f => f.Image.Width), h = frames.Max(f => f.Image.Height);
        return frames.Select(f =>
        {
            if (f.Image.Width == w && f.Image.Height == h) return f;
            var padded = new Bitmap32(w, h);
            int ox = (w - f.Image.Width) / 2, oy = (h - f.Image.Height) / 2;
            for (int y = 0; y < f.Image.Height; y++)
                Buffer.BlockCopy(f.Image.Pixels, y * f.Image.Stride, padded.Pixels, ((y + oy) * w + ox) * 4, f.Image.Stride);
            return f with { Image = padded };
        }).ToList();
    }
}
