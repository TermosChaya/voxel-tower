using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

public enum Team : byte { Player, Horde, Wild }

public enum UnitState : byte { Idle, Walk, Work, Fight, Carve, Siege, Flee, Dead }

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
    /// <summary>Melee reach and bow range, in voxels.</summary>
    public float Reach = 14, Range;
    public bool Ranged;
    /// <summary>Damage to the citadel per blow.</summary>
    public float KeepDamage;
    public int Cost, Bounty;
    /// <summary>Body size in voxels, for the model and for being hit.</summary>
    public float BodyH = 29, BodyR = 4;
    public string Tool = "";
}

public static class Kinds
{
    public static readonly UnitType Worker = new()
    {
        Key = "worker", Name = "Рабочий", Team = Team.Player,
        // pawns scramble up a ledge as high as their chest: out of a metre-deep pit, onto a low wall
        Move = new Mover { Height = 4, Climb = 2, Speed = 3.2f },
        Work = 1, Yield = 1, Hp = 60, Damage = 6, AttackTime = 1.1f, Cost = 25,
        Tool = "pick",
    };
    public static readonly UnitType Archer = new()
    {
        Key = "archer", Name = "Лучник", Team = Team.Player,
        Move = new Mover { Height = 4, Climb = 2, Speed = 3.2f },
        Work = .4f, Yield = .6f, Hp = 70, Damage = 18, AttackTime = 1.3f, Ranged = true, Range = 12 * 16, Cost = 40, Tool = "bow",
    };
    public static readonly UnitType Swordsman = new()
    {
        Key = "sword", Name = "Мечник", Team = Team.Player,
        Move = new Mover { Height = 4, Climb = 2, Speed = 2.9f },
        Work = .4f, Yield = .6f, Hp = 140, Damage = 16, AttackTime = 1f, Cost = 35, Tool = "sword",
    };
    public static readonly UnitType Goblin = new()
    {
        Key = "goblin", Name = "Гоблин", Team = Team.Horde,
        Move = new Mover { Height = 3, Climb = 1, Drop = 5, Speed = 4.2f, Carve = new float[] { 10, 22, 3, 1 }, Build = .35f },
        Hp = 40, Damage = 5, AttackTime = .9f, KeepDamage = 1, Bounty = 3, BodyH = 22, BodyR = 3, Tool = "knife",
    };
    public static readonly UnitType Orc = new()
    {
        Key = "orc", Name = "Орк-рубака", Team = Team.Horde,
        Move = new Mover { Height = 4, Speed = 2.8f, Carve = new float[] { 5, 120, 14, 2 }, Build = .25f },
        Hp = 110, Damage = 14, AttackTime = 1.2f, KeepDamage = 3, Bounty = 5, BodyH = 31, BodyR = 4.5f, Tool = "axe",
    };
    public static readonly UnitType Miner = new()
    {
        Key = "miner", Name = "Рудокоп", Team = Team.Horde,
        Move = new Mover { Height = 3, Climb = 1, Drop = 5, Speed = 3f, Carve = new float[] { 140, 35, 55, 24 }, Build = .5f },
        Hp = 70, Damage = 9, AttackTime = 1.1f, KeepDamage = 2, Bounty = 5, BodyH = 23, BodyR = 3.4f, Tool = "pick",
    };
    public static readonly UnitType Troll = new()
    {
        Key = "troll", Name = "Тролль", Team = Team.Horde,
        Move = new Mover { Height = 5, Climb = 2, Drop = 5, Speed = 1.9f, Carve = new float[] { 60, 140, 90, 14 }, Build = .15f },
        Hp = 420, Damage = 30, AttackTime = 1.6f, KeepDamage = 9, Bounty = 18, BodyH = 48, BodyR = 7, Reach = 20, Tool = "club",
    };
    /// <summary>The horde's own worker: raises ladders, lays bridges, fills ditches; poor in a fight.</summary>
    public static readonly UnitType Sapper = new()
    {
        Key = "sapper", Name = "Гоблин-сапёр", Team = Team.Horde,
        Move = new Mover { Height = 3, Climb = 1, Drop = 5, Speed = 3.8f, Carve = new float[] { 14, 26, 4, 1 }, Build = 1 },
        Hp = 45, Damage = 4, AttackTime = 1.1f, KeepDamage = 1, Bounty = 4, BodyH = 22, BodyR = 3, Tool = "mallet",
    };

    // the valley's beasts: they graze and wander, bolt from anyone who comes near, and pay for the hunt
    public static readonly UnitType Deer = new()
    {
        Key = "deer", Name = "Олень", Team = Team.Wild,
        Move = new Mover { Height = 3, Climb = 1, Drop = 3, Speed = 5.4f },
        Hp = 50, Damage = 0, AttackTime = 1, Bounty = 6, BodyH = 26, BodyR = 6, FleeDist = 10 * 16,
    };
    public static readonly UnitType Boar = new()
    {
        Key = "boar", Name = "Кабан", Team = Team.Wild,
        Move = new Mover { Height = 2, Climb = 1, Drop = 3, Speed = 4.4f },
        Hp = 90, Damage = 9, AttackTime = 1.1f, Bounty = 9, BodyH = 14, BodyR = 6, Reach = 12, FleeDist = 5 * 16,
    };
    public static readonly UnitType Hare = new()
    {
        Key = "hare", Name = "Заяц", Team = Team.Wild,
        Move = new Mover { Height = 1, Climb = 1, Drop = 3, Speed = 6.5f },
        Hp = 10, Damage = 0, AttackTime = 1, Bounty = 2, BodyH = 9, BodyR = 3, FleeDist = 6 * 16,
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

    // beasts
    /// <summary>Where it grazes and wanders round.</summary>
    public int Home = -1;

    /// <summary>Walk cycle phase, for the legs.</summary>
    public float Stride;

    public float Alive => Dead ? 0 : Hp / T.Hp;
}
