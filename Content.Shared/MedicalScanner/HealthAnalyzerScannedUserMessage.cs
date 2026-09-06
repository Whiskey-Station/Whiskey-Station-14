// <Trauma>
using Content.Medical.Common.Traumas;
using Content.Medical.Common.Wounds;
using Content.Shared.Body;
using Content.Shared.FixedPoint;
using Content.Trauma.Common.Medical.HealthAnalyzer;
using Robust.Shared.Prototypes;
// </Trauma>
using Robust.Shared.Serialization;

namespace Content.Shared.MedicalScanner;

/// <summary>
/// On interacting with an entity retrieves the entity UID for use with getting the current damage of the mob.
/// </summary>
[Serializable, NetSerializable]
public sealed class HealthAnalyzerScannedUserMessage : BoundUserInterfaceMessage
{
    public HealthAnalyzerUiState State;

    public bool OpenUi; // ADT-Tweak: Opened UI state

    public HealthAnalyzerScannedUserMessage(HealthAnalyzerUiState state, bool openUi = false) // ADT-Tweak
    {
        State = state;
        OpenUi = openUi; // ADT-Tweak
    }
}

/// <summary>
/// Contains the current state of a health analyzer control. Used for the health analyzer and cryo pod.
/// </summary>
[Serializable, NetSerializable]
public struct HealthAnalyzerUiState
{
    public readonly NetEntity? TargetEntity;
    public float Temperature;
    public float BloodLevel;
    public bool? ScanMode;
    // <Trauma>
    public Dictionary<ProtoId<OrganCategoryPrototype>, WoundableSeverity>? Body;
    public HashSet<ProtoId<OrganCategoryPrototype>> Bleeding = new(); // per-part instead of global
    public Dictionary<ProtoId<OrganCategoryPrototype>, BoneSeverity> BoneDamage = new();
    public FixedPoint2 VitalDamage;
    public NetEntity? Part;
    // </Traumaa>
    public bool? Unrevivable;
    public List<(string ReagentId, FixedPoint2 Quantity)>? MetabolizingReagents;

    public HealthAnalyzerUiState() {}

    public HealthAnalyzerUiState(NetEntity? targetEntity, float temperature, float bloodLevel, bool? scanMode,
        // <Trauma>
        HashSet<ProtoId<OrganCategoryPrototype>> bleeding,
        Dictionary<ProtoId<OrganCategoryPrototype>, BoneSeverity> boneDamage,
        bool? unrevivable,
        Dictionary<ProtoId<OrganCategoryPrototype>, WoundableSeverity>? body,
        FixedPoint2 vitalDamage,
        NetEntity? part = null,
        List<(string ReagentId, FixedPoint2 Quantity)>? metabolizingReagents = null)
        // </Trauma>
    {
        // <Shitmed>
        Body = body;
        VitalDamage = vitalDamage;
        Part = part;
        // </Shitmed>
        TargetEntity = targetEntity;
        Temperature = temperature;
        BloodLevel = bloodLevel;
        ScanMode = scanMode;
        Bleeding = bleeding;
        BoneDamage = boneDamage;
        Unrevivable = unrevivable;
        MetabolizingReagents = metabolizingReagents;
    }
}
