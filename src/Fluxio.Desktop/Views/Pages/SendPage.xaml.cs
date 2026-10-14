using System.Windows;
using System.Windows.Controls;
using Fluxio.Desktop.ViewModels;

namespace Fluxio.Desktop.Views.Pages;

public partial class SendPage : UserControl
{
    public SendPage()
    {
        InitializeComponent();
    }

    private SendViewModel? ViewModel => DataContext as SendViewModel;

    private void OnDragEnter(object sender, DragEventArgs e) => OnDragOver(sender, e);

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        if (ViewModel is { } vm)
        {
            vm.IsDragOver = hasFiles;
        }

        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.IsDragOver = false;
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        vm.IsDragOver = false;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            vm.Add(paths);
        }
    }
}
