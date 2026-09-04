// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey;

[TestFixture]
public sealed class InsurgentFactionTest : GameTest
{
    private static readonly ProtoId<NpcFactionPrototype> Insurgent = "Insurgent";
    private const string AntiInsurgent = "InsurgentDestroyer9000";

    /// <summary>
    /// Só a torreta anti-insurgente reage sozinha.
    /// </summary>
    /// <remarks>
    /// Insurgente atacado por todo bicho, robô e tripulante assim que pisa na
    /// estação perde o disfarce antes de qualquer jogador perceber quem ele é.
    ///
    /// Este teste já se perdeu uma vez num upstream, e do jeito difícil de
    /// enxergar: o arquivo em conflito estava resolvido certo, e a hostilidade
    /// voltou por Partials/ai_factions.yml, que chegou SEM conflito nenhum
    /// porque era arquivo novo. Se ele reprovar depois de um merge, procure lá
    /// antes de procurar em qualquer outro lugar.
    ///
    /// Desfazer com um partial nosso usando !Remove não funciona: partial sobre
    /// partial não empilha, e isso foi medido com este mesmo teste.
    /// </remarks>
    [Test]
    public async Task ApenasTorretaAntiInsurgenteAtacaAutomaticamente()
    {
        await Server.WaitAssertion(() =>
        {
            var hostis = Server.ProtoMan.EnumeratePrototypes<NpcFactionPrototype>()
                .Where(faction => faction.Hostile.Contains(Insurgent))
                .Select(faction => faction.ID)
                .ToArray();

            Assert.That(hostis, Is.EquivalentTo(new[] { AntiInsurgent }));
        });
    }
}
