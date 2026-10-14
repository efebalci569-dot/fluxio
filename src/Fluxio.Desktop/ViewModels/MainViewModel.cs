using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluxio.Desktop.Services;
using Fluxio.Transfer.Sessions;
using Fluxio.Transfer.Transfers;

namespace Fluxio.Desktop.ViewModels;

/// <summary>Sidebar entry. Title follows the current language.</summary>
public sealed partial class NavItem : ObservableObject
{
    public NavItem(string key, string iconKey)
    {
        Key = key;
        Icon = (Geometry)Application.Current.FindResource(iconKey);
        Loc.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Title));
    }

    public string Key { get; }

    public Geometry Icon { get; }

    public string Title => Loc.Instance[$"Nav.{Key}"];

    [ObservableProperty] private int _badge;
}

public sealed partial class ToastViewModel(string message, string tone) : ObservableObject
{
    public string Message { get; } = message;

    public string Tone { get; } = tone;
}

/// <summary>Shell: navigation, the session bar, approval overlays, in-app toasts and engine events.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private NavItem _selectedNav;
    [ObservableProperty] private object _currentPage;
    [ObservableProperty] private IncomingRequestViewModel? _overlayRequest;

    public MainViewModel(AppServices services)
    {
        _services = services;
        Session = new SessionViewModel(services);
        Home = new HomeViewModel(services, Session, Navigate);
        Send = new SendViewModel(services, Session, () => Navigate("Transfers"));
        Receive = new ReceiveViewModel(services, Session, IncomingRequests);
        Transfers = new TransfersViewModel(services);
        History = new HistoryViewModel(services);
        Settings = new SettingsViewModel(services);
        About = new AboutViewModel();

        NavItems =
        [
            new NavItem("Home", "Icon.Home"),
            new NavItem("Send", "Icon.Upload"),
            new NavItem("Receive", "Icon.Download"),
            new NavItem("Transfers", "Icon.Transfers"),
            new NavItem("History", "Icon.History"),
            new NavItem("Settings", "Icon.Settings"),
            new NavItem("About", "Icon.Info"),
        ];
        _selectedNav = NavItems[0];
        _currentPage = Home;

        services.Sessions.SessionChanged += (_, e) => Application.Current.Dispatcher.BeginInvoke(() => OnSessionEvent(e));
        IncomingRequests.CollectionChanged += (_, _) => UpdateOverlay();

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Tick(), Application.Current.Dispatcher);
        _timer.Start();
    }

    public SessionViewModel Session { get; }

    public HomeViewModel Home { get; }

    public SendViewModel Send { get; }

    public ReceiveViewModel Receive { get; }

    public TransfersViewModel Transfers { get; }

    public HistoryViewModel History { get; }

    public SettingsViewModel Settings { get; }

    public AboutViewModel About { get; }

    public IReadOnlyList<NavItem> NavItems { get; }

    public ObservableCollection<IncomingRequestViewModel> IncomingRequests { get; } = [];

    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    public string Version => AppServices.Version;

    public void Navigate(string key)
    {
        var item = NavItems.FirstOrDefault(n => n.Key == key);
        if (item is not null)
        {
            SelectedNav = item;
        }
    }

    partial void OnSelectedNavChanged(NavItem value)
    {
        CurrentPage = value.Key switch
        {
            "Send" => Send,
            "Receive" => Receive,
            "Transfers" => Transfers,
            "History" => History,
            "Settings" => Settings,
            "About" => About,
            _ => Home,
        };

        switch (value.Key)
        {
            case "Send":
                _ = Send.OnNavigatedToAsync();
                break;
            case "Receive":
                _ = Receive.OnNavigatedToAsync();
                break;
            case "Settings":
                Settings.OnNavigatedTo();
                break;
        }

        UpdateOverlay();
    }

    [RelayCommand]
    private void DismissToast(ToastViewModel toast) => Toasts.Remove(toast);

    [RelayCommand]
    private void NavigateTo(string key) => Navigate(key);

    [RelayCommand]
    private void NavigateToSettings() => Navigate("Settings");

    public void ShowToast(string message, string tone = "accent")
    {
        var toast = new ToastViewModel(message, tone);
        Toasts.Add(toast);
        while (Toasts.Count > 3)
        {
            Toasts.RemoveAt(0);
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Toasts.Remove(toast);
        };
        timer.Start();
    }

    private void Tick()
    {
        Transfers.Refresh();
        Home.Refresh();
        Receive.Refresh();
        NavItems.First(n => n.Key == "Transfers").Badge = Transfers.ActiveCount;
        NavItems.First(n => n.Key == "Receive").Badge = IncomingRequests.Count;

        // Requests the phone cancelled or that ended with the session disappear by themselves.
        for (var i = IncomingRequests.Count - 1; i >= 0; i--)
        {
            if (IncomingRequests[i].Batch.Status != BatchStatus.AwaitingApproval)
            {
                IncomingRequests.RemoveAt(i);
            }
        }
    }

    private void UpdateOverlay() =>
        OverlayRequest = SelectedNav.Key == "Receive" ? null : IncomingRequests.FirstOrDefault();

    private void OnSessionEvent(SessionEventArgs e)
    {
        Session.Refresh();
        var loc = Loc.Instance;
        switch (e.Kind)
        {
            case SessionEventKind.DevicePaired:
                _services.Notifications.Notify(loc["Notify.DeviceTitle"], loc.Format("Notify.DeviceBody", e.Session.Device?.Label ?? string.Empty), important: true);
                break;

            case SessionEventKind.DeviceApproved:
                ShowToast(loc.Format("Toast.Connected", e.Session.Device?.Label ?? string.Empty), "success");
                break;

            case SessionEventKind.IncomingRequest when e.Batch is IncomingBatch batch:
                IncomingRequests.Add(new IncomingRequestViewModel(_services, batch, answered => IncomingRequests.Remove(answered)));
                _services.Notifications.Notify(loc["Notify.IncomingTitle"], loc.Format("Notify.IncomingBody", batch.DeviceLabel, batch.Files.Count, Fmt.Bytes(batch.TotalBytes)), important: true);
                break;

            case SessionEventKind.IncomingAutoAccepted when e.Batch is not null:
                ShowToast(loc.Format("Toast.Receiving", e.Batch.Files.Count), "accent");
                break;

            case SessionEventKind.OfferAccepted:
                ShowToast(loc["Toast.OfferAccepted"], "success");
                break;

            case SessionEventKind.OfferDeclined:
                ShowToast(loc["Toast.OfferDeclined"], "warning");
                break;

            case SessionEventKind.BatchFinished when e.Batch is { Status: BatchStatus.Finished } batch:
                var ok = batch.CountIn(FileTransferState.Completed);
                var key = batch.Direction == TransferDirection.Incoming ? "Toast.ReceivedDone" : "Toast.SentDone";
                var failed = batch.CountIn(FileTransferState.Failed);
                ShowToast(loc.Format(key, ok) + (failed > 0 ? " " + loc.Format("Toast.Failed", failed) : string.Empty), failed > 0 ? "warning" : "success");
                _services.Notifications.Notify("Fluxio", loc.Format(key, ok));
                break;

            case SessionEventKind.SessionEnded:
                IncomingRequests.Clear();
                if (e.Session.CloseReason is CloseReason.DeviceLeft or CloseReason.IdleTimeout or CloseReason.NetworkLost)
                {
                    ShowToast(loc[$"Session.Reason.{e.Session.CloseReason}"], e.Session.CloseReason == CloseReason.NetworkLost ? "danger" : "warning");
                }

                break;
        }
    }
}
