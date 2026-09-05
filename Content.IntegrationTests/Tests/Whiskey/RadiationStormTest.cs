// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.StationEvents.Components;
using Content.Trauma.Server.Weather;
using Content.Shared.Weather;
using Content.Trauma.Shared.Weather;
using Robust.Shared.Audio;
using Content.Goobstation.Shared.EntityConditions;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects.Effects.Damage;
using Content.Shared.EntityEffects.Effects.StatusEffects;
using Content.Shared.EntityConditions;
using Content.Shared.StatusEffectNew;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey;

/// <summary>
/// Trava as promessas que a tempestade radioativa faz ao jogador.
///
/// O anúncio manda correr para as manutenções, e isso só é verdade enquanto
/// AreaMaints estiver na lista de áreas seguras. Um anúncio que mente é pior
/// que anúncio nenhum, porque a pessoa corre para o lugar errado e morre lá.
/// </summary>
[TestFixture]
public sealed class RadiationStormTest : GameTest
{
    [SidedDependency(Side.Server)] private readonly ILocalizationManager _loc = null!;

    private static readonly EntProtoId Evento = "RadiationStormRule";
    private static readonly EntProtoId Agendador = "WeatherSchedulerRadiationStorm";
    private static readonly EntProtoId Tempestade = "WeatherRadiationStorm";
    private static readonly EntProtoId Maints = "AreaMaints";
    private static readonly ProtoId<ReagentPrototype> Iodo = "PotassiumIodide";
    private static readonly EntProtoId Protecao = "StatusEffectRadiationProtection";

    /// <summary>
    /// O que o anúncio promete tem que ser verdade no clima.
    /// </summary>
    [Test]
    public async Task ManutencaoProtegeDeVerdade()
    {
        var protos = Server.ProtoMan;
        var nome = Server.EntMan.ComponentFactory.GetComponentName(typeof(WeatherEffectsComponent));

        Assert.That(protos.Index(Tempestade).Components.TryGetComponent(nome, out var raw), Is.True,
            "o clima da tempestade precisa ter WeatherEffects");

        var efeitos = (WeatherEffectsComponent) raw!;

        var seguras = efeitos.SafeAreas.Select(a => a.Id).ToList();
        TestContext.Out.WriteLine("áreas seguras: " + string.Join(", ", seguras));

        Assert.That(seguras, Does.Contain(Maints.Id),
            "o anúncio manda ir para as manutenções, então elas têm que estar na lista de áreas seguras");

        await Task.CompletedTask;
    }

    /// <summary>
    /// O evento precisa ser ouvido, não só lido. Ele entrava mudo.
    /// </summary>
    [Test]
    public async Task OEventoAnunciaEFazBarulho()
    {
        var protos = Server.ProtoMan;
        var nome = Server.EntMan.ComponentFactory.GetComponentName(typeof(StationEventComponent));

        Assert.That(protos.Index(Evento).Components.TryGetComponent(nome, out var raw), Is.True);

        var evento = (StationEventComponent) raw!;

        Assert.Multiple(() =>
        {
            Assert.That(evento.StartAnnouncement, Is.Not.Null, "a tempestade tem que avisar antes");
            Assert.That(_loc.HasString(evento.StartAnnouncement!), Is.True,
                $"a chave {evento.StartAnnouncement} não existe no locale");
            Assert.That(evento.StartAudio, Is.Not.Null,
                "sem som o aviso passa despercebido no meio do chat");
        });

        await Task.CompletedTask;
    }

    /// <summary>
    /// Toda mensagem de estágio precisa existir, incluindo a do encerramento.
    /// Estágio com chave inventada não dá erro, sai a chave crua na tela.
    /// </summary>
    [Test]
    public async Task TodasAsMensagensDosEstagiosExistem()
    {
        var protos = Server.ProtoMan;
        var nome = Server.EntMan.ComponentFactory.GetComponentName(typeof(WeatherSchedulerComponent));

        Assert.That(protos.Index(Agendador).Components.TryGetComponent(nome, out var raw), Is.True);

        var agendador = (WeatherSchedulerComponent) raw!;
        var mensagens = agendador.Stages
            .Where(e => e.Message is not null)
            .Select(e => e.Message!.Value)
            .ToList();

        TestContext.Out.WriteLine($"estágios: {agendador.Stages.Count}, com mensagem: {mensagens.Count}");

        Assert.Multiple(() =>
        {
            foreach (var chave in mensagens)
            {
                Assert.That(_loc.HasString(chave), Is.True, $"a chave {chave} não existe no locale");
            }

            Assert.That(mensagens.Select(m => m.ToString()), Does.Contain("radiation-storm-over"),
                "falta o recado de que a ameaça passou, que é o que libera sair do abrigo");
        });

        await Task.CompletedTask;
    }

