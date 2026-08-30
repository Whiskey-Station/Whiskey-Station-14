// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Chat.Managers;
using Content.Server.Mind;
using Content.Shared._EinsteinEngines.Mood;
using Content.Shared._Whiskey.Mood;
using Content.Shared.Chat;
using Content.Shared.Popups;
using Content.Shared.Random.Helpers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Whiskey.Mood;

/// <summary>
/// Dispara os episódios do <see cref="PeriodicMoodComponent"/>.
/// </summary>
public sealed partial class PeriodicMoodSystem : EntitySystem
{
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private ISharedPlayerManager _player = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        // ComponentStartup, e não MapInitEvent: o TraitSystem adiciona o
        // componente numa entidade que já nasceu, e naquele caminho o MapInit
        // não dispara. Mesmo motivo do motor de alucinação.
        SubscribeLocalEvent<PeriodicMoodComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<PeriodicMoodComponent> ent, ref ComponentStartup args)
    {
        Agendar(ent);

        // O aviso vai sempre por popup, mesmo quando o resto vai por chat: ele
        // aparece uma vez só, no instante em que a pessoa nasce, e no chat
        // ficaria enterrado embaixo dos comunicados de início de rodada.
        if (ent.Comp.GainMessage is { } aviso)
            _popup.PopupEntity(Loc.GetString(aviso), ent, ent, PopupType.LargeCaution);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var agora = _timing.CurTime;

        var consulta = EntityQueryEnumerator<PeriodicMoodComponent>();
        while (consulta.MoveNext(out var uid, out var periodico))
        {
            if (agora < periodico.NextEpisode)
                continue;

            Disparar((uid, periodico));
            Agendar((uid, periodico));
        }
    }

    private void Agendar(Entity<PeriodicMoodComponent> ent)
    {
        var espera = _random.NextFloat(ent.Comp.MinTimeBetween, ent.Comp.MaxTimeBetween);
        ent.Comp.NextEpisode = _timing.CurTime + TimeSpan.FromSeconds(espera);
    }

    private void Disparar(Entity<PeriodicMoodComponent> ent)
    {
        // O MoodSystem escuta este evento na própria entidade. Quanto o
        // modificador pesa e quanto dura são propriedades dele, no YAML.
        RaiseLocalEvent(ent, new MoodEffectEvent(ent.Comp.Effect));

        if (ent.Comp.Messages is not { } listaId)
            return;

        if (!_proto.TryIndex(listaId, out var lista) || lista.Values.Count == 0)
            return;

        Contar(ent, _random.Pick(lista));
    }

    /// <summary>
    /// Entrega a frase só para quem tem o componente, pelo canal escolhido.
    ///
    /// Popup serve para susto, porque dura 0,04 segundo por caractere e some.
    /// Chat serve para pensamento, porque fica e dá para reler. A cor existe
    /// para o pensamento não se perder no meio dos comunicados da estação, que
    /// foi o que aconteceu quando isto era só uma linha branca a mais.
    /// </summary>
    private void Contar(Entity<PeriodicMoodComponent> ent, string frase)
    {
        if (ent.Comp.Popup)
        {
            // O terceiro argumento é o destinatário. A sobrecarga de dois
            // mostraria para todo mundo por perto, e um pensamento que é da
            // pessoa passaria a ser lido pela estação inteira.
            _popup.PopupEntity(frase, ent, ent, PopupType.LargeCaution);
            return;
        }

        if (!_mind.TryGetMind(ent, out _, out var mente) || mente.UserId is not { } usuario)
            return;

        if (!_player.TryGetSessionById(usuario, out var sessao))
            return;

        // Sem embrulho de "servidor": a frase é um pensamento, não um aviso da
        // estação, e o embrulho é justamente o que a fazia parecer comunicado.
        _chat.ChatMessageToOne(
            ChatChannel.Emotes,
            frase,
            frase,
            default,
            false,
            sessao.Channel,
            ent.Comp.MessageColor);
    }
}
