using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluxio.Desktop.Services;
using Fluxio.Transfer;
using Fluxio.Transfer.Transfers;

namespace Fluxio.Desktop.ViewModels;

/// <summary>One file of a transfer, refreshed from the engine's real counters.</summary>
public sealed partial class FileTransferViewModel(AppServices services, TransferFile file) : ObservableObject
{
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _detail = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _tone = "muted";
    [ObservableProperty] private bool _canCancel;
    [ObservableProperty] private bool _canShow;

    public TransferFile File { get; } = file;

    public string Name => File is IncomingFile { SavedName: { } saved } ? saved : File.Name;

    public string Kind => Fmt.FileKind(File.Name);

    public void Update()
    {
        var size = File.Size;
        var done = File.State == FileTransferState.Completed ? size : Math.Min(File.Transferred, size);
        Progress = size == 0 ? (File.State == FileTransferState.Completed ? 100 : 0) : done * 100.0 / size;
        var speed = File.Meter.BytesPerSecond;
        CanCancel = !File.IsFinal && File.State != FileTransferState.Verifying && !File.Batch.IsFinal;
        CanShow = File is IncomingFile { SavedPath: not null };

        (StatusText, Tone) = File.State switch
        {
            FileTransferState.Pending when File.Batch.Status == BatchStatus.AwaitingApproval => (Loc.Instance["File.AwaitingApproval"], "muted"),
            FileTransferState.Pending => (Loc.Instance["File.Queued"], "muted"),
            FileTransferState.InProgress => (Loc.Instance["File.InProgress"], "accent"),
            FileTransferState.Verifying => (Loc.Instance["File.Verifying"], "accent"),
            FileTransferState.Completed when File.Verified => (Loc.Instance["File.Verified"], "success"),
            FileTransferState.Completed when File is OutgoingFile { SentWithoutVerification: true } => (Loc.Instance["File.SentUnverified"], "success"),
            FileTransferState.Completed => (Loc.Instance["File.Completed"], "success"),
            FileTransferState.Skipped => (Loc.Instance["File.Skipped"], "warning"),
            FileTransferState.Cancelled when File.Failure == FileFailure.Declined => (Loc.Instance["File.Declined"], "warning"),
            FileTransferState.Cancelled => (Loc.Instance["File.Cancelled"], "muted"),
            _ => (Loc.Instance[$"Failure.{File.Failure}"], "danger"),
        };

        Detail = File.State == FileTransferState.InProgress
            ? Loc.Instance.Format("Transfer.Progress", Fmt.Bytes(done), Fmt.Bytes(size))
                + (speed > 0 ? " · " + Fmt.Speed(speed) + " · " + Loc.Instance.Format("Transfer.Remaining", Fmt.Duration(SpeedMeter.EstimateRemaining(size - done, speed))) : string.Empty)
            : Fmt.Bytes(size);
    }

    [RelayCommand]
    private void Cancel()
    {
        try
        {
            services.Sessions.CancelFile(File.Batch.Id, File.Id);
        }
        catch (FluxioException)
        {
        }
    }

    [RelayCommand]
    private void Show()
    {
        if (File is IncomingFile { SavedPath: { } path })
        {
            Dialogs.ShowInExplorer(path);
        }
    }
}

/// <summary>A batch of files (one request/offer) with overall progress.</summary>
public sealed partial class BatchViewModel : ObservableObject
{
    private readonly AppServices _services;

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _detail = string.Empty;
    [ObservableProperty] private string _tone = "accent";
    [ObservableProperty] private bool _isActive;

    public BatchViewModel(AppServices services, TransferBatch batch)
    {
        _services = services;
        Batch = batch;
        Files = new ObservableCollection<FileTransferViewModel>(batch.Files.Select(f => new FileTransferViewModel(services, f)));
        Update();
    }

    public TransferBatch Batch { get; }

    public ObservableCollection<FileTransferViewModel> Files { get; }

    public bool IsIncoming => Batch.Direction == TransferDirection.Incoming;

    public string Title => Loc.Instance.Format(IsIncoming ? "Transfer.FromPhone" : "Transfer.ToPhone", Batch.DeviceLabel);