    /// <summary>
    /// O alarme tem que estar no estágio, e não no som do clima.
    ///
    /// Escrito depois de ele não tocar em jogo: o som do WeatherStatusEffect é
    /// ambiental e o cliente abafa para nada quando não existe tile exposto por
    /// perto, então quem está no meio da estação não ouve. O dano ignora teto,
    /// o som não.
    /// </summary>
    [Test]
    public async Task OAlarmeDaTempestadeEGlobal()
    {
        var protos = Server.ProtoMan;
        var nomeAgenda = Server.EntMan.ComponentFactory.GetComponentName(typeof(WeatherSchedulerComponent));
        var nomeClima = Server.EntMan.ComponentFactory.GetComponentName(typeof(WeatherStatusEffectComponent));

        Assert.That(protos.Index(Agendador).Components.TryGetComponent(nomeAgenda, out var rawAgenda), Is.True);
        var agendador = (WeatherSchedulerComponent) rawAgenda!;
        var estagio = agendador.Stages.First(e => e.Weather == Tempestade);

        Assert.That(estagio.Sound, Is.Not.Null,
            "o alarme precisa estar no estágio, que toca global, e não no som do clima");

        // E o som do próprio clima não pode virar o alarme de novo: ali ele fica
        // preso na oclusão e some para quem está longe de um tile exposto.
        if (protos.Index(Tempestade).Components.TryGetComponent(nomeClima, out var rawClima))
        {
            var clima = (WeatherStatusEffectComponent) rawClima!;
            var caminho = (clima.Sound as SoundPathSpecifier)?.Path.ToString() ?? "";
            TestContext.Out.WriteLine($"ambiente do clima: {caminho}");
            Assert.That(caminho, Does.Not.Contain("alarm"),
                "alarme como som de clima não alcança quem está dentro da estação");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// A host pediu escape de 100% para quem tomou iodo antes. Isso só funciona
    /// enquanto o reagente e a tempestade concordarem no nome do status. Se
    /// alguém trocar um dos dois, a proteção some sem erro nenhum.
    /// </summary>
    [Test]
    public async Task IodoTomadoAntesProtegePorInteiro()
    {
        var protos = Server.ProtoMan;
        var nome = Server.EntMan.ComponentFactory.GetComponentName(typeof(WeatherEffectsComponent));

        Assert.That(protos.Index(Tempestade).Components.TryGetComponent(nome, out var raw), Is.True);
        var efeitos = (WeatherEffectsComponent) raw!;

        var protegido = efeitos.Conditions?
            .OfType<HasStatusEffectCondition>()
            .FirstOrDefault(c => c.Inverted);

        Assert.That(protegido, Is.Not.Null,
            "a tempestade precisa pular quem tem status de proteção contra radiação");

        // O outro lado do contrato: o reagente aplica esse mesmo status.
        var iodo = protos.Index(Iodo);
        var doSangue = iodo.Metabolisms?.Metabolisms["Bloodstream"].Effects ?? [];
        var aplicados = doSangue
            .OfType<ModifyStatusEffect>()
            .Select(e => e.EffectProto.Id)
            .ToList();

        TestContext.Out.WriteLine("status que o iodo aplica: " + string.Join(", ", aplicados));

        Assert.That(aplicados, Does.Contain(protegido!.EffectProto.Id),
            "o iodo tem que aplicar exatamente o status que a tempestade respeita");
    }

    /// <summary>
    /// A tempestade é para derrubar, não para matar de graça. Trava a conta
    /// inteira, incluindo os 30 segundos que o agendador soma de crossfade e
    /// que não aparecem no YAML.
    /// </summary>
    [Test]
    public async Task ATempestadeDerrubaMasNaoMata()
    {
        var protos = Server.ProtoMan;
        var nomeClima = Server.EntMan.ComponentFactory.GetComponentName(typeof(WeatherEffectsComponent));
        var nomeAgenda = Server.EntMan.ComponentFactory.GetComponentName(typeof(WeatherSchedulerComponent));

        Assert.That(protos.Index(Tempestade).Components.TryGetComponent(nomeClima, out var rawClima), Is.True);
        Assert.That(protos.Index(Agendador).Components.TryGetComponent(nomeAgenda, out var rawAgenda), Is.True);

        var efeitos = (WeatherEffectsComponent) rawClima!;
        var agendador = (WeatherSchedulerComponent) rawAgenda!;

        var dano = efeitos.Effects
            .OfType<HealthChange>()
            .SelectMany(e => e.Damage.DamageDict)
            .Where(d => d.Key == "Radiation")
            .Sum(d => (float) d.Value);

        var estagio = agendador.Stages.First(e => e.Weather == Tempestade);
        // O agendador soma StartupTime e ShutdownTime quando os estágios
        // vizinhos também têm clima, e são 15 segundos cada.
        var segundos = estagio.Duration.Max + 30;
        var total = dano * segundos / efeitos.UpdateDelay.TotalSeconds;

        TestContext.Out.WriteLine(
            $"{dano}/s por {segundos}s reais = {total} de dano em exposição completa");

        Assert.Multiple(() =>
        {
            Assert.That(total, Is.GreaterThanOrEqualTo(100f),
                "abaixo do SoftCrit a tempestade não assusta ninguém e o abrigo perde a razão de existir");
            Assert.That(total, Is.LessThan(200f),
                "200 é morte no humanoide: quem ficou no corredor tem que cair, não morrer sem chance");
        });

        await Task.CompletedTask;
    }

    /// <summary>
    /// A host tirou a mutação: aqui a radiação machuca, não transforma.
    /// </summary>
    [Test]
    public async Task ATempestadeNaoMutaMaisNinguem()
    {
        var protos = Server.ProtoMan;
        var nome = Server.EntMan.ComponentFactory.GetComponentName(typeof(WeatherEffectsComponent));

        Assert.That(protos.Index(Tempestade).Components.TryGetComponent(nome, out var raw), Is.True);
        var efeitos = (WeatherEffectsComponent) raw!;

        var tipos = efeitos.Effects.Select(e => e.GetType().Name).ToList();
        TestContext.Out.WriteLine("efeitos da tempestade: " + string.Join(", ", tipos));

        Assert.That(tipos.Any(t => t.Contains("Mutation")), Is.False,
            "a mutação saiu do desenho a pedido da host");

        await Task.CompletedTask;
    }

    /// <summary>
    /// Raro e pesado, em vez de frequente e chato.
    /// </summary>
    [Test]
    public async Task AcontecerUmaVezPorRodada()
    {
        var protos = Server.ProtoMan;
        var nome = Server.EntMan.ComponentFactory.GetComponentName(typeof(StationEventComponent));

        Assert.That(protos.Index(Evento).Components.TryGetComponent(nome, out var raw), Is.True);
        var evento = (StationEventComponent) raw!;

        Assert.That(evento.MaxOccurrences, Is.EqualTo(1),
            "a host pediu uma tempestade por rodada");

        await Task.CompletedTask;
    }

    /// <summary>
    /// A condição do iodo, avaliada contra uma pessoa de verdade.
    /// </summary>
    /// <remarks>
    /// O teste de contrato ao lado ficou verde enquanto o recurso estava
    /// quebrado em jogo, porque ele só olha nomes: o clima cita um status e o
    /// reagente aplica um status com o mesmo id. Isto aqui pergunta a coisa que
    /// importa, que é se uma pessoa com o status é poupada.
    /// </remarks>
    [Test]
    public async Task QuemTemOStatusDeProtecaoNaoEAfetado()
    {
        var pessoa = await Spawn("MobHuman");

        await Server.WaitAssertion(() =>
        {
            var nome = Server.EntMan.ComponentFactory.GetComponentName(typeof(WeatherEffectsComponent));
            Assert.That(Server.ProtoMan.Index(Tempestade).Components.TryGetComponent(nome, out var raw), Is.True);
            var efeitos = (WeatherEffectsComponent) raw!;

            var condicoes = Server.System<SharedEntityConditionsSystem>();
            var status = Server.System<StatusEffectsSystem>();

            // Sem proteção, a tempestade tem que pegar.
            Assert.That(condicoes.TryConditions(pessoa, efeitos.Conditions), Is.True,
                "sem proteção a pessoa tem que ser afetada, senão o teste não mede nada");

            // Com o status, tem que ser poupada por inteiro.
            status.TryAddStatusEffect(pessoa, Protecao, out _);
            Assert.That(status.HasStatusEffect(pessoa, Protecao), Is.True,
                "o status precisa estar aplicado para o resto do teste valer");

            Assert.That(condicoes.TryConditions(pessoa, efeitos.Conditions), Is.False,
                "com o status de proteção contra radiação a pessoa não pode ser afetada");
        });
    }

    /// <summary>
    /// Mede a janela real de proteção depois de uma dose de iodo.
    /// </summary>
    /// <remarks>
    /// Esta é a pergunta que faltava. O teste ao lado prova que a condição
    /// funciona quando o status existe; este mede se o status existe pelo tempo
    /// que a tempestade dura, que é o que falhou no teste em jogo.
    ///
    /// A conta que preocupa: a dose da pílula é 20u, o metabolismo consome 0,5
    /// por ciclo de um segundo, e o clima fica 40 segundos no ar.
    /// </remarks>
    [Test]
    public async Task UmaDoseDeIodoProtegeATempestadeInteira()
    {
        var pessoa = await Spawn("MobHuman");
        var status = Server.System<StatusEffectsSystem>();

        await Server.WaitPost(() =>
        {
            var solucao = new Solution();
            solucao.AddReagent(Iodo, FixedPoint2.New(20));
            Server.System<BloodstreamSystem>().TryAddToBloodstream(pessoa, solucao);
        });

        // Deixa metabolizar por um instante, para o status aparecer.
        await RunSeconds(3);

        await Server.WaitAssertion(() =>
            Assert.That(status.HasStatusEffect(pessoa, Protecao), Is.True,
                "três segundos depois da dose a proteção já tinha que estar de pé"));

        // Os 40 segundos que o clima fica no ar: 10 do estágio mais 15 de
        // entrada e 15 de saída do crossfade.
        await RunSeconds(40);

        await Server.WaitAssertion(() =>
            Assert.That(status.HasStatusEffect(pessoa, Protecao), Is.True,
                "a dose precisa cobrir os 40 segundos inteiros, senão quem se preparou "
                + "toma dano no fim da tempestade e o preparo não vale"));
    }
}
