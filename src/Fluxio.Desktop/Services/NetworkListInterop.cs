using System.Runtime.InteropServices;

namespace Fluxio.Desktop.Services;

// Minimal early-bound definitions of the Windows Network List Manager (netlistmgr.h).
// Late binding (dynamic/IDispatch) cannot return the GUIDs needed to match adapters.
// Method order must match the native vtable; only the members Fluxio uses are complete.

internal enum NlmNetworkCategory
{
    Public = 0,
    Private = 1,
    DomainAuthenticated = 2,
}

[ComImport]
[Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface INetworkListManager
{
    [return: MarshalAs(UnmanagedType.Interface)]
    object GetNetworks(int flags);

    [return: MarshalAs(UnmanagedType.Interface)]
    INetwork GetNetwork(Guid networkId);

    [return: MarshalAs(UnmanagedType.Interface)]
    IEnumNetworkConnections GetNetworkConnections();
}

[ComImport]
[Guid("DCB00006-570F-4A9B-8D69-199FDBA5723B")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface IEnumNetworkConnections
{
    [return: MarshalAs(UnmanagedType.Interface)]
    object GetNewEnum();

    [PreserveSig]
    int Next(int count, [MarshalAs(UnmanagedType.Interface)] out INetworkConnection? connection, ref int fetched);
}

[ComImport]
[Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface INetworkConnection
{
    [return: MarshalAs(UnmanagedType.Interface)]
    INetwork GetNetwork();

    bool IsConnectedToInternet { [return: MarshalAs(UnmanagedType.VariantBool)] get; }

    bool IsConnected { [return: MarshalAs(UnmanagedType.VariantBool)] get; }

    int GetConnectivity();

    Guid GetConnectionId();

    Guid GetAdapterId();
}

[ComImport]
[Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface INetwork
{
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetName();

    void SetName([MarshalAs(UnmanagedType.BStr)] string name);

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetDescription();

    void SetDescription([MarshalAs(UnmanagedType.BStr)] string description);

    Guid GetNetworkId();

    int GetDomainType();

    [return: MarshalAs(UnmanagedType.Interface)]
    IEnumNetworkConnections GetNetworkConnections();

    void GetTimeCreatedAndConnected(out uint lowCreated, out uint highCreated, out uint lowConnected, out uint highConnected);

    bool IsConnectedToInternet { [return: MarshalAs(UnmanagedType.VariantBool)] get; }

    bool IsConnected { [return: MarshalAs(UnmanagedType.VariantBool)] get; }

    int GetConnectivity();

    NlmNetworkCategory GetCategory();
}
