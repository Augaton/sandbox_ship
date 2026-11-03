// Measuring for --start=stress: frame times, engine monitors and the time spent in each part of
// the battle view, logged once over the run so that two runs (before and after a change) compare.
using System.Diagnostics;
using Godot;
using SandboxShip.Core;

namespace SandboxShip.Battle;

public sealed class StressProbe
{
    /// <summary>Frames left out at the start: loading, first draws, the JIT warming up.</summary>
    const int Warmup = 60;

    /// <summary>The probe of the battle being measured; null the rest of the time, which costs nothing.</summary>
    public static StressProbe? Current { get; private set; }

    readonly BattleScreen scene;
    readonly Rid main, fog;
    readonly List<double> frames = [];
    readonly Dictionary<string, double> sections = [], worst = [], bytes = [];
    double process, physics, gpu, fogGpu, renderCpu, drawCalls, objects, primitives, entities;
    ulong last;
    int frame;
    long alloc0, main0;
    TimeSpan pause0;
    int gc0, gc1, gc2;
    bool measuring, done;
    /// <summary>The view measured: held whatever the pointer or the wheel do meanwhile.</summary>
    (Vector2 Center, float Zoom)? view;

    StressProbe(BattleScreen scene, Rid main, Rid fog)
    {
        this.scene = scene;
        this.main = main;
        this.fog = fog;
        RenderingServer.ViewportSetMeasureRenderTime(main, true);
        RenderingServer.ViewportSetMeasureRenderTime(fog, true);
    }

    public static void Attach(BattleScreen scene, Viewport main, Viewport fog) => Current = new StressProbe(scene, main.GetViewportRid(), fog.GetViewportRid());

    public static void Detach() => Current = null;

    /// <summary>When a part of the frame started, and how much the main thread had allocated by then.</summary>
    public readonly record struct Mark(long Ticks, long Bytes);

    public static Mark Start() => Current is { measuring: true } ? new(Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread()) : default;

    /// <summary>Adds the time and the memory allocated since `m` (from Start) to a named part of the frame.</summary>
    public static void Stop(string name, Mark m)
    {
        if (Current is not { measuring: true } p || m.Ticks == 0) return;
        var ms = (Stopwatch.GetTimestamp() - m.Ticks) * 1000.0 / Stopwatch.Frequency;
        p.sections[name] = p.sections.GetValueOrDefault(name) + ms;
        p.worst[name] = Math.Max(p.worst.GetValueOrDefault(name), ms);
        p.bytes[name] = p.bytes.GetValueOrDefault(name) + GC.GetAllocatedBytesForCurrentThread() - m.Bytes;
    }

    /// <summary>Puts the camera back where the run started, after the controls had their turn.</summary>
    public void Hold()
    {
        if (view is { } v) (scene.Center, scene.Zoom) = v;
    }

    /// <summary>Called first thing every frame: samples what the frame before cost.</summary>
    public void Frame()
    {
        var now = Godot.Time.GetTicksUsec();
        // The view as the run set it up, before the controls had a chance to move it.
        view ??= (scene.Center, scene.Zoom);
        frame++;
        if (measuring)
        {
            frames.Add((now - last) / 1000.0);
            process += Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000;
            physics += Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000;
            drawCalls += Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
            objects += Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
            primitives += Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
            gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(main);
            renderCpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(main);
            fogGpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(fog);
            entities += scene.World.Entities.Count;
        }
        last = now;
        if (frame == Warmup)
        {
            measuring = true;
            alloc0 = GC.GetTotalAllocatedBytes();
            main0 = GC.GetAllocatedBytesForCurrentThread();
            pause0 = GC.GetTotalPauseDuration();
            (gc0, gc1, gc2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        }
        // Logged just before a capture run quits.
        if (!done && frame >= Dev.Frames - 2 && frames.Count > 0) Report();
    }

    /// <summary>A digest of where everything is: two runs that played the same battle print the same.</summary>
    static uint Checksum(Sim.World w)
    {
        // FNV-1a: System.HashCode is seeded differently in every process.
        var h = 2166136261u;
        void Add(double d)
        {
            var bits = BitConverter.DoubleToInt64Bits(d);
            for (var i = 0; i < 8; i++) h = (h ^ (uint)((bits >> (i * 8)) & 0xff)) * 16777619u;
        }
        Add(w.NextId);
        foreach (var e in w.Entities)
        {
            Add(e.X);
            Add(e.Y);
            Add(e.Hull);
        }
        return h;
    }

    void Report()
    {
        done = true;
        var n = frames.Count;
        var sorted = frames.OrderBy(x => x).ToList();
        var nodes = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        GD.Print($"stress zoom {scene.Zoom}: {n} frames, {entities / n:F0} entities, {nodes} nodes; battle at {scene.World.Time:F2} s, state {Checksum(scene.World):X8}");
        GD.Print($"  frame ms: avg {frames.Average():F2}, p50 {sorted[n / 2]:F2}, p95 {sorted[(int)(n * 0.95)]:F2}, p99 {sorted[(int)(n * 0.99)]:F2}, worst {sorted[^1]:F2}; over 33 ms: {frames.Count(f => f > 33.4)}");
        GD.Print($"  process (worst of each second) {process / n:F2} ms, physics {physics / n:F2} ms, render cpu {renderCpu / n:F2} ms, gpu {gpu / n:F2} ms (fog mask {fogGpu / n:F2})");
        GD.Print($"  draw calls {drawCalls / n:F0}, objects {objects / n:F0}, primitives {primitives / n:F0}");
        // The whole process, the warm-up on its thread included while it runs; then the main thread alone.
        GD.Print($"  allocated {(GC.GetTotalAllocatedBytes() - alloc0) / (double)n / 1024:F1} KiB/frame (main thread {(GC.GetAllocatedBytesForCurrentThread() - main0) / (double)n / 1024:F1}), GCs gen0 {GC.CollectionCount(0) - gc0}, gen1 {GC.CollectionCount(1) - gc1}, gen2 {GC.CollectionCount(2) - gc2}, paused {(GC.GetTotalPauseDuration() - pause0).TotalMilliseconds:F1} ms in all");
        GD.Print("  average ms: " + string.Join(", ", sections.OrderByDescending(s => s.Value).Select(s => $"{s.Key} {s.Value / n:F2}")));
        GD.Print("  worst ms: " + string.Join(", ", worst.OrderByDescending(s => s.Value).Select(s => $"{s.Key} {s.Value:F1}")));
        GD.Print("  allocated KiB/frame: " + string.Join(", ", bytes.Where(s => s.Value > 0).OrderByDescending(s => s.Value).Select(s => $"{s.Key} {s.Value / n / 1024:F2}")));
        int anims = 0, playing = 0, shown = 0;
        void Walk(Node node)
        {
            if (node is AnimatedSprite2D a)
            {
                anims++;
                if (a.IsPlaying()) playing++;
                if (a.IsVisibleInTree()) shown++;
            }
            foreach (var c in node.GetChildren()) Walk(c);
        }
        Walk(scene);
        GD.Print($"  animated sprites {anims}, playing {playing}, visible {shown}");
    }
}
