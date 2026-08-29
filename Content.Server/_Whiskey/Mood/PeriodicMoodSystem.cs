// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._EinsteinEngines.Mood;
using Content.Shared._Whiskey.Mood;
using Content.Shared.Popups;
using Content.Shared.Random.Helpers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Whiskey.Mood;

/// <summary>
/// Dispara os episódios do <see cref="PeriodicMoodComponent"/>.
/// </summary>
public sealed partial class PeriodicMoodSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
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

        if (ent.Comp.GainMessage is { } aviso)
            MostrarParaODono(ent, Loc.GetString(aviso));
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
        var evento = new MoodEffectEvent(ent.Comp.Effect);
        RaiseLocalEvent(ent, evento);

        if (ent.Comp.Messages is not { } listaId)
            return;

        if (!_proto.TryIndex(listaId, out var lista) || lista.Values.Count == 0)
            return;

        MostrarParaODono(ent, _random.Pick(lista));
    }

    /// <summary>
    /// Mostra o texto só para quem tem o componente.
    ///
    /// A sobrecarga de três argumentos do popup tem o destinatário no terceiro.
    /// A de dois mostraria para todo mundo por perto, e um pensamento que é da
    /// pessoa passaria a ser lido pela estação inteira.
    /// </summary>
    private void MostrarParaODono(EntityUid uid, string texto)
    {
        _popup.PopupEntity(texto, uid, uid, PopupType.LargeCaution);
    }
}
