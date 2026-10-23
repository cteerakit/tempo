using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace Tempo.App.Controls;

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>Visible when the value is truthy: true, non-null, non-empty string, non-zero.</summary>
public sealed class TruthyToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var truthy = value switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            int i => i != 0,
            double d => d != 0,
            _ => true,
        };
        return truthy ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Maps a 0–100 value onto a width given as the converter parameter.</summary>
public sealed class PercentToLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double percent && double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var full)
            ? Math.Clamp(percent, 0, 100) / 100 * full
            : 0d;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Attached input helpers for templates: mouse wheel and middle click to commands.</summary>
public static class Input
{
    public static readonly DependencyProperty WheelCommandProperty = DependencyProperty.RegisterAttached(
        "WheelCommand", typeof(ICommand), typeof(Input), new PropertyMetadata(null, OnWheelCommandChanged));

    public static ICommand? GetWheelCommand(DependencyObject d) => (ICommand?)d.GetValue(WheelCommandProperty);

    public static void SetWheelCommand(DependencyObject d, ICommand? value) => d.SetValue(WheelCommandProperty, value);

    private static void OnWheelCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
            return;
        element.MouseWheel -= OnMouseWheel;
        if (e.NewValue is not null)
            element.MouseWheel += OnMouseWheel;
    }

    private static void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (GetWheelCommand((DependencyObject)sender) is { } command && command.CanExecute(e.Delta))
        {
            command.Execute(e.Delta);
            e.Handled = true;
        }
    }

    public static readonly DependencyProperty MiddleClickCommandProperty = DependencyProperty.RegisterAttached(
        "MiddleClickCommand", typeof(ICommand), typeof(Input), new PropertyMetadata(null, OnMiddleClickCommandChanged));

    public static ICommand? GetMiddleClickCommand(DependencyObject d) => (ICommand?)d.GetValue(MiddleClickCommandProperty);

    public static void SetMiddleClickCommand(DependencyObject d, ICommand? value) => d.SetValue(MiddleClickCommandProperty, value);

    public static readonly DependencyProperty MiddleClickParameterProperty = DependencyProperty.RegisterAttached(
        "MiddleClickParameter", typeof(object), typeof(Input));

    public static object? GetMiddleClickParameter(DependencyObject d) => d.GetValue(MiddleClickParameterProperty);

    public static void SetMiddleClickParameter(DependencyObject d, object? value) => d.SetValue(MiddleClickParameterProperty, value);

    private static void OnMiddleClickCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
            return;
        element.MouseUp -= OnMouseUp;
        if (e.NewValue is not null)
            element.MouseUp += OnMouseUp;
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
            return;
        var d = (DependencyObject)sender;
        var parameter = GetMiddleClickParameter(d);
        if (GetMiddleClickCommand(d) is { } command && command.CanExecute(parameter))
        {
            command.Execute(parameter);
            e.Handled = true;
        }
    }
}

/// <summary>Width of a slider's filled track: (value − min) / (max − min) of the usable width.</summary>
public sealed class SliderFillConverter : IMultiValueConverter
{
    private const double ThumbWidth = 16;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not [double value, double min, double max, double width] || max <= min || width <= ThumbWidth)
            return 0d;
        return (Math.Clamp(value, min, max) - min) / (max - min) * (width - ThumbWidth) + ThumbWidth / 2;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
