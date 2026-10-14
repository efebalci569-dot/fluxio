using System.Net;
using System.Net.Sockets;

namespace Fluxio.Transfer.Security;

/// <summary>Address classification used for interface selection and request filtering.</summary>
public static class NetworkRules
{
    /// <summary>RFC 1918 private IPv4 ranges (10/8, 172.16/12, 192.168/16).</summary>
    public static bool IsPrivateIPv4(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        Span<byte> b = stackalloc byte[4];
        address.TryWriteBytes(b, out _);
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168);
    }

    public static bool IsLinkLocalIPv4(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        Span<byte> b = stackalloc byte[4];
        address.TryWriteBytes(b, out _);
        return b[0] == 169 && b[1] == 254;
    }

    /// <summary>
    /// Only devices on the local network may talk to the transfer service. Requests that
    /// arrive from public addresses (e.g. through a port forward) are refused.
    /// </summary>
    public static bool IsAllowedRemote(IPAddress? remote)
    {
        if (remote is null)
        {
            return false;
        }

        if (remote.IsIPv4MappedToIPv6)
        {
            remote = remote.MapToIPv4();
        }

        return IPAddress.IsLoopback(remote) || IsPrivateIPv4(remote) || IsLinkLocalIPv4(remote);
    }
}
