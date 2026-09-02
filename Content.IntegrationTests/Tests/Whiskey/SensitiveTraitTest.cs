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
/// O traço que liga a pressão mental a alguém.
/// </summary>
/// <remarks>
/// Antes dele, o sistema inteiro era inalcançável em jogo: nenhum traço dava
/// <c>MentalPressureComponent</c>, então nem o motor nem os sintomas nem os
/// gatilhos chegavam a rodar para jogador nenhum.
/// </remarks>
[TestFixture]
public sealed class SensitiveTraitTest : GameTest
{
    [SidedDependency(Side.Server)] private readonly ILocalizationManager _loc = null!;

    private static readonly ProtoId<TraitPrototype> Sensivel = "Sensitive";

    /// <summary>
    /// O traço dá o componente, que é a única coisa que ele precisa fazer.
    /// </summary>
    [Test]
    public async Task OTracoDaPressaoMental()
    {
        await Server.WaitAssertion(() =>
        {
            var traco = Server.ProtoMan.Index(Sensivel);
            var nome = Server.EntMan.ComponentFactory.GetComponentName(typeof(MentalPressureComponent));

            Assert.That(traco.Components.TryGetComponent(nome, out _), Is.True,
                "sem o componente o traço não liga coisa nenhuma");

            Assert.Multiple(() =>
            {
                Assert.That(_loc.HasString(traco.Name!), Is.True, "falta o nome no locale");
                Assert.That(_loc.HasString(traco.Description!), Is.True, "falta a descrição no locale");
            });
        });
    }

    /// <summary>
    /// Alguém no jogo precisa poder sentir pressão, senão o sistema é código
    /// morto por mais completo que esteja.
    /// </summary>
    /// <remarks>
    /// Este é o teste que eu queria ter tido antes: as PRs do sistema, do motor
    /// e das duas fontes passaram todas, e mesmo assim nenhum jogador jamais
    /// teria pressão mental, porque faltava exatamente isto.
    ///
    /// Ele não cobra que seja o Sensitive: cobra que exista alguém. Se um dia a
    /// pressão for para outro traço, ou para vários, o teste continua valendo.
    /// </remarks>
    [Test]
    public async Task AlgumTracoDoJogoDaPressaoMental()
    {
        await Server.WaitAssertion(() =>
        {
            var nome = Server.EntMan.ComponentFactory.GetComponentName(typeof(MentalPressureComponent));

            var donos = Server.ProtoMan.EnumeratePrototypes<TraitPrototype>()
                .Where(t => t.Components.TryGetComponent(nome, out _))
                .Select(t => t.ID)
                .ToList();

            TestContext.Out.WriteLine("traços que dão pressão mental: " + string.Join(", ", donos));

            Assert.That(donos, Is.Not.Empty,
                "nenhum traço dá MentalPressureComponent, então o sistema inteiro é inalcançável em jogo");
        });
    }
}
