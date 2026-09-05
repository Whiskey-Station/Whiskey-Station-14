// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using Content.Server._Whiskey.Dwaine.Devices;
using Content.Server._Whiskey.Dwaine.Identity;
using Content.Server._Whiskey.Dwaine.Process;
using Content.Server._Whiskey.Dwaine.Syscalls;
using Content.Shared._Whiskey.Dwaine.Devices;
using NUnit.Framework;
using System.Collections.Generic;

namespace Content.Tests.Server.Whiskey.Dwaine;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class DwaineSyscallPrimitivesTest
{
    [Test]
    public void CapabilityHandlesAreProcessPrincipalGenerationAndPermissionScoped()
    {
        var table = new DwaineDeviceCapabilityTable(8, 4);
        var endpoint = new DwaineDeviceEndpointId(7);
        var process = new DwaineProcessId(11);
        var principal = new DwainePrincipalId(13);
        var available = DwaineDeviceCapability.Inspect | DwaineDeviceCapability.Message;

        Assert.That(table.TryIssue(endpoint, process, principal, 3, available, available, out var handle),
            Is.EqualTo(DwaineDeviceResult.Success));
        Assert.Multiple(() =>
        {
            Assert.That(handle.IsValid, Is.True);
            Assert.That(table.TryResolve(handle, process, principal, 3, DwaineDeviceCapability.Message, out var entry),
                Is.EqualTo(DwaineDeviceResult.Success));
            Assert.That(entry.Endpoint, Is.EqualTo(endpoint));
            Assert.That(table.TryResolve(handle, new DwaineProcessId(12), principal, 3, DwaineDeviceCapability.Message, out _),
                Is.EqualTo(DwaineDeviceResult.AccessDenied));
            Assert.That(table.TryResolve(handle, process, new DwainePrincipalId(14), 3, DwaineDeviceCapability.Message, out _),
                Is.EqualTo(DwaineDeviceResult.AccessDenied));
            Assert.That(table.TryResolve(handle, process, principal, 4, DwaineDeviceCapability.Message, out _),
                Is.EqualTo(DwaineDeviceResult.AccessDenied));
            Assert.That(table.TryResolve(handle, process, principal, 3, DwaineDeviceCapability.Mount, out _),
                Is.EqualTo(DwaineDeviceResult.Unsupported));
        });

        Assert.That(table.InvalidateEndpoint(endpoint), Is.EqualTo(1));
        Assert.That(table.TryResolve(handle, process, principal, 3, DwaineDeviceCapability.Inspect, out _),
            Is.EqualTo(DwaineDeviceResult.StaleHandle));
    }

    [Test]
    public void CapabilityTableDeduplicatesAndEnforcesPerProcessCapacity()
    {
        var table = new DwaineDeviceCapabilityTable(4, 2);
        var process = new DwaineProcessId(1);
        var principal = new DwainePrincipalId(1);

        Assert.That(table.TryIssue(new DwaineDeviceEndpointId(1), process, principal, 1,
            DwaineDeviceCapability.Inspect, DwaineDeviceCapability.Inspect, out var first), Is.EqualTo(DwaineDeviceResult.Success));
        Assert.That(table.TryIssue(new DwaineDeviceEndpointId(1), process, principal, 1,
            DwaineDeviceCapability.Inspect, DwaineDeviceCapability.Inspect, out var duplicate), Is.EqualTo(DwaineDeviceResult.Success));
        Assert.That(duplicate, Is.EqualTo(first));
        Assert.That(table.Count, Is.EqualTo(1));
        Assert.That(table.TryIssue(new DwaineDeviceEndpointId(2), process, principal, 1,
            DwaineDeviceCapability.Inspect, DwaineDeviceCapability.Inspect, out _), Is.EqualTo(DwaineDeviceResult.Success));
        Assert.That(table.TryIssue(new DwaineDeviceEndpointId(3), process, principal, 1,
            DwaineDeviceCapability.Inspect, DwaineDeviceCapability.Inspect, out _), Is.EqualTo(DwaineDeviceResult.CapacityReached));
    }

    [Test]
    public void ThousandHandleChurnNeverRevivesStaleTokens()
    {
        var table = new DwaineDeviceCapabilityTable(16, 4);
        var seen = new HashSet<DwaineDeviceHandle>();
        var endpoint = new DwaineDeviceEndpointId(1);
        var principal = new DwainePrincipalId(1);

        for (ulong index = 1; index <= 1_000; index++)
        {
            var process = new DwaineProcessId(index);
            Assert.That(table.TryIssue(endpoint, process, principal, 9,
                DwaineDeviceCapability.Inspect, DwaineDeviceCapability.Inspect, out var handle),
                Is.EqualTo(DwaineDeviceResult.Success));
            Assert.That(seen.Add(handle), Is.True);
            Assert.That(table.InvalidateProcess(process), Is.EqualTo(1));
            Assert.That(table.TryResolve(handle, process, principal, 9, DwaineDeviceCapability.Inspect, out _),
                Is.EqualTo(DwaineDeviceResult.StaleHandle));
        }
        Assert.That(table.Count, Is.Zero);
    }

    [Test]
    public void AuditedIdsAndMessageIdsRemainStable()
    {
        Assert.Multiple(() =>
        {
            Assert.That((byte) DwaineSyscallId.MessageTerminal, Is.EqualTo(1));
            Assert.That((byte) DwaineSyscallId.TaskExitMessage, Is.EqualTo(16));
            Assert.That((byte) DwaineSyscallId.Mount, Is.EqualTo(23));
            Assert.That((byte) DwaineSyscallId.ReceiveFileMessage, Is.EqualTo(24));
            Assert.That((byte) DwaineSyscallId.BreakMessage, Is.EqualTo(25));
            Assert.That((byte) DwaineSyscallId.ReplyMessage, Is.EqualTo(30));
            Assert.That((byte) DwaineKernelMessageType.TaskExit, Is.EqualTo((byte) DwaineSyscallId.TaskExitMessage));
            Assert.That((byte) DwaineKernelMessageType.ReceiveFile, Is.EqualTo((byte) DwaineSyscallId.ReceiveFileMessage));
            Assert.That((byte) DwaineKernelMessageType.Break, Is.EqualTo((byte) DwaineSyscallId.BreakMessage));
            Assert.That((byte) DwaineKernelMessageType.Reply, Is.EqualTo((byte) DwaineSyscallId.ReplyMessage));
        });
    }
}
