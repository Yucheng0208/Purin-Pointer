namespace PurinPointer.Core;

/// <summary>Where a source image lands inside the square cursor canvas.</summary>
public readonly record struct Placement(double Scale, int OffsetX, int OffsetY, int Width, int Height)
{
    public (double X, double Y) CanvasToSource(double x, double y) => ((x - OffsetX) / Scale, (y - OffsetY) / Scale);
    public (double X, double Y) SourceToCanvas(double x, double y) => (x * Scale + OffsetX, y * Scale + OffsetY);
}

public static class Resampler
{
    /// <summary>How an image of the given size fits, centred, into a square canvas.</summary>
    public static Placement Place(int width, int height, int canvas)
    {
        double scale = (double)canvas / Math.Max(width, height);
        int w = Math.Clamp((int)Math.Round(width * scale), 1, canvas);
        int h = Math.Clamp((int)Math.Round(height * scale), 1, canvas);
        return new Placement(scale, (canvas - w) / 2, (canvas - h) / 2, w, h);
    }

    /// <summary>Scales the image to fit a transparent square canvas, keeping its aspect ratio.</summary>
    public static Bitmap32 FitToCanvas(Bitmap32 src, int canvas)
    {
        var p = Place(src.Width, src.Height, canvas);
        var scaled = Resize(src, p.Width, p.Height);
        var dst = new Bitmap32(canvas, canvas);
        for (int y = 0; y < p.Height; y++)
            Buffer.BlockCopy(scaled.Pixels, y * scaled.Stride, dst.Pixels, ((y + p.OffsetY) * canvas + p.OffsetX) * 4, scaled.Stride);
        return dst;
    }

    /// <summary>Area-averaging resize in premultiplied alpha. Downscaling averages every covered
    /// source pixel; upscaling keeps hard pixel edges, which suits pixel-art cursors.</summary>
    public static Bitmap32 Resize(Bitmap32 src, int width, int height)
    {
        if (width == src.Width && height == src.Height) return src.Clone();

        int sw = src.Width, sh = src.Height;
        var pre = new float[sw * sh * 4];
        for (int i = 0; i < sw * sh; i++)
        {
            float a = src.Pixels[i * 4 + 3] / 255f;
            pre[i * 4] = src.Pixels[i * 4] * a;
            pre[i * 4 + 1] = src.Pixels[i * 4 + 1] * a;
            pre[i * 4 + 2] = src.Pixels[i * 4 + 2] * a;
            pre[i * 4 + 3] = a;
        }

        var xw = Weights(sw, width);
        var yw = Weights(sh, height);

        var tmp = new float[width * sh * 4];
        for (int y = 0; y < sh; y++)
            for (int x = 0; x < width; x++)
                foreach (var (j, wgt) in xw[x])
                    for (int c = 0; c < 4; c++)
                        tmp[(y * width + x) * 4 + c] += pre[(y * sw + j) * 4 + c] * wgt;

        var dst = new Bitmap32(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float b = 0, g = 0, r = 0, a = 0;
                foreach (var (j, wgt) in yw[y])
                {
                    int o = (j * width + x) * 4;
                    b += tmp[o] * wgt; g += tmp[o + 1] * wgt; r += tmp[o + 2] * wgt; a += tmp[o + 3] * wgt;
                }
                int d = (y * width + x) * 4;
                if (a > 0.0001f)
                {
                    dst.Pixels[d] = ToByte(b / a);
                    dst.Pixels[d + 1] = ToByte(g / a);
                    dst.Pixels[d + 2] = ToByte(r / a);
                }
                dst.Pixels[d + 3] = ToByte(a * 255f);
            }
        return dst;
    }

