using System.Globalization;
using System.IO;
using System.Windows;
using PurinPointer.Core;

namespace PurinPointer;

/// <summary>
/// App state and actions, ported from AniOnMac's <c>Studio</c>: the theme library, the selected
/// mode, importing, background removal, hotspot, apply / undo / restore Windows appearance.
/// The window listens to <see cref="Changed"/> and redraws.
/// </summary>
public sealed class Studio
{
    public static readonly int[] FrameIntervals = [50, 83, 100, 150, 200, 300, 500];

    public string Root { get; } = CursorSystem.DataDir;
    public AssetStore Assets { get; }
    public Library Library { get; private set; } = new();
    public bool Ready { get; private set; }
    public bool Busy { get; private set; }
    public string Mode { get; private set; } = CursorModes.Arrow;
    public string StatusKey { get; private set; } = "ready";
    public object[] StatusArgs { get; private set; } = [];
    public int FrameInterval { get; set; } = 100;
    public bool WandActive { get; set; }
    public double Tolerance { get; set; } = 48;
    public bool CheckingUpdates { get; private set; }

    /// <summary>Only a successful apply in this running session counts as "already applied".</summary>
    Profile? _activeApplication;

    readonly Dictionary<string, PreviewFrames> _cache = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Changed;
    /// <summary>Raised with a message the window shows in an error dialog.</summary>
    public event Action<string>? Failed;
    /// <summary>Raised when several images need ordering on the timeline first.</summary>
    public event Action<List<string>>? TimelineRequested;

    public Studio() => Assets = new AssetStore(Root);

    static string T(string key) => Loc.Instance[key];
    string LibraryFile => Path.Combine(Root, "library.json");

    public Profile Profile => Library.Current!;
    public Slot Slot => Profile.Slot(Mode);

    // ── Loading and saving ───────────────────────────────────────────────────

    public void Load()
    {
        try
        {
            Directory.CreateDirectory(Root);
            if (File.Exists(LibraryFile))
            {
                Library = Library.Deserialize(File.ReadAllText(LibraryFile));
            }
            else
            {
                // Name the first theme in the system language without switching the UI yet:
                // the window redraws on a language change, and there is no theme until this returns.
                var code = Loc.Detect(CultureInfo.CurrentUICulture);
                var name = Loc.Load(code).GetValueOrDefault("newTheme") ?? T("newTheme");
                Library = Library.CreateNew(name);
                Persist();
            }
            // "In use" only while Windows really points at our files (Settings or another app may have changed them).
            if (Library.InUse is not null && !CursorSystem.UsesAppCursors())
            {
                Library.InUse = null;
                Persist();
            }
            ApplyLanguage(Library.Language);
            ThemeManager.Apply(Library.Appearance);
            Ready = true;
            SetStatus("ready");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            Library = Library.CreateNew(T("newTheme"));
            SetStatus("loadFailed");
            Failed?.Invoke(T("invalidLibrary") + "\n" + e.Message);
        }
        Changed?.Invoke();
    }

    void Persist()
    {
        Directory.CreateDirectory(Root);
        var temp = LibraryFile + ".tmp";
        File.WriteAllText(temp, Library.Serialize());
        File.Move(temp, LibraryFile, overwrite: true);
    }

