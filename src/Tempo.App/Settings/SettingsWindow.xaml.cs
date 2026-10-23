using System.Windows;
using Tempo.App.Widgets;

namespace Tempo.App.Settings;

internal partial class SettingsWindow : Window
{
    public SettingsWindow(AppServices services)
    {
        var viewModel = new SettingsViewModel(services);
        DataContext = viewModel;
        InitializeComponent();
        Closed += (_, _) => viewModel.Dispose();
    }
}
