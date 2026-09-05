// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Identity;

namespace Content.Server._Whiskey.Dwaine.Network;

public readonly record struct DwaineCommunicationMessage(
    string SourceAddress,
    string Sender,
    string Message,
    TimeSpan ReceivedAt);

[RegisterComponent]
public sealed partial class DwaineCommunicationRuntimeComponent : Component
{
    public readonly Dictionary<DwainePrincipalId, Queue<DwaineCommunicationMessage>> Mailboxes = [];
    public int MessageCount;
    public bool Online;
    public ulong BootGeneration;
}
