using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluxio.Desktop.Services;
using Fluxio.Transfer.Security;
using Fluxio.Transfer.Storage;
using Fluxio.Transfer.Transfers;

namespace Fluxio.Desktop.ViewModels;

/// <summary>Phone → computer: QR code, download folder and incoming requests.</summary>
public sealed partial class ReceiveViewModel : ObservableObject
{
    private readonly AppServices _services;

    [ObservableProperty] private string? _folderError;

    public ReceiveViewModel(AppServices services, SessionViewModel session, ObservableCollection<IncomingRequestViewModel> requests)
    {
        _services = services;
        Session = session;
        Requests = requests;
        services.Settings.Changed += (_, _) => OnPropertyChanged(nameof(DownloadFolder));
    }

    public SessionViewModel Session { get; }

    public ObservableCollection<IncomingRequestViewModel> Requests { get; }

    public ObservableCollection<FileTransferViewModel> Received { get; } = [];

    public string DownloadFolder => _services.Settings.Current.DownloadFolder;

    public Task OnNavigatedToAsync() => Session.EnsureSessionAsync();

    /// <summary>Completed incoming files of this app run (newest first).</summary>
    public void Refresh()
    {
        var files = _services.Sessions.RecentBatches
            .Where(b => b.Direction == TransferDirection.Incoming)
            .SelectMany(b => b.Files)
            .Where(f => f.State == FileTransferState.Completed)
            .Reverse()
            .Take(8)
            .ToList();
        if (!files.SequenceEqual(Received.Select(r => r.File)))
        {
            Received.Clear();
            foreach (var file in files)
            {
                var vm = new FileTransferViewModel(_services, file);
                vm.Update();
                Received.Add(vm);
            }
        }
    }

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
}

public sealed record RecentItem(string Name, string Detail, bool Incoming, string Outcome, string Tone);

public sealed partial class HomeViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action<string> _navigate;

    [ObservableProperty] private int _activeTransfers;

    public HomeViewModel(AppServices services, SessionViewModel session, Action<string> navigate)
    {
        _services = services;
        _navigate = navigate;
        Session = session;
        services.History.Changed += (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(LoadRecent);
        services.Settings.Changed += (_, _) => RaiseQuickSettings();
        Loc.Instance.LanguageChanged += (_, _) => LoadRecent();
        LoadRecent();
    }

    public SessionViewModel Session { get; }

    public ObservableCollection<RecentItem> Recent { get; } = [];

    public bool HasRecent => Recent.Count > 0;

    public string ActiveText => Loc.Instance.Format("Home.Active", ActiveTransfers);

    public bool IsDark
    {
        get => _services.Theme.IsDark;
        set => _services.Settings.Update(s => s with { Theme = value ? ThemeChoice.Dark : ThemeChoice.Light });
    }

    public bool IsEnglish
    {
        get => _services.Settings.Current.Language == "en";
        set => _services.Settings.Update(s => s with { Language = value ? "en" : "tr" });
    }

    public bool RequireApproval
    {
        get => _services.Settings.Current.RequireIncomingApproval;
        set => _services.Settings.Update(s => s with { RequireIncomingApproval = value });
    }

    public void Refresh() =>
        ActiveTransfers = _services.Sessions.RecentBatches.Count(b => !b.IsFinal && b.Status != BatchStatus.AwaitingApproval);

    partial void OnActiveTransfersChanged(int value) => OnPropertyChanged(nameof(ActiveText));

    [RelayCommand]
    private void Go(string page) => _navigate(page);

    private void LoadRecent()
    {
        Recent.Clear();
        foreach (var entry in _services.History.Query(5))
        {
            Recent.Add(HistoryViewModel.ToRecent(entry));
        }

        OnPropertyChanged(nameof(HasRecent));
    }

    private void RaiseQuickSettings()
    {
        OnPropertyChanged(nameof(IsDark));
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(RequireApproval));
    }
}

