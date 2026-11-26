using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PurinPointer.Core;
using Microsoft.Win32;

namespace PurinPointer;

public partial class MainWindow : Window
{
    const double PreviewSize = 320;

    readonly List<RoleItem> _roles = CursorRole.All.Select(r => new RoleItem(r)).ToList();
    readonly AppSettings _settings = AppSettings.Load();
    readonly DispatcherTimer _timer = new();

    List<CursorFrame> _rendered = [];
    List<BitmapSource> _renderedImages = [];
    int _frameIndex;
    bool _loadingControls;

    RoleItem? Current => RoleList.SelectedItem as RoleItem;
    CursorProject? Project => Current?.Project;

    public MainWindow()
    {
        InitializeComponent();

        Loc.Instance.Code = _settings.Language ?? Loc.Detect(CultureInfo.CurrentUICulture);
        _loadingControls = true;
        LanguageBox.ItemsSource = Loc.Languages;
        LanguageBox.SelectedItem = Loc.Languages.First(l => l.Code == Loc.Instance.Code);
        _loadingControls = false;
        Loc.Instance.PropertyChanged += (_, _) => { foreach (var r in _roles) r.Refresh(); UpdateEditor(); };

        SizeBox.ItemsSource = CursorProject.CanvasSizes.Select(s => $"{s} × {s}").ToList();
        RoleList.ItemsSource = _roles;
        _timer.Tick += (_, _) => NextFrame();

        _loadingControls = true;
        BaseSizeSlider.Value = CursorSystem.GetBaseSize();
        BaseSizeValue.Text = $"{(int)BaseSizeSlider.Value} px";
        _loadingControls = false;
        ToleranceValue.Text = ((int)ToleranceSlider.Value).ToString();

        RefreshFromSystem();
        RoleList.SelectedIndex = 0;
    }

    // ── Pointer list ─────────────────────────────────────────────────────────

    /// <summary>Reads what Windows is using now and updates each row that has no pending edit.</summary>
    void RefreshFromSystem()
    {
        var snap = CursorSystem.ReadCurrent();
        foreach (var item in _roles)
        {
            item.IsCustom = !CursorSystem.IsDefault(snap, item.Role);
            if (item.IsDirty && item.Project is not null) continue;
            var file = CursorSystem.CurrentFile(snap, item.Role);
            var thumb = file is null ? null : ImageLoader.Thumbnail(file);
            item.Thumbnail = thumb is null ? null : ImageLoader.ToBitmapSource(thumb);
        }
        UndoButton.IsEnabled = CursorSystem.HasBackup;
    }

    void OnRoleSelected(object sender, SelectionChangedEventArgs e)
    {
        WandToggle.IsChecked = false;
        Rerender();
    }

    // ── Importing ────────────────────────────────────────────────────────────

