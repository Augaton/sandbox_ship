// What players ask of a battle or of the galaxy, as plain data, and its application (not in the
// web version). The screens no longer call the simulation themselves: they send commands, that a
// sink (CommandSinks.cs) applies at once, keeps for the next step, or hands to a host. A command
// names entities, fleets and planets by id, never by reference, and does not say whose it is:
// whoever applies it stamps it with the side it came from, and Apply checks that side's rights.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

/// <summary>A battle command. Ships ordered together are listed in the order they were selected: it sets their places in the formation.</summary>
public abstract record Command
{
    /// <summary>
    /// Ships to a point in formation; with `AttackMove`, they fight what they meet on the way. Orders
    /// `Queued` (Shift) wait for the ones before them to be done.
    /// </summary>
    public sealed record Move(int[] Ships, double X, double Y, bool AttackMove = false, bool Queued = false) : Command;
    /// <summary>Ships at a foe the issuer's team sees.</summary>
    public sealed record Attack(int[] Ships, int Target, bool Queued = false) : Command;
    public sealed record Stop(int[] Ships) : Command;
    /// <summary>Ships round the points and back for good, fighting what they meet (Movement.IssuePatrol).</summary>
    public sealed record Patrol(int[] Ships, Point[] Points, bool Queued = false) : Command;
    /// <summary>Ships guarding one of the issuer's side or of an ally's, ship or structure, and following it.</summary>
    public sealed record Guard(int[] Ships, int Ward, bool Queued = false) : Command;
    /// <summary>How ships fight between orders.</summary>
    public sealed record SetStance(int[] Ships, Stance Stance) : Command;
    /// <summary>One ability of one ship or structure, at the aim when it needs one.</summary>
    public sealed record UseAbility(int Caster, AbilityId Ability, Aim? Aim = null) : Command;
    /// <summary>`Count` ships of a class at once (Shift: five), as many as the queue and the credits allow.</summary>
    public sealed record EnqueueShip(ShipClass Cls, int Count = 1) : Command;
    public sealed record EnqueueHero(HeroId Hero) : Command;
    /// <summary>The station's next level.</summary>
    public sealed record UpgradeStation : Command;
    /// <summary>The next level of a platform or an outpost.</summary>
    public sealed record UpgradeStructure(int Structure) : Command;
    public sealed record StartResearch(TechId Tech) : Command;
    public sealed record PlaceStructure(BuildableType Type, double X, double Y) : Command;
    /// <summary>A queued ship (units) or station work (buildings), refunded; `Index` as the queue stood when sent.</summary>
    public sealed record CancelQueued(int Index, QueueSide Queue) : Command;
    public sealed record CancelConstruction(int Structure) : Command;
    /// <summary>A research job, 0 being the running one.</summary>
    public sealed record CancelResearch(int Index) : Command;
    /// <summary>Where new ships gather once out of the shipyard.</summary>
    public sealed record SetRally(double X, double Y) : Command;
    /// <summary>Keeps a ship class in production, or stops it.</summary>
    public sealed record SetLoop(ShipClass Cls, bool On) : Command;
    /// <summary>The issuer's ships use their abilities by themselves.</summary>
    public sealed record SetAutoAbilities(bool On) : Command;
    /// <summary>Takes the controls of a ship: it forgets its order and flies by hand.</summary>
    public sealed record StartPilot(int Ship) : Command;
    /// <summary>Gives the controls back: the ship holds where it is.</summary>
    public sealed record StopPilot(int Ship) : Command;
    /// <summary>A flown ship's controls, sent whenever they change: thrust and turn from -1 to 1, the point aimed at, the trigger.</summary>
    public sealed record PilotInput(int Ship, double Thrust, double Turn, double AimX, double AimY, bool Firing) : Command;

    // Sandbox tools: only where the world allows them (World.SandboxTools).

