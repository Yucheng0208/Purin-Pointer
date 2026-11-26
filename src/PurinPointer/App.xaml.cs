using System.Windows;
using System.Windows.Threading;
using PurinPointer.Core;

namespace PurinPointer;

public partial class App : Application
{
    void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(Loc.Instance.Format("msg.error", e.Exception.Message), "Purin Pointer",
                        MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
