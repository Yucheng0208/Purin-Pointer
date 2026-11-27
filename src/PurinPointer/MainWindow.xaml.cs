using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PurinPointer.Core;
using Microsoft.Win32;

namespace PurinPointer;

/// <summary>The main window, laid out like AniOnMac's ContentView: sidebar with themes,
/// header, four mode tabs, the editor card and the footer toolbar.</summary>
public partial class MainWindow : Window
{
    public static readonly RoutedCommand ApplyCommand = new();

    readonly Studio _studio = new();
    readonly List<(string Key, ToggleButton Button, AnimatedImage Image, TextBlock Icon, TextBlock Label)> _tabs = [];
    bool _loading;
    bool _dragging;
    string _shownHotX = "", _shownHotY = "";
    string _sidebarSignature = "";
    /// <summary>Show the preview enlarged (default) instead of at the real pointer size.</summary>
    bool _zoomed = true;
    const double EnlargedSide = 240;

    static readonly Dictionary<string, string> ModeGlyphs = new()
    {
        [CursorModes.Arrow] = "", [CursorModes.Pointing] = "", [CursorModes.Text] = "", [CursorModes.Wait] = "",
    };

    static string T(string key) => Loc.Instance[key];

    public MainWindow()
    {
        InitializeComponent();
        // Fit smaller or highly scaled screens (e.g. a laptop at 175 %) instead of running off the bottom.
        var work = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, work.Width - 16);
        MinHeight = Math.Min(MinHeight, work.Height - 16);
        Width = Math.Min(Width, work.Width - 16);
        Height = Math.Min(Height, work.Height - 16);
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        foreach (var key in CursorModes.Main) _tabs.Add(MakeTab(key));
        IntervalBox.ItemsSource = Studio.FrameIntervals.Select(ms => $"{ms} ms").ToList();
        VersionText.Text = "v" + UpdateChecker.AppVersion;

