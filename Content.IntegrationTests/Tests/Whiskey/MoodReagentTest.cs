// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Shared._EinsteinEngines.Mood;
using Content.Shared._Whiskey.EntityEffects;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// Cobre TODO reagente que mexe em humor, e não um reagente nomeado.
///
/// Escrito varrendo pelo mesmo motivo do <see cref="PeriodicMoodContentTest"/>:
/// o defeito não é de um reagente, é da forma. Um id de modificador escrito
/// errado dentro de um efeito de reagente não quebra build, não quebra linter,
/// e em jogo a pessoa toma o remédio e nada acontece, sem erro no log.
/// </summary>
[TestFixture]
public sealed class MoodReagentTest : GameTest
{
    /// <summary>
    /// Junta todo par de reagente e efeito de humor declarado nele.
    /// </summary>
    private static List<(string Reagente, AdjustMood Efeito)> Varrer(IPrototypeManager protos)
    {
        var achados = new List<(string, AdjustMood)>();

        foreach (var reagente in protos.EnumeratePrototypes<ReagentPrototype>())
        {
            if (reagente.Metabolisms is not { } metabolismos)
                continue;

            foreach (var (_, entrada) in metabolismos.Metabolisms)
            {
                foreach (var efeito in entrada.Effects)
                {
                    if (efeito is AdjustMood humor)
                        achados.Add((reagente.ID, humor));
                }
            }
        }

        return achados;
    }

    /// <summary>
    /// Existe pelo menos um reagente que mexe em humor.
    ///
    /// Sem isto, o teste abaixo varreria lista vazia e passaria sem olhar nada.
    /// </summary>
    [Test]
    public async Task ExisteAlgumReagenteDeHumor()
    {
        List<(string, AdjustMood)> achados = null!;
        await Server.WaitPost(() => achados = Varrer(Server.ProtoMan));

        Assert.That(achados, Is.Not.Empty,
            "nenhum reagente mexe em humor, então este teste não confere nada");
    }

    /// <summary>
    /// Todo efeito de humor em reagente aponta para um modificador que existe.
    /// </summary>
    [Test]
    public async Task TodoReagenteApontaParaModificadorQueExiste()
    {
        var protos = Server.ProtoMan;
        var problemas = new List<string>();

        await Server.WaitPost(() =>
        {
            foreach (var (reagente, efeito) in Varrer(protos))
            {
                if (!protos.HasIndex<MoodEffectPrototype>(efeito.Effect))
                    problemas.Add($"o reagente {reagente} aponta para o modificador {efeito.Effect}, que não existe");
            }
        });

        Assert.That(problemas, Is.Empty, string.Join(" | ", problemas));
    }
}
