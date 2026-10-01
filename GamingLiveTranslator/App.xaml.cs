using System.Windows;
using System.Windows.Threading;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("Unhandled Dispatcher Exception", e.Exception);
        MessageBox.Show($"An unexpected error occurred:\n\n{e.Exception.Message}\n\nDetails:\n{e.Exception}", 
            "Gaming Live Translator - Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Logger.Error("Unhandled Domain Exception", ex);
            MessageBox.Show($"A critical error occurred:\n\n{ex.Message}\n\nDetails:\n{ex}", 
                "Gaming Live Translator - Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

