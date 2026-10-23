using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Tempo.App.Widgets;

namespace Tempo.App.Controls;

public sealed class AppsPanel : StackPanel
{
    private Dictionary<UIElement, double> _positions = new();

    public AppsPanel() => Orientation = Orientation.Horizontal;

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var visiblePositions = _positions.ToDictionary(pair => pair.Key,
            pair => pair.Value + ((pair.Key.RenderTransform as TranslateTransform)?.X ?? 0));
        var result = base.ArrangeOverride(arrangeSize);
        var positions = new Dictionary<UIElement, double>();
        foreach (UIElement child in InternalChildren)
        {
            var x = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot((FrameworkElement)child).X;
            positions[child] = x;
            if (!visiblePositions.TryGetValue(child, out var previous))
                continue;
            var transform = child.RenderTransform as TranslateTransform;
            if (transform is null)
                child.RenderTransform = transform = new TranslateTransform();
            if (Math.Abs(_positions[child] - x) < .01)
                continue;
            var offset = previous - x;
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = offset;
            if (((FrameworkElement)child).DataContext is not (AppEntry { IsDragging: true } or WidgetViewModelBase { IsDragging: true }))
                Settle(transform);
        }
        _positions = positions;
        return result;
    }

    internal static void Settle(TranslateTransform transform)
    {
        var from = transform.X;
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = 0;
        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, 0,
            TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        });
    }
}

