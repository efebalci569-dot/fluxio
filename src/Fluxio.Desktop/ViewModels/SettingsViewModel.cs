using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluxio.Desktop.Services;
using Fluxio.Transfer.Network;
using Fluxio.Transfer.Security;
using Fluxio.Transfer.Storage;

namespace Fluxio.Desktop.ViewModels;

public sealed record Choice<T>(T Value, string Label);

public sealed record InterfaceChoice(string? Id, string Label);

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;

    [ObservableProperty] private string? _folderError;
    [ObservableProperty] private string? _maintenanceMessage;
    [ObservableProperty] private string _networkCategoryText = string.Empty;
    [ObservableProperty] private string _firewallText = string.Empty;
    [ObservableProperty] private string _firewallTone = "muted";
    [ObservableProperty] private bool _canFixFirewall;
    [ObservableProperty] private bool _isPublicNetwork;
    [ObservableProperty] private string _currentAddress = string.Empty;
    [ObservableProperty] private bool _isCheckingNetwork;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        services.Settings.Changed += (_, _) => RaiseAll();
        Loc.Instance.LanguageChanged += (_, _) =>
        {
            BuildChoices();
            RaiseAll();
        };
        BuildChoices();
    }

    private AppSettings S => _services.Settings.Current;

    public ObservableCollection<Choice<int>> PairingChoices { get; } = [];

    public ObservableCollection<InterfaceChoice> Interfaces { get; } = [];

    public string Language
    {
        get => S.Language;
        set => _services.Settings.Update(s => s with { Language = value });
    }

    public ThemeChoice Theme
    {
        get => S.Theme;
        set => _services.Settings.Update(s => s with { Theme = value });
    }

    public string DownloadFolder => S.DownloadFolder;

    public Choice<int>? SelectedPairing
    {
        get => PairingChoices.FirstOrDefault(c => c.Value == S.PairingMinutes);
        set
        {
            if (value is not null)
            {
                _services.Settings.Update(s => s with { PairingMinutes = value.Value });
            }
        }
    }

    public bool RequireIncomingApproval
    {
        get => S.RequireIncomingApproval;
        set => _services.Settings.Update(s => s with { RequireIncomingApproval = value });
    }

    public bool Notifications
    {
        get => S.Notifications;
        set => _services.Settings.Update(s => s with { Notifications = value });
    }

    public bool MarkIncomingFiles
    {
        get => S.MarkIncomingFiles;
        set => _services.Settings.Update(s => s with { MarkIncomingFiles = value });
    }

    public InterfaceChoice? SelectedInterface
    {
        get => Interfaces.FirstOrDefault(i => i.Id == S.PreferredInterfaceId) ?? Interfaces.FirstOrDefault();
        set
        {
            if (value is not null)
            {
                _services.Settings.Update(s => s with { PreferredInterfaceId = value.Id });
            }
        }
    }

    public int Port => S.Port;

    public string Version => AppServices.Version;

    public string DataFolder => AppPaths.LocalFolder;

    public void OnNavigatedTo() => _ = RefreshNetworkAsync();

    [RelayCommand]
    private void ChangeFolder()
    {
        var folder = Dialogs.PickFolder(DownloadFolder, "Receive.ChooseFolder");
        if (folder is null)
        {
            return;
        }

        if (!FolderPolicy.IsAllowed(folder))
        {
            FolderError = Loc.Instance["Error.InvalidFolder"];
            return;
        }

        FolderError = null;
        _services.Settings.Update(s => s with { DownloadFolder = folder });
    }

    [RelayCommand]
    private void OpenFolder()
    {
        System.IO.Directory.CreateDirectory(DownloadFolder);
        Dialogs.ShowInExplorer(DownloadFolder);
    }

    [RelayCommand]
    private void ClearHistory()
    {
        if (Dialogs.Confirm(Loc.Instance["History.ClearTitle"], Loc.Instance["History.ClearConfirm"]))
        {
            _services.History.Clear();
            MaintenanceMessage = Loc.Instance["Settings.HistoryCleared"];
        }
    }

    /// <summary>Removes unfinished ".part" data left in the download folder (e.g. after a power loss).</summary>
    [RelayCommand]
    private void ClearTemp()
    {
        if (_services.Sessions.Current?.HasActiveTransfers == true)
        {
            MaintenanceMessage = Loc.Instance["Settings.TempBusy"];
            return;
        }

        var removed = IncomingStore.CleanupTempFolder(DownloadFolder);
        MaintenanceMessage = Loc.Instance.Format("Settings.TempCleared", removed);
    }

    [RelayCommand]
    private async Task RefreshNetworkAsync()
    {
        IsCheckingNetwork = true;
        try
        {
            var candidates = await Task.Run(NetworkSelector.GetCandidates);
            Interfaces.Clear();
            Interfaces.Add(new InterfaceChoice(null, Loc.Instance["Settings.Network.Auto"]));
            foreach (var candidate in candidates)
            {
                var label = $"{candidate.Name} — {candidate.Address}" + (candidate.LooksVirtual ? $" ({Loc.Instance["Settings.Network.Virtual"]})" : string.Empty);
                Interfaces.Add(new InterfaceChoice(candidate.InterfaceId, label));
            }

            OnPropertyChanged(nameof(SelectedInterface));

            var session = _services.Sessions.Current;
            var active = session is { IsOpen: true }
                ? candidates.FirstOrDefault(c => c.Address.Equals(session.Address))
                : NetworkSelector.SelectBest(S.PreferredInterfaceId);
            if (active is null)
            {
                CurrentAddress = Loc.Instance["Error.NoNetwork"];
                NetworkCategoryText = string.Empty;
                FirewallText = string.Empty;
                CanFixFirewall = false;
                return;
            }

            CurrentAddress = session is { IsOpen: true } ? $"{session.Address}:{session.Port}" : $"{active.Address}:{S.Port}";
            var (category, firewall) = await Task.Run(() =>
            {
                var c = NetworkDiagnostics.GetCategory(active.InterfaceId);
                return (c, NetworkDiagnostics.GetFirewallState(c));
            });

            IsPublicNetwork = category == NetworkCategory.Public;
            NetworkCategoryText = Loc.Instance[$"Network.Category.{category}"];
            FirewallText = Loc.Instance[$"Network.Firewall.{firewall}"];
            FirewallTone = firewall switch
            {
                FirewallState.Allowed or FirewallState.Disabled => "success",
                FirewallState.Blocked or FirewallState.NotAllowedOnThisNetwork => "danger",
                FirewallState.NoRule => "warning",
                _ => "muted",
            };
            CanFixFirewall = firewall is FirewallState.Blocked or FirewallState.NoRule or FirewallState.NotAllowedOnThisNetwork;
        }
        finally
        {
            IsCheckingNetwork = false;
        }
    }

    [RelayCommand]
    private async Task FixFirewallAsync()
    {
        if (!Dialogs.Confirm(Loc.Instance["Network.FixTitle"], Loc.Instance["Network.FixConfirm"]))
        {
            return;
        }

        var ok = await Task.Run(() => NetworkDiagnostics.AllowThroughFirewall(S.Port));
        MaintenanceMessage = Loc.Instance[ok ? "Network.FixDone" : "Network.FixFailed"];
        await RefreshNetworkAsync();
    }

    [RelayCommand]
    private void OpenNetworkSettings()
    {
        var active = NetworkSelector.SelectBest(S.PreferredInterfaceId);
        NetworkDiagnostics.OpenNetworkSettings(active?.IsWireless ?? false);
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        System.IO.Directory.CreateDirectory(AppPaths.LocalFolder);
        Dialogs.ShowInExplorer(AppPaths.LocalFolder);
    }

    private void BuildChoices()
    {
        PairingChoices.Clear();
        foreach (var minutes in AppSettings.PairingChoices)
        {
            PairingChoices.Add(new Choice<int>(minutes, Loc.Instance.Format("Settings.Minutes", minutes)));
        }

        if (Interfaces.Count > 0)
        {
            Interfaces[0] = new InterfaceChoice(null, Loc.Instance["Settings.Network.Auto"]);
        }
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(DownloadFolder));
        OnPropertyChanged(nameof(SelectedPairing));
        OnPropertyChanged(nameof(RequireIncomingApproval));
        OnPropertyChanged(nameof(Notifications));
        OnPropertyChanged(nameof(MarkIncomingFiles));
        OnPropertyChanged(nameof(SelectedInterface));
        OnPropertyChanged(nameof(Port));
    }
}
