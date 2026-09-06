# Blood Cult: licensing and attribution

The Blood Cult is ported code, not original work. Every `.cs` file that this port adds carries
`// SPDX-License-Identifier: AGPL-3.0-or-later` and a pointer back to this file.

## Chain of origin

| Step | Repository | License |
| --- | --- | --- |
| Original implementation | [WWhiteDreamProject/wwdpublic](https://github.com/WWhiteDreamProject/wwdpublic) | AGPL-3.0-or-later |
| Intermediate port | Mini-Station | AGPL-3.0-or-later (inherited) |
| This port | Whiskey-Station/Whiskey-Station-14 | AGPL-3.0-or-later |

The intermediate step is identified from what came across with the code: the `_Mini/` namespaces
(`Content.Client/_Mini/BloodCult/`, `Content.Server/_Mini/BloodCult/`) and the `# Mini:` comments left
in the prototypes. If a maintainer knows the hop went through Einstein Engines instead, or through both,
this table is the place to correct it.

Because the original is AGPL-3.0-or-later, everything derived from it stays AGPL-3.0-or-later regardless
of the license the rest of the repository uses.

## Files covered

- `Content.Server/WhiteDream/**`
- `Content.Shared/WhiteDream/**`
- `Content.Client/WhiteDream/**`
- `Content.Client/_Mini/BloodCult/**`, `Content.Server/_Mini/BloodCult/**`
- `Content.Server/Roles/BloodCultistRoleComponent.cs`
- `Content.Shared/Actions/Events/ActionGettingDisabledEvent.cs`
- `Content.Shared/Antag/IAntagStatusIconComponent.cs`
- `Content.Shared/Magic/ISpeakSpell.cs`, `Content.Shared/Magic/Events/SpeakSpellEvent.cs`

Pre-existing upstream files that this port only *modifies* (`SharedInteractionSystem`, `RoundEndSystem`,
`PullingSystem`, `SharedDoorSystem`, `BlockingSystem`, and so on) keep their original headers. The added
sections there are marked inline with `// <WhiteDream>` … `// </WhiteDream>`.

## Open question: funky-station

The veil progression (the collective chant, the blood rift and its summoning runes, the final ritual,
and the reagent the rift bleeds) is derived from funky-station's blood cult.

**funky-station declares no license.** That is unresolved, and it needs a maintainer decision before this
merges. The affected code is confined to these files, so it can be dropped without touching the White Dream
port underneath:

- `Content.Server/WhiteDream/BloodCult/Gamerule/BloodCultRuleSystem.Veil.cs`
- `Content.Server/WhiteDream/BloodCult/Gamerule/BloodCultRuleSystem.Progression.cs`
- `Content.Server/WhiteDream/BloodCult/Rift/**`
- `Resources/Prototypes/WhiteDream/Entities/Objects/Structures/Cult/rift.yml`
- `Resources/Prototypes/WhiteDream/Reagents/blood_cult.yml`

## Assets

Sounds and sprites under `Resources/Audio/WhiteDream/` and `Resources/Textures/WhiteDream/` come from the
same White Dream port. Sprites added later from
[funky-station PR #2426](https://github.com/funky-station/funky-station/pull/2426) are noted in the
`meta.json` of the `.rsi` folders they belong to.

The cult leader aura in `Resources/Textures/WhiteDream/BloodCult/Effects/leader_aura.rsi` is a red
recolor of tgstation's heretic aura, imported through Goobstation. Its CC-BY-SA-3.0 attribution and
source commit are recorded in that folder's `meta.json`.

The cult pylon replacement and cult crossbow assets under `Resources/Textures/_Whiskey/BloodCult/`
were imported from [Monolith](https://github.com/Monolith-Station/Monolith). Their CC-BY-SA-3.0
authorship and upstream sprite sources are preserved in each RSI's `meta.json`. The crossbow weapon
and bolt prototypes are adapted from Monolith commits `31b5cd10d86b8c0cdde1e654dbcba4230ddb0640`
and `8aac82c006cb84f2ac608ab4aa6e990b11829eec` to this codebase's bow and damage APIs.

The cult member status icon, runic metal, cult shade and leader halo under
`Resources/Textures/_Whiskey/BloodCult/` were imported from
[Funky Station](https://github.com/funky-station/funky-station). The icon, metal and shade use commit
`1d33e834c087ba7d481e600de9a45dbbc7684042`; the halo uses its original introduction commit
`f5e00a9ec875112e1fc62a4f084706540f41e943`. Their CC-BY-SA-3.0 credits are preserved in each RSI's
`meta.json`.

The Blood Bolt Barrage and Blood Beam greater rites are adapted from
[BeeStation/BeeStation-Hornet](https://github.com/BeeStation/BeeStation-Hornet), including the
25-shot barrage and the charged 12-beam fan behavior. The SS13 implementation is AGPL-3.0 and was
consulted at commit `16ce653a625569c1633b2a33f03aab791e99f177`; this port uses the existing SS14 projectile,
damage, do-after and tile systems instead of copying BYOND-specific code.
