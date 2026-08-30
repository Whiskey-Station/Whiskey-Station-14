using System.Linq;
using System.Numerics;
using Content.Server.Antag;
using Content.Server.Antag.Components;
using Content.Server.Clothing.Systems;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Mind;
using Content.Server.Objectives;
using Content.Server.PDA.Ringer;
using Content.Server.Roles;
using Content.Server.Traitor.Uplink;
using Content.Server.Whiskey.Native;
using Content.Server.Whiskey.OperativeHidden;
using Content.Server.Zombies;
using Content.Shared.Body;
using Content.Shared.Antag;
using Content.Shared.Chat;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Mind;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.NukeOps;
using Content.Shared.PDA.Ringer;
using Content.Shared.Roles;
using Content.Shared.Roles.Components;
using Content.Shared.Storage;
using Content.Shared.StatusEffect;
using Content.Shared.Stunnable;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Content.Shared.Tag;
using Content.Shared.Throwing;
using Content.Shared.Speech.Components;
using Content.Shared.Speech.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Whiskey.Native;
using Content.Shared.Whiskey.OperativeHidden;
using Content.Shared.Traits.Assorted;
using Content.Shared.Zombies;
using Content.Trauma.Common.Language;
using Robust.Client.GameObjects;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Whiskey;

[TestFixture]
public sealed class OperativeHiddenTest : GameTest
{
    private static readonly EntProtoId[] OperativeActions =
    [
        "ActionOperativeHiddenTouch",
        "ActionOperativeHiddenProcedure",
        "ActionOperativeHiddenPatientHeal",
        "ActionOperativeHiddenPatientKill",
        "ActionOperativeHiddenReception",
    ];
    private static readonly EntProtoId OperativeBody = "MobOviniaOperativeHidden";
    private static readonly EntProtoId OrdinaryOvinia = "MobOvinia";
    private static readonly EntProtoId OperativeRule = "OperativeHiddenRule";
    private static readonly EntProtoId LoneOpsRule = "LoneOpsSpawn";
    private static readonly EntProtoId OperativeDuffel = "ClothingBackpackDuffelSyndicateOperativeHidden";
    private static readonly EntProtoId PatientZombieProfile = "OperativeHiddenPatientZombieProfile";
    private static readonly EntProtoId PatientComponentBundle = "OperativeHiddenPatientComponents";
    private static readonly ProtoId<StartingGearPrototype> OperativeGear = "OperativeHiddenGear";
    private static readonly ProtoId<StartingGearPrototype> LoneOpsGear = "SyndicateLoneOperativeGearFull";
    private static readonly ProtoId<LanguagePrototype> UniversalLanguage = "Universal";
    private static readonly ProtoId<LanguagePrototype> PatientLanguage = "TauCetiBasic";
    private static readonly ProtoId<AntagSpecifierPrototype> OperativeSpecifier = "OperativeHidden";
    private static readonly ProtoId<AntagSpecifierPrototype> LoneOpsSpecifier = "LoneOp";
    private static readonly ProtoId<NpcFactionPrototype> SyndicateFaction = "Syndicate";
    private static readonly ProtoId<ReagentPrototype> OperativeTriclorReagent = "OperativeHiddenTriclor";
    private static readonly ProtoId<ReactionPrototype> OperativeTriclorReaction = "OperativeHiddenTriclor";
    private static readonly ProtoId<TagPrototype> CannotSuicideTag = "CannotSuicide";

    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task FixedOviniaBodyCyberneticsAndSelfContainedGear()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await pair.Client.WaitAssertion(() =>
        {
            var spriteName = pair.Client.EntMan.ComponentFactory.CompName<SpriteComponent>();
            foreach (var actionId in OperativeActions)
            {
                var action = pair.Client.ProtoMan.Index(actionId);
                Assert.That(action.HasComp(spriteName), Is.True,
                    $"{actionId} must expose a client Sprite component for the action bar and key binds");
            }
        });

