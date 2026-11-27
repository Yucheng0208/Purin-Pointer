using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32.SafeHandles;
using PurinPointer.Core;

namespace PurinPointer;

/// <summary>
/// Hotspot calibration. Inside the pad the pointer is the cursor being edited, loaded at the size
/// Windows draws it. The user puts the tip they see on each dot and clicks; the click lands on the
/// current hotspot, so the average gap says where the hotspot should be.
/// </summary>
public partial class CalibrationWindow : Window
{
    readonly (double X, double Y) _hotspot;
    readonly double _width, _height;           // cursor image as shown, in DIPs (matches the editor's px)
    readonly double _reach;                    // clicks further than this from the dot are slips
    readonly List<(double X, double Y)> _offsets = [];
    CursorHandle? _handle;

    /// <summary>The corrected hotspot (fraction of the image) when the user pressed "Use this hotspot".</summary>
    public (double X, double Y)? Result { get; private set; }

    static string T(string key) => Loc.Instance[key];

    public CalibrationWindow(Slot slot, AnimatedCursor asset, (double X, double Y) hotspot, int baseSize)
    {
        InitializeComponent();
        _hotspot = hotspot;
        var img = asset.Frames[0].Image;
        double max = Math.Max(img.Width, img.Height);
        _width = baseSize * img.Width / max;
        _height = baseSize * img.Height / max;
        _reach = Math.Max(_width, _height) + 24;

        Title = TitleText.Text = T("calibrateTitle");
        HintText.Text = T("calibrateHint");
        CancelText.Text = T("cancel");
        UseText.Text = T("useHotspot");
        AgainText.Text = T("calibrateAgain");

        SourceInitialized += (_, _) =>
        {
            ThemeManager.ApplyTitleBar(this);
            PadCanvas.Cursor = LoadCursor(slot, asset, baseSize);
        };
        Pad.SizeChanged += (_, _) => Layout();
        Closed += (_, _) => _handle?.Dispose();
        Layout();
    }

    /// <summary>The first frame as a still cursor, at the pixel size Windows uses for this pointer size.</summary>
    Cursor? LoadCursor(Slot slot, AnimatedCursor asset, int baseSize)
    {
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int size = Math.Clamp((int)Math.Round(baseSize * dpi), 16, CurWriter.MaxSize);
        var still = new AnimatedCursor([asset.Frames[0]], asset.HotspotX, asset.HotspotY);
        var (data, _) = CursorOps.ExportFor(slot, still, baseSize, dpi);
        var path = Path.Combine(Path.GetTempPath(), $"PurinPointer-calibrate-{Guid.NewGuid():N}.cur");
        try
        {
            File.WriteAllBytes(path, data);
            var h = LoadImage(IntPtr.Zero, path, ImageCursor, size, size, LoadFromFile);
            if (h == IntPtr.Zero) return null;
            _handle = new CursorHandle(h);
            return CursorInteropHelper.Create(_handle);
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    bool Done => _offsets.Count >= Calibration.Targets.Length;

    void Layout()
    {
        double w = PadCanvas.ActualWidth, h = PadCanvas.ActualHeight;
        ProgressText.Text = Loc.Instance.Format("calibrateProgress", Math.Min(_offsets.Count + 1, Calibration.Targets.Length), Calibration.Targets.Length);
        Target.Visibility = Done ? Visibility.Collapsed : Visibility.Visible;
        ResultPanel.Visibility = Done ? Visibility.Visible : Visibility.Collapsed;
        UseButton.IsEnabled = Done;
        if (!Done)
        {
            var (tx, ty) = Calibration.Targets[_offsets.Count];
            Canvas.SetLeft(Target, tx * w - Target.Width / 2);
            Canvas.SetTop(Target, ty * h - Target.Height / 2);
            return;
        }

        var r = Calibration.Solve(_offsets, _hotspot, _width, _height);
        ResultText.Text = Math.Abs(r.OffsetX) < 1 && Math.Abs(r.OffsetY) < 1
            ? T("calibrateAccurate")
            : Loc.Instance.Format("calibrateResult", Signed(r.OffsetX), Signed(r.OffsetY));
        NewHotspotText.Text = Loc.Instance.Format("calibrateNewHotspot",
            Math.Round(r.HotX * _width, 1).ToString(CultureInfo.CurrentCulture) + " px",
            Math.Round(r.HotY * _height, 1).ToString(CultureInfo.CurrentCulture) + " px");
        ResultPanel.Width = Math.Max(0, w - 40);
        ResultPanel.UpdateLayout();
        Canvas.SetLeft(ResultPanel, 20);
        Canvas.SetTop(ResultPanel, Math.Max(0, (h - ResultPanel.ActualHeight) / 2));
        Result = (r.HotX, r.HotY);
    }

    static string Signed(double v) => (v >= 0 ? "+" : "") + Math.Round(v, 1).ToString(CultureInfo.CurrentCulture) + " px";

    void OnPadClick(object sender, MouseButtonEventArgs e)
    {
        if (Done) return;
        var p = e.GetPosition(PadCanvas);
        var (tx, ty) = Calibration.Targets[_offsets.Count];
        double dx = tx * PadCanvas.ActualWidth - p.X, dy = ty * PadCanvas.ActualHeight - p.Y;
        bool slipped = Math.Sqrt(dx * dx + dy * dy) > _reach;
        WarningText.Text = T("calibrateTooFar");
        WarningText.Visibility = slipped ? Visibility.Visible : Visibility.Collapsed;
        if (!slipped) _offsets.Add((dx, dy));
        Layout();
    }

    void OnAgain(object sender, RoutedEventArgs e)
    {
        _offsets.Clear();
        Result = null;
        WarningText.Visibility = Visibility.Collapsed;
        Layout();
    }

    void OnCancel(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
    }

    void OnUse(object sender, RoutedEventArgs e)
    {
        if (!Done) return;
        DialogResult = true;
    }

    sealed class CursorHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public CursorHandle(IntPtr handle) : base(true) => SetHandle(handle);
        protected override bool ReleaseHandle() => DestroyCursor(handle);
    }

    const uint ImageCursor = 2, LoadFromFile = 0x10;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint load);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyCursor(IntPtr cursor);
}