    public string Started => Fmt.DateTime(Batch.CreatedAt);

    public void Update()
    {
        foreach (var file in Files)
        {
            file.Update();
        }

        var counted = Batch.Files.Where(f => f.State is not (FileTransferState.Cancelled or FileTransferState.Skipped)).ToList();
        long total = counted.Sum(f => f.Size);
        long done = counted.Sum(f => f.State == FileTransferState.Completed ? f.Size : Math.Min(f.Transferred, f.Size));
        Progress = total == 0 ? (Batch.IsFinal ? 100 : 0) : done * 100.0 / total;
        IsActive = !Batch.IsFinal;
        var speed = Batch.BytesPerSecond;
        var completed = Batch.CountIn(FileTransferState.Completed);
        var failed = Batch.CountIn(FileTransferState.Failed);

        (StatusText, Tone) = Batch.Status switch
        {
            BatchStatus.AwaitingApproval => (Loc.Instance[IsIncoming ? "Batch.AwaitingYou" : "Batch.AwaitingPhone"], "warning"),
            BatchStatus.InProgress => (Loc.Instance["Batch.InProgress"], "accent"),
            BatchStatus.Declined => (Loc.Instance["Batch.Declined"], "warning"),
            BatchStatus.Cancelled => (Loc.Instance["Batch.Cancelled"], "muted"),
            _ when failed > 0 => (Loc.Instance.Format("Batch.FinishedWithErrors", completed, failed), "danger"),
            _ => (Loc.Instance.Format("Batch.Finished", completed), "success"),
        };

        Detail = Loc.Instance.Format("Transfer.Progress", Fmt.Bytes(done), Fmt.Bytes(total))
            + $" · {Loc.Instance.Format("Transfer.Files", Batch.Files.Count)}"
            + (IsActive && speed > 0
                ? $" · {Fmt.Speed(speed)} · {Loc.Instance.Format("Transfer.Remaining", Fmt.Duration(SpeedMeter.EstimateRemaining(total - done, speed)))}"
                : string.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        try
        {
            _services.Sessions.CancelBatch(Batch.Id);
        }
        catch (FluxioException)
        {
        }
    }
}

public sealed partial class TransfersViewModel : ObservableObject
{
    private readonly AppServices _services;

    [ObservableProperty] private int _activeCount;
    [ObservableProperty] private double _overallProgress;
    [ObservableProperty] private string _overallText = string.Empty;
    [ObservableProperty] private bool _isEmpty = true;

    public TransfersViewModel(AppServices services)
    {
        _services = services;
    }

    public ObservableCollection<BatchViewModel> Batches { get; } = [];

    /// <summary>Called by the shell timer (4×/s): syncs with the engine and refreshes the numbers.</summary>
    public void Refresh()
    {
        var batches = _services.Sessions.RecentBatches;
        foreach (var batch in batches)
        {
            if (Batches.All(b => b.Batch != batch))
            {
                Batches.Insert(0, new BatchViewModel(_services, batch));
            }
        }

        for (var i = Batches.Count - 1; i >= 0; i--)
        {
            if (!batches.Contains(Batches[i].Batch))
            {
                Batches.RemoveAt(i);
            }
        }

        foreach (var vm in Batches)
        {
            vm.Update();
        }

        var active = Batches.Where(b => b.IsActive).ToList();
        ActiveCount = active.Count;
        IsEmpty = Batches.Count == 0;
        long total = active.Sum(b => b.Batch.TotalBytes);
        long done = active.Sum(b => b.Batch.TransferredBytes);
        OverallProgress = total == 0 ? 0 : done * 100.0 / total;
        OverallText = active.Count == 0
            ? Loc.Instance["Transfers.NoneActive"]
            : Loc.Instance.Format("Transfers.Overall", active.Count, Fmt.Bytes(done), Fmt.Bytes(total), Fmt.Speed(active.Sum(b => b.Batch.BytesPerSecond)));
    }

    [RelayCommand]
    private void ClearFinished()
    {
        _services.Sessions.ClearFinishedBatches();
        Refresh();
    }
}
