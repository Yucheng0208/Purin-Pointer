using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using PurinPointer.Core;

var tests = new List<(string Name, Action Run)>
{
    ("cur round-trip keeps pixels and hotspot", () =>
    {
        var img = Pattern(48, 48);
        var data = CurWriter.Write(img, 5, 7);
        Eq(2, (int)BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2)), "type");
        var back = IconReader.Read(data);
        Eq(5, back.HotspotX, "hx"); Eq(7, back.HotspotY, "hy");
        True(back.Image.Pixels.SequenceEqual(img.Pixels), "pixels");
    }),
    ("cur encodes 256 px as 0 in the directory", () =>
    {
        var data = CurWriter.Write(new Bitmap32(256, 256), 0, 0);
        Eq(0, (int)data[6], "width byte");
        Eq(256, IconReader.Read(data).Image.Width, "decoded width");
    }),
    ("cur AND mask marks transparent pixels", () =>
    {
        var img = new Bitmap32(32, 32);
        img.Pixels[3] = 255; // top-left opaque, everything else transparent
        var data = CurWriter.Write(img, 0, 0);
        int maskStart = 22 + 40 + 32 * 32 * 4;
        // the top image row is the last mask row (bottom-up); its first bit must be 0 (opaque)
        int topRow = maskStart + 31 * 4;
        Eq(0x7F, (int)data[topRow], "first mask byte of the top row");
    }),
    ("ani round-trip keeps frames, order, timing and hotspot", () =>
    {
        var frames = Enumerable.Range(0, 5).Select(i => new CursorFrame(Pattern(32, 32, i), 50 + i * 50)).ToList();
        var data = AniWriter.Write(frames, 3, 4);
        Eq("RIFF", Encoding.ASCII.GetString(data, 0, 4), "riff");
        Eq(data.Length - 8, (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)), "riff size");
        var back = AniReader.Read(data);
        Eq(5, back.Frames.Count, "frames");
        Eq(3, back.HotspotX, "hx"); Eq(4, back.HotspotY, "hy");
        for (int i = 0; i < 5; i++)
        {
            True(back.Frames[i].Image.Pixels.SequenceEqual(frames[i].Image.Pixels), $"frame {i} pixels");
            Eq((int)Math.Round(AniWriter.ToJiffies(frames[i].DelayMs) * 1000 / 60.0), back.Frames[i].DelayMs, $"frame {i} delay");
        }
    }),
    ("ani reader follows a seq chunk", () =>
    {
        var a = Pattern(32, 32, 1); var b = Pattern(32, 32, 2);
        var data = BuildAniWithSequence([CurWriter.Write(a, 0, 0), CurWriter.Write(b, 0, 0)], [1, 0, 1]);
        var back = AniReader.Read(data);
        Eq(3, back.Frames.Count, "steps");
        True(back.Frames[0].Image.Pixels.SequenceEqual(b.Pixels), "step 0 is frame 1");
        True(back.Frames[1].Image.Pixels.SequenceEqual(a.Pixels), "step 1 is frame 0");
    }),
    ("dib reader handles 1-bit cursors with an AND mask", () =>
    {
        var dib = Build1BitDib();
        var img = IconReader.DecodeDib(dib);
        Eq(32, img.Width, "w");
        Eq(255, (int)img.Alpha(0, 0), "opaque where mask is 0");
        Eq(0, (int)img.Alpha(31, 31), "transparent where mask is 1");
    }),
    ("png payload is handed to the decoder", () =>
    {
        var cur = CurWriter.Write(new Bitmap32(32, 32), 1, 1);
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        var data = new byte[22 + png.Length];
        Array.Copy(cur, data, 22);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(14), (uint)png.Length);
        Array.Copy(png, 0, data, 22, png.Length);
        bool called = false;
        var img = IconReader.Read(data, _ => { called = true; return new Bitmap32(32, 32); });
        True(called, "decoder called");
        Throws<InvalidDataException>(() => IconReader.Read(data));
    }),
    ("resize keeps a solid colour solid and transparent edges clean", () =>
    {
        var img = new Bitmap32(100, 50);
        for (int i = 0; i < img.Pixels.Length; i += 4) { img.Pixels[i] = 10; img.Pixels[i + 1] = 200; img.Pixels[i + 2] = 30; img.Pixels[i + 3] = 255; }
        var fit = Resampler.FitToCanvas(img, 32);
        Eq(32, fit.Width, "canvas");
        var p = Resampler.Place(100, 50, 32);
        Eq(32, p.Width, "placed w"); Eq(16, p.Height, "placed h"); Eq(8, p.OffsetY, "offset");
        Eq(0, (int)fit.Alpha(0, 0), "letterbox transparent");
        int mid = (16 * 32 + 16) * 4;
        Eq(200, (int)fit.Pixels[mid + 1], "green kept"); Eq(255, (int)fit.Pixels[mid + 3], "opaque");
    }),
    ("resize does not darken semi-transparent edges", () =>
    {
        var img = new Bitmap32(2, 1);
        img.Pixels[0] = 255; img.Pixels[1] = 255; img.Pixels[2] = 255; img.Pixels[3] = 255; // white
        // second pixel is transparent black
        var small = Resampler.Resize(img, 1, 1);
        Eq(255, (int)small.Pixels[2], "red stays white, not grey");
        Eq(128, (int)small.Pixels[3], "half alpha");
    }),
    ("gif composer honours disposal", () =>
    {
        var patch = new Bitmap32(2, 2);
        for (int i = 0; i < patch.Pixels.Length; i += 4) patch.Pixels[i + 3] = 255;
        var frames = GifComposer.Compose([new GifFrame(patch, 0, 0, 5, 1), new GifFrame(patch, 2, 2, 0, 2), new GifFrame(patch, 0, 2, 7, 0)], 4, 4);
        Eq(3, frames.Count, "frames");
        Eq(255, (int)frames[1].Image.Alpha(0, 0), "frame 1 keeps frame 0 (disposal 1)");
        Eq(255, (int)frames[1].Image.Alpha(3, 3), "frame 1 drawn");
        Eq(0, (int)frames[2].Image.Alpha(3, 3), "frame 2: previous patch cleared (disposal 2)");
        Eq(50, frames[0].DelayMs, "5 cs = 50 ms"); Eq(100, frames[1].DelayMs, "0 cs treated as 100 ms");
    }),
    ("magic wand clears only the connected region", () =>
    {
        var img = new Bitmap32(5, 5);
        for (int i = 0; i < img.Pixels.Length; i += 4) { img.Pixels[i] = 255; img.Pixels[i + 1] = 255; img.Pixels[i + 2] = 255; img.Pixels[i + 3] = 255; }
        for (int y = 0; y < 5; y++) { int o = (y * 5 + 2) * 4; img.Pixels[o] = img.Pixels[o + 1] = img.Pixels[o + 2] = 0; } // black wall
        var r = BackgroundRemover.MagicWand(img, 0, 0, 10);
        Eq(0, (int)r.Alpha(1, 4), "left side cleared");
        Eq(255, (int)r.Alpha(2, 2), "wall kept");
        Eq(255, (int)r.Alpha(4, 0), "right side kept");
    }),
    ("edge background removal keeps the subject", () =>
    {
        var img = new Bitmap32(9, 9);
        for (int i = 0; i < img.Pixels.Length; i += 4) { img.Pixels[i] = 0; img.Pixels[i + 1] = 255; img.Pixels[i + 2] = 0; img.Pixels[i + 3] = 255; }
        int c = (4 * 9 + 4) * 4; img.Pixels[c] = 0; img.Pixels[c + 1] = 0; img.Pixels[c + 2] = 255;
        var r = BackgroundRemover.RemoveEdgeBackground(img, 10);
        Eq(0, (int)r.Alpha(0, 0), "corner cleared"); Eq(0, (int)r.Alpha(4, 3), "next to subject cleared");
        Eq(255, (int)r.Alpha(4, 4), "subject kept");
    }),
    ("project hotspot follows the picture when the size changes", () =>
    {
        var p = new CursorProject([new CursorFrame(Pattern(64, 64), 100)], "x", 32, 16);
        p.CanvasSize = 32;
        Eq((16, 8), p.CanvasHotspot, "scaled hotspot");
        p.SetCanvasHotspot(4, 4);
        p.CanvasSize = 128;
        Eq((16, 16), p.CanvasHotspot, "re-scaled hotspot");
        var (data, ext) = p.Encode();
        Eq(".cur", ext, "still image becomes .cur");
        Eq(16, IconReader.Read(data).HotspotX, "hotspot written");
    }),
    ("project encodes animations as .ani with speed applied", () =>
    {
        var p = new CursorProject([new CursorFrame(Pattern(32, 32, 1), 200), new CursorFrame(Pattern(32, 32, 2), 200)], "x") { Speed = 2 };
        var (data, ext) = p.Encode();
        Eq(".ani", ext, "ext");
        var back = AniReader.Read(data);
        Eq(2, back.Frames.Count, "frames"); Eq(100, back.Frames[0].DelayMs, "2x speed halves delay");
    }),
    ("large imports are shrunk with the hotspot", () =>
    {
        var p = new CursorProject([new CursorFrame(new Bitmap32(1024, 512), 100)], "big", 1000, 500);
        Eq(512, p.SourceWidth, "w"); Eq(256, p.SourceHeight, "h");
        Eq(500.0, p.HotspotX, "hx");
    }),
    ("mixed frame sizes are padded to the largest", () =>
    {
        var p = new CursorProject([new CursorFrame(new Bitmap32(10, 10), 100), new CursorFrame(new Bitmap32(20, 16), 100)], "x");
        True(p.Frames.All(f => f.Image.Width == 20 && f.Image.Height == 16), "same size");
    }),
    ("natural sort puts 2 before 10", () =>
    {
        var names = new[] { "10.png", "2.png", "1.png", "frame-002.png", "Frame-10.png" }.OrderBy(n => n, NaturalStringComparer.Instance).ToArray();
        Eq("1.png,2.png,10.png,frame-002.png,Frame-10.png", string.Join(",", names), "order");
    }),
    ("every language has every key", () =>
    {
        var en = Loc.Load("en");
        foreach (var lang in Loc.Languages)
        {
            var t = Loc.Load(lang.Code);
            var missing = en.Keys.Except(t.Keys).ToList();
            var extra = t.Keys.Except(en.Keys).ToList();
            True(missing.Count == 0 && extra.Count == 0, $"{lang.Code}: missing [{string.Join(", ", missing)}] extra [{string.Join(", ", extra)}]");
            foreach (var (k, v) in en)
                Eq(Placeholders(v), Placeholders(t[k]), $"{lang.Code}:{k} placeholders");
        }
        foreach (var role in CursorRole.All) True(en.ContainsKey(role.NameKey), $"name for {role.RegistryName}");
    }),
    ("system language detection", () =>
    {
        Eq("zh-Hant", Loc.Detect(new CultureInfo("zh-TW")), "zh-TW");
        Eq("zh-Hant", Loc.Detect(new CultureInfo("zh-HK")), "zh-HK");
        Eq("zh-Hans", Loc.Detect(new CultureInfo("zh-CN")), "zh-CN");
        Eq("ja", Loc.Detect(new CultureInfo("ja-JP")), "ja");
        Eq("ko", Loc.Detect(new CultureInfo("ko-KR")), "ko");
        Eq("en", Loc.Detect(new CultureInfo("de-DE")), "fallback");
    }),
    ("roles match the Windows scheme order", () =>
    {
        Eq("Arrow,Help,AppStarting,Wait,Crosshair,IBeam,NWPen,No,SizeNS,SizeWE,SizeNWSE,SizeNESW,SizeAll,UpArrow,Hand,Pin,Person",
           string.Join(",", CursorRole.All.Select(r => r.RegistryName)), "order");
        Eq(@"%SystemRoot%\cursors\aero_arrow.cur", CursorRole.Get("Arrow").DefaultValue, "arrow default");
        Eq("", CursorRole.Get("IBeam").DefaultValue, "ibeam default is built in");
    }),
};

