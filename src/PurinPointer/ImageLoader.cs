using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PurinPointer.Core;

namespace PurinPointer;

/// <summary>Turns files the user picks or drops into a <see cref="CursorProject"/>, using WPF's
/// built-in image codecs (PNG, JPEG, BMP, GIF, TIFF, ICO and any WIC codec Windows has, e.g. WebP).</summary>
public static class ImageLoader
{
    static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".ico", ".cur"];
    static readonly string[] AllExtensions = [.. ImageExtensions, ".ani"];

    public static string DialogFilter(string label) =>
        $"{label}|{string.Join(";", AllExtensions.Select(e => "*" + e))}";

    public static bool IsSupported(string path) =>
        AllExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static CursorProject Load(IReadOnlyList<string> paths)
    {
        // A dropped folder means "these images, in file-name order".
        var files = paths.SelectMany(p => Directory.Exists(p)
                ? Directory.GetFiles(p).Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                : [p])
            .Where(IsSupported)
            .OrderBy(Path.GetFileName, NaturalStringComparer.Instance)
            .ToList();
        if (files.Count == 0) throw new InvalidDataException(Loc.Instance["import.none"]);

        string name = paths.Count == 1 ? Path.GetFileName(paths[0].TrimEnd('\\', '/')) : $"{files.Count} files";

        if (files.Count == 1)
        {
            var file = files[0];
            var bytes = File.ReadAllBytes(file);
            switch (Path.GetExtension(file).ToLowerInvariant())
            {
                case ".ani":
                    var ani = AniReader.Read(bytes, Decode);
                    return new CursorProject(ani.Frames, name, ani.HotspotX, ani.HotspotY);
                case ".cur":
                case ".ico":
                    var cur = IconReader.Read(bytes, Decode);
                    return new CursorProject([new CursorFrame(cur.Image, 100)], name, cur.HotspotX, cur.HotspotY);
                case ".gif":
                    return new CursorProject(LoadGif(bytes), name);
                default:
                    return new CursorProject([new CursorFrame(Decode(bytes) ?? throw Unreadable(file), 100)], name);
            }
        }

        // Several files: one frame each, 100 ms apiece; the speed slider adjusts them together.
        var frames = new List<CursorFrame>();
        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            var ext = Path.GetExtension(file).ToLowerInvariant();
            var image = ext is ".cur" or ".ico" ? IconReader.Read(bytes, Decode).Image : Decode(bytes) ?? throw Unreadable(file);
            frames.Add(new CursorFrame(image, 100));
        }
        return new CursorProject(frames, name);
    }

    /// <summary>Reads a cursor file for the small preview in the pointer list.</summary>
    public static Bitmap32? Thumbnail(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return Path.GetExtension(path).Equals(".ani", StringComparison.OrdinalIgnoreCase)
                ? AniReader.Read(bytes, Decode).Frames[0].Image
                : IconReader.Read(bytes, Decode).Image;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
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

        // Prefer the GIF's logical screen size when Windows exposes it.
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

    static InvalidDataException Unreadable(string file) => new($"{Path.GetFileName(file)}: unsupported image.");
}
