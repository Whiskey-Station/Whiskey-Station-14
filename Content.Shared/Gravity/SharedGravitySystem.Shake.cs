using Content.Shared._ES.Camera;
using Content.Shared.GameTicking;
using Robust.Shared.Player;

namespace Content.Shared.Gravity;

public abstract partial class SharedGravitySystem
{
    [Dependency] private EntityQuery<GravityComponent> _gravityQuery = default!;
    [Dependency] private readonly SharedGameTicker _ticker = default!;
    [Dependency] private readonly ESScreenshakeSystem _screenshake = default!;

    protected const float GravityKick = 100.0f;
    protected const float ShakeCooldown = 0.2f;

    private void UpdateShake()
    {
        var curTime = Timing.CurTime;
        var query = EntityQueryEnumerator<GravityShakeComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.NextShake <= curTime)
            {
                if (comp.ShakeTimes == 0 || !_gravityQuery.TryGetComponent(uid, out var gravity))
                {
                    RemCompDeferred<GravityShakeComponent>(uid);
                    continue;
                }

                ShakeGrid(uid, gravity);
                comp.ShakeTimes--;
                comp.NextShake += TimeSpan.FromSeconds(ShakeCooldown);
                Dirty(uid, comp);
            }
        }
    }

    public void StartGridShake(EntityUid uid, GravityComponent? gravity = null)
    {
        if (Terminating(uid))
            return;

        if (!Resolve(uid, ref gravity, false))
            return;

        if (Timing.CurTime - _ticker.RoundStartTimeSpan < TimeSpan.FromSeconds(30))
            return;

        var shake = new ESScreenshakeParameters { Trauma = 0.8f, DecayRate = 0.04f, Frequency = 0.015f };
        _screenshake.Screenshake(Filter.BroadcastGrid(uid), shake, null);
    }

    protected virtual void ShakeGrid(EntityUid uid, GravityComponent? comp = null) {}
}
