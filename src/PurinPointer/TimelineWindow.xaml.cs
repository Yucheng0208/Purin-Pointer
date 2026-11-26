using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PurinPointer.Core;

namespace PurinPointer;

/// <summary>
/// AniOnMac's timeline sheet: check the order of several still images (file name 1, 2, 3… by
/// default), drag or nudge frames to reorder, drop unwanted ones, preview, then build the animation.
/// </summary>
public partial class TimelineWindow : Window
{
    readonly Studio _studio;
    readonly List<string> _frames;
    readonly Dictionary<string, BitmapSource?> _thumbs;
    readonly DispatcherTimer _timer = new();
    int _step;
    string? _dragging;

    /// <summary>The chosen frames, in order, when the user pressed "Create animation".</summary>
    public List<string>? Result { get; private set; }

    static string T(string key) => Loc.Instance[key];

    public TimelineWindow(Studio studio, List<string> frames)
    {
        InitializeComponent();
        _studio = studio;
        _frames = frames.ToList();
        _thumbs = _frames.Distinct().ToDictionary(f => f, f => ImageLoader.Thumbnail(f, 168));
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        Title = T("timelineTitle");
        TitleText.Text = T("timelineTitle");
        HintText.Text = T("timelineHint");
        PreviewLabel.Text = T("preview");
        SortText.Text = T("sortByName");
        ReverseText.Text = T("reverseOrder");
        CancelText.Text = T("cancel");
        CreateText.Text = T("createAnimation");
        IntervalBox.ItemsSource = Studio.FrameIntervals.Select(ms => $"{ms} ms").ToList();
        IntervalBox.SelectedIndex = Array.IndexOf(Studio.FrameIntervals, _studio.FrameInterval);
        IntervalBox.ToolTip = T("frameIntervalHint");

        _timer.Tick += (_, _) => { _step++; ShowStep(); };
        Loaded += (_, _) => RestartTimer();
        Closed += (_, _) => _timer.Stop();
        Rebuild();
    }

    void Rebuild()
    {
        FrameStrip.Children.Clear();
        for (int i = 0; i < _frames.Count; i++) FrameStrip.Children.Add(FrameCard(i, _frames[i]));
        CountText.Text = $"{_frames.Count} {T("frames")}";
        CreateButton.IsEnabled = _frames.Count > 0;
        _step = 0;
        ShowStep();
    }

    void ShowStep()
    {
        PreviewImage.Source = _frames.Count == 0 ? null : _thumbs.GetValueOrDefault(_frames[_step % _frames.Count]);
    }

    void RestartTimer()
    {
        _timer.Interval = TimeSpan.FromMilliseconds(_studio.FrameInterval);
        _timer.Start();
    }

    UIElement FrameCard(int index, string path)
    {
        var box = new Grid { Width = 84, Height = 84 };
        var bg = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), ClipToBounds = true };
        bg.SetResourceReference(Border.BackgroundProperty, "Checker");
        bg.SetResourceReference(Border.BorderBrushProperty, "Line");
        bg.Child = new Image { Source = _thumbs.GetValueOrDefault(path), Margin = new Thickness(6) };
        box.Children.Add(bg);

        var badge = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };
        badge.SetResourceReference(Border.BackgroundProperty, "Accent");
        var number = new TextBlock { Text = (index + 1).ToString(), FontSize = 10, FontWeight = FontWeights.Bold };
        number.SetResourceReference(TextBlock.ForegroundProperty, "OnAccent");
        badge.Child = number;
        box.Children.Add(badge);

        var name = new TextBlock
        {
            Text = Path.GetFileName(path), FontSize = 11, Width = 84, TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 6, 0, 0),
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        buttons.Children.Add(CardButton("", T("moveEarlier"), index > 0, () => Move(index, -1)));
        buttons.Children.Add(CardButton("", T("removeFrame"), true, () => { _frames.RemoveAt(index); Rebuild(); }));
        buttons.Children.Add(CardButton("", T("moveLater"), index < _frames.Count - 1, () => Move(index, 1)));

        var card = new StackPanel { Margin = new Thickness(0, 0, 10, 0), AllowDrop = true, Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        card.Children.Add(box);
        card.Children.Add(name);
        card.Children.Add(buttons);

        // Drag a card onto another to move it there (live reordering, as in AniOnMac).
        Point? pressedAt = null;
        card.PreviewMouseLeftButtonDown += (_, e) =>
            pressedAt = IsInsideButton(e.OriginalSource as DependencyObject) ? null : e.GetPosition(card);
        card.MouseMove += (_, e) =>
        {
            if (pressedAt is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            var d = e.GetPosition(card) - start;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            pressedAt = null;
            _dragging = path;
            DragDrop.DoDragDrop(card, path, DragDropEffects.Move);
            _dragging = null;
        };
        card.DragEnter += (_, e) =>
        {
            if (_dragging is null || _dragging == path) return;
            int from = _frames.IndexOf(_dragging), to = _frames.IndexOf(path);
            if (from < 0 || to < 0) return;
            _frames.RemoveAt(from);
            _frames.Insert(to, _dragging);
            Rebuild();
        };
        card.DragOver += (_, e) => { e.Effects = DragDropEffects.Move; e.Handled = true; };
        return card;
    }

    static bool IsInsideButton(DependencyObject? d)
    {
        for (; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is Button) return true;
        return false;
    }

    Button CardButton(string glyph, string tip, bool enabled, Action action)
    {
        var b = new Button { Content = glyph, ToolTip = tip, IsEnabled = enabled, Style = (Style)FindResource("IconButton"), Width = 26, Height = 22, FontSize = 10 };
        b.Click += (_, _) => action();
        return b;
    }

    void Move(int index, int offset)
    {
        int target = index + offset;
        if (target < 0 || target >= _frames.Count) return;
        (_frames[index], _frames[target]) = (_frames[target], _frames[index]);
        Rebuild();
    }

    void OnSort(object sender, RoutedEventArgs e)
    {
        var sorted = ImageLoader.ImageFrames(_frames);
        _frames.Clear();
        _frames.AddRange(sorted);
        Rebuild();
    }

    void OnReverse(object sender, RoutedEventArgs e)
    {
        _frames.Reverse();
        Rebuild();
    }

    void OnIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IntervalBox.SelectedIndex < 0) return;
        _studio.FrameInterval = Studio.FrameIntervals[IntervalBox.SelectedIndex];
        if (IsLoaded) RestartTimer();
    }

    void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    void OnCreate(object sender, RoutedEventArgs e)
    {
        Result = _frames.ToList();
        DialogResult = true;
    }
}
