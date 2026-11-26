using System.Buffers.Binary;
using System.Text;

namespace PurinPointer.Core;

/// <summary>Decodes a PNG (or any other compressed payload) into a <see cref="Bitmap32"/>.
/// The core library has no image codecs of its own; the Windows app passes WPF's decoder.</summary>
public delegate Bitmap32? ImageDecoder(byte[] data);

public sealed record CursorImage(Bitmap32 Image, int HotspotX, int HotspotY);

public sealed record AnimatedCursor(IReadOnlyList<CursorFrame> Frames, int HotspotX, int HotspotY);

/// <summary>Writes Windows <c>.cur</c> files.</summary>
public static class CurWriter
{
    public const int MaxSize = 256;

    public static byte[] Write(Bitmap32 image, int hotspotX, int hotspotY)
    {
        if (image.Width > MaxSize || image.Height > MaxSize)
            throw new ArgumentException($"Cursor images are limited to {MaxSize}×{MaxSize} pixels.", nameof(image));

        byte[] dib = EncodeDib(image);
        using var ms = new MemoryStream(6 + 16 + dib.Length);
        using var w = new BinaryWriter(ms);
        // ICONDIR
        w.Write((ushort)0);   // reserved
        w.Write((ushort)2);   // type: cursor
        w.Write((ushort)1);   // image count
        // ICONDIRENTRY
        w.Write((byte)(image.Width == 256 ? 0 : image.Width));
        w.Write((byte)(image.Height == 256 ? 0 : image.Height));
        w.Write((byte)0);     // colour count
        w.Write((byte)0);     // reserved
        w.Write((ushort)Math.Clamp(hotspotX, 0, image.Width - 1));
        w.Write((ushort)Math.Clamp(hotspotY, 0, image.Height - 1));
        w.Write((uint)dib.Length);
        w.Write((uint)22);    // offset of the image data
        w.Write(dib);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>A 32-bit BGRA DIB with an AND mask, as stored inside icon and cursor files.</summary>
    static byte[] EncodeDib(Bitmap32 image)
    {
        int w = image.Width, h = image.Height;
        int xorSize = w * h * 4;
        int maskStride = (w + 31) / 32 * 4;
        int maskSize = maskStride * h;
        var data = new byte[40 + xorSize + maskSize];
        var span = data.AsSpan();

        BinaryPrimitives.WriteInt32LittleEndian(span[0..], 40);         // biSize
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], w);          // biWidth
        BinaryPrimitives.WriteInt32LittleEndian(span[8..], h * 2);      // biHeight covers XOR + AND
        BinaryPrimitives.WriteInt16LittleEndian(span[12..], 1);         // biPlanes
        BinaryPrimitives.WriteInt16LittleEndian(span[14..], 32);        // biBitCount
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 0);         // BI_RGB
        BinaryPrimitives.WriteInt32LittleEndian(span[20..], xorSize + maskSize);

        // DIB rows are bottom-up.
        for (int y = 0; y < h; y++)
        {
            int src = (h - 1 - y) * w * 4;
            Buffer.BlockCopy(image.Pixels, src, data, 40 + y * w * 4, w * 4);

            int maskRow = 40 + xorSize + y * maskStride;
            for (int x = 0; x < w; x++)
                if (image.Pixels[src + x * 4 + 3] == 0)
                    data[maskRow + x / 8] |= (byte)(0x80 >> (x % 8));
        }
        return data;
    }
}

/// <summary>Writes Windows animated cursor (<c>.ani</c>) files.</summary>
public static class AniWriter
{
    /// <summary>Converts milliseconds to the 1/60-second "jiffies" .ani files use.</summary>
    public static int ToJiffies(int ms) => Math.Max(1, (int)Math.Round(ms * 60 / 1000.0));

    public static byte[] Write(IReadOnlyList<CursorFrame> frames, int hotspotX, int hotspotY)
    {
        if (frames.Count == 0) throw new ArgumentException("An animated cursor needs at least one frame.", nameof(frames));
        var first = frames[0].Image;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Ascii("RIFF"));
        w.Write(0u); // patched below
        w.Write(Ascii("ACON"));

        // anih
        w.Write(Ascii("anih"));
        w.Write(36u);
        w.Write(36u);                         // cbSize
        w.Write((uint)frames.Count);          // nFrames
        w.Write((uint)frames.Count);          // nSteps
        w.Write((uint)first.Width);           // cx
        w.Write((uint)first.Height);          // cy
        w.Write(32u);                         // cBitCount
        w.Write(1u);                          // cPlanes
        w.Write((uint)ToJiffies(frames[0].DelayMs)); // default rate
        w.Write(1u);                          // AF_ICON: frames are stored as .cur data

        // rate: one entry per step so every frame can keep its own duration
        w.Write(Ascii("rate"));
        w.Write((uint)(frames.Count * 4));
        foreach (var f in frames) w.Write((uint)ToJiffies(f.DelayMs));

