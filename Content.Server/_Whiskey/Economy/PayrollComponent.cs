// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Cargo.Prototypes;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Whiskey.Economy;

/// <summary>
/// Folha de pagamento da estação. Fica na mesma entidade que guarda as contas
/// de departamento, porque é de lá que o dinheiro sai.
///
/// O salário é transferido do orçamento do departamento, e não criado. Isso é
/// a decisão central deste sistema: dinheiro que nasce do nada precisa de
/// imposto e de rebalanceamento de inflação depois, que é trabalho para
/// desfazer trabalho. Saindo do orçamento, quem decide se a equipe recebe é o
/// chefe que administra aquela conta, e a conta acaba se ele gastar tudo em
/// pedido de carga.
///
/// Fica no servidor de propósito: ninguém prevê salário no cliente, e o saldo
/// que a pessoa vê já vem replicado pela própria conta.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class PayrollComponent : Component
{
    /// <summary>
    /// De quanto em quanto tempo a folha roda.
    /// </summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Quando cai o próximo pagamento. Pausa junto com a entidade, senão a
    /// folha dispara em rajada quando a rodada volta de uma pausa.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan NextPayday;

    /// <summary>
    /// De qual conta sai o salário de cada departamento.
    ///
    /// Comando não tem conta própria, então sai da conta de Serviço, que é a
    /// conta geral da estação. Silício e Específico ficam de fora porque
    /// cyborg não usa crachá: dar salário a eles é decisão separada, e passa
    /// por pôr conta no chassi.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<DepartmentPrototype>, ProtoId<CargoAccountPrototype>> Accounts = new()
    {
        { "Cargo",       "Cargo" },
        { "Engineering", "Engineering" },
        { "Medical",     "Medical" },
        { "Science",     "Science" },
        { "Security",    "Security" },
        { "Civilian",    "Service" },
        { "Service",     "Service" },
        { "Command",     "Service" },
    };

    /// <summary>
    /// Salário por cargo. O que não estiver aqui recebe o
    /// <see cref="DefaultSalary"/>. A tabela nasce vazia porque valor de
    /// cargo é balanceamento, e balanceamento vem em PR de conteúdo.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<JobPrototype>, int> Salaries = new();

    /// <inheritdoc cref="Salaries"/>
    [DataField]
    public int DefaultSalary = 100;
}
