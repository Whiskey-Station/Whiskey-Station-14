// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._EinsteinEngines.Mood;
using Content.Shared._EinsteinEngines.Mood;
using Content.Shared._Whiskey.Mood;
using Content.Shared.Alert;
using Content.Shared.Dataset;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// Cobre o que o porte do humor tem de frágil, que é referência de prototype
/// resolvida em tempo de execução. Erro assim não quebra build, não quebra
/// linter, e em jogo simplesmente não acontece nada.
/// </summary>
[TestFixture]
public sealed class MoodTest : GameTest
{
    // ProtoId e não string: o RA0033 proíbe literal no Index, e ele só reprova
    // em Release, então a build em Debug passa e esconde o erro.
    private static readonly ProtoId<Content.Shared.Traits.TraitPrototype> TracoDepressao = "Depression";

    private static readonly ProtoId<LocalizedDatasetPrototype>[] Datasets =
    {
        "WhiskeyDepressaoPensamentos",
        "WhiskeyAlucinacaoFrases",
    };

    /// <summary>
    /// Todo alerta que o componente de humor promete para cada faixa precisa
    /// existir.
    ///
    /// O porte trouxe os alertas de um arquivo de caminho base do Einstein, e
    /// eu os recortei para um arquivo próprio daqui. Se um tivesse ficado para
    /// trás, o jogo só não mostraria o ícone naquela faixa, sem erro nenhum.
    /// </summary>
    [Test]
    public async Task TodoAlertaDeFaixaExiste()
    {
        var server = Server;
        var protos = server.ProtoMan;
        var padrao = new MoodComponent();

        Assert.That(padrao.MoodThresholdsAlerts, Is.Not.Empty,
            "o componente tem que declarar alerta por faixa");

        Assert.Multiple(() =>
        {
            foreach (var (faixa, alerta) in padrao.MoodThresholdsAlerts)
            {
                Assert.That(protos.HasIndex<AlertPrototype>(alerta), Is.True,
                    $"a faixa {faixa} aponta para o alerta {alerta}, que não existe");
            }
        });

        await Task.CompletedTask;
    }

    /// <summary>
    /// A depressão precisa apontar para um modificador de humor que existe.
    ///
    /// Id errado aqui não aparece em log: o episódio dispara, o modificador não
    /// é encontrado, e o humor não se mexe. A pessoa lê o pensamento e nada
    /// acontece.
    /// </summary>
    [Test]
    public async Task ADepressaoApontaParaModificadorEDatasetQueExistem()
    {
        var server = Server;
        var protos = server.ProtoMan;

        var traco = protos.Index(TracoDepressao);
        var nome = server.EntMan.ComponentFactory.GetComponentName(typeof(PeriodicMoodComponent));

        Assert.That(traco.Components.TryGetComponent(nome, out var bruto), Is.True,
            "o traço de depressão precisa trazer o PeriodicMoodComponent");

        var periodico = (PeriodicMoodComponent) bruto!;

        Assert.Multiple(() =>
        {
            Assert.That(protos.HasIndex<MoodEffectPrototype>(periodico.Effect), Is.True,
                $"o modificador de humor {periodico.Effect} não existe");

            Assert.That(periodico.Messages, Is.Not.Null, "sem conjunto de frases o episódio é mudo");

            Assert.That(protos.HasIndex<LocalizedDatasetPrototype>(periodico.Messages!.Value), Is.True,
                $"o dataset {periodico.Messages} não existe");

            Assert.That(periodico.MinTimeBetween, Is.LessThan(periodico.MaxTimeBetween));
        });

        await Task.CompletedTask;
    }

    /// <summary>
    /// Toda frase que o dataset promete tem que existir na tradução.
    ///
    /// O dataset é prefixo mais contagem, então subir a contagem sem escrever
    /// as frases faz o jogo mostrar o nome cru da chave na tela, tipo
    /// "depression-thought-17". Eu fiz exatamente isso com um sed que casou nos
    /// dois datasets de uma vez.
    /// </summary>
    [Test]
    public async Task TodaFrasePrometidaPeloDatasetExiste()
    {
        var server = Server;
        var protos = server.ProtoMan;

        // O Loc estático não funciona da thread do teste: dá "IoC has no
        // context on this thread". Tem que ser o gerenciador do servidor, e
        // dentro de um WaitPost, que roda na thread dele.
        var loc = server.ResolveDependency<ILocalizationManager>();
        var faltando = new List<string>();

        await server.WaitPost(() =>
        {
            foreach (var id in Datasets)
            {
                foreach (var chave in protos.Index(id).Values)
                {
                    if (!loc.TryGetString(chave, out _))
                        faltando.Add($"{id} promete {chave}");
                }
            }
        });

        Assert.That(faltando, Is.Empty,
            "dataset prometendo chave que não existe na tradução: " + string.Join(", ", faltando));
    }

    /// <summary>
    /// Todo modificador de humor precisa de descrição na tradução.
    ///
    /// O MoodEffectPrototype monta a descrição como `mood-effect-{ID}`. Sem a
    /// chave, o painel de humor mostra o nome cru do prototype na tela do
    /// jogador e o servidor loga erro a cada vez que ele abre o alerta.
    ///
    /// Isto pegou dois defeitos meus: eu criei dois modificadores e não escrevi
    /// a chave de nenhum. Só apareceu testando em jogo.
    /// </summary>
    [Test]
    public async Task TodoModificadorDeHumorTemDescricao()
    {
        var server = Server;
        var protos = server.ProtoMan;
        var loc = server.ResolveDependency<ILocalizationManager>();
        var faltando = new List<string>();

        await server.WaitPost(() =>
        {
            foreach (var efeito in protos.EnumeratePrototypes<MoodEffectPrototype>())
            {
                if (!loc.TryGetString($"mood-effect-{efeito.ID}", out _))
                    faltando.Add(efeito.ID);
            }
        });

        Assert.That(faltando, Is.Empty,
            "modificador sem descrição na tradução: " + string.Join(", ", faltando));
    }

    /// <summary>
    /// Todo modificador de humor que aponta uma categoria precisa que ela
    /// exista.
    ///
    /// Isto pegou um defeito real do porte: o `genericPositiveEffects` do
    /// Einstein usa a categoria `PositiveInteraction` em dois modificadores, e
    /// o repositório de origem não declara ela em lugar nenhum.
    /// </summary>
    [Test]
    public async Task TodaCategoriaUsadaPorModificadorExiste()
    {
        var server = Server;
        var protos = server.ProtoMan;
        var faltando = new List<string>();

        await server.WaitPost(() =>
        {
            foreach (var efeito in protos.EnumeratePrototypes<MoodEffectPrototype>())
            {
                if (efeito.Category is not { } categoria)
                    continue;

                if (!protos.HasIndex<MoodCategoryPrototype>(categoria))
                    faltando.Add($"{efeito.ID} usa a categoria {categoria}");
            }
        });

        Assert.That(faltando, Is.Empty,
            "modificador apontando categoria que não existe: " + string.Join(", ", faltando));
    }
}
