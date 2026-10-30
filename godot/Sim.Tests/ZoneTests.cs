// Zone control (not in the web version): three beacons, taken by ships of a single camp in a few
// seconds, frozen while contested, scoring for their holder's camp; the first camp to the target
// wins, and destroying every foe station still wins too.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class ZoneTests
{
    static readonly Side Fourth = new(4);

    static World Zones(int target = 100, List<Slot>? slots = null) => Scenario.CreateSkirmish(new SkirmishOptions
    {
        Mode = BattleMode.Zones, ZoneTarget = target, Seed = 11, Raid = true,
        Slots = slots ?? [new(Faction.Klaed, Faction.Klaed), new(Faction.Nairan, Faction.Nairan)],
    });

    /// <summary>A ship that will not die during the test, its guns silent: only its presence counts.</summary>
    static Entity Sentinel(World w, Side side, IPoint at, double dx = 0)
    {
        var e = SpawnShip(w, side, ShipClass.Bulwark, at.X + dx, at.Y);
        e.Hull = e.MaxHull = 1e9;
        e.Weapons = [];
        return e;
    }

    [Fact]
    public void Three_beacons_the_center_and_a_mirrored_pair_away_from_the_stations()
    {
        var w = Zones();
        var b = w.Mode.Beacons;
        Assert.Equal(3, b.Count);
        Assert.Equal((w.Width / 2, w.Height / 2), (b[1].X, b[1].Y));
        Assert.Equal(w.Width, b[0].X + b[2].X, 6);
        Assert.Equal(w.Height, b[0].Y + b[2].Y, 6);
        // A above C, all of them nobody's, and no raid colossus sitting on one.
        Assert.True(b[0].Y <= b[2].Y);
        Assert.All(b, x => Assert.Null(x.Holder));
        Assert.DoesNotContain(w.Entities, e => e.Type == "colossus");
        foreach (var s in w.Entities.Where(e => e.Type == "station")) Expect.Greater(b.Min(x => Dist(x, s)), 1000);
        Assert.Equal(new double[] { 0, 0 }, w.Mode.Scores.Values);
    }

    [Fact]
    public void Ships_of_one_camp_take_a_beacon_in_a_few_seconds()
    {
        var w = Zones();
        var b = w.Mode.Beacons[1];
        Sentinel(w, Faction.Klaed, b);
        Steps.Run(w, ZoneControl.CaptureTime - 1);
        Assert.Null(b.Holder);
        Assert.Equal(Faction.Klaed, b.Claim);
        Expect.Greater(b.Progress, 0.8);
        Steps.Run(w, 1.5);
        Assert.Equal(Faction.Klaed, b.Holder);
        Assert.Contains(w.Events, e => e is BeaconTaken { Beacon: 1 } t && t.Side == Faction.Klaed);
        // A ship farther than the radius counts for nothing.
        var a = w.Mode.Beacons[0];
        Sentinel(w, Faction.Nairan, a, ZoneControl.Radius + 60);
        Steps.Run(w, ZoneControl.CaptureTime + 1);
        Assert.Null(a.Claim);
    }

    [Fact]
    public void A_contested_beacon_freezes()
    {
        var w = Zones();
        var b = w.Mode.Beacons[1];
        Sentinel(w, Faction.Klaed, b, -100);
        Steps.Run(w, ZoneControl.CaptureTime / 2);
        var progress = b.Progress;
        var foe = Sentinel(w, Faction.Nairan, b, 100);
        Steps.Run(w, ZoneControl.CaptureTime);
        Assert.True(b.Contested);
        Assert.Equal(progress, b.Progress, 6);
        Assert.Null(b.Holder);
        // Once the foe is gone, the taking goes on.
        foe.Hull = 0;
        Steps.Run(w, ZoneControl.CaptureTime);
        Assert.False(b.Contested);
        Assert.Equal(Faction.Klaed, b.Holder);
    }

    [Fact]
    public void A_foe_must_undo_a_hold_before_taking_the_beacon()
    {
        var w = Zones();
        var b = w.Mode.Beacons[2];
        var ours = Sentinel(w, Faction.Klaed, b);
        Steps.Run(w, ZoneControl.CaptureTime + 1);
        Assert.Equal(Faction.Klaed, b.Holder);
        ours.Hull = 0;
        Sentinel(w, Faction.Nairan, b);
        Steps.Run(w, ZoneControl.CaptureTime - 1);
        Assert.Equal(Faction.Klaed, b.Holder);
        Steps.Run(w, 1.5);
        Assert.Null(b.Holder);
        Assert.Contains(w.Events, e => e is BeaconLost { Beacon: 2 } l && l.Side == Faction.Klaed);
        Steps.Run(w, ZoneControl.CaptureTime + 0.5);
        Assert.Equal(Faction.Nairan, b.Holder);
    }

    [Fact]
    public void Held_beacons_score_for_their_camp_and_the_first_to_the_target_wins()
    {
        var w = Zones(target: 10);
        var b = w.Mode.Beacons;
        (b[0].Holder, b[0].Claim, b[0].Progress) = (Faction.Klaed, Faction.Klaed, 1);
        (b[1].Holder, b[1].Claim, b[1].Progress) = (Faction.Klaed, Faction.Klaed, 1);
        (b[2].Holder, b[2].Claim, b[2].Progress) = (Faction.Nairan, Faction.Nairan, 1);
        Steps.Run(w, 10);
        Expect.Close(2 * ZoneControl.PointsPerSecond * 10, w.Mode.Scores[Faction.Klaed], 1);
        Expect.Close(ZoneControl.PointsPerSecond * 10, w.Mode.Scores[Faction.Nairan], 1);
        Assert.Null(w.Winner);
        Steps.Run(w, (10 - w.Mode.Scores[Faction.Klaed]) / (2 * ZoneControl.PointsPerSecond) + 0.1);
        Assert.Equal(Faction.Klaed, w.Winner);
        Assert.Contains(w.Events, e => e is Victory v && v.Side == Faction.Klaed);
    }

    [Fact]
    public void A_team_scores_together_and_a_camp_without_a_station_no_more()
    {
        var w = Zones(slots:
        [
            new(Faction.Klaed, Faction.Klaed, 1), new(Faction.Nairan, Faction.Nairan, 2),
            new(Faction.Nautolan, Faction.Nautolan, 1), new(Fourth, Faction.Klaed, 2),
        ]);
        var b = w.Mode.Beacons;
        (b[0].Holder, b[0].Progress) = (Faction.Klaed, 1);
        (b[1].Holder, b[1].Progress) = (Faction.Nautolan, 1);
        (b[2].Holder, b[2].Progress) = (Fourth, 1);
        Steps.Run(w, 10);
        Expect.Close(2, ZoneControl.Score(w, Faction.Nautolan), 1);
        Expect.Close(1, ZoneControl.Score(w, Faction.Nairan), 1);
        // Team 2 loses one station of two: it still scores.
        var nairan = StationOf(w, Faction.Nairan)!;
        foreach (var e in ChildrenOf(w, nairan.Id).Prepend(nairan)) e.Hull = 0;
        Steps.Run(w, 10);
        Expect.Close(2, ZoneControl.Score(w, Faction.Nairan), 1);
        Assert.Null(w.Winner);
        // Its last station gone, it is out and team 1 wins, as in the standard rules.
        var fourth = StationOf(w, Fourth)!;
        foreach (var e in ChildrenOf(w, fourth.Id).Prepend(fourth)) e.Hull = 0;
        Steps.StepWorld(w);
        Assert.True(Won(w, Faction.Nautolan));
        Assert.False(Won(w, Fourth));
    }

    [Fact]
    public void Destroying_every_foe_station_still_wins()
    {
        var w = Zones();
        var station = StationOf(w, Faction.Nairan)!;
        foreach (var e in ChildrenOf(w, station.Id).Prepend(station)) e.Hull = 0;
        Steps.StepWorld(w);
        Assert.Equal(Faction.Klaed, w.Winner);
    }

    [Fact]
    public void Between_AIs_beacons_change_hands_and_it_ends()
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions
        {
            Mode = BattleMode.Zones, Seed = 101, Slots = [new(Faction.Klaed, Faction.Klaed, 0, Difficulty.Normal), new(Faction.Nautolan, Faction.Nautolan, 0, Difficulty.Normal)],
        });
        var taken = new HashSet<Side>();
        while (w.Winner is null && w.Time < 40 * 60)
        {
            Steps.StepWorld(w, 1.0 / 30);
            foreach (var e in w.Events) if (e is BeaconTaken t) taken.Add(t.Side);
            w.Events.Clear();
        }
        Assert.NotNull(w.Winner);
        // Both AIs went for the beacons.
        Assert.Equal(2, taken.Count);
    }
}
