// Commands on the wire (not in the web version): a tag byte, then the fields in order. Ids and
// counts are 7-bit encoded, enums one byte, points full doubles: commands are few, and a host
// applies exactly what the player aimed at. Reading never trusts the bytes: a malformed message
// throws InvalidDataException or EndOfStreamException, and Apply refuses what reads but makes no sense.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

public static partial class Commands
{
    /// <summary>Layout of commands below: client and host compare it when they connect. Bumped with any change.</summary>
    public const int WireVersion = 2;

    /// <summary>Most ids a list may carry: a whole battle's.</summary>
    const int MaxList = 1 << 16;

    public static void Write(BinaryWriter w, Command c)
    {
        switch (c)
        {
            // Queued moves and attacks have tags of their own: the others keep the bytes of layout 1.
            case Command.Move m: Tag(w, m.Queued ? (byte)25 : (byte)1); WriteIds(w, m.Ships); w.Write(m.X); w.Write(m.Y); w.Write(m.AttackMove); break;
            case Command.Attack a: Tag(w, a.Queued ? (byte)26 : (byte)2); WriteIds(w, a.Ships); w.Write7BitEncodedInt(a.Target); break;
            case Command.Stop s: Tag(w, 3); WriteIds(w, s.Ships); break;
            case Command.Patrol p:
                Tag(w, 27); WriteIds(w, p.Ships); w.Write7BitEncodedInt(p.Points.Length);
                foreach (var point in p.Points)
                {
                    w.Write(point.X);
                    w.Write(point.Y);
                }
                w.Write(p.Queued);
                break;
            case Command.Guard g: Tag(w, 28); WriteIds(w, g.Ships); w.Write7BitEncodedInt(g.Ward); w.Write(g.Queued); break;
            case Command.SetStance st: Tag(w, 29); WriteIds(w, st.Ships); w.Write((byte)st.Stance); break;
            case Command.UseAbility u: Tag(w, 4); w.Write7BitEncodedInt(u.Caster); w.Write((byte)u.Ability); AimOf(w, u.Aim); break;
            case Command.EnqueueShip q: Tag(w, 5); w.Write((byte)q.Cls); w.Write7BitEncodedInt(q.Count); break;
            case Command.EnqueueHero h: Tag(w, 6); HeroOf(w, h.Hero); break;
            case Command.UpgradeStation: Tag(w, 7); break;
            case Command.UpgradeStructure u: Tag(w, 8); w.Write7BitEncodedInt(u.Structure); break;
            case Command.StartResearch r: Tag(w, 9); w.Write((byte)r.Tech); break;
            case Command.PlaceStructure p: Tag(w, 10); w.Write((byte)p.Type); w.Write(p.X); w.Write(p.Y); break;
            case Command.CancelQueued q: Tag(w, 11); w.Write7BitEncodedInt(q.Index); w.Write((byte)q.Queue); break;
            case Command.CancelConstruction cc: Tag(w, 12); w.Write7BitEncodedInt(cc.Structure); break;
            case Command.CancelResearch cr: Tag(w, 13); w.Write7BitEncodedInt(cr.Index); break;
            case Command.SetRally r: Tag(w, 14); w.Write(r.X); w.Write(r.Y); break;
            case Command.SetLoop l: Tag(w, 15); w.Write((byte)l.Cls); w.Write(l.On); break;
            case Command.SetAutoAbilities a: Tag(w, 16); w.Write(a.On); break;
            case Command.StartPilot p: Tag(w, 17); w.Write7BitEncodedInt(p.Ship); break;
            case Command.StopPilot p: Tag(w, 18); w.Write7BitEncodedInt(p.Ship); break;
            case Command.PilotInput p:
                Tag(w, 19); w.Write7BitEncodedInt(p.Ship); w.Write(p.Thrust); w.Write(p.Turn); w.Write(p.AimX); w.Write(p.AimY); w.Write(p.Firing);
                break;
            case Command.Spawn s: Tag(w, 20); w.Write((byte)s.Race); w.Write(s.What); w.Write(s.X); w.Write(s.Y); w.Write(s.Angle); break;
            case Command.GrantCredits g: Tag(w, 21); w.Write(g.Amount); break;
            case Command.SetFog f: Tag(w, 22); w.Write(f.On); break;
            case Command.SetAI a: Tag(w, 23); w.Write7BitEncodedInt(a.Side.Id); w.Write(a.Difficulty is Difficulty level ? (byte)level : byte.MaxValue); break;
            case Command.Destroy d: Tag(w, 24); WriteIds(w, d.Entities); break;
            default: throw new ArgumentException($"Commande sans format : {c.GetType().Name}");
        }
    }