    /// <summary>A unit or structure of a race, on that race's side: `What` is a ship class, a structure, "station" or a hero role, as Ids name them.</summary>
    public sealed record Spawn(Faction Race, string What, double X, double Y, double Angle) : Command;
    public sealed record GrantCredits(double Amount) : Command;
    public sealed record SetFog(bool On) : Command;
    /// <summary>A side plays by itself at this difficulty, or (null) only does what it is told.</summary>
    public sealed record SetAI(Side Side, Difficulty? Difficulty) : Command;
    /// <summary>Destroys these entities, whoever they belong to.</summary>
    public sealed record Destroy(int[] Entities) : Command;
}

/// <summary>A galaxy command.</summary>
public abstract record GalaxyCommand
{
    /// <summary>A docked fleet towards a planet: whole, or the ships and heroes chosen, the rest staying behind.</summary>
    public sealed record SendFleet(int Fleet, int Dest, ShipCounts? Ships = null, HeroId[]? Heroes = null) : GalaxyCommand;
    /// <summary>A fleet, docked or on the move, towards a planet the shorter way.</summary>
    public sealed record RedirectFleet(int Fleet, int Dest) : GalaxyCommand;
    /// <summary>A fleet on the move stops at its next planet.</summary>
    public sealed record StopFleet(int Fleet) : GalaxyCommand;
    public sealed record EnqueueBuild(int Planet, GalaxyBuild Build) : GalaxyCommand;
    public sealed record CancelBuild(int Planet, int Index, QueueSide Queue) : GalaxyCommand;
    public sealed record SetLoop(int Planet, ShipClass Cls, bool On) : GalaxyCommand;
    /// <summary>Where a planet's new ships leave for; null, or the planet itself, clears it.</summary>
    public sealed record SetRally(int Planet, int? Target) : GalaxyCommand;
    public sealed record StartTech(TechId Tech) : GalaxyCommand;
    public sealed record CancelTech(int Index) : GalaxyCommand;
    /// <summary>Fires the star lance of planet `From` at planet `Target`.</summary>
    public sealed record FireLance(int From, int Target) : GalaxyCommand;
    public sealed record OfferTruce(Side To) : GalaxyCommand;
    /// <summary>Accepts or refuses the truce `From` offered.</summary>
    public sealed record AnswerTruce(Side From, bool Accept) : GalaxyCommand;
    public sealed record BreakTruce(Side Other) : GalaxyCommand;
    /// <summary>Offers `To` a deal: a tribute or a demand of `Amount` credits, once or each minute.</summary>
    public sealed record ProposeDeal(Side To, DealKind Kind, double Amount = 0, bool PerMinute = false) : GalaxyCommand;
    /// <summary>Accepts or refuses the deal of this kind `From` offered.</summary>
    public sealed record AnswerDeal(Side From, DealKind Kind, bool Accept) : GalaxyCommand;
    /// <summary>Ends a deal with `Other`: breaks a truce or an alliance, gives up trade, stops or lets off a tribute.</summary>
    public sealed record EndDeal(Side Other, DealKind Kind) : GalaxyCommand;
    /// <summary>
    /// How the issuer wants a battle waiting for it fought: commanded in the battle view (Tactical)
    /// or resolved on its own (Auto). It starts once every human side in it has chosen.
    /// </summary>
    public sealed record ChooseBattle(int Battle, BattleStatus Mode) : GalaxyCommand;
    /// <summary>Leaves a battle being commanded: the AI plays the issuer's side; with nobody commanding it, it plays on by itself.</summary>
    public sealed record DelegateBattle(int Battle) : GalaxyCommand;
    /// <summary>The attacker falls back: its survivors return where they came from.</summary>
    public sealed record Retreat(int Battle) : GalaxyCommand;
}

public static partial class Commands
{
    /// <summary>Sandbox spawns stop here: past it, the game would crawl.</summary>
    public const int MaxEntities = 2500;

    static readonly Result Invalid = Result.Fail("Ordre invalide");
    static readonly Result NotYours = Result.Fail("Pas sous tes ordres");

    static bool Defined<T>(T value) where T : struct, Enum => Enum.IsDefined(value);
    static bool Finite(double x, double y) => double.IsFinite(x) && double.IsFinite(y);

