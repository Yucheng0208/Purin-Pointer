using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using PurinPointer.Core;
using Microsoft.Win32;

namespace PurinPointer;

/// <summary>Everything needed to put the pointer settings back exactly as they were.</summary>
public sealed class CursorSnapshot
{
    public Dictionary<string, string> Values { get; set; } = [];
    public string? SchemeName { get; set; }
    public int? SchemeSource { get; set; }
}

/// <summary>
/// Reads and writes the current user's pointer scheme. Everything lives under
/// <c>HKEY_CURRENT_USER\Control Panel\Cursors</c> and in the user's local app data, so no
/// administrator rights (UAC prompt) are ever needed — the same place the Mouse control panel writes.
/// </summary>
public static class CursorSystem
{
    const string CursorsKey = @"Control Panel\Cursors";
    const string SchemesKey = @"Control Panel\Cursors\Schemes";
    public const string SchemeName = "Purin Pointer";

    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PurinPointer");
    public static string CursorDir => Path.Combine(DataDir, "cursors");
    public static string BackupDir => Path.Combine(DataDir, "backups");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static CursorSnapshot ReadCurrent()
    {
        using var key = Registry.CurrentUser.CreateSubKey(CursorsKey);
        var snap = new CursorSnapshot
        {
            SchemeName = key.GetValue("") as string,
            SchemeSource = key.GetValue("Scheme Source") is int s ? s : null,
        };
        foreach (var role in CursorRole.All)
            snap.Values[role.RegistryName] =
                key.GetValue(role.RegistryName, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
        return snap;
    }

    /// <summary>The file a role currently uses, with environment variables expanded, or null for the built-in cursor.</summary>
    public static string? CurrentFile(CursorSnapshot snap, CursorRole role)
    {
        var v = snap.Values.GetValueOrDefault(role.RegistryName, "");
        if (string.IsNullOrWhiteSpace(v)) return null;
        var path = Environment.ExpandEnvironmentVariables(v.Trim('"'));
        return File.Exists(path) ? path : null;
    }

    public static bool IsDefault(CursorSnapshot snap, CursorRole role)
    {
        var v = snap.Values.GetValueOrDefault(role.RegistryName, "");
        if (string.IsNullOrWhiteSpace(v)) return true;
        return string.Equals(Environment.ExpandEnvironmentVariables(v), Environment.ExpandEnvironmentVariables(role.DefaultValue),
                             StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Saves a new cursor file into the app's data folder and returns its path.</summary>
    public static string StoreCursorFile(CursorRole role, byte[] data, string extension)
    {
        Directory.CreateDirectory(CursorDir);
        var path = Path.Combine(CursorDir, $"{role.RegistryName}-{DateTime.Now:yyyyMMdd-HHmmss-fff}{extension}");
        File.WriteAllBytes(path, data);
        return path;
    }

    /// <summary>
    /// Applies the changes (role → file path, or null for the Windows default), after backing up
    /// the current state so it can be undone.
    /// </summary>
    public static void Apply(IReadOnlyDictionary<CursorRole, string?> changes)
    {
        var before = ReadCurrent();
        SaveBackup(before);

        var values = new Dictionary<string, string>(before.Values);
        foreach (var (role, path) in changes)
            values[role.RegistryName] = path ?? role.DefaultValue;

        bool allDefault = CursorRole.All.All(r =>
            string.Equals(values[r.RegistryName], r.DefaultValue, StringComparison.OrdinalIgnoreCase));

        if (allDefault)
        {
            Write(new CursorSnapshot { Values = values, SchemeName = "Windows Default", SchemeSource = 2 });
        }
        else
        {
            Write(new CursorSnapshot { Values = values, SchemeName = SchemeName, SchemeSource = 1 });
            // Also save it as a named scheme so it shows up in the classic Mouse Properties dialog.
            using var schemes = Registry.CurrentUser.CreateSubKey(SchemesKey);
            schemes.SetValue(SchemeName, string.Join(",", CursorRole.All.Select(r => values[r.RegistryName])),
                             RegistryValueKind.ExpandString);
        }
        CleanUpFiles();
    }

    public static void ResetAll() => Apply(CursorRole.All.ToDictionary(r => r, r => (string?)null));

    /// <summary>Restores the most recent backup. Returns false when there is none.</summary>
    public static bool UndoLast()
    {
        var latest = Backups().FirstOrDefault();
        if (latest is null) return false;
        var snap = JsonSerializer.Deserialize<CursorSnapshot>(File.ReadAllText(latest));
        if (snap is null) { File.Delete(latest); return false; }
        Write(snap);
        File.Delete(latest);
        CleanUpFiles();
        return true;
    }

    public static bool HasBackup => Backups().Any();

    /// <summary>The pointer size from Settings › Accessibility › Mouse pointer (32–256 px).</summary>
    public static int GetBaseSize()
    {
        using var key = Registry.CurrentUser.OpenSubKey(CursorsKey);
        return key?.GetValue("CursorBaseSize") is int v && v >= 32 ? v : 32;
    }

    public static void SetBaseSize(int size)
    {
        size = Math.Clamp(size / 16 * 16, 32, 256);
        using (var key = Registry.CurrentUser.CreateSubKey(CursorsKey))
            key.SetValue("CursorBaseSize", size, RegistryValueKind.DWord);
        Reload();
    }

    static void Write(CursorSnapshot snap)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(CursorsKey))
        {
            foreach (var role in CursorRole.All)
                key.SetValue(role.RegistryName, snap.Values.GetValueOrDefault(role.RegistryName, role.DefaultValue),
                             RegistryValueKind.ExpandString);
            key.SetValue("", snap.SchemeName ?? "", RegistryValueKind.String);
            if (snap.SchemeSource is int src) key.SetValue("Scheme Source", src, RegistryValueKind.DWord);
        }
        Reload();
    }

    /// <summary>Tells Windows to reload the cursors from the registry and notifies running apps.</summary>
    static void Reload()
    {
        if (!SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
            throw new InvalidOperationException($"Windows refused to reload the cursors (error {Marshal.GetLastWin32Error()}).");
    }

    static void SaveBackup(CursorSnapshot snap)
    {
        Directory.CreateDirectory(BackupDir);
        File.WriteAllText(Path.Combine(BackupDir, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.json"), JsonSerializer.Serialize(snap, Json));
        foreach (var old in Backups().Skip(30)) File.Delete(old);
    }

    static IEnumerable<string> Backups() =>
        Directory.Exists(BackupDir)
            ? Directory.GetFiles(BackupDir, "*.json").OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
            : [];

    /// <summary>Deletes cursor files that neither the registry nor any backup refers to.</summary>
    static void CleanUpFiles()
    {
        if (!Directory.Exists(CursorDir)) return;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(CursorSnapshot? s)
        {
            if (s is null) return;
            foreach (var v in s.Values.Values)
                if (!string.IsNullOrWhiteSpace(v)) used.Add(Path.GetFullPath(Environment.ExpandEnvironmentVariables(v.Trim('"'))));
        }
        Add(ReadCurrent());
        foreach (var b in Backups())
        {
            try { Add(JsonSerializer.Deserialize<CursorSnapshot>(File.ReadAllText(b))); }
            catch (JsonException) { }
        }
        foreach (var file in Directory.GetFiles(CursorDir))
            if (!used.Contains(Path.GetFullPath(file)))
                try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    const uint SPI_SETCURSORS = 0x0057;
    const uint SPIF_UPDATEINIFILE = 0x01;
    const uint SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);
}

/// <summary>Small persisted preferences.</summary>
public sealed class AppSettings
{
    public string? Language { get; set; }

    static string FilePath => Path.Combine(CursorSystem.DataDir, "settings.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(CursorSystem.DataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
}