// Optional: write sample files for checking with other tools (e.g. Pillow).
if (args.Length == 2 && args[0] == "--write-samples")
{
    Directory.CreateDirectory(args[1]);
    File.WriteAllBytes(Path.Combine(args[1], "sample.cur"), CurWriter.Write(Pattern(64, 64), 10, 20));
    File.WriteAllBytes(Path.Combine(args[1], "sample.ani"),
        AniWriter.Write(Enumerable.Range(0, 4).Select(i => new CursorFrame(Pattern(48, 48, i), 120)).ToList(), 1, 2));
}

int failed = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"  ok    {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"  FAIL  {name}\n        {e.Message}"); }
}
Console.WriteLine(failed == 0 ? $"All {tests.Count} tests passed." : $"{failed} of {tests.Count} tests failed.");
return failed == 0 ? 0 : 1;

static Bitmap32 Pattern(int w, int h, int seed = 0)
{
    var b = new Bitmap32(w, h);
    for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int o = (y * w + x) * 4;
            b.Pixels[o] = (byte)(x * 5 + seed * 40); b.Pixels[o + 1] = (byte)(y * 5); b.Pixels[o + 2] = (byte)(x ^ y);
            b.Pixels[o + 3] = (byte)((x + y + seed) % 3 == 0 ? 0 : 255);
        }
    return b;
}

