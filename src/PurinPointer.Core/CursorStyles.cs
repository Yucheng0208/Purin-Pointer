namespace PurinPointer.Core;

/// <summary>
/// How a pointer is drawn at its final size:
/// <list type="bullet">
/// <item><b>smooth</b> — high resolution, soft anti-aliased edges (the default).</item>
/// <item><b>pixel</b> — drawn on a coarse grid and enlarged without smoothing, so each block shows.</item>
/// <item><b>jagged</b> — smooth inside, but every pixel is either opaque or clear, giving hard, stepped edges.</item>
/// </list>
/// </summary>
public static class CursorStyles
{
    public const string Smooth = "smooth", Pixel = "pixel", Jagged = "jagged";
    public static readonly IReadOnlyList<string> All = [Smooth, Pixel, Jagged];

    public static string Of(Slot slot) => All.Contains(slot.Style) ? slot.Style! : Smooth;

    /// <summary>
    /// Draws <paramref name="src"/> at <paramref name="width"/> × <paramref name="height"/> in the given style.
    /// <paramref name="pixelBlock"/> is the side of one "pixel" in output pixels for the pixel style.
    /// </summary>
    public static Bitmap32 Render(Bitmap32 src, int width, int height, string style, int pixelBlock = 2)
    {
        switch (style)
        {
            case Pixel:
                int block = Math.Max(1, pixelBlock);
                int gw = Math.Max(1, (int)Math.Round((double)width / block)), gh = Math.Max(1, (int)Math.Round((double)height / block));
                return Nearest(HardEdges(Resampler.Resize(src, gw, gh)), width, height);
            case Jagged:
                return HardEdges(Resampler.Resize(src, width, height));
            default:
                return Resampler.Resize(src, width, height);
        }
    }

    /// <summary>Every pixel becomes fully opaque (alpha ≥ 50 %) or fully transparent.</summary>
    public static Bitmap32 HardEdges(Bitmap32 src)
    {
        var dst = src.Clone();
        for (int i = 0; i < dst.Pixels.Length; i += 4)
        {
            if (dst.Pixels[i + 3] >= 128) dst.Pixels[i + 3] = 255;
            else { dst.Pixels[i] = dst.Pixels[i + 1] = dst.Pixels[i + 2] = dst.Pixels[i + 3] = 0; }
        }
        return dst;
    }

    /// <summary>Nearest-neighbour resize: keeps blocks sharp.</summary>
    public static Bitmap32 Nearest(Bitmap32 src, int width, int height)
    {
        var dst = new Bitmap32(width, height);
        for (int y = 0; y < height; y++)
        {
            int sy = Math.Min(src.Height - 1, y * src.Height / height);
            for (int x = 0; x < width; x++)
            {
                int sx = Math.Min(src.Width - 1, x * src.Width / width);
                Buffer.BlockCopy(src.Pixels, (sy * src.Width + sx) * 4, dst.Pixels, (y * width + x) * 4, 4);
            }
        }
        return dst;
    }
}