        // LIST fram
        var icons = frames.Select(f => CurWriter.Write(f.Image, hotspotX, hotspotY)).ToList();
        uint listSize = 4 + (uint)icons.Sum(i => 8 + i.Length + (i.Length & 1));
        w.Write(Ascii("LIST"));
        w.Write(listSize);
        w.Write(Ascii("fram"));
        foreach (var icon in icons)
        {
            w.Write(Ascii("icon"));
            w.Write((uint)icon.Length);
            w.Write(icon);
            if ((icon.Length & 1) == 1) w.Write((byte)0);
        }

        w.Flush();
        var bytes = ms.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
        return bytes;
    }

    static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);
}

/// <summary>Reads <c>.cur</c> and <c>.ico</c> files, taking the largest image they contain.</summary>
public static class IconReader
{
    static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static CursorImage Read(byte[] data, ImageDecoder? pngDecoder = null)
    {
        var span = data.AsSpan();
        if (span.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(span) != 0)
            throw new InvalidDataException("Not an icon or cursor file.");
        int type = BinaryPrimitives.ReadUInt16LittleEndian(span[2..]);
        int count = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]);
        if (type is not (1 or 2) || count == 0 || span.Length < 6 + count * 16)
            throw new InvalidDataException("Not an icon or cursor file.");

        int best = -1; long bestScore = -1;
        for (int i = 0; i < count; i++)
        {
            var e = span.Slice(6 + i * 16, 16);
            int w = e[0] == 0 ? 256 : e[0];
            int h = e[1] == 0 ? 256 : e[1];
            long score = (long)w * h * 64 + (type == 1 ? BinaryPrimitives.ReadUInt16LittleEndian(e[6..]) : 32);
            if (score > bestScore) { bestScore = score; best = i; }
        }

        var entry = span.Slice(6 + best * 16, 16);
        int entryW = entry[0] == 0 ? 256 : entry[0];
        int entryH = entry[1] == 0 ? 256 : entry[1];
        int hx = type == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]) : 0;
        int hy = type == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]) : 0;
        int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
        int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
        if (offset < 0 || size <= 0 || offset > span.Length) throw new InvalidDataException("Damaged icon entry.");
        size = Math.Min(size, span.Length - offset);
        var payload = span.Slice(offset, size);

        Bitmap32 image;
        if (payload.Length >= 8 && payload[..8].SequenceEqual(PngSignature))
        {
            image = pngDecoder?.Invoke(payload.ToArray())
                ?? throw new InvalidDataException("This cursor stores a PNG image, which needs an image decoder.");
        }
        else
        {
            image = DecodeDib(payload);
        }

        // Hotspots are in entry coordinates; rescale if the stored image differs.
        if (image.Width != entryW && entryW > 0) hx = hx * image.Width / entryW;
        if (image.Height != entryH && entryH > 0) hy = hy * image.Height / entryH;
        return new CursorImage(image, Math.Clamp(hx, 0, image.Width - 1), Math.Clamp(hy, 0, image.Height - 1));
    }

    /// <summary>Decodes an icon-style DIB (height doubled, followed by an AND mask).</summary>
    public static Bitmap32 DecodeDib(ReadOnlySpan<byte> dib)
    {
        if (dib.Length < 40) throw new InvalidDataException("Damaged bitmap.");
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib);
        int width = BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
        int height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(dib[8..])) / 2;
        int bpp = BinaryPrimitives.ReadInt16LittleEndian(dib[14..]);
        int compression = BinaryPrimitives.ReadInt32LittleEndian(dib[16..]);
        int colorsUsed = BinaryPrimitives.ReadInt32LittleEndian(dib[32..]);
        if (width <= 0 || height <= 0 || width > 1024 || height > 1024) throw new InvalidDataException("Unsupported bitmap size.");
        if (compression is not (0 or 3)) throw new InvalidDataException("Compressed bitmaps are not supported.");
        if (bpp is not (1 or 4 or 8 or 24 or 32)) throw new InvalidDataException($"Unsupported bit depth: {bpp}.");

        int paletteCount = bpp <= 8 ? (colorsUsed > 0 ? colorsUsed : 1 << bpp) : 0;
        int pos = headerSize + (compression == 3 && headerSize == 40 ? 12 : 0);
        var palette = dib.Slice(pos, paletteCount * 4);
        pos += paletteCount * 4;

        int xorStride = (width * bpp + 31) / 32 * 4;
        int maskStride = (width + 31) / 32 * 4;
        var xor = dib.Slice(pos, Math.Min(xorStride * height, dib.Length - pos));
        pos += xorStride * height;
        var mask = pos < dib.Length ? dib[pos..] : ReadOnlySpan<byte>.Empty;

        var image = new Bitmap32(width, height);
        var px = image.Pixels;
        bool anyAlpha = false;
        for (int y = 0; y < height; y++)
        {
            int row = (height - 1 - y) * xorStride; // bottom-up
            if (row + xorStride > xor.Length) break;
            for (int x = 0; x < width; x++)
            {
                int o = (y * width + x) * 4;
                switch (bpp)
                {
                    case 32:
                        px[o] = xor[row + x * 4]; px[o + 1] = xor[row + x * 4 + 1];
                        px[o + 2] = xor[row + x * 4 + 2]; px[o + 3] = xor[row + x * 4 + 3];
                        if (px[o + 3] != 0) anyAlpha = true;
                        break;
                    case 24:
                        px[o] = xor[row + x * 3]; px[o + 1] = xor[row + x * 3 + 1]; px[o + 2] = xor[row + x * 3 + 2];
                        break;
                    default:
                        int bitsPerRow = x * bpp;
                        int b = xor[row + bitsPerRow / 8];
                        int shift = 8 - bpp - bitsPerRow % 8;
                        int index = (b >> shift) & ((1 << bpp) - 1);
                        if (index * 4 + 2 < palette.Length)
                        {
                            px[o] = palette[index * 4]; px[o + 1] = palette[index * 4 + 1]; px[o + 2] = palette[index * 4 + 2];
                        }
                        break;
                }
            }
        }

        // Without real alpha, transparency comes from the AND mask.
        if (!anyAlpha)
        {
            for (int y = 0; y < height; y++)
            {
                int row = (height - 1 - y) * maskStride;
                for (int x = 0; x < width; x++)
                {
                    bool transparent = row + x / 8 < mask.Length && (mask[row + x / 8] & (0x80 >> (x % 8))) != 0;
                    px[(y * width + x) * 4 + 3] = transparent ? (byte)0 : (byte)255;
                }
            }
        }
        return image;
    }
}

