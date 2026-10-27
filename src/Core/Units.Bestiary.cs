using System;

namespace VoxelTower.Core;

/// <summary>
/// The neutral creatures of the world (Bestiary.cs): what wanders the big map in stacks as the monsters of «Heroes» do
/// and keeps its mines, treasures and passes, a third side in a valley, going for ours and the horde's alike. The wolf,
/// the bear and the skeleton are the old ones (Units.cs); these are the rest, tier by tier.
/// </summary>
public static partial class Kinds
{
    const float M = VoxelWorld.PerMetre;

    // ---------------------------------------------------------------- the first tier

    /// <summary>A brigand of the roads: a man gone to the woods, a cudgel and a hood; he can be talked over to a stronger side.</summary>
    public static readonly UnitType Bandit = new()
    {
        Key = "bandit", Name = "Разбойник", Team = Team.Wild,
        Move = new Mover { Height = 2, Climb = 2, Drop = 4, Speed = 2.7f },
        Hp = 75, Damage = 10, AttackTime = 1.05f, Bounty = 4, BodyH = 15, BodyR = 2, Reach = 11, Tool = "club", Aggro = 12 * M,
    };
    /// <summary>A giant spider of the dark woods and the marshes: as big as a calf, a bite with venom in it, and webs it throws.</summary>
    public static readonly UnitType Spider = new()
    {
        Key = "spider", Name = "Гигантский паук", Team = Team.Wild,
        Move = new Mover { Height = 1, Climb = 3, Drop = 5, Speed = 3.4f, Ladders = false },
        Hp = 60, Damage = 9, AttackTime = .9f, Bounty = 5, BodyH = 7, BodyR = 3, Reach = 10, Tool = "bite", Range = 12 * M, Aggro = 13 * M,
    };

    // ---------------------------------------------------------------- the second

    /// <summary>A warg: a wolf the size of a pony, black-grey, hunting in packs in the cold and the burnt lands.</summary>
    public static readonly UnitType Warg = new()
    {
        Key = "warg", Name = "Варг", Team = Team.Wild,
        Move = new Mover { Height = 2, Climb = 1, Drop = 3, Speed = 5f, Ladders = false },
        Hp = 140, Damage = 17, AttackTime = .9f, Bounty = 10, BodyH = 11, BodyR = 3, Reach = 11, Tool = "bite", Aggro = 16 * M,
    };
    /// <summary>A harpy of the crags: a hag's body on a bird's wings and talons, hovering and swooping on whoever is below.</summary>
    public static readonly UnitType Harpy = new()
    {
        Key = "harpy", Name = "Гарпия", Team = Team.Wild,
        Move = new Mover { Height = 2, Climb = 4, Drop = 8, Speed = 4.4f, Ladders = false },
        Hp = 85, Damage = 13, AttackTime = .8f, Bounty = 6, BodyH = 13, BodyR = 2, Reach = 11, Tool = "bite", Aggro = 15 * M, Hover = 16,
    };
    /// <summary>A cultist of the old fire: a red-robed man throwing balls of burning pitch from his hands.</summary>
    public static readonly UnitType Cultist = new()
    {
        Key = "cultist", Name = "Культист", Team = Team.Wild,
        Move = new Mover { Height = 2, Climb = 2, Drop = 4, Speed = 2.4f },
        Hp = 75, Damage = 16, AttackTime = 2.2f, Bounty = 8, BodyH = 15, BodyR = 2, Reach = 10, Tool = "knife", Ranged = true, Range = 20 * M, Aggro = 18 * M,
    };

    // ---------------------------------------------------------------- the third

    /// <summary>A centaur of the plains: a man to the waist on a horse's body, a bow; a proud kind, it may go with the stronger.</summary>
    public static readonly UnitType Centaur = new()
    {
        Key = "centaur", Name = "Кентавр", Team = Team.Wild,
        Move = new Mover { Height = 2, Climb = 1, Drop = 3, Speed = 4.4f, Ladders = false },
        Hp = 160, Damage = 18, AttackTime = 1.2f, Bounty = 12, BodyH = 18, BodyR = 3, Reach = 11, Tool = "bow", Ranged = true, Range = 26 * M, Aggro = 22 * M,
    };
    /// <summary>A ghoul of the marsh graves and the ash: grey, gaunt, long-clawed, its touch rotting the wound.</summary>
    public static readonly UnitType Ghoul = new()
    {
        Key = "ghoul", Name = "Упырь", Team = Team.Wild,
        Move = new Mover { Height = 2, Climb = 2, Drop = 6, Speed = 2.6f },
        Hp = 210, Damage = 18, AttackTime = 1.2f, Bounty = 0, BodyH = 15, BodyR = 2, Reach = 11, Tool = "bite", Aggro = 12 * M,
    };

