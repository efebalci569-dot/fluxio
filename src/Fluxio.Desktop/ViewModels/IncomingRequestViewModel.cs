using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluxio.Desktop.Services;
using Fluxio.Transfer;
using Fluxio.Transfer.Security;
using Fluxio.Transfer.Transfers;

namespace Fluxio.Desktop.ViewModels;

public sealed record FileLine(string Name, string Size, string Kind);

/// <summary>
/// Files a phone wants to send. Nothing is written until the user accepts. If names already
/// exist in the target folder the user chooses: keep both (rename), replace, or skip.
/// </summary>
public sealed partial class IncomingRequestViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action<IncomingRequestViewModel> _onAnswered;

    [ObservableProperty] private ConflictPolicy _policy = ConflictPolicy.Rename;
    [ObservableProperty] private string _targetFolder;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private IReadOnlyList<string> _conflicts = [];

    public IncomingRequestViewModel(AppServices services, IncomingBatch batch, Action<IncomingRequestViewModel> onAnswered)
    {
        _services = services;
        _onAnswered = onAnswered;
        Batch = batch;
        _targetFolder = services.Settings.Current.DownloadFolder;
        Files = batch.Files.Select(f => new FileLine(f.Name, Fmt.Bytes(f.Size), Fmt.FileKind(f.Name))).ToList();
        UpdateConflicts();
    }

    public IncomingBatch Batch { get; }

    public string DeviceLabel => Batch.DeviceLabel;

    public IReadOnlyList<FileLine> Files { get; }

    public string Summary => Loc.Instance.Format("Incoming.Summary", Batch.Files.Count, Fmt.Bytes(Batch.TotalBytes));

    public string Title => Loc.Instance.Format("Incoming.Title", DeviceLabel);

    public bool HasConflicts => Conflicts.Count > 0;

    public string ConflictText => Loc.Instance.Format("Incoming.ConflictText", Conflicts.Count);

    public string FreeSpaceText
    {
        get
        {
            var free = Fluxio.Transfer.Storage.IncomingStore.AvailableFreeSpace(TargetFolder);
            return free is { } bytes ? Loc.Instance.Format("Incoming.FreeSpace", Fmt.Bytes(bytes)) : string.Empty;
        }
    }

    [RelayCommand]
    private void Accept()
    {
        try
        {
            _services.Sessions.AcceptIncoming(Batch.Id, Policy, TargetFolder);
            _onAnswered(this);
        }
        catch (FluxioException ex)
        {
            Error = Loc.Instance[$"Error.{ex.Code}"];
        }
    }

    [RelayCommand]
    private void Decline()
    {
        try
        {
            _services.Sessions.DeclineIncoming(Batch.Id);
        }
        catch (FluxioException)
        {
            // Already cancelled by the phone or the session ended.
        }

        _onAnswered(this);
    }

    [RelayCommand]
    private void ChangeFolder()
    {
        var folder = Dialogs.PickFolder(TargetFolder, "Receive.ChooseFolder");
        if (folder is null)
        {
            return;
        }

        if (!FolderPolicy.IsAllowed(folder))
        {
            Error = Loc.Instance["Error.InvalidFolder"];
            return;
        }

        Error = null;
        TargetFolder = folder;
        UpdateConflicts();
        OnPropertyChanged(nameof(FreeSpaceText));
    }

    private void UpdateConflicts()
    {
        try
        {
            Conflicts = _services.Sessions.FindConflicts(Batch, FolderPolicy.Validate(TargetFolder));
        }
        catch (FluxioException)
        {
            Conflicts = [];
        }

        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(ConflictText));
    }
}