    void OnBrowseFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Filter = ImageLoader.DialogFilter(Loc.Instance["import.filter"]) };
        if (dialog.ShowDialog(this) == true) Import(dialog.FileNames);
    }

    void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog(this) == true) Import([dialog.FolderName]);
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0) Import(paths);
    }

    void Import(IReadOnlyList<string> paths)
    {
        if (Current is not { } item) return;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            item.Project = ImageLoader.Load(paths);
            item.PendingDefault = false;
            item.IsDirty = true;
            WandToggle.IsChecked = false;
            Rerender();
            Status(Loc.Instance.Format("import.loaded", item.Project.SourceName));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            var name = paths.Count == 1 ? Path.GetFileName(paths[0]) : $"{paths.Count} files";
            Status(Loc.Instance.Format("import.failed", name, ex.Message));
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    // ── Editing ──────────────────────────────────────────────────────────────

    void OnSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingControls || Project is null || SizeBox.SelectedIndex < 0) return;
        Project.CanvasSize = CursorProject.CanvasSizes[SizeBox.SelectedIndex];
        Touch();
    }

    void OnSpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        double speed = Math.Pow(2, SpeedSlider.Value);
        if (SpeedValue is null) return;
        SpeedValue.Text = $"{speed:0.##}×";
        if (_loadingControls || Project is null) return;
        Project.Speed = speed;
        Touch();
    }

    void OnPreviewClick(object sender, MouseButtonEventArgs e)
    {
        if (Project is not { } p) return;
        var pos = e.GetPosition(ClickSurface);
        double cx = pos.X / PreviewSize * p.CanvasSize, cy = pos.Y / PreviewSize * p.CanvasSize;

        if (WandToggle.IsChecked == true)
        {
            if (!p.MagicWandAtCanvas(_frameIndex, cx, cy, (int)ToleranceSlider.Value)) { Status(Loc.Instance["bg.nothing"]); return; }
        }
        else
        {
            p.SetCanvasHotspot(Math.Floor(cx), Math.Floor(cy));
        }
        Touch();
    }

    void OnHotspotKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnHotspotTyped(sender, e);
    }

    void OnHotspotTyped(object sender, RoutedEventArgs e)
    {
        if (_loadingControls || Project is not { } p) return;
        if (int.TryParse(HotXBox.Text, out int x) && int.TryParse(HotYBox.Text, out int y))
        {
            p.SetCanvasHotspot(Math.Clamp(x, 0, p.CanvasSize - 1), Math.Clamp(y, 0, p.CanvasSize - 1));
            Touch();
        }
        else
        {
            UpdateEditor();
        }
    }

    void OnRemoveEdge(object sender, RoutedEventArgs e)
    {
        if (Project is null) return;
        Project.RemoveEdgeBackground((int)ToleranceSlider.Value);
        Touch();
    }

    void OnWandToggled(object sender, RoutedEventArgs e)
    {
        bool on = WandToggle.IsChecked == true;
        WandHint.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ClickSurface.Cursor = on ? Cursors.Pen : Cursors.Cross;
    }

    void OnToleranceChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ToleranceValue is not null) ToleranceValue.Text = ((int)ToleranceSlider.Value).ToString();
    }

    void OnRevertBackground(object sender, RoutedEventArgs e)
    {
        if (Project is null) return;
        Project.RevertBackground();
        Touch();
    }

    void OnClearRole(object sender, RoutedEventArgs e)
    {
        if (Current is not { } item) return;
        item.Project = null;
        item.PendingDefault = true;
        item.Thumbnail = null;
        item.IsDirty = true;
        Rerender();
    }

    /// <summary>Marks the current role as changed and redraws.</summary>
    void Touch()
    {
        if (Current is { } item) item.IsDirty = true;
        Rerender();
    }

    // ── Windows pointer size ─────────────────────────────────────────────────

    void OnBaseSizeSliding(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BaseSizeValue is not null) BaseSizeValue.Text = $"{(int)BaseSizeSlider.Value} px";
    }

    void OnBaseSizeCommit(object sender, EventArgs e)
    {
        if (_loadingControls) return;
        int size = (int)BaseSizeSlider.Value;
        if (size == CursorSystem.GetBaseSize()) return;
        CursorSystem.SetBaseSize(size);
        Status(Loc.Instance.Format("msg.sizeChanged", size));
    }

    // ── Apply / undo / reset ─────────────────────────────────────────────────

    void OnApply(object sender, RoutedEventArgs e)
    {
        var dirty = _roles.Where(r => r.IsDirty).ToList();
        if (dirty.Count == 0) { Status(Loc.Instance["msg.nothingToApply"]); return; }

        var changes = new Dictionary<CursorRole, string?>();
        foreach (var item in dirty)
        {
            if (item.PendingDefault || item.Project is null) { changes[item.Role] = null; continue; }
            var (data, ext) = item.Project.Encode();
            changes[item.Role] = CursorSystem.StoreCursorFile(item.Role, data, ext);
        }

        CursorSystem.Apply(changes);
        foreach (var item in dirty) { item.PendingDefault = false; item.IsDirty = false; }
        RefreshFromSystem();
        Status(Loc.Instance.Format("msg.applied", dirty.Count));
    }

    void OnUndo(object sender, RoutedEventArgs e)
    {
        if (!CursorSystem.UndoLast()) { Status(Loc.Instance["msg.nothingToUndo"]); return; }
        DiscardEdits();
        Status(Loc.Instance["msg.undone"]);
    }

    void OnResetAll(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, Loc.Instance["msg.confirmReset"], "Purin Pointer", MessageBoxButton.OKCancel,
                            MessageBoxImage.Question) != MessageBoxResult.OK) return;
        CursorSystem.ResetAll();
        DiscardEdits();
        Status(Loc.Instance["msg.reset"]);
    }

    void DiscardEdits()
    {
        foreach (var item in _roles) { item.Project = null; item.PendingDefault = false; item.IsDirty = false; }
        RefreshFromSystem();
        Rerender();
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_roles.Any(r => r.IsDirty) &&
            MessageBox.Show(this, Loc.Instance["msg.unsaved"], "Purin Pointer", MessageBoxButton.YesNo,
                            MessageBoxImage.Warning) != MessageBoxResult.Yes)
            e.Cancel = true;
    }

    void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingControls || LanguageBox.SelectedItem is not Language lang) return;
        Loc.Instance.Code = lang.Code;
        _settings.Language = lang.Code;
        try { _settings.Save(); } catch (IOException) { }
    }

    // ── Preview ──────────────────────────────────────────────────────────────

    /// <summary>Re-renders the current project (scaling, background, speed) and restarts the animation.</summary>
    void Rerender()
    {
        _timer.Stop();
        _frameIndex = 0;
        if (Project is { } p)
        {
            _rendered = p.Render();
            _renderedImages = _rendered.Select(f => ImageLoader.ToBitmapSource(f.Image)).ToList();
            if (Current is { } item) item.Thumbnail = _renderedImages[0];
        }
        else
        {
            _rendered = [];
            _renderedImages = [];
        }
        ShowFrame();
        UpdateEditor();
        if (_rendered.Count > 1) _timer.Start();
    }

    void NextFrame()
    {
        if (_rendered.Count < 2) { _timer.Stop(); return; }
        _frameIndex = (_frameIndex + 1) % _rendered.Count;
        ShowFrame();
    }

    void ShowFrame()
    {
        if (_renderedImages.Count == 0)
        {
            PreviewImage.Source = ActualImage.Source = Current?.PendingDefault == true ? null : Current?.Thumbnail as BitmapSource;
            return;
        }
        PreviewImage.Source = ActualImage.Source = _renderedImages[_frameIndex];
        _timer.Interval = TimeSpan.FromMilliseconds(_rendered[_frameIndex].DelayMs);
    }

    /// <summary>Syncs labels, inputs and the hotspot marker with the current role and project.</summary>
    void UpdateEditor()
    {
        var item = Current;
        var p = item?.Project;
        RoleTitle.Text = item?.Name ?? "";
        ProjectSettings.IsEnabled = p is not null;
        EmptyHint.Visibility = p is null && PreviewImage.Source is null ? Visibility.Visible : Visibility.Collapsed;
        HotspotLayer.Visibility = p is null ? Visibility.Collapsed : Visibility.Visible;
        ClickSurface.IsHitTestVisible = p is not null;

        _loadingControls = true;
        try
        {
            if (p is null)
            {
                SourceInfo.Text = item?.PendingDefault == true ? Loc.Instance["status.pending"] : Loc.Instance["editor.empty"];
                StaticHint.Visibility = Visibility.Collapsed;
                return;
            }

            SourceInfo.Text = Loc.Instance.Format("editor.source", p.SourceName, p.SourceWidth, p.SourceHeight, p.Frames.Count);
            SizeBox.SelectedIndex = Array.IndexOf(CursorProject.CanvasSizes, p.CanvasSize);
            SpeedSlider.Value = Math.Log2(p.Speed);
            SpeedValue.Text = $"{p.Speed:0.##}×";
            SpeedSlider.IsEnabled = p.IsAnimated;
            StaticHint.Visibility = p.IsAnimated ? Visibility.Collapsed : Visibility.Visible;

            var (hx, hy) = p.CanvasHotspot;
            HotXBox.Text = hx.ToString();
            HotYBox.Text = hy.ToString();

            double k = PreviewSize / p.CanvasSize;
            double mx = (hx + 0.5) * k, my = (hy + 0.5) * k;
            HotH.X1 = 0; HotH.X2 = PreviewSize; HotH.Y1 = HotH.Y2 = my;
            HotV.Y1 = 0; HotV.Y2 = PreviewSize; HotV.X1 = HotV.X2 = mx;
            Canvas.SetLeft(HotDot, mx - HotDot.Width / 2);
            Canvas.SetTop(HotDot, my - HotDot.Height / 2);
        }
        finally
        {
            _loadingControls = false;
        }
    }

    void Status(string message) => StatusText.Text = message;
}
