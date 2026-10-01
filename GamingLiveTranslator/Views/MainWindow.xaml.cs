using System.Windows;
using GamingLiveTranslator.ViewModels;

namespace GamingLiveTranslator.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml.
/// Initializes the main shell window and assigns MainViewModel as DataContext.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