        await server.WaitAssertion(() =>
        {
            var factory = server.ResolveDependency<IComponentFactory>();
            var containerSystem = server.System<SharedContainerSystem>();
            var inventorySystem = server.System<InventorySystem>();
            var outfitSystem = server.System<OutfitSystem>();

            var bodyPrototype = server.ProtoMan.Index(OperativeBody);
            Assert.That(bodyPrototype.TryComp<InitialBodyComponent>(out var initialBody, factory), Is.True);
            Assert.That(bodyPrototype.TryComp<HumanoidProfileComponent>(out var profile, factory), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(profile.Species.Id, Is.EqualTo("Ovinia"));
                Assert.That(initialBody.Organs["Brain"].Id, Is.EqualTo("OrganOperativeHiddenCyberBrain"));
                Assert.That(initialBody.Organs["Eyes"].Id, Is.EqualTo("OrganOperativeHiddenCyberEyes"));
                Assert.That(initialBody.Organs["Heart"].Id, Is.EqualTo("OrganOperativeHiddenCyberHeart"));
                Assert.That(initialBody.Organs["Lungs"].Id, Is.EqualTo("OrganOperativeHiddenCyberLungs"));
                Assert.That(initialBody.Organs["Liver"].Id, Is.EqualTo("OrganOperativeHiddenCyberLiver"));
                Assert.That(initialBody.Organs["Kidneys"].Id, Is.EqualTo("OrganOperativeHiddenCyberKidneys"));
            });

            var ordinary = server.ProtoMan.Index(OrdinaryOvinia);
            Assert.That(ordinary.TryComp<InitialBodyComponent>(out var ordinaryBody, factory), Is.True);
            Assert.That(ordinaryBody.Organs.Values.All(id => !id.Id.StartsWith("OrganOperativeHidden")), Is.True,
                "ordinary Ovinias must not inherit operative cybernetics");

            var operative = server.EntMan.SpawnEntity(OperativeBody, map.GridCoords);
            var bodyContainer = containerSystem.GetContainer(operative, BodyComponent.ContainerID);
            var installed = bodyContainer.ContainedEntities
                .Select(uid => server.EntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID)
                .Where(id => id != null)
                .ToHashSet();

            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.GetComponent<MetaDataComponent>(operative).EntityName, Is.Not.Empty);
                Assert.That(installed, Does.Contain("OrganOperativeHiddenCyberBrain"));
                Assert.That(installed, Does.Contain("OrganOperativeHiddenCyberEyes"));
                Assert.That(installed, Does.Contain("OrganOperativeHiddenCyberHeart"));
                Assert.That(installed, Does.Contain("OrganOperativeHiddenCyberLungs"));
                Assert.That(installed, Does.Contain("OrganOperativeHiddenCyberLiver"));
                Assert.That(installed, Does.Contain("OrganOperativeHiddenCyberKidneys"));
                Assert.That(server.EntMan.HasComponent<UplinkComponent>(operative), Is.False);
                Assert.That(server.EntMan.HasComponent<RingerAccessUplinkComponent>(operative), Is.False);
                Assert.That(server.EntMan.HasComponent<RingerUplinkComponent>(operative), Is.False);
                Assert.That(server.EntMan.HasComponent<Content.Shared.CombatMode.Pacification.PacifiedComponent>(operative), Is.False,
                    "the operative must not receive a permanent mental pacification restraint");
                Assert.That(server.EntMan.GetComponent<StatusEffectsComponent>(operative).AllowedEffects,
                    Does.Not.Contain("Pacified"),
                    "the operative must reject every later attempt to reapply pacifism");
                var thresholds = server.EntMan.GetComponent<MobThresholdsComponent>(operative).Thresholds;
                Assert.That(thresholds.Values, Does.Contain(MobState.Dead),
                    "the operative must be able to leave Critical through the normal death state");
            });

            var native = server.EntMan.GetComponent<NativeAntagComponent>(operative);
            var remote = server.EntMan.GetComponent<OperativeHiddenRemoteControlComponent>(operative);
            Assert.Multiple(() =>
            {
                Assert.That(native.Handle, Is.Not.Zero, "the real native ELF must initialize during entity startup");
                Assert.That(native.ActionEntities, Has.Count.EqualTo(4));
                Assert.That(native.ActionEntities.ContainsKey(3), Is.False,
                    "the operative self-heal action must not be granted");
                Assert.That(remote.ActionEntity, Is.Not.Null,
                    "the reception action must be granted independently from the native action map");
            });

            Assert.That(outfitSystem.SetOutfit(operative, "OperativeHiddenGear"), Is.True);
            Assert.That(inventorySystem.TryGetSlotEntity(operative, "id", out var equippedId), Is.True);
            var equippedIdUid = equippedId!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.GetComponent<MetaDataComponent>(equippedIdUid).EntityPrototype?.ID,
                    Is.EqualTo("OperativeHiddenMortuaryIDCard"));
                Assert.That(server.EntMan.HasComponent<UplinkComponent>(equippedIdUid), Is.False);
                Assert.That(server.EntMan.HasComponent<RingerAccessUplinkComponent>(equippedIdUid), Is.False);
                Assert.That(server.EntMan.HasComponent<RingerUplinkComponent>(equippedIdUid), Is.False);
            });
            var gear = server.ProtoMan.Index(OperativeGear);
            Assert.That(gear.Inhand, Is.Empty, "the operative must not spawn with a firearm or other held weapon");
            Assert.That(gear.Storage, Is.Empty, "the filled backpack owns its contents and avoids duplicate outfit insertion");
            Assert.Multiple(() =>
            {
                Assert.That(gear.Equipment["jumpsuit"], Is.EqualTo((EntProtoId) "ClothingUniformOperativeHiddenMourningDress"));
                Assert.That(gear.Equipment["mask"], Is.EqualTo((EntProtoId) "ClothingMaskOperativeHiddenSuturedGaiter"));
                Assert.That(gear.Equipment["eyes"], Is.EqualTo((EntProtoId) "ClothingEyesOperativeHiddenDeadroomLenses"));
                Assert.That(gear.Equipment["head"], Is.EqualTo((EntProtoId) "ClothingHeadOperativeHiddenTheaterCap"));
                Assert.That(gear.Equipment["outerClothing"], Is.EqualTo((EntProtoId) "ClothingOuterOperativeHiddenOssuaryCoat"));
                Assert.That(gear.Equipment.Values.Any(id => id.Id.Contains("Helmet", StringComparison.OrdinalIgnoreCase)), Is.False,
                    "the tested default outfit does not contain a helmet");
            });

            foreach (var slot in gear.Equipment.Keys)
            {
                Assert.That(inventorySystem.TryGetSlotEntity(operative, slot, out var equipped), Is.True,
                    $"operative gear slot {slot} must be equipped");
                Assert.That(server.EntMan.HasComponent<UnremoveableComponent>(equipped!.Value), Is.True,
                    $"operative gear slot {slot} must be irremovable");
                Assert.That(server.EntMan.GetComponent<MetaDataComponent>(equipped.Value).EntityName, Is.Not.Empty,
                    $"operative gear slot {slot} must have a localized name");
                Assert.That(server.EntMan.GetComponent<MetaDataComponent>(equipped.Value).EntityDescription, Is.Not.Empty,
                    $"operative gear slot {slot} must have a localized description");
                Assert.That(inventorySystem.TryUnequip(operative, slot), Is.False,
                    $"operative gear slot {slot} must reject ordinary removal");
            }

            var duffel = server.EntMan.SpawnEntity(OperativeDuffel, map.GridCoords);
            var contents = server.EntMan.GetComponent<StorageComponent>(duffel).Container.ContainedEntities;
            var ids = contents
                .Select(uid => server.EntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID)
                .Where(id => id != null)
                .ToHashSet();
            Assert.Multiple(() =>
            {
                Assert.That(contents, Has.Count.EqualTo(9));
                Assert.That(ids, Does.Contain("MedkitCombatFilled"));
                Assert.That(ids, Does.Not.Contain("OmnimedToolSyndie"));
                Assert.That(ids, Does.Contain("Scalpel"));
                Assert.That(ids, Does.Contain("Retractor"));
                Assert.That(ids, Does.Contain("Hemostat"));
                Assert.That(ids, Does.Contain("Cautery"));
                Assert.That(ids, Does.Contain("Saw"));
                Assert.That(ids, Does.Contain("Drill"));
                Assert.That(ids, Does.Contain("Bonesetter"));
                Assert.That(ids, Does.Contain("BoneGel"));
                Assert.That(contents.Any(uid => server.EntMan.HasComponent<UplinkComponent>(uid)), Is.False);
                Assert.That(contents.Any(uid => server.EntMan.HasComponent<RingerAccessUplinkComponent>(uid)), Is.False);
                Assert.That(contents.Any(uid => server.EntMan.HasComponent<RingerUplinkComponent>(uid)), Is.False);
                Assert.That(ids.Any(id => id!.Contains("Telecrystal") || id.Contains("Uplink")), Is.False);
            });

            server.EntMan.DeleteEntity(operative);
            server.EntMan.DeleteEntity(duffel);
            server.System<SharedMapSystem>().DeleteMap(map.MapId);
        });
    }

    [Test]
    public async Task TriclorSequenceKillsAfterEightSecondsWithTraceDamageAndAllowsRecovery()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var victims = new List<(string Species, EntityUid Uid, FixedPoint2 OriginalDeathThreshold)>();
        var ticksBeforeDeath = 0;

        await server.WaitAssertion(() =>
        {
            var mobState = server.System<MobStateSystem>();
            var thresholds = server.System<MobThresholdSystem>();
            ticksBeforeDeath = (int) Math.Ceiling(server.ResolveDependency<IGameTiming>().TickRate * 7.5);
            foreach (var species in new[]
                     {
                         "MobHuman",
                         "MobOvinia",
                         "MobReptilian",
                         "MobMoth",
                         "MobDiona",
                         "MobSlimePerson",
                         "MobVox",
                     })
            {
                var operative = server.EntMan.SpawnEntity(OperativeBody, map.GridCoords);
                var victim = server.EntMan.SpawnEntity(species, map.GridCoords);
                Assert.That(server.EntMan.HasComponent<HumanoidProfileComponent>(victim), Is.True,
                    $"{species} must be recognized as a playable species target");
                Assert.That(thresholds.TryGetThresholdForState(victim, MobState.Dead, out var originalDeath), Is.True);

                var action = new OperativeHiddenTriclorActionEvent
                {
                    Target = victim,
                };
                server.EntMan.EventBus.RaiseLocalEvent(operative, action);

                Assert.Multiple(() =>
                {
                    Assert.That(action.Handled, Is.True);
                    Assert.That(mobState.IsDead(victim), Is.False,
                        $"triclor must not instantly kill playable species {species}");
                    Assert.That(server.EntMan.HasComponent<OperativeHiddenTriclorComponent>(victim), Is.True);
                    Assert.That(server.EntMan.HasComponent<UnrevivableComponent>(victim), Is.False,
                        "the victim must remain eligible for recovery surgery");
                });
                victims.Add((species, victim, originalDeath!.Value));
            }
        });

        await server.WaitRunTicks(ticksBeforeDeath);
        await server.WaitAssertion(() =>
        {
            var mobState = server.System<MobStateSystem>();
            foreach (var victim in victims)
                Assert.That(mobState.IsDead(victim.Uid), Is.False,
                    $"{victim.Species} must still be alive before second eight");
        });

        await server.WaitRunTicks((int) Math.Ceiling(
            (double) server.ResolveDependency<IGameTiming>().TickRate));
        await server.WaitAssertion(() =>
        {
            var mobState = server.System<MobStateSystem>();
            var thresholds = server.System<MobThresholdSystem>();
            foreach (var victim in victims)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(mobState.IsDead(victim.Uid), Is.True,
                        $"{victim.Species} must die at second eight");
                    Assert.That(thresholds.CheckVitalDamage(victim.Uid).Float(), Is.LessThan(10f),
                        $"{victim.Species} must retain only trace real damage");
                    Assert.That(server.EntMan.HasComponent<UnrevivableComponent>(victim.Uid), Is.False);
                });
            }

            // Recovery surgery ultimately performs the same dead-to-alive
            // state transition. Verify that it restores the original health
            // threshold and consumes the temporary triclor death marker.
            var recoveryPatient = victims[0];
            mobState.ChangeMobState(recoveryPatient.Uid, MobState.Alive);
            Assert.That(mobState.IsAlive(recoveryPatient.Uid), Is.True);
            Assert.That(thresholds.GetThresholdForState(recoveryPatient.Uid, MobState.Dead),
                Is.EqualTo(recoveryPatient.OriginalDeathThreshold));
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.HasComponent<OperativeHiddenTriclorComponent>(victims[0].Uid), Is.False);
            server.System<SharedMapSystem>().DeleteMap(map.MapId);
        });
    }

    [Test]
    public void RuleInheritsLoneOpsConditionsWithoutItsUplinkRuntime()
    {
        var factory = Server.ResolveDependency<IComponentFactory>();
        var operativeRule = SProtoMan.Index(OperativeRule);
        var loneOpsRule = SProtoMan.Index(LoneOpsRule);

        Assert.Multiple(() =>
        {
            Assert.That(operativeRule.HasComp<NativeModuleRequirementComponent>(factory), Is.True);
            Assert.That(operativeRule.HasComp<NukeopsRuleComponent>(factory), Is.False,
                "the operative must not receive the NukeOps uplink/TC setup");
            Assert.That(loneOpsRule.HasComp<NukeopsRuleComponent>(factory), Is.True,
                "the original LoneOps rule must retain its normal uplink runtime");
        });
    }

    [Test]
    public void TriclorIsAVisibleToxinWithTheHardestGuidebookRecipe()
    {
        var reagent = SProtoMan.Index(OperativeTriclorReagent);
        var reaction = SProtoMan.Index(OperativeTriclorReaction);

        Assert.Multiple(() =>
        {
            Assert.That(reagent.Group.Id, Is.EqualTo("Toxins"),
                "the guidebook's toxin group must enumerate Triclor Hyper");
            Assert.That(reaction.Products["OperativeHiddenTriclor"], Is.EqualTo(FixedPoint2.New(5)));
            Assert.That(reaction.Reactants, Has.Count.EqualTo(11));
            Assert.That(reaction.Reactants["Uranium"].Catalyst, Is.True);
            Assert.That(reaction.Reactants["Johntonite"].Amount, Is.EqualTo(FixedPoint2.New(10)));
            Assert.That(reaction.MinimumTemperature, Is.EqualTo(665f));
            Assert.That(reaction.MaximumTemperature, Is.EqualTo(675f));
            Assert.That(reaction.Quantized, Is.True);
            Assert.That(reaction.MixingCategories, Is.Not.Null);
            Assert.That(reaction.MixingCategories!.Select(category => category.Id),
                Does.Contain("Centrifuge"));
        });
    }

    [Test]
    public void PatientZombieProfilePreservesHumanBodyAndUsesNeuralLeash()
    {
        var factory = Server.ResolveDependency<IComponentFactory>();
        var profilePrototype = SProtoMan.Index(PatientZombieProfile);
        var bundlePrototype = SProtoMan.Index(PatientComponentBundle);
        Assert.That(profilePrototype.TryComp<ZombieComponent>(out var zombie, factory), Is.True);
        Assert.That(bundlePrototype.TryComp<NativeAntagPatientComponent>(out var patient, factory), Is.True);
        Assert.That(bundlePrototype.TryComp<OperativeHiddenPuppetVisualsComponent>(out var receiver, factory), Is.True);
        var ordinaryZombie = new ZombieComponent();

        Assert.Multiple(() =>
        {
            Assert.That(zombie.BaseZombieInfectionChance, Is.EqualTo(0.75f));
            Assert.That(zombie.ZombieMovementSpeedDebuff, Is.EqualTo(0.95f));
            Assert.That(zombie.PassiveHealingCritMultiplier, Is.EqualTo(2f));
            Assert.That(zombie.HealingOnBite.DamageDict["Blunt"].Float(), Is.EqualTo(-2f));
            Assert.That(zombie.HealingOnBite.DamageDict["Slash"].Float(), Is.EqualTo(-2f));
            Assert.That(zombie.HealingOnBite.DamageDict["Piercing"].Float(), Is.EqualTo(-2f));
            Assert.That(zombie.ResistanceEffectiveness.DamageDict.ContainsKey("Ballistic"), Is.False);
            Assert.That(zombie.PassiveHealing.DamageDict.ContainsKey("Ballistic"), Is.False);
            Assert.That(zombie.DamageOnBite.DamageDict["Slash"].Float(), Is.EqualTo(13f));
            Assert.That(zombie.DamageOnBite.DamageDict["Piercing"].Float(), Is.EqualTo(7f));
            Assert.That(patient.KnockdownTime, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(patient.MaxThrow, Is.EqualTo(10f));
            Assert.That(patient.MaxFlairDistance, Is.EqualTo(500f));
            Assert.That(patient.MaxMasterDistance, Is.EqualTo(10f));
            Assert.That(patient.OutOfRangeSpeedModifier, Is.EqualTo(0.5f));
            Assert.That(receiver.State, Is.EqualTo(OperativeHiddenPuppetVisualState.Linked));
            Assert.That(patient.ActionJumpId?.Id, Is.EqualTo("ZombieJump"));
            Assert.That(patient.ActionFlairId?.Id, Is.EqualTo("ZombieFlair"));
            Assert.That(patient.ReceptionSound, Is.TypeOf<SoundPathSpecifier>());
            Assert.That(((SoundPathSpecifier) patient.ReceptionSound).Path.ToString(),
                Is.EqualTo("/Audio/_Whiskey/OperativeHidden/operative_hidden_patient_reception.ogg"));
            Assert.That(zombie.ForcedLanguage.Id, Is.EqualTo("TauCetiBasic"));
            Assert.That(zombie.NameModifier.Id, Is.EqualTo("operative-hidden-patient-name-prefix"));
            Assert.That(zombie.UseZombieEmoteSounds, Is.False);
            Assert.That(zombie.AutoGroan, Is.False);
            Assert.That(zombie.PlayGreetSound, Is.False);
            Assert.That(zombie.RemoveHands, Is.False);
            Assert.That(zombie.AttackAnimation.Id, Is.EqualTo("WeaponArcFist"));
            Assert.That(zombie.BiteSound, Is.TypeOf<SoundCollectionSpecifier>());
            Assert.That(((SoundCollectionSpecifier) zombie.BiteSound).Collection?.Id, Is.EqualTo("Punch"));
            Assert.That(SProtoMan.HasIndex<EntityPrototype>(patient.ActionJumpId!.Value), Is.True);
            Assert.That(SProtoMan.HasIndex<EntityPrototype>(patient.ActionFlairId!.Value), Is.True);
            Assert.That(ordinaryZombie.BaseZombieInfectionChance, Is.EqualTo(1f),
                "the patient profile must not rebalance ordinary Whiskey zombies");
            Assert.That(ordinaryZombie.PassiveHealingCritMultiplier, Is.EqualTo(5f));
            Assert.That(ordinaryZombie.HealingOnBite.DamageDict["Blunt"].Float(), Is.EqualTo(-25f));
            Assert.That(ordinaryZombie.UseZombieEmoteSounds, Is.True);
            Assert.That(ordinaryZombie.AutoGroan, Is.True);
            Assert.That(ordinaryZombie.PlayGreetSound, Is.True);
            Assert.That(ordinaryZombie.RemoveHands, Is.True);
        });
    }

    [Test]
    public async Task ReceptionPreservesBothMindsAndReturnsOnDamageOrShove()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var operative = server.EntMan.SpawnEntity(OperativeBody, map.GridCoords);
            var patient = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            var patientComponent = server.EntMan.EnsureComponent<NativeAntagPatientComponent>(patient);
            var receiver = server.EntMan.EnsureComponent<OperativeHiddenPuppetVisualsComponent>(patient);
            patientComponent.Master = operative;

            var mindSystem = server.System<MindSystem>();
            var operativeMind = mindSystem.CreateMind(null);
            var patientMind = mindSystem.CreateMind(null);
            mindSystem.TransferTo(operativeMind, operative, ghostCheckOverride: true, mind: operativeMind.Comp);
            mindSystem.TransferTo(patientMind, patient, ghostCheckOverride: true, mind: patientMind.Comp);

            var reception = new OperativeHiddenReceptionActionEvent { Target = patient };
            server.EntMan.EventBus.RaiseLocalEvent(operative, reception);

            var remote = server.EntMan.GetComponent<OperativeHiddenRemoteControlComponent>(operative);
            Assert.Multiple(() =>
            {
                Assert.That(reception.Handled, Is.True);
                Assert.That(remote.ControlledPatient, Is.EqualTo(patient));
                Assert.That(server.EntMan.GetComponent<RelayInputMoverComponent>(operative).RelayEntity,
                    Is.EqualTo(patient));
                Assert.That(server.EntMan.GetComponent<InteractionRelayComponent>(operative).RelayEntity,
                    Is.EqualTo(patient));
                Assert.That(server.EntMan.GetComponent<EyeComponent>(operative).Target, Is.EqualTo(patient));
                Assert.That(operativeMind.Comp.CurrentEntity, Is.EqualTo(operative));
                Assert.That(patientMind.Comp.CurrentEntity, Is.EqualTo(patient));
                Assert.That(server.EntMan.HasComponent<HandsComponent>(patient), Is.True,
                    "the conscious patient must retain hands for relayed weapons");
            });

            var pen = server.EntMan.SpawnEntity("Pen", map.GridCoords);
            server.System<SharedInteractionSystem>().UserInteraction(
                operative,
                server.EntMan.GetComponent<TransformComponent>(pen).Coordinates,
                pen);
            var hands = server.System<SharedHandsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(hands.GetActiveItem((patient, null)), Is.EqualTo(pen),
                    "the operative's click must use the puppet's active hand");
                Assert.That(hands.GetActiveItem((operative, null)), Is.Null,
                    "remote interaction must not use the operative body's hands");
            });

            var smallDamage = new DamageSpecifier();
            smallDamage.DamageDict.Add("Blunt", FixedPoint2.New(1));
            var damageEvent = new DamageDealtEvent(smallDamage, patient, true, false, smallDamage);
            server.EntMan.EventBus.RaiseLocalEvent(operative, ref damageEvent);
            Assert.Multiple(() =>
            {
                Assert.That(remote.ControlledPatient, Is.Null);
                Assert.That(server.EntMan.HasComponent<RelayInputMoverComponent>(operative), Is.False);
                Assert.That(server.EntMan.HasComponent<InteractionRelayComponent>(operative), Is.False);
                Assert.That(server.EntMan.GetComponent<EyeComponent>(operative).Target, Is.Null);
            });

            reception = new OperativeHiddenReceptionActionEvent { Target = patient };
            server.EntMan.EventBus.RaiseLocalEvent(operative, reception);
            var shove = new DisarmedEvent(operative, patient, 0f);
            server.EntMan.EventBus.RaiseLocalEvent(operative, ref shove);
            Assert.That(remote.ControlledPatient, Is.Null);

            var burst = new DamageSpecifier();
            burst.DamageDict.Add("Blunt", FixedPoint2.New(20));
            var burstEvent = new DamageDealtEvent(burst, patient, true, false, burst);
            server.EntMan.EventBus.RaiseLocalEvent(operative, ref burstEvent);
            Assert.Multiple(() =>
            {
                Assert.That(patientComponent.SignalLost, Is.True);
                Assert.That(receiver.State, Is.EqualTo(OperativeHiddenPuppetVisualState.Reconnect));
                Assert.That(server.EntMan.HasComponent<KnockedDownComponent>(patient), Is.True);
            });

            server.System<SharedMapSystem>().DeleteMap(map.MapId);
        });
    }

    [Test]
    public async Task CorpseProcedureConvertsThroughNativeBridge()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        EntityUid operative = default;
        EntityUid victim = default;
        EntityUid conversionObjective = default;
        Entity<MindComponent> operativeMind = default;
        string originalName = string.Empty;
        string activeHand = string.Empty;
        var ticksPerStage = 0;

        await server.WaitAssertion(() =>
        {
            // Move the connected harness client before creating any scenario
            // entity, ensuring none of the deliberately server-only direct
            // event mutations ever enter that session's PVS history.
            var mindSystem = server.System<MindSystem>();
            var observer = server.EntMan.SpawnEntity(
                "MobHuman",
                new MapCoordinates(new Vector2(10_000f, 10_000f), map.MapId));
            var observerMind = mindSystem.GetOrCreateMind(pair.Player!.UserId);
            mindSystem.TransferTo(observerMind, observer, ghostCheckOverride: true, mind: observerMind.Comp);
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var hands = server.System<SharedHandsSystem>();
            var mobState = server.System<MobStateSystem>();
            operative = server.EntMan.SpawnEntity(OperativeBody, map.GridCoords);
            victim = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            originalName = server.EntMan.GetComponent<MetaDataComponent>(victim).EntityName;
            mobState.ChangeMobState(victim, MobState.Dead);
            ticksPerStage = (int) Math.Ceiling(server.ResolveDependency<IGameTiming>().TickRate * 3.6);

            var mindSystem = server.System<MindSystem>();
            // The procedure itself is a server-authoritative state-machine test;
            // use a sessionless mind so advancing real ticks cannot introduce
            // unrelated client PVS/component-state traffic into the transaction.
            operativeMind = mindSystem.CreateMind(null);
            mindSystem.TransferTo(operativeMind, operative, ghostCheckOverride: true, mind: operativeMind.Comp);
            conversionObjective = server.EntMan.SpawnEntity(
                "OperativeHiddenConvertObjective",
                MapCoordinates.Nullspace);
            operativeMind.Comp.Objectives.Add(conversionObjective);

            activeHand = server.EntMan.GetComponent<HandsComponent>(operative).ActiveHandId!;
            foreach (var invalidTool in new[] { "OmnimedToolSyndie", "Saw" })
            {
                var held = server.EntMan.SpawnEntity(invalidTool, map.GridCoords);
                Assert.That(hands.TryPickup(operative, held, activeHand), Is.True);

                var rejectedAction = new NativeAntagTargetActionEvent
                {
                    EventType = (uint) NativeAntagEventType.ProcedureAction,
                    Target = victim,
                };
                server.EntMan.EventBus.RaiseLocalEvent(operative, rejectedAction);

                Assert.That(server.EntMan.HasComponent<ZombieComponent>(victim), Is.False,
                    $"{invalidTool} must not start or skip the first cautery stage");
                Assert.That(hands.TryDrop(operative, held, checkActionBlocker: false), Is.True);
                server.EntMan.DeleteEntity(held);
            }

        });

        var toolOrder = new[] { "Cautery", "Drill", "Scalpel", "Retractor", "Hemostat", "Saw" };
        foreach (var tool in toolOrder)
        {
            EntityUid held = default;
            await server.WaitAssertion(() =>
            {
                var hands = server.System<SharedHandsSystem>();
                var transform = server.System<SharedTransformSystem>();
                held = server.EntMan.SpawnEntity(tool, map.GridCoords);
                Assert.That(hands.TryPickup(operative, held, activeHand), Is.True);

                var action = new NativeAntagTargetActionEvent
                {
                    EventType = (uint) NativeAntagEventType.ProcedureAction,
                    Target = victim,
                };
                server.EntMan.EventBus.RaiseLocalEvent(operative, action);

                if (tool == toolOrder[0])
                {
                    var blockedDamage = new DamageSpecifier();
                    var zeroDamage = new DamageDealtEvent(
                        blockedDamage,
                        victim,
                        InterruptsDoAfters: true,
                        IgnoreBlockers: false,
                        ModifiedDamage: blockedDamage);
                    server.EntMan.EventBus.RaiseLocalEvent(operative, ref zeroDamage);
                    transform.SetCoordinates(operative, map.GridCoords.Offset(new Vector2(0.1f, 0.1f)));
                    transform.SetCoordinates(victim, map.GridCoords.Offset(new Vector2(-0.1f, -0.1f)));
                }
            });

            await server.WaitRunTicks(ticksPerStage);

            await server.WaitAssertion(() =>
            {
                if (tool != toolOrder[^1])
                    Assert.That(server.EntMan.HasComponent<ZombieComponent>(victim), Is.False,
                        $"the procedure must not convert before completing the {tool} stage");

                var hands = server.System<SharedHandsSystem>();
                Assert.That(hands.TryDrop(operative, held, checkActionBlocker: false), Is.True);
                server.EntMan.DeleteEntity(held);
            });
        }

        await server.WaitAssertion(() =>
        {
            var factions = server.System<NpcFactionSystem>();
            var mobState = server.System<MobStateSystem>();
            Assert.That(server.EntMan.HasComponent<ZombieComponent>(victim), Is.True,
                "the ordered six-instrument procedure must convert the patient");
            var patient = server.EntMan.GetComponent<NativeAntagPatientComponent>(victim);
            var patientZombie = server.EntMan.GetComponent<ZombieComponent>(victim);
            var condition = server.EntMan.GetComponent<NativeCounterConditionComponent>(conversionObjective);
            var localization = server.ResolveDependency<ILocalizationManager>();
            var expectedName = localization.GetString(
                "operative-hidden-patient-name-prefix",
                ("baseName", originalName));
            Assert.Multiple(() =>
            {
                Assert.That(patient.Master, Is.EqualTo(operative));
                Assert.That(patient.SpeechSoundToken, Is.EqualTo(2));
                Assert.That(patient.ActionEntities, Has.Count.EqualTo(2));
                Assert.That(mobState.IsAlive(victim), Is.True);
                Assert.That(factions.IsMember(victim, SyndicateFaction), Is.True);
                Assert.That(server.EntMan.GetComponent<MetaDataComponent>(victim).EntityName,
                    Is.EqualTo(expectedName));
                Assert.That(patientZombie.ForcedLanguage, Is.EqualTo(PatientLanguage));
                Assert.That(server.EntMan.HasComponent<ZombieAccentOverrideComponent>(victim), Is.True);
                Assert.That(server.EntMan.GetComponent<ZombieAccentOverrideComponent>(victim).Accent,
                    Is.EqualTo("OperativeHiddenLobotomy"));
                Assert.That(server.EntMan.HasComponent<ReplacementAccentComponent>(victim), Is.True,
                    "zombification must apply the configured lobotomy replacement accent");
                Assert.That(server.EntMan.HasComponent<NativeAntagComponent>(victim), Is.False,
                    "patients must not inherit the operative's continuous disclosure radio");
                Assert.That(server.EntMan.HasComponent<HandsComponent>(victim), Is.True,
                    "reconditioned people retain hands and can wield weapons");
                var retainedHands = server.EntMan.GetComponent<HandsComponent>(victim);
                Assert.That(retainedHands.Count, Is.GreaterThan(0),
                    "reconditioned people must retain their real hand slots");
                Assert.That(retainedHands.ActiveHandId, Is.Not.Null,
                    "reconditioned people must retain an active hand");
                Assert.That(server.EntMan.GetComponent<OperativeHiddenPuppetVisualsComponent>(victim).State,
                    Is.EqualTo(OperativeHiddenPuppetVisualState.Linked),
                    "the implanted receiver must visibly show a live link");
                Assert.That(server.System<TagSystem>().HasTag(victim, CannotSuicideTag), Is.True,
                    "the receiver must prevent the conscious patient from taking their own life");
                Assert.That(condition.Current, Is.EqualTo(1),
                    "the mind-owned objective mirror must advance only after the atomic conversion commits");
                Assert.That(condition.DistinctTargets, Does.Contain(victim));
            });

            var hands = server.System<SharedHandsSystem>();
            var pen = server.EntMan.SpawnEntity("Pen", map.GridCoords);
            Assert.That(hands.TryPickup(victim, pen), Is.True,
                "a reconditioned patient's retained hands must remain functional");
            Assert.That(hands.TryDrop(victim, pen, checkActionBlocker: false), Is.True);
            server.EntMan.DeleteEntity(pen);

            var accentSource = localization.GetString("operative-hidden-lobotomy-word-1");
            var accentReplacement = localization.GetString("operative-hidden-lobotomy-replacement-1");
            var distorted = server.System<ReplacementAccentSystem>()
                .ApplyReplacements(accentSource, "OperativeHiddenLobotomy", victim);
            Assert.That(distorted, Is.EqualTo(accentReplacement),
                "the lobotomy accent must visibly distort patient chat");

            var language = server.ProtoMan.Index(PatientLanguage);
            var spoke = new EntitySpokeEvent(victim, distorted, null, false, language);
            server.EntMan.EventBus.RaiseLocalEvent(victim, spoke);
            var native = server.EntMan.GetComponent<NativeAntagComponent>(operative);
            Assert.That(native.PatientSpeechStreams.TryGetValue(victim, out var patientSpeech), Is.True,
                "patient speech must play the operative's short speech collection");
            var patientAudio = server.EntMan.GetComponent<AudioComponent>(patientSpeech);
            Assert.Multiple(() =>
            {
                Assert.That(patientAudio.FileName,
                    Does.StartWith("/Audio/_Whiskey/OperativeHidden/operative_hidden_speech"));
                Assert.That(patientAudio.FileName,
                    Is.Not.EqualTo("/Audio/_Whiskey/OperativeHidden/operative_hidden_position.ogg"));
                Assert.That(patientAudio.Params.MaxDistance, Is.EqualTo(5f));
                Assert.That(patientAudio.Flags.HasFlag(AudioFlags.NoOcclusion), Is.False);
            });
        });

        await server.WaitAssertion(() =>
        {
            var kill = new NativeAntagTargetActionEvent
            {
                EventType = (uint) NativeAntagEventType.PatientKillAction,
                Target = victim,
            };
            server.EntMan.EventBus.RaiseLocalEvent(operative, kill);

            var native = server.EntMan.GetComponent<NativeAntagComponent>(operative);
            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.HasComponent<NativeAntagPatientComponent>(victim), Is.False);
                Assert.That(server.EntMan.HasComponent<ZombieComponent>(victim), Is.False);
                Assert.That(native.CountedTargets[1], Does.Contain(victim),
                    "the durable per-body identity set must survive patient termination");
                Assert.That(native.PatientSpeechStreams.ContainsKey(victim), Is.False,
                    "terminating a patient must stop and forget its short speech stream");
            });

            var hands = server.System<SharedHandsSystem>();
            var cautery = server.EntMan.SpawnEntity("Cautery", map.GridCoords);
            Assert.That(hands.TryPickup(operative, cautery, activeHand), Is.True);
            var reconversion = new NativeAntagTargetActionEvent
            {
                EventType = (uint) NativeAntagEventType.ProcedureAction,
                Target = victim,
            };
            server.EntMan.EventBus.RaiseLocalEvent(operative, reconversion);
            Assert.That(hands.TryDrop(operative, cautery, checkActionBlocker: false), Is.True);
            server.EntMan.DeleteEntity(cautery);
        });

        await server.WaitRunTicks(ticksPerStage);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.HasComponent<NativeAntagPatientComponent>(victim), Is.False,
                    "a terminated patient must never be accepted as a fresh conversion target");
                Assert.That(server.EntMan.HasComponent<ZombieComponent>(victim), Is.False,
                    "reconversion of the same corpse must fail before the first surgical stage");
            });

            // Exercise the reviewer's exact terminal state: a completed 3/3
            // counter, mind transferred to a ghost, and the native body deleted.
            var condition = server.EntMan.GetComponent<NativeCounterConditionComponent>(conversionObjective);
            condition.Current = 3;
            var ghost = server.EntMan.SpawnEntity("MobObserver", map.GridCoords);
            server.System<MindSystem>().TransferTo(
                operativeMind,
                ghost,
                ghostCheckOverride: true,
                mind: operativeMind.Comp);
            server.EntMan.DeleteEntity(operative);
            var progress = server.System<ObjectivesSystem>()
                .GetInfo(conversionObjective, operativeMind.Owner, operativeMind.Comp)?.Progress;
            Assert.That(progress, Is.EqualTo(1f),
                "3/3 progress must survive ghosting and destruction of the native scenario handle");

            Assert.That(operativeMind.Comp.Objectives.Remove(conversionObjective), Is.True);
            server.EntMan.Dirty(operativeMind);
            server.EntMan.DeleteEntity(conversionObjective);
            server.EntMan.DeleteEntity(victim);
            server.System<SharedMapSystem>().DeleteMap(map.MapId);
        });
    }

    [Test]
    public async Task PuppetReceiverStaysOnHeadBelowInHandSprites()
    {
        var pair = Pair;
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        EntityUid patient = default;

        await server.WaitAssertion(() =>
        {
            patient = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            server.EntMan.EnsureComponent<OperativeHiddenPuppetVisualsComponent>(patient);
            var mind = server.System<MindSystem>().GetOrCreateMind(pair.Player!.UserId);
            server.System<MindSystem>().TransferTo(mind, patient, ghostCheckOverride: true, mind: mind.Comp);

            var pen = server.EntMan.SpawnEntity("Pen", map.GridCoords);
            Assert.That(server.System<SharedHandsSystem>().TryPickup(patient, pen), Is.True);
        });

        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
        {
            var clientPatient = client.EntMan.GetEntity(server.EntMan.GetNetEntity(patient));
            var sprite = client.EntMan.GetComponent<SpriteComponent>(clientPatient);
            var spriteSystem = client.System<SpriteSystem>();
            Assert.That(spriteSystem.LayerMapTryGet(
                (clientPatient, sprite),
                OperativeHiddenPuppetVisualLayers.HeadController,
                out var controllerLayer,
                false), Is.True);
            Assert.That(spriteSystem.TryGetLayer(
                (clientPatient, sprite),
                controllerLayer,
                out var controller,
                false), Is.True);
            Assert.That(controller.Offset, Is.EqualTo(new Vector2(0f, 6f / 32f)),
                "the receiver art must be raised from the torso to the head");

            var hands = client.EntMan.GetComponent<HandsComponent>(clientPatient);
            var inHandLayers = hands.RevealedLayers.Values.SelectMany(layers => layers).ToArray();
            Assert.That(inHandLayers, Is.Not.Empty);
            foreach (var key in inHandLayers)
            {
                Assert.That(spriteSystem.LayerMapGet((clientPatient, sprite), key),
                    Is.GreaterThan(controllerLayer),
                    "held items and hands must render above the receiver layer");
            }
        });

        await server.WaitAssertion(() => server.System<SharedMapSystem>().DeleteMap(map.MapId));
    }

    [Test]
    public async Task NeuralLeashOnlySlowsPatientsOutsideRange()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        EntityUid master = default;
        EntityUid patient = default;
        Vector2 startingPosition = default;

        await server.WaitAssertion(() =>
        {
            master = server.EntMan.SpawnEntity(
                "MobHuman",
                map.GridCoords.Offset(new Vector2(12f, 0f)));
            patient = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            var leash = server.EntMan.EnsureComponent<NativeAntagPatientComponent>(patient);
            leash.Master = master;
            startingPosition = server.System<SharedTransformSystem>().GetMapCoordinates(patient).Position;
        });

        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.HasComponent<ThrownItemComponent>(patient), Is.False,
                "the neural leash must never throw an out-of-range patient");
            Assert.That(server.EntMan.HasComponent<KnockedDownComponent>(patient), Is.False,
                "the neural leash must never knock down an out-of-range patient");
            Assert.That(server.System<SharedTransformSystem>().GetMapCoordinates(patient).Position,
                Is.EqualTo(startingPosition),
                "the neural leash must not move or teleport an out-of-range patient");

            var leash = server.EntMan.GetComponent<NativeAntagPatientComponent>(patient);
            var movement = server.EntMan.GetComponent<MovementSpeedModifierComponent>(patient);
            Assert.That(leash.OutOfRange, Is.True);
            Assert.That(movement.WalkSpeedModifier, Is.EqualTo(0.5f));
            Assert.That(movement.SprintSpeedModifier, Is.EqualTo(0.5f));

            server.System<SharedTransformSystem>().SetCoordinates(
                master,
                map.GridCoords.Offset(new Vector2(5f, 0f)));
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            var leash = server.EntMan.GetComponent<NativeAntagPatientComponent>(patient);
            var movement = server.EntMan.GetComponent<MovementSpeedModifierComponent>(patient);
            Assert.That(leash.OutOfRange, Is.False);
            Assert.That(movement.WalkSpeedModifier, Is.EqualTo(1f));
            Assert.That(movement.SprintSpeedModifier, Is.EqualTo(1f));
            server.System<SharedMapSystem>().DeleteMap(map.MapId);
        });
    }

    [Test]
    public async Task TwentyPlayerScenarioSelectsOneOperativeAndPreservesMind()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var population = new List<ICommonSession> { pair.Player! };
        for (var i = 1; i < 20; i++)
            population.Add(await server.AddDummySession($"operative_population_{i:D2}"));

        await server.WaitAssertion(() =>
        {
            var session = pair.Player!;
            var originalBody = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            var originalProfile = server.EntMan.GetComponent<HumanoidProfileComponent>(originalBody);
            originalProfile.Species = "Reptilian";

            var mindSystem = server.System<MindSystem>();
            var roleSystem = server.System<RoleSystem>();
            var mind = mindSystem.GetOrCreateMind(session.UserId);
            mindSystem.TransferTo(mind, originalBody, ghostCheckOverride: true, mind: mind.Comp);
            Assert.That(session.AttachedEntity, Is.EqualTo(originalBody));

            foreach (var crewSession in population.Skip(1))
            {
                var crewBody = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
                var crewMind = mindSystem.GetOrCreateMind(crewSession.UserId);
                mindSystem.TransferTo(crewMind, crewBody, ghostCheckOverride: true, mind: crewMind.Comp);
            }
            Assert.That(server.PlayerMan.PlayerCount, Is.EqualTo(20),
                "the selection scenario must run at the dynamic rule's exact minimum population");

            var rule = server.EntMan.SpawnEntity(OperativeRule, map.GridCoords);
            var selection = server.EntMan.GetComponent<AntagSelectionComponent>(rule);
            var loadedGrids = new RuleLoadedGridsEvent(map.MapId, new[] { map.Grid.Owner });
            server.EntMan.EventBus.RaiseLocalEvent(rule, ref loadedGrids);
            // The loaded striker shuttle uses the generic nuclear-operative spawn marker.
            server.EntMan.SpawnEntity("SpawnPointNukies", map.GridCoords);

            var specifier = server.ProtoMan.Index(OperativeSpecifier);
            Assert.That(server.System<AntagSelectionSystem>()
                .TryMakeAntag((rule, selection), specifier, session, checkPref: false), Is.True);

            Assert.That(mind.Comp.CurrentEntity, Is.Not.Null);
            var operative = mind.Comp.CurrentEntity!.Value;
            var operativeProfile = server.EntMan.GetComponent<HumanoidProfileComponent>(operative);
            var roles = roleSystem.MindGetAllRoleInfo(mind.Owner);
            var objectivesSystem = server.System<ObjectivesSystem>();
            var objectiveInfo = mind.Comp.Objectives
                .Select(objective => objectivesSystem.GetInfo(objective, mind.Owner, mind.Comp))
                .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(operative, Is.Not.EqualTo(originalBody));
                Assert.That(operativeProfile.Species.Id, Is.EqualTo("Ovinia"));
                Assert.That(mind.Comp.CurrentEntity, Is.EqualTo(operative));
                Assert.That(session.AttachedEntity, Is.EqualTo(operative));
                Assert.That(roleSystem.MindIsAntagonist(mind.Owner), Is.True);
                Assert.That(roles.Count(role => role.Prototype == "OperativeHidden"), Is.EqualTo(1));
                Assert.That(mind.Comp.Objectives, Has.Count.EqualTo(2));
                Assert.That(objectiveInfo, Has.Length.EqualTo(2));
                Assert.That(objectiveInfo.All(info => info is not null), Is.True,
                    "both objectives must provide title, description, icon, and progress to the character UI");
                Assert.That(server.EntMan.GetComponent<MetaDataComponent>(operative).EntityName, Is.Not.Empty);
                Assert.That(server.EntMan.EntityQuery<NativeAntagComponent>().Count(), Is.EqualTo(1),
                    "the fixed antag count must produce exactly one native scenario owner at 20 players");
            });

            server.EntMan.DeleteEntity(rule);
            server.System<SharedMapSystem>().DeleteMap(map.MapId);
        });

        foreach (var dummy in population.Skip(1))
            await server.RemoveDummySession(dummy, removeUser: true);
    }

    [Test]
    public async Task PositionalRadioIsBroadcastToEveryoneExceptOperative()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var operativeSession = await server.AddDummySession("operative_radio_source");
        NetEntity disclosureNet = default;
        NetEntity speechNet = default;

        await server.WaitAssertion(() =>
        {
            var listener = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            var listenerSession = pair.Player!;
            server.PlayerMan.SetAttachedEntity(listenerSession, listener);

            var operative = server.EntMan.SpawnEntity(OperativeBody, map.GridCoords);
            server.PlayerMan.SetAttachedEntity(operativeSession, operative);

            var native = server.EntMan.GetComponent<NativeAntagComponent>(operative);
            Assert.That(native.AudioStreams.TryGetValue(1, out var disclosureStream), Is.True,
                "attaching a player must start the continuous disclosure radio");
            AssertBroadcastRecipientsExceptOperative(server, disclosureStream, operative, listener);
            disclosureNet = server.EntMan.GetNetEntity(disclosureStream);

            var language = server.ProtoMan.Index(UniversalLanguage);
            var spoke = new EntitySpokeEvent(operative, "teste", null, false, language);
            server.EntMan.EventBus.RaiseLocalEvent(operative, spoke);

            Assert.That(native.AudioStreams.TryGetValue(2, out var speechStream), Is.True,
                "speaking must start the random 1.5/2 second radio cue");
            AssertBroadcastRecipientsExceptOperative(server, speechStream, operative, listener);
            speechNet = server.EntMan.GetNetEntity(speechStream);
        });

        await pair.RunUntilSynced();
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(pair.Client.EntMan.TryGetEntity(disclosureNet, out var disclosureClient), Is.True,
                "the listening client must receive the continuous radio entity");
            Assert.That(pair.Client.EntMan.HasComponent<AudioComponent>(disclosureClient), Is.True);
            Assert.That(pair.Client.EntMan.TryGetEntity(speechNet, out var speechClient), Is.True,
                "the listening client must receive the speech radio entity");
            Assert.That(pair.Client.EntMan.HasComponent<AudioComponent>(speechClient), Is.True);
        });

        await server.WaitAssertion(() =>
        {
            server.PlayerMan.SetAttachedEntity(operativeSession, null);
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.System<SharedMapSystem>().DeleteMap(map.MapId);
        });
        await server.RemoveDummySession(operativeSession, removeUser: true);
    }

    private static void AssertBroadcastRecipientsExceptOperative(
        RobustIntegrationTest.ServerIntegrationInstance server,
        EntityUid stream,
        EntityUid operative,
        EntityUid listener)
    {
        var audio = server.EntMan.GetComponent<AudioComponent>(stream);
        var streamTransform = server.EntMan.GetComponent<TransformComponent>(stream);
        var operativeTransform = server.EntMan.GetComponent<TransformComponent>(operative);

        Assert.Multiple(() =>
        {
            Assert.That(streamTransform.ParentUid, Is.EqualTo(operativeTransform.MapUid),
                "the sound must be map-anchored so it does not depend on operative PVS");
            Assert.That(audio.Params.MaxDistance, Is.EqualTo(5f),
                "the disclosure radio must not be audible beyond five tiles");
            Assert.That(audio.Params.ReferenceDistance, Is.EqualTo(0.8f));
            Assert.That(audio.Params.Volume, Is.EqualTo(-7f));
            Assert.That(audio.Flags.HasFlag(AudioFlags.NoOcclusion), Is.False,
                "walls must keep the engine's low-pass occlusion enabled");
            Assert.That(audio.IncludedEntities, Does.Not.Contain(operative),
                "the Hidden Operative must not receive their own disclosure radio");
            Assert.That(audio.IncludedEntities, Does.Contain(listener),
                "a second attached player must receive the radio audio state");
        });
    }

    [Test]
    public async Task LoneOpsStillSelectsWithRoleUplinkTcAndObjectives()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var session = pair.Player!;
            var originalBody = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            var mindSystem = server.System<MindSystem>();
            var roleSystem = server.System<RoleSystem>();
            var factions = server.System<NpcFactionSystem>();
            var mind = mindSystem.GetOrCreateMind(session.UserId);
            mindSystem.TransferTo(mind, originalBody, ghostCheckOverride: true, mind: mind.Comp);

            var rule = server.EntMan.SpawnEntity(LoneOpsRule, map.GridCoords);
            var selection = server.EntMan.GetComponent<AntagSelectionComponent>(rule);
            var loadedGrids = new RuleLoadedGridsEvent(map.MapId, new[] { map.Grid.Owner });
            server.EntMan.EventBus.RaiseLocalEvent(rule, ref loadedGrids);
            server.EntMan.SpawnEntity("SpawnPointNukies", map.GridCoords);

            var specifier = server.ProtoMan.Index(LoneOpsSpecifier);
            var gear = server.ProtoMan.Index(LoneOpsGear);
            Assert.That(specifier.StartingGear, Is.EqualTo(LoneOpsGear));
            Assert.That(gear.Equipment["pocket2"].Id, Is.EqualTo("LoneOpsUplink225TC"));

            Assert.That(server.System<AntagSelectionSystem>()
                .TryMakeAntag((rule, selection), specifier, session, checkPref: false), Is.True);

            Assert.That(mindSystem.TryGetMind(session, out var loneMindId, out var loneMindComponent), Is.True);
            var loneMind = new Entity<MindComponent>(loneMindId, loneMindComponent);
            Assert.That(loneMind.Comp.CurrentEntity, Is.Not.Null);
            var loneOperative = loneMind.Comp.CurrentEntity!.Value;
            var uplinkUid = server.EntMan.SpawnEntity("LoneOpsUplink225TC", map.GridCoords);
            var store = server.EntMan.GetComponent<StoreComponent>(uplinkUid);

            Assert.Multiple(() =>
            {
                Assert.That(loneOperative, Is.Not.EqualTo(originalBody));
                Assert.That(server.EntMan.HasComponent<NukeOperativeComponent>(loneOperative), Is.True);
                Assert.That(roleSystem.MindHasRole<NukeopsRoleComponent>(loneMind.Owner), Is.True);
                Assert.That(factions.IsMember(loneOperative, SyndicateFaction), Is.True);
                Assert.That(server.EntMan.GetComponent<MetaDataComponent>(uplinkUid).EntityPrototype?.ID,
                    Is.EqualTo("LoneOpsUplink225TC"));
                Assert.That(store.Balance[(ProtoId<CurrencyPrototype>) "Telecrystal"],
                    Is.EqualTo(FixedPoint2.New(225)));
                Assert.That(loneMind.Comp.Objectives, Is.Not.Empty);
            });

            server.System<SharedMapSystem>().DeleteMap(map.MapId);
        });
    }
}
