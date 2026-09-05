// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Devices;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Whiskey.Dwaine.Services;

namespace Content.Server._Whiskey.Dwaine.Services;

/// <summary>
/// Narrow APC adapter for explicitly marked Whiskey prototypes. The Device ABI has already
/// authenticated an operator capability before this system receives a message.
/// </summary>
public sealed partial class DwaineStationDeviceSystem : EntitySystem
{
    [Dependency] private ApcSystem _apcs = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DwaineApcInterfaceComponent, DwaineDeviceMessageEvent>(OnApcMessage);
    }

    private void OnApcMessage(Entity<DwaineApcInterfaceComponent> ent, ref DwaineDeviceMessageEvent args)
    {
        if (args.Handled || !ent.Comp.Enabled || !TryComp<ApcComponent>(ent, out var apc))
            return;

        if (string.Equals(args.Command, "inspect", StringComparison.OrdinalIgnoreCase))
        {
            args.Handled = true;
            args.Response = DwaineDeviceResponse.Success(
                $"breaker={(apc.MainBreakerEnabled ? "on" : "off")} trip={(apc.TripFlag ? "yes" : "no")} external={apc.LastExternalState.ToString().ToLowerInvariant()}");
            return;
        }

        if (!string.Equals(args.Command, "breaker", StringComparison.OrdinalIgnoreCase))
            return;
        args.Handled = true;
        var desired = args.Payload.ToLowerInvariant() switch
        {
            "on" => true,
            "off" => false,
            "toggle" => !apc.MainBreakerEnabled,
            _ => (bool?) null,
        };
        if (desired is null)
        {
            args.Response = DwaineDeviceResponse.Failure(DwaineDeviceResult.MalformedMessage);
            return;
        }
        if (apc.MainBreakerEnabled != desired.Value)
            _apcs.ApcToggleBreaker(ent, apc);
        args.Response = DwaineDeviceResponse.Success(desired.Value ? "on" : "off");
    }
}