    /// <summary>
    /// Applies a battle command for `issuer`, who may only order its own side's entities and
    /// queues, and only aim at foes its team sees. Ids of entities gone since pass: the order
    /// skips them. Malformed commands (unknown values, points off any map) are refused, never thrown on.
    /// </summary>
    public static Result Apply(World world, Side issuer, Command c)
    {
        // Kept as it comes, whatever comes of it: played again, it comes to the same.
        world.Log?.Add(world.StepCount, issuer, c);
        switch (c)
        {
            case Command.Move m:
                if (!Finite(m.X, m.Y)) return Invalid;
                if (Own(world, issuer, m.Ships) is { Ok: false } moveRefused) return moveRefused;
                Movement.IssueMove(world, m.Ships, m.X, m.Y, m.AttackMove, m.Queued);
                return Result.OK;
            case Command.Attack a:
            {
                if (Own(world, issuer, a.Ships) is { Ok: false } refused) return refused;
                if (world.Entities.Get(a.Target) is not { } target || !Vision.IsVisible(world, issuer, target)) return Result.Fail("Cible hors de vue");
                Movement.IssueAttack(world, a.Ships, a.Target, a.Queued);
                return Result.OK;
            }
            case Command.Stop s:
                if (Own(world, issuer, s.Ships) is { Ok: false } stopRefused) return stopRefused;
                Movement.IssueStop(world, s.Ships);
                return Result.OK;
            case Command.Patrol p:
            {
                if (p.Points is not { Length: > 0 and < Movement.MaxRoute } points) return Invalid;
                foreach (var point in points) if (!Finite(point.X, point.Y)) return Invalid;
                if (Own(world, issuer, p.Ships) is { Ok: false } refused) return refused;
                Movement.IssuePatrol(world, p.Ships, points, p.Queued);
                return Result.OK;
            }
            case Command.Guard g:
            {
                if (Own(world, issuer, g.Ships) is { Ok: false } refused) return refused;
                if (world.Entities.Get(g.Ward) is not { } ward || !Worlds.Allies(world, issuer, ward.Side)) return Result.Fail("Choisis un allié");
                Movement.IssueGuard(world, g.Ships, g.Ward, g.Queued);
                return Result.OK;
            }
            case Command.SetStance st:
                if (!Defined(st.Stance)) return Invalid;
                if (Own(world, issuer, st.Ships) is { Ok: false } stanceRefused) return stanceRefused;
                Movement.SetStance(world, st.Ships, st.Stance);
                return Result.OK;
            case Command.UseAbility u:
            {
                if (!Defined(u.Ability) || u.Aim is { } aim && !Finite(aim.X, aim.Y)) return Invalid;
                if (world.Entities.Get(u.Caster) is not { } e) return Result.Fail("Unité détruite");
                if (e.Side != issuer) return NotYours;
                return AbilitySystem.UseAbility(world, e, u.Ability, u.Aim);
            }
            case Command.EnqueueShip q:
            {
                if (!Defined(q.Cls) || q.Count < 1) return Invalid;
                var first = Structures.EnqueueShip(world, issuer, q.Cls);
                for (var i = 1; first.Ok && i < q.Count; i++)
                {
                    if (!Structures.EnqueueShip(world, issuer, q.Cls).Ok) break;
                }
                return first;
            }
            case Command.EnqueueHero h:
                return Hero(h.Hero) ? Structures.EnqueueHero(world, issuer, h.Hero) : Invalid;
            case Command.UpgradeStation:
                return Structures.EnqueueUpgrade(world, issuer);
            case Command.UpgradeStructure u:
                return Structures.UpgradeStructure(world, issuer, u.Structure);
            case Command.StartResearch r:
                return Defined(r.Tech) ? Structures.StartResearch(world, issuer, r.Tech) : Invalid;
            case Command.PlaceStructure p:
                return Defined(p.Type) && Finite(p.X, p.Y) ? Structures.PlaceStructure(world, issuer, p.Type, p.X, p.Y) : Invalid;
            case Command.CancelQueued q:
                if (!Defined(q.Queue)) return Invalid;
                Structures.CancelQueued(world, issuer, q.Index, q.Queue);
                return Result.OK;
            case Command.CancelConstruction cc:
                Structures.CancelConstruction(world, issuer, cc.Structure);
                return Result.OK;
            case Command.CancelResearch cr:
                Structures.CancelResearch(world, issuer, cr.Index);
                return Result.OK;
            case Command.SetRally r:
            {
                if (!Finite(r.X, r.Y)) return Invalid;
                if (!world.Factions.TryGetValue(issuer, out var f)) return NotYours;
                // On the map: new ships fly there as they are told, and a snapshot's float holds it.
                f.RallyX = Math.Clamp(r.X, 0, world.Width);
                f.RallyY = Math.Clamp(r.Y, 0, world.Height);
                return Result.OK;
            }
            case Command.SetLoop l:
            {
                if (!Defined(l.Cls)) return Invalid;
                if (!world.Factions.TryGetValue(issuer, out var f)) return NotYours;
                if (!l.On) f.Repeat.Remove(l.Cls);
                else if (!f.Repeat.Contains(l.Cls)) f.Repeat.Add(l.Cls);
                return Result.OK;
            }
            case Command.SetAutoAbilities a:
            {
                if (!world.Factions.TryGetValue(issuer, out var f)) return NotYours;
                f.AutoAbilities = a.On;
                return Result.OK;
            }
            case Command.StartPilot p:
            {
                if (OwnShip(world, issuer, p.Ship) is not { } s) return NotYours;
                s.Order = null;
                s.Queue?.Clear();
                s.Pilot = new Pilot { AimX = s.X, AimY = s.Y };
                return Result.OK;
            }
            case Command.StopPilot p:
            {
                if (OwnShip(world, issuer, p.Ship) is not { } s) return NotYours;
                s.Pilot = null;
                s.GuardX = s.X;
                s.GuardY = s.Y;
                return Result.OK;
            }
            case Command.PilotInput p:
            {
                if (!Finite(p.Thrust, p.Turn) || !Finite(p.AimX, p.AimY)) return Invalid;
                if (OwnShip(world, issuer, p.Ship)?.Pilot is not { } pilot) return Result.Fail("Vaisseau non piloté");
                pilot.Thrust = Math.Clamp(p.Thrust, -1, 1);
                pilot.Turn = Math.Clamp(p.Turn, -1, 1);
                pilot.Firing = p.Firing;
                pilot.AimX = p.AimX;
                pilot.AimY = p.AimY;
                return Result.OK;
            }
            case Command.Spawn or Command.GrantCredits or Command.SetFog or Command.SetAI or Command.Destroy when !world.SandboxTools:
                return Result.Fail("Réservé au bac à sable");
            case Command.Spawn s:
                if (!Defined(s.Race) || s.What is null || !Finite(s.X, s.Y) || !double.IsFinite(s.Angle)) return Invalid;
                // On the map: far off it, a replay forged so would bring the spatial grid down.
                return Spawn(world, s.Race, s.What, Math.Clamp(s.X, 0, world.Width), Math.Clamp(s.Y, 0, world.Height), s.Angle);
            case Command.GrantCredits g:
            {
                if (!double.IsFinite(g.Amount)) return Invalid;
                if (!world.Factions.TryGetValue(issuer, out var f)) return NotYours;
                f.Credits += g.Amount;
                return Result.OK;
            }
            case Command.SetFog fog:
                world.Fog = fog.On;
                return Result.OK;
            case Command.SetAI ai:
            {
                if (ai.Difficulty is Difficulty d && !Defined(d)) return Invalid;
                if (!world.Factions.TryGetValue(ai.Side, out var f)) return Result.Fail("Camp inconnu");
                f.AI = ai.Difficulty is Difficulty level ? Worlds.NewAI(level) : null;
                return Result.OK;
            }
            case Command.Destroy d:
                if (d.Entities is null) return Invalid;
                foreach (var id in d.Entities)
                {
                    if (world.Entities.Get(id) is { } e) e.Hull = 0;
                }
                return Result.OK;
            default:
                return Invalid;
        }
    }

