using System;

namespace VoxelTower.Core;

/// <summary>The kinds of work a pawn can be set to, the columns of the work table: laying, and taking out earth, wood or stone.</summary>
public enum WorkKind : byte { Build, Dig, Fell, Quarry }

/// <summary>
/// What each pawn takes up and in what order, as colonists are set in RimWorld: for every kind of work a priority
/// from 1 (first) to 4 (last), or 0 for never. Of the work it may do a pawn goes to the nearest of the highest
/// priority there is; work of the same priority it takes as it comes, nearest first.
/// </summary>
public static class Work
{
    public const int Kinds = 4;
    /// <summary>The priority a new pawn has for everything.</summary>
    public const byte Usual = 3;

    public static readonly string[] Names = { "Стройка", "Копать", "Рубить", "Камень" };
    /// <summary>What it is to do it: "не велено: копать".</summary>
    public static readonly string[] Verbs = { "строить", "копать", "рубить", "ломать камень" };

    public static readonly string[] Tips =
    {
        "Стройка\nКласть размеченное: стены и башни, лестницы, мосты, ворота, починку — из запасов камня, брёвен и земли.",
        "Копать\nВынимать землю: рвы, котлованы, ямы, земля с делянок.",
        "Рубить\nВалить лес и разбирать деревянное: деревья, кусты, лестницы, настилы орды, брёвна стен.",
        "Камень\nЛомать камень: скалы, валуны, руины, руду в жилах и свою кладку, если её велено разобрать.",
    };

    /// <summary>The work an order is: laying for what is to be laid, else what is taken out.</summary>
    public static WorkKind Of(byte order) => order switch
    {
        Orders.Remove => WorkKind.Dig,
        Orders.RemoveWood => WorkKind.Fell,
        Orders.RemoveStone => WorkKind.Quarry,
        _ => WorkKind.Build,
    };

    /// <summary>The work that brings in a store: stone is broken, timber felled, earth dug.</summary>
    public static WorkKind For(int store) => store switch { Stockpile.Stone => WorkKind.Quarry, Stockpile.Wood => WorkKind.Fell, _ => WorkKind.Dig };

    /// <summary>The kinds of work a pawn takes up at a priority, as bits by <see cref="WorkKind"/>.</summary>
    public static int Mask(Unit u, int priority)
    {
        int m = 0;
        for (int k = 0; k < Kinds; k++) if (u.Priority[k] == priority) m |= 1 << k;
        return m;
    }

    /// <summary>Every kind of work it takes up at all.</summary>
    public static int Allowed(Unit u)
    {
        int m = 0;
        for (int k = 0; k < Kinds; k++) if (u.Priority[k] > 0) m |= 1 << k;
        return m;
    }

    public static bool Does(Unit u, WorkKind k) => u.Priority[(int)k] > 0;

    /// <summary>The next priority a click on a cell of the table gives: none → 1 → 2 → 3 → 4 → none, or back the other way.</summary>
    public static byte Cycle(byte p, bool back) => back ? (byte)(p == 0 ? 4 : p - 1) : (byte)(p >= 4 ? 0 : p + 1);
}
