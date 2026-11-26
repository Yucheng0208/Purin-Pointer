using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PurinPointer.Core;

namespace PurinPointer;

/// <summary>Image decoding with WPF's built-in codecs (PNG, JPEG, BMP, GIF, TIFF, ICO and any
/// WIC codec Windows has installed, e.g. WebP or HEIC).</summary>
public static class ImageLoader
{
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic"];
    public static readonly string[] CursorExtensions = [".ani", ".cur", ".ico"];

    public static string DialogFilter(string label) =>
        $"{label}|{string.Join(";", CursorExtensions.Concat(ImageExtensions).Select(e => "*" + e))}";

    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    public static bool IsCursor(string path) => CursorExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Image files from the given files and folders, in Explorer order (1, 2, … 10).</summary>
    public static List<string> ImageFrames(IEnumerable<string> paths) =>
        paths.SelectMany(p => Directory.Exists(p) ? Directory.GetFiles(p).Where(IsImage) : IsImage(p) ? [p] : Array.Empty<string>())
             .OrderBy(Path.GetFileName, NaturalStringComparer.Instance)
             .ToList();

    /// <summary>Reads an .ani, .cur or .ico file.</summary>
    public static AnimatedCursor ReadCursorFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (Path.GetExtension(path).Equals(".ani", StringComparison.OrdinalIgnoreCase)) return AniReader.Read(bytes, Decode);
        var cur = IconReader.Read(bytes, Decode);
        return new AnimatedCursor([new CursorFrame(cur.Image, 100)], cur.HotspotX, cur.HotspotY);
    }

    /// <summary>Frames for an image import: an animated GIF keeps its own frames and timing;
    /// several stills become one frame each, <paramref name="intervalMs"/> apart.</summary>
    public static List<CursorFrame> ReadImageFrames(IReadOnlyList<string> files, int intervalMs)
    {
        if (files.Count == 1 && Path.GetExtension(files[0]).Equals(".gif", StringComparison.OrdinalIgnoreCase))
            return LoadGif(File.ReadAllBytes(files[0]));
        return files.Select(f => new CursorFrame(Decode(File.ReadAllBytes(f)) ?? throw new InvalidDataException(Path.GetFileName(f)), intervalMs)).ToList();
    }

    /// <summary>Decodes the first frame of any image WPF understands into straight-alpha BGRA.</summary>
    public static Bitmap32? Decode(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat,
                                               BitmapCacheOption.OnLoad);
            return ToBitmap32(decoder.Frames[0]);
        }
        catch (Exception e) when (e is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    static List<CursorFrame> LoadGif(byte[] data)
    {
        using var ms = new MemoryStream(data);
        var decoder = new GifBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        var raw = new List<GifFrame>();
        int width = 0, height = 0;
        foreach (var frame in decoder.Frames)
        {
            var meta = frame.Metadata as BitmapMetadata;
            int left = Query(meta, "/imgdesc/Left"), top = Query(meta, "/imgdesc/Top");
            var patch = ToBitmap32(frame);
            raw.Add(new GifFrame(patch, left, top, Query(meta, "/grctlext/Delay"), Query(meta, "/grctlext/Disposal")));
            width = Math.Max(width, left + patch.Width);
            height = Math.Max(height, top + patch.Height);
        }

        try
        {
            if (decoder.Metadata is BitmapMetadata m)
            {
                int w = Query(m, "/logscrdesc/Width"), h = Query(m, "/logscrdesc/Height");
                if (w > 0 && h > 0) { width = w; height = h; }
            }
        }
        catch (NotSupportedException) { }

        return GifComposer.Compose(raw, width, height);
    }

    static int Query(BitmapMetadata? meta, string query)
    {
        try { return meta?.GetQuery(query) is { } v ? Convert.ToInt32(v) : 0; }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException or ArgumentException or FormatException) { return 0; }
    }

    static Bitmap32 ToBitmap32(BitmapSource source)
    {
        var converted = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var bmp = new Bitmap32(converted.PixelWidth, converted.PixelHeight);
        converted.CopyPixels(bmp.Pixels, bmp.Stride, 0);
        return bmp;
    }

    public static BitmapSource ToBitmapSource(Bitmap32 bmp)
    {
        var src = BitmapSource.Create(bmp.Width, bmp.Height, 96, 96, PixelFormats.Bgra32, null, bmp.Pixels, bmp.Stride);
        src.Freeze();
        return src;
    }

    /// <summary>A small thumbnail straight from an image file (for the timeline).</summary>
    public static BitmapSource? Thumbnail(string path, int width)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            img.DecodePixelWidth = width;
            img.UriSource = new Uri(path);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception e) when (e is NotSupportedException or IOException or FileFormatException or ArgumentException) { return null; }
    }
}

/// <summary>A decoded cursor ready to show: frames in play order with their durations.</summary>
public sealed record PreviewFrames(IReadOnlyList<BitmapSource> Images, IReadOnlyList<int> Delays, AnimatedCursor Cursor)
{
    public int Width => Cursor.Frames[0].Image.Width;
    public int Height => Cursor.Frames[0].Image.Height;

    public static PreviewFrames From(AnimatedCursor c) =>
        new(c.Frames.Select(f => ImageLoader.ToBitmapSource(f.Image)).ToList(), c.Frames.Select(f => Math.Max(16, f.DelayMs)).ToList(), c);
}
