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
    ("assets are normalised: padded, shrunk to 256 px, hotspot scaled", () =>
    {
        var big = new AnimatedCursor([new CursorFrame(new Bitmap32(1024, 512), 100), new CursorFrame(new Bitmap32(10, 10), 100)], 1000, 500);
        var n = AssetStore.Normalise(big);
        True(n.Frames.All(f => f.Image.Width == 256 && f.Image.Height == 128), "all frames 256×128");
        Eq(250, n.HotspotX, "hx"); Eq(125, n.HotspotY, "hy");
    }),
    ("asset store saves, loads and collects unused files", () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "purin-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AssetStore(root);
            var frames = new List<CursorFrame> { new(Pattern(40, 30, 1), 120), new(Pattern(40, 30, 2), 80) };
            var a = store.Save(new AnimatedCursor(frames, 3, 4));
            var b = store.Save(new AnimatedCursor(frames, 0, 0));
            var back = store.Load(a);
            Eq(2, back.Frames.Count, "frames"); Eq(3, back.HotspotX, "hx"); Eq(40, back.Frames[0].Image.Width, "w");
            var lib = Library.CreateNew("t");
            lib.Current!.Slot(CursorModes.Arrow).Asset = a;
            store.CollectGarbage(lib);
            True(File.Exists(store.FullPath(a)), "kept referenced asset");
            True(!File.Exists(store.FullPath(b)), "removed unreferenced asset");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }),
    ("library round-trips through JSON and keeps every slot", () =>
    {
        var lib = Library.CreateNew("Theme");
        var p = lib.Current!;
        p.Slot(CursorModes.Wait).Asset = "assets/x.ani";
        p.Slot(CursorModes.Wait).Hotspot = [0.5, 0.25];
        p.Slot("Help").Asset = "assets/y.ani";
        lib.Language = "ja"; lib.Appearance = "dark"; lib.InUse = p.Id;
        var back = Library.Deserialize(lib.Serialize());
        Eq(p.Id, back.Selected, "selected");
        Eq(16, back.Current!.Slots.Count, "4 main + 12 extra slots");
        Eq(0.25, back.Current.Slot(CursorModes.Wait).Hotspot![1], "hotspot");
        Eq(1, back.Current.FilledMain, "main filled"); Eq(1, back.Current.FilledExtra, "extra filled");
        Eq("ja", back.Language, "language"); Eq("dark", back.Appearance, "appearance");
        True(back.Current.SameAs(p), "content equality");
        Throws<InvalidDataException>(() => Library.Deserialize("{\"Profiles\":[]}"));
    }),
    ("add, duplicate and remove themes", () =>
    {
        var lib = Library.CreateNew("A");
        lib.Current!.Slot(CursorModes.Arrow).Asset = "assets/a.ani";
        lib.Add("New", copyCurrent: true, copySuffix: "copy");
        Eq(2, lib.Profiles.Count, "two themes");
        Eq("A copy", lib.Current!.Name, "copy name");
        Eq("assets/a.ani", lib.Current.Slot(CursorModes.Arrow).Asset, "copied slots");
        True(lib.Current.Id != lib.Profiles[0].Id, "new id");
        lib.BackupsByTheme[lib.Current.Id] = "backups/x.json";
        lib.InUse = lib.Current.Id;
        Eq("backups/x.json", lib.RemoveSelected(), "returns backup to delete");
        Eq(1, lib.Profiles.Count, "one left"); Eq(null, lib.InUse, "in-use cleared");
        Eq(null, lib.RemoveSelected(), "never removes the last theme");
    }),
    ("an edit records undo and drops the apply backup", () =>
    {
        var lib = Library.CreateNew("A");
        var p = lib.Current!;
        lib.BackupsByTheme[p.Id] = "backups/a.json";
        p.Slot(CursorModes.Text).Asset = "assets/before.ani";
        Eq("backups/a.json", lib.RecordEditUndo(p), "backup returned");
        p.Slot(CursorModes.Text).Asset = "assets/after.ani";
        Eq("assets/before.ani", lib.UndoProfiles[p.Id].Slot(CursorModes.Text).Asset, "undo keeps the old state");
        True(lib.ReferencedAssets().Contains("assets/before.ani"), "undo state keeps its asset alive");
    }),
    ("wait mode writes both Windows busy pointers", () =>
    {
        Eq("Wait,AppStarting", string.Join(",", CursorModes.Roles(CursorModes.Wait).Select(r => r.RegistryName)), "wait roles");
        Eq("Hand", CursorModes.Roles(CursorModes.Pointing)[0].RegistryName, "pointing");
        Eq("IBeam", CursorModes.Roles(CursorModes.Text)[0].RegistryName, "text");
        var covered = CursorModes.All.SelectMany(CursorModes.Roles).Select(r => r.RegistryName).OrderBy(x => x).ToList();
        Eq(string.Join(",", CursorRole.All.Select(r => r.RegistryName).OrderBy(x => x)), string.Join(",", covered), "every Windows pointer reachable once");
    }),
    ("image imports use AniOnMac's default hotspots", () =>
    {
        var frames = new List<CursorFrame> { new(new Bitmap32(21, 11), 100) };
        var wait = CursorOps.FromFrames(frames, CursorModes.Wait);
        Eq(10, wait.HotspotX, "wait centred x"); Eq(5, wait.HotspotY, "wait centred y");
        var arrow = CursorOps.FromFrames(frames, CursorModes.Arrow);
        Eq(0, arrow.HotspotX, "arrow top-left");
    }),
    ("export places the hotspot on the canvas", () =>
    {
        var asset = new AnimatedCursor([new CursorFrame(Pattern(64, 32), 100)], 0, 0);
        var slot = new Slot { Key = CursorModes.Arrow, Asset = "x", Hotspot = [0.5, 0.5] };
        var (data, ext) = CursorOps.Export(slot, asset, 128);
        Eq(".cur", ext, "still → cur");
        var cur = IconReader.Read(data);
        Eq(128, cur.Image.Width, "canvas");
        Eq(64, cur.HotspotX, "x"); Eq(64, cur.HotspotY, "y (image letterboxed 32 px down)");
        var anim = new AnimatedCursor([new CursorFrame(Pattern(32, 32, 1), 100), new CursorFrame(Pattern(32, 32, 2), 200)], 4, 8);
        var (aniData, aniExt) = CursorOps.Export(new Slot { Key = CursorModes.Wait, Asset = "x" }, anim, 64);
        Eq(".ani", aniExt, "animation → ani");
        var back = AniReader.Read(aniData);
        Eq(8, back.HotspotX, "file hotspot scaled ×2"); Eq(200, back.Frames[1].DelayMs, "timing kept");
        Eq(64, CursorOps.CanvasFor(32), "canvas for 32 px"); Eq(256, CursorOps.CanvasFor(256), "capped at 256");
    }),
    ("knockout removes background on every frame and reports it", () =>
    {
        Bitmap32 Solid() { var b = new Bitmap32(6, 6); for (int i = 0; i < b.Pixels.Length; i += 4) { b.Pixels[i + 1] = 255; b.Pixels[i + 3] = 255; } return b; }
        var src = new AnimatedCursor([new CursorFrame(Solid(), 100), new CursorFrame(Solid(), 100)], 0, 0);
        var (wand, removed) = CursorOps.Knockout(src, (0.1, 0.1), 10);
        Eq(72, removed, "all 36 pixels × 2 frames");
        True(wand.Frames.All(f => f.Image.IsEmpty()), "both frames cleared");
        var (_, none) = CursorOps.Knockout(wand, (0.5, 0.5), 10);
        Eq(0, none, "nothing left to remove");
    }),
    ("update check compares versions numerically", () =>
    {
        True(Versions.IsNewer("v0.2.0", "0.1.9"), "minor");
        True(Versions.IsNewer("v0.10.0", "0.9.0"), "numeric not lexical");
        True(!Versions.IsNewer("v0.1.0", "0.1.0"), "same");
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
        foreach (var key in CursorModes.All) True(en.ContainsKey(CursorModes.NameKey(key)), $"name for {key}");
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
