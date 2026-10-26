using System.Numerics;
using Pulse.Rendering;
namespace Pulse.Core.Tests;
public sealed class StarLayoutTests
{
    [Fact] public void StarBandsAreStableSeparatedAndWithinDecoration()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var points = StarLayout.Create(seed);
            Assert.Equal(points, StarLayout.Create(seed));
            Assert.Equal(points.Take(9), StarLayout.Create(seed, 9));
            for (var i = 0; i < points.Length; i++)
            {
                var radius = points[i].Length();
                Assert.True(radius >= 71.999f && radius <= 120.001f);
                Assert.True(radius < 75 || radius is > 82 and < 86 || radius > 117);
                Assert.True(Math.Abs(radius - 107*.9f) > 11);
                foreach (var previous in points.Take(i)) Assert.True(Vector2.DistanceSquared(previous, points[i]) >= 64);
            }
        }
        Assert.NotEqual(StarLayout.Create(11)[0], StarLayout.Create(22)[0]);
    }
    [Fact] public void RotationMovesStarsWithoutChangingBandOrPairSpacing()
    {
        var points=StarLayout.Create(1842);var rotated=points.Select(p=>StarLayout.Rotate(p,6)).ToArray();
        Assert.True(Vector2.Distance(points[0],rotated[0])>10);
        for(var i=0;i<points.Length;i++)Assert.True(Math.Abs(points[i].Length()-rotated[i].Length())<.001);
        Assert.True(Math.Abs(Vector2.Distance(points[0],points[1])-Vector2.Distance(rotated[0],rotated[1]))<.001);
    }
}
