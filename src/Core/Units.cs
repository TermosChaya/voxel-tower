using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

public enum Team : byte { Player, Horde, Wild }

public enum UnitState : byte { Idle, Walk, Work, Fight, Carve, Siege, Flee, Dead }

/// <summary>What a pawn with nothing to do passes the time with: for its pose and for the panel.</summary>
public enum Pastime : byte { None, Stroll, Look, Sit, Talk, Stretch, Warm, Watch }

/// <summary>A kind of pawn or attacker: how it moves, works, fights, and what it looks like.</summary>
public sealed class UnitType
{
    public string Key, Name;
    public Team Team;
    public Mover Move;
    /// <summary>Labour: how fast this kind works (a worker is 1) and how much of what it digs reaches the stores.</summary>
    public float Work, Yield;
    /// <summary>Animals: how close a pawn or raider may come before it bolts, in voxels.</summary>
    public float FleeDist;
    public float Hp, Damage, AttackTime;
    /// <summary>Melee reach and bow range, in voxels (8 to the metre).</summary>
    public float Reach = 10, Range;
    public bool Ranged;
    /// <summary>Damage to the citadel per blow.</summary>
    public float KeepDamage;
    public int Cost, Bounty;
    /// <summary>Body size in voxels, for the model and for being hit: a man is 15 tall (1.9 m with his hat).</summary>
    public float BodyH = 15, BodyR = 2;
    public string Tool = "";
    /// <summary>How many raiders of this kind come with a torch, to burn through what is wooden.</summary>
    public float TorchShare;
}

public static class Kinds
{
    public static readonly UnitType Worker = new()
    {
        Key = "worker", Name = "Рабочий", Team = Team.Player,
        // pawns scramble up a ledge a head higher than themselves: out of a two-metre pit, onto a low wall
        Move = new Mover { Height = 2, Climb = 2, Speed = 2.8f },
        Work = 1, Yield = 1, Hp = 60, Damage = 6, AttackTime = 1.1f, Cost = 25,
        Tool = "pick",
    };
    public static readonly UnitType Archer = new()
    {
        Key = "archer", Name = "Лучник", Team = Team.Player,
        Move = new Mover { Height = 2, Climb = 2, Speed = 2.6f },
        Work = .4f, Yield = .6f, Hp = 70, Damage = 18, AttackTime = 1.3f, Ranged = true, Range = 24 * VoxelWorld.PerMetre, Cost = 40, Tool = "bow",
    };
    public static readonly UnitType Swordsman = new()
    {
        Key = "sword", Name = "Мечник", Team = Team.Player,
        Move = new Mover { Height = 2, Climb = 2, Speed = 2.4f },
        Work = .4f, Yield = .6f, Hp = 140, Damage = 16, AttackTime = 1f, Cost = 35, Tool = "sword", BodyR = 2.5f, Reach = 11,
    };
    public static readonly UnitType Goblin = new()
    {
        Key = "goblin", Name = "Гоблин", Team = Team.Horde,
        Move = new Mover { Height = 2, Climb = 1, Drop = 5, Speed = 3f, Carve = new float[] { 10, 22, 3, 1 }, Build = .35f },
        Hp = 40, Damage = 5, AttackTime = .9f, KeepDamage = 1, Bounty = 3, BodyH = 10, BodyR = 1.5f, Reach = 9, Tool = "knife", TorchShare = .4f,
    };
    public static readonly UnitType Orc = new()
    {
        Key = "orc", Name = "Орк-рубака", Team = Team.Horde,
        Move = new Mover { Height = 2, Speed = 2f, Carve = new float[] { 5, 120, 14, 2 }, Build = .25f },
        Hp = 110, Damage = 14, AttackTime = 1.2f, KeepDamage = 3, Bounty = 5, BodyH = 16, BodyR = 2.5f, Reach = 11, Tool = "axe", TorchShare = .25f,
    };
    public static readonly UnitType Miner = new()
    {
        Key = "miner", Name = "Рудокоп", Team = Team.Horde,
        Move = new Mover { Height = 2, Climb = 1, Drop = 5, Speed = 2.1f, Carve = new float[] { 140, 35, 55, 24 }, Build = .5f },
        Hp = 70, Damage = 9, AttackTime = 1.1f, KeepDamage = 2, Bounty = 5, BodyH = 12, BodyR = 2, Reach = 9, Tool = "pick",
    };
    public static readonly UnitType Troll = new()
    {
        Key = "troll", Name = "Тролль", Team = Team.Horde,
        Move = new Mover { Height = 3, Climb = 2, Drop = 5, Speed = 1.6f, Carve = new float[] { 60, 140, 90, 14 }, Build = .15f },
        Hp = 420, Damage = 30, AttackTime = 1.6f, KeepDamage = 9, Bounty = 18, BodyH = 24, BodyR = 3.5f, Reach = 14, Tool = "club",
    };
    /// <summary>The horde's own worker: raises ladders, lays bridges, fills ditches; poor in a fight.</summary>
    public static readonly UnitType Sapper = new()
    {
        Key = "sapper", Name = "Гоблин-сапёр", Team = Team.Horde,
        Move = new Mover { Height = 2, Climb = 1, Drop = 5, Speed = 2.7f, Carve = new float[] { 14, 26, 4, 1 }, Build = 1 },
        Hp = 45, Damage = 4, AttackTime = 1.1f, KeepDamage = 1, Bounty = 4, BodyH = 10, BodyR = 1.5f, Reach = 9, Tool = "mallet",
    };

