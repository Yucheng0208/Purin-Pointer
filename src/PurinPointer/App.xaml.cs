using System.IO;
using System.Windows;
using System.Windows.Threading;
using PurinPointer.Core;

namespace PurinPointer;

public partial class App : Application
{
    static string LogFile => Path.Combine(CursorSystem.DataDir, "error.log");

    void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        var root = e.Exception;
        while (root.InnerException is not null) root = root.InnerException;

        // Keep the full details (type, XAML line, stack) for bug reports.
        try
        {
            Directory.CreateDirectory(CursorSystem.DataDir);
            File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Purin Pointer {UpdateChecker.AppVersion}\n{e.Exception}\n\n");
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }

        MessageBox.Show($"{Loc.Instance["errorDetail"]}\n\n{root.GetType().Name}: {root.Message}\n\n{LogFile}",
                        Loc.Instance["errorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);

        // If the main window never opened, there is nothing to return to.
        if (MainWindow is not { IsLoaded: true }) Shutdown(1);
    }
}
