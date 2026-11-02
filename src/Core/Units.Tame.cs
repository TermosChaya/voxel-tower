namespace VoxelTower.Core;

/// <summary>
/// The town's own beasts (Sim.Tame.cs): hens about the dwellings, a yard dog that keeps with the workers, horses in the
/// stables' paddock, goats in a pen by the market. They are the valley's beasts (Team.Wild) but tame: nobody hunts them,
/// they take no places in the household, they climb no ladder, and they do nothing but live in the town; the horde may
/// cut them down in passing.
/// </summary>
public static partial class Kinds
{
    /// <summary>A hen: a hand high, she pecks about her dwelling's door, scatters from whoever walks into her, roosts at night.</summary>
    public static readonly UnitType Hen = new()
    {
        Key = "hen", Name = "Курица", Team = Team.Wild, Tame = true,
        Move = new Mover { Height = 1, Climb = 1, Drop = 3, Speed = 2.4f, Ladders = false },
        Hp = 6, Damage = 0, AttackTime = 1, BodyH = 4, BodyR = 1, FleeDist = 2 * VoxelWorld.PerMetre,
    };
    /// <summary>A yard dog: a scruffy cur with a rope collar; it trots after the workers, lies by the fire, and barks at the horde.</summary>
    public static readonly UnitType Cur = new()
    {
        Key = "cur", Name = "Дворовый пёс", Team = Team.Wild, Tame = true,
        Move = new Mover { Height = 1, Climb = 1, Drop = 3, Speed = 3.8f, Ladders = false },
        Hp = 40, Damage = 0, AttackTime = 1, BodyH = 7, BodyR = 2, FleeDist = 3 * VoxelWorld.PerMetre,
    };
    /// <summary>A horse of the stables, unsaddled: it grazes in the paddock and bolts for the stables from the horde.</summary>
    public static readonly UnitType Horse = new()
    {
        Key = "horse", Name = "Лошадь", Team = Team.Wild, Tame = true,
        Move = new Mover { Height = 2, Climb = 1, Drop = 2, Speed = 3.6f, Ladders = false },
        Hp = 120, Damage = 0, AttackTime = 1, BodyH = 15, BodyR = 4, FleeDist = 4 * VoxelWorld.PerMetre,
    };
    /// <summary>A goat: horns and a beard, it browses in its pen and bleats.</summary>
    public static readonly UnitType Goat = new()
    {
        Key = "goat", Name = "Коза", Team = Team.Wild, Tame = true,
        Move = new Mover { Height = 1, Climb = 1, Drop = 3, Speed = 3f, Ladders = false },
        Hp = 30, Damage = 0, AttackTime = 1, BodyH = 8, BodyR = 2, FleeDist = 3 * VoxelWorld.PerMetre,
    };

    public static readonly UnitType[] TameKinds = { Hen, Cur, Horse, Goat };
}
