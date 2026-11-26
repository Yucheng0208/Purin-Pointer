using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace PurinPointer;

/// <summary>
/// Switches between the light and dark colour dictionaries. In "system" mode it follows
/// Settings › Personalisation › Colours › "Choose your app mode" and updates live when it changes.
/// </summary>
public static class ThemeManager
{
    public const string System = "system", Light = "light", Dark = "dark";
    public static readonly string[] Modes = [System, Light, Dark];

    public static string Mode { get; private set; } = System;
    public static bool IsDark { get; private set; }

    static bool _listening;

    public static void Apply(string mode)
    {
        Mode = Modes.Contains(mode) ? mode : System;
        IsDark = Mode == Dark || (Mode == System && SystemUsesDarkMode());

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var theme = new ResourceDictionary { Source = new Uri($"pack://application:,,,/PurinPointer;component/Themes/{(IsDark ? "Dark" : "Light")}.xaml") };
        if (dictionaries.Count > 0) dictionaries[0] = theme; else dictionaries.Add(theme);

        foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);

        if (!_listening)
        {
            _listening = true;
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (Mode == System && e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
                    Application.Current.Dispatcher.BeginInvoke(() => Apply(System));
            };
        }
    }

    static bool SystemUsesDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }

    /// <summary>Gives the window a dark or light title bar to match (Windows 10 20H1 and later).</summary>
    public static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));
    }

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
