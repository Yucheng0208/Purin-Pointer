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
    /// <summary>The pointer size (CursorBaseSize); null in backups made before it was recorded.</summary>
    public int? BaseSize { get; set; }
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
    /// <summary>Pointer settings from just before the last "Restore Windows appearance".</summary>
    public static string RecoveryFile => Path.Combine(DataDir, "native-reset-recovery.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static CursorSnapshot ReadCurrent()
    {
        using var key = Registry.CurrentUser.CreateSubKey(CursorsKey);
        var snap = new CursorSnapshot
        {
            SchemeName = key.GetValue("") as string,
            SchemeSource = key.GetValue("Scheme Source") is int s ? s : null,
            BaseSize = key.GetValue("CursorBaseSize") is int b ? b : null,
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
    public static string StoreCursorFile(string name, byte[] data, string extension)
    {
        Directory.CreateDirectory(CursorDir);
        var path = Path.Combine(CursorDir, $"{name}-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, data);
        return path;
    }

    /// <summary>
    /// Writes the given pointers (role → file path) and leaves every other pointer as it is,
    /// like AniOnMac's "only the filled modes are applied".
    /// </summary>
    public static void ApplyRoles(IReadOnlyDictionary<CursorRole, string> changes)
    {
        var before = ReadCurrent();
        var values = new Dictionary<string, string>(before.Values);
        foreach (var (role, path) in changes) values[role.RegistryName] = path;
        Write(new CursorSnapshot { Values = values, SchemeName = SchemeName, SchemeSource = 1, BaseSize = before.BaseSize });
        // Also save it as a named scheme so it shows up in the classic Mouse Properties dialog.
        using var schemes = Registry.CurrentUser.CreateSubKey(SchemesKey);
        schemes.SetValue(SchemeName, string.Join(",", CursorRole.All.Select(r => values[r.RegistryName])), RegistryValueKind.ExpandString);
    }

    /// <summary>
    /// Puts every pointer back to the "Windows Default" scheme and the pointer size back to 32 px,
    /// like Mouse Properties › Pointers › "Use Default" plus the accessibility size slider.
    /// </summary>
    public static void ResetToDefaults()
    {
        var defaults = SystemDefaults();
        Write(new CursorSnapshot
        {
            Values = CursorRole.All.ToDictionary(r => r.RegistryName, r => defaults[r]),
            SchemeName = "Windows Default",
            SchemeSource = 2,
            BaseSize = 32,
        });
    }

    public static void SaveSnapshot(CursorSnapshot snap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(snap, Json));
    }

    /// <summary>Writes a saved snapshot back to Windows.</summary>
    public static void Restore(string path)
    {
        var snap = JsonSerializer.Deserialize<CursorSnapshot>(File.ReadAllText(path))
            ?? throw new InvalidDataException("The backup is empty.");
        Write(snap);
    }

    /// <summary>
    /// The default scheme as this copy of Windows defines it (HKLM … Cursors\Schemes\"Windows Default"),
    /// falling back to the standard Aero files for any entry that is missing or points nowhere.
    /// </summary>
    public static Dictionary<CursorRole, string> SystemDefaults()
    {
        var result = CursorRole.All.ToDictionary(r => r, r => r.DefaultValue);
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\Cursors\Schemes");
            if (key?.GetValue("Windows Default", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string scheme)
            {
                var parts = scheme.Split(',');
                for (int i = 0; i < Math.Min(parts.Length, CursorRole.All.Count); i++)
                {
                    var v = parts[i].Trim();
                    if (v.Length == 0 || File.Exists(Environment.ExpandEnvironmentVariables(v)))
                        result[CursorRole.All[i]] = v;
                }
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return result;
    }

    /// <summary>The pointer size from Settings › Accessibility › Mouse pointer (32–256 px).</summary>
    public static int GetBaseSize()
    {
        int live = 0;
        if (SystemParametersInfo(SPI_GETCURSORBASESIZE, 0, ref live, 0) && live >= 32 && live <= 256) return live;
        using var key = Registry.CurrentUser.OpenSubKey(CursorsKey);
        return key?.GetValue("CursorBaseSize") is int v && v >= 32 ? v : 32;
    }

    public static void SetBaseSize(int size)
    {
        size = Math.Clamp(size / 16 * 16, 32, 256);
        StoreBaseSize(size);
        Reload(size);
    }

    /// <summary>Writes the size where Windows and Settings › Accessibility › Mouse pointer read it.</summary>
    static void StoreBaseSize(int size)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(CursorsKey))
            key.SetValue("CursorBaseSize", size, RegistryValueKind.DWord);
        // The Settings slider (1–15, 1 = 32 px, +16 px per step) reads this; keep it in sync.
        try
        {
            using var a11y = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Accessibility");
            a11y.SetValue("CursorSize", (size - 32) / 16 + 1, RegistryValueKind.DWord);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
    }

    public static void Write(CursorSnapshot snap)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(CursorsKey))
        {
            foreach (var role in CursorRole.All)
                key.SetValue(role.RegistryName, snap.Values.GetValueOrDefault(role.RegistryName, role.DefaultValue),
                             RegistryValueKind.ExpandString);
            key.SetValue("", snap.SchemeName ?? "", RegistryValueKind.String);
            if (snap.SchemeSource is int src) key.SetValue("Scheme Source", src, RegistryValueKind.DWord);
        }
        int size = snap.BaseSize is int s ? Math.Clamp(s / 16 * 16, 32, 256) : GetBaseSize();
        StoreBaseSize(size);
        Reload(size);
    }

    /// <summary>
    /// Tells Windows to use the pointer size and reload the cursors from the registry.
    /// SPI_SETCURSORS reports FALSE (with no error code) on many Windows 10/11 builds even though
    /// the cursors do reload, so its return value is not treated as a failure.
    /// </summary>
    static void Reload(int size)
    {
        // Undocumented but stable since Windows 10 1809: the call Settings makes for the pointer size.
        // Without it Windows keeps its old in-memory size and may write it back over CursorBaseSize.
        SystemParametersInfo(SPI_SETCURSORBASESIZE, 0, (IntPtr)size, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    }

    /// <summary>True when Windows' pointers currently point at files this app wrote.</summary>
    public static bool UsesAppCursors()
    {
        var snap = ReadCurrent();
        var dir = Path.GetFullPath(CursorDir).TrimEnd('\\') + "\\";
        return snap.Values.Values.Any(v =>
        {
            if (string.IsNullOrWhiteSpace(v)) return false;
            try
            {
                var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(v.Trim('"')));
                return path.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && File.Exists(path);
            }
            catch (ArgumentException) { return false; }
        });
    }

    /// <summary>Deletes applied cursor files that neither the registry nor any saved snapshot refers to.</summary>
    public static void CleanUpFiles()
    {
        if (!Directory.Exists(CursorDir)) return;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(CursorSnapshot? s)
        {
            if (s is null) return;
            foreach (var v in s.Values.Values)
                if (!string.IsNullOrWhiteSpace(v))
                    try { used.Add(Path.GetFullPath(Environment.ExpandEnvironmentVariables(v.Trim('"')))); } catch (ArgumentException) { }
        }
        Add(ReadCurrent());
        var snapshots = Directory.Exists(BackupDir) ? Directory.GetFiles(BackupDir, "*.json").ToList() : [];
        if (File.Exists(RecoveryFile)) snapshots.Add(RecoveryFile);
        foreach (var b in snapshots)
        {
            try { Add(JsonSerializer.Deserialize<CursorSnapshot>(File.ReadAllText(b))); }
            catch (Exception e) when (e is JsonException or IOException) { }
        }
        foreach (var file in Directory.GetFiles(CursorDir))
            if (!used.Contains(Path.GetFullPath(file)))
                try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    const uint SPI_SETCURSORS = 0x0057;
    const uint SPI_GETCURSORBASESIZE = 0x2028;
    const uint SPI_SETCURSORBASESIZE = 0x2029;
    const uint SPIF_UPDATEINIFILE = 0x01;
    const uint SPIF_SENDCHANGE = 0x02;

    /// <summary>The system display scale (1 = 100 %), which Windows uses to size pointers.</summary>
    public static double DpiScale()
    {
        try { return Math.Max(1, GetDpiForSystem() / 96.0); }
        catch (EntryPointNotFoundException) { return 1; }
    }

    [DllImport("user32.dll")]
    static extern uint GetDpiForSystem();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);
}