        _studio.Changed += Refresh;
        _studio.Failed += message => MessageBox.Show(this, T("errorDetail") + "\n\n" + message, T("errorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        _studio.TimelineRequested += OpenTimeline;
        Loc.Instance.PropertyChanged += (_, _) => { _sidebarSignature = ""; Refresh(); };

        _studio.Load();
        Refresh();
    }

    (string, ToggleButton, AnimatedImage, TextBlock, TextBlock) MakeTab(string key)
    {
        var image = new AnimatedImage { Width = 28, Height = 28 };
        var icon = new TextBlock
        {
            Text = ModeGlyphs[key], FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            FontFamily = (FontFamily)FindResource("IconFont"),
        };
        var label = new TextBlock { FontWeight = FontWeights.Medium, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
        var glyph = new Grid { Width = 32, Height = 32 };
        glyph.Children.Add(image);
        glyph.Children.Add(icon);
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(glyph);
        content.Children.Add(label);
        var button = new ToggleButton
        {
            Style = (Style)FindResource("RowToggle"), Padding = new Thickness(6, 9, 6, 9), Margin = new Thickness(0, 0, 8, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center, Content = content,
        };
        button.Click += (_, _) => { PathBox.Text = ""; _studio.SetMode(key); };
        ModeTabs.Children.Add(button);
        return (key, button, image, icon, label);
    }

    // ── Redraw ───────────────────────────────────────────────────────────────

    void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        if (_studio.Library.Current is null) return; // still loading
        _loading = true;
        try
        {
            RefreshSidebar();
            RefreshHeader();
            RefreshTabs();
            RefreshEditor();
            RefreshFooter();
            BusyOverlay.Visibility = _studio.Busy ? Visibility.Visible : Visibility.Collapsed;
            BusyText.Text = T("processing");
            Root.IsEnabled = !_studio.Busy;
        }
        finally { _loading = false; }
    }

    void RefreshSidebar()
    {
        var lib = _studio.Library;
        MyThemesText.Text = T("myThemes");
        ThemeCountText.Text = lib.Profiles.Count.ToString(CultureInfo.InvariantCulture);
        AddThemeButton.ToolTip = T("addTheme");
        CopyThemeButton.ToolTip = T("duplicate");
        DeleteThemeButton.ToolTip = T("delete");
        DeleteThemeButton.IsEnabled = lib.Profiles.Count > 1;
        AutoSaveText.Text = T("autoSave");
        AutoSaveRow.ToolTip = T("copyHint").Replace("\n", "，");
        AboutText.Text = T("aboutShort");
        UpdateText.Text = T("checkUpdates");
        UpdateButton.IsEnabled = !_studio.CheckingUpdates;

        // Rebuild the rows only when something they show changed, so thumbnails keep animating.
        var signature = string.Join("|", lib.Profiles.Select(p =>
            $"{p.Id}:{p.Name}:{p.FilledMain}:{p.FilledExtra}:{p.Slots.FirstOrDefault(s => !s.IsEmpty)?.Asset}")) + $"#{lib.Selected}#{lib.InUse}#{Loc.Instance.Code}";
        if (signature == _sidebarSignature) return;
        _sidebarSignature = signature;

        ThemeList.Children.Clear();
        for (int i = 0; i < lib.Profiles.Count; i++) ThemeList.Children.Add(ThemeRow(lib.Profiles[i], i + 1));
    }

    UIElement ThemeRow(Profile p, int number)
    {
        bool selected = p.Id == _studio.Library.Selected;
        bool inUse = p.Id == _studio.Library.InUse;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(43) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var num = new TextBlock
        {
            Text = number.ToString("00", CultureInfo.InvariantCulture), FontSize = 10, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
        };
        num.SetResourceReference(TextBlock.ForegroundProperty, selected ? "Accent" : "Muted");
        grid.Children.Add(num);

        var thumbBox = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(8) };
        thumbBox.SetResourceReference(Border.BackgroundProperty, "Card");
        if (_studio.ThemeThumbnail(p) is { } frames)
            thumbBox.Child = new AnimatedImage { Frames = frames, Width = 24, Height = 24, IsHitTestVisible = false };
        else
        {
            var glyph = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("IconFont"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            thumbBox.Child = glyph;
        }
        Grid.SetColumn(thumbBox, 1);
        grid.Children.Add(thumbBox);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0) };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(p.Name) ? T("untitled") : p.Name, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, MaxWidth = inUse ? 90 : 140,
        });
        if (inUse)
        {
            var tag = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            tag.SetResourceReference(Border.BackgroundProperty, "Accent");
            var tagText = new TextBlock { Text = T("inUse"), FontSize = 9, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.NoWrap };
            tagText.SetResourceReference(TextBlock.ForegroundProperty, "OnAccent");
            tag.Child = tagText;
            nameRow.Children.Add(tag);
        }
        text.Children.Add(nameRow);
        var summary = $"{p.FilledMain} / 4 · {T("modes")}" + (p.FilledExtra > 0 ? $" · +{p.FilledExtra}" : "");
        text.Children.Add(new TextBlock { Text = summary, Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.NoWrap });
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);

        var row = new ToggleButton
        {
            Style = (Style)FindResource("RowToggle"), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 6),
            IsChecked = selected, Content = grid, ToolTip = inUse ? T("inUseHint") : null,
        };
        var id = p.Id;
        row.Click += (_, _) => { PathBox.Text = ""; _studio.Select(id); };
        return row;
    }

    void RefreshHeader()
    {
        if (!ThemeNameBox.IsKeyboardFocused && ThemeNameBox.Text != _studio.Profile.Name) ThemeNameBox.Text = _studio.Profile.Name;
        ThemeNameBox.ToolTip = T("themeName");
        SubtitleText.Text = T("subtitle");
        AppearanceText.Text = T("appearance_" + _studio.Library.Appearance);
        AppearanceButton.ToolTip = T("appearance");
        LanguageText.Text = _studio.Library.Language == "system" ? T("languageSystem") : T("languageName");
        LanguageButton.ToolTip = T("language");
    }

    void RefreshTabs()
    {
        foreach (var (key, button, image, icon, label) in _tabs)
        {
            button.IsChecked = _studio.Mode == key;
            label.Text = T(key);
            var frames = _studio.Preview(_studio.Profile.Slot(key));
            if (!ReferenceEquals(image.Frames, frames)) image.Frames = frames;
            icon.Visibility = frames is null ? Visibility.Visible : Visibility.Collapsed;
        }
        bool extra = !CursorModes.IsMain(_studio.Mode);
        MoreButton.IsChecked = extra;
        MoreText.Text = extra ? T(CursorModes.NameKey(_studio.Mode)) : T("moreRoles");
        var moreFrames = extra ? _studio.Preview(_studio.Slot) : null;
        if (!ReferenceEquals(MoreImage.Frames, moreFrames)) MoreImage.Frames = moreFrames;
        MoreIcon.Visibility = moreFrames is null ? Visibility.Visible : Visibility.Collapsed;
    }

    void RefreshEditor()
    {
        var slot = _studio.Slot;
        bool has = !slot.IsEmpty;
        var preview = _studio.Preview(slot);

        ImportHeading.Text = T("importHeading");
        ClearText.Text = T("clearMode");
        ClearButton.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        DropHint.Text = T("dropHint");
        DropDetail.Text = has ? slot.Filename : T("animationHint");
        ChooseText.Text = T("chooseFile");
        IntervalLabel.Text = T("frameInterval");
        IntervalBox.ToolTip = T("frameIntervalHint");
        IntervalBox.SelectedIndex = Array.IndexOf(Studio.FrameIntervals, _studio.FrameInterval);
        PathPlaceholder.Text = T("pathPlaceholder");
        PathPlaceholder.Visibility = PathBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ImportText.Text = T("import");
        PathImportButton.IsEnabled = PathBox.Text.Trim().Length > 0;

        int baseSize = _studio.BaseSize;
        SizeHeading.Text = T("sizeHeading");
        SizeValue.Text = $"{baseSize} px";
        if (!SizeSlider.IsMouseCaptureWithin) SizeSlider.Value = baseSize;
        ResetSizeText.Text = T("resetSize");
        SizeHint.Text = T("sizeHint");
        var style = CursorStyles.Of(slot);
        StyleLabel.Text = T("styleHeading");
        StyleSmoothText.Text = T("style.smooth");
        StylePixelText.Text = T("style.pixel");
        StyleJaggedText.Text = T("style.jagged");
        StyleSmooth.IsChecked = style == CursorStyles.Smooth;
        StylePixel.IsChecked = style == CursorStyles.Pixel;
        StyleJagged.IsChecked = style == CursorStyles.Jagged;
        StyleButtons.IsEnabled = has;
        StyleHint.Text = T("styleHint." + style);

        HotspotHeading.Text = T("hotspotHeading");
        ResetHotspotText.Text = T("resetHotspot");
        ResetHotspotButton.IsEnabled = has && slot.Hotspot is not null;
        CalibrateText.Text = T("calibrate");
        CalibrateButton.ToolTip = T("calibrateTip");
        CalibrateButton.IsEnabled = has && preview is not null;
        HotspotInputs.IsEnabled = has;
        var (w, h) = DisplaySize(preview, baseSize, actual: true);
        var (hx, hy) = _studio.HotspotOf(slot);
        _shownHotX = has ? Math.Round(hx * w, 1).ToString(CultureInfo.CurrentCulture) : "0";
        _shownHotY = has ? Math.Round(hy * h, 1).ToString(CultureInfo.CurrentCulture) : "0";
        if (!HotXBox.IsKeyboardFocused) HotXBox.Text = _shownHotX;
        if (!HotYBox.IsKeyboardFocused) HotYBox.Text = _shownHotY;
        HotspotHint.Text = T("hotspotHint");

        BackgroundHeading.Text = T("backgroundHeading");
        RestoreOriginalText.Text = T("restoreOriginal");
        RestoreOriginalButton.Visibility = slot.OriginalAsset is not null ? Visibility.Visible : Visibility.Collapsed;
        BackgroundTools.IsEnabled = has;
        ToleranceRow.IsEnabled = has;
        AutoRemoveText.Text = T("autoRemove");
        WandText.Text = T("magicWand");
        WandToggle.IsChecked = _studio.WandActive;
        ToleranceLabel.Text = T("tolerance");
        ToleranceSlider.Value = _studio.Tolerance;
        ToleranceValue.Text = ((int)_studio.Tolerance).ToString(CultureInfo.InvariantCulture);
        BackgroundHint.Text = T(_studio.WandActive ? "wandActiveHint" : "backgroundHint");
        ModeHint.Text = _studio.Mode switch { CursorModes.Pointing => T("pointingHint"), CursorModes.Wait => T("waitHint"), _ => "" };
        ModeHint.Visibility = ModeHint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        PreviewHeading.Text = T("preview");
        bool zoomed = _zoomed || _studio.WandActive;
        ZoomToggle.IsChecked = zoomed;
        ZoomToggle.IsEnabled = !_studio.WandActive;
        ZoomToggle.ToolTip = T("zoomHint");
        ZoomIcon.Text = zoomed ? "" : ""; // zoom / full screen
        ActualSizeText.Text = zoomed ? T("enlarged") : T("actualSize");
        ShowHotspotText.Text = T("showHotspot");
        PreviewHint.Text = (zoomed ? T("enlargedHint") : T("previewHint")) + "\n" + T("markerHint");
        NoCursorText.Text = T("noCursor");
        RefreshPreview(slot, preview, baseSize);
    }

    /// <summary>The preview's on-screen size: the Windows pointer size (1:1, or <paramref name="actual"/>),
    /// or enlarged to at least 240 so it is easy to see and to place the hotspot.</summary>
    (double W, double H) DisplaySize(PreviewFrames? p, int baseSize, bool actual = false)
    {
        bool zoomed = !actual && (_zoomed || _studio.WandActive);
        double side = zoomed ? Math.Max(EnlargedSide, baseSize) : baseSize;
        if (p is null) return (side, side);
        double max = Math.Max(p.Width, p.Height);
        return (side * p.Width / max, side * p.Height / max);
    }

    void RefreshPreview(Slot slot, PreviewFrames? preview, int baseSize)
    {
        bool wand = _studio.WandActive;
        NoCursorText.Visibility = preview is null ? Visibility.Visible : Visibility.Collapsed;
        PreviewHost.Visibility = preview is null ? Visibility.Collapsed : Visibility.Visible;
        PreviewChecker.Visibility = wand && preview is not null ? Visibility.Visible : Visibility.Collapsed;
        if (preview is null) { PreviewImage.Frames = null; return; }

        var (w, h) = DisplaySize(preview, baseSize);
        PreviewHost.Width = PreviewImage.Width = w;
        PreviewHost.Height = PreviewImage.Height = h;

        // Pixel and jagged styles: show what Windows will draw (the pointer at its real pixel size),
        // enlarged without smoothing so the blocks and hard edges are visible.
        var shown = preview;
        if (!wand)
        {
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var (aw, ah) = DisplaySize(preview, baseSize, actual: true);
            shown = _studio.StyledPreview(slot, (int)Math.Round(aw * dpi), (int)Math.Round(ah * dpi), CursorOps.PixelBlock(dpi)) ?? preview;
        }
        if (!ReferenceEquals(PreviewImage.Frames, shown)) PreviewImage.Frames = shown;
        bool hard = wand || !ReferenceEquals(shown, preview);
        RenderOptions.SetBitmapScalingMode(PreviewImage, hard ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        PreviewHost.Cursor = wand ? Cursors.Cross : Cursors.Hand;

        var (hx, hy) = _studio.HotspotOf(slot);
        double x = hx * w, y = hy * h;
        HotCross.Data = Geometry.Parse(FormattableString.Invariant($"M{x - 5},{y} L{x + 5},{y} M{x},{y - 5} L{x},{y + 5}"));
        HotMarker.Visibility = ShowHotspotBox.IsChecked == true && !wand ? Visibility.Visible : Visibility.Collapsed;
    }

    void RefreshFooter()
    {
        StatusText.Text = _studio.StatusText;
        StatusText.ToolTip = _studio.StatusText;
        StatusIcon.Text = _studio.StatusIsWarning ? "" : _studio.StatusIsSuccess ? "" : "";
        StatusIcon.SetResourceReference(TextBlock.ForegroundProperty, _studio.StatusIsWarning ? "Warning" : "Accent");
        NativeText.Text = T("nativeReset");
        NativeButton.ToolTip = T("nativeHint");
        UndoText.Text = T("undo");
        UndoButton.IsEnabled = _studio.CanUndo;
        ApplyText.Text = T(_studio.IsApplied ? "alreadyApplied" : "apply");
        ApplyButton.IsEnabled = _studio.CanApply;
        ApplyButton.ToolTip = "Ctrl + Enter";
    }

    // ── Sidebar actions ──────────────────────────────────────────────────────

    void OnAddTheme(object sender, RoutedEventArgs e) => _studio.AddTheme(copy: false);
    void OnCopyTheme(object sender, RoutedEventArgs e) => _studio.AddTheme(copy: true);

    void OnDeleteTheme(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, T("deleteMessage"), T("deleteTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
            _studio.RemoveTheme();
    }

    void OnAbout(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this, $"Purin Pointer v{UpdateChecker.AppVersion}\n\n{T("aboutDescription")}\n\n{T("authorLine")}\n{UpdateChecker.ProjectUrl}",
                        T("about"), MessageBoxButton.OK, MessageBoxImage.Information);

    void OnGitHub(object sender, RoutedEventArgs e) => UpdateChecker.Open(UpdateChecker.ProjectUrl);

    async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _studio.CheckForUpdates();
            if (result is null) return;
            var (outcome, tag) = result.Value;
            switch (outcome)
            {
                case UpdateChecker.Outcome.UpdateAvailable:
                    if (MessageBox.Show(this, $"{T("updateCurrent")} {UpdateChecker.AppVersion}", $"{T("updateAvailable")} {tag}",
                                        MessageBoxButton.OKCancel, MessageBoxImage.Information) == MessageBoxResult.OK)
                        UpdateChecker.Open(UpdateChecker.ReleasesUrl);
                    break;
                case UpdateChecker.Outcome.UpToDate:
                    MessageBox.Show(this, $"{T("updateCurrent")} {UpdateChecker.AppVersion}", T("updateLatest"), MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                default:
                    if (MessageBox.Show(this, T("updateNoRelease"), T("updateNone"), MessageBoxButton.OKCancel, MessageBoxImage.Information) == MessageBoxResult.OK)
                        UpdateChecker.Open(UpdateChecker.ProjectUrl);
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, T("updateFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ── Header ───────────────────────────────────────────────────────────────

    void OnThemeNameChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading && _studio.Ready) _studio.Rename(ThemeNameBox.Text);
    }

    void OnOpenMenu(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var menu = button.ContextMenu!;
        menu.Items.Clear();
        if (button == AppearanceButton)
        {
            foreach (var mode in Library.AppearanceModes)
                menu.Items.Add(MenuItem(T("appearance_" + mode), _studio.Library.Appearance == mode, () => _studio.SetAppearance(mode)));
        }
        else
        {
            menu.Items.Add(MenuItem(T("languageSystem"), _studio.Library.Language == "system", () => _studio.SetLanguage("system")));
            menu.Items.Add(new Separator { Margin = new Thickness(6, 4, 6, 4) });
            foreach (var lang in Loc.Languages)
                menu.Items.Add(MenuItem(lang.NativeName, _studio.Library.Language == lang.Code, () => _studio.SetLanguage(lang.Code)));
        }
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    static MenuItem MenuItem(string header, bool isChecked, Action action)
    {
        var item = new MenuItem { Header = header, IsChecked = isChecked };
        item.Click += (_, _) => action();
        return item;
    }

    // ── Mode tabs ────────────────────────────────────────────────────────────

    void OnMoreClicked(object sender, RoutedEventArgs e)
    {
        MoreMenu.Items.Clear();
        foreach (var key in CursorModes.Extra)
        {
            var filled = !_studio.Profile.Slot(key).IsEmpty;
            MoreMenu.Items.Add(MenuItem(T(CursorModes.NameKey(key)) + (filled ? "  ●" : ""), _studio.Mode == key,
                                        () => { PathBox.Text = ""; _studio.SetMode(key); }));
        }
        MoreMenu.PlacementTarget = MoreButton;
        MoreMenu.Placement = PlacementMode.Bottom;
        MoreMenu.IsOpen = true;
    }

    void OnMoreMenuClosed(object sender, RoutedEventArgs e) => Refresh();

    // ── Import ───────────────────────────────────────────────────────────────

    void OnChooseFile(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        menu.Items.Add(MenuItem(T("chooseFiles"), false, () =>
        {
            var dialog = new OpenFileDialog { Multiselect = true, Filter = ImageLoader.DialogFilter(T("chooseHint")), Title = T("chooseHint") };
            if (dialog.ShowDialog(this) == true) _studio.ImportItems(dialog.FileNames);
        }));
        menu.Items.Add(MenuItem(T("chooseFolder"), false, () =>
        {
            var dialog = new OpenFolderDialog { Title = T("chooseHint") };
            if (dialog.ShowDialog(this) == true) _studio.ImportItems([dialog.FolderName]);
        }));
        menu.IsOpen = true;
    }

    void OnIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && IntervalBox.SelectedIndex >= 0) _studio.FrameInterval = Studio.FrameIntervals[IntervalBox.SelectedIndex];
    }

    void OnDropZoneDragEnter(object sender, DragEventArgs e) => DropZone.SetResourceReference(Border.BackgroundProperty, "Hover");
    void OnDropZoneDragLeave(object sender, DragEventArgs e) => DropZone.SetResourceReference(Border.BackgroundProperty, "Cream");

    void OnDropZoneDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void OnDropZoneDrop(object sender, DragEventArgs e)
    {
        DropZone.SetResourceReference(Border.BackgroundProperty, "Cream");
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0) _studio.ImportItems(paths);
    }

    void OnPathChanged(object sender, TextChangedEventArgs e)
    {
        PathPlaceholder.Visibility = PathBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        PathImportButton.IsEnabled = PathBox.Text.Trim().Length > 0;
    }

    void OnPathKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnImportPath(sender, e);
    }

    void OnImportPath(object sender, RoutedEventArgs e)
    {
        var input = PathBox.Text.Trim().Trim('"');
        if (input.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(input, UriKind.Absolute, out var uri)) input = uri.LocalPath;
        input = Environment.ExpandEnvironmentVariables(input);
        if (input.Length == 0) return;
        if (!File.Exists(input) && !Directory.Exists(input))
        {
            MessageBox.Show(this, T("errorDetail") + "\n\n" + T("chooseANI") + "\n" + input, T("errorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _studio.ImportItems([input]);
    }

    void OnClearSlot(object sender, RoutedEventArgs e) => _studio.ClearSlot();

    void OpenTimeline(List<string> frames)
    {
        var timeline = new TimelineWindow(_studio, frames) { Owner = this };
        if (timeline.ShowDialog() == true && timeline.Result is { Count: > 0 } chosen) _studio.ImportFrames(chosen);
        else Refresh();
    }

    // ── Size ─────────────────────────────────────────────────────────────────

    void OnSizeSliding(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SizeValue is not null) SizeValue.Text = $"{(int)SizeSlider.Value} px";
    }

    void OnSizeCommit(object sender, EventArgs e)
    {
        if (_loading) return;
        int size = (int)SizeSlider.Value;
        if (size != _studio.BaseSize) RunSafely(() => _studio.SetBaseSize(size));
    }

    void OnResetSize(object sender, RoutedEventArgs e) => RunSafely(() => _studio.SetBaseSize(32));

    // ── Drawing style ────────────────────────────────────────────────────────

    void OnStyleClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string style }) RunSafely(() => _studio.SetStyle(style));
        else Refresh();
    }

    // ── Hotspot ──────────────────────────────────────────────────────────────

    void OnHotspotKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnHotspotTyped(sender, e);
    }

    void OnHotspotTyped(object sender, RoutedEventArgs e)
    {
        if (_loading || _studio.Slot.IsEmpty) return;
        if (HotXBox.Text == _shownHotX && HotYBox.Text == _shownHotY) return; // unchanged: keep the file's own hotspot
        var (w, h) = DisplaySize(_studio.Preview(_studio.Slot), _studio.BaseSize, actual: true);
        if (double.TryParse(HotXBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var x) &&
            double.TryParse(HotYBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var y) &&
            double.IsFinite(x) && double.IsFinite(y))
            _studio.SetHotspot([x / w, y / h]);
        else Refresh();
    }

    void OnResetHotspot(object sender, RoutedEventArgs e) => _studio.SetHotspot(null);

    void OnCalibrate(object sender, RoutedEventArgs e)
    {
        if (_studio.Preview(_studio.Slot) is not { } preview) return;
        var slot = _studio.Slot;
        var window = new CalibrationWindow(slot, preview.Cursor, _studio.HotspotOf(slot), _studio.BaseSize) { Owner = this };
        if (window.ShowDialog() == true && window.Result is { } hot)
        {
            _studio.SetHotspot([hot.X, hot.Y]);
            _studio.SetStatus("calibrateDone");
        }
    }

    void OnShowHotspotChanged(object sender, RoutedEventArgs e) => Refresh();

    void OnZoomClicked(object sender, RoutedEventArgs e)
    {
        _zoomed = !_zoomed;
        Refresh();
    }

    // Drag the hotspot on the preview; with the magic wand on, a click removes that colour instead.
    void OnPreviewDown(object sender, MouseButtonEventArgs e)
    {
        if (_studio.Slot.IsEmpty || _studio.WandActive) return;
        _dragging = true;
        PreviewHost.CaptureMouse();
        MoveHotspot(e.GetPosition(PreviewHost), save: false);
    }

    void OnPreviewMove(object sender, MouseEventArgs e)
    {
        if (_dragging) MoveHotspot(e.GetPosition(PreviewHost), save: false);
    }

    void OnPreviewUp(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(PreviewHost);
        if (_studio.WandActive)
        {
            if (!_studio.Slot.IsEmpty)
                _studio.Knockout((Math.Clamp(pos.X / PreviewHost.ActualWidth, 0, 1), Math.Clamp(pos.Y / PreviewHost.ActualHeight, 0, 1)));
            return;
        }
        if (!_dragging) return;
        _dragging = false;
        PreviewHost.ReleaseMouseCapture();
        MoveHotspot(pos, save: true);
    }

    void MoveHotspot(Point pos, bool save)
    {
        double x = Math.Clamp(pos.X / PreviewHost.ActualWidth, 0, 0.999999);
        double y = Math.Clamp(pos.Y / PreviewHost.ActualHeight, 0, 0.999999);
        if (save) { _studio.SetHotspot([x, y]); return; }
        _studio.Slot.Hotspot = [x, y];
        _loading = true;
        try { RefreshEditor(); } finally { _loading = false; }
    }

    // ── Background ───────────────────────────────────────────────────────────

    void OnAutoRemove(object sender, RoutedEventArgs e)
    {
        _studio.WandActive = false;
        _studio.Knockout(null);
    }

    void OnWandClicked(object sender, RoutedEventArgs e)
    {
        _studio.WandActive = !_studio.WandActive;
        Refresh();
    }

    void OnToleranceChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _studio.Tolerance = Math.Round(ToleranceSlider.Value);
        if (ToleranceValue is not null) ToleranceValue.Text = ((int)_studio.Tolerance).ToString(CultureInfo.InvariantCulture);
    }

    void OnRestoreOriginal(object sender, RoutedEventArgs e) => _studio.RestoreOriginal();

    // ── Footer ───────────────────────────────────────────────────────────────

    void OnRestoreNative(object sender, RoutedEventArgs e) => _studio.RestoreNative();
    void OnUndo(object sender, RoutedEventArgs e) => _studio.Restore();
    void OnApply(object sender, RoutedEventArgs e) => _studio.Apply();

    void RunSafely(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, T("errorDetail") + "\n\n" + ex.Message, T("errorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