    void Save()
    {
        try { Persist(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Failed?.Invoke(T("saveFailed") + "\n" + e.Message); }
        Changed?.Invoke();
    }

    public PreviewFrames? Preview(string asset)
    {
        if (string.IsNullOrEmpty(asset)) return null;
        if (_cache.TryGetValue(asset, out var cached)) return cached;
        try
        {
            var frames = PreviewFrames.From(Assets.Load(asset, ImageLoader.Decode));
            _cache[asset] = frames;
            return frames;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public PreviewFrames? Preview(Slot slot) => Preview(slot.Asset);

    /// <summary>The first filled slot of a theme, for its thumbnail in the sidebar.</summary>
    public PreviewFrames? ThemeThumbnail(Profile p) =>
        p.Slots.Where(s => !s.IsEmpty).Select(Preview).FirstOrDefault(f => f is not null);

    // ── Status ───────────────────────────────────────────────────────────────

    public void SetStatus(string key, params object[] args)
    {
        StatusKey = key;
        StatusArgs = args;
        Changed?.Invoke();
    }

    public string StatusText => StatusArgs.Length > 0 ? Loc.Instance.Format(StatusKey, StatusArgs) : T(StatusKey);

    public bool StatusIsWarning => StatusKey is "applyFailed" or "loadFailed" or "refreshWarning" or "nothingRemoved";
    public bool StatusIsSuccess => StatusKey is "applied" or "alreadyApplied" or "nativeRestored" or "imported" or "backgroundRemoved"
                                   or "restored" or "originalRestored" or "sizeChanged";

    // ── Language and appearance ──────────────────────────────────────────────

    public void SetLanguage(string code)
    {
        Library.Language = code;
        ApplyLanguage(code);
        Save();
    }

    static void ApplyLanguage(string code) =>
        Loc.Instance.Code = code == "system" ? Loc.Detect(CultureInfo.CurrentUICulture) : code;

    public void SetAppearance(string mode)
    {
        Library.Appearance = Library.AppearanceModes.Contains(mode) ? mode : "system";
        ThemeManager.Apply(Library.Appearance);
        Save();
    }

    // ── Themes ───────────────────────────────────────────────────────────────

    public void Select(string id)
    {
        Library.Selected = id;
        WandActive = false;
        Save();
    }

    public void Rename(string name)
    {
        if (Profile.Name == name) return;
        Profile.Name = name;
        Save();
    }

    public void AddTheme(bool copy)
    {
        Library.Add(T("newTheme"), copy, T("copySuffix"));
        WandActive = false;
        Save();
    }

    public void RemoveTheme()
    {
        var backup = Library.RemoveSelected();
        DeleteData(backup);
        Assets.CollectGarbage(Library);
        Save();
    }

    public void SetMode(string key)
    {
        Mode = key;
        WandActive = false;
        Changed?.Invoke();
    }

    void RecordEditUndo() => DeleteData(Library.RecordEditUndo(Profile));

    void DeleteData(string? relative)
    {
        if (relative is null) return;
        try { File.Delete(Path.Combine(Root, relative)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // ── Import ───────────────────────────────────────────────────────────────

    /// <summary>One .ani/.cur imports directly; images and folders become frames (several go to the timeline first).</summary>
    public void ImportItems(IReadOnlyList<string> paths)
    {
        if (Busy || !Ready || paths.Count == 0) return;
        if (paths.Count == 1 && File.Exists(paths[0]) && ImageLoader.IsCursor(paths[0]))
        {
            var file = paths[0];
            _ = Run(async () =>
            {
                var cursor = await Task.Run(() => ImageLoader.ReadCursorFile(file));
                await StoreImport(cursor, Path.GetFileName(file));
            });
            return;
        }
        var frames = ImageLoader.ImageFrames(paths);
        if (frames.Count == 0) { Failed?.Invoke(T("chooseANI")); return; }
        if (frames.Count > 1) TimelineRequested?.Invoke(frames);
        else ImportFrames(frames);
    }

    public void ImportFrames(IReadOnlyList<string> files)
    {
        if (Busy || files.Count == 0) return;
        var key = Mode;
        int interval = FrameInterval;
        _ = Run(async () =>
        {
            var cursor = await Task.Run(() => CursorOps.FromFrames(ImageLoader.ReadImageFrames(files, interval), key));
            var name = files.Count == 1 ? Path.GetFileName(files[0])
                : $"{Path.GetFileName(files[0])} – {Path.GetFileName(files[^1])} · {files.Count} {T("frames")}";
            await StoreImport(cursor, name);
        });
    }

    async Task StoreImport(AnimatedCursor cursor, string filename)
    {
        var relative = await Task.Run(() => Assets.Save(cursor));
        RecordEditUndo();
        var slot = Slot;
        slot.Asset = relative;
        slot.Filename = filename;
        slot.Hotspot = null;
        slot.OriginalAsset = null;
        Assets.CollectGarbage(Library);
        SetStatus("imported");
        Save();
    }

    public void ClearSlot()
    {
        if (Slot.IsEmpty) return;
        RecordEditUndo();
        var key = Slot.Key;
        var i = Profile.Slots.FindIndex(s => s.Key == key);
        Profile.Slots[i] = new Slot { Key = key };
        WandActive = false;
        Assets.CollectGarbage(Library);
        Save();
    }

    // ── Hotspot ──────────────────────────────────────────────────────────────

    public (double X, double Y) HotspotOf(Slot slot) =>
        Preview(slot) is { } p ? CursorOps.Hotspot(slot, p.Cursor) : CursorModes.DefaultHotspot(slot.Key);

    public void SetHotspot(double[]? normalised)
    {
        Slot.Hotspot = normalised?.Select(v => Math.Clamp(v, 0, 0.999999)).ToArray();
        Save();
    }

    // ── Background removal ───────────────────────────────────────────────────

    /// <summary>Edge-background removal, or the magic wand at <paramref name="point"/> (fraction of the image).</summary>
    public void Knockout((double X, double Y)? point)
    {
        if (Busy || Slot.IsEmpty || Preview(Slot) is not { } preview) return;
        var slot = Slot;
        int tolerance = (int)Tolerance;
        _ = Run(async () =>
        {
            var (result, removed) = await Task.Run(() => CursorOps.Knockout(preview.Cursor, point, tolerance));
            if (removed == 0) { SetStatus("nothingRemoved"); return; }
            var relative = await Task.Run(() => Assets.Save(result));
            RecordEditUndo();
            slot.OriginalAsset ??= slot.Asset;
            slot.Asset = relative;
            Assets.CollectGarbage(Library);
            SetStatus("backgroundRemoved");
            Save();
        });
    }

    public void RestoreOriginal()
    {
        if (Slot.OriginalAsset is not { } original) return;
        RecordEditUndo();
        Slot.Asset = original;
        Slot.OriginalAsset = null;
        WandActive = false;
        Assets.CollectGarbage(Library);
        SetStatus("originalRestored");
        Save();
    }

    // ── Windows pointer size ─────────────────────────────────────────────────

    public int BaseSize => CursorSystem.GetBaseSize();

    public void SetBaseSize(int size)
    {
        CursorSystem.SetBaseSize(size);
        SetStatus("sizeChanged", CursorSystem.GetBaseSize());
    }

    // ── Apply / undo / restore ───────────────────────────────────────────────

    public bool IsApplied => _activeApplication is { } a && a.Id == Profile.Id && Profile.SameAs(a);
    public bool CanApply => Ready && !Busy && !IsApplied && Profile.HasAny;
    public bool CanUndo => Library.BackupsByTheme.ContainsKey(Profile.Id) || Library.UndoProfiles.ContainsKey(Profile.Id);

    public void Apply()
    {
        if (!CanApply) return;
        var p = Profile;
        var snapshot = p.Clone();
        int canvas = CursorOps.CanvasFor(CursorSystem.GetBaseSize());
        _ = Run(async () =>
        {
            // Build every file first, so a broken asset leaves Windows untouched.
            var files = await Task.Run(() =>
            {
                var result = new Dictionary<CursorRole, string>();
                foreach (var slot in snapshot.Slots.Where(s => !s.IsEmpty))
                {
                    var asset = Assets.Load(slot.Asset, ImageLoader.Decode);
                    var (data, ext) = CursorOps.Export(slot, asset, canvas);
                    var path = CursorSystem.StoreCursorFile($"{slot.Key}", data, ext);
                    foreach (var role in CursorModes.Roles(slot.Key)) result[role] = path;
                }
                return result;
            });

            var backup = $"backups/{p.Id}.json";
            CursorSystem.SaveSnapshot(CursorSystem.ReadCurrent(), Path.Combine(Root, backup));
            Library.UndoProfiles[p.Id] = Library.AppliedProfiles.GetValueOrDefault(p.Id) ?? Library.UndoProfiles.GetValueOrDefault(p.Id) ?? p.Clone();
            Library.BackupsByTheme[p.Id] = backup;
            Persist();

            _activeApplication = null;
            try { CursorSystem.ApplyRoles(files); }
            catch (InvalidOperationException) { SetStatus("refreshWarning"); }
            Library.AppliedProfiles[p.Id] = snapshot;
            _activeApplication = snapshot;
            Library.InUse = p.Id;
            CursorSystem.CleanUpFiles();
            if (StatusKey != "refreshWarning") SetStatus("applied");
            Save();
        }, failureStatus: "applyFailed");
    }

    /// <summary>AniOnMac's "Undo last change": puts Windows back as it was before this theme was
    /// applied (if it was), and the theme back to before its last edit.</summary>
    public void Restore()
    {
        if (Busy || !CanUndo) return;
        var id = Profile.Id;
        var backup = Library.BackupsByTheme.GetValueOrDefault(id);
        var backupPath = backup is null ? null : Path.Combine(Root, backup);
        _ = Run(() =>
        {
            if (backupPath is not null && File.Exists(backupPath))
            {
                _activeApplication = null;
                CursorSystem.Restore(backupPath);
            }
            if (Library.UndoProfiles.Remove(id, out var previous))
            {
                var i = Library.Profiles.FindIndex(x => x.Id == id);
                if (i >= 0) Library.Profiles[i] = previous;
                if (backup is not null) Library.AppliedProfiles[id] = previous;
            }
            if (backup is not null && Library.InUse == id) Library.InUse = null;
            Library.BackupsByTheme.Remove(id);
            DeleteData(backup);
            Assets.CollectGarbage(Library);
            CursorSystem.CleanUpFiles();
            SetStatus("restored");
            Save();
            return Task.CompletedTask;
        });
    }

    /// <summary>Puts all pointers and the pointer size back to the Windows defaults (independent of the selected theme).</summary>
    public void RestoreNative()
    {
        if (Busy) return;
        _ = Run(() =>
        {
            CursorSystem.SaveSnapshot(CursorSystem.ReadCurrent(), CursorSystem.RecoveryFile);
            _activeApplication = null;
            CursorSystem.ResetToDefaults();
            Library.InUse = null;
            CursorSystem.CleanUpFiles();
            SetStatus("nativeRestored");
            Save();
            return Task.CompletedTask;
        }, failureStatus: "applyFailed");
    }

    // ── Updates ──────────────────────────────────────────────────────────────

    public async Task<(UpdateChecker.Outcome, string?)?> CheckForUpdates()
    {
        if (CheckingUpdates) return null;
        CheckingUpdates = true;
        Changed?.Invoke();
        try { return await UpdateChecker.CheckAsync(); }
        finally { CheckingUpdates = false; Changed?.Invoke(); }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    async Task Run(Func<Task> work, string? failureStatus = null)
    {
        if (Busy) return;
        Busy = true;
        Changed?.Invoke();
        try { await work(); }
        catch (Exception e)
        {
            if (failureStatus is not null) SetStatus(failureStatus);
            Failed?.Invoke(e.Message);
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }
}
