using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PurinPointer;

/// <summary>An Image that plays <see cref="PreviewFrames"/> while it is on screen.</summary>
public sealed class AnimatedImage : Image
{
    public static readonly DependencyProperty FramesProperty = DependencyProperty.Register(
        nameof(Frames), typeof(PreviewFrames), typeof(AnimatedImage),
        new PropertyMetadata(null, (d, _) => ((AnimatedImage)d).Restart()));

    public PreviewFrames? Frames
    {
        get => (PreviewFrames?)GetValue(FramesProperty);
        set => SetValue(FramesProperty, value);
    }

    /// <summary>Index of the frame being shown (the magic wand samples this one).</summary>
    public int FrameIndex { get; private set; }

    readonly DispatcherTimer _timer = new(DispatcherPriority.Render);

    public AnimatedImage()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        _timer.Tick += (_, _) => Next();
        Loaded += (_, _) => Restart();
        Unloaded += (_, _) => _timer.Stop();
    }

    void Restart()
    {
        _timer.Stop();
        FrameIndex = 0;
        var f = Frames;
        Source = f?.Images.FirstOrDefault();
        if (f is { Images.Count: > 1 } && IsLoaded)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(f.Delays[0]);
            _timer.Start();
        }
    }

    void Next()
    {
        var f = Frames;
        if (f is not { Images.Count: > 1 }) { _timer.Stop(); return; }
        FrameIndex = (FrameIndex + 1) % f.Images.Count;
        Source = f.Images[FrameIndex];
        _timer.Interval = TimeSpan.FromMilliseconds(f.Delays[FrameIndex]);
    }
}
