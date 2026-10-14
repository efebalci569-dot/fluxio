using System.Windows.Controls;

namespace Fluxio.Desktop.Views.Controls;

/// <summary>QR code / connection panel shared by the Send and Receive pages. DataContext: SessionViewModel.</summary>
public partial class QrPanel : UserControl
{
    public QrPanel()
    {
        InitializeComponent();
    }
}
