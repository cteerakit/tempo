using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Tempo.App.Controls;

public static class ContextMenuHighlight
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ContextMenuHighlight), new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.RegisterAttached(
        "IsOpen", typeof(bool), typeof(ContextMenuHighlight), new PropertyMetadata(false));

    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);
    public static bool GetIsOpen(DependencyObject target) => (bool)target.GetValue(IsOpenProperty);
    public static void SetIsOpen(DependencyObject target, bool value) => target.SetValue(IsOpenProperty, value);

    private static void OnIsEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not FrameworkElement element)
            return;
        if ((bool)e.NewValue)
            element.ContextMenuOpening += OnContextMenuOpening;
        else
        {
            element.ContextMenuOpening -= OnContextMenuOpening;
            BindingOperations.ClearBinding(element, IsOpenProperty);
            element.ClearValue(IsOpenProperty);
        }
    }

    private static void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var element = (FrameworkElement)sender;
        for (DependencyObject? ancestor = element; ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
        {
            if (ancestor is not FrameworkElement { ContextMenu: { } menu })
                continue;
            BindingOperations.SetBinding(element, IsOpenProperty, new Binding(nameof(ContextMenu.IsOpen)) { Source = menu });
            menu.Closed += OnClosed;
            break;

            void OnClosed(object sender, RoutedEventArgs args)
            {
                menu.Closed -= OnClosed;
                BindingOperations.ClearBinding(element, IsOpenProperty);
                element.ClearValue(IsOpenProperty);
            }
        }
    }
}
