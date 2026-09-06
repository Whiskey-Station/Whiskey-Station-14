// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Whiskey.Dwaine.Kernel;

namespace Content.Server._Whiskey.Dwaine.Kernel;

public enum DwaineKernelShutdownReason : byte
{
    Requested,
    Reboot,
    PowerLost,
    BootFailed,
    Panic,
    EntityRemoved,
}

public readonly record struct DwaineKernelShutdownContext(
    EntityUid Mainframe,
    ulong BootGeneration,
    DwaineKernelShutdownReason Reason);

/// <summary>
/// Synchronous, bounded kernel service lifecycle. Services may not start background tasks here.
/// </summary>
public interface IDwaineKernelService
{
    void Shutdown(in DwaineKernelShutdownContext context);
}

public readonly record struct DwaineKernelServiceFailure(string ServiceName, string ErrorCode);

/// <summary>
/// Kernel-owned registry with deterministic reverse-order shutdown.
/// </summary>
public sealed class DwaineKernelServiceRegistry
{
    public const int HardMaxServices = 128;
    public const int HardMaxServiceNameLength = 48;

    private readonly Dictionary<string, IDwaineKernelService> _services = new(StringComparer.Ordinal);
    private readonly List<string> _registrationOrder = new();
    private readonly int _capacity;
    private bool _shuttingDown;

    public int Count => _services.Count;

    public DwaineKernelServiceRegistry(int capacity = 64)
    {
        if (capacity is <= 0 or > HardMaxServices)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
    }

    public bool TryRegister(string name, IDwaineKernelService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (_shuttingDown
            || !IsValidName(name)
            || _services.Count >= _capacity
            || _services.ContainsKey(name))
        {
            return false;
        }

        _services.Add(name, service);
        _registrationOrder.Add(name);
        return true;
    }

    public bool TryGet(string name, out IDwaineKernelService service)
    {
        return _services.TryGetValue(name, out service!);
    }

    public bool TryUnregister(string name)
    {
        if (_shuttingDown || !_services.Remove(name))
            return false;

        _registrationOrder.Remove(name);
        return true;
    }

    public DwaineKernelServiceFailure[] ShutdownAll(in DwaineKernelShutdownContext context)
    {
        if (_shuttingDown || _registrationOrder.Count == 0)
            return [];

        var pending = new List<(string Name, IDwaineKernelService Service)>(_registrationOrder.Count);
        for (var index = _registrationOrder.Count - 1; index >= 0; index--)
        {
            var name = _registrationOrder[index];
            if (_services.TryGetValue(name, out var service))
                pending.Add((name, service));
        }

        // Shutdown owns an immutable snapshot. Clear the live registry before callbacks so a
        // reentrant service cannot reorder, remove, duplicate, or add work to this shutdown pass.
        _services.Clear();
        _registrationOrder.Clear();
        _shuttingDown = true;
        var failures = new List<DwaineKernelServiceFailure>();
        try
        {
            foreach (var (name, service) in pending)
            {
                try
                {
                    service.Shutdown(context);
                }
                catch (Exception)
                {
                    failures.Add(new DwaineKernelServiceFailure(name, "shutdown-failed"));
                }
            }
        }
        finally
        {
            _shuttingDown = false;
        }

        return failures.ToArray();
    }

    private static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > HardMaxServiceNameLength)
            return false;

        foreach (var character in name)
        {
            if (!(character is >= 'a' and <= 'z'
                  or >= '0' and <= '9'
                  or '.' or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }
}

public readonly record struct DwaineSystemClockSnapshot(
    TimeSpan Now,
    TimeSpan Uptime,
    ulong BootGeneration,
    bool Running);

/// <summary>
/// Deterministic clock fed only with authoritative game time.
/// </summary>
public sealed class DwaineSystemClock
{
    private TimeSpan _now;
    private TimeSpan _bootStartedAt;
    private TimeSpan _stoppedAt;
    private ulong _bootGeneration;
    private bool _running;

    public void Observe(TimeSpan gameTime)
    {
        _now = gameTime < TimeSpan.Zero ? TimeSpan.Zero : gameTime;
    }

    public void StartBoot(TimeSpan gameTime, ulong bootGeneration)
    {
        Observe(gameTime);
        _bootStartedAt = _now;
        _stoppedAt = _now;
        _bootGeneration = bootGeneration;
        _running = true;
    }

    public void Stop(TimeSpan gameTime)
    {
        Observe(gameTime);
        _stoppedAt = _now;
        _running = false;
    }

    public DwaineSystemClockSnapshot Snapshot()
    {
        var end = _running ? _now : _stoppedAt;
        var uptime = end > _bootStartedAt ? end - _bootStartedAt : TimeSpan.Zero;
        return new DwaineSystemClockSnapshot(_now, uptime, _bootGeneration, _running);
    }
}

public readonly record struct DwaineBootDiagnostic(
    TimeSpan Timestamp,
    DwaineSystemState State,
    string Code,
    string Message);

/// <summary>
/// Synchronous bootloader extension point. A handler may satisfy the storage prerequisite only
/// through its own bounded, authoritative recovery policy.
/// </summary>
[ByRefEvent]
public record struct DwaineBootRecoveryRequestedEvent
{
    public bool Recovered;
}

public sealed class DwaineBootDiagnosticBuffer
{
    public const int HardMaxEntries = 128;
    public const int HardMaxCodeLength = 48;
    public const int HardMaxMessageLength = 256;

    private readonly Queue<DwaineBootDiagnostic> _entries = new();
    private readonly int _capacity;

    public int Count => _entries.Count;

    public DwaineBootDiagnosticBuffer(int capacity = 64)
    {
        if (capacity is <= 0 or > HardMaxEntries)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
    }

    public void Add(TimeSpan timestamp, DwaineSystemState state, string code, string message)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(message);
        code = Normalize(code, HardMaxCodeLength);
        message = Normalize(message, HardMaxMessageLength);
        _entries.Enqueue(new DwaineBootDiagnostic(timestamp, state, code, message));
        while (_entries.Count > _capacity)
            _entries.Dequeue();
    }

    public DwaineBootDiagnostic[] Snapshot()
    {
        return _entries.ToArray();
    }

    public void Clear()
    {
        _entries.Clear();
    }

    private static string Normalize(string value, int maxLength)
    {
        value = value.Replace('\r', ' ').Replace('\n', ' ');
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
