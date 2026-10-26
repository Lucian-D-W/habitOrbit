using System.Numerics;

namespace Pulse.Rendering;

// Stable identity-seeded relative positions; rotation preserves radii and spacing.
public static class StarLayout
{
    public static Vector2[] Create(int seed, int count = 24)
    {
        if (count is < 0 or > 24) throw new ArgumentOutOfRangeException(nameof(count));
        var random = new Random(seed);
        float[] radii = [73, 84, 119];
        var points = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            Vector2 point;
            do
            {
                var radius = radii[random.Next(3)] + (float)random.NextDouble() * 2 - 1;
                var angle = (float)(random.NextDouble() * Math.Tau);
                point = new(radius * MathF.Cos(angle), radius * MathF.Sin(angle));
            } while (points.Take(i).Any(previous => Vector2.DistanceSquared(previous, point) < 64));
            points[i] = point;
        }
        return points;
    }
    public static Vector2 Rotate(Vector2 point,double time)=>Vector2.Transform(point,Matrix3x2.CreateRotation((float)(time*.04)));
}
