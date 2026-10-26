using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>Where a hero wears a thing: as the doll of «Heroes», a place for each.</summary>
public enum ArtSlot : byte { Head, Neck, Body, Weapon, Shield, Feet, Ring, Cloak }

/// <summary>How rare a thing is: a minor one the merchant has every week, a major one dearer and rarer, a relic only in a treasure.</summary>
public enum ArtTier : byte { Minor, Major, Relic }

/// <summary>
/// A thing a hero can wear, as the artifacts of «Heroes» (docs/handoff/13-world-map.md): its place on him, how rare, and
/// what it gives — grades of his skills over those he learnt (so it works wherever the skill does, in a valley and on the
/// map), moves a day, hexes seen further, and a resource the realm has of it every dawn; what the merchant asks for it.
/// </summary>
public sealed class Artifact
{
    public string Key = "", Name = "";
    public ArtSlot Slot;
    public ArtTier Tier;
    /// <summary>Grades of skills it adds, over what he learnt (as far as <see cref="Artifacts.MostGrade"/>).</summary>
    public (Skill skill, int grade)[] Skills = Array.Empty<(Skill, int)>();
    /// <summary>Moves a day, and hexes he sees further on the map.</summary>
    public int Moves, Sight;
    /// <summary>What the realm has of it every dawn while he wears it.</summary>
    public Res Daily;
    public float DailyAmount;
    /// <summary>The merchant's price: gold, and the rare resources (index from mercury, how many); none for a relic, which is not sold.</summary>
    public int Gold;
    public (int res, int n)[] Rare = Array.Empty<(int, int)>();

    /// <summary>What it gives, a line for its hover: «Строй +1, ход +200».</summary>
    public string About()
    {
        var parts = new List<string>();
        foreach (var (s, g) in Skills) parts.Add($"{Heroes.NameOf(s)} +{g}");
        if (Moves > 0) parts.Add($"ход +{Moves}");
        if (Sight > 0) parts.Add($"обзор +{Sight} {Sim.Plural(Sight, "клетка", "клетки", "клеток")}");
        if (DailyAmount > 0) parts.Add($"{MapThing.Amount_(Daily, DailyAmount)} в день");
        return string.Join(", ", parts);
    }

    /// <summary>The merchant's price as the game writes it: «1500 зол., 2 (кристаллы)».</summary>
    public string Price()
    {
        var parts = new List<string> { $"{Gold} зол." };
        foreach (var (i, n) in Rare) parts.Add(MapThing.Amount_((Res)((int)Res.Mercury + i), n));
        return string.Join(", ", parts);
    }
}

/// <summary>
/// The artifacts and their rules: the thirty of them, what a hero wearing some has of them (cached on him as he puts them
/// on and off), putting on and taking off, and the record of what he wears and carries.
/// </summary>
public static class Artifacts
{
    public const int Slots = 8;
    /// <summary>A skill's grade with what he wears, at the most: one over the grades he can learn.</summary>
    public const int MostGrade = Heroes.Grades + 1;

    public static readonly string[] SlotNames = { "голова", "шея", "тело", "оружие", "щит", "ноги", "кольцо", "плащ" };
    public static readonly string[] TierNames = { "простой", "редкий", "реликвия" };

    static Artifact A(string key, string name, ArtSlot slot, ArtTier tier, int gold, params (Skill, int)[] skills) =>
        new() { Key = key, Name = name, Slot = slot, Tier = tier, Gold = gold, Skills = skills };