    /// <summary>
    /// A thief of the horde: quick and weak, it builds nothing and keeps out of a fight. It makes for the stores in the
    /// citadel, or for a worker of ours caught alone, and runs off west with what it took.
    /// </summary>
    public static readonly UnitType Thief = new()
    {
        Key = "thief", Name = "Грабитель", Team = Team.Horde,
        Move = new Mover { Height = 2, Climb = 1, Drop = 5, Speed = 3.6f, Carve = new float[] { 10, 22, 3, 1 }, Build = .35f },
        Hp = 35, Damage = 7, AttackTime = .8f, KeepDamage = 0, Bounty = 6, BodyH = 10, BodyR = 1.5f, Reach = 9, Tool = "knife",
    };

    // the horde's engines: timber on wheels, slow, little hurt by arrows and quick to burn; none of them fights
    /// <summary>A battering ram under a roof of hides on four wheels: it breaks through walls and gates like nothing else, and at the citadel batters that.</summary>
    public static readonly UnitType Ram = new()
    {
        Key = "ram", Name = "Таран", Team = Team.Horde,
        Move = new Mover { Height = 3, Climb = 1, Drop = 1, Speed = 1.1f, Carve = new float[] { 30, 600, 220, 12 }, Build = 0 },
        Hp = 520, Damage = 0, AttackTime = 2.4f, KeepDamage = 45, Bounty = 30, BodyH = 18, BodyR = 6, Reach = 14,
    };
    /// <summary>A stone-thrower: it stops out of bowshot of the walls and throws stones that break them up, a little at a time.</summary>
    public static readonly UnitType Catapult = new()
    {
        Key = "catapult", Name = "Камнемёт", Team = Team.Horde,
        Move = new Mover { Height = 3, Climb = 1, Drop = 1, Speed = .9f, Build = 0 },
        Hp = 300, Damage = 0, AttackTime = 7, Bounty = 30, BodyH = 20, BodyR = 6, Range = 40 * VoxelWorld.PerMetre,
    };
    /// <summary>A mantlet: a wall of planks on two wheels pushed before a band; arrows loosed at those behind it strike it instead.</summary>
    public static readonly UnitType Mantlet = new()
    {
        Key = "mantlet", Name = "Мантлет", Team = Team.Horde,
        Move = new Mover { Height = 2, Climb = 1, Drop = 2, Speed = 2.2f, Build = 0 },
        Hp = 260, Damage = 0, AttackTime = 1, Bounty = 12, BodyH = 16, BodyR = 5,
    };
    /// <summary>A siege tower: a tower of timber under hides on six wheels; rolled up to a wall it drops a ladder against it, inside its own timbers, that no one can throw down.</summary>
    public static readonly UnitType SiegeTower = new()
    {
        Key = "tower", Name = "Осадная башня", Team = Team.Horde,
        Move = new Mover { Height = 3, Climb = 1, Drop = 1, Speed = .7f, Build = 0 },
        Hp = 800, Damage = 0, AttackTime = 1, Bounty = 40, BodyH = 44, BodyR = 8,
    };
    public static readonly UnitType[] Engines = { Ram, Catapult, Mantlet, SiegeTower };
    public static bool IsEngine(UnitType t) => t == Ram || t == Catapult || t == Mantlet || t == SiegeTower;

