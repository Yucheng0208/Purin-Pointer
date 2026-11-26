using System.Text.Json;
using System.Text.Json.Serialization;

namespace PurinPointer.Core;

/// <summary>
/// The editable pointer slots. The four main modes match AniOnMac; "wait" drives both of
/// Windows' busy pointers. The other Windows pointers are reachable from "More pointers".
/// </summary>
public static class CursorModes
{
    public const string Arrow = "arrow", Pointing = "pointing", Text = "text", Wait = "wait";
    public static readonly IReadOnlyList<string> Main = [Arrow, Pointing, Text, Wait];

    /// <summary>Windows pointers not covered by a main mode; their key is the registry name.</summary>
    public static readonly IReadOnlyList<string> Extra =
        ["Help", "Crosshair", "NWPen", "No", "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW", "SizeAll", "UpArrow", "Pin", "Person"];

    public static IEnumerable<string> All => Main.Concat(Extra);

    public static bool IsMain(string key) => Main.Contains(key);

    /// <summary>The Windows pointers a slot writes to when applied.</summary>
    public static IReadOnlyList<CursorRole> Roles(string key) => key switch
    {
        Arrow => [CursorRole.Get("Arrow")],
        Pointing => [CursorRole.Get("Hand")],
        Text => [CursorRole.Get("IBeam")],
        Wait => [CursorRole.Get("Wait"), CursorRole.Get("AppStarting")],
        _ => [CursorRole.Get(key)],
    };

    /// <summary>Localisation key for the slot's name.</summary>
    public static string NameKey(string key) => IsMain(key) ? key : "role." + key;

    /// <summary>Wait and text cursors are usually centred; the others point from the top-left (as in AniOnMac).</summary>
    public static (double X, double Y) DefaultHotspot(string key) =>
        key is Wait or Text or "Crosshair" or "SizeAll" or "SizeNS" or "SizeWE" or "SizeNWSE" or "SizeNESW" ? (0.5, 0.5) : (0, 0);
}

public sealed class Slot
{
    public string Key { get; set; } = "";
    /// <summary>Path of the stored .ani asset, relative to the data folder; empty = not set.</summary>
    public string Asset { get; set; } = "";
    public string Filename { get; set; } = "";
    /// <summary>Hotspot as a fraction of the image (0–1); null uses the asset's own hotspot.</summary>
    public double[]? Hotspot { get; set; }
    /// <summary>Asset before background removal, so it can be restored.</summary>
    public string? OriginalAsset { get; set; }

    [JsonIgnore] public bool IsEmpty => Asset.Length == 0;

    public Slot Clone() => new() { Key = Key, Asset = Asset, Filename = Filename, Hotspot = Hotspot?.ToArray(), OriginalAsset = OriginalAsset };
}

public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public List<Slot> Slots { get; set; } = [];

    public Slot Slot(string key)
    {
        var s = Slots.FirstOrDefault(x => x.Key == key);
        if (s is null) { s = new Slot { Key = key }; Slots.Add(s); }
        return s;
    }

    /// <summary>Makes sure every known slot exists exactly once, in a stable order.</summary>
    public void Normalise() =>
        Slots = CursorModes.All.Select(k => Slots.FirstOrDefault(x => x.Key == k) ?? new Slot { Key = k }).ToList();

    public int FilledMain => CursorModes.Main.Count(k => !Slot(k).IsEmpty);
    public int FilledExtra => CursorModes.Extra.Count(k => !Slot(k).IsEmpty);
    public bool HasAny => Slots.Any(s => !s.IsEmpty);

    public Profile Clone() => new() { Id = Id, Name = Name, Slots = Slots.Select(s => s.Clone()).ToList() };

    /// <summary>Content equality (used for "already applied").</summary>
    public bool SameAs(Profile? other) =>
        other is not null && JsonSerializer.Serialize(this, Library.Json) == JsonSerializer.Serialize(other, Library.Json);
}

/// <summary>Everything the app remembers, saved as <c>library.json</c> after every change.</summary>
public sealed class Library
{
    public static readonly string[] AppearanceModes = ["system", "light", "dark"];
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public List<Profile> Profiles { get; set; } = [];
    public string Selected { get; set; } = "";
    /// <summary>Theme whose cursors are currently applied to Windows (shown as "In use").</summary>
    public string? InUse { get; set; }
    /// <summary>Per theme: the profile before the last edit or apply, for "Undo last change".</summary>
    public Dictionary<string, Profile> UndoProfiles { get; set; } = [];
    /// <summary>Per theme: the profile as last applied.</summary>
    public Dictionary<string, Profile> AppliedProfiles { get; set; } = [];
    /// <summary>Per theme: relative path of the pointer settings saved just before it was applied.</summary>
    public Dictionary<string, string> BackupsByTheme { get; set; } = [];
    /// <summary>"system" or a language code such as "zh-Hant".</summary>
    public string Language { get; set; } = "system";
    /// <summary>"system", "light" or "dark".</summary>
    public string Appearance { get; set; } = "system";

