// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Client.Changelog;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using NUnit.Framework;
using Robust.Shared.ContentPack;
using Robust.Shared.Localization;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// A aba de changelog da Whiskey.
/// </summary>
/// <remarks>
/// Antes dela as nossas entradas ficavam dentro do TraumaChangelog, e apareciam
/// na aba do fork pai: ninguém conseguia distinguir o que a Whiskey mudou do que
/// veio do Trauma.
/// </remarks>
[TestFixture]
public sealed class WhiskeyChangelogTest : GameTest
{
    [SidedDependency(Side.Client)] private readonly IResourceManager _res = null!;
    [SidedDependency(Side.Client)] private readonly ILocalizationManager _loc = null!;

    private static readonly ResPath Arquivo = new("/Changelog/WhiskeyChangelog.yml");

    /// <summary>
    /// O arquivo existe e tem entrada.
    /// </summary>
    [Test]
    public async Task OChangelogDaWhiskeyExiste()
    {
        await Client.WaitAssertion(() =>
        {
            Assert.That(_res.ContentFileExists(Arquivo), Is.True,
                "sem este arquivo a aba não aparece e as entradas somem da tela");
        });
    }

    /// <summary>
    /// A aba tem nome traduzido.
    /// </summary>
    /// <remarks>
    /// O nome da aba vem de <c>changelog-tab-title-</c> mais o campo Name do
    /// arquivo. Se os dois não baterem, não dá erro nenhum: o jogador lê o id
    /// cru no topo da janela, e só quem abrir percebe.
    /// </remarks>
    [Test]
    public async Task AAbaTemNomeTraduzido()
    {
        await Client.WaitAssertion(() =>
        {
            const string chave = "changelog-tab-title-Whiskeylog";

            Assert.That(_loc.TryGetString(chave, out var texto), Is.True,
                $"a chave {chave} não existe, e o jogador veria o id cru na aba");
            Assert.That(texto, Is.Not.Empty);

            TestContext.Out.WriteLine($"aba da Whiskey: {texto}");
        });
    }

    /// <summary>
    /// As nossas entradas não ficaram para trás no arquivo do Trauma.
    /// </summary>
    [Test]
    public async Task NadaDaWhiskeySobrouNoChangelogDoTrauma()
    {
        await Client.WaitAssertion(() =>
        {
            using var stream = _res.ContentFileReadText(new ResPath("/Changelog/TraumaChangelog.yml"));
            var conteudo = stream.ReadToEnd();

            Assert.That(conteudo, Does.Not.Contain("author: Whiskey Station"),
                "entrada da Whiskey no arquivo do Trauma aparece na aba errada");
        });
    }

    /// <summary>
    /// O changelog principal é o da Whiskey.
    /// </summary>
    /// <remarks>
    /// É deste arquivo que sai o aviso de novidade no botão, e não de qual aba
    /// abre. Com o do Trauma, o aviso acendia por mudança que a Whiskey não fez.
    /// </remarks>
    [Test]
    public async Task OChangelogPrincipalEODaWhiskey()
    {
        await Client.WaitAssertion(() =>
        {
            Assert.That(ChangelogManager.MainChangelogName, Is.EqualTo("Whiskeylog"));
        });
    }

    /// <summary>
    /// Os ids da Whiskey ficam acima dos do Trauma.
    /// </summary>
    /// <remarks>
    /// Este é o teste que guarda o aviso de novidade. Ele é
    /// <c>LastReadId &lt; MaxId</c>, e o MaxId sai do changelog principal, que
    /// agora é o nosso. Se os ids daqui recomeçassem do 1, quem já jogou teria
    /// LastReadId na casa do milhar e o aviso nunca mais apareceria.
    /// </remarks>
    [Test]
    public async Task OsIdsDaWhiskeyFicamAcimaDosDoTrauma()
    {
        await Client.WaitAssertion(() =>
        {
            var nosso = LerIds(Arquivo);
            var deles = LerIds(new ResPath("/Changelog/TraumaChangelog.yml"));

            TestContext.Out.WriteLine($"Whiskey vai ate {nosso.Max()}, Trauma ate {deles.Max()}");

            Assert.That(nosso.Min(), Is.GreaterThan(deles.Max()),
                "id da Whiskey abaixo do maior do Trauma faz o aviso de novidade "
                + "sumir para quem já jogou");
        });
    }

    private System.Collections.Generic.List<int> LerIds(ResPath caminho)
    {
        using var stream = _res.ContentFileReadText(caminho);
        var ids = new System.Collections.Generic.List<int>();
        while (stream.ReadLine() is { } linha)
        {
            var t = linha.Trim();
            if (t.StartsWith("id: ") && int.TryParse(t[4..], out var id))
                ids.Add(id);
        }
        return ids;
    }
}