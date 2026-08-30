// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Whiskey.Dwaine.Kernel;
using Content.Server._Whiskey.Dwaine.Process;
using Content.Shared._Whiskey.Dwaine.FileSystem;
using Content.Shared._Whiskey.Dwaine.Kernel;
using Robust.Shared.Timing;

namespace Content.Server._Whiskey.Dwaine.FileSystem;

/// <summary>
/// Binds the pure VFS to kernel generations. The tree and every handle remain server-only.
/// </summary>
public sealed partial class DwaineFileSystemSystem : EntitySystem
{
    [Dependency] private DwaineKernelSystem _kernel = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DwaineFileSystemComponent, DwaineKernelReadyEvent>(OnKernelReady);
        SubscribeLocalEvent<DwaineFileSystemRuntimeComponent, ComponentShutdown>(OnRuntimeShutdown);
        SubscribeLocalEvent<DwaineFileSystemRuntimeComponent, DwaineProcessStateChangedEvent>(OnProcessStateChanged);
        SubscribeLocalEvent<DwaineFileSystemRuntimeComponent, DwaineProcessRemovedEvent>(OnProcessRemoved);
    }

    public bool TryGetFileSystem(EntityUid mainframe, out DwaineVirtualFileSystem fileSystem)
    {
        if (!TryComp<DwaineFileSystemRuntimeComponent>(mainframe, out var runtime)
            || !runtime.Online
            || runtime.FileSystem is not { } current
            || _kernel.GetState(mainframe) != DwaineSystemState.SystemReady)
        {
            fileSystem = null!;
            return false;
        }

        fileSystem = current;
        return true;
    }

    public bool IsDirectory(EntityUid mainframe, DwaineWorkingDirectoryHandle workingDirectory)
    {
        return TryGetFileSystem(mainframe, out var fileSystem)
               && fileSystem.IsDirectory(ToVfsHandle(workingDirectory));
    }

    public static DwaineVfsNodeHandle ToVfsHandle(DwaineWorkingDirectoryHandle workingDirectory)
    {
        return new DwaineVfsNodeHandle(
            new DwaineVfsVolumeId(workingDirectory.Volume),
            new DwaineVfsNodeId(workingDirectory.Node));
    }

    public static DwaineWorkingDirectoryHandle ToWorkingDirectory(DwaineVfsNodeHandle handle)
    {
        return new DwaineWorkingDirectoryHandle(handle.Volume.Value, handle.Node.Value);
    }

    private void OnKernelReady(Entity<DwaineFileSystemComponent> ent, ref DwaineKernelReadyEvent args)
    {
        if (!TryComp<DwaineFileSystemRuntimeComponent>(ent, out var runtime))
            return;

        runtime.FileSystem ??= new DwaineVirtualFileSystem(ent.Comp, _timing.CurTime);
        runtime.FileSystem.ClearProcessViews();
        runtime.Online = true;
        runtime.BootGeneration = args.BootGeneration;
        if (_kernel.TryRegisterService(
                ent.Owner,
                "virtual-filesystem",
                new FileSystemKernelService(this, ent.Owner, args.BootGeneration)))
        {
            return;
        }

        runtime.Online = false;
        runtime.BootGeneration = 0;
        _kernel.Panic(ent.Owner, "filesystem-service-registration");
    }

    private void OnRuntimeShutdown(Entity<DwaineFileSystemRuntimeComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.FileSystem?.ClearProcessViews();
        ent.Comp.FileSystem = null;
        ent.Comp.Online = false;
        ent.Comp.BootGeneration = 0;
    }

    private void OnProcessStateChanged(
        Entity<DwaineFileSystemRuntimeComponent> ent,
        ref DwaineProcessStateChangedEvent args)
    {
        if (!ent.Comp.Online
            || ent.Comp.BootGeneration != args.BootGeneration
            || ent.Comp.FileSystem is not { } fileSystem)
        {
            return;
        }

        fileSystem.UpsertProcessView(
            args.ProcessId.Value,
            args.Current.ToString(),
            args.BootGeneration,
            _timing.CurTime);
    }

    private void OnProcessRemoved(
        Entity<DwaineFileSystemRuntimeComponent> ent,
        ref DwaineProcessRemovedEvent args)
    {
        if (!ent.Comp.Online
            || ent.Comp.BootGeneration != args.BootGeneration
            || ent.Comp.FileSystem is not { } fileSystem)
        {
            return;
        }

        fileSystem.RemoveProcessView(args.ProcessId.Value);
    }

    private void OnKernelServiceShutdown(
        EntityUid mainframe,
        ulong bootGeneration,
        DwaineKernelShutdownReason reason)
    {
        if (!TryComp<DwaineFileSystemRuntimeComponent>(mainframe, out var runtime)
            || !runtime.Online
            || runtime.BootGeneration != bootGeneration)
        {
            return;
        }

        runtime.FileSystem?.ClearProcessViews();
        runtime.Online = false;
        runtime.BootGeneration = 0;
    }

    private sealed class FileSystemKernelService(
        DwaineFileSystemSystem system,
        EntityUid mainframe,
        ulong bootGeneration) : IDwaineKernelService
    {
        public void Shutdown(in DwaineKernelShutdownContext context)
        {
            if (context.Mainframe != mainframe || context.BootGeneration != bootGeneration)
                return;

            system.OnKernelServiceShutdown(mainframe, bootGeneration, context.Reason);
        }
    }
}
