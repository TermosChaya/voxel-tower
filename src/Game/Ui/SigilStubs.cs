using Godot;

namespace VoxelTower.Game;

/// <summary>
/// Stand-ins for the sigils of the troops' and the rites' pages till they are engraved: the skills' own sigils where a
/// key has one, and the nearest of the tool belt's for the rest.
/// </summary>
public static partial class Icons
{
    public static Painter SigilFor(string key) => SigilOf(key);

    public static Painter BranchMark(string branch) => branch switch
    {
        "Луки" => Bow, "Кладка" => Fortress, "Труд" => Pick, "Дружина" => Sword, "Ловушки" => Stakes,
        "Пехота" => Chevrons, "Стрелки" => Bow, "Конница" => Horn, "Великаны" => Anvil,
        "Круг земли" => Earth, "Круг огня и неба" => Flame, "Круг смерти" => Skull, "Круг воли" => Eye, "Благодать" => Tome,
        _ => Question,
    };

    public static Painter PageMark(int page) => page switch { 0 => Tome, 1 => Sword, _ => Eye };
}
