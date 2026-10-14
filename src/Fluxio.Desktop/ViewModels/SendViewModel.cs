using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluxio.Desktop.Services;
using Fluxio.Transfer;

namespace Fluxio.Desktop.ViewModels;

public sealed partial class SelectedFileViewModel(string path, long size) : ObservableObject
{
    public string Path { get; } = path;

    public string Name => System.IO.Path.GetFileName(Path);

    public long Size { get; } = size;

    public string SizeText => Fmt.Bytes(Size);

    public string Kind => Fmt.FileKind(Name);

    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
}

/// <summary>Computer → phone: choose files, show the QR code, offer the files to the connected phone.</summary>
public sealed partial class SendViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action _showTransfers;

    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _messageIsError;
    [ObservableProperty] private bool _isDragOver;

    public SendViewModel(AppServices services, SessionViewModel session, Action showTransfers)
    {
        _services = services;
        _showTransfers = showTransfers;
        Session = session;
        Files.CollectionChanged += (_, _) => OnFilesChanged();
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionViewModel.State))
            {
                SendCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(SendHint));
            }
        };
    }

    public SessionViewModel Session { get; }

    public ObservableCollection<SelectedFileViewModel> Files { get; } = [];

    public bool HasFiles => Files.Count > 0;

    public string TotalText => Loc.Instance.Format("Send.Total", Files.Count, Fmt.Bytes(Files.Sum(f => f.Size)));

    /// <summary>Why the Send button is disabled, if it is.</summary>
    public string? SendHint =>
        Files.Count == 0 ? Loc.Instance["Send.Hint.NoFiles"]
        : !Session.IsConnected ? Loc.Instance["Send.Hint.NoPhone"]
        : null;

    public Task OnNavigatedToAsync() => Session.EnsureSessionAsync();

    [RelayCommand]
    private void AddFiles() => Add(Dialogs.PickFiles());

    [RelayCommand]
    private void AddFolder()
    {
        var folder = Dialogs.PickFolder(null, "Send.ChooseFolder");
        if (folder is not null)
        {
            Add([folder]);
        }
    }

    [RelayCommand]
    private void Remove(SelectedFileViewModel? file)
    {
        if (file is not null)
        {
            Files.Remove(file);
        }
    }

    [RelayCommand]
    private void Clear() => Files.Clear();

    /// <summary>Adds files and the files inside folders (recursively, skipping hidden/system files and links).</summary>
    public void Add(IEnumerable<string> paths)
    {
        var limit = _services.Sessions.Options.MaxFilesPerBatch;
        var known = new HashSet<string>(Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        var skipped = 0;
        var tooMany = false;
        foreach (var path in paths)
        {
            IEnumerable<string> candidates;
            try
            {
                candidates = Directory.Exists(path)
                    ? Directory.EnumerateFiles(path, "*", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
                    })
                    : [path];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
                continue;
            }

            foreach (var file in candidates)
            {
                if (Files.Count >= limit)
                {
                    tooMany = true;
                    break;
                }

                try
                {
                    var info = new FileInfo(file);
                    if (info.Exists && known.Add(info.FullName))
                    {
                        Files.Add(new SelectedFileViewModel(info.FullName, info.Length));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    skipped++;
                }
            }
        }

        if (tooMany)
        {
            ShowMessage(Loc.Instance.Format("Send.TooMany", limit), true);
        }
        else if (skipped > 0)
        {
            ShowMessage(Loc.Instance.Format("Send.Skipped", skipped), true);
        }
        else
        {
            Message = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void Send()
    {
        try
        {
            var batch = _services.Sessions.OfferFiles(Files.Select(f => f.Path).ToList());
            Files.Clear();
            ShowMessage(Loc.Instance.Format("Send.Offered", batch.Files.Count), false);
        }
        catch (FluxioException ex)
        {
            ShowMessage(Loc.Instance[$"Error.{ex.Code}"], true);
        }
    }

    [RelayCommand]
    private void ViewTransfers() => _showTransfers();

    private bool CanSend() => Files.Count > 0 && Session.IsConnected;

    private void OnFilesChanged()
    {
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(SendHint));
        SendCommand.NotifyCanExecuteChanged();
    }

    private void ShowMessage(string text, bool error)
    {
        Message = text;
        MessageIsError = error;
    }
}
