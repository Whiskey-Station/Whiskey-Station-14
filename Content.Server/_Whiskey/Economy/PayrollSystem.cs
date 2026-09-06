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
/// Paga a tripulação, tirando o dinheiro do orçamento do departamento de cada
/// um e pondo na conta do crachá.
///
/// Recebe quem está com o crachá no lugar do crachá. Carregar na mão não
/// conta, e é isso que impede a fábrica de identidades: imprimir vinte
/// cartões não paga vinte salários, porque só um está sendo usado.
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
        // Relógio do jogo, nunca frameTime: a folha precisa cair no mesmo
        // intervalo com o servidor cheio e com o servidor vazio.
        var query = EntityQueryEnumerator<PayrollComponent, StationBankAccountComponent>();
        while (query.MoveNext(out var uid, out var folha, out var banco))
        {
            if (_timing.CurTime < folha.NextPayday)
                continue;

            folha.NextPayday = _timing.CurTime + folha.Interval;
            PagarTodos((uid, folha, banco));
        }
    }

    /// <summary>
    /// Roda a folha inteira de uma estação.
    /// </summary>
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
    /// Paga uma pessoa, se ela tiver crachá com cargo e o departamento dela
    /// tiver dinheiro. Devolve o valor pago, ou zero.
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

        // Conferir o orçamento antes de tudo. Departamento sem dinheiro
        // simplesmente não paga, e nunca fica devendo: conta de departamento
        // negativa trava pedido de carga e ninguém entende por quê.
        if (_cargo.GetBalanceFromAccount((estacao.Owner, estacao.Comp2), conta) < valor)
            return 0;

        // Depositar primeiro. Se o depósito não couber, o orçamento não é
        // tocado, e a ordem inversa apagaria o dinheiro no caminho.
        if (!_contas.TryDeposit(cracha.Owner, valor))
            return 0;

        _cargo.UpdateBankAccount((estacao.Owner, estacao.Comp2), -valor, conta);
        return valor;
    }

    /// <summary>
    /// Acha o crachá que a pessoa está usando, e não o que ela está segurando.
    /// O PDA conta, porque o cartão vive dentro dele.
    /// </summary>
    private bool TryGetCracha(EntityUid pessoa, out Entity<IdCardComponent> cracha)
    {
        cracha = default;

        return _inventory.TryGetSlotEntity(pessoa, "id", out var slot)
               && _idCard.TryGetIdCard(slot.Value, out cracha);
    }

    /// <summary>
    /// De qual conta sai o salário daquele cargo, pelo departamento dele.
    /// </summary>
    private bool TryGetConta(PayrollComponent folha, ProtoId<Content.Shared.Roles.JobPrototype> cargo, out ProtoId<CargoAccountPrototype> conta)
    {
        conta = default;

        // Departamento primário, e não o primeiro que casar: chefe de setor
        // aparece em Comando e no setor dele, e quem paga é o setor.
        if (!_jobs.TryGetPrimaryDepartment(cargo, out var departamento) &&
            !_jobs.TryGetDepartment(cargo, out departamento))
            return false;

        return folha.Accounts.TryGetValue(departamento.ID, out conta);
    }
}
