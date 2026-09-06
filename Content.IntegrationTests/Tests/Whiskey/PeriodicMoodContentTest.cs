// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Shared._EinsteinEngines.Mood;
using Content.Shared._Whiskey.Mood;
using Content.Shared.Dataset;
using Content.Shared.Traits;
using Robust.Shared.Localization;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// Cobre TODO traço que carrega o <see cref="PeriodicMoodComponent"/>, e não um
/// traço nomeado.
///
/// O motivo é que o defeito que estes testes pegam não é de um traço, é da
/// forma: referência de prototype resolvida em tempo de execução e relação
/// temporal entre dois números. Erro assim não quebra build, não quebra linter,
/// e em jogo simplesmente não acontece nada.
///
/// Escrito varrendo em vez de nomeando para o próximo traço periódico nascer
/// coberto sem ninguém lembrar de escrever teste para ele.
/// </summary>
[TestFixture]
public sealed class PeriodicMoodContentTest : GameTest
{
    /// <summary>
    /// Junta todo traço que traz um episódio periódico de humor, para os testes
    /// abaixo não repetirem a varredura.
    /// </summary>
    private static List<(TraitPrototype Traco, PeriodicMoodComponent Periodico)> Varrer(
        RobustIntegrationTest.ServerIntegrationInstance server)
    {
        var achados = new List<(TraitPrototype, PeriodicMoodComponent)>();
        var nome = server.EntMan.ComponentFactory.GetComponentName(typeof(PeriodicMoodComponent));

        foreach (var traco in server.ProtoMan.EnumeratePrototypes<TraitPrototype>())
        {
            if (traco.Components.TryGetComponent(nome, out var bruto))
                achados.Add((traco, (PeriodicMoodComponent) bruto));
        }

        return achados;
    }

    /// <summary>
    /// Existe pelo menos um traço periódico.
    ///
    /// Sem isto, os testes abaixo varreriam uma lista vazia e passariam sem
    /// olhar nada, que é a forma mais silenciosa de um teste mentir.
    /// </summary>
    [Test]
    public async Task ExisteAlgumTracoPeriodico()
    {
        List<(TraitPrototype, PeriodicMoodComponent)> achados = null!;
        await Server.WaitPost(() => achados = Varrer(Server));

        Assert.That(achados, Is.Not.Empty,
            "nenhum traço traz PeriodicMood, então os outros testes desta classe não conferem nada");
    }

    /// <summary>
    /// Todo episódio aponta para modificador e conjunto de frases que existem.
    ///
    /// Id errado aqui não aparece em log nenhum: o episódio dispara, o
    /// modificador não é encontrado, e o humor não se mexe. A pessoa lê o
    /// pensamento e nada acontece.
    /// </summary>
    [Test]
    public async Task TodoEpisodioApontaParaCoisaQueExiste()
    {
        var protos = Server.ProtoMan;
        var problemas = new List<string>();

        await Server.WaitPost(() =>
        {
            foreach (var (traco, periodico) in Varrer(Server))
            {
                if (!protos.HasIndex<MoodEffectPrototype>(periodico.Effect))
                    problemas.Add($"{traco.ID}: o modificador {periodico.Effect} não existe");

                if (periodico.Messages is not { } lista)
                    continue;

                if (!protos.HasIndex<LocalizedDatasetPrototype>(lista))
                    problemas.Add($"{traco.ID}: o conjunto de frases {lista} não existe");
            }
        });

        Assert.That(problemas, Is.Empty, string.Join(" | ", problemas));
    }

    /// <summary>
    /// O modificador tem que expirar antes do próximo episódio poder chegar.
    ///
    /// Modificadores da mesma categoria se substituem, e a substituição
    /// reinicia o relógio. Se a duração for maior ou igual ao menor intervalo
    /// entre episódios, o seguinte chega antes do anterior expirar e o humor
    /// fica preso na ponta para sempre.
    ///
    /// Isto foi defeito real e só apareceu testando em jogo, com a tela cinza
    /// que não voltava. Nenhum teste olhava a relação entre os dois números,
    /// porque cada um sozinho parecia razoável.
    /// </summary>
    [Test]
    public async Task OModificadorExpiraAntesDoProximoEpisodio()
    {
        var protos = Server.ProtoMan;
        var problemas = new List<string>();

        await Server.WaitPost(() =>
        {
            foreach (var (traco, periodico) in Varrer(Server))
            {
                if (periodico.MinTimeBetween >= periodico.MaxTimeBetween)
                {
                    problemas.Add(
                        $"{traco.ID}: o intervalo mínimo {periodico.MinTimeBetween}s não é menor que o " +
                        $"máximo {periodico.MaxTimeBetween}s");
                }

                if (!protos.TryIndex<MoodEffectPrototype>(periodico.Effect, out var efeito))
                    continue;

                // Sem categoria eles não se substituem, então não travam.
                if (efeito.Category is null || efeito.Timeout == 0)
                    continue;

                if (efeito.Timeout >= periodico.MinTimeBetween)
                {
                    problemas.Add(
                        $"{traco.ID}: o modificador {efeito.ID} dura {efeito.Timeout}s e o episódio pode " +
                        $"voltar em {periodico.MinTimeBetween}s, então o humor nunca recupera");
                }
            }
        });

        Assert.That(problemas, Is.Empty, string.Join(" | ", problemas));
    }

    /// <summary>
    /// Toda frase que o conjunto promete precisa existir na tradução ativa.
    ///
    /// O conjunto é prefixo mais contagem, então subir a contagem sem escrever
    /// as frases faz o jogo mostrar o nome cru da chave na tela, tipo
    /// "jolly-thought-17". Já aconteceu aqui, com um sed que casou em dois
    /// conjuntos de uma vez.
    /// </summary>
    [Test]
    public async Task TodaFrasePrometidaExiste()
    {
        var protos = Server.ProtoMan;

        // O Loc estático não funciona da thread do teste, dá "IoC has no context
        // on this thread". Tem que ser o gerenciador do servidor, dentro de um
        // WaitPost, que roda na thread dele.
        var loc = Server.ResolveDependency<ILocalizationManager>();
        var faltando = new List<string>();

        await Server.WaitPost(() =>
        {
            foreach (var (traco, periodico) in Varrer(Server))
            {
                if (periodico.Messages is not { } listaId
                    || !protos.TryIndex(listaId, out var lista))
                    continue;

                foreach (var chave in lista.Values)
                {
                    if (!loc.TryGetString(chave, out _))
                        faltando.Add($"{traco.ID} promete {chave}");
                }
            }
        });

        Assert.That(faltando, Is.Empty,
            "conjunto prometendo chave que não existe na tradução: " + string.Join(", ", faltando));
    }
}