    /// <summary>Every entity named that still exists is the issuer's.</summary>
    static Result Own(World world, Side issuer, int[]? ids)
    {
        if (ids is null) return Invalid;
        foreach (var id in ids)
        {
            if (world.Entities.Get(id) is { } e && e.Side != issuer) return NotYours;
        }
        return Result.OK;
    }

    static Entity? OwnShip(World world, Side issuer, int id) =>
        world.Entities.Get(id) is { Kind: EntityKind.Ship } s && s.Side == issuer ? s : null;

    static bool Hero(HeroId id) => Heroes.Defs.ContainsKey(id);

    /// <summary>Places a unit or structure of a race for the sandbox, on that race's own side; refused once the battle is full.</summary>
    static Result Spawn(World world, Faction race, string what, double x, double y, double angle)
    {
        if (world.Entities.Count >= MaxEntities) return Result.Fail($"Limite de {MaxEntities} éléments atteinte");
        if (!world.Factions.ContainsKey(race)) Worlds.AddFaction(world, race, 0);
        if (what == "station")
        {
            if (Worlds.StationOf(world, race) is null) Worlds.SpawnStation(world, race, x, y);
        }
        else if (Enum.TryParse<HeroRole>(what, true, out var role) && Defined(role))
        {
            // The Errants have no heroes.
            var id = new HeroId(race, role);
            if (!Hero(id)) return Result.Fail("Aucun héros pour cette faction");
            Worlds.SpawnHero(world, race, id, x, y, angle);
        }
        else if (Ids.IsShip(what))
        {
            Worlds.SpawnShip(world, race, Ids.Ship(what), x, y, angle);
        }
        else if (Ids.IsBuildable(what, out var type))
        {
            var e = Worlds.SpawnStructure(world, race, type, x, y);
            var field = type == BuildableType.Mine ? world.Fields.Find(f => f.MineId is null && Worlds.Dist(f, e) <= f.Radius) : null;
            if (field is not null)
            {
                e.X = field.X;
                e.Y = field.Y;
                field.MineId = e.Id;
                e.FieldId = field.Id;
            }
        }
        return Result.OK;
    }

