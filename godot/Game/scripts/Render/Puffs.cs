// Short-lived particles drawn in one batch: hull fragments thrown out by explosions, smoke
// from badly damaged hulls. Stands for Phaser's particle emitters.
using Godot;

namespace SandboxShip.Render;

public partial class Puffs : Node2D
{
    struct Puff
    {
        public double X, Y, Vx, Vy, Age, Life, Rotation;
    }

    readonly List<Puff> puffs = [];
    readonly Random random = new();
    public required Texture2D Texture;
    public double LifeMin, LifeMax, SpeedMin, SpeedMax, ScaleStart, ScaleEnd, AlphaStart = 1;
    public bool Spin;
    /// <summary>Beyond this many, older ones make room.</summary>
    public int Max = 600;

    public void Emit(double x, double y, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (puffs.Count >= Max) puffs.RemoveAt(0);
            var a = random.NextDouble() * Math.Tau;
            var v = SpeedMin + random.NextDouble() * (SpeedMax - SpeedMin);
            puffs.Add(new Puff
            {
                X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v,
                Life = LifeMin + random.NextDouble() * (LifeMax - LifeMin), Rotation = Spin ? random.NextDouble() * Math.Tau : 0,
            });
        }
    }

    public override void _Process(double delta)
    {
        for (var i = puffs.Count - 1; i >= 0; i--)
        {
            var p = puffs[i];
            p.Age += delta;
            if (p.Age >= p.Life)
            {
                puffs.RemoveAt(i);
                continue;
            }
            p.X += p.Vx * delta;
            p.Y += p.Vy * delta;
            puffs[i] = p;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var t = Battle.StressProbe.Start();
        foreach (var p in puffs)
        {
            var k = p.Age / p.Life;
            var scale = ScaleStart + (ScaleEnd - ScaleStart) * k;
            Render.Draw.Sprite(this, Texture, p.X, p.Y, p.Rotation, scale, new Color(1, 1, 1, (float)(AlphaStart * (1 - k))));
        }
        Battle.StressProbe.Stop("puffs", t);
    }
}