    // the valley's beasts: they graze and wander, bolt from anyone who comes near, and pay for the hunt
    public static readonly UnitType Deer = new()
    {
        Key = "deer", Name = "Олень", Team = Team.Wild,
        Move = new Mover { Height = 2, Climb = 1, Drop = 3, Speed = 4.2f },
        Hp = 50, Damage = 0, AttackTime = 1, Bounty = 6, BodyH = 13, BodyR = 3, FleeDist = 20 * VoxelWorld.PerMetre,
    };
    public static readonly UnitType Boar = new()
    {
        Key = "boar", Name = "Кабан", Team = Team.Wild,
        Move = new Mover { Height = 1, Climb = 1, Drop = 3, Speed = 3.2f },
        Hp = 90, Damage = 9, AttackTime = 1.1f, Bounty = 9, BodyH = 7, BodyR = 3, Reach = 9, FleeDist = 10 * VoxelWorld.PerMetre,
    };
    public static readonly UnitType Hare = new()
    {
        Key = "hare", Name = "Заяц", Team = Team.Wild,
        Move = new Mover { Height = 1, Climb = 1, Drop = 3, Speed = 4.8f },
        Hp = 10, Damage = 0, AttackTime = 1, Bounty = 2, BodyH = 4, BodyR = 1.5f, FleeDist = 12 * VoxelWorld.PerMetre,
    };

    public static readonly UnitType[] PlayerKinds = { Worker, Archer, Swordsman };
    /// <summary>The horde's kinds that find their way by the field (the catapult goes its own way to where it throws from).</summary>
    public static readonly UnitType[] HordeKinds = { Goblin, Orc, Miner, Troll, Sapper, Thief, Ram };
    public static readonly UnitType[] WildKinds = { Deer, Boar, Hare };
}

/// <summary>One pawn or attacker on the field. Positions are in voxels, feet on the ground.</summary>
public sealed class Unit
{
    public int Id;
    public UnitType T;
    /// <summary>Which of its kind's looks it has (<see cref="Bodies"/>).</summary>
    public int Look;
    public Team Team => T.Team;
    public float X, Y, Z, Facing;
    public int Cell;
    /// <summary>Where in a cell this one walks and stands, off the middle, so a file of them doesn't tread one track.</summary>
    public float OffX, OffZ;
    /// <summary>Its own tempo, a little over or under the kind's, for walking and working.</summary>
    public float Pace = 1;
    public UnitState State;
    public float Hp, MaxHp;
    public bool Dead;
    public double DiedAt;
    /// <summary>What killed it, for the chronicle: a unit's name, "стрела" or "падение".</summary>
    public string KilledBy = "";

    // movement
    public List<int> Path;
    public int PathI;
    /// <summary>The player sent it here and it stays until told to work again.</summary>
    public bool Hold;
    /// <summary>Where the player sent it; it keeps finding its way there if the way breaks.</summary>
    public int Dest = -1;
    /// <summary>Where a soldier goes when the horn sounds; -1 for none.</summary>
    public int Post = -1;

    // labour
    public int Site = -1;
    public readonly List<(int x, int y, int z, byte m)> Jobs = new();
    public int JobI;
    public float Effort;
    public double NextSearch;
    /// <summary>Why it is not working, for the panel: "нет камня", "не дойти".</summary>
    public string Why = "";
    public readonly Dictionary<int, double> Unreachable = new();

    // combat
    /// <summary>What the player told it to hunt or strike down; null when it chooses for itself.</summary>
    public Unit Prey;
    public Unit Target;
    public float Cooldown;
    public double LastHurt = -99;
    /// <summary>Who last drew its blood: a mortal wound or bleeding out is put down to them (the hunter is paid).</summary>
    public Unit HurtBy;
    public float Swing;

    // the horde: camped at a mine it keeps, or sent to take one of ours; the warband it marches with, and whether it leads it
    public bool Guard;
    public Mine Raid;
    public Warband Band;
    public bool Leader;
    /// <summary>Until when a raider that found no straight way to its place in the band goes by its own way instead.</summary>
    public double OwnWayUntil;
    /// <summary>A raider that has had enough — badly hurt, or its band broken — going back west, fighting only what catches it.</summary>
    public bool Routed;
    /// <summary>A raider back from a wave it got away from.</summary>
    public bool Returned;
    /// <summary>Its body once it has been hurt (what blows carved out of it, what still works); null while it is whole.</summary>
    public BodyState Wounds;
    /// <summary>One of ours badly hurt that has left the fight to mend by the citadel; back to its post once it has.</summary>
    public bool Mending;
    /// <summary>Whose wound it is binding, and who is binding its own; how far along it is.</summary>
    public Unit Patient, Bandager;
    public float BandageEffort;
    public double NextLookForWounded;