    /// <summary>Every artifact, minor first, then the major ones, then the relics.</summary>
    public static readonly Artifact[] All =
    {
        // ---- minor: the merchant's every week, for gold alone
        A("scout_helm", "Шлем дозорного", ArtSlot.Head, ArtTier.Minor, 500, (Skill.Scouting, 1)),
        A("herb_amulet", "Амулет травника", ArtSlot.Neck, ArtTier.Minor, 400, (Skill.Healer, 1)),
        A("valor_medal", "Медальон отваги", ArtSlot.Neck, ArtTier.Minor, 400, (Skill.Word, 1)),
        A("mail", "Кольчуга", ArtSlot.Body, ArtTier.Minor, 600, (Skill.Line, 1)),
        A("sharp_sword", "Острый меч", ArtSlot.Weapon, ArtTier.Minor, 600, (Skill.Tactics, 1)),
        A("tracker_bow", "Лук следопыта", ArtSlot.Weapon, ArtTier.Minor, 600, (Skill.Archers, 1)),
        A("oak_shield", "Дубовый щит", ArtSlot.Shield, ArtTier.Minor, 400, (Skill.Line, 1)),
        A("mason_shield", "Щит каменщика", ArtSlot.Shield, ArtTier.Minor, 400, (Skill.Builder, 1)),
        new() { Key = "road_boots", Name = "Сапоги путника", Slot = ArtSlot.Feet, Tier = ArtTier.Minor, Gold = 600, Moves = 200 },
        A("builder_ring", "Кольцо зодчего", ArtSlot.Ring, ArtTier.Minor, 500, (Skill.Builder, 1)),
        new() { Key = "purse_ring", Name = "Перстень казначея", Slot = ArtSlot.Ring, Tier = ArtTier.Minor, Gold = 700, Daily = Res.Gold, DailyAmount = 25 },
        new() { Key = "far_ring", Name = "Кольцо зоркости", Slot = ArtSlot.Ring, Tier = ArtTier.Minor, Gold = 500, Sight = 2 },
        new() { Key = "road_cloak", Name = "Дорожный плащ", Slot = ArtSlot.Cloak, Tier = ArtTier.Minor, Gold = 500, Moves = 100, Sight = 1 },
        A("forest_cloak", "Плащ лесника", ArtSlot.Cloak, ArtTier.Minor, 500, (Skill.Scouting, 1)),
        // ---- major: now and then at the merchant, for gold and the rare resources; in the richer treasures
        new() { Key = "leader_crown", Name = "Венец полководца", Slot = ArtSlot.Head, Tier = ArtTier.Major, Gold = 1500, Skills = new[] { (Skill.Tactics, 1), (Skill.Word, 1) }, Rare = new[] { (2, 3) } },
        new() { Key = "honour_chain", Name = "Ожерелье чести", Slot = ArtSlot.Neck, Tier = ArtTier.Major, Gold = 1400, Skills = new[] { (Skill.Word, 1), (Skill.Line, 1) }, Rare = new[] { (3, 2) } },
        new() { Key = "guard_plate", Name = "Латы стража", Slot = ArtSlot.Body, Tier = ArtTier.Major, Gold = 1800, Skills = new[] { (Skill.Line, 2) }, Rare = new[] { (2, 2) } },
        new() { Key = "hero_sword", Name = "Меч-кладенец", Slot = ArtSlot.Weapon, Tier = ArtTier.Major, Gold = 2000, Skills = new[] { (Skill.Tactics, 2) }, Rare = new[] { (1, 3) } },
        new() { Key = "storm_bow", Name = "Лук бури", Slot = ArtSlot.Weapon, Tier = ArtTier.Major, Gold = 2000, Skills = new[] { (Skill.Archers, 2) }, Rare = new[] { (0, 3) } },
        new() { Key = "tower_shield", Name = "Башенный щит", Slot = ArtSlot.Shield, Tier = ArtTier.Major, Gold = 1500, Skills = new[] { (Skill.Line, 1), (Skill.Healer, 1) }, Rare = new[] { (2, 2) } },
        new() { Key = "swift_boots", Name = "Сапоги скорохода", Slot = ArtSlot.Feet, Tier = ArtTier.Major, Gold = 1800, Skills = new[] { (Skill.March, 1) }, Moves = 200, Rare = new[] { (3, 2) } },
        new() { Key = "healer_ring", Name = "Перстень знахаря", Slot = ArtSlot.Ring, Tier = ArtTier.Major, Gold = 1500, Skills = new[] { (Skill.Healer, 2) }, Rare = new[] { (0, 2) } },
        new() { Key = "gem_ring", Name = "Кольцо самоцветов", Slot = ArtSlot.Ring, Tier = ArtTier.Major, Gold = 2500, Daily = Res.Gems, DailyAmount = 1, Rare = new[] { (2, 2) } },
        new() { Key = "shadow_cloak", Name = "Плащ теней", Slot = ArtSlot.Cloak, Tier = ArtTier.Major, Gold = 1600, Skills = new[] { (Skill.Scouting, 2) }, Rare = new[] { (2, 2) } },
        new() { Key = "sage_mantle", Name = "Мантия мудреца", Slot = ArtSlot.Cloak, Tier = ArtTier.Major, Gold = 1500, Skills = new[] { (Skill.Healer, 1), (Skill.Builder, 1) }, Rare = new[] { (0, 2) } },
        // ---- relics: only in the treasures of the far zones
        new() { Key = "lord_crown", Name = "Корона владыки", Slot = ArtSlot.Head, Tier = ArtTier.Relic, Skills = new[] { (Skill.Word, 2) }, Daily = Res.Gold, DailyAmount = 100 },
        new() { Key = "hero_plate", Name = "Доспех героя", Slot = ArtSlot.Body, Tier = ArtTier.Relic, Skills = new[] { (Skill.Line, 2), (Skill.Tactics, 1) } },
        new() { Key = "flame_blade", Name = "Клинок пламени", Slot = ArtSlot.Weapon, Tier = ArtTier.Relic, Skills = new[] { (Skill.Tactics, 2), (Skill.Archers, 1) } },
        new() { Key = "wind_boots", Name = "Сапоги ветра", Slot = ArtSlot.Feet, Tier = ArtTier.Relic, Skills = new[] { (Skill.March, 2) }, Moves = 300 },
        new() { Key = "general_cloak", Name = "Плащ полководца", Slot = ArtSlot.Cloak, Tier = ArtTier.Relic, Skills = new[] { (Skill.Word, 2), (Skill.March, 1) } },
    };

