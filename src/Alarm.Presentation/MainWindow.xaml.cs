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

    public void MinimizeToTrayHandler(object _, RoutedEventArgs __) => AppWindow?.Hide();

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