    // ---------------------------------------------------------------- the fourth

    /// <summary>An ogre: three metres of fat and muscle, a tree for a club that knocks men off their feet.</summary>
    public static readonly UnitType Ogre = new()
    {
        Key = "ogre", Name = "Огр", Team = Team.Wild,
        Move = new Mover { Height = 3, Climb = 2, Drop = 5, Speed = 1.9f },
        Hp = 440, Damage = 34, AttackTime = 1.7f, Bounty = 20, BodyH = 24, BodyR = 3.5f, Reach = 16, Tool = "club", Aggro = 14 * M,
    };
    /// <summary>A troll of the woods: the horde's trolls' wild kin, mossed over, its wounds closing as you watch — but not under fire.</summary>
    public static readonly UnitType WildTroll = new()
    {
        Key = "wildtroll", Name = "Лесной тролль", Team = Team.Wild,
        Move = new Mover { Height = 3, Climb = 2, Drop = 5, Speed = 1.8f },
        Hp = 520, Damage = 32, AttackTime = 1.6f, Bounty = 20, BodyH = 24, BodyR = 3.5f, Reach = 14, Tool = "club", Aggro = 13 * M, Regen = 6,
    };
    /// <summary>A wraith: a shroud with a cold fire in its hood, drifting a hand over the ground, drinking the life it strikes.</summary>
    public static readonly UnitType Wraith = new()
    {
        Key = "wraith", Name = "Призрак", Team = Team.Wild,
        Move = new Mover { Height = 2, Climb = 3, Drop = 8, Speed = 2.8f, Ladders = false },
        Hp = 260, Damage = 26, AttackTime = 1f, Bounty = 0, BodyH = 16, BodyR = 2, Reach = 11, Tool = "bite", Aggro = 14 * M, Hover = 4,
    };

    // ---------------------------------------------------------------- the fifth

    /// <summary>A minotaur of the stony wastes: a bull's head on a giant's body, a double axe, and a charge with the horns down.</summary>
    public static readonly UnitType Minotaur = new()
    {
        Key = "minotaur", Name = "Минотавр", Team = Team.Wild,
        Move = new Mover { Height = 3, Climb = 2, Drop = 5, Speed = 2.6f },
        Hp = 700, Damage = 48, AttackTime = 1.5f, Bounty = 30, BodyH = 26, BodyR = 3.5f, Reach = 16, Tool = "axe", Aggro = 15 * M,
    };
    /// <summary>A griffin: an eagle's head and wings on a lion's body, hovering over the hills and dropping on its prey.</summary>
    public static readonly UnitType Griffin = new()
    {
        Key = "griffin", Name = "Грифон", Team = Team.Wild,
        Move = new Mover { Height = 2, Climb = 4, Drop = 8, Speed = 5.2f, Ladders = false },
        Hp = 520, Damage = 40, AttackTime = 1.1f, Bounty = 30, BodyH = 16, BodyR = 4, Reach = 14, Tool = "bite", Aggro = 18 * M, Hover = 22,
    };
    /// <summary>A basilisk of the marshes and the sands: a long eight-legged lizard whose look turns limbs to stone, for a while.</summary>
    public static readonly UnitType Basilisk = new()
    {
        Key = "basilisk", Name = "Василиск", Team = Team.Wild,
        Move = new Mover { Height = 2, Climb = 1, Drop = 3, Speed = 2.2f, Ladders = false },
        Hp = 600, Damage = 38, AttackTime = 1.4f, Bounty = 30, BodyH = 11, BodyR = 4, Reach = 13, Tool = "bite", Range = 16 * M, Aggro = 16 * M,
    };

    // ---------------------------------------------------------------- the sixth

