// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Server._Whiskey.Dwaine.Transport;
using Content.Shared._Whiskey.Dwaine.Identity;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Robust.Shared.Timing;
using System;

namespace Content.Server._Whiskey.Dwaine.Identity;

/// <summary>
/// Attaches the server-only identity store to kernel generations and transport sessions.
/// Transport IDs are revalidated against the owning mainframe for every public operation.
/// </summary>
public sealed partial class DwaineIdentitySystem : EntitySystem
{
    [Dependency] private DwaineKernelSystem _kernel = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DwaineIdentityComponent, DwaineKernelReadyEvent>(OnKernelReady);
        SubscribeLocalEvent<DwaineIdentityRuntimeComponent, ComponentShutdown>(OnRuntimeShutdown);
        SubscribeLocalEvent<DwaineIdentityRuntimeComponent, DwaineMainframeSessionConnectedEvent>(OnSessionConnected);
        SubscribeLocalEvent<DwaineIdentityRuntimeComponent, DwaineMainframeSessionDisconnectedEvent>(OnSessionDisconnected);
    }

    public bool TryGetStore(EntityUid mainframe, out DwaineIdentityStore store)
    {
        if (TerminatingOrDeleted(mainframe)
            || !TryComp<DwaineIdentityRuntimeComponent>(mainframe, out var runtime)
            || !runtime.Online
            || runtime.Store is not { } current
            || _kernel.GetState(mainframe) != DwaineSystemState.SystemReady)
        {
            store = null!;
            return false;
        }

        store = current;
        return true;
    }

    public DwaineIdentityResult TryGetSession(
        EntityUid mainframe,
        DwaineSessionId transportSession,
        out DwaineIdentitySessionSnapshot session)
    {
        session = default;
        if (!TryGetValidatedTransportSession(mainframe, transportSession, out var store))
            return DwaineIdentityResult.SessionNotFound;
        return store.TryGetSessionForTerminal(transportSession.Value, _timing.CurTime, out session);
    }

    public DwaineIdentityResult TryLogin(
        EntityUid mainframe,
        DwaineSessionId transportSession,
        string name,
        string password,
        out DwaineIdentitySessionSnapshot session)
    {
        session = default;
        if (!TryGetValidatedTransportSession(mainframe, transportSession, out var store)
            || !TryComp<DwaineIdentityComponent>(mainframe, out var config))
        {
            return DwaineIdentityResult.SessionNotFound;
        }

        return store.TryLogin(
            name,
            password,
            transportSession.Value,
            _timing.CurTime,
            SessionLifetime(config),
            out session);
    }

    public DwaineIdentityResult TryLogout(EntityUid mainframe, DwaineSessionId transportSession)
    {
        if (!TryGetValidatedTransportSession(mainframe, transportSession, out var store)
            || !TryComp<DwaineIdentityComponent>(mainframe, out var config))
        {
            return DwaineIdentityResult.SessionNotFound;
        }

        store.DisconnectTerminal(transportSession.Value);
        return store.TryCreateTemporarySession(
            transportSession.Value,
            _timing.CurTime,
            SessionLifetime(config),
            out _);
    }

    private void OnKernelReady(Entity<DwaineIdentityComponent> ent, ref DwaineKernelReadyEvent args)
    {
        if (!TryComp<DwaineIdentityRuntimeComponent>(ent, out var runtime))
            return;

        runtime.Store ??= new DwaineIdentityStore(
            Math.Clamp(ent.Comp.MaxAccounts, 1, DwaineIdentityComponent.HardMaxAccounts),
            Math.Clamp(ent.Comp.MaxGroups, 3, DwaineIdentityComponent.HardMaxGroups),
            Math.Clamp(ent.Comp.MaxSessions, 1, DwaineIdentityComponent.HardMaxSessions));
        runtime.Store.RevokeAllSessions();
        runtime.Online = true;
        runtime.BootGeneration = args.BootGeneration;

        if (!_kernel.TryRegisterService(
                ent.Owner,
                "identity",
                new IdentityKernelService(this, ent.Owner, args.BootGeneration)))
        {
            runtime.Store.RevokeAllSessions();
            runtime.Online = false;
            runtime.BootGeneration = 0;
            _kernel.Panic(ent.Owner, "identity-service-registration");
            return;
        }

        if (!TryComp<DwaineMainframeRuntimeComponent>(ent, out var transport))
            return;
        foreach (var transportSession in transport.Sessions.Keys)
            EnsureTemporarySession(ent.Owner, transportSession);
    }

    private void OnRuntimeShutdown(Entity<DwaineIdentityRuntimeComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.Store?.RevokeAllSessions();
        ent.Comp.Store = null;
        ent.Comp.Online = false;
        ent.Comp.BootGeneration = 0;
    }

    private void OnSessionConnected(
        Entity<DwaineIdentityRuntimeComponent> ent,
        ref DwaineMainframeSessionConnectedEvent args)
    {
        EnsureTemporarySession(ent.Owner, args.Session);
    }

    private void OnSessionDisconnected(
        Entity<DwaineIdentityRuntimeComponent> ent,
        ref DwaineMainframeSessionDisconnectedEvent args)
    {
        ent.Comp.Store?.DisconnectTerminal(args.Session.Value);
    }

    private void EnsureTemporarySession(EntityUid mainframe, DwaineSessionId transportSession)
    {
        if (!TryGetValidatedTransportSession(mainframe, transportSession, out var store)
            || !TryComp<DwaineIdentityComponent>(mainframe, out var config))
        {
            return;
        }

        if (store.TryGetSessionForTerminal(transportSession.Value, _timing.CurTime, out _)
            == DwaineIdentityResult.Success)
        {
            return;
        }

        store.TryCreateTemporarySession(
            transportSession.Value,
            _timing.CurTime,
            SessionLifetime(config),
            out _);
    }

    private bool TryGetValidatedTransportSession(
        EntityUid mainframe,
        DwaineSessionId transportSession,
        out DwaineIdentityStore store)
    {
        store = null!;
        if (transportSession.Value == 0
            || !TryComp<DwaineMainframeRuntimeComponent>(mainframe, out var transport)
            || !transport.Sessions.ContainsKey(transportSession))
        {
            return false;
        }

        return TryGetStore(mainframe, out store);
    }

    private static TimeSpan SessionLifetime(DwaineIdentityComponent config)
    {
        var seconds = float.IsFinite(config.SessionLifetimeSeconds)
            ? config.SessionLifetimeSeconds
            : DwaineIdentityComponent.HardMaxSessionLifetimeSeconds;
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 1f, DwaineIdentityComponent.HardMaxSessionLifetimeSeconds));
    }

    private void OnKernelServiceShutdown(EntityUid mainframe, ulong bootGeneration)
    {
        if (!TryComp<DwaineIdentityRuntimeComponent>(mainframe, out var runtime)
            || runtime.BootGeneration != bootGeneration)
        {
            return;
        }

        runtime.Store?.RevokeAllSessions();
        runtime.Online = false;
        runtime.BootGeneration = 0;
    }

    private sealed class IdentityKernelService(
        DwaineIdentitySystem system,
        EntityUid mainframe,
        ulong bootGeneration) : IDwaineKernelService
    {
        public void Shutdown(in DwaineKernelShutdownContext context)
        {
            if (context.Mainframe == mainframe && context.BootGeneration == bootGeneration)
                system.OnKernelServiceShutdown(mainframe, bootGeneration);
        }
    }
}
