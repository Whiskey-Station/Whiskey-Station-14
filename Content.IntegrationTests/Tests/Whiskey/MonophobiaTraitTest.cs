// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._Whiskey.Pressure;
using Content.Shared.Traits;
using NUnit.Framework;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// A primeira fobia do fork, portada do quirk monophobia do /tg/station.
/// </summary>
[TestFixture]
public sealed class MonophobiaTraitTest : GameTest
{
    [SidedDependency(Side.Server)] private readonly ILocalizationManager _loc = null!;

    private const string Traco = "Monophobia";
    private static readonly ProtoId<PressureSourcePrototype> Solidao = "WhiskeyPressaoSolidao";

    private TraitPrototype Prototipo => Server.ProtoMan.Index<TraitPrototype>(Traco);

    /// <summary>
    /// O traço tem que dar pressão, senão não faz nada.
    /// </summary>
    [Test]
    public async Task OTracoDaPressao()
    {
        await Server.WaitAssertion(() =>
        {
            var tem = Prototipo.Components.Values
                .Any(c => c.Component is MentalPressureComponent);

            Assert.That(tem, Is.True,
                "sem MentalPressureComponent o traço não sente coisa nenhuma");
        });
    }

    /// <summary>
    /// Monofobia é fobia, e não sensibilidade geral.
    /// </summary>
    /// <remarks>
    /// Este é o teste que separa este traço do Sensível. Se a lista de
    /// suscetibilidade vier vazia, o traço passa a sentir morte e dor junto, e
    /// vira o Sensível com outro nome: é exatamente o engano que a lista foi
    /// escrita para impedir.
    /// </remarks>
    [Test]
    public async Task MonofobiaSenteSolidaoENadaMais()
    {
        await Server.WaitAssertion(() =>
        {
            var entrada = Prototipo.Components.Values
                .First(c => c.Component is MentalPressureComponent);
            var comp = (MentalPressureComponent) entrada.Component;

            TestContext.Out.WriteLine(
                "monofobia sente: " + string.Join(", ", comp.SusceptibleTo.Select(f => f.Id)));

            Assert.Multiple(() =>
            {
                Assert.That(comp.SusceptibleTo, Is.Not.Empty,
                    "lista vazia quer dizer TODAS as fontes, e aí monofobia vira o Sensível");

                Assert.That(comp.SusceptibleTo, Does.Contain(Solidao),
                    "medo de ficar sozinho tem que sentir a solidão");

                Assert.That(comp.SusceptibleTo, Has.Count.EqualTo(1),
                    "no TG cada medo é um trauma que não conhece os outros");
            });
        });
    }

    /// <summary>
    /// Nome e descrição existem nos dois idiomas.
    /// </summary>
    /// <remarks>
    /// LocId que não existe não dá erro: aparece a própria chave na tela de
    /// escolha de traço, e só quem olhar percebe.
    /// </remarks>
    [Test]
    public async Task OTracoTemTextoNosDoisIdiomas()
    {
        await Server.WaitAssertion(() =>
        {
            foreach (var chave in new[] { "trait-monophobia-name", "trait-monophobia-desc" })
            {
                Assert.That(_loc.TryGetString(chave, out var texto), Is.True,
                    $"a chave {chave} não existe, e o jogador veria o id cru na tela");
                Assert.That(texto, Is.Not.Empty);
            }
        });
    }
}