    [JsonIgnore] public Profile? Current => Profiles.FirstOrDefault(p => p.Id == Selected);

    public static Library CreateNew(string themeName)
    {
        var p = new Profile { Name = themeName };
        p.Normalise();
        return new Library { Profiles = [p], Selected = p.Id };
    }

    public Profile Add(string name, bool copyCurrent, string copySuffix)
    {
        var p = copyCurrent && Current is { } c ? c.Clone() : new Profile();
        p.Id = Guid.NewGuid().ToString();
        p.Name = copyCurrent && Current is { } cur ? $"{cur.Name} {copySuffix}" : name;
        p.Normalise();
        Profiles.Add(p);
        Selected = p.Id;
        return p;
    }

    /// <summary>Removes the selected theme (never the last one). Returns its backup path, if any, so the caller can delete it.</summary>
    public string? RemoveSelected()
    {
        if (Profiles.Count < 2) return null;
        var id = Selected;
        BackupsByTheme.Remove(id, out var backup);
        UndoProfiles.Remove(id);
        AppliedProfiles.Remove(id);
        if (InUse == id) InUse = null;
        Profiles.RemoveAll(p => p.Id == id);
        Selected = Profiles[0].Id;
        return backup;
    }

    /// <summary>Remembers the current state of a theme before an edit; the edit replaces any apply backup.</summary>
    public string? RecordEditUndo(Profile p)
    {
        UndoProfiles[p.Id] = p.Clone();
        BackupsByTheme.Remove(p.Id, out var backup);
        return backup;
    }

    /// <summary>Every asset path the library still refers to.</summary>
    public HashSet<string> ReferencedAssets()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(Profile p)
        {
            foreach (var s in p.Slots)
            {
                if (!s.IsEmpty) set.Add(s.Asset);
                if (s.OriginalAsset is { Length: > 0 } o) set.Add(o);
            }
        }
        foreach (var p in Profiles) Add(p);
        foreach (var p in UndoProfiles.Values) Add(p);
        foreach (var p in AppliedProfiles.Values) Add(p);
        return set;
    }

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static Library Deserialize(string json)
    {
        var lib = JsonSerializer.Deserialize<Library>(json, Json) ?? throw new InvalidDataException("Empty library.");
        if (lib.Profiles.Count == 0) throw new InvalidDataException("The library has no themes.");
        foreach (var p in lib.Profiles.Concat(lib.UndoProfiles.Values).Concat(lib.AppliedProfiles.Values)) p.Normalise();
        if (lib.Current is null) lib.Selected = lib.Profiles[0].Id;
        if (!AppearanceModes.Contains(lib.Appearance)) lib.Appearance = "system";
        return lib;
    }
}

/// <summary>
/// Stores imported cursors as normalised .ani files under <c>assets/</c> (at most 256 px, frames in
/// play order, original timing and hotspot), and reads them back.
/// </summary>
public sealed class AssetStore(string root)
{
    public const int MaxSide = 256;
    public string Root { get; } = root;

    public string FullPath(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    public string Save(AnimatedCursor cursor)
    {
        var normalised = Normalise(cursor);
        var relative = $"assets/{Guid.NewGuid():N}.ani";
        var path = FullPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, AniWriter.Write(normalised.Frames, normalised.HotspotX, normalised.HotspotY));
        return relative;
    }

    public AnimatedCursor Load(string relative, ImageDecoder? decoder = null) => AniReader.Read(File.ReadAllBytes(FullPath(relative)), decoder);

    /// <summary>Deletes asset files nothing refers to any more.</summary>
    public void CollectGarbage(Library library)
    {
        var dir = FullPath("assets");
        if (!Directory.Exists(dir)) return;
        var keep = library.ReferencedAssets().Select(FullPath).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.GetFiles(dir))
            if (!keep.Contains(Path.GetFullPath(f)))
                try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Pads mixed frame sizes to one size and shrinks anything over 256 px, scaling the hotspot to match.</summary>
    public static AnimatedCursor Normalise(AnimatedCursor c)
    {
        if (c.Frames.Count == 0) throw new ArgumentException("No frames.");
        int w = c.Frames.Max(f => f.Image.Width), h = c.Frames.Max(f => f.Image.Height);
        var frames = c.Frames.Select(f => f.Image.Width == w && f.Image.Height == h ? f : f with { Image = Pad(f.Image, w, h) }).ToList();
        int hx = c.HotspotX, hy = c.HotspotY;
        int side = Math.Max(w, h);
        if (side > MaxSide)
        {
            double k = (double)MaxSide / side;
            int nw = Math.Max(1, (int)Math.Round(w * k)), nh = Math.Max(1, (int)Math.Round(h * k));
            frames = frames.Select(f => f with { Image = Resampler.Resize(f.Image, nw, nh) }).ToList();
            hx = (int)Math.Round(hx * k); hy = (int)Math.Round(hy * k);
            w = nw; h = nh;
        }
        return new AnimatedCursor(frames, Math.Clamp(hx, 0, w - 1), Math.Clamp(hy, 0, h - 1));
    }