    // the horde hacking its way
    public int CarveCell = -1;
    public readonly List<(int x, int y, int z)> CarveList = new();
    public int CarveI;
    public float CarveEffort;
    /// <summary>A miner: the cell of our wall it last dug out the ground under (it does that once a wall, then hacks);
    /// while it is at it, the work the whole chamber takes, for the ground goes from under the wall all at once.</summary>
    public int Sapped = -1;
    public float SapCost;

    // the horde raising siege works
    public int BuildCell = -1;
    public Works Building;
    public readonly List<(int x, int y, int z, byte m)> BuildList = new();
    public int BuildI;
    public float BuildEffort;
    public double NoBuildUntil;

    // a soldier throwing down a siege ladder
    public int PushCell = -1;
    public (int x, int y, int z) PushVoxel;
    public float PushEffort;
    public double NextLook;

    /// <summary>Its priority for every kind of work (<see cref="WorkKind"/>): 1 first, 4 last, 0 never.</summary>
    public readonly byte[] Priority = { Work.Usual, Work.Usual, Work.Usual, Work.Usual, 1 };
    /// <summary>The kinds of work (bits by <see cref="WorkKind"/>) it went to its site for; it does only those there.</summary>
    public int SiteWork = (1 << Work.Kinds) - 1;
    /// <summary>A resource the player told it to bring in by itself (a store kind, or none), and the area it works for it.</summary>
    public int Duty;
    public Zone DutyZone;
    /// <summary>A worker called up: it stands and fights like a swordsman instead of running.</summary>
    public bool Militia;
    /// <summary>In the citadel, out of everyone's reach: sheltering from the horde, or mending.</summary>
    public bool Inside;
    /// <summary>The shelter it makes for: the citadel's door, or a place in a shelter area; −1 for none.</summary>
    public int Refuge = -1;

    // a thief, and those it takes
    /// <summary>What a thief carries off from the stores: the store (0 for gold, −1 for nothing), and how much of it.</summary>
    public int LootKind = -1;
    public double Loot;
    /// <summary>One of ours a thief leads off bound; for one of ours, the thief leading it off.</summary>
    public Unit Captive, Captor;
    /// <summary>The engine a raider guards, keeping by it; and for a catapult, where it throws at (packed voxel, −1 none).</summary>
    public Unit Escort;
    public int AimAt = -1;
    /// <summary>A siege tower at a wall: the voxels of the ladder it dropped against it (packed).</summary>
    public readonly List<int> Docked = new();

    // beasts, and pawns with nothing to do
    /// <summary>Where it grazes and wanders round; for a pawn, where it idles round.</summary>
    public int Home = -1;
    public Pastime Pastime;
    /// <summary>What it takes up when a stroll ends.</summary>
    public Pastime After;
    public double PastimeUntil;
    /// <summary>Who it is talking to.</summary>
    public Unit Mate;
    /// <summary>The way it looks while at it: at a fire, out over the wall.</summary>
    public float Gaze;

    // fire
    /// <summary>Seconds it will go on burning (its clothes alight, or standing in the flames), and how hot.</summary>
    public float Burn, BurnHeat = 1;
    /// <summary>A raider carrying a torch; when it may set the next thing alight, and how long it has been at it.</summary>
    public bool Torch;
    public double NextTorch;
    public float Kindling;
    /// <summary>An archer of ours shooting fire arrows; the voxel (packed) the player told it to set alight, or −1.</summary>
    public bool FireArrows;
    public int KindleAt = -1;
    /// <summary>A pawn putting out a fire: the cell of the fire it went to, and how far along the next beat is.</summary>
    public int FireCell = -1;
    public float FireBeat;
    public double NextFireLook;

    /// <summary>Walk cycle phase, for the legs.</summary>
    public float Stride;

    public float Alive => Dead ? 0 : Hp / MaxHp;

    /// <summary>"Орк-рубака", or "Вожак — орк-рубака" for the one who leads a warband.</summary>
    public string Name => Leader ? "Вожак — " + T.Name.ToLowerInvariant() : T.Name;
}
