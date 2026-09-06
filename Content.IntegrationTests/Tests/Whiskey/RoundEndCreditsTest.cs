// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.GameTicking;
using Content.Trauma.Client.RoundEndCredits;
using NUnit.Framework;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// Cobre o fim de rodada até a tela de créditos, que ninguém testava.
///
/// Escrito por causa de um relato de "os créditos não aparecem". A causa era
/// aritmética: o sistema dimensionava a tela dividindo o tamanho da janela pelo
/// valor cru de <c>display.uiScale</c>, que nasce em zero.
/// </summary>
public sealed class RoundEndCreditsTest : GameTest
{
    [SidedDependency(Side.Client)] private readonly IUserInterfaceManager _ui = null!;
    [SidedDependency(Side.Client)] private readonly IClyde _clyde = null!;
    [SidedDependency(Side.Client)] private readonly IConfigurationManager _cfg = null!;

    /// <summary>
    /// O jogador que nunca mexeu na escala de interface tem que ver os créditos
    /// igual a quem mexeu.
    /// </summary>
    [Test]
    public async Task CreditosNascemComTamanhoUtilizavel()
    {
        await Client.WaitPost(() =>
        {
            // A janela do cliente de teste nasce sem tamanho, e (0,0) dividido por
            // zero dá NaN, que o layout ignora em silêncio. Com tamanho de verdade
            // a divisão vira infinito e o defeito aparece.
            _clyde.MainWindow.Size = new Vector2i(1280, 720);

            Assert.That(_cfg.GetCVar(CVars.DisplayUIScale), Is.Zero,
                "este teste vale pelo padrão do CVar: se ele deixar de ser zero, revisar");
        });

        await Server.WaitPost(() => Server.System<GameTicker>().ShowRoundEndScoreboard());
        await RunTicksSync(15);

        await Client.WaitAssertion(() =>
        {
            var creditos = _ui.WindowRoot.Children.OfType<EndRoundCreditsControl>().FirstOrDefault();

            Assert.That(creditos, Is.Not.Null,
                "o fim de rodada tinha que ter criado a tela de créditos");

            var tamanho = creditos!.SetSize;
            TestContext.Out.WriteLine($"SetSize dos créditos: {tamanho}");

            Assert.Multiple(() =>
            {
                Assert.That(float.IsFinite(tamanho.X) && float.IsFinite(tamanho.Y), Is.True,
                    $"os créditos nasceram com tamanho {tamanho}, ou seja divisão por zero na escala de interface");

                Assert.That(tamanho.X, Is.GreaterThan(0f), "largura precisa sobrar alguma coisa");
                Assert.That(tamanho.Y, Is.GreaterThan(0f), "altura precisa sobrar alguma coisa");
            });
        });
    }
}
