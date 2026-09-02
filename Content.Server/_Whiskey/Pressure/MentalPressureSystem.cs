// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Shared._Whiskey.Pressure;
using Content.Shared.Examine;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Whiskey.Pressure;

/// <summary>
/// Acumula, decai e expõe a pressão mental.
/// </summary>
/// <remarks>
/// Ver o <see cref="MentalPressureComponent"/> para o porquê de a pressão ter
/// origem em vez de ser uma barra.
/// </remarks>
public sealed partial class MentalPressureSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MentalPressureComponent, ExaminedEvent>(OnExamined);
    }

    /// <summary>
    /// Acrescenta pressão de uma fonte, respeitando o teto dela e o geral.
    /// </summary>
    public void Adicionar(Entity<MentalPressureComponent?> ent, ProtoId<PressureSourcePrototype> fonte)
    {
        if (!Resolve(ent, ref ent.Comp, false) || !_proto.TryIndex(fonte, out var proto))
            return;

        var antes = ent.Comp.Total;
        var atual = ent.Comp.Sources.GetValueOrDefault(fonte);

        // Teto por fonte antes do teto geral: uma coisa só não satura ninguém.
        ent.Comp.Sources[fonte] = MathF.Min(atual + proto.Weight, proto.Cap);

        // O relógio de decaimento nasce em TimeSpan.Zero, ou seja já vencido, e
        // sem isto a primeira rodada de decaimento acontecia no mesmo instante
        // em que a pressão era criada. Na prática a pessoa perdia um tique de
        // pressão antes de ter tido tempo de sentir qualquer coisa.
        //
        // Só empurra quando está vencido: empurrar sempre faria pressão que
        // chega o tempo todo nunca decair.
        var agora = _timing.CurTime;
        if (ent.Comp.NextDecay <= agora)
            ent.Comp.NextDecay = agora + ent.Comp.DecayInterval;

        Recalcular(ent!, antes);
    }

    /// <summary>
    /// Tira pressão de UMA fonte, que é o que permite tratar a causa em vez do
    /// total. Sair da sala do cadáver mexe só naquela entrada.
    /// </summary>
    /// <remarks>
    /// A assimetria com o <see cref="Adicionar"/> é de propósito, e foi
    /// apontada em revisão.
    ///
    /// O Adicionar precisa do prototype porque lê o peso e o teto dele: sem o
    /// prototype não existe quanto acrescentar. Aliviar não precisa de nada
    /// disso, porque a quantidade vem de quem chama e a operação é sobre uma
    /// entrada que já está no dicionário.
    ///
    /// A consequência é boa: se um prototype for removido embaixo de um jogo em
    /// andamento, ainda dá para limpar a entrada órfã de quem já estava sob
    /// aquela pressão. Exigir o prototype aqui deixaria essa pressão presa até
    /// o Update passar e descartá-la.
    /// </remarks>
    public void Aliviar(Entity<MentalPressureComponent?> ent, ProtoId<PressureSourcePrototype> fonte, float quanto)
    {
        if (!Resolve(ent, ref ent.Comp, false) || !ent.Comp.Sources.TryGetValue(fonte, out var atual))
            return;

        var antes = ent.Comp.Total;
        var novo = atual - quanto;

        if (novo <= 0)
            ent.Comp.Sources.Remove(fonte);
        else
            ent.Comp.Sources[fonte] = novo;

        Recalcular(ent!, antes);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var agora = _timing.CurTime;
        var consulta = EntityQueryEnumerator<MentalPressureComponent>();

        while (consulta.MoveNext(out var uid, out var pressao))
        {
            if (agora < pressao.NextDecay)
                continue;

            pressao.NextDecay = agora + pressao.DecayInterval;

            if (pressao.Sources.Count == 0)
                continue;

            var antes = pressao.Total;

            // Copiar as chaves antes de mexer: não dá para remover de um
            // dicionário enquanto se itera nele.
            foreach (var fonte in pressao.Sources.Keys.ToArray())
            {
                if (!_proto.TryIndex(fonte, out var proto))
                {
                    // Prototype removido embaixo de um jogo em andamento. Melhor
                    // largar a entrada que arrastar um id morto para sempre.
                    pressao.Sources.Remove(fonte);
                    continue;
                }

                var novo = pressao.Sources[fonte] - proto.Decay;

                if (novo <= 0)
                    pressao.Sources.Remove(fonte);
                else
                    pressao.Sources[fonte] = novo;
            }

            Recalcular((uid, pressao), antes);
        }
    }

    private void Recalcular(Entity<MentalPressureComponent> ent, float antes)
    {
        var soma = 0f;
        foreach (var (_, peso) in ent.Comp.Sources)
            soma += peso;

        ent.Comp.Total = MathF.Min(soma, ent.Comp.Max);
        Dirty(ent);

        // Registro do estado, com as fontes abertas. Em Debug, então não sai em
        // produção, mas destrava diagnóstico: sem isto, "não aconteceu nada" num
        // teste em jogo não se distingue de "aconteceu e passou despercebido",
        // e as duas coisas pedem consertos diferentes.
        if (ent.Comp.Sources.Count > 0)
        {
            var detalhe = string.Join(", ", ent.Comp.Sources.Select(p => $"{p.Key.Id}={p.Value:F1}"));
            Log.Debug($"pressão: {ToPrettyString(ent.Owner)} total {ent.Comp.Total:F1} [{detalhe}]");
        }

        if (MathF.Abs(ent.Comp.Total - antes) < 0.001f)
            return;

        var ev = new MentalPressureChangedEvent(antes, ent.Comp.Total);
        RaiseLocalEvent(ent, ev);
    }

    /// <summary>
    /// A superfície social: quem examina de perto lê o que está pesando, e não
    /// um número.
    /// </summary>
    /// <remarks>
    /// É esta parte que faz o sistema valer a pena. Sem ela seria mais uma
    /// barra privada, igual às outras cinco que eu li antes de escrever isto, e
    /// o Psicólogo continuaria dando comprimido no escuro.
    ///
    /// Mostra a fonte e não o peso de propósito. "Ele viu alguém morrer" é uma
    /// deixa para conversar; "pressão 34" é um número para otimizar.
    /// </remarks>
    private void OnExamined(Entity<MentalPressureComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || ent.Comp.Sources.Count == 0)
            return;

        using var bloco = args.PushGroup(nameof(MentalPressureComponent));

        // Da mais pesada para a mais leve: quem examina lê primeiro o que mais
        // importa, em vez da ordem em que as coisas aconteceram.
        foreach (var (fonte, _) in ent.Comp.Sources.OrderByDescending(par => par.Value))
        {
            if (_proto.TryIndex(fonte, out var proto))
                args.PushMarkup(Loc.GetString(proto.Description));
        }
    }
}
