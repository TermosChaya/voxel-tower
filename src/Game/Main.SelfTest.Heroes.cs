using System;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The heroes through the interface: a commander risen a level, his medallion comes up and his face in the row has a
    /// star; the right button on his face opens his card with his name, his level and the skills offered; a click on one
    /// takes it, the offer goes and the medallion with it; Esc shuts the card; a level more, the medallion opens his card.
    /// </summary>
    async Task SelfTestHeroes(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        // the valley's commander, or one hired for the test and let go after (a commander more would change the tests after)
        var cap = Sim.Units.Find(u => !u.Dead && u.T == Kinds.Captain && !u.Inside);
        bool hired = cap == null;
        cap ??= Sim.Spawn(Kinds.Captain, Sim.Gate);
        await Frames(3);
        var h = cap.Hero;
        int level0 = h?.Level ?? 0, xp0 = h?.Xp ?? 0;
        var grade0 = h == null ? null : (byte[])h.Grade.Clone();
        if (h == null) { check("a commander of ours is a hero", false, "he has no hero"); return; }
        Sim.Glory(cap, Heroes.XpFor(h.Level + 1) - h.Xp);
        await Frames(3);
        bool medallion = Hud.HeroMedallionShown;
        // the right button on his face in the row
        var bar = Hud.Target("faces") as ColonistBar;
        var at = bar?.FaceOf(cap);
        if (at is { } p) bar._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = p });
        await Frames(3);
        bool open = Hud.HeroPane.IsOpen && Hud.HeroPane.Hero == h;
        var choices = Hud.HeroPane.Choices;
        string name = Hud.HeroPane.NameShown;
        check("a commander risen a level: his medallion comes up; the right button on his face opens his card, his name on it and the skills offered",
            medallion && at != null && open && name.Contains(h.Name) && choices.Count == h.Offer.Count && choices.Count >= 2,
            $"medallion {medallion}; his face in the row {at != null}; card open {open} «{name}»; {choices.Count} skills offered of {h.Offer.Count}");
        // a click on the first offered
        var took = h.Offer.Count > 0 ? h.Offer[0] : Skill.Line;
        int g0 = h[took];
        if (choices.Count > 0) choices[0].EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(3);
        check("a click on a skill offered takes it: a grade more, nothing left to choose, the offer and the medallion gone",
            h[took] == g0 + 1 && h.Unspent == 0 && !h.Pending && Hud.HeroPane.Choices.Count == 0 && !Hud.HeroMedallionShown,
            $"{Heroes.NameOf(took)} {g0} → {h[took]}; waiting {h.Unspent}; offered on the card {Hud.HeroPane.Choices.Count}; medallion {Hud.HeroMedallionShown}");
        // Esc shuts it
        Tools._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.Escape, Keycode = Key.Escape, Pressed = true });
        await Frames(2);
        bool shut = !Hud.HeroPane.IsOpen;
        // a level more: the medallion opens his card
        Sim.Glory(cap, Heroes.XpFor(h.Level + 1) - h.Xp);
        await Frames(3);
        Hud.OpenRisenHero();
        await Frames(2);
        check("Esc shuts the card; a level more, the medallion opens his card", shut && Hud.HeroPane.IsOpen && Hud.HeroPane.Hero == h && Hud.HeroPane.Choices.Count > 0,
            $"shut {shut}; then open {Hud.HeroPane.IsOpen} with {Hud.HeroPane.Choices.Count} to choose");
        Hud.HeroPane.Close();
        // as he was
        h.Level = level0; h.Xp = xp0; h.Unspent = 0; h.Offer.Clear();
        Array.Copy(grade0, h.Grade, grade0.Length);
        if (hired) { cap.Dead = true; cap.DiedAt = -999; cap.State = UnitState.Dead; }
        await Frames(2);
    }

    /// <summary>
    /// A party's hero on the map: risen a level, a star over his face on the board; chosen twice there, his card opens;
    /// a skill taken on it. He is put back as he was after, so the campaign's test goes on as before.
    /// </summary>
    async Task SelfTestHeroBoard(Action<string, bool, string> check, MapParty party)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var h = party.Party.Commander?.Hero;
        if (h == null) { check("a party's commander on the map is a hero", false, "no hero"); return; }
        int level0 = h.Level, xp0 = h.Xp;
        var grade0 = (byte[])h.Grade.Clone();
        Heroes.Gain(h, Heroes.XpFor(h.Level + 1) - h.Xp);
        Hud.Board.Open();
        Hud.Board.ChooseParty(party);
        Hud.Board.ChooseParty(party);
        await Frames(3);
        bool open = Hud.HeroPane.IsOpen && Hud.HeroPane.Hero == h;
        var took = h.Offer.Count > 0 ? h.Offer[0] : Skill.Line;
        int g0 = h[took];
        var choices = Hud.HeroPane.Choices;
        if (choices.Count > 0) choices[0].EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        check("a party's hero on the map: chosen twice on the board, his card opens; a skill offered taken there",
            open && choices.Count >= 2 && h[took] == g0 + 1 && !h.Pending,
            $"card open {open} with {choices.Count} offered; {Heroes.NameOf(took)} {g0} → {h[took]}; waiting {h.Unspent}");
        Hud.HeroPane.Close();
        Hud.Board.Close();
        h.Level = level0; h.Xp = xp0; h.Unspent = 0; h.Offer.Clear();
        Array.Copy(grade0, h.Grade, grade0.Length);
        await Frames(2);
    }
}
