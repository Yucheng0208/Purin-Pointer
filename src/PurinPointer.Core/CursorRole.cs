namespace PurinPointer.Core;

/// <summary>A Windows pointer, named after its value under <c>HKCU\Control Panel\Cursors</c>.</summary>
public sealed record CursorRole(string RegistryName, string DefaultFile)
{
    /// <summary>The value the "Windows Default" scheme stores (empty means the built-in cursor).</summary>
    public string DefaultValue => DefaultFile.Length == 0 ? "" : @"%SystemRoot%\cursors\" + DefaultFile;

    /// <summary>All 17 pointers, in the order Windows stores them in a scheme string.</summary>
    public static readonly IReadOnlyList<CursorRole> All =
    [
        new("Arrow", "aero_arrow.cur"),
        new("Help", "aero_helpsel.cur"),
        new("AppStarting", "aero_working.ani"),
        new("Wait", "aero_busy.ani"),
        new("Crosshair", ""),
        new("IBeam", ""),
        new("NWPen", "aero_pen.cur"),
        new("No", "aero_unavail.cur"),
        new("SizeNS", "aero_ns.cur"),
        new("SizeWE", "aero_ew.cur"),
        new("SizeNWSE", "aero_nwse.cur"),
        new("SizeNESW", "aero_nesw.cur"),
        new("SizeAll", "aero_move.cur"),
        new("UpArrow", "aero_up.cur"),
        new("Hand", "aero_link.cur"),
        new("Pin", "aero_pin.cur"),
        new("Person", "aero_person.cur"),
    ];

    public static CursorRole Get(string registryName) => All.First(r => r.RegistryName == registryName);
}
