using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The frame unfolding in a siege of one's own, as the settings want it at first: at the start the main tools (brush,
/// line, rectangle, unmark, area, repair, the materials and the pick) and three tabs (tasks, works, horde); the rest let
/// out as they are wanted, each with a line in the chronicle and a mark of new on it till it is first used, and the
/// card of first meeting then. The circle and extrude after 3 markings; the ladder after 5, or when workers cannot get
/// to their work; the gate after 6, a house laid out, or the first night; the bridge with the first ditch; the magic
/// wand after 8, or the third marking of the pick; the blueprint after 10; the lore with its mysteries when the first night is beaten off or there is knowledge to spend.
/// What is let out is kept in the save (Sim.LetOut); the sandbox and the tutorial do not unfold.
/// </summary>
public sealed class Unfold
{
    readonly Main _m;
    double _next;

    /// <summary>What comes later, its key, and its line in the chronicle.</summary>
    static readonly (Tool tool, string line)[] LaterTools =
    {
        (Tool.Circle, "Новое: круг ({ToolCircle}), для башен и бастионов."),
        (Tool.Extrude, "Новое: выдавить ({ToolExtrude}), наращивает грань на шаг или углубляет её."),
        (Tool.Ladder, "Новое: лестница ({ToolLadder}), на стену или обрыв."),
        (Tool.Gate, "Новое: ворота ({ToolGate}), проём со створками в стене."),
        (Tool.Bridge, "Новое: мост ({ToolBridge}), через ров или реку."),
        (Tool.Capture, "Новое: чертёж ({ToolCapture}), сохраняет постройку, чтобы ставить её копии."),
        (Tool.Wand, "Новое: волшебная палочка ({ToolWand}). Выделяет всё прилегающее из одного материала, чтобы снести или переложить."),
    };
    const string LoreKey = "tab:lore", LoreLine = "Новое: вкладка «Знания». За отбитые волны дают знания, на них изучают улучшения.";

    public Unfold(Main m) { _m = m; }

    static string KeyOf(Tool t) => "tool:" + t;

    bool Out(string key) => !_m.Sim.Unfolds || _m.Sim.LetOut.Contains(key);

    /// <summary>A siege of one's own begins: it unfolds if the settings want it, from nothing let out.</summary>
    public void Begin()
    {
        _m.Sim.Unfolds = Settings.Unfold;
        _m.Sim.LetOut.Clear();
        Apply();
    }

    /// <summary>Everything let out at once (the settings changed in play).</summary>
    public void OpenAll()
    {
        _m.Sim.Unfolds = false;
        Apply();
    }

    /// <summary>The frame shown as far as it is let out: a game begun, read back, or changed in its settings.</summary>
    public void Apply()
    {
        var hud = _m.Hud;
        foreach (var (tool, _) in LaterTools) hud.Withhold(tool, !Out(KeyOf(tool)));
        bool lore = Out(LoreKey);
        hud.Withhold(Hud.Part.Lore, !lore);
        hud.Withhold(Hud.Part.Magic, !lore);
        // the town comes with the lore: what the lore opens is put up there
        hud.Withhold(Hud.Part.Town, !lore);
    }

    /// <summary>Once a second: what is wanted now is let out.</summary>
    public void Tick()
    {
        var sim = _m.Sim;
        if (!sim.Unfolds || sim.Sandbox || _m.Tutor is { Over: false } || Time.GetTicksMsec() / 1000.0 < _next) return;
        _next = Time.GetTicksMsec() / 1000.0 + 1;
        int made = sim.MarkingsMade;
        bool stuck = false, house = false;
        foreach (var u in sim.Units) if (!u.Dead && u.Team == Team.Player && u.Why == "до работы не дойти") { stuck = true; break; }
        foreach (var z in sim.Zones) if (z.Kind == Zone.Dwelling) { house = true; break; }
        bool night = sim.Waves.Number >= 1;
        foreach (var (tool, line) in LaterTools)
        {
            bool want = tool switch
            {
                Tool.Circle or Tool.Extrude => made >= 3,
                Tool.Ladder => made >= 5 || stuck,
                Tool.Gate => made >= 6 || house || night,
                Tool.Bridge => _m.Tools.DigMarkings > 0,
                Tool.Capture => made >= 10,
                Tool.Wand => made >= 8 || _m.Tools.DigMarkings >= 3,
                _ => false,
            };
            if (want) Open(KeyOf(tool), line, () => _m.Hud.Freshen(tool));
        }
        if (night && !sim.Waves.Active || sim.Knowledge >= 1) Open(LoreKey, LoreLine, () => _m.Hud.Freshen(Hud.Part.Lore));
    }

    void Open(string key, string line, Action fresh)
    {
        if (Out(key)) return;
        _m.Sim.LetOut.Add(key);
        Apply();
        fresh();
        _m.Hud.Log(line, Hud.Kind.Ok);
        Sound.Ui("tick");
    }
}