    static Bitmap32 Pad(Bitmap32 img, int w, int h)
    {
        var padded = new Bitmap32(w, h);
        int ox = (w - img.Width) / 2, oy = (h - img.Height) / 2;
        for (int y = 0; y < img.Height; y++)
            Buffer.BlockCopy(img.Pixels, y * img.Stride, padded.Pixels, ((y + oy) * w + ox) * 4, img.Stride);
        return padded;
    }
}

public static class CursorOps
{
    /// <summary>The slot's hotspot as a fraction of the image.</summary>
    public static (double X, double Y) Hotspot(Slot slot, AnimatedCursor asset)
    {
        if (slot.Hotspot is { Length: 2 } h) return (h[0], h[1]);
        var img = asset.Frames[0].Image;
        return ((double)asset.HotspotX / img.Width, (double)asset.HotspotY / img.Height);
    }

    /// <summary>Builds the image-sequence cursor (one image, an animated GIF's frames, or several stills).</summary>
    public static AnimatedCursor FromFrames(IReadOnlyList<CursorFrame> frames, string slotKey)
    {
        var (fx, fy) = CursorModes.DefaultHotspot(slotKey);
        int w = frames.Max(f => f.Image.Width), h = frames.Max(f => f.Image.Height);
        return new AnimatedCursor(frames, (int)Math.Floor(fx * (w - 1)), (int)Math.Floor(fy * (h - 1)));
    }

    /// <summary>
    /// Background removal on every frame. <paramref name="point"/> (fraction of the image) selects the
    /// magic wand; without it the flat edge background is removed. Returns the cursor and how many pixels went.
    /// </summary>
    public static (AnimatedCursor Result, int Removed) Knockout(AnimatedCursor src, (double X, double Y)? point, int tolerance)
    {
        int removed = 0;
        var frames = new List<CursorFrame>(src.Frames.Count);
        (byte, byte, byte)? seed = null;
        foreach (var f in src.Frames)
        {
            Bitmap32 result;
            if (point is { } p)
            {
                int x = Math.Clamp((int)(p.X * f.Image.Width), 0, f.Image.Width - 1);
                int y = Math.Clamp((int)(p.Y * f.Image.Height), 0, f.Image.Height - 1);
                if (seed is null)
                {
                    int o = (y * f.Image.Width + x) * 4;
                    if (f.Image.Pixels[o + 3] == 0) return (src, 0);
                    seed = (f.Image.Pixels[o], f.Image.Pixels[o + 1], f.Image.Pixels[o + 2]);
                }
                result = BackgroundRemover.MagicWand(f.Image, x, y, seed.Value, tolerance);
            }
            else
            {
                result = BackgroundRemover.RemoveEdgeBackground(f.Image, tolerance);
            }
            for (int i = 3; i < result.Pixels.Length; i += 4)
                if (result.Pixels[i] == 0 && f.Image.Pixels[i] != 0) removed++;
            frames.Add(f with { Image = result });
        }
        return (new AnimatedCursor(frames, src.HotspotX, src.HotspotY), removed);
    }

    /// <summary>
    /// The file written to Windows: frames fitted onto a square canvas with the slot's hotspot.
    /// A still image becomes .cur, an animation .ani.
    /// </summary>
    public static (byte[] Data, string Extension) Export(Slot slot, AnimatedCursor asset, int canvas)
    {
        canvas = Math.Clamp(canvas, 16, CurWriter.MaxSize);
        var img = asset.Frames[0].Image;
        var place = Resampler.Place(img.Width, img.Height, canvas);
        var (hx, hy) = Hotspot(slot, asset);
        var (cx, cy) = place.SourceToCanvas(hx * img.Width, hy * img.Height);
        int x = Math.Clamp((int)Math.Floor(cx), 0, canvas - 1), y = Math.Clamp((int)Math.Floor(cy), 0, canvas - 1);
        var frames = asset.Frames.Select(f => f with { Image = Resampler.FitToCanvas(f.Image, canvas) }).ToList();
        return frames.Count == 1
            ? (CurWriter.Write(frames[0].Image, x, y), ".cur")
            : (AniWriter.Write(frames, x, y), ".ani");
    }

    /// <summary>Canvas size for applied cursors: double the pointer size so they stay sharp at high DPI.</summary>
    public static int CanvasFor(int baseSize) => Math.Clamp(baseSize * 2, 64, CurWriter.MaxSize);
}