    /// <summary>A treant: an old oak walking, slow and terrible; the forest heals it, and fire is its death.</summary>
    public static readonly UnitType Treant = new()
    {
        Key = "treant", Name = "Энт", Team = Team.Wild,
        Move = new Mover { Height = 3, Climb = 2, Drop = 4, Speed = 1.4f, Wade = 1 },
        Hp = 1400, Damage = 60, AttackTime = 2.2f, Bounty = 0, BodyH = 34, BodyR = 4.5f, Reach = 19, Tool = "club", Aggro = 12 * M, Regen = 8,
    };
    /// <summary>A stone golem: an earth spirit in a body of rock, tearing boulders out of the ground and hurling them.</summary>
    public static readonly UnitType Golem = new()
    {
        Key = "golem", Name = "Каменный голем", Team = Team.Wild,
        Move = new Mover { Height = 3, Climb = 2, Drop = 5, Speed = 1.5f, Wade = 2 },
        Hp = 1600, Damage = 56, AttackTime = 2.1f, Bounty = 40, BodyH = 28, BodyR = 4.5f, Reach = 18, Tool = "maul", Range = 28 * M, Aggro = 16 * M,
    };
    /// <summary>A fire spirit of the lava fields: a man-shaped blaze, setting alight what it passes and whom it strikes.</summary>
    public static readonly UnitType FireSpirit = new()
    {
        Key = "firespirit", Name = "Огненный дух", Team = Team.Wild,
        Move = new Mover { Height = 2, Climb = 3, Drop = 8, Speed = 3f, Ladders = false },
        Hp = 900, Damage = 60, AttackTime = 1.3f, Bounty = 40, BodyH = 20, BodyR = 3, Reach = 12, Tool = "bite", Aggro = 16 * M, Hover = 3,
    };
    /// <summary>A wyvern: a dragon's lesser cousin, two legs, wings and a poisoned sting, hovering over the marsh and the crags.</summary>
    public static readonly UnitType Wyvern = new()
    {
        Key = "wyvern", Name = "Виверна", Team = Team.Wild,
        Move = new Mover { Height = 2, Climb = 4, Drop = 8, Speed = 4.6f, Ladders = false },
        Hp = 1000, Damage = 55, AttackTime = 1.3f, Bounty = 40, BodyH = 20, BodyR = 5, Reach = 16, Tool = "bite", Aggro = 18 * M, Hover = 20,
    };

    // ---------------------------------------------------------------- the seventh

    /// <summary>A dragon: the greatest of them, red-scaled, its breath a fire that sets the field and the timber alight.</summary>
    public static readonly UnitType Dragon = new()
    {
        Key = "dragon", Name = "Дракон", Team = Team.Wild,
        Move = new Mover { Height = 3, Climb = 3, Drop = 8, Speed = 2.8f, Ladders = false, Wade = 2 },
        Hp = 4000, Damage = 120, AttackTime = 1.8f, Bounty = 150, BodyH = 36, BodyR = 7, Reach = 22, Tool = "bite", Range = 18 * M, Aggro = 24 * M,
    };

    // ---------------------------------------------------------------- those that join a party of ours

    /// <summary>
    /// A creature of the wild that has gone over to a party of ours (Campaign.Bestiary.cs): the same body, arms and
    /// strength on our side — a fighter, it does no work, it takes places in the army as its size is.
    /// </summary>
    static UnitType Joined(UnitType w) => new()
    {
        Key = w.Key + "_ours", Name = w.Name, Team = Team.Player, Move = w.Move, Hp = w.Hp, Damage = w.Damage, AttackTime = w.AttackTime,
        Reach = w.Reach, Range = w.Range, Ranged = w.Ranged, BodyH = w.BodyH, BodyR = w.BodyR, Tool = w.Tool,
        Room = w.BodyH >= 20 ? 3 : w.BodyH >= 17 ? 2 : 1,
    };

    public static readonly UnitType OurBandit = Joined(Bandit), OurCultist = Joined(Cultist), OurCentaur = Joined(Centaur), OurOgre = Joined(Ogre),
        OurMinotaur = Joined(Minotaur);

    /// <summary>The creatures that can go over to us, as ours.</summary>
    public static readonly UnitType[] Joiners = { OurBandit, OurCultist, OurCentaur, OurOgre, OurMinotaur };

    /// <summary>The kind a creature of the wild becomes on our side; null for one that never goes over.</summary>
    public static UnitType OursOf(UnitType wild) => Array.Find(Joiners, t => t.Key == wild.Key + "_ours");

    /// <summary>The new creatures of the bestiary (the old wolf, bear and skeleton stand in their own lists).</summary>
    public static readonly UnitType[] Beasts =
    {
        Bandit, Spider, Warg, Harpy, Cultist, Centaur, Ghoul, Ogre, WildTroll, Wraith, Minotaur, Griffin, Basilisk, Treant, Golem, FireSpirit, Wyvern, Dragon,
    };
}
