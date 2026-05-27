using Alarm.Presentation.ViewModels;
using Microsoft.UI.Xaml;

namespace Alarm.Presentation;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Title = "Alarm";
        viewModel.AttachDispatcher(DispatcherQueue);
    }

    // Invoked by XAML Click="MinimizeToTray_Click" — analyzers can't see the XAML wire-up.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0051:Remove unused private members", Justification = "Wired up via XAML.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Required by Click event signature.")]
    private void MinimizeToTray_Click(object sender, RoutedEventArgs e) =>
        AppWindow?.Hide();

    // x:Bind helpers — replace XAML IValueConverters with type-safe binding functions.
    public Visibility VisibleWhenTrue(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public Visibility VisibleWhenFalse(bool value) =>
        value ? Visibility.Collapsed : Visibility.Visible;

    public Visibility VisibleWhenIdle(bool isArmed, bool isRinging) =>
        (!isArmed && !isRinging) ? Visibility.Visible : Visibility.Collapsed;

    public Visibility VisibleWhenArmed(bool isArmed, bool isRinging) =>
        (isArmed && !isRinging) ? Visibility.Visible : Visibility.Collapsed;
}
