using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The interface's icons: silhouettes of one set (game-icons.net, CC BY 3.0: see icons/CREDITS.txt), white in their
/// files and inked here in the frame's colours, each over a faint shadow so that it reads on any plate.
/// </summary>
public static class Icons
{
    public delegate void Painter(CanvasItem c, Rect2 r, Color ink);

    static readonly Dictionary<string, Texture2D> _glyphs = new();
    static readonly Dictionary<string, Painter> _painters = new();

    /// <summary>An icon of the set by its file's name, in the ink given, fitted square in the middle of the rect.</summary>
    public static void Glyph(CanvasItem c, Rect2 r, Color ink, string name)
    {
        if (!_glyphs.TryGetValue(name, out var t)) _glyphs[name] = t = GD.Load<Texture2D>($"res://icons/{name}.svg");
        if (t == null) return;
        float s = Mathf.Min(r.Size.X, r.Size.Y);
        var box = new Rect2(r.Position + (r.Size - new Vector2(s, s)) / 2, new Vector2(s, s));
        c.DrawTextureRect(t, new Rect2(box.Position + new Vector2(.8f, 1.2f), box.Size), false, new Color(0, 0, 0, .55f * ink.A));
        c.DrawTextureRect(t, box, false, ink);
    }

    /// <summary>The painter of an icon of the set, made once.</summary>
    public static Painter Of(string name)
    {
        if (!_painters.TryGetValue(name, out var p)) _painters[name] = p = (c, r, ink) => Glyph(c, r, ink, name);
        return p;
    }

    // the tool belt and the frame
    public static readonly Painter Brush = Of("paint-brush"), StraightLine = Of("pencil-ruler"), Box = Of("cube"), Circle = Of("circle"),
        Extrude = Of("3d-stairs"), Erase = Of("broom"), Flame = Of("flame"), Menu = Of("hamburger-menu"), Shovel = Of("dig-hole"),
        Pick = Of("mining"), Bow = Of("bow-arrow"), Sword = Of("broadsword"), Ladder = Of("ladder"), GateIcon = Of("dungeon-gate"),
        BridgeIcon = Of("stone-bridge"), Mend = Of("trowel"), Gather = Of("axe-in-stump"), Horn = Of("hunting-horn"),
        Scroll = Of("scroll-unfurled"), Skull = Of("death-skull"), Eye = Of("all-seeing-eye"), Tome = Of("spell-book"),
        Coin = Of("two-coins"), Earth = Of("dig-hole"), TopView = Of("treasure-map"), HomeView = Of("castle"), Question = Of("help"),
        Helm = Of("visored-helm"), CrossedSwords = Of("crossed-swords"), WandIcon = Of("magic-wand"), YardIcon = Of("campfire"),
        TemplateIcon = Of("stone-tower");

    /// <summary>A material: a block of stone, a pile of logs, a pit of earth, in the material's own colour.</summary>
    public static Painter Material(byte m) => (c, r, ink) =>
        Glyph(c, r, (m == Mat.Stone ? new Color("a9a3a6") : Ornament.MatColor(m)).Lerp(ink, .25f), m == Mat.Stone ? "stone-block" : m == Mat.Wood ? "wood-pile" : "dig-hole");

