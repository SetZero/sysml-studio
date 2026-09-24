using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace SysmlStudio.App.Views;

/// <summary>
/// A panel that floats over another part of the window and shows it through
/// frosted glass: behind its children it draws the region of
/// <see cref="Source"/> it covers, blurred, under a translucent
/// <see cref="Tint"/>.
/// </summary>
/// <remarks>
/// Avalonia has no backdrop blur of in-window content, and a
/// <see cref="VisualBrush"/> of a visual that is already in the window paints
/// nothing useful, so the source is rendered into a small bitmap whenever it
/// changes and that is blurred with a <see cref="BlurEffect"/>. Half of
/// the resolution is plenty under a blur, and keeps the snapshot cheap.
/// </remarks>
public sealed class FrostPanel : Panel
{
    public static readonly StyledProperty<Visual?> SourceProperty =
        AvaloniaProperty.Register<FrostPanel, Visual?>(nameof(Source));

    public static readonly StyledProperty<double> BlurRadiusProperty =
        AvaloniaProperty.Register<FrostPanel, double>(nameof(BlurRadius), 8);

    public static readonly StyledProperty<IBrush?> TintProperty =
        AvaloniaProperty.Register<FrostPanel, IBrush?>(nameof(Tint));

    private const double Scale = 0.5;

    private readonly Rectangle _glass = new() { IsHitTestVisible = false };
    private readonly Border _tint = new() { IsHitTestVisible = false };
    private readonly ImageBrush _brush = new() { Stretch = Stretch.Fill, TileMode = TileMode.None };
    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(40);

    private readonly DispatcherTimer _pending = new() { Interval = Every };
    private readonly DispatcherTimer _fallback = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private DateTime _snapped;
    private bool _posted;
    private RenderTargetBitmap? _snapshot;
    private Visual? _watched;

    public FrostPanel()
    {
        ClipToBounds = true;
        _glass.Fill = _brush;
        _glass.Effect = new BlurEffect { Radius = BlurRadius };
        Children.Add(_glass);
        Children.Add(_tint);
        LayoutUpdated += (_, _) => Aim();
        _pending.Tick += (_, _) =>
        {
            _pending.Stop();
            Snap();
        };
        _fallback.Tick += (_, _) => Later();
    }

    /// <summary>What shows through the glass: the element this panel floats over.</summary>
    public Visual? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public double BlurRadius
    {
        get => GetValue(BlurRadiusProperty);
        set => SetValue(BlurRadiusProperty, value);
    }

    /// <summary>The frosting over the blurred source; translucent, or nothing shows through.</summary>
    public IBrush? Tint
    {
        get => GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    /// <summary>The blur spreads the edges of what it blurs, so the glass reaches this far past the panel.</summary>
    private double Bleed => BlurRadius * 2;

    /// <summary>Takes a new snapshot of the source now, instead of at the next change.</summary>
    public void Snap()
    {
        if (Source is not { } source || !IsEffectivelyVisible || source.Bounds.Width < 1 || source.Bounds.Height < 1)
            return;

        var pixels = new PixelSize((int)Math.Ceiling(source.Bounds.Width * Scale), (int)Math.Ceiling(source.Bounds.Height * Scale));
        _snapped = DateTime.UtcNow;
        var snapshot = new RenderTargetBitmap(pixels, new Vector(96 * Scale, 96 * Scale));
        snapshot.Render(source);

        _brush.Source = snapshot;
        _snapshot?.Dispose();
        _snapshot = snapshot;
        Aim();
        _glass.InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            Watch(change.GetNewValue<Visual?>());
        }
        else if (change.Property == BlurRadiusProperty)
        {
            _glass.Effect = new BlurEffect { Radius = BlurRadius };
            InvalidateArrange();
        }
        else if (change.Property == TintProperty)
        {
            _tint.Background = Tint;
        }
        else if (change.Property == IsVisibleProperty && IsVisible)
        {
            Later();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _fallback.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _pending.Stop();
        _fallback.Stop();
        _brush.Source = null;
        _snapshot?.Dispose();
        _snapshot = null;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var rect = ReferenceEquals(child, _glass)
                ? new Rect(-Bleed, -Bleed, finalSize.Width + (2 * Bleed), finalSize.Height + (2 * Bleed))
                : new Rect(finalSize);
            child.Arrange(rect);
        }

        return finalSize;
    }

    /// <summary>Places the snapshot so that the part of the source under the glass lines up with it.</summary>
    private void Aim()
    {
        if (Source is not { } source || this.TranslatePoint(new Point(-Bleed, -Bleed), source) is not { } origin)
            return;

        var placed = new RelativeRect(-origin.X, -origin.Y, source.Bounds.Width, source.Bounds.Height, RelativeUnit.Absolute);
        if (_brush.DestinationRect != placed)
            _brush.DestinationRect = placed;
    }

    /// <summary>
    /// What the user does to the canvas comes through the pointer or through
    /// layout, so those
    /// take a new snapshot, at most one every 40 milliseconds. What the program
    /// does to it without either, such as framing a diagram, is caught by a
    /// slower snapshot twice a second.
    /// </summary>
    private void Watch(Visual? source)
    {
        if (_watched is InputElement old)
        {
            old.RemoveHandler(PointerMovedEvent, OnSourceChanged);
            old.RemoveHandler(PointerWheelChangedEvent, OnSourceChanged);
            old.RemoveHandler(PointerReleasedEvent, OnSourceChanged);
            old.LayoutUpdated -= OnSourceLayout;
        }

        _watched = source;
        if (source is InputElement input)
        {
            const RoutingStrategies routes = RoutingStrategies.Tunnel | RoutingStrategies.Bubble;
            input.AddHandler(PointerMovedEvent, OnSourceChanged, routes, handledEventsToo: true);
            input.AddHandler(PointerWheelChangedEvent, OnSourceChanged, routes, handledEventsToo: true);
            input.AddHandler(PointerReleasedEvent, OnSourceChanged, routes, handledEventsToo: true);
            input.LayoutUpdated += OnSourceLayout;
        }

        Later();
    }

    /// <summary>
    /// A new snapshot once the dispatcher is idle, unless one was taken less
    /// than 40 milliseconds ago; then the timer takes it when that time is up.
    /// </summary>
    private void Later()
    {
        if (_posted || _pending.IsEnabled)
            return;

        if (DateTime.UtcNow - _snapped < Every)
        {
            _pending.Start();
            return;
        }

        _posted = true;
        Dispatcher.UIThread.Post(() =>
        {
            _posted = false;
            Snap();
        }, DispatcherPriority.Background);
    }

    private void OnSourceChanged(object? sender, RoutedEventArgs e) => Later();

    private void OnSourceLayout(object? sender, EventArgs e) => Later();
}
