// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Identity;
using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Shared._Whiskey.Dwaine.FileSystem;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Content.Shared._Whiskey.Dwaine.Network;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Server._Whiskey.Dwaine.Network;

/// <summary>
/// Authenticated user messaging over the DWAINE packet router. Sender identity is resolved from the
/// source mainframe; the sandbox cannot supply a trusted sender, mainframe or network entity.
/// </summary>
public sealed partial class DwaineCommunicationSystem : EntitySystem
{
    private const string MessageProtocol = "dwaine.message";
    private const string FileProtocol = "dwaine.file";

    [Dependency] private DwaineFileSystemSystem _fileSystems = default!;
    [Dependency] private DwaineIdentitySystem _identities = default!;
    [Dependency] private DwaineKernelSystem _kernel = default!;
    [Dependency] private DwaineNetworkSystem _network = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DwaineCommunicationServiceComponent, DwaineKernelReadyEvent>(OnKernelReady);
        SubscribeLocalEvent<DwaineCommunicationRuntimeComponent, ComponentShutdown>(OnRuntimeShutdown);
        SubscribeLocalEvent<DwaineCommunicationServiceComponent, DwaineNetworkPacketReceivedEvent>(OnPacket);
    }

    public DwaineNetworkResult TrySend(
        EntityUid sourceMainframe,
        DwainePrincipalId sender,
        string destination,
        string recipient,
        string message)
    {
        if (!_identities.TryGetStore(sourceMainframe, out var identities)
            || !identities.TryGetAccount(sender, out var senderAccount)
            || !senderAccount.Enabled
            || senderAccount.Temporary)
        {
            return DwaineNetworkResult.Disabled;
        }
        if (!IsName(recipient)
            || !IsMessage(message)
            || !TryComp<DwaineCommunicationServiceComponent>(sourceMainframe, out var config))
        {
            return DwaineNetworkResult.InvalidPayload;
        }
        if (message.Length > GetLimits(config).MaxMessageCharacters)
            return DwaineNetworkResult.PayloadTooLarge;

        var payload = senderAccount.Name + "\n" + recipient + "\n" + message;
        var request = _network.TryRequest(sourceMainframe, destination, MessageProtocol, payload, out var correlation);
        if (request is not (DwaineNetworkResult.Success or DwaineNetworkResult.Pending))
            return request;
        var replyResult = _network.TryTakeReply(sourceMainframe, correlation, out var reply);
        if (replyResult != DwaineNetworkResult.Success)
            return replyResult;
        return reply switch
        {
            "accepted" => DwaineNetworkResult.Success,
            "recipient-not-found" => DwaineNetworkResult.NotFound,
            "mailbox-full" => DwaineNetworkResult.CapacityReached,
            _ => DwaineNetworkResult.Unsupported,
        };
    }

    public DwaineNetworkResult TryReceive(
        EntityUid mainframe,
        DwainePrincipalId recipient,
        out DwaineCommunicationMessage message)
    {
        message = default;
        if (!TryGetRuntime(mainframe, out _, out var runtime)
            || !_identities.TryGetStore(mainframe, out var identities)
            || !identities.TryGetAccount(recipient, out var account)
            || !account.Enabled
            || account.Temporary)
        {
            return DwaineNetworkResult.Disabled;
        }
        if (!runtime.Mailboxes.TryGetValue(recipient, out var mailbox) || mailbox.Count == 0)
            return DwaineNetworkResult.NotFound;
        message = mailbox.Dequeue();
        runtime.MessageCount--;
        if (mailbox.Count == 0)
            runtime.Mailboxes.Remove(recipient);
        return DwaineNetworkResult.Success;
    }

    public DwaineNetworkResult TrySendFile(
        EntityUid sourceMainframe,
        DwainePrincipalId sender,
        string destination,
        string recipient,
        string sourcePath,
        DwaineVfsNodeHandle workingDirectory,
        out string receivedPath)
    {
        receivedPath = string.Empty;
        if (!_identities.TryGetStore(sourceMainframe, out var identities)
            || !identities.TryGetAccount(sender, out var senderAccount)
            || !senderAccount.Enabled
            || senderAccount.Temporary
            || !IsName(recipient)
            || !_fileSystems.TryGetFileSystem(sourceMainframe, out var fileSystem)
            || !TryComp<DwaineCommunicationServiceComponent>(sourceMainframe, out var config))
        {
            return DwaineNetworkResult.Disabled;
        }

        var files = new DwaineAuthorizedFileSystem(fileSystem, identities);
        var stat = files.TryStat(sender, sourcePath, workingDirectory, out var snapshot);
        if (stat != DwaineVfsResult.Success || snapshot.Kind != DwaineVfsNodeKind.Text || !IsFileName(snapshot.Name))
            return DwaineNetworkResult.NotFound;
        var read = files.TryReadText(sender, sourcePath, workingDirectory, out var text);
        if (read != DwaineVfsResult.Success)
            return DwaineNetworkResult.NotFound;
        var maximum = Math.Clamp(config.MaxFileCharacters, 1, DwaineCommunicationServiceComponent.HardMaxFileCharacters);
        if (text.Length > maximum)
            return DwaineNetworkResult.PayloadTooLarge;

        var payload = senderAccount.Name + "\n" + recipient + "\n" + snapshot.Name + "\n" + text;
        var request = _network.TryRequest(sourceMainframe, destination, FileProtocol, payload, out var correlation);
        if (request is not (DwaineNetworkResult.Success or DwaineNetworkResult.Pending))
            return request;
        var replyResult = _network.TryTakeReply(sourceMainframe, correlation, out var reply);
        if (replyResult != DwaineNetworkResult.Success)
            return replyResult;
        if (reply.StartsWith("accepted\n", StringComparison.Ordinal))
        {
            receivedPath = reply["accepted\n".Length..];
            return DwaineNetworkResult.Success;
        }
        return reply switch
        {
            "recipient-not-found" or "destination-unavailable" => DwaineNetworkResult.NotFound,
            "file-too-large" => DwaineNetworkResult.PayloadTooLarge,
            "permission-denied" => DwaineNetworkResult.CrossNetwork,
            _ => DwaineNetworkResult.Unsupported,
        };
    }

    private void OnKernelReady(Entity<DwaineCommunicationServiceComponent> ent, ref DwaineKernelReadyEvent args)
    {
        var runtime = EnsureComp<DwaineCommunicationRuntimeComponent>(ent.Owner);
        Cleanup(runtime);
        runtime.Online = true;
        runtime.BootGeneration = args.BootGeneration;
        if (!_kernel.TryRegisterService(
                ent.Owner,
                "communications",
                new CommunicationKernelService(this, ent.Owner, args.BootGeneration)))
        {
            Cleanup(runtime);
            _kernel.Panic(ent.Owner, "communication-service-registration");
        }
    }

    private static void OnRuntimeShutdown(Entity<DwaineCommunicationRuntimeComponent> ent, ref ComponentShutdown args)
        => Cleanup(ent.Comp);

    private void OnPacket(
        Entity<DwaineCommunicationServiceComponent> ent,
        ref DwaineNetworkPacketReceivedEvent args)
    {
        if (string.Equals(args.Packet.Protocol, FileProtocol, StringComparison.Ordinal))
        {
            OnFilePacket(ent.Owner, ref args);
            return;
        }
        if (!string.Equals(args.Packet.Protocol, MessageProtocol, StringComparison.Ordinal))
            return;
        args.Handled = true;
        if (!TryGetRuntime(ent.Owner, out var config, out var runtime)
            || args.Packet.Correlation is not { IsValid: true })
        {
            args.Reply = "service-offline";
            return;
        }

        var parts = args.Packet.Payload.Split('\n', 3, StringSplitOptions.None);
        if (parts.Length != 3 || !IsName(parts[0]) || !IsName(parts[1]) || !IsMessage(parts[2]))
        {
            args.Reply = "invalid-message";
            return;
        }
        var limits = GetLimits(config);
        if (!_identities.TryGetStore(args.Packet.SourceEntity, out var sourceIdentities)
            || !sourceIdentities.TryGetAccount(parts[0], out var sender)
            || !sender.Enabled
            || sender.Temporary)
        {
            args.Reply = "invalid-sender";
            return;
        }
        if (parts[2].Length > limits.MaxMessageCharacters
            || !_identities.TryGetStore(ent.Owner, out var identities)
            || !identities.TryGetAccount(parts[1], out var recipient)
            || !recipient.Enabled
            || recipient.Temporary)
        {
            args.Reply = "recipient-not-found";
            return;
        }
        if (runtime.MessageCount >= limits.MaxMessages)
        {
            args.Reply = "mailbox-full";
            return;
        }
        if (!runtime.Mailboxes.TryGetValue(recipient.Principal, out var mailbox))
        {
            mailbox = [];
            runtime.Mailboxes.Add(recipient.Principal, mailbox);
        }
        if (mailbox.Count >= limits.MaxMessagesPerUser)
        {
            args.Reply = "mailbox-full";
            return;
        }

        mailbox.Enqueue(new DwaineCommunicationMessage(
            args.Packet.Source.Value,
            parts[0],
            parts[2],
            _timing.CurTime));
        runtime.MessageCount++;
        args.Reply = "accepted";
    }

    private void OnFilePacket(EntityUid mainframe, ref DwaineNetworkPacketReceivedEvent args)
    {
        args.Handled = true;
        if (!TryGetRuntime(mainframe, out var config, out _)
            || args.Packet.Correlation is not { IsValid: true })
        {
            args.Reply = "service-offline";
            return;
        }

        var parts = args.Packet.Payload.Split('\n', 4, StringSplitOptions.None);
        if (parts.Length != 4 || !IsName(parts[0]) || !IsName(parts[1]) || !IsFileName(parts[2])
            || parts[3].IndexOf('\0') >= 0)
        {
            args.Reply = "invalid-file";
            return;
        }
        if (!_identities.TryGetStore(args.Packet.SourceEntity, out var sourceIdentities)
            || !sourceIdentities.TryGetAccount(parts[0], out var sender)
            || !sender.Enabled
            || sender.Temporary)
        {
            args.Reply = "invalid-sender";
            return;
        }
        var maximum = Math.Clamp(config.MaxFileCharacters, 1, DwaineCommunicationServiceComponent.HardMaxFileCharacters);
        if (parts[3].Length > maximum)
        {
            args.Reply = "file-too-large";
            return;
        }
        if (!_identities.TryGetStore(mainframe, out var identities)
            || !identities.TryGetAccount(parts[1], out var recipient)
            || !recipient.Enabled
            || recipient.Temporary
            || !_fileSystems.TryGetFileSystem(mainframe, out var fileSystem))
        {
            args.Reply = "recipient-not-found";
            return;
        }

        var files = new DwaineAuthorizedFileSystem(fileSystem, identities);
        var inbox = $"/home/{recipient.Name}/inbox";
        var directory = files.TryCreateDirectory(recipient.Principal, inbox, fileSystem.Root, _timing.CurTime, out _);
        if (directory is not (DwaineVfsResult.Success or DwaineVfsResult.AlreadyExists))
        {
            args.Reply = directory == DwaineVfsResult.AccessDenied ? "permission-denied" : "destination-unavailable";
            return;
        }
        var destination = $"{inbox}/{parts[2]}";
        var write = files.TryWriteText(recipient.Principal, destination, fileSystem.Root, parts[3], false, _timing.CurTime);
        if (write == DwaineVfsResult.NotFound)
        {
            write = files.TryCreateText(recipient.Principal, destination, fileSystem.Root, parts[3], null, _timing.CurTime);
        }
        if (write != DwaineVfsResult.Success)
        {
            args.Reply = write == DwaineVfsResult.AccessDenied ? "permission-denied" : "destination-unavailable";
            return;
        }
        args.Reply = "accepted\n" + destination;
    }

    private bool TryGetRuntime(
        EntityUid mainframe,
        out DwaineCommunicationServiceComponent config,
        out DwaineCommunicationRuntimeComponent runtime)
    {
        config = null!;
        runtime = null!;
        if (TerminatingOrDeleted(mainframe)
            || !TryComp<DwaineCommunicationServiceComponent>(mainframe, out var foundConfig)
            || !TryComp<DwaineCommunicationRuntimeComponent>(mainframe, out var foundRuntime))
        {
            return false;
        }
        config = foundConfig;
        runtime = foundRuntime;
        return runtime.Online
               && runtime.BootGeneration != 0
               && _kernel.GetState(mainframe) == DwaineSystemState.SystemReady;
    }

    private static (int MaxMessages, int MaxMessagesPerUser, int MaxMessageCharacters) GetLimits(
        DwaineCommunicationServiceComponent config)
        => (
            Math.Clamp(config.MaxMessages, 1, DwaineCommunicationServiceComponent.HardMaxMessages),
            Math.Clamp(config.MaxMessagesPerUser, 1, DwaineCommunicationServiceComponent.HardMaxMessagesPerUser),
            Math.Clamp(config.MaxMessageCharacters, 1, DwaineCommunicationServiceComponent.HardMaxMessageCharacters));

    private static bool IsName(string value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length <= 64
           && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static bool IsMessage(string value)
        => !string.IsNullOrWhiteSpace(value)
           && value.IndexOf('\0') < 0
           && value.All(character => !char.IsControl(character) || character == '\t');

    private static bool IsFileName(string value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length <= 64
           && value is not "." and not ".."
           && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static void Cleanup(DwaineCommunicationRuntimeComponent runtime)
    {
        runtime.Mailboxes.Clear();
        runtime.MessageCount = 0;
        runtime.Online = false;
        runtime.BootGeneration = 0;
    }

    private void OnKernelServiceShutdown(EntityUid mainframe, ulong generation)
    {
        if (TryComp<DwaineCommunicationRuntimeComponent>(mainframe, out var runtime)
            && runtime.BootGeneration == generation)
        {
            Cleanup(runtime);
        }
    }

    private sealed class CommunicationKernelService(
        DwaineCommunicationSystem system,
        EntityUid mainframe,
        ulong generation) : IDwaineKernelService
    {
        public void Shutdown(in DwaineKernelShutdownContext context)
        {
            if (context.Mainframe == mainframe && context.BootGeneration == generation)
                system.OnKernelServiceShutdown(mainframe, generation);
        }
    }
}