    /// <summary>
    /// Applies a galaxy command for `issuer`, who may only move its own fleets, run its own
    /// planets and research, and choose in its own battles. Malformed commands are refused.
    /// </summary>
    public static Result Apply(Galaxy g, Side issuer, GalaxyCommand c)
    {
        switch (c)
        {
            case GalaxyCommand.SendFleet s:
                if (!Planet(g, s.Dest) || s.Ships is { } ships && !Counts(ships) || s.Heroes is { } heroes && !Distinct(heroes)) return Invalid;
                return OwnFleet(g, issuer, s.Fleet) ?? Galaxies.SendFleet(g, s.Fleet, s.Dest, s.Ships, s.Heroes);
            case GalaxyCommand.RedirectFleet r:
                if (!Planet(g, r.Dest)) return Invalid;
                return OwnFleet(g, issuer, r.Fleet) ?? Galaxies.RedirectFleet(g, r.Fleet, r.Dest);
            case GalaxyCommand.StopFleet s:
                return OwnFleet(g, issuer, s.Fleet) ?? Galaxies.StopFleet(g, s.Fleet);
            case GalaxyCommand.EnqueueBuild b:
            {
                var build = b.Build;
                if (!Planet(g, b.Planet) || !Defined(build.Kind) || !Defined(build.Cls) || build.Kind == GalaxyBuildKind.Hero && !Hero(build.Hero)) return Invalid;
                return Galaxies.EnqueueBuild(g, issuer, b.Planet, build);
            }
            case GalaxyCommand.CancelBuild b:
                if (!Planet(g, b.Planet) || !Defined(b.Queue)) return Invalid;
                if (g.Planets[b.Planet].Owner != issuer) return NotYours;
                Galaxies.CancelBuild(g, b.Planet, b.Index, b.Queue);
                return Result.OK;
            case GalaxyCommand.SetLoop l:
                return Planet(g, l.Planet) && Defined(l.Cls) ? Galaxies.SetLoop(g, issuer, l.Planet, l.Cls, l.On) : Invalid;
            case GalaxyCommand.SetRally r:
                return Planet(g, r.Planet) && (r.Target is not int target || Planet(g, target)) ? Galaxies.SetRally(g, issuer, r.Planet, r.Target) : Invalid;
            case GalaxyCommand.StartTech t:
                return Defined(t.Tech) ? Galaxies.StartTech(g, issuer, t.Tech) : Invalid;
            case GalaxyCommand.CancelTech t:
                Galaxies.CancelTech(g, issuer, t.Index);
                return Result.OK;
            case GalaxyCommand.FireLance l:
                return Planet(g, l.From) && Planet(g, l.Target) ? Galaxies.FireLance(g, issuer, l.From, l.Target) : Invalid;
            case GalaxyCommand.OfferTruce o:
                return Galaxies.ProposeTruce(g, issuer, o.To);
            case GalaxyCommand.AnswerTruce a:
                if (a.Accept) return Galaxies.AcceptTruce(g, issuer, a.From);
                Galaxies.RefuseTruce(g, issuer, a.From);
                return Result.OK;
            case GalaxyCommand.BreakTruce b:
                return Galaxies.BreakTruce(g, issuer, b.Other);
            case GalaxyCommand.ProposeDeal p:
                if (!Defined(p.Kind) || !double.IsFinite(p.Amount)) return Invalid;
                return Galaxies.ProposeDeal(g, issuer, p.To, p.Kind, p.Amount, p.PerMinute);
            case GalaxyCommand.AnswerDeal a:
                if (!Defined(a.Kind)) return Invalid;
                if (a.Accept) return Galaxies.AcceptDeal(g, issuer, a.From, a.Kind);
                Galaxies.RefuseDeal(g, issuer, a.From, a.Kind);
                return Result.OK;
            case GalaxyCommand.EndDeal e:
                return Defined(e.Kind) ? Galaxies.EndDeal(g, issuer, e.Other, e.Kind) : Invalid;
            case GalaxyCommand.ChooseBattle b:
                if (b.Mode is not (BattleStatus.Auto or BattleStatus.Tactical)) return Invalid;
                if (OwnBattle(g, issuer, b.Battle) is not { } choosing) return NotYours;
                Galaxies.Choose(g, issuer, choosing.Id, b.Mode);
                return Result.OK;
            case GalaxyCommand.DelegateBattle b:
                if (OwnBattle(g, issuer, b.Battle) is not { } delegated) return NotYours;
                Galaxies.Delegate(g, issuer, delegated.Id);
                return Result.OK;
            case GalaxyCommand.Retreat b:
                if (g.Battles.Find(x => x.Id == b.Battle) is not { } battle || battle.Attacker != issuer) return NotYours;
                Galaxies.ConcludeBattle(g, battle.Id, BattleResult.Retreat);
                return Result.OK;
            default:
                return Invalid;
        }
    }

    static bool Planet(Galaxy g, int id) => (uint)id < (uint)g.Planets.Count;

    /// <summary>Ship counts to split off a fleet: known classes, none below zero (a negative count would add ships).</summary>
    static bool Counts(ShipCounts ships) => ships.All(k => Defined(k.Key) && k.Value >= 0);

    /// <summary>Heroes to send, each once: a hero named twice would be cloned.</summary>
    static bool Distinct(HeroId[] heroes) => heroes.Distinct().Count() == heroes.Length;

    /// <summary>Null when the fleet is the issuer's (or gone: the order then says so itself).</summary>
    static Result? OwnFleet(Galaxy g, Side issuer, int id) =>
        g.Fleets.TryGetValue(id, out var f) && f.Side != issuer ? NotYours : null;

    static GalaxyBattle? OwnBattle(Galaxy g, Side issuer, int id) =>
        g.Battles.Find(b => b.Id == id) is { } b && (b.Attacker == issuer || b.Defender == issuer) ? b : null;
}
