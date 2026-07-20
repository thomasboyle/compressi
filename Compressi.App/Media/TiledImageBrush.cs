using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Compressi_App.Media;

/// <summary>
/// Tiles an image across any size using Win2D BorderEffect wrap (WinUI ImageBrush cannot repeat).
/// </summary>
public sealed class TiledImageBrush : XamlCompositionBrushBase
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source),
        typeof(Uri),
        typeof(TiledImageBrush),
        new PropertyMetadata(null, OnSourceChanged));

    private LoadedImageSurface? _surface;
    private CompositionSurfaceBrush? _surfaceBrush;
    private CompositionEffectBrush? _effectBrush;

    public Uri? Source
    {
        get => (Uri?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((TiledImageBrush)d).Rebuild();
    }

    protected override void OnConnected()
    {
        Rebuild();
    }

    protected override void OnDisconnected()
    {
        TearDown();
    }

    private void Rebuild()
    {
        TearDown();

        if (Source is null)
        {
            return;
        }

        var compositor = CompositionTarget.GetCompositorForCurrentThread();
        _surface = LoadedImageSurface.StartLoadFromUri(Source);
        _surface.LoadCompleted += OnSurfaceLoadCompleted;

        if (_surface.DecodedPhysicalSize.Width > 0 && _surface.DecodedPhysicalSize.Height > 0)
        {
            ApplySurface(compositor);
        }
    }

    private void OnSurfaceLoadCompleted(LoadedImageSurface sender, LoadedImageSourceLoadCompletedEventArgs args)
    {
        if (args.Status != LoadedImageSourceLoadStatus.Success || !ReferenceEquals(sender, _surface))
        {
            return;
        }

        ApplySurface(CompositionTarget.GetCompositorForCurrentThread());
    }

    private void ApplySurface(Compositor compositor)
    {
        if (_surface is null)
        {
            return;
        }

        _surfaceBrush?.Dispose();
        _effectBrush?.Dispose();
        CompositionBrush = null;

        _surfaceBrush = compositor.CreateSurfaceBrush(_surface);
        _surfaceBrush.Stretch = CompositionStretch.None;
        _surfaceBrush.HorizontalAlignmentRatio = 0f;
        _surfaceBrush.VerticalAlignmentRatio = 0f;
        _surfaceBrush.SnapToPixels = true;

        using var borderEffect = new BorderEffect
        {
            ExtendX = Microsoft.Graphics.Canvas.CanvasEdgeBehavior.Wrap,
            ExtendY = Microsoft.Graphics.Canvas.CanvasEdgeBehavior.Wrap,
            Source = new CompositionEffectSourceParameter("source"),
        };

        _effectBrush = compositor.CreateEffectFactory(borderEffect).CreateBrush();
        _effectBrush.SetSourceParameter("source", _surfaceBrush);
        CompositionBrush = _effectBrush;
    }

    private void TearDown()
    {
        if (_surface is not null)
        {
            _surface.LoadCompleted -= OnSurfaceLoadCompleted;
            _surface.Dispose();
            _surface = null;
        }

        CompositionBrush = null;
        _effectBrush?.Dispose();
        _effectBrush = null;
        _surfaceBrush?.Dispose();
        _surfaceBrush = null;
    }
}