static byte[] BuildAniWithSequence(byte[][] icons, uint[] seq)
{
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    void Tag(string s) => w.Write(Encoding.ASCII.GetBytes(s));
    Tag("RIFF"); w.Write(0u); Tag("ACON");
    Tag("anih"); w.Write(36u); w.Write(36u); w.Write((uint)icons.Length); w.Write((uint)seq.Length);
    w.Write(0u); w.Write(0u); w.Write(0u); w.Write(0u); w.Write(6u); w.Write(3u);
    Tag("seq "); w.Write((uint)seq.Length * 4); foreach (var s in seq) w.Write(s);
    Tag("LIST"); w.Write((uint)(4 + icons.Sum(i => 8 + i.Length + (i.Length & 1)))); Tag("fram");
    foreach (var i in icons) { Tag("icon"); w.Write((uint)i.Length); w.Write(i); if ((i.Length & 1) == 1) w.Write((byte)0); }
    w.Flush();
    var bytes = ms.ToArray();
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
    return bytes;
}

static byte[] Build1BitDib()
{
    // 32×32, 1 bpp, two-colour palette; AND mask: bottom-right pixel transparent.
    int stride = 4;
    var d = new byte[40 + 8 + stride * 32 + stride * 32];
    BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(0), 40);
    BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(4), 32);
    BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(8), 64);
    BinaryPrimitives.WriteInt16LittleEndian(d.AsSpan(12), 1);
    BinaryPrimitives.WriteInt16LittleEndian(d.AsSpan(14), 1);
    d[44] = 255; d[45] = 255; d[46] = 255; // palette[1] = white
    int mask = 48 + stride * 32;
    d[mask + 3] |= 0x01; // row 0 in file = bottom row; last bit = x 31
    return d;
}

static string Placeholders(string s) => string.Join(",", System.Text.RegularExpressions.Regex.Matches(s, @"\{\d\}").Select(m => m.Value).Order());

static void Eq<T>(T expected, T actual, string what)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{what}: expected {expected}, got {actual}");
}

static void True(bool condition, string what)
{
    if (!condition) throw new Exception(what);
}

static void Throws<TEx>(Action a) where TEx : Exception
{
    try { a(); } catch (TEx) { return; }
    throw new Exception($"expected {typeof(TEx).Name}");
}
