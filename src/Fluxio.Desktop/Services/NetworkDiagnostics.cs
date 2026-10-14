using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace Fluxio.Desktop.Services;

public enum NetworkCategory
{
    Unknown,
    Public,
    Private,
    Domain,
}

public enum FirewallState
{
    Unknown,

    /// <summary>The firewall is off for the current profile.</summary>
    Disabled,

    /// <summary>An enabled inbound allow rule exists for Fluxio.</summary>
    Allowed,

    /// <summary>An enabled inbound block rule exists for Fluxio (e.g. "Cancel" was chosen in the Windows prompt).</summary>
    Blocked,

    /// <summary>No rule: Windows asks the first time Fluxio listens (allowing needs an administrator).</summary>
    NoRule,
}

/// <summary>
/// Read-only checks of Windows network settings that commonly stop a phone from connecting:
/// the network profile (Public/Private) and Windows Defender Firewall rules for Fluxio.
/// Changing them is always the user's explicit action (UAC prompt / Windows Settings).
/// </summary>
public static class NetworkDiagnostics
{
    private const int ProfileDomain = 1;
    private const int ProfilePrivate = 2;
    private const int ProfilePublic = 4;
    public const string FirewallRuleName = "Fluxio (local file transfer)";

    public static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Fluxio.exe");

    /// <summary>Category of the network the given adapter is connected to (Network List Manager).</summary>
    public static NetworkCategory GetCategory(string interfaceId)
    {
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"));
            if (type is null)
            {
                return NetworkCategory.Unknown;
            }

            var manager = (INetworkListManager)Activator.CreateInstance(type)!;
            var connections = manager.GetNetworkConnections();
            for (var i = 0; i < 64; i++)
            {
                var fetched = 0;
                if (connections.Next(1, out var connection, ref fetched) != 0 || fetched == 0 || connection is null)
                {
                    break;
                }

                if (!string.Equals(connection.GetAdapterId().ToString("B"), interfaceId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return connection.GetNetwork().GetCategory() switch
                {
                    NlmNetworkCategory.Public => NetworkCategory.Public,
                    NlmNetworkCategory.Private => NetworkCategory.Private,
                    NlmNetworkCategory.DomainAuthenticated => NetworkCategory.Domain,
                    _ => NetworkCategory.Unknown,
                };
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException or NotSupportedException)
        {
            AppLog.Warn("Network category could not be read.");
        }

        return NetworkCategory.Unknown;
    }

    public static FirewallState GetFirewallState(NetworkCategory category)
    {
        var profile = category switch
        {
            NetworkCategory.Private => ProfilePrivate,
            NetworkCategory.Domain => ProfileDomain,
            _ => ProfilePublic,
        };

        try
        {
            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (type is null)
            {
                return FirewallState.Unknown;
            }

            dynamic policy = Activator.CreateInstance(type)!;
            bool enabled = IsFirewallEnabled(policy, profile);
            if (!enabled)
            {
                return FirewallState.Disabled;
            }

            var exe = ExecutablePath;
            var allowed = false;
            foreach (dynamic rule in policy.Rules)
            {
                string? application = rule.ApplicationName;
                if (application is null || !string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(application)), exe, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Direction 1 = inbound, Action 0 = block / 1 = allow, Profiles = bit mask.
                if (!(bool)rule.Enabled || (int)rule.Direction != 1 || ((int)rule.Profiles & profile) == 0)
                {
                    continue;
                }

                if ((int)rule.Action == 0)
                {
                    return FirewallState.Blocked; // Block rules win over allow rules.
                }

                allowed = true;
            }

            return allowed ? FirewallState.Allowed : FirewallState.NoRule;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or RuntimeBinderException or UnauthorizedAccessException or ArgumentException)
        {
            AppLog.Warn("Firewall state could not be read.");
            return FirewallState.Unknown;
        }
    }

    private static bool IsFirewallEnabled(dynamic policy, int profile)
    {
        try
        {
            return (bool)policy.FirewallEnabled[profile];
        }
        catch (RuntimeBinderException)
        {
            return (bool)policy.get_FirewallEnabled(profile);
        }
    }

    /// <summary>
    /// Replaces Fluxio's inbound rules with one allow rule limited to private/domain networks,
    /// Fluxio's port range and the local subnet. Runs netsh elevated, so Windows shows a UAC
    /// prompt; returns false if the user declined it.
    /// </summary>
    public static bool AllowThroughFirewall(int port)
    {
        var exe = ExecutablePath.Replace("\"", string.Empty, StringComparison.Ordinal);
        var range = $"{port}-{port + 19}";
        var command =
            $"/c netsh advfirewall firewall delete rule name=all dir=in program=\"{exe}\" & " +
            $"netsh advfirewall firewall add rule name=\"{FirewallRuleName}\" dir=in action=allow program=\"{exe}\" " +
            $"protocol=TCP localport={range} profile=private,domain remoteip=localsubnet enable=yes";
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", command)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            process?.WaitForExit(30_000);
            return process is { HasExited: true, ExitCode: 0 };
        }
        catch (Win32Exception)
        {
            return false; // UAC prompt declined.
        }
    }

    /// <summary>Opens the Windows page where the network can be switched to Private.</summary>
    public static void OpenNetworkSettings(bool wireless)
    {
        try
        {
            Process.Start(new ProcessStartInfo(wireless ? "ms-settings:network-wifi" : "ms-settings:network-ethernet") { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
        }
    }

    public static bool IsWireless(string interfaceId) =>
        NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Id == interfaceId)?.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
}

