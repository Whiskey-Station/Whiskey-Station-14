// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Examine;
using Content.Trauma.Common.Hands;
using Content.Trauma.Common.Wizard;

namespace Content.Trauma.Shared.Wizard.ArcaneBarrage;

public sealed class DeleteOnDropAttemptSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DeleteOnDropAttemptComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<DeleteOnDropAttemptComponent, ItemDropAttemptEvent>(OnDroppedItem);
    }

    private void OnExamine(Entity<DeleteOnDropAttemptComponent> ent, ref ExaminedEvent args)
    {
        var message = ent.Comp.DeleteOnAttempt
            ? "delete-on-drop-attempt-comp-examine"
            : "delete-on-drop-attempt-comp-examine-bound"; // Whiskey
        args.PushMarkup(Loc.GetString(message));
    }

    private void OnDroppedItem(Entity<DeleteOnDropAttemptComponent> ent, ref ItemDropAttemptEvent args)
    {
        if (ent.Comp.DeleteOnAttempt)
            PredictedQueueDel(ent);

        // Whiskey - both variants cancel the drop; bound rites stay safely in hand.
        args.Cancelled = true;
    }
}
