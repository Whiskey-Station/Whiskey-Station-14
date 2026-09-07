// SPDX-FileCopyrightText: 2026 Zequinza <felipe828218@gmail.com>
// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Cargo.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Whiskey.Economy;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Whiskey.Economy;

/// <summary>
/// Paga a tripulação com o dinheiro do orçamento do departamento de cada um.
///
/// Só recebe quem está com o cartão no slot: sem isso, imprimir vinte cartões
/// pagaria vinte salários.
/// </summary>
public sealed partial class PayrollSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private CargoSystem _cargo = default!;
    [Dependency] private CreditAccountSystem _contas = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private SharedJobSystem _jobs = default!;
    [Dependency] private StationSystem _station = default!;

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<PayrollComponent, StationBankAccountComponent>();
        while (query.MoveNext(out var uid, out var folha, out var banco))
        {
            if (_timing.CurTime < folha.NextPayday)
                continue;

            folha.NextPayday = _timing.CurTime + folha.Interval;
            PagarTodos((uid, folha, banco));
        }
    }

    public void PagarTodos(Entity<PayrollComponent, StationBankAccountComponent> estacao)
    {
        var query = EntityQueryEnumerator<ActorComponent>();
        while (query.MoveNext(out var pessoa, out _))
        {
            if (_station.GetOwningStation(pessoa) != estacao.Owner)
                continue;

            Pagar(estacao, pessoa);
        }
    }

    /// <summary>
    /// Paga uma pessoa e devolve o valor pago, ou zero.
    /// </summary>
    public int Pagar(Entity<PayrollComponent, StationBankAccountComponent> estacao, EntityUid pessoa)
    {
        if (!TryGetCracha(pessoa, out var cracha))
            return 0;

        if (cracha.Comp.JobPrototype is not { } cargo)
            return 0;

        if (!TryGetConta(estacao.Comp1, cargo, out var conta))
            return 0;

        var valor = estacao.Comp1.Salaries.GetValueOrDefault(cargo, estacao.Comp1.DefaultSalary);
        if (valor <= 0)
            return 0;

        // Departamento sem dinheiro não paga e não fica devendo: conta negativa
        // trava pedido de carga e ninguém liga uma coisa na outra.
        if (_cargo.GetBalanceFromAccount((estacao.Owner, estacao.Comp2), conta) < valor)
            return 0;

        // Depositar primeiro: a ordem inversa apagaria o dinheiro no caminho.
        if (!_contas.TryDeposit(cracha.Owner, valor))
            return 0;

        _cargo.UpdateBankAccount((estacao.Owner, estacao.Comp2), -valor, conta);
        return valor;
    }

    /// <summary>
    /// O cartão que a pessoa está usando, não o que ela segura. PDA conta.
    /// </summary>
    private bool TryGetCracha(EntityUid pessoa, out Entity<IdCardComponent> cracha)
    {
        cracha = default;

        return _inventory.TryGetSlotEntity(pessoa, "id", out var slot)
               && _idCard.TryGetIdCard(slot.Value, out cracha);
    }

    private bool TryGetConta(PayrollComponent folha, ProtoId<Content.Shared.Roles.JobPrototype> cargo, out ProtoId<CargoAccountPrototype> conta)
    {
        conta = default;

        // Primário, não o primeiro que casar: chefe aparece em Comando e no
        // setor dele, e quem paga é o setor.
        if (!_jobs.TryGetPrimaryDepartment(cargo, out var departamento) &&
            !_jobs.TryGetDepartment(cargo, out departamento))
            return false;

        return folha.Accounts.TryGetValue(departamento.ID, out conta);
    }
}
