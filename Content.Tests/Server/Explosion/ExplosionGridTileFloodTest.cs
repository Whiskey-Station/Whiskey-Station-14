using Content.Server.Explosion.EntitySystems;
using Content.Shared.FixedPoint;
using NUnit.Framework;

namespace Content.Tests.Server.Explosion;

[TestFixture]
[TestOf(typeof(ExplosionGridTileFlood))]
[Parallelizable(ParallelScope.All)]
public sealed class ExplosionGridTileFloodTest
{
    [TestCase(-10f, 2f, 12)]
    [TestCase(0f, 2f, 12)]
    [TestCase(0.1f, 2f, 13)]
    [TestCase(4f, 2f, 14)]
    public void ClearIterationNeverMovesBackwards(float required, float stepSize, int expected)
    {
        var result = ExplosionGridTileFlood.GetClearIteration(12, FixedPoint2.New(required), stepSize);

        Assert.That(result, Is.EqualTo(expected));
    }
}
