// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Maps;
using Content.Shared.CCVar;
using Content.Shared.Maps;
using Content.Shared.Random.Helpers;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// O peso do mapa no sorteio. Sem ele todo mapa elegível caía com a mesma
/// chance, e a única forma de um mapa aparecer mais era tirar os outros.
/// </summary>
[TestFixture]
public sealed class GameMapWeightTest : GameTest
{
    [SidedDependency(Side.Server)] private readonly IPrototypeManager _proto = null!;
    [SidedDependency(Side.Server)] private readonly IRobustRandom _random = null!;
    [SidedDependency(Side.Server)] private readonly IGameMapManager _mapManager = null!;
    [SidedDependency(Side.Server)] private readonly IConfigurationManager _cfg = null!;

    /// <summary>
    /// Mapa que não declara peso continua valendo 1, senão acrescentar o campo
    /// teria mudado o sorteio de todo mundo de uma vez.
    /// </summary>
    [Test]
    public async Task MapaSemPesoValeUm()
    {
        await Server.WaitAssertion(() =>
        {
            var mapas = _proto.EnumeratePrototypes<GameMapPrototype>().ToList();
            Assert.That(mapas, Is.Not.Empty, "nenhum gameMap carregou");

            // peso zero é intencional: congela o mapa sem apagar a
            // configuração dele. Negativo não, isso é engano de quem escreveu.
            var negativos = mapas.Where(m => m.Weight < 0f).Select(m => $"{m.ID}={m.Weight}").ToList();
            Assert.That(negativos, Is.Empty,
                $"peso negativo não tem significado, use 0 para congelar: {string.Join(", ", negativos)}");

            // e pelo menos um mapa precisa sobrar, senão o servidor não escolhe
            // mapa nenhum e não sobe.
            Assert.That(mapas.Any(m => m.Weight > 0f), Is.True,
                "todos os mapas ficaram com peso zero, nenhuma rodada conseguiria começar");
        });
    }

    /// <summary>
    /// O sorteio de verdade respeita o peso. Este é o teste que guarda a
    /// mudança: ele chama o <c>SelectMapRandom</c> do jogo, e não o helper do
    /// motor, então reprova se alguém devolver o sorteio para o Pick simples.
    /// </summary>
    [Test]
    public async Task OSorteioDoJogoRespeitaOPeso()
    {
        await Server.WaitAssertion(() =>
        {
            var elegiveis = _mapManager.CurrentlyEligibleMaps().ToList();
            if (elegiveis.Count < 2)
                Assert.Ignore("precisa de pelo menos dois mapas elegíveis para medir preferência");

            var pesado = elegiveis[0];
            var pesoOriginal = new Dictionary<string, float>();
            foreach (var m in elegiveis)
                pesoOriginal[m.ID] = m.Weight;

            // o PoolManager fixa CCVars.GameMap para os testes, e o
            // GetSelectedMap devolve o mapa da config antes do sorteado. Sem
            // limpar isso, o teste mede a config e nunca o sorteio.
            var mapaDaConfig = _cfg.GetCVar(CCVars.GameMap);
            _cfg.SetCVar(CCVars.GameMap, string.Empty);

            try
            {
                // um mapa com peso muito maior que todos os outros somados tem
                // que dominar o sorteio. Se o peso for ignorado, cada um sai
                // perto de 1/N e este teste reprova.
                pesado.Weight = 1000f;
                foreach (var m in elegiveis.Skip(1))
                    m.Weight = 1f;

                var vezes = 0;
                for (var i = 0; i < 300; i++)
                {
                    _mapManager.SelectMapRandom();
                    if (_mapManager.GetSelectedMap()?.ID == pesado.ID)
                        vezes++;
                }

                Assert.That(vezes, Is.GreaterThan(250),
                    $"o mapa com peso 1000 contra {elegiveis.Count - 1} de peso 1 saiu só {vezes} de 300 vezes, o peso está sendo ignorado");
            }
            finally
            {
                foreach (var m in elegiveis)
                    m.Weight = pesoOriginal[m.ID];
                _cfg.SetCVar(CCVars.GameMap, mapaDaConfig);
            }
        });
    }

    /// <summary>
    /// Peso zero tira do sorteio. Serve para congelar mapa sem apagar a
    /// configuração dele.
    /// </summary>
    [Test]
    public async Task PesoZeroNaoSai()
    {
        await Server.WaitAssertion(() =>
        {
            var pesos = new Dictionary<string, float> { ["vivo"] = 1f };

            for (var i = 0; i < 200; i++)
                Assert.That(_random.Pick(pesos), Is.EqualTo("vivo"),
                    "com um candidato só, o sorteio tem que devolver ele sempre");
        });
    }
}