    static Dictionary<string, Artifact> _byKey;
    public static Artifact Of(string key)
    {
        if (key == null) return null;
        if (_byKey == null) { _byKey = new(); foreach (var a in All) _byKey[a.Key] = a; }
        return _byKey.TryGetValue(key, out var x) ? x : null;
    }

    /// <summary>The artifacts of a tier, in their order.</summary>
    public static List<Artifact> OfTier(ArtTier t) => new(Array.FindAll(All, a => a.Tier == t));

    // ------------------------------------------------------------ worn

    /// <summary>What he has of what he wears, counted again: the grades over his skills, the moves, the hexes seen further.</summary>
    public static void Recount(Hero h)
    {
        Array.Clear(h.Bonus);
        h.ArtMoves = 0; h.ArtSight = 0;
        foreach (var key in h.Worn)
        {
            if (Of(key) is not { } a) continue;
            foreach (var (s, g) in a.Skills) h.Bonus[(int)s] = (byte)Math.Min(255, h.Bonus[(int)s] + g);
            h.ArtMoves += a.Moves; h.ArtSight += a.Sight;
        }
    }

    /// <summary>A thing come to a hero: on him if its place is free, else into his bag.</summary>
    public static void Give(Hero h, Artifact a)
    {
        if (h == null || a == null) return;
        if (h.Worn[(int)a.Slot] == null) { h.Worn[(int)a.Slot] = a.Key; Recount(h); }
        else h.Bag.Add(a.Key);
    }

    /// <summary>A thing from his bag put on, what was in its place into the bag; false if it is not in his bag.</summary>
    public static bool PutOn(Hero h, string key)
    {
        if (h == null || Of(key) is not { } a || !h.Bag.Remove(key)) return false;
        int s = (int)a.Slot;
        if (h.Worn[s] != null) h.Bag.Add(h.Worn[s]);
        h.Worn[s] = key;
        Recount(h);
        return true;
    }

    /// <summary>What he wears in a place taken off into his bag; false if nothing is there.</summary>
    public static bool TakeOff(Hero h, ArtSlot slot)
    {
        if (h?.Worn[(int)slot] is not { } key) return false;
        h.Worn[(int)slot] = null;
        h.Bag.Add(key);
        Recount(h);
        return true;
    }

    /// <summary>What a hero's things give the realm at a dawn: (resource, amount) for each worn thing that gives.</summary>
    public static IEnumerable<(Res res, float n)> Dailies(Hero h)
    {
        if (h == null) yield break;
        foreach (var key in h.Worn) if (Of(key) is { DailyAmount: > 0 } a) yield return (a.Daily, a.DailyAmount);
    }

    /// <summary>«Кольчуга, Острый меч» — what he wears, or «ничего».</summary>
    public static string Worn(Hero h)
    {
        var list = new List<string>();
        foreach (var key in h.Worn) if (Of(key) is { } a) list.Add(a.Name);
        return list.Count == 0 ? "ничего" : string.Join(", ", list);
    }

    // ------------------------------------------------------------ the record (Heroes.Write/Read)

    internal static void Write(System.IO.BinaryWriter w, Hero h)
    {
        foreach (var key in h.Worn) w.Write(key ?? "");
        w.Write((byte)Math.Min(255, h.Bag.Count));
        for (int i = 0; i < h.Bag.Count && i < 255; i++) w.Write(h.Bag[i]);
    }

    internal static void Read(System.IO.BinaryReader r, Hero h)
    {
        for (int s = 0; s < Slots; s++) { string key = r.ReadString(); h.Worn[s] = Of(key) != null ? key : null; }
        for (int n = r.ReadByte(); n > 0; n--) { string key = r.ReadString(); if (Of(key) != null) h.Bag.Add(key); }
        Recount(h);
    }
}
