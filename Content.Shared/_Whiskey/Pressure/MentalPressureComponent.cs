// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Whiskey.Pressure;

/// <summary>
/// Pressão mental que sabe DE ONDE veio.
/// </summary>
/// <remarks>
/// Isto nasceu lendo cinco sistemas que já existem no fork e vendo que cada um
/// tem uma peça e nenhum tem o conjunto:
///
/// O humor do Einstein tem modificador por prototype, categoria e alerta, e é
/// uma barra só, sem origem. A sanidade do /tg/station tem duas velocidades,
/// uma que reage e uma que lembra, e também é barra só. A Paracusia do
/// Goobstation entrega som falso por jogador e não guarda estado nenhum. O
/// medo do Heretic, no Trauma, guarda <c>Dictionary&lt;EntityUid, float&gt;</c>,
/// ou seja sabe quem está te assustando, e joga isso fora quando acaba. E a
/// alucinação do Oculto, aqui mesmo, desenha na tela de um cliente só.
///
/// O que nenhum deles faz é deixar o estado mental ter superfície social. Todos
/// medem QUANTO. Só o medo mede DE QUÊ, e por pouco tempo. Enquanto for só um
/// número, ninguém de fora consegue perceber, entender ou ajudar, e é por isso
/// que o cargo de Psicólogo em qualquer um desses jogos acaba sendo um
/// dispensador de comprimido.
///
/// Aqui a pressão é um mapa de fonte para peso. Cada entrada sabe o que a
/// causou, decai no próprio ritmo, e some sozinha. Isso destrava três coisas
/// que uma barra não permite: o sintoma pode escolher o canal pela fonte, dá
/// para tratar a causa em vez do total, e alguém examinando consegue ver o que
/// está pesando.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class MentalPressureComponent : Component
{
    /// <summary>
    /// As pressões ativas, uma por tipo de fonte.
    ///
    /// A chave é o prototype da fonte e não a entidade que causou, de
    /// propósito: dois cadáveres na mesma sala são a mesma pressão mais forte,
    /// e não duas pressões. O medo do Heretic guarda por entidade porque lá o
    /// que importa é de qual monstro fugir; aqui o que importa é o que está
    /// acontecendo com a pessoa.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<ProtoId<PressureSourcePrototype>, float> Sources = new();

    /// <summary>
    /// Soma das pressões ativas, recalculada quando alguma muda.
    /// Vai para o cliente porque o alerta na tela precisa dela.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public float Total;

    /// <summary>
    /// Teto do total. Existe para uma fonte sozinha não conseguir saturar a
    /// pessoa: sem teto, ficar meia hora no escuro seria igual a ficar meia
    /// hora no escuro com cadáveres, e a origem deixaria de importar.
    /// </summary>
    [DataField]
    public float Max = 100f;

    /// <summary>
    /// Quando o relógio de decaimento roda de novo.
    /// </summary>
    [DataField]
    [AutoPausedField]
    public TimeSpan NextDecay;

    /// <summary>
    /// De quanto em quanto tempo a pressão decai.
    /// </summary>
    /// <remarks>
    /// ATENÇÃO: o Decay de cada fonte é por CICLO disto, e não por segundo.
    /// Com cinco segundos aqui, uma fonte de decay 3 perde três pontos a cada
    /// cinco segundos. Ler isso como "por segundo" já rendeu comentário com
    /// duração cinco vezes menor que a real.
    /// </remarks>
    [DataField]
    public TimeSpan DecayInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A quais fontes esta pessoa é suscetível. Lista vazia quer dizer todas.
    /// </summary>
    /// <remarks>
    /// Existe para uma fobia poder ser fobia, e não sensibilidade geral.
    ///
    /// No /tg/station cada medo é um trauma separado e independente: quem tem
    /// monofobia sofre de ficar sozinho e não sofre de ver morte, porque são
    /// dois datums que não se conhecem. Sem esta lista, qualquer traço que
    /// desse pressão faria a pessoa sentir TUDO, e portar monofobia daria na
    /// prática outro traço Sensível com nome diferente.
    ///
    /// Vazia por padrão porque é o que o Sensível quer: quem é sensível é
    /// sensível a tudo. Quem tem uma fobia lista a fonte dela.
    ///
    /// NÃO é networkado, e a regra 14 do regulamento pede a razão registrada,
    /// já que o componente é adicionado com este campo modificado pelo traço.
    /// A razão: quem consulta é só o servidor, na hora de decidir se a pressão
    /// entra. O cliente nunca pergunta se alguém sente uma fonte, porque o que
    /// ele desenha é o total e o alerta, e esses já vêm prontos. Networkar uma
    /// lista que nasce do prototype e nunca muda em jogo seria tráfego para
    /// nada, em todo jogador que tiver o traço.
    ///
    /// Se um dia a interface precisar mostrar a que a pessoa é sensível, isto
    /// vira AutoNetworkedField e a razão acima deixa de valer.
    /// </remarks>
    [DataField]
    public HashSet<ProtoId<PressureSourcePrototype>> SusceptibleTo = new();

    /// <summary>
    /// Se esta pessoa sente a fonte.
    /// </summary>
    public bool Sente(ProtoId<PressureSourcePrototype> fonte)
        => SusceptibleTo.Count == 0 || SusceptibleTo.Contains(fonte);
}

/// <summary>
/// Avisa que o total de pressão mudou de faixa, para quem quiser reagir.
/// </summary>
[Serializable, NetSerializable]
public sealed class MentalPressureChangedEvent : EntityEventArgs
{
    public readonly float Antes;
    public readonly float Agora;

    public MentalPressureChangedEvent(float antes, float agora)
    {
        Antes = antes;
        Agora = agora;
    }
}
