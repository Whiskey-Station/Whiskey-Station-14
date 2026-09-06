// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Process;

namespace Content.Server._Whiskey.Dwaine.Syscalls;

[RegisterComponent]
public sealed partial class DwaineSyscallRuntimeComponent : Component
{
    public bool Online;
    public ulong BootGeneration;
    internal readonly Dictionary<DwaineProcessId, TimeSpan> NextAuthenticationAt = [];
    internal readonly Dictionary<DwaineRequestCorrelationId, DwainePendingReply> PendingReplies = [];
    internal ulong NextCorrelation = 1;
}

internal readonly record struct DwainePendingReply(
    DwaineProcessId Requester,
    DwaineProcessId Responder,
    TimeSpan ExpiresAt);
