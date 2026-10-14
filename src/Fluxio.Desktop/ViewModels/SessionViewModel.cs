using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluxio.Desktop.Services;
using Fluxio.Transfer;
using Fluxio.Transfer.Network;
using Fluxio.Transfer.Sessions;

namespace Fluxio.Desktop.ViewModels;

/// <summary>What the QR panel and the session bar show.</summary>
public enum SessionUiState
{
    None,
    Starting,
    AwaitingScan,
    AwaitingApproval,
    Connected,
    Expired,
    Closed,
    Error,
}

/// <summary>
/// UI state of the current pairing session (QR code, countdown, connected phone, approval).
/// Shared by the Home, Send and Receive pages and the top session bar.
/// </summary>
public sealed partial class SessionViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _timer;
    private TransferSession? _shownSession;
    private string? _networkCheckedFor;

    [ObservableProperty] private SessionUiState _state;
    [ObservableProperty] private Geometry? _qrCode;
    [ObservableProperty] private string _pairingUrl = string.Empty;
    [ObservableProperty] private string _countdown = string.Empty;
    [ObservableProperty] private double _countdownProgress;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _detailText = string.Empty;
    [ObservableProperty] private string _deviceLabel = string.Empty;
    [ObservableProperty] private string _deviceAddress = string.Empty;
    [ObservableProperty] private string _matchCode = string.Empty;
    [ObservableProperty] private bool _deviceResponsive = true;
    [ObservableProperty] private string _connectedSince = string.Empty;
    [ObservableProperty] private string _endpoint = string.Empty;
    [ObservableProperty] private string? _errorText;
    [ObservableProperty] private string? _errorDetail;
    [ObservableProperty] private string? _networkWarning;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasActiveTransfers;

    public SessionViewModel(AppServices services)
    {
        _services = services;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Refresh(), Application.Current.Dispatcher);
        _timer.Start();
        Loc.Instance.LanguageChanged += (_, _) =>
        {
            _shownSession = null;
            Refresh();
        };
        Refresh();
    }

    public bool IsOpen => State is SessionUiState.AwaitingScan or SessionUiState.AwaitingApproval or SessionUiState.Connected;

    public bool IsConnected => State == SessionUiState.Connected;

    public bool IsAwaitingApproval => State == SessionUiState.AwaitingApproval;

    public bool ShowQr => State == SessionUiState.AwaitingScan && QrCode is not null;

    public bool ShowStart => State is SessionUiState.None or SessionUiState.Expired or SessionUiState.Closed or SessionUiState.Error;

    /// <summary>"Send"/"Receive" pages create a session when there is none (requirement: choosing Send or Receive starts pairing).</summary>
    public async Task EnsureSessionAsync()
    {
        if (!IsOpen && State != SessionUiState.Starting && !IsBusy)
        {
            await StartAsync();
        }
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (_services.Sessions.Current?.HasActiveTransfers == true
            && !Dialogs.Confirm(Loc.Instance["Session.NewCodeTitle"], Loc.Instance["Session.NewCodeActive"]))
        {
            return;
        }

        IsBusy = true;
        ErrorText = null;
        ErrorDetail = null;
        State = SessionUiState.Starting;
        StatusText = Loc.Instance["Session.Status.Preparing"];
        try
        {
            await _services.Sessions.StartSessionAsync();
        }
        catch (FluxioException ex)
        {
            AppLog.Warn($"Session could not start: {ex.Code}");
            State = SessionUiState.Error;
            ErrorText = Loc.Instance[$"Error.{ex.Code}"];
            ErrorDetail = ex.InnerException?.Message ?? ex.Message;
        }
        finally
        {
            IsBusy = false;
            _shownSession = null;
            Refresh();
        }
    }

    [RelayCommand]
    private async Task EndAsync()
    {
        if (_services.Sessions.Current?.HasActiveTransfers == true
            && !Dialogs.Confirm(Loc.Instance["Session.EndTitle"], Loc.Instance["Session.EndActive"]))
        {
            return;
        }

        await _services.Sessions.CloseSessionAsync(CloseReason.ClosedByUser);
        Refresh();
    }

    [RelayCommand]
    private void Approve()
    {
        try
        {
            _services.Sessions.ApproveDevice();
        }
        catch (FluxioException)
        {
            // The phone left or the approval window ran out in the meantime.
        }

        Refresh();
    }

    [RelayCommand]
    private async Task RejectAsync()
    {
        await _services.Sessions.RejectDeviceAsync();
        Refresh();
    }

    [RelayCommand]
    private void CopyLink()
    {
        if (!string.IsNullOrEmpty(PairingUrl))
        {
            try
            {
                Clipboard.SetText(PairingUrl);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
            }
        }
    }

    public void Refresh()
    {
        var session = _services.Sessions.Current;
        if (State == SessionUiState.Starting && IsBusy)
        {
            return;
        }

        if (State == SessionUiState.Error && (session is null || !session.IsOpen))
        {
            return; // keep the start error visible until the user retries
        }

        if (session is null)
        {
            SetState(SessionUiState.None, Loc.Instance["Session.Status.None"], Loc.Instance["Session.Detail.None"]);
            QrCode = null;
            return;
        }

        var now = _services.Sessions.Now;
        HasActiveTransfers = session.HasActiveTransfers;
        Endpoint = $"{session.Address}:{session.Port}";

        if (!ReferenceEquals(session, _shownSession))
        {
            _shownSession = session;
            QrCode = session.Status == SessionStatus.AwaitingScan ? QrRenderer.Render(session.PairingUrl, out _) : null;
            PairingUrl = session.Status == SessionStatus.AwaitingScan ? session.PairingUrl : string.Empty;
            if (session.IsOpen && _networkCheckedFor != session.Id)
            {
                _networkCheckedFor = session.Id;
                _ = CheckNetworkAsync(session);
            }
        }

        switch (session.Status)
        {
            case SessionStatus.AwaitingScan:
            {
                var remaining = session.PairingExpiresAt - now;
                Countdown = Fmt.Countdown(remaining);
                var total = (session.PairingExpiresAt - session.CreatedAt).TotalSeconds;
                CountdownProgress = total > 0 ? Math.Clamp(remaining.TotalSeconds / total, 0, 1) : 0;
                SetState(SessionUiState.AwaitingScan, Loc.Instance["Session.Status.AwaitingScan"], Loc.Instance["Session.Detail.AwaitingScan"]);
                break;
            }

            case SessionStatus.AwaitingApproval:
                QrCode = null;
                UpdateDevice(session);
                Countdown = session.ApprovalExpiresAt is { } deadline ? Fmt.Countdown(deadline - now) : string.Empty;
                SetState(SessionUiState.AwaitingApproval, Loc.Instance["Session.Status.AwaitingApproval"], Loc.Instance.Format("Session.Detail.AwaitingApproval", DeviceLabel));
                break;

            case SessionStatus.Connected:
                QrCode = null;
                UpdateDevice(session);
                var status = HasActiveTransfers ? "Session.Status.Transferring" : "Session.Status.Connected";
                SetState(SessionUiState.Connected, Loc.Instance[status], DeviceResponsive ? Loc.Instance.Format("Session.Detail.Connected", DeviceLabel) : Loc.Instance["Session.Detail.NotResponding"]);
                break;

            case SessionStatus.Expired:
                QrCode = null;
                SetState(SessionUiState.Expired, Loc.Instance["Session.Status.Expired"], Loc.Instance[$"Session.Reason.{session.CloseReason}"]);
                break;

            case SessionStatus.Failed:
                QrCode = null;
                SetState(SessionUiState.Closed, Loc.Instance["Session.Status.Failed"], Loc.Instance[$"Session.Reason.{session.CloseReason}"]);
                break;

            case SessionStatus.Closed:
                QrCode = null;
                SetState(SessionUiState.Closed, Loc.Instance["Session.Status.Closed"], Loc.Instance[$"Session.Reason.{session.CloseReason}"]);
                break;

            default:
                SetState(SessionUiState.Starting, Loc.Instance["Session.Status.Preparing"], string.Empty);
                break;
        }
    }

    private void UpdateDevice(TransferSession session)
    {
        if (session.Device is not { } device)
        {
            return;
        }

        DeviceLabel = device.Label;
        DeviceAddress = device.Address?.ToString() ?? string.Empty;
        MatchCode = device.MatchCode;
        DeviceResponsive = _services.Sessions.IsDeviceResponsive(session);
        ConnectedSince = session.ConnectedAt is { } at ? Loc.Instance.Format("Session.ConnectedSince", Fmt.Time(at)) : string.Empty;
    }

    private void SetState(SessionUiState state, string status, string detail)
    {
        var changed = State != state;
        State = state;
        StatusText = status;
        DetailText = detail;
        if (changed)
        {
            OnPropertyChanged(nameof(IsOpen));
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsAwaitingApproval));
            OnPropertyChanged(nameof(ShowStart));
            OnPropertyChanged(nameof(ShowQr));
        }
    }

    partial void OnQrCodeChanged(Geometry? value) => OnPropertyChanged(nameof(ShowQr));

    /// <summary>Warns about the Windows network profile / firewall, the usual reasons a phone cannot connect.</summary>
    private async Task CheckNetworkAsync(TransferSession session)
    {
        var candidate = NetworkSelector.GetCandidates().FirstOrDefault(c => c.Address.Equals(session.Address));
        if (candidate is null)
        {
            NetworkWarning = null;
            return;
        }

        NetworkCategory category;
        FirewallState firewall;
        try
        {
            (category, firewall) = await Task.Run(() =>
            {
                var c = NetworkDiagnostics.GetCategory(candidate.InterfaceId);
                return (c, NetworkDiagnostics.GetFirewallState(c));
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("Network check failed", ex);
            return;
        }

        NetworkWarning = (category, firewall) switch
        {
            (_, FirewallState.Blocked) => Loc.Instance["Network.Warning.Blocked"],
            (_, FirewallState.NotAllowedOnThisNetwork) => Loc.Instance["Network.Warning.NotAllowed"],
            (NetworkCategory.Public, FirewallState.Disabled) => null,
            (NetworkCategory.Public, _) => Loc.Instance["Network.Warning.Public"],
            (_, FirewallState.NoRule) => Loc.Instance["Network.Warning.NoRule"],
            _ => null,
        };
    }
}