    static byte ToByte(float v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

    /// <summary>For each destination index, the source indices it covers and their normalised weights.</summary>
    static List<(int, float)>[] Weights(int srcLen, int dstLen)
    {
        var result = new List<(int, float)>[dstLen];
        double ratio = (double)srcLen / dstLen;
        for (int i = 0; i < dstLen; i++)
        {
            double start = i * ratio, end = (i + 1) * ratio;
            var list = new List<(int, float)>();
            for (int j = (int)Math.Floor(start); j < Math.Min(srcLen, (int)Math.Ceiling(end)); j++)
            {
                double cover = Math.Min(end, j + 1) - Math.Max(start, j);
                if (cover > 1e-9) list.Add((j, (float)(cover / ratio)));
            }
            result[i] = list;
        }
        return result;
    }
}

/// <summary>A raw GIF frame as stored in the file: a sub-rectangle plus how to dispose of it.</summary>
public sealed record GifFrame(Bitmap32 Patch, int Left, int Top, int DelayCentiseconds, int Disposal);

public static class GifComposer
{
    /// <summary>Renders partial GIF frames onto a full canvas, honouring disposal methods.</summary>
    public static List<CursorFrame> Compose(IReadOnlyList<GifFrame> frames, int width, int height)
    {
        var result = new List<CursorFrame>(frames.Count);
        var canvas = new Bitmap32(width, height);
        foreach (var f in frames)
        {
            byte[]? previous = f.Disposal == 3 ? (byte[])canvas.Pixels.Clone() : null;

            for (int y = 0; y < f.Patch.Height; y++)
            {
                int cy = y + f.Top;
                if (cy < 0 || cy >= height) continue;
                for (int x = 0; x < f.Patch.Width; x++)
                {
                    int cx = x + f.Left;
                    if (cx < 0 || cx >= width) continue;
                    int s = (y * f.Patch.Width + x) * 4;
                    if (f.Patch.Pixels[s + 3] == 0) continue; // transparent GIF pixels leave the canvas alone
                    Buffer.BlockCopy(f.Patch.Pixels, s, canvas.Pixels, (cy * width + cx) * 4, 4);
                }
            }

            // Browsers treat 0–1 cs delays as 100 ms; do the same so "as fast as possible" GIFs look right.
            int delayMs = f.DelayCentiseconds <= 1 ? 100 : f.DelayCentiseconds * 10;
            result.Add(new CursorFrame(canvas.Clone(), delayMs));

            if (f.Disposal == 2)
            {
                for (int y = Math.Max(0, f.Top); y < Math.Min(height, f.Top + f.Patch.Height); y++)
                    Array.Clear(canvas.Pixels, (y * width + Math.Max(0, f.Left)) * 4,
                        (Math.Min(width, f.Left + f.Patch.Width) - Math.Max(0, f.Left)) * 4);
            }
            else if (previous is not null)
            {
                Buffer.BlockCopy(previous, 0, canvas.Pixels, 0, previous.Length);
            }
        }
        return result;
    }
}

public static class BackgroundRemover
{
    /// <summary>Clears the 4-connected region around (x, y) whose colour is within
    /// <paramref name="tolerance"/> (0–255 per channel) of the seed colour.</summary>
    public static Bitmap32 MagicWand(Bitmap32 src, int x, int y, int tolerance)
    {
        var dst = src.Clone();
        if (x < 0 || y < 0 || x >= src.Width || y >= src.Height) return dst;
        int seed = (y * src.Width + x) * 4;
        if (src.Pixels[seed + 3] == 0) return dst;
        Flood(dst, [(x, y)], src.Pixels[seed], src.Pixels[seed + 1], src.Pixels[seed + 2], tolerance);
        return dst;
    }

    /// <summary>Same as <see cref="MagicWand"/>, but the seed colour is given, so one click can be
    /// replayed across every frame of an animation.</summary>
    public static Bitmap32 MagicWand(Bitmap32 src, int x, int y, (byte B, byte G, byte R) color, int tolerance)
    {
        var dst = src.Clone();
        if (x < 0 || y < 0 || x >= src.Width || y >= src.Height) return dst;
        Flood(dst, [(x, y)], color.B, color.G, color.R, tolerance);
        return dst;
    }

    /// <summary>Removes a flat background touching the image border, using the most common border colour.</summary>
    public static Bitmap32 RemoveEdgeBackground(Bitmap32 src, int tolerance)
    {
        var dst = src.Clone();
        var border = new List<(int X, int Y)>();
        for (int x = 0; x < src.Width; x++) { border.Add((x, 0)); border.Add((x, src.Height - 1)); }
        for (int y = 1; y < src.Height - 1; y++) { border.Add((0, y)); border.Add((src.Width - 1, y)); }

        var counts = new Dictionary<int, int>();
        foreach (var (bx, by) in border)
        {
            int o = (by * src.Width + bx) * 4;
            if (src.Pixels[o + 3] == 0) continue;
            int key = (src.Pixels[o] >> 3) | (src.Pixels[o + 1] >> 3) << 5 | (src.Pixels[o + 2] >> 3) << 10;
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        if (counts.Count == 0) return dst; // border already transparent

        int top = counts.MaxBy(kv => kv.Value).Key;
        byte b = (byte)((top & 31) << 3 | 4), g = (byte)((top >> 5 & 31) << 3 | 4), r = (byte)((top >> 10 & 31) << 3 | 4);
        Flood(dst, border, b, g, r, Math.Max(tolerance, 6));
        return dst;
    }

    static void Flood(Bitmap32 img, IEnumerable<(int X, int Y)> seeds, byte b, byte g, byte r, int tolerance)
    {
        int w = img.Width, h = img.Height;
        var px = img.Pixels;
        var seen = new bool[w * h];
        var queue = new Queue<(int, int)>();

        bool Matches(int i)
        {
            int o = i * 4;
            return px[o + 3] != 0
                && Math.Abs(px[o] - b) <= tolerance
                && Math.Abs(px[o + 1] - g) <= tolerance
                && Math.Abs(px[o + 2] - r) <= tolerance;
        }

        foreach (var (sx, sy) in seeds)
        {
            int i = sy * w + sx;
            if (!seen[i] && Matches(i)) { seen[i] = true; queue.Enqueue((sx, sy)); }
        }

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            px[(y * w + x) * 4 + 3] = 0;
            Visit(x + 1, y); Visit(x - 1, y); Visit(x, y + 1); Visit(x, y - 1);
        }

        void Visit(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int i = y * w + x;
            if (seen[i] || !Matches(i)) return;
            seen[i] = true;
            queue.Enqueue((x, y));
        }
    }
}

/// <summary>Orders file names the way Explorer does: "2.png" before "10.png".</summary>
public sealed class NaturalStringComparer : IComparer<string?>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? a, string? b)
    {
        if (a is null || b is null) return string.Compare(a, b, StringComparison.Ordinal);
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a[si..i].TrimStart('0'); var nb = b[sj..j].TrimStart('0');
                int c = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : string.CompareOrdinal(na, nb);
                if (c != 0) return c;
            }
            else
            {
                int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (c != 0) return c;
                i++; j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}
