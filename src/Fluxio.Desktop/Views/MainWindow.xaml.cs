using System.ComponentModel;
using System.Windows;
using Fluxio.Desktop.Services;
using Fluxio.Desktop.ViewModels;

namespace Fluxio.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly AppServices _services;

    public MainWindow(AppServices services, MainViewModel viewModel)
    {
        _services = services;
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => services.Theme.ApplyTitleBar(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing Fluxio ends the session and stops the local service; ask first if files are moving.
        if (_services.Sessions.Current?.HasActiveTransfers == true
            && !Dialogs.Confirm(Loc.Instance["Exit.Title"], Loc.Instance["Exit.ActiveTransfers"]))
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