public sealed partial class HistoryItemViewModel(HistoryEntry entry) : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public HistoryEntry Entry { get; } = entry;

    public string Name => Entry.FileName;

    public string When => Fmt.DateTime(Entry.Timestamp);

    public string Size => Fmt.Bytes(Entry.Size);

    public bool Incoming => Entry.Direction == TransferDirection.Incoming;

    public string Direction => Loc.Instance[Incoming ? "History.Received" : "History.Sent"];

    public string Device => Entry.DeviceLabel;

    public string Outcome => Loc.Instance[$"Outcome.{Entry.Outcome}"] + (Entry.Verified ? " ✓" : string.Empty);

    public string Tone => HistoryViewModel.ToneOf(Entry.Outcome);
}

/// <summary>Local transfer history (names, sizes, dates, results only – never contents or paths).</summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly AppServices _services;

    public HistoryViewModel(AppServices services)
    {
        _services = services;
        services.History.Changed += (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(Load);
        Loc.Instance.LanguageChanged += (_, _) => Load();
        Load();
    }

    public ObservableCollection<HistoryItemViewModel> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    public static string ToneOf(HistoryOutcome outcome) => outcome switch
    {
        HistoryOutcome.Completed => "success",
        HistoryOutcome.Failed => "danger",
        HistoryOutcome.Skipped => "warning",
        HistoryOutcome.Declined => "warning",
        _ => "muted",
    };

    public static RecentItem ToRecent(HistoryEntry entry) => new(
        entry.FileName,
        $"{Fmt.Bytes(entry.Size)} · {Fmt.DateTime(entry.Timestamp)}",
        entry.Direction == TransferDirection.Incoming,
        Loc.Instance[$"Outcome.{entry.Outcome}"],
        ToneOf(entry.Outcome));

    [RelayCommand]
    private void Load()
    {
        Items.Clear();
        foreach (var entry in _services.History.Query(1000))
        {
            Items.Add(new HistoryItemViewModel(entry));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        var ids = Items.Where(i => i.IsSelected).Select(i => i.Entry.Id).ToList();
        if (ids.Count > 0)
        {
            _services.History.Delete(ids);
        }
    }

    [RelayCommand]
    private void Delete(HistoryItemViewModel? item)
    {
        if (item is not null)
        {
            _services.History.Delete([item.Entry.Id]);
        }
    }

    [RelayCommand]
    private void ClearAll()
    {
        if (Items.Count > 0 && Dialogs.Confirm(Loc.Instance["History.ClearTitle"], Loc.Instance["History.ClearConfirm"]))
        {
            _services.History.Clear();
        }
    }
}

public sealed record Notice(string Name, string License, string Url);

public sealed partial class AboutViewModel : ObservableObject
{
    public string Version => AppServices.Version;

    public string VersionText => Loc.Instance.Format("About.Version", Version);

    public IReadOnlyList<Notice> Notices { get; } =
    [
        new("QRCoder", "MIT", "https://github.com/codebude/QRCoder"),
        new("CommunityToolkit.Mvvm", "MIT", "https://github.com/CommunityToolkit/dotnet"),
        new("Microsoft.Data.Sqlite / SQLite", "MIT / Public domain", "https://github.com/dotnet/efcore"),
        new("ASP.NET Core (Kestrel)", "MIT", "https://github.com/dotnet/aspnetcore"),
        new("React", "MIT", "https://github.com/facebook/react"),
        new("@noble/hashes", "MIT", "https://github.com/paulmillr/noble-hashes"),
    ];

    [RelayCommand]
    private static void OpenGitHub() => Dialogs.OpenUrl(AppServices.GitHubUrl);

    [RelayCommand]
    private static void OpenUrl(string url) => Dialogs.OpenUrl(url);

    [RelayCommand]
    private static void OpenLogs()
    {
        System.IO.Directory.CreateDirectory(AppPaths.LogFolder);
        Dialogs.ShowInExplorer(AppPaths.LogFolder);
    }
}
