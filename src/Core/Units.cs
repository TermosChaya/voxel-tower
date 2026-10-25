using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

public enum Team : byte { Player, Horde, Wild }

public enum UnitState : byte { Idle, Walk, Work, Fight, Carve, Siege, Flee, Dead }

/// <summary>What a pawn with nothing to do passes the time with: for its pose and for the panel.</summary>
public enum Pastime : byte { None, Stroll, Look, Sit, Talk, Stretch, Warm, Watch, Pray, Bound }

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
    /// <summary>A priest: it prays in a sanctuary by day and does no other work.</summary>
    public bool Prays;
    /// <summary>Ours: the lore that must be learnt before the citadel will take this kind on; none for those it always takes.</summary>
    public string Lore = "";
    /// <summary>Ours: how many of the household's places it takes — a man one, a horse and its rider two, a giant four.</summary>
    public int Room = 1;
    /// <summary>A rider on his horse: four legs under a man, a charge in the gallop, and no ladder for either of them.</summary>
    public bool Mounted;
    /// <summary>A beast of ours, a war hound: it bites and does nothing a hand does — no ladder thrown down, no fuse cut, no wound bound.</summary>
    public bool NoHands;
    /// <summary>An engine of ours: the timber it is built of, cubic metres from the stores besides the gold, and how many
    /// men work it (they come with it, and its places are theirs too).</summary>
    public float Logs;
    public int Crew;
    /// <summary>Metal it is armed or built of, cubic metres from the stores besides the gold (plate, iron fittings, an engine's bands).</summary>
    public float Metal;
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
    /// <summary>
    /// A commander of ours: a captain in a long cloak and a helm with a plume, a sword and a horn at the hip. It fights as a
    /// swordsman does and sees to the soldiers about it by itself (Sim.Captains.cs); the citadel takes two at most.
    /// </summary>
    public static readonly UnitType Captain = new()
    {
        Key = "captain", Name = "Командир", Team = Team.Player,
        Move = new Mover { Height = 2, Climb = 2, Speed = 2.5f },
        Work = 0, Yield = 0, Hp = 190, Damage = 18, AttackTime = 1f, Cost = 120, Tool = "sword", BodyR = 2.5f, Reach = 11,
    };
    /// <summary>A priest of the old powers: in a dark robe and hood, with a staff; it prays, and fights only for its life.</summary>
    public static readonly UnitType Priest = new()
    {
        Key = "priest", Name = "Жрец", Team = Team.Player,
        Move = new Mover { Height = 2, Climb = 2, Speed = 2.5f },
        Work = 1, Yield = 0, Hp = 50, Damage = 4, AttackTime = 1.3f, Cost = 50, Tool = "staff", Prays = true,
    };

    // the troops the citadel takes on once the household has learnt how to make them: men on foot with long arms and
    // shields, men who throw and shoot, beasts and riders, and the great ones; each waits on its own lore

    /// <summary>A spearman in a quilted coat and a kettle hat with a spear of two and a half metres: he strikes from where no sword reaches.</summary>
    public static readonly UnitType Spearman = new()
    {
        Key = "spear", Name = "Копейщик", Team = Team.Player,
        Move = new Mover { Height = 2, Climb = 2, Speed = 2.5f },
        Work = .4f, Yield = .6f, Hp = 100, Damage = 14, AttackTime = 1.1f, Reach = 15, Cost = 30, Lore = "spearmen", Tool = "spear",
    };
    /// <summary>A shieldbearer in mail behind a great tower shield, a mace in his other hand: slow to kill, and those beside him are covered too.</summary>
    public static readonly UnitType Shieldbearer = new()
    {
        Key = "shield", Name = "Щитоносец", Team = Team.Player,
        Move = new Mover { Height = 2, Climb = 2, Speed = 2.1f },
        Work = .4f, Yield = .6f, Hp = 190, Damage = 10, AttackTime = 1.2f, Reach = 11, BodyR = 2.5f, Cost = 40, Metal = .1f, Lore = "shieldmen", Tool = "mace",
    };
    /// <summary>A halberdier in mail and a red tabard under a sallet: the axe on the end of his pole comes down deep.</summary>
    public static readonly UnitType Halberdier = new()
    {
        Key = "halberd", Name = "Алебардщик", Team = Team.Player,
        Move = new Mover { Height = 2, Climb = 2, Speed = 2.3f },
        Work = .4f, Yield = .6f, Hp = 130, Damage = 24, AttackTime = 1.5f, Reach = 14, BodyR = 2.5f, Cost = 45, Metal = .05f, Lore = "halberds", Tool = "halberd",
    };
    /// <summary>A knight in full plate under a great helm, a long sword and a heater shield: what a blade gets through is little.</summary>
    public static readonly UnitType Knight = new()
    {
        Key = "knight", Name = "Рыцарь", Team = Team.Player,
        Move = new Mover { Height = 2, Climb = 2, Speed = 2f },
        Work = .4f, Yield = .6f, Hp = 240, Damage = 24, AttackTime = 1.1f, Reach = 12, BodyR = 2.5f, Cost = 90, Metal = .2f, Lore = "knights", Tool = "longsword",
    };
    /// <summary>A slinger, light and lean, a pouch of stones at his belt: cheap, quick, and his stones knock the wind out of a raider.</summary>
    public static readonly UnitType Slinger = new()
    {
        Key = "sling", Name = "Пращник", Team = Team.Player,
        Move = new Mover { Height = 2, Climb = 2, Speed = 2.8f },
        Work = .4f, Yield = .6f, Hp = 55, Damage = 10, AttackTime = 1f, Ranged = true, Range = 18 * VoxelWorld.PerMetre, Cost = 20, Lore = "slingers", Tool = "sling",
    };
    /// <summary>A crossbowman in a brigandine: slow to span his bow, but a bolt of his goes through mail and far.</summary>
    public static readonly UnitType Crossbowman = new()
    {
        Key = "crossbow", Name = "Арбалетчик", Team = Team.Player,
        Move = new Mover { Height = 2, Climb = 2, Speed = 2.4f },
        Work = .4f, Yield = .6f, Hp = 80, Damage = 34, AttackTime = 2.8f, Ranged = true, Range = 28 * VoxelWorld.PerMetre, Cost = 50, Metal = .05f, Lore = "crossbows", Tool = "crossbow",
    };
    /// <summary>A bombardier in a leather apron with pots of powder: one bursts where it comes down, among the raiders and their ladders.</summary>
    public static readonly UnitType Bombardier = new()
    {
        Key = "bombard", Name = "Бомбардир", Team = Team.Player,
        Move = new Mover { Height = 2, Climb = 2, Speed = 2.4f },
        // its damage is the pot's at the heart of the burst
        Work = .4f, Yield = .6f, Hp = 70, Damage = 40, AttackTime = 4f, Ranged = true, Range = 16 * VoxelWorld.PerMetre, Cost = 60, Lore = "bombards", Tool = "bombard",
    };
    /// <summary>
    /// An alchemist of ours in a long leather apron, gloves and goggles, a satchel of flasks at his hip: he keeps out of the
    /// press and lobs acid that eats the horde's ladders, decks and engines through (Sim.OurAlchemists.cs).
    /// </summary>
    public static readonly UnitType OurAlchemist = new()
    {
        Key = "ouralchemist", Name = "Алхимик", Team = Team.Player,
        Move = new Mover { Height = 2, Climb = 2, Speed = 2.4f },
        // its damage is a flask's on whoever it breaks on; its throws are AttackTime apart
        Work = .4f, Yield = .6f, Hp = 60, Damage = 16, AttackTime = 3.6f, Ranged = true, Range = 18 * VoxelWorld.PerMetre, Cost = 55, Lore = "alchemists", Tool = "flask",
    };
    /// <summary>A war hound, a great dog in a spiked collar: the swiftest of ours, it runs down whoever runs.</summary>
    public static readonly UnitType Hound = new()
    {
        Key = "hound", Name = "Боевой пёс", Team = Team.Player,
        Move = new Mover { Height = 1, Climb = 1, Drop = 3, Speed = 5.5f, Ladders = false },
        Work = 0, Yield = 0, Hp = 45, Damage = 9, AttackTime = .7f, Reach = 9, BodyH = 8, BodyR = 2, Cost = 15, Lore = "hounds", Tool = "bite", NoHands = true,
    };
    /// <summary>A rider with a sword on a horse a metre and a half at the withers: quick, and a blow from the gallop is a charge.</summary>
    public static readonly UnitType Rider = new()
    {
        Key = "rider", Name = "Всадник", Team = Team.Player,
        Move = new Mover { Height = 3, Climb = 1, Drop = 2, Speed = 5f, Ladders = false },
        Work = 0, Yield = 0, Hp = 160, Damage = 16, AttackTime = 1f, Reach = 13, BodyH = 21, BodyR = 3.5f, Cost = 70, Room = 2, Lore = "riders", Tool = "sword", Mounted = true,
    };
    /// <summary>A horse archer: he looses from the saddle and is gone before the horde comes up.</summary>
    public static readonly UnitType HorseArcher = new()
    {
        Key = "horsearcher", Name = "Конный лучник", Team = Team.Player,
        Move = new Mover { Height = 3, Climb = 1, Drop = 2, Speed = 5.2f, Ladders = false },
        Work = 0, Yield = 0, Hp = 120, Damage = 16, AttackTime = 1.4f, Ranged = true, Range = 20 * VoxelWorld.PerMetre, BodyH = 21, BodyR = 3.5f, Cost = 80, Room = 2,
        Lore = "horsearchers", Tool = "bow", Mounted = true,
    };
    /// <summary>A knight on a barded horse with a lance couched: every blow of his is a charge.</summary>
    public static readonly UnitType Lancer = new()
    {
        Key = "lancer", Name = "Конный рыцарь", Team = Team.Player,
        Move = new Mover { Height = 3, Climb = 1, Drop = 2, Speed = 4.2f, Ladders = false },
        Work = 0, Yield = 0, Hp = 280, Damage = 30, AttackTime = 1.3f, Reach = 15, BodyH = 22, BodyR = 3.5f, Cost = 140, Metal = .25f, Room = 3, Lore = "lancers", Tool = "lance", Mounted = true,
    };
    /// <summary>A troll tamed and bought from the hill folk, in our blue, an iron collar on it and a broken chain: it mends as trolls do.</summary>
    public static readonly UnitType TameTroll = new()
    {
        Key = "tametroll", Name = "Ручной тролль", Team = Team.Player,
        Move = new Mover { Height = 3, Climb = 2, Drop = 5, Speed = 1.7f },
        Work = .6f, Yield = .6f, Hp = 420, Damage = 30, AttackTime = 1.6f, Reach = 14, BodyH = 24, BodyR = 3.5f, Cost = 110, Room = 3, Lore = "trolls", Tool = "club",
    };
    /// <summary>A giant, a man of three metres in furs with a beard to his chest and a great maul: whoever it strikes flies.</summary>
    public static readonly UnitType Giant = new()
    {
        Key = "giant", Name = "Великан", Team = Team.Player,
        Move = new Mover { Height = 3, Climb = 2, Drop = 4, Speed = 1.8f },
        Work = .6f, Yield = .6f, Hp = 600, Damage = 40, AttackTime = 1.8f, Reach = 18, BodyH = 24, BodyR = 3.5f, Cost = 160, Room = 4, Lore = "giants", Tool = "maul",
    };

    // our engines (Sim.OurEngines.cs): timber on wheels like the horde's, slow and little hurt by arrows, each worked by a
    // crew of its own that comes out of the citadel with it; their places in the household are the crew's
    /// <summary>A catapult of ours, a mangonel: a throwing arm in a frame on wheels; stones in a high arc at the horde's engines and its bands.</summary>
    public static readonly UnitType Mangonel = new()
    {
        Key = "mangonel", Name = "Катапульта", Team = Team.Player,
        Move = new Mover { Height = 3, Climb = 1, Drop = 1, Speed = .8f, Ladders = false },
        Work = 0, Yield = 0, Hp = 300, Damage = 0, AttackTime = 7, Range = 46 * VoxelWorld.PerMetre, BodyH = 20, BodyR = 6,
        Cost = 110, Logs = 3, Metal = .3f, Crew = 2, Room = 3, Lore = "mangonels",
    };
    /// <summary>A ballista: a great crossbow on a wheeled stand; a bolt like a spear, straight, through a file of raiders, and into a giant.</summary>
    public static readonly UnitType Ballista = new()
    {
        Key = "ballista", Name = "Баллиста", Team = Team.Player,
        Move = new Mover { Height = 2, Climb = 1, Drop = 1, Speed = 1.1f, Ladders = false },
        Work = 0, Yield = 0, Hp = 220, Damage = 90, AttackTime = 4.5f, Range = 50 * VoxelWorld.PerMetre, BodyH = 12, BodyR = 5,
        Cost = 120, Logs = 2, Metal = .4f, Crew = 2, Room = 3, Lore = "ballistas",
    };
    /// <summary>A trebuchet: a counterweight on a long arm in a tall frame; heavy stones farther than anything of the horde's, and set up where it stands.</summary>
    public static readonly UnitType Trebuchet = new()
    {
        Key = "trebuchet", Name = "Требушет", Team = Team.Player,
        Move = new Mover { Height = 3, Climb = 1, Drop = 1, Speed = .55f, Ladders = false },
        Work = 0, Yield = 0, Hp = 480, Damage = 0, AttackTime = 12, Range = 90 * VoxelWorld.PerMetre, BodyH = 40, BodyR = 8,
        Cost = 220, Logs = 8, Metal = .8f, Crew = 3, Room = 5, Lore = "trebuchets",
    };
    /// <summary>One of an engine's crew, in a leather cap and apron: it winds the engine, loads it and pushes it; with its engine gone, a worker.</summary>
    public static readonly UnitType Crewman = new()
    {
        Key = "crew", Name = "Механик", Team = Team.Player,
        Move = new Mover { Height = 2, Climb = 2, Speed = 2.6f },
        Work = 1, Yield = 1, Hp = 65, Damage = 6, AttackTime = 1.1f, Room = 0, Tool = "pick",
    };
    /// <summary>Our engines, in the citadel's order; and their crews' kind.</summary>
    public static readonly UnitType[] OurEngines = { Mangonel, Ballista, Trebuchet };
    public static readonly UnitType[] Crews = { Crewman };
    public static bool IsOurEngine(UnitType t) => t == Mangonel || t == Ballista || t == Trebuchet;

    /// <summary>The troops the lore opens, in the order the citadel offers them.</summary>
    public static readonly UnitType[] Troops = { Spearman, Shieldbearer, Halberdier, Knight, Slinger, Crossbowman, Bombardier, OurAlchemist, Hound, Rider, HorseArcher, Lancer, TameTroll, Giant, Mangonel, Ballista, Trebuchet };

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
    /// <summary>
    /// A hill giant come down with the horde: three and a half metres of it in hides, bald and heavy-browed, a tree for a
    /// club. It tears boulders out of the ground and hurls them at our walls and at knots of our people, smashes through
    /// what it comes up against a heap at a blow, swats whoever stands before it into the air, and wades a ditch of water
    /// up to its chest. Arrows hardly go through its hide.
    /// </summary>
    public static readonly UnitType HillGiant = new()
    {
        Key = "hillgiant", Name = "Горный великан", Team = Team.Horde,
        Move = new Mover { Height = 3, Climb = 2, Drop = 5, Speed = 1.7f, Carve = new float[] { 160, 220, 110, 16 }, Build = 0, Wade = 2 },
        Hp = 950, Damage = 44, AttackTime = 2.1f, KeepDamage = 16, Bounty = 60, BodyH = 28, BodyR = 4, Reach = 19, Tool = "club",
        Range = 30 * VoxelWorld.PerMetre,
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
    public static bool IsEngine(UnitType t) => t == Ram || t == Catapult || t == Mantlet || t == SiegeTower || IsOurEngine(t);

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

    public static readonly UnitType[] PlayerKinds =
    {
        Worker, Archer, Swordsman, Priest, Captain, Spearman, Shieldbearer, Halberdier, Knight, Slinger, Crossbowman, Bombardier, OurAlchemist, Hound, Rider, HorseArcher, Lancer,
        TameTroll, Giant, Mangonel, Ballista, Trebuchet,
    };
    /// <summary>The horde's kinds that find their way by the field (the catapult goes its own way to where it throws from).</summary>
    public static readonly UnitType[] HordeKinds = { Goblin, Orc, Miner, Troll, Sapper, Thief, Ram, HillGiant };
    /// <summary>A white stag, seen in the valley once in a long while: the swiftest thing in it, and worth a deal of gold to whoever runs it down.</summary>
    public static readonly UnitType WhiteStag = new()
    {
        Key = "stag", Name = "Белый олень", Team = Team.Wild,
        Move = new Mover { Height = 2, Climb = 1, Drop = 3, Speed = 5.2f },
        Hp = 70, Damage = 0, AttackTime = 1, Bounty = 60, BodyH = 15, BodyR = 3, FleeDist = 26 * VoxelWorld.PerMetre,
    };
    /// <summary>A cave bear out of the hills: it runs from nobody and goes for whoever comes near it, ours or the horde's.</summary>
    public static readonly UnitType Bear = new()
    {
        Key = "bear", Name = "Пещерный медведь", Team = Team.Wild,
        Move = new Mover { Height = 2, Climb = 2, Drop = 3, Speed = 3.4f },
        // a long reach: a bear is a big beast, and it strikes from its bulk, not from a man's arm
        Hp = 380, Damage = 24, AttackTime = 1.4f, Bounty = 80, BodyH = 15, BodyR = 4.5f, Reach = 17, FleeDist = 0,
    };

    public static readonly UnitType[] WildKinds = { Deer, Boar, Hare, WhiteStag, Bear };

    /// <summary>The dead raised by a spell: bones and a rusted blade, fighting for us a minute, then falling apart.</summary>
    public static readonly UnitType Risen = new()
    {
        Key = "risen", Name = "Мертвец", Team = Team.Player,
        Move = new Mover { Height = 2, Climb = 1, Drop = 5, Speed = 2.3f },
        Work = 0, Yield = 0, Hp = 60, Damage = 11, AttackTime = 1.1f, Reach = 10, Tool = "sword",
    };
    /// <summary>A bone thrall: one of the horde's fallen got up again by a necromancer; it fights for the horde a minute, then falls apart.</summary>
    public static readonly UnitType Thrall = new()
    {
        Key = "thrall", Name = "Костяк", Team = Team.Horde,
        Move = new Mover { Height = 2, Climb = 1, Drop = 5, Speed = 2.4f, Carve = new float[] { 10, 22, 3, 1 } },
        Hp = 45, Damage = 9, AttackTime = 1.1f, KeepDamage = 1, Bounty = 0, BodyH = 15, BodyR = 2, Reach = 10, Tool = "sword",
    };
    /// <summary>What the spells raise, ours or the horde's: not hired, not the household's, gone in a minute.</summary>
    public static readonly UnitType[] Summoned = { Risen, Thrall };

    /// <summary>
    /// A goblin shaman: a skull for a face and an ember in the horn of its staff; it mends the band it goes with and lays
    /// curses on our walls. It goes the goblins' way.
    /// </summary>
    public static readonly UnitType Shaman = new()
    {
        Key = "shaman", Name = "Шаман гоблинов", Team = Team.Horde,
        Move = new Mover { Height = 2, Climb = 1, Drop = 5, Speed = 2.8f, Carve = new float[] { 10, 22, 3, 1 }, Build = .3f },
        Hp = 60, Damage = 5, AttackTime = 1f, KeepDamage = 1, Bounty = 12, BodyH = 10, BodyR = 1.5f, Reach = 9, Tool = "staff",
    };
    /// <summary>
    /// A witch of the horde: an old orc woman bent under a shawl, grey hair loose, a staff hung with herbs and bones. She
    /// puts out the torches on our walls about her, and the fires that burn the horde. She goes the goblins' way.
    /// </summary>
    public static readonly UnitType Witch = new()
    {
        Key = "witch", Name = "Ворожея", Team = Team.Horde,
        Move = new Mover { Height = 2, Climb = 1, Drop = 5, Speed = 2.5f, Carve = new float[] { 10, 22, 3, 1 }, Build = .3f },
        Hp = 70, Damage = 5, AttackTime = 1.1f, KeepDamage = 1, Bounty = 14, BodyH = 13, BodyR = 2, Reach = 9, Tool = "staff",
    };
    /// <summary>
    /// A necromancer of the horde: a tall orc in a black hooded robe, a skull on its staff with pale fire in its eyes. It
    /// gets the horde's fallen up again as bone thralls, and near a sanctuary of ours the intervention drains away while it
    /// stands there. It goes the orcs' way.
    /// </summary>
    public static readonly UnitType Necromancer = new()
    {
        Key = "necro", Name = "Некромант", Team = Team.Horde,
        Move = new Mover { Height = 2, Speed = 2f, Carve = new float[] { 5, 120, 14, 2 }, Build = .25f },
        Hp = 120, Damage = 8, AttackTime = 1.3f, KeepDamage = 2, Bounty = 20, BodyH = 16, BodyR = 2.5f, Reach = 10, Tool = "staff",
    };
    /// <summary>The horde's sorcerers.</summary>
    public static readonly UnitType[] Casters = { Shaman, Witch, Necromancer };
    public static bool IsCaster(UnitType t) => t == Shaman || t == Witch || t == Necromancer;

    /// <summary>
    /// An alchemist of the horde: a goblin in a stained robe and a leather apron, goggles of green glass, flasks of acid
    /// on a bandolier. It stops out of reach and throws them at our walls, gates and ladders, or into a knot of our
    /// people (Sim.Alchemists.cs); its flasks spent, it fights with its knife like any goblin. It goes the goblins' way.
    /// </summary>
    public static readonly UnitType Alchemist = new()
    {
        Key = "alchemist", Name = "Алхимик", Team = Team.Horde,
        Move = new Mover { Height = 2, Climb = 1, Drop = 5, Speed = 2.7f, Carve = new float[] { 10, 22, 3, 1 }, Build = .3f },
        // its damage is a flask's on whoever it breaks on; its throws are AttackTime apart
        Hp = 50, Damage = 14, AttackTime = 4.5f, KeepDamage = 1, Bounty = 12, BodyH = 10, BodyR = 1.5f, Reach = 9,
        Range = 18 * VoxelWorld.PerMetre, Tool = "flask",
    };
    /// <summary>The horde's throwers of what is not a weapon of war: the alchemists.</summary>
    public static readonly UnitType[] Throwers = { Alchemist };
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
    /// <summary>The player sent it here and it stays until told to work again (a worker only on its way, see Back).</summary>
    public bool Hold;
    /// <summary>A worker sent somewhere: there it goes back to work by itself, the area it was sent to first (Focus).</summary>
    public bool Back;
    /// <summary>The area set aside a worker was sent into: it takes its work from there first, till it is spent.</summary>
    public Zone Focus;
    /// <summary>When a worker next looks whether it is shut in, and when it last marked a way out (Sim.Escape.cs).</summary>
    public double NextWayOut, WayOutMarked = -999, ShutSince = -1;
    /// <summary>The scaffolding it is raising, or taking down (Sim.Scaffold.cs); when it next looks to climb down by some.</summary>
    public Scaffold Raising;
    public double NextGetDown;
    /// <summary>Where the player sent it; it keeps finding its way there if the way breaks.</summary>
    public int Dest = -1;
    /// <summary>Where a soldier goes when the horn sounds; -1 for none.</summary>
    public int Post = -1;

    // labour
    public int Site = -1;
    public readonly List<(int x, int y, int z, byte m)> Jobs = new();
    public int JobI;
    public float Effort;
    /// <summary>How far through the blow it is working at, 0 to 1: the tool comes down at 1 (the view draws the arm by it).</summary>
    public float Beat;
    /// <summary>
    /// What its wounds do to it (Sim.Pain.cs): the pain of them (0 none, 1 hardly to be borne); reeling from a hard blow
    /// till then, knocked back so fast; dazed by a blow to the head till then; the hand to the wound — the part (9 the
    /// head) and till when; off its feet since and till when (infinite: till the pain eases).
    /// </summary>
    public float Pain, StagX, StagZ;
    public double StaggerUntil, DazedUntil, ClutchUntil, DownSince, DownUntil;
    public byte Clutch;
    public bool Down;
    /// <summary>Where the last blow struck it (for the pain of it): the part, and whether the head.</summary>
    public BodyPart HitPart;
    public bool HitHead;
    /// <summary>How it got out of the way of the last blow at it, or took it on its shield or blade, and when; to which side it leaned.</summary>
    public Guarded React;
    public double ReactAt = -99;
    public sbyte ReactSide;
    /// <summary>When it last fought hand to hand and with whom (its own come to help a fight that steps in and out of reach).</summary>
    public double FoughtAt = -99;
    public Unit FoughtWith;
    /// <summary>Circling the one it fights: which way round (−1, 0, 1), till it changes its mind.</summary>
    public float Circle;
    public double CircleNext;
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
    // the blow it is at and the one it means next; none of it goes into a save, a blow is over in a second
    /// <summary>The stroke it struck last, and when (simulation seconds); the one it is drawing back for, and at whom.</summary>
    public StrokeKind Stroke, NextStroke;
    public double StrokeTime = -99;
    public Unit StrokeFor;
    /// <summary>Blows it has struck, which picks its next stroke without touching the random stream.</summary>
    public int Strokes;
    /// <summary>How the last stroke was laid out: tipped down (radians), turned in, the step into it (voxels), and how far along its sweep (0…1) the blade met the body.</summary>
    public float StrokeAim, StrokeYaw, StrokeLunge, StrokeAt;
    /// <summary>The last blow was turned by armour or a shield and did not go through: the blade comes back off it.</summary>
    public bool Glanced;

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
    /// <summary>
    /// What blunt things did to it (Sim.Trauma.cs): when it was last struck by one; pinned under what came down on it —
    /// how heavy it lies (kg, 0 for free), since when, the comrade digging it out and when that one was last at it; for
    /// that comrade, whom it digs out, how far along the next voxel is, and when it next looks for one pinned.
    /// </summary>
    public double StruckAt = -99, PinnedSince, DugAt;
    public float PinKg, FreeEffort;
    /// <summary>Gone under deep water weighed down by its armour, since when (-1 not under): it drowns (Sim.Flood.cs).</summary>
    public double UnderSince = -1;
    public Unit Digger, Freeing;
    public double NextLookForPinned;
    /// <summary>Whose wound it is binding, and who is binding its own; how far along it is.</summary>
    public Unit Patient, Bandager;
    public float BandageEffort;
    public double NextLookForWounded;
    /// <summary>
    /// The wounded carried and helped along (Sim.Carry.cs): whom it carries (or goes to lift) and who carries it, whether
    /// it is up on the shoulder yet, how far the lifting has got, where it is being taken; whom it helps along on a broken
    /// leg and who helps it; when it next looks for one to carry or help. For one pinned, when it last called out and
    /// when it calls again; for one sent by the player to dig a comrade out, that it was sent (it digs beside another).
    /// </summary>
    public Unit Carrying, CarriedBy, Propping, PropBy;
    public bool Aloft, FreeOrdered;
    public float LiftEffort;
    public int CarryTo = -1;
    public double NextLookForFallen, CalledAt = -99, NextCall;

    // the horde hacking its way
    public int CarveCell = -1;
    public readonly List<(int x, int y, int z)> CarveList = new();
    public int CarveI;
    public float CarveEffort;
    /// <summary>A miner: the cell of our wall it last dug out the ground under (it does that once a wall, then hacks);
    /// while it is at it, the work the whole chamber takes, for the ground goes from under the wall all at once.</summary>
    public int Sapped = -1;
    public float SapCost;
    /// <summary>A miner: the mine it is digging or following the digger of (Sim.Tunnels.cs), and when it next looks along
    /// its way for a wall to go under; one of ours: when it may next knock out a frame of the horde's props.</summary>
    public Tunnel Bore;
    public double NextBoreLook;

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
    /// <summary>A priest: the place in a sanctuary it prays at; −1 for none.</summary>
    public int Shrine = -1;

    // a thief, and those it takes
    /// <summary>What a thief carries off from the stores: the store (0 for gold, −1 for nothing), and how much of it.</summary>
    public int LootKind = -1;
    public double Loot;
    /// <summary>One of ours a thief leads off bound; for one of ours, the thief leading it off.</summary>
    public Unit Captive, Captor;
    /// <summary>The engine a raider guards, keeping by it; and for a catapult, where it throws at (packed voxel, −1 none).</summary>
    public Unit Escort;
    public int AimAt = -1;
    /// <summary>An alchemist: the flasks it has thrown of those it came with.</summary>
    public int Thrown;
    /// <summary>When it last coughed in the fumes of acid and stepped back out of them.</summary>
    public double CoughAt = -99;
    /// <summary>A siege tower at a wall: the voxels of the ladder it dropped against it (packed).</summary>
    public readonly List<int> Docked = new();
    /// <summary>An envoy of the horde, under a white rag, come to bargain: no one of the horde strikes with it.</summary>
    public bool Envoy;

    // captives of ours, and what is given to the old powers
    /// <summary>One of ours told to take raiders alive: the blow that would bring one down to a quarter of itself fells and binds it instead.</summary>
    public bool TakeAlive;
    /// <summary>A raider struck down and bound — lying where it fell, led in, or a prisoner in the citadel — and since when.</summary>
    public bool Bound;
    public double BoundAt;
    /// <summary>A prisoner: seconds of the night it has been left unguarded.</summary>
    public double Unguarded;
    /// <summary>A worker going out for a bound raider; when it next looks for one.</summary>
    public Unit Fetching;
    public double NextFetchLook;
    /// <summary>A priest's sacrifice under way, or null.</summary>
    public Rite Rite;
    /// <summary>Till when it is held fast in the chains of a spell.</summary>
    public double ChainedUntil = -1;
    /// <summary>One of the risen: when it falls apart.</summary>
    public double CrumbleAt;
    /// <summary>A sorcerer of the horde: when it next casts each of its two spells — a shaman mends and curses, a witch
    /// douses fires and puts out torches, a necromancer raises the fallen.</summary>
    public double NextHeal, NextCurse;
    /// <summary>Off its feet and in the air — thrown by a blast or a wind, or held up by telekinesis — or null.</summary>
    public Flier Airborne;
    /// <summary>A raider: till when the plague is on it. One of ours: till when the blood oath holds.</summary>
    public double PlagueUntil = -1, OathUntil = -1;
    /// <summary>A sapper of the horde with a charge of powder on its back, to lay against a wall of ours.</summary>
    public bool Charge;
    /// <summary>One of ours running to cut the fuse of a sapper's charge; one keeping clear of a charge nobody can get to in time.</summary>
    public Charge Defusing, Shunning;
    /// <summary>
    /// One of ours keeping out of a place about to be struck — the lane a tree goes down along, what a dig will bring down
    /// (Sim.Felling.cs, Sim.Digging.cs); and, for a feller, the voxel it last weighed and till when that holds.
    /// </summary>
    public Danger Avoiding;
    public int FellLooked = -1;
    public double FellLookedUntil;
    public bool FellHold;

    // beasts, and pawns with nothing to do
    /// <summary>Where it grazes and wanders round; for a pawn, where it idles round.</summary>
    public int Home = -1;
    public Pastime Pastime;
    /// <summary>The seat it went to sit on (Sim.Yard.cs), and once it sits there, how high over the floor.</summary>
    public Seat SeatAt;
    public float SeatH;
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

    // a troop of ours at its trade; none of it goes into a save, it is all had again in a moment
    /// <summary>Where it stood a tick ago, and till when it is still at the gallop: a rider's blow then is a charge.</summary>
    public float WasX = float.NaN, WasZ;
    public double GallopUntil = -1;
    /// <summary>A giant or a tame troll: when it may next tear a stone out of the ground and hurl it.</summary>
    public double NextHurl;
    /// <summary>
    /// A great one at a throw (Sim.Giants.cs): the stone in its hands, or null once it is let go; when the throw began;
    /// where the stone was torn from; where it is aimed, and at whom (null for a wall).
    /// </summary>
    public Flier Hefted;
    public double HurlFrom = -99;
    public float HurlSX, HurlSY, HurlSZ, HurlTX, HurlTY, HurlTZ;
    public Unit HurlAt;
    /// <summary>
    /// An engine of ours (Sim.OurEngines.cs): how far it is set up where it stands, 0 packed to roll to 1 ready to
    /// throw (−1 not yet known: read back from a save, it is set up if it stands where it was sent).
    /// </summary>
    public float Rig = -1;

    /// <summary>
    /// A commander of ours (Sim.Captains.cs): whether it sees to those about it by itself; the spot the player gave it to
    /// hold (-1 none); when it next looks over the field; when each of its four calls is ready again.
    /// </summary>
    public bool Commands = true;
    public int Goal = -1;
    public double NextOrders;
    public readonly double[] Ready = new double[4];
    /// <summary>
    /// One of ours under a commander's word: the commander who set it where it stands (null: the player, or nobody); sent
    /// back by it to mend; following it till then; standing in its shield wall till then; heartened by it till then.
    /// </summary>
    public Unit Ward, Follows;
    public bool SentBack;
    public double FollowUntil, WallUntil, RallyUntil;

    public float Alive => Dead ? 0 : Hp / MaxHp;

    /// <summary>"Орк-рубака", or "Вожак — орк-рубака" for the one who leads a warband.</summary>
    public string Name => Champion == 4 ? $"{Title}, вождь орды" : Champion > 0 ? $"{Title}, вожак главы {Sim.Roman(Champion)} ({T.Name.ToLowerInvariant()})"
        : Leader ? "Вожак — " + T.Name.ToLowerInvariant() : T.Name;
    /// <summary>A chapter's champion (1–3) or the horde's chief (4), and his name; 0 for the rest.</summary>
    public byte Champion;
    public string Title;
}