    static readonly Dictionary<string, string> Sigils = new()
    {
        // the skills
        ["yew"] = "pocket-bow", ["barbs"] = "arrowhead", ["braziers"] = "fire-bowl", ["volley"] = "arrow-cluster", ["owleye"] = "owl",
        ["longbows"] = "high-shot", ["mortar"] = "brick-wall", ["masons"] = "chisel", ["ironbound"] = "gate", ["granite"] = "stone-stack",
        ["bastion"] = "hill-fort", ["fortress"] = "stone-tower", ["picks"] = "war-pick", ["barrows"] = "wheelbarrow",
        ["buckets"] = "full-wood-bucket", ["carts"] = "stone-path", ["hides"] = "animal-hide", ["guild"] = "anvil",
        ["gambesons"] = "leather-vest", ["drill"] = "target-dummy", ["banners"] = "knight-banner", ["mail"] = "chain-mail",
        ["sergeants"] = "rally-the-troops", ["veterans"] = "visored-helm", ["spikes"] = "stakes-fence", ["tar"] = "cauldron",
        ["soaked"] = "flaming-arrow", ["naphtha"] = "fire-bottle", ["firestorm"] = "fire-ray", ["deathtraps"] = "wolf-trap",
        // the troops
        ["spearmen"] = "spears", ["shieldmen"] = "shield", ["halberds"] = "halberd", ["shieldwall"] = "attached-shield",
        ["longshafts"] = "barbed-spear", ["knights"] = "black-knight-helm", ["slingers"] = "sling", ["crossbows"] = "crossbow",
        ["leadshot"] = "cannon-ball", ["bodkins"] = "broadhead-arrow", ["alchemists"] = "acid-flask", ["bombards"] = "cannon",
        ["hounds"] = "hound", ["riders"] = "horse-head", ["hunt"] = "sniffing-dog", ["horsearchers"] = "cloaked-figure-on-horseback",
        ["charge"] = "mounted-knight", ["lancers"] = "cavalry", ["barracks"] = "medieval-barracks", ["drillyard"] = "sword-clash",
        ["bunks"] = "bunk-beds", ["trolls"] = "troll", ["giants"] = "giant", ["boulders"] = "boulder-dash",
        ["mangonels"] = "catapult", ["pitchkegs"] = "pitch-keg", ["windlass"] = "winch", ["ballistas"] = "ballista",
        ["heavystones"] = "heavy-stone", ["trebuchets"] = "trebuchet",
        // the town's buildings, as the lore opens them and the «Город» tab shows them (Sim.Town.cs)
        ["b_hall"] = "castle", ["b_market"] = "two-coins", ["b_smithy"] = "anvil", ["b_carpentry"] = "axe-in-stump",
        ["b_masons"] = "trowel", ["b_drill"] = "target-dummy", ["b_infirmary"] = "bunk-beds", ["b_traps"] = "wolf-trap",
        ["b_barracks"] = "medieval-barracks", ["b_knights"] = "black-knight-helm", ["b_range"] = "bow-arrow", ["b_lab"] = "acid-flask",
        ["b_kennel"] = "hound", ["b_stables"] = "horse-head", ["b_trolls"] = "troll", ["b_giants"] = "giant", ["b_workshop"] = "trebuchet",
        // the rites
        ["spell.Hand"] = "earth-spit", ["spell.Rift"] = "earth-crack", ["spell.Mire"] = "swamp", ["spell.Skin"] = "rock-golem",
        ["spell.Fall"] = "falling-rocks", ["spell.Quake"] = "quake-stomp", ["spell.FireRain"] = "fire-wave",
        ["spell.Thunder"] = "lightning-branches", ["spell.Weather"] = "lightning-storm", ["spell.BlackWind"] = "wind-slap",
        ["spell.BloodMoon"] = "evil-moon", ["spell.Meteor"] = "meteor-impact", ["spell.Dread"] = "terror", ["spell.DeadFog"] = "poison-cloud",
        ["spell.Chains"] = "manacles", ["spell.Plague"] = "rat", ["spell.Raise"] = "raise-skeleton", ["reap"] = "scythe",
        ["spell.Telekinesis"] = "magic-palm", ["spell.Recall"] = "return-arrow", ["spell.Eye"] = "all-seeing-eye",
        ["spell.Explosion"] = "mine-explosion", ["spell.Oath"] = "bloody-sword", ["spell.Whirlwind"] = "whirlwind",
        ["rosary"] = "prayer-beads", ["vessel"] = "jeweled-chalice", ["altar"] = "sword-altar", ["litany"] = "open-book",
        ["vigil"] = "candle-light", ["might"] = "holy-symbol",
    };

    /// <summary>The sigil of any node of the tree by its key; a question mark for one without its own.</summary>
    public static Painter SigilFor(string key) => Sigils.TryGetValue(key, out var name) ? Of(name) : Question;

    /// <summary>The great emblem at the outer end of a branch, by the branch's name.</summary>
    public static Painter BranchMark(string branch) => branch switch
    {
        "Хозяйство" => Of("two-coins"), "Ремёсла" => Of("anvil"), "Кладка" => Of("stone-wall"), "Дружина" => Sword, "Ловушки" => Of("spiky-pit"),
        "Пехота" => Of("swordman"), "Стрелки" => Of("bowman"), "Конница" => Of("horse-head"), "Великаны" => Of("footprint"), "Машины" => Of("catapult"),
        "Круг земли" => Of("mountains"), "Круг огня и неба" => Of("fire"), "Круг смерти" => Skull, "Круг воли" => Of("magic-swirl"),
        "Благодать" => Of("angel-wings"),
        _ => Question,
    };

    /// <summary>The emblem of a page of the tree, for its tab and its middle.</summary>
    public static Painter PageMark(int page) => page switch { 0 => Of("castle"), 1 => CrossedSwords, _ => Eye };
}
