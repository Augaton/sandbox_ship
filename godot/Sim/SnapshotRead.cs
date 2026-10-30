// Reading a battle snapshot into a replica (see Snapshot.cs for the layout): entities are
// created, updated and removed by id, the rest replaced. Nothing of the simulation runs: the
// replica holds what the host said, and only what the viewer's team may see.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

public static partial class Snapshot
{
    /// <summary>Weapons on a replica stand in by number: the view only counts them.</summary>
    static readonly WeaponDef Unknown = new(WeaponKind.Bullet, 0, 0, 0, 0);

    /// <summary>
    /// Brings `replica` to the snapshot: the battle as its viewer's team sees it, with the events
    /// since the previous snapshot added to its Events. A snapshot of another version is refused
    /// before anything changes. A snapshot cut short leaves the replica half updated; the next whole one mends it.
    /// </summary>
    public static void Read(BinaryReader r, World replica)
    {
        if (r.ReadUInt32() != Magic) throw new InvalidDataException("Pas un instantané de bataille");
        var version = r.ReadUInt16();
        if (version != Version) throw new InvalidDataException($"Instantané en version {version}, version {Version} attendue");
        var viewer = new Side(r.Read7BitEncodedInt());
        replica.Time = r.ReadDouble();
        replica.Width = r.ReadDouble();
        replica.Height = r.ReadDouble();
        var flags = r.ReadByte();
        replica.Fog = (flags & 1) != 0;
        replica.SandboxTools = (flags & 2) != 0;
        replica.Winner = (flags & 4) != 0 ? new Side(r.Read7BitEncodedInt()) : null;
        replica.LastDamage = r.ReadDouble();
        ReadMode(r, replica.Mode);
        ReadSides(r, replica, viewer);
        ReadFields(r, replica);
        ReadEntities(r, replica, viewer);
        ReadShots(r, replica);
        var zones = new List<Zone>();
        for (var n = Count(r); n > 0; n--)
        {
            zones.Add(new Zone((ZoneKind)r.ReadByte(), new Side(r.Read7BitEncodedInt()), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
        }
        replica.Zones = zones;
        var strikes = new List<Strike>();
        for (var n = Count(r); n > 0; n--) strikes.Add(ReadStrike(r));
        replica.Strikes = strikes;
        for (var n = Count(r); n > 0; n--) replica.Events.Add(ReadEvent(r));
    }

    /// <summary>A count, checked: a broken snapshot must not ask for a huge list.</summary>
    static int Count(BinaryReader r)
    {
        var n = r.Read7BitEncodedInt();
        return n is >= 0 and <= 1 << 20 ? n : throw new InvalidDataException($"Liste de {n} éléments");
    }

    static int? Optional(BinaryReader r) => r.Read7BitEncodedInt() is var id && id >= 0 ? id : null;

    /// <summary>The battle mode's state (see ModeBytes), into the replica's own, beacons in place; it must take the whole length announced.</summary>
    static void ReadMode(BinaryReader r, ModeState mode)
    {
        var length = Count(r);
        var bytes = r.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        using var m = new BinaryReader(new MemoryStream(bytes));
        mode.Kind = (BattleMode)m.ReadByte();
        Side? SideOrNone() => m.Read7BitEncodedInt() is var id && id > 0 ? new Side(id - 1) : null;
        switch (mode.Kind)
        {
            case BattleMode.Standard: break;
            case BattleMode.Survival:
                mode.Wave = m.Read7BitEncodedInt();
                mode.Cleared = m.Read7BitEncodedInt();
                mode.NextWave = m.ReadDouble();
                break;
            case BattleMode.Zones:
                mode.ZoneTarget = m.Read7BitEncodedInt();
                var n = Count(m);
                if (mode.Beacons.Count != n) mode.Beacons = [.. Enumerable.Range(0, n).Select(_ => new Beacon(0, 0))];
                foreach (var b in mode.Beacons)
                {
                    b.X = m.ReadSingle();
                    b.Y = m.ReadSingle();
                    b.Holder = SideOrNone();
                    b.Claim = SideOrNone();
                    b.Progress = m.ReadSingle();
                    b.Contested = m.ReadBoolean();
                }
                mode.Scores.Clear();
                for (var i = Count(m); i > 0; i--) mode.Scores[new Side(m.Read7BitEncodedInt())] = m.ReadDouble();
                break;
            case BattleMode.Assault:
                mode.Deadline = m.ReadDouble();
                break;
            case BattleMode.Regicide:
                mode.Flagships.Clear();
                for (var i = Count(m); i > 0; i--) mode.Flagships[new Side(m.Read7BitEncodedInt())] = m.Read7BitEncodedInt();
                break;
            default: throw new InvalidDataException($"Mode de bataille inconnu : {mode.Kind}");
        }
        if (m.BaseStream.Position != length) throw new InvalidDataException("État du mode mal formé");
    }

    static void ReadSides(BinaryReader r, World replica, Side viewer)
    {
        var listed = new HashSet<Side>();
        for (var n = Count(r); n > 0; n--)
        {
            var side = new Side(r.Read7BitEncodedInt());
            listed.Add(side);
            if (!replica.Factions.TryGetValue(side, out var f)) replica.Factions[side] = f = new FactionState { Id = side };
            f.Race = (Faction)r.ReadByte();
            f.Team = r.Read7BitEncodedInt();
            f.Color = r.ReadUInt32();
            var flags = r.ReadByte();
            f.HadStation = (flags & 1) != 0;
            f.StationId = (flags & 2) != 0 ? r.Read7BitEncodedInt() : null;
            f.Stats.Built = r.Read7BitEncodedInt();
            f.Stats.Lost = r.Read7BitEncodedInt();
            f.Stats.Kills = r.Read7BitEncodedInt();
            f.Stats.Damage = r.ReadSingle();
            f.Stats.Earned = r.ReadSingle();
            if ((flags & 4) != 0) ReadEconomy(r, f);
        }
        foreach (var gone in replica.Factions.Keys.Where(s => !listed.Contains(s)).ToList()) replica.Factions.Remove(gone);
    }

    static void ReadEconomy(BinaryReader r, FactionState f)
    {
        f.Credits = r.ReadDouble();
        f.Bounties = r.ReadDouble();
        f.AutoAbilities = r.ReadBoolean();
        f.RallyX = r.ReadSingle();
        f.RallyY = r.ReadSingle();
        f.Queue = ReadQueue(r);
        f.BuildQueue = ReadQueue(r);
        var techs = Count(r);
        for (var i = 0; i < techs; i++)
        {
            var level = r.Read7BitEncodedInt();
            if (i < Techs.All.Count) f.Research[Techs.All[i]] = level;
        }
        f.Researching = r.ReadBoolean() ? ReadJob(r) : null;
        f.ResearchQueue = [];
        for (var n = Count(r); n > 0; n--) f.ResearchQueue.Add(ReadJob(r));
        f.Repeat = [];
        for (var n = Count(r); n > 0; n--) f.Repeat.Add((ShipClass)r.ReadByte());
        f.HeroCooldowns.Clear();
        for (var n = Count(r); n > 0; n--) f.HeroCooldowns[ReadHero(r)] = r.ReadSingle();
    }

    static List<QueueItem> ReadQueue(BinaryReader r)
    {
        var queue = new List<QueueItem>();
        for (var n = Count(r); n > 0; n--)
        {
            queue.Add(new QueueItem
            {
                Kind = (QueueKind)r.ReadByte(), Cls = (ShipClass)r.ReadByte(), TargetId = r.Read7BitEncodedInt(), Hero = ReadHero(r),
                Remaining = r.ReadSingle(), Total = r.ReadSingle(), Cost = r.ReadSingle(),
            });
        }
        return queue;
    }

    static ResearchJob ReadJob(BinaryReader r)
    {
        var tech = (TechId)r.ReadByte();
        var remaining = r.ReadSingle();
        return new ResearchJob { Tech = tech, Remaining = remaining, Total = r.ReadSingle(), Cost = r.ReadSingle() };
    }

    static HeroId ReadHero(BinaryReader r) => new((Faction)r.ReadByte(), (HeroRole)r.ReadByte());

    /// <summary>Fields stay where they are for the whole battle: they are kept, and only their mines change.</summary>
    static void ReadFields(BinaryReader r, World replica)
    {
        var fields = new List<AsteroidField>();
        for (var n = Count(r); n > 0; n--)
        {
            fields.Add(new AsteroidField(r.Read7BitEncodedInt(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()) { MineId = Optional(r) });
        }
        if (!fields.Select(f => f.Id).SequenceEqual(replica.Fields.Select(f => f.Id)))
        {
            replica.Fields.Clear();
            replica.Fields.AddRange(fields);
            return;
        }
        for (var i = 0; i < fields.Count; i++) replica.Fields[i].MineId = fields[i].MineId;
    }

    static void ReadEntities(BinaryReader r, World replica, Side viewer)
    {
        var n = Count(r);
        var present = new HashSet<int>(n);
        var visible = new HashSet<int>();
        var seen = new HashSet<int>();
        for (var i = 0; i < n; i++)
        {
            var id = r.Read7BitEncodedInt();
            var e = replica.Entities.Get(id);
            if (e is null)
            {
                e = new Entity { Id = id };
                replica.Entities.Add(e);
            }
            present.Add(id);
            var remembered = ReadEntity(r, e, viewer);
            if (Worlds.Allies(replica, e.Side, viewer)) continue;
            if (!remembered) visible.Add(id);
            if (e.Kind != EntityKind.Ship) seen.Add(id);
        }
        List<int>? gone = null;
        foreach (var e in replica.Entities) if (!present.Contains(e.Id)) (gone ??= []).Add(e.Id);
        foreach (var id in gone ?? []) replica.Entities.Remove(id);
        // Only the viewer's sight is known here: Vision.IsVisible and WasSeen answer for it as on the host.
        replica.Visible.Clear();
        replica.Seen.Clear();
        replica.Visible[viewer] = visible;
        replica.Seen[viewer] = seen;
    }

    /// <summary>Reads one entity's record into it; true for a remembered structure, of which only the place and level came.</summary>
    static bool ReadEntity(BinaryReader r, Entity e, Side viewer)
    {
        var head = r.ReadByte();
        e.Kind = (EntityKind)(head & 3);
        e.Size = (SizeClass)(head >> 2 & 3);
        e.Type = ReadType(r);
        e.Side = new Side(r.Read7BitEncodedInt());
        e.Race = (Faction)r.ReadByte();
        e.X = r.ReadSingle();
        e.Y = r.ReadSingle();
        e.Radius = (double)r.ReadHalf();
        e.Level = r.Read7BitEncodedInt();
        e.ParentId = (head & HasParent) != 0 ? r.Read7BitEncodedInt() : null;
        e.Hero = (head & HasHero) != 0 ? ReadHero(r) : null;
        e.Build = (head & Building) != 0 ? r.ReadSingle() : 1;
        if ((head & Remembered) != 0) return true;
        e.Angle = r.ReadUInt16() * AngleUnit;
        var has = (Has)r.ReadUInt16();
        e.Speed = has.HasFlag(Has.Speed) ? (double)r.ReadHalf() : 0;
        if (has.HasFlag(Has.Shield))
        {
            e.Shield = r.ReadSingle();
            e.MaxShield = r.ReadSingle();
        }
        else e.Shield = e.MaxShield = 0;
        var weapons = has.HasFlag(Has.Weapons) ? Count(r) : 0;
        if (e.Weapons.Count != weapons) e.Weapons = [.. Enumerable.Range(0, weapons).Select(_ => new WeaponState(Unknown))];
        e.TargetId = has.HasFlag(Has.Target) ? r.Read7BitEncodedInt() : null;
        ReadAbilities(r, e, has.HasFlag(Has.Abilities) ? Count(r) : 0);
        var fx = e.Effects;
        if (has.HasFlag(Has.Effects))
        {
            fx.Marked = r.ReadSingle();
            fx.Stunned = r.ReadSingle();
            fx.Corroded = r.ReadSingle();
            fx.Taunted = r.ReadSingle();
            fx.TauntedBy = Optional(r);
        }
        else
        {
            fx.Marked = fx.Stunned = fx.Corroded = fx.Taunted = 0;
            fx.TauntedBy = null;
        }
        e.ShieldDown = has.HasFlag(Has.ShieldDown) ? r.ReadSingle() : 0;
        e.Expires = has.HasFlag(Has.Expires) ? r.ReadSingle() : null;
        if (has.HasFlag(Has.Hangar))
        {
            var cap = r.Read7BitEncodedInt();
            if (e.Hangar?.Def.Cap != cap) e.Hangar = new HangarState(new HangarDef([], cap, 0));
            e.Hangar.Launched.Clear();
            for (var n = Count(r); n > 0; n--) e.Hangar.Launched.Add(r.Read7BitEncodedInt());
        }
        else e.Hangar = null;
        e.Income = has.HasFlag(Has.Income) ? r.ReadSingle() : 0;
        if (has.HasFlag(Has.Engines))
        {
            e.Engines = r.Read7BitEncodedInt();
            e.MaxEngines = r.Read7BitEncodedInt();
        }
        else (e.Engines, e.MaxEngines) = (null, 0);
        e.ShieldGens = has.HasFlag(Has.ShieldGens) ? r.Read7BitEncodedInt() : null;
        e.Launched = has.HasFlag(Has.Launched);
        if (has.HasFlag(Has.Pilot))
        {
            var p = e.Pilot ??= new Pilot();
            p.Thrust = r.ReadSingle();
            p.Turn = r.ReadSingle();
            p.AimX = r.ReadSingle();
            p.AimY = r.ReadSingle();
            p.Firing = r.ReadBoolean();
        }
        else e.Pilot = null;
        e.Order = has.HasFlag(Has.Order) ? new Order((OrderType)r.ReadByte(), r.ReadSingle(), r.ReadSingle(), r.Read7BitEncodedInt()) : null;
        e.Range = has.HasFlag(Has.Range) ? r.ReadSingle() : 0;
        e.Hull = r.ReadSingle();
        e.MaxHull = r.ReadSingle();
        return false;
    }

    /// <summary>Abilities in place when they are the same ones, so that what holds them keeps them.</summary>
    static void ReadAbilities(BinaryReader r, Entity e, int n)
    {
        var same = e.Abilities.Count == n;
        var list = same ? e.Abilities : new List<AbilityState>(n);
        for (var i = 0; i < n; i++)
        {
            var id = (AbilityId)r.ReadByte();
            AbilityState a;
            if (same && list[i].Id == id) a = list[i];
            else
            {
                a = new AbilityState(id);
                if (same) list[i] = a;
                else list.Add(a);
            }
            a.Cooldown = r.ReadSingle();
            a.Active = r.ReadSingle();
        }
        e.Abilities = list;
    }

    /// <summary>Shots are listed by id, as fired: those still flying keep their object, for the view that follows them.</summary>
    static void ReadShots(BinaryReader r, World replica)
    {
        var old = replica.Projectiles;
        var shots = new List<Projectile>();
        var j = 0;
        for (var n = Count(r); n > 0; n--)
        {
            var id = r.Read7BitEncodedInt();
            while (j < old.Count && old[j].Id < id) j++;
            var p = j < old.Count && old[j].Id == id ? old[j] : new Projectile { Id = id };
            p.Side = new Side(r.Read7BitEncodedInt());
            p.Kind = (WeaponKind)r.ReadByte();
            p.X = r.ReadSingle();
            p.Y = r.ReadSingle();
            p.Vx = r.ReadSingle();
            p.Vy = r.ReadSingle();
            shots.Add(p);
        }
        replica.Projectiles = shots;
    }

    static Strike ReadStrike(BinaryReader r) => new()
    {
        Kind = (StrikeKind)r.ReadByte(), Side = new Side(r.Read7BitEncodedInt()),
        X = r.ReadSingle(), Y = r.ReadSingle(), X2 = r.ReadSingle(), Y2 = r.ReadSingle(),
        Radius = r.ReadSingle(), Damage = r.ReadSingle(), Delay = r.ReadSingle(), Total = r.ReadSingle(),
    };

    static string ReadType(BinaryReader r)
    {
        var code = r.ReadByte();
        return code == NamedType ? r.ReadString() : code < Types.Length ? Types[code] : throw new InvalidDataException($"Type inconnu : {code}");
    }

    static SimEvent ReadEvent(BinaryReader r) => (Tag)r.ReadByte() switch
    {
        Tag.Destroyed => new Destroyed(r.Read7BitEncodedInt(), (EntityKind)r.ReadByte(), ReadType(r), new Side(r.Read7BitEncodedInt()),
            r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
        Tag.Hit => new Hit(r.ReadSingle(), r.ReadSingle(), (WeaponKind)r.ReadByte(), r.Read7BitEncodedInt(), new Side(r.Read7BitEncodedInt()), Optional(r)),
        Tag.Built => new Built(r.Read7BitEncodedInt()),
        Tag.LanceOnline => new LanceOnline(r.Read7BitEncodedInt(), new Side(r.Read7BitEncodedInt())),
        Tag.Delivered => new Delivered(r.Read7BitEncodedInt()),
        Tag.Researched => new Researched(new Side(r.Read7BitEncodedInt()), (TechId)r.ReadByte()),
        Tag.AbilityUsed => new AbilityUsed((AbilityId)r.ReadByte(), new Side(r.Read7BitEncodedInt()), r.Read7BitEncodedInt(),
            r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
        Tag.StrikeLanded => new StrikeLanded(ReadStrike(r)),
        Tag.Rebuilt => new Rebuilt(r.Read7BitEncodedInt(), new Side(r.Read7BitEncodedInt()), (HardpointRole)r.ReadByte()),
        Tag.Victory => new Victory(new Side(r.Read7BitEncodedInt()), r.Read7BitEncodedInt()),
        Tag.RaidWon => new RaidWon(new Side(r.Read7BitEncodedInt()), r.ReadDouble(), r.ReadByte() is var t && t != byte.MaxValue ? (TechId)t : null),
        Tag.WaveIncoming => new WaveIncoming(r.Read7BitEncodedInt(), r.ReadSingle(), r.ReadSingle(), r.ReadByte() is var boss && boss != byte.MaxValue ? (ShipClass)boss : null),
        Tag.WaveCleared => new WaveCleared(r.Read7BitEncodedInt()),
        Tag.BeaconTaken => new BeaconTaken(r.Read7BitEncodedInt(), new Side(r.Read7BitEncodedInt())),
        Tag.BeaconLost => new BeaconLost(r.Read7BitEncodedInt(), new Side(r.Read7BitEncodedInt())),
        Tag.FlagshipLost => new FlagshipLost(new Side(r.Read7BitEncodedInt()), r.ReadSingle(), r.ReadSingle()),
        Tag.ClockWarning => new ClockWarning(r.ReadSingle()),
        var tag => throw new InvalidDataException($"Événement inconnu : {tag}"),
    };
}