/// <summary>Reads Windows animated cursor (<c>.ani</c>) files into a flat list of frames in play order.</summary>
public static class AniReader
{
    public static AnimatedCursor Read(byte[] data, ImageDecoder? pngDecoder = null)
    {
        var span = data.AsSpan();
        if (span.Length < 12 || Tag(span, 0) != "RIFF" || Tag(span, 8) != "ACON")
            throw new InvalidDataException("Not an animated cursor (.ani) file.");

        int end = (int)Math.Min(span.Length, 8L + BinaryPrimitives.ReadUInt32LittleEndian(span[4..]));
        int defaultRate = 6, flags = 1, stepCount = 0;
        uint[]? rates = null, sequence = null;
        var raw = new List<byte[]>();

        void Walk(int start, int stop)
        {
            var span = data.AsSpan();
            int p = start;
            while (p + 8 <= stop)
            {
                string id = Tag(span, p);
                int size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(span[(p + 4)..]), (uint)(stop - p - 8));
                var body = span.Slice(p + 8, size);
                switch (id)
                {
                    case "anih" when size >= 36:
                        stepCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(body[8..]);
                        defaultRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(body[28..]);
                        flags = (int)BinaryPrimitives.ReadUInt32LittleEndian(body[32..]);
                        break;
                    case "rate": rates = ReadUInts(body); break;
                    case "seq ": sequence = ReadUInts(body); break;
                    case "LIST" when size >= 4 && Tag(body, 0) == "fram": Walk(p + 12, p + 8 + size); break;
                    case "icon": raw.Add(body.ToArray()); break;
                }
                p += 8 + size + (size & 1);
            }
        }
        Walk(12, end);

        if (raw.Count == 0) throw new InvalidDataException("The .ani file contains no frames.");

        var images = new List<CursorImage>();
        foreach (var r in raw)
        {
            if ((flags & 1) != 0) images.Add(IconReader.Read(r, pngDecoder));
            else images.Add(new CursorImage(IconReader.DecodeDib(r), 0, 0));
        }

        var order = sequence is { Length: > 0 }
            ? sequence.Select(i => (int)Math.Min(i, (uint)images.Count - 1)).ToArray()
            : Enumerable.Range(0, images.Count).ToArray();
        if (sequence is null && stepCount > 0 && stepCount < order.Length) order = order[..stepCount];

        var frames = new List<CursorFrame>(order.Length);
        for (int step = 0; step < order.Length; step++)
        {
            int jiffies = rates is not null && step < rates.Length ? (int)rates[step] : defaultRate;
            if (jiffies <= 0) jiffies = 6;
            frames.Add(new CursorFrame(images[order[step]].Image, (int)Math.Round(jiffies * 1000 / 60.0)));
        }
        var hot = images[order[0]];
        return new AnimatedCursor(frames, hot.HotspotX, hot.HotspotY);
    }

    static string Tag(ReadOnlySpan<byte> s, int at) => at + 4 <= s.Length ? Encoding.ASCII.GetString(s.Slice(at, 4)) : "";

    static uint[] ReadUInts(ReadOnlySpan<byte> s)
    {
        var a = new uint[s.Length / 4];
        for (int i = 0; i < a.Length; i++) a[i] = BinaryPrimitives.ReadUInt32LittleEndian(s[(i * 4)..]);
        return a;
    }
}
