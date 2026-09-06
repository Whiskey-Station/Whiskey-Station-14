// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Devices;
using Content.Shared._Whiskey.Dwaine.Devices;
using Content.Shared._Whiskey.Dwaine.Hardware;
using Content.Shared._Whiskey.Dwaine.Network;
using System.Linq;

namespace Content.Server._Whiskey.Dwaine.Network;

/// <summary>
/// Capability-gated radio driver. The Device ABI validates process, principal, handle and Message
/// capability before this system receives an operation.
/// </summary>
public sealed partial class DwaineNetworkDeviceSystem : EntitySystem
{
    [Dependency] private DwaineCommunicationSystem _communications = default!;
    [Dependency] private DwaineNetworkSystem _network = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DwaineNetworkConnectorComponent, DwaineDeviceMessageEvent>(OnMessage);
    }

    private void OnMessage(Entity<DwaineNetworkConnectorComponent> ent, ref DwaineDeviceMessageEvent args)
    {
        if (!TryComp<DwaineDeviceComponent>(ent, out var device)
            || !string.Equals(device.DriverId, "radio", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        args.Handled = true;
        if (!args.Context.GrantedCapabilities.HasFlag(DwaineDeviceCapability.Message))
        {
            args.Response = DwaineDeviceResponse.Failure(DwaineDeviceResult.AccessDenied);
            return;
        }

        var command = args.Command.ToLowerInvariant();
        switch (command)
        {
            case "address" when args.Payload.Length == 0:
            {
                var result = _network.GetNode(ent.Owner, out var node);
                args.Response = NetworkResponse(result, node.Address.Value);
                return;
            }
            case "discover":
            {
                var result = _network.Discover(ent.Owner,
                    string.IsNullOrWhiteSpace(args.Payload) ? null : args.Payload,
                    out var nodes);
                args.Response = NetworkResponse(result,
                    string.Join('\n', nodes.Select(node => node.Address.Value + "\t" + string.Join(',', node.Tags))));
                return;
            }
            case "ping":
            {
                var reply = string.Empty;
                var result = _network.TryRequest(ent.Owner, args.Payload, "dwaine.ping", string.Empty, out var correlation);
                if (result is DwaineNetworkResult.Success or DwaineNetworkResult.Pending)
                    result = _network.TryTakeReply(ent.Owner, correlation, out reply);
                args.Response = NetworkResponse(result, reply);
                return;
            }
            case "send":
            {
                var parts = args.Payload.Split('\n', 3, StringSplitOptions.None);
                if (parts.Length != 3)
                {
                    args.Response = DwaineDeviceResponse.Failure(DwaineDeviceResult.MalformedMessage);
                    return;
                }
                var result = _communications.TrySend(
                    args.Context.Mainframe,
                    args.Context.Principal,
                    parts[0],
                    parts[1],
                    parts[2]);
                args.Response = NetworkResponse(result, result == DwaineNetworkResult.Success ? "sent" : string.Empty);
                return;
            }
            case "receive" when args.Payload.Length == 0:
            {
                var result = _communications.TryReceive(args.Context.Mainframe, args.Context.Principal, out var message);
                args.Response = NetworkResponse(result,
                    result == DwaineNetworkResult.Success
                        ? $"{message.SourceAddress}\t{message.Sender}\t{message.Message}"
                        : string.Empty);
                return;
            }
            default:
                args.Response = DwaineDeviceResponse.Failure(DwaineDeviceResult.Unsupported);
                return;
        }
    }

    private static DwaineDeviceResponse NetworkResponse(DwaineNetworkResult result, string payload)
        => result switch
        {
            DwaineNetworkResult.Success => DwaineDeviceResponse.Success(payload),
            DwaineNetworkResult.InvalidAddress or DwaineNetworkResult.InvalidPayload
                => DwaineDeviceResponse.Failure(DwaineDeviceResult.MalformedMessage),
            DwaineNetworkResult.NotFound => DwaineDeviceResponse.Failure(DwaineDeviceResult.NotFound),
            DwaineNetworkResult.DuplicateAddress => DwaineDeviceResponse.Failure(DwaineDeviceResult.DuplicateAddress),
            DwaineNetworkResult.RateLimited => DwaineDeviceResponse.Failure(DwaineDeviceResult.RateLimited),
            DwaineNetworkResult.PayloadTooLarge or DwaineNetworkResult.CapacityReached
                => DwaineDeviceResponse.Failure(DwaineDeviceResult.CapacityReached),
            DwaineNetworkResult.Unsupported => DwaineDeviceResponse.Failure(DwaineDeviceResult.Unsupported),
            _ => DwaineDeviceResponse.Failure(DwaineDeviceResult.Offline),
        };
}
