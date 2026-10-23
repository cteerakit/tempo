using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace Tempo.App.Settings;

/// <summary>
/// Builds an editor for a widget's settings object by reflection:
/// bool → toggle, enum → combo box, number with [Range] → slider, number/string → text box.
/// Collections are skipped (they're managed by the widget itself, e.g. Apps pins).
/// </summary>
internal static class WidgetSettingsEditor
{
    public static UIElement Build(object settings, Action<object> onChanged)
    {
        var panel = new StackPanel();
        var properties = settings.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, CanWrite: true } && !IsCollection(p.PropertyType))
            .ToList();

        if (properties.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "This widget has no settings.", Opacity = 0.7 });
            return panel;
        }

        foreach (var property in properties)
        {
            var label = property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? property.Name;
            var help = property.GetCustomAttribute<DescriptionAttribute>()?.Description;
            var control = CreateControl(settings, property, label, () => onChanged(settings));
            if (control is null)
                continue;

            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            if (control is not CheckBox)
                row.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });
            row.Children.Add(control);
            if (help is not null)
                row.Children.Add(new TextBlock { Text = help, Opacity = 0.65, FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(row);
        }
        return panel;
    }

    private static FrameworkElement? CreateControl(object settings, PropertyInfo property, string label, Action changed)
    {
        var type = property.PropertyType;
        var value = property.GetValue(settings);

        if (type == typeof(bool))
        {
            var box = new CheckBox { Content = label, IsChecked = (bool)value! };
            RoutedEventHandler update = (_, _) =>
            {
                property.SetValue(settings, box.IsChecked == true);
                changed();
            };
            box.Checked += update;
            box.Unchecked += update;
            return box;
        }

        if (type.IsEnum)
        {
            var combo = new ComboBox { ItemsSource = Enum.GetValues(type), SelectedItem = value, MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left };
            combo.SelectionChanged += (_, _) =>
            {
                property.SetValue(settings, combo.SelectedItem);
                changed();
            };
            return combo;
        }

        if (type == typeof(double) || type == typeof(int))
        {
            if (property.GetCustomAttribute<RangeAttribute>() is { } range)
            {
                var min = Convert.ToDouble(range.Minimum, CultureInfo.InvariantCulture);
                var max = Convert.ToDouble(range.Maximum, CultureInfo.InvariantCulture);
                var slider = new Slider { Minimum = min, Maximum = max, Value = Convert.ToDouble(value, CultureInfo.InvariantCulture), Width = 220, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
                var readout = new TextBlock { Text = slider.Value.ToString("0", CultureInfo.CurrentCulture), Width = 40, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                slider.ValueChanged += (_, e) =>
                {
                    readout.Text = e.NewValue.ToString("0", CultureInfo.CurrentCulture);
                    property.SetValue(settings, type == typeof(int) ? (object)(int)Math.Round(e.NewValue) : Math.Round(e.NewValue));
                    changed();
                };
                return new StackPanel { Orientation = Orientation.Horizontal, Children = { slider, readout } };
            }

            var number = new TextBox { Text = Convert.ToString(value, CultureInfo.CurrentCulture), Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
            number.TextChanged += (_, _) =>
            {
                if (double.TryParse(number.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed))
                {
                    property.SetValue(settings, type == typeof(int) ? (object)(int)parsed : parsed);
                    changed();
                }
            };
            return number;
        }

        if (type == typeof(string))
        {
            var text = new TextBox { Text = (string?)value ?? "", Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
            text.TextChanged += (_, _) =>
            {
                property.SetValue(settings, text.Text);
                changed();
            };
            return text;
        }

        return null;
    }

    private static bool IsCollection(Type type) => type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
}
