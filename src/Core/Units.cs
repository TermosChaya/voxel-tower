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
        Hp = 40, Damage = 5, AttackTime = .9f, KeepDamage = 1, Bounty = 3, BodyH = 10, BodyR = 1.5f, Reach = 9, Tool = "knife",
    };
    public static readonly UnitType Orc = new()
    {
        Key = "orc", Name = "Орк-рубака", Team = Team.Horde,
        Move = new Mover { Height = 2, Speed = 2f, Carve = new float[] { 5, 120, 14, 2 }, Build = .25f },
        Hp = 110, Damage = 14, AttackTime = 1.2f, KeepDamage = 3, Bounty = 5, BodyH = 16, BodyR = 2.5f, Reach = 11, Tool = "axe",
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
    public static readonly UnitType[] HordeKinds = { Goblin, Orc, Miner, Troll, Sapper };
    public static readonly UnitType[] WildKinds = { Deer, Boar, Hare };
}

/// <summary>One pawn or attacker on the field. Positions are in voxels, feet on the ground.</summary>
public sealed class Unit
{
    public int Id;
    public UnitType T;
    public Team Team => T.Team;
    public float X, Y, Z, Facing;
    public int Cell;
    /// <summary>Where in a cell this one walks and stands, off the middle, so a file of them doesn't tread one track.</summary>
    public float OffX, OffZ;
    /// <summary>Its own tempo, a little over or under the kind's, for walking and working.</summary>
    public float Pace = 1;
    public UnitState State;
    public float Hp;
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
    public float Swing;

    // the horde hacking its way
    public int CarveCell = -1;
    public readonly List<(int x, int y, int z)> CarveList = new();
    public int CarveI;
    public float CarveEffort;

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

    /// <summary>Walk cycle phase, for the legs.</summary>
    public float Stride;

    public float Alive => Dead ? 0 : Hp / T.Hp;
}
