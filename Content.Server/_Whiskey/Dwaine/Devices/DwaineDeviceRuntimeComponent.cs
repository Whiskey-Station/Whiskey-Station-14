// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Process;
using Content.Server._Whiskey.Dwaine.Transport;

namespace Content.Server._Whiskey.Dwaine.Devices;

[RegisterComponent]
public sealed partial class DwaineDeviceAbiRuntimeComponent : Component
{
    public bool Online;
    public ulong BootGeneration;
    public ulong NextEndpointId = 1;
    public ulong NextGeneratedAddress = 1;
    internal DwaineDeviceCapabilityTable? Handles;
    internal readonly Dictionary<DwaineDeviceEndpointId, DwaineDeviceEndpoint> Endpoints = [];
    internal readonly Dictionary<string, DwaineDeviceEndpointId> ByAddress = new(StringComparer.OrdinalIgnoreCase);
    internal readonly Dictionary<EntityUid, DwaineDeviceEndpointId> ByEntity = [];
    internal readonly Dictionary<DwaineSessionId, DwaineDeviceEndpointId> ByTerminalSession = [];
    internal readonly Dictionary<DwaineProcessId, TimeSpan> NextScanAt = [];
}
