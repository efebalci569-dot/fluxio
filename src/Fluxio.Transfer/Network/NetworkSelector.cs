using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Fluxio.Transfer.Security;

namespace Fluxio.Transfer.Network;

/// <summary>A local IPv4 address the transfer service could listen on.</summary>
public sealed record NetworkCandidate(
    string InterfaceId,
    string Name,
    string Description,
    IPAddress Address,
    int PrefixLength,
    NetworkInterfaceType Type,
    bool HasGateway,
    bool LooksVirtual)
{
    public bool IsWireless => Type == NetworkInterfaceType.Wireless80211;
}

/// <summary>
/// Picks the network interface that a phone on the same Wi-Fi/LAN can reach. Only private
/// (RFC 1918) IPv4 addresses are considered; VPN and virtual adapters are ranked last
/// because a phone normally cannot reach them.
/// </summary>
public static class NetworkSelector
{
    private static readonly string[] VirtualMarkers =
    [
        "virtual", "hyper-v", "vmware", "virtualbox", "vbox", "wsl", "docker", "vethernet", "loopback",
        "tap-", "tun", "vpn", "radmin", "hamachi", "zerotier", "tailscale", "wireguard", "npcap", "bluetooth",
        "pseudo", "teredo", "isatap",
    ];

    public static IReadOnlyList<NetworkCandidate> GetCandidates()
    {
        var result = new List<NetworkCandidate>();
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return result;
        }

        foreach (var ni in interfaces)
        {
            if (ni.OperationalStatus != OperationalStatus.Up
                || ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            IPInterfaceProperties props;
            try
            {
                props = ni.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            var hasGateway = props.GatewayAddresses.Any(g =>
                g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
            var looksVirtual = LooksVirtual(ni.Name) || LooksVirtual(ni.Description);

            foreach (var unicast in props.UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily == AddressFamily.InterNetwork && NetworkRules.IsPrivateIPv4(address))
                {
                    result.Add(new NetworkCandidate(
                        ni.Id, ni.Name, ni.Description, address, unicast.PrefixLength,
                        ni.NetworkInterfaceType, hasGateway, looksVirtual));
                }
            }
        }

        return result
            .OrderBy(c => c.LooksVirtual)
            .ThenByDescending(c => c.HasGateway)
            .ThenBy(c => c.Type switch
            {
                NetworkInterfaceType.Wireless80211 => 0,
                NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => 1,
                _ => 2,
            })
            .ToList();
    }

    /// <summary>The preferred interface if it is usable, otherwise the best ranked candidate.</summary>
    public static NetworkCandidate? SelectBest(string? preferredInterfaceId)
    {
        var candidates = GetCandidates();
        if (!string.IsNullOrEmpty(preferredInterfaceId))
        {
            var preferred = candidates.FirstOrDefault(c => c.InterfaceId == preferredInterfaceId);
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return candidates.FirstOrDefault();
    }

    /// <summary>True while <paramref name="address"/> is still assigned to an interface that is up.</summary>
    public static bool IsAddressAssigned(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Any(u => u.Address.Equals(address));
        }
        catch (NetworkInformationException)
        {
            return true; // Unknown: do not tear the session down on a transient query failure.
        }
    }

    private static bool LooksVirtual(string text) =>
        VirtualMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
