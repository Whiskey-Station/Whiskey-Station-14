// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Popups;
using Content.Shared._Whiskey.Pressure;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Friends.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Whiskey.Pressure;

/// <summary>
/// Ficar sozinho pesa.
/// </summary>
/// <remarks>
/// Tradução do <c>terror_handler/simple_source/monophobia</c> do /tg/station,
/// que fica em <c>code/datums/components/fearful/sources/_sources.dm</c>. O
/// comportamento é o de lá; o que muda é para onde o peso vai, porque aqui
/// existe pressão com origem e lá existe uma barra de terror.
///
/// Três decisões vieram prontas de lá e valem manter:
///
/// Companhia é gente de verdade ou bicho de estimação. NPC qualquer não serve,
/// e isso importa: uma sala cheia de monstro continua sendo solidão. No .dm a
/// checagem é <c>friend.ckey</c> ou <c>/mob/living/basic/pet</c>.
///
/// A parte do pet está implementada mas hoje não dispara em jogo, e é melhor
/// dizer isso do que deixar parecer que funciona. O fork não tem equivalente
/// do <c>/mob/living/basic/pet</c>: procurei e o
/// <see cref="PettableFriendComponent"/> não está em prototype nenhum de
/// bicho, nem no corgi. Marcar os pets é conteúdo, mexe em arquivo de upstream
/// e gera conflito de merge, então fica para uma PR própria. O código aqui já
/// aceita, e o teste cobre com uma entidade que carrega o componente.
///
/// Quem não enxerga só se conforta com companhia bem perto, a um tile. Faz
/// sentido e é barato: não adianta ter gente na sala se a pessoa não sabe.
///
/// O acúmulo é lento de propósito. O comentário de lá é
/// <c>"Pretty low, ~4 minutes to reach passive cap"</c>. Ficar sozinho não
/// assusta, acumula.
///
/// E a pessoa é avisada de vez em quando, que no .dm é o
/// <c>to_chat(owner, span_warning("You feel terribly lonely..."))</c> com 10%
/// de chance e um tempo mínimo entre avisos. Isto não é enfeite: sem aviso a
/// pressão sobe calada, e quem está jogando só descobre se resolver se
/// examinar. Já aconteceu de um sintoma desta família passar despercebido em
/// teste em jogo exatamente por não avisar nada.
/// </remarks>
public sealed partial class LonelinessPressureSystem : EntitySystem
{
    /// <summary>
    /// Raio em que a companhia conta, do <c>check_radius = 7</c> do TG.
    /// </summary>
    private const float Raio = 7f;

    /// <summary>
    /// Raio de quem está cego, do <c>if (owner.is_blind()) check_radius = 1</c>.
    /// </summary>
    private const float RaioCego = 1f;

    private static readonly ProtoId<PressureSourcePrototype> Fonte = "WhiskeyPressaoSolidao";

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private MentalPressureSystem _pressao = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;

    /// <summary>
    /// De quanto em quanto tempo a solidão é conferida.
    /// </summary>
    /// <remarks>
    /// Igual ao ciclo de decaimento da pressão, e não é coincidência: conferir
    /// mais rápido que o decaimento faria a solidão subir sempre e nunca
    /// descer, porque o ganho chegaria mais vezes que a perda.
    /// </remarks>
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Chance de avisar a pessoa, por ciclo, quando ela está sozinha.
    /// </summary>
    /// <remarks>
    /// Vem do <c>SPT_PROB(10, seconds_per_tick)</c> do TG, e copiar o 10 seria
    /// errado. O SPT_PROB não quer dizer "10% por tique": ele ajusta 10% POR
    /// SEGUNDO ao tamanho do tique, por <c>1 - (1 - p) ^ segundos</c>. Como
    /// aqui o ciclo é de cinco segundos, o equivalente é
    /// 1 - 0,9^5, ou seja 41%.
    ///
    /// A diferença não é acadêmica: com 10% o aviso sairia a cada cinquenta
    /// segundos em média, e com 41% sai a cada doze, que é o ritmo do original.
    ///
    /// É o mesmo engano de unidade que o decaimento por ciclo já me custou uma
    /// vez. Número que veio de outro jogo carrega a escala de tempo dele junto.
    /// </remarks>
    private const float ChanceDeAvisar = 0.41f;

    /// <summary>
    /// Tempo mínimo entre dois avisos, do <c>TERROR_MESSAGE_CD</c> do TG.
    /// </summary>
    /// <remarks>
    /// Existe para o aviso não virar spam. A pressão continua subindo no
    /// silêncio entre um e outro; o que o tempo segura é só o texto.
    /// </remarks>
    private static readonly TimeSpan EntreAvisos = TimeSpan.FromSeconds(45);

    private TimeSpan _proxima;

    /// <summary>
    /// Quando cada pessoa pode ser avisada de novo.
    /// </summary>
    private readonly Dictionary<EntityUid, TimeSpan> _proximoAviso = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var agora = _timing.CurTime;
        if (agora < _proxima)
            return;

        _proxima = agora + Intervalo;

        var consulta = EntityQueryEnumerator<MentalPressureComponent>();
        while (consulta.MoveNext(out var uid, out var pressao))
        {
            // Sai antes da busca por entidades, que é a parte cara: quem não
            // sente solidão não precisa nem saber quem está por perto.
            if (!pressao.Sente(Fonte))
                continue;

            if (!EstaSozinho(uid))
                continue;

            _pressao.Adicionar((uid, pressao), Fonte);
            TalvezAvisar(uid, agora);
        }
    }

    /// <summary>
    /// De vez em quando, diz à pessoa que ela está se sentindo sozinha.
    /// </summary>
    private void TalvezAvisar(EntityUid uid, TimeSpan agora)
    {
        if (_proximoAviso.TryGetValue(uid, out var quando) && agora < quando)
            return;

        if (!_random.Prob(ChanceDeAvisar))
            return;

        _proximoAviso[uid] = agora + EntreAvisos;
        _popup.PopupEntity(Loc.GetString("pressure-loneliness-warning"), uid, uid, PopupType.MediumCaution);
    }

    /// <summary>
    /// Se não há gente de verdade nem bicho de estimação por perto.
    /// </summary>
    private bool EstaSozinho(EntityUid uid)
    {
        var raio = Raio;

        if (TryComp<BlindableComponent>(uid, out var visao) && visao.IsBlind)
            raio = RaioCego;

        var coordenadas = _transform.GetMapCoordinates(uid);

        foreach (var perto in _lookup.GetEntitiesInRange<MobStateComponent>(coordenadas, raio))
        {
            if (perto.Owner == uid)
                continue;

            // Gente de verdade conta. Ter alguém do lado resolve, mesmo que
            // essa pessoa não faça nada, e é o que o traço promete.
            if (HasComp<ActorComponent>(perto))
                return false;

            // Bicho de estimação também conta, e isto veio do TG de propósito.
            // É a única saída que não depende de outro jogador querer ficar
            // por perto, então é a que dá autonomia para quem pegou o traço.
            if (HasComp<PettableFriendComponent>(perto))
                return false;
        }

        return true;
    }
}