    public static Command ReadCommand(BinaryReader r) => r.ReadByte() switch
    {
        1 => new Command.Move(ReadIds(r), r.ReadDouble(), r.ReadDouble(), r.ReadBoolean()),
        2 => new Command.Attack(ReadIds(r), r.ReadVarint()),
        3 => new Command.Stop(ReadIds(r)),
        4 => new Command.UseAbility(r.ReadVarint(), r.ReadEnum<AbilityId>(), AimOf(r)),
        5 => new Command.EnqueueShip(r.ReadEnum<ShipClass>(), r.ReadVarint()),
        6 => new Command.EnqueueHero(HeroOf(r)),
        7 => new Command.UpgradeStation(),
        8 => new Command.UpgradeStructure(r.ReadVarint()),
        9 => new Command.StartResearch(r.ReadEnum<TechId>()),
        10 => new Command.PlaceStructure(r.ReadEnum<BuildableType>(), r.ReadDouble(), r.ReadDouble()),
        11 => new Command.CancelQueued(r.ReadVarint(), r.ReadEnum<QueueSide>()),
        12 => new Command.CancelConstruction(r.ReadVarint()),
        13 => new Command.CancelResearch(r.ReadVarint()),
        14 => new Command.SetRally(r.ReadDouble(), r.ReadDouble()),
        15 => new Command.SetLoop(r.ReadEnum<ShipClass>(), r.ReadBoolean()),
        16 => new Command.SetAutoAbilities(r.ReadBoolean()),
        17 => new Command.StartPilot(r.ReadVarint()),
        18 => new Command.StopPilot(r.ReadVarint()),
        19 => new Command.PilotInput(r.ReadVarint(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadBoolean()),
        20 => new Command.Spawn(r.ReadEnum<Faction>(), r.ReadText(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble()),
        21 => new Command.GrantCredits(r.ReadDouble()),
        22 => new Command.SetFog(r.ReadBoolean()),
        23 => new Command.SetAI(new Side(r.ReadVarint()), r.ReadEnumOrNone<Difficulty>()),
        24 => new Command.Destroy(ReadIds(r)),
        25 => new Command.Move(ReadIds(r), r.ReadDouble(), r.ReadDouble(), r.ReadBoolean(), Queued: true),
        26 => new Command.Attack(ReadIds(r), r.ReadVarint(), Queued: true),
        27 => new Command.Patrol(ReadIds(r), PointsOf(r), r.ReadBoolean()),
        28 => new Command.Guard(ReadIds(r), r.ReadVarint(), r.ReadBoolean()),
        29 => new Command.SetStance(ReadIds(r), r.ReadEnum<Stance>()),
        var tag => throw new InvalidDataException($"Commande inconnue : {tag}"),
    };

    public static void Write(BinaryWriter w, GalaxyCommand c)
    {
        switch (c)
        {
            case GalaxyCommand.SendFleet s:
                Tag(w, 1); w.Write7BitEncodedInt(s.Fleet); w.Write7BitEncodedInt(s.Dest);
                w.Write(s.Ships is not null);
                if (s.Ships is { } ships)
                {
                    w.Write7BitEncodedInt(ships.Count);
                    foreach (var (cls, n) in ships)
                    {
                        w.Write((byte)cls);
                        w.Write7BitEncodedInt(n);
                    }
                }
                w.Write(s.Heroes is not null);
                if (s.Heroes is { } heroes)
                {
                    w.Write7BitEncodedInt(heroes.Length);
                    foreach (var h in heroes) HeroOf(w, h);
                }
                break;
            case GalaxyCommand.RedirectFleet f: Tag(w, 2); w.Write7BitEncodedInt(f.Fleet); w.Write7BitEncodedInt(f.Dest); break;
            case GalaxyCommand.StopFleet f: Tag(w, 3); w.Write7BitEncodedInt(f.Fleet); break;
            case GalaxyCommand.EnqueueBuild b: Tag(w, 4); w.Write7BitEncodedInt(b.Planet); w.Write((byte)b.Build.Kind); w.Write((byte)b.Build.Cls); HeroOf(w, b.Build.Hero); break;
            case GalaxyCommand.CancelBuild b: Tag(w, 5); w.Write7BitEncodedInt(b.Planet); w.Write7BitEncodedInt(b.Index); w.Write((byte)b.Queue); break;
            case GalaxyCommand.SetLoop l: Tag(w, 6); w.Write7BitEncodedInt(l.Planet); w.Write((byte)l.Cls); w.Write(l.On); break;
            case GalaxyCommand.SetRally r: Tag(w, 7); w.Write7BitEncodedInt(r.Planet); w.Write7BitEncodedInt(r.Target ?? -1); break;
            case GalaxyCommand.StartTech t: Tag(w, 8); w.Write((byte)t.Tech); break;
            case GalaxyCommand.CancelTech t: Tag(w, 9); w.Write7BitEncodedInt(t.Index); break;
            case GalaxyCommand.FireLance l: Tag(w, 10); w.Write7BitEncodedInt(l.From); w.Write7BitEncodedInt(l.Target); break;
            case GalaxyCommand.OfferTruce o: Tag(w, 11); w.Write7BitEncodedInt(o.To.Id); break;
            case GalaxyCommand.AnswerTruce a: Tag(w, 12); w.Write7BitEncodedInt(a.From.Id); w.Write(a.Accept); break;
            case GalaxyCommand.BreakTruce b: Tag(w, 13); w.Write7BitEncodedInt(b.Other.Id); break;
            case GalaxyCommand.ChooseBattle b: Tag(w, 14); w.Write7BitEncodedInt(b.Battle); w.Write((byte)b.Mode); break;
            case GalaxyCommand.DelegateBattle b: Tag(w, 15); w.Write7BitEncodedInt(b.Battle); break;
            case GalaxyCommand.Retreat b: Tag(w, 16); w.Write7BitEncodedInt(b.Battle); break;
            default: throw new ArgumentException($"Commande sans format : {c.GetType().Name}");
        }
    }

    public static GalaxyCommand ReadGalaxyCommand(BinaryReader r) => r.ReadByte() switch
    {
        1 => new GalaxyCommand.SendFleet(r.ReadVarint(), r.ReadVarint(), r.ReadBoolean() ? CountsOf(r) : null, r.ReadBoolean() ? HeroesOf(r) : null),
        2 => new GalaxyCommand.RedirectFleet(r.ReadVarint(), r.ReadVarint()),
        3 => new GalaxyCommand.StopFleet(r.ReadVarint()),
        4 => new GalaxyCommand.EnqueueBuild(r.ReadVarint(), new GalaxyBuild(r.ReadEnum<GalaxyBuildKind>(), r.ReadEnum<ShipClass>(), HeroOf(r))),
        5 => new GalaxyCommand.CancelBuild(r.ReadVarint(), r.ReadVarint(), r.ReadEnum<QueueSide>()),
        6 => new GalaxyCommand.SetLoop(r.ReadVarint(), r.ReadEnum<ShipClass>(), r.ReadBoolean()),
        7 => new GalaxyCommand.SetRally(r.ReadVarint(), r.ReadVarint() is var t && t >= 0 ? t : null),
        8 => new GalaxyCommand.StartTech(r.ReadEnum<TechId>()),
        9 => new GalaxyCommand.CancelTech(r.ReadVarint()),
        10 => new GalaxyCommand.FireLance(r.ReadVarint(), r.ReadVarint()),
        11 => new GalaxyCommand.OfferTruce(new Side(r.ReadVarint())),
        12 => new GalaxyCommand.AnswerTruce(new Side(r.ReadVarint()), r.ReadBoolean()),
        13 => new GalaxyCommand.BreakTruce(new Side(r.ReadVarint())),
        14 => new GalaxyCommand.ChooseBattle(r.ReadVarint(), r.ReadEnum<BattleStatus>()),
        15 => new GalaxyCommand.DelegateBattle(r.ReadVarint()),
        16 => new GalaxyCommand.Retreat(r.ReadVarint()),
        var tag => throw new InvalidDataException($"Commande galactique inconnue : {tag}"),
    };

    static void Tag(BinaryWriter w, byte tag) => w.Write(tag);

    static void WriteIds(BinaryWriter w, int[] ids)
    {
        w.Write7BitEncodedInt(ids.Length);
        foreach (var id in ids) w.Write7BitEncodedInt(id);
    }

    static int[] ReadIds(BinaryReader r)
    {
        var ids = new int[Length(r)];
        for (var i = 0; i < ids.Length; i++) ids[i] = r.ReadVarint();
        return ids;
    }

    /// <summary>A patrol's points: no more than a patrol takes, so a forged count allocates nothing.</summary>
    static Point[] PointsOf(BinaryReader r)
    {
        var n = r.ReadVarint();
        if (n is < 0 or >= Movement.MaxRoute) throw new InvalidDataException($"Patrouille de {n} points");
        var points = new Point[n];
        for (var i = 0; i < n; i++) points[i] = new Point(r.ReadDouble(), r.ReadDouble());
        return points;
    }

    static int Length(BinaryReader r)
    {
        var n = r.ReadVarint();
        return n is >= 0 and <= MaxList ? n : throw new InvalidDataException($"Liste de {n} éléments");
    }

    static void AimOf(BinaryWriter w, Aim? aim)
    {
        w.Write(aim is not null);
        if (aim is not Aim a) return;
        w.Write(a.X);
        w.Write(a.Y);
        w.Write7BitEncodedInt(a.Id ?? -1);
    }

    static Aim? AimOf(BinaryReader r) =>
        r.ReadBoolean() ? new Aim(r.ReadDouble(), r.ReadDouble(), r.ReadVarint() is var id && id >= 0 ? id : null) : null;

    static void HeroOf(BinaryWriter w, HeroId h)
    {
        w.Write((byte)h.Faction);
        w.Write((byte)h.Role);
    }

    static HeroId HeroOf(BinaryReader r) => new(r.ReadEnum<Faction>(), r.ReadEnum<HeroRole>());

    static ShipCounts CountsOf(BinaryReader r)
    {
        var counts = new ShipCounts();
        for (var n = Length(r); n > 0; n--) counts[r.ReadEnum<ShipClass>()] = r.ReadVarint();
        return counts;
    }

    static HeroId[] HeroesOf(BinaryReader r)
    {
        var heroes = new HeroId[Length(r)];
        for (var i = 0; i < heroes.Length; i++) heroes[i] = HeroOf(r);
        return heroes;
    }
}
