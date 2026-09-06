// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Devices;
using Content.Server.Power.Components;
using Content.Shared._Whiskey.Dwaine.Services;
using Content.Shared.Paper;

namespace Content.Server._Whiskey.Dwaine.Services;

/// <summary>
/// Physical, synchronous printer driver reached exclusively after Device ABI capability validation.
/// It keeps no spool and therefore cannot accumulate unbounded queued jobs.
/// </summary>
public sealed partial class DwainePrinterSystem : EntitySystem
{
    [Dependency] private PaperSystem _paper = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DwainePrinterComponent, DwaineDeviceMessageEvent>(OnDeviceMessage);
    }

    private void OnDeviceMessage(Entity<DwainePrinterComponent> ent, ref DwaineDeviceMessageEvent args)
    {
        if (args.Handled || !string.Equals(args.Command, "print", StringComparison.OrdinalIgnoreCase))
            return;
        args.Handled = true;
        if (!ent.Comp.Enabled
            || TryComp<ApcPowerReceiverComponent>(ent, out var receiver) && !receiver.Powered)
        {
            args.Response = DwaineDeviceResponse.Failure(DwaineDeviceResult.Offline);
            return;
        }

        var limit = Math.Clamp(
            ent.Comp.MaxDocumentCharacters,
            1,
            DwainePrinterComponent.HardMaxDocumentCharacters);
        if (string.IsNullOrWhiteSpace(args.Payload)
            || args.Payload.Length > limit
            || args.Payload.IndexOf('\0') >= 0)
        {
            args.Response = DwaineDeviceResponse.Failure(DwaineDeviceResult.MalformedMessage);
            return;
        }

        var paper = Spawn(ent.Comp.PaperPrototype, Transform(ent).Coordinates);
        _paper.SetContent(paper, args.Payload);
        args.Response = DwaineDeviceResponse.Success("printed");
    }
}
