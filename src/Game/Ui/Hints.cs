using System;
using System.Collections.Generic;
using Godot;

namespace VoxelTower.Game;

/// <summary>
/// The line over the console of what the mouse's buttons and the keys do now: with nothing in hand and nobody chosen,
/// with people chosen, with each tool in hand (the pick's version where it differs), with a blueprint or a spell. Each
/// key on a small plate, what it does after it; the keys as they are bound now. Whatever does not fit between the map
/// and the tools is left off from the end. The settings can turn it off.
/// </summary>
public partial class Hints : Control
{
    Main _m;
    readonly List<(string key, string what)> _items = new();
    readonly List<(string key, string what)> _want = new();
    string _shown = "";
    /// <summary>How wide it may be: the room between the map and the tools.</summary>
    public float Room = 900;

    const int KeySize = 12, TextSize = 13, Gap = 18, Pad = 7;
    public const float H = 24;

    public void Init(Main m)
    {
        _m = m;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        if (_m == null) return;
        _want.Clear();
        Collect(_want);
        // laid out again only when what it says changes
        string said = Room.ToString();
        foreach (var (k, w) in _want) said += k + w;
        if (said == _shown) return;
        _shown = said;
        _items.Clear();
        float width = 0;
        foreach (var it in _want)
        {
            float w = Width(it);
            if (width + w > Room) break;
            _items.Add(it);
            width += w + Gap;
        }
        Size = new Vector2(Math.Max(0, width - Gap), H);
        QueueRedraw();
    }

    static float Width((string key, string what) it) =>
        Ornament.SerifBold.GetStringSize(it.key, HorizontalAlignment.Left, -1, KeySize).X + 2 * Pad + 6
        + Ornament.Serif.GetStringSize(it.what, HorizontalAlignment.Left, -1, TextSize).X;

    static string L(Act a) => Bindings.Label(a);
    static string Lmb => Bindings.Fill("{Lmb}");
    static string Rmb => Bindings.Fill("{Rmb}");

    /// <summary>What to tell for what the player is doing now, the most needed first.</summary>
    void Collect(List<(string, string)> o)
    {
        var t = _m.Tools;
        bool dig = t.Opt.Dig;
        string size = $"{L(Act.WidthLess)} {L(Act.WidthMore)}", height = $"{L(Act.HeightLess)} {L(Act.HeightMore)}";
        switch (t.Tool)
        {
            case Tool.None when _m.Selected.Count > 0:
                // over one of ours pinned or down for good: what a right click on it does (Sim.Carry.cs)
                if (VoxelTower.Core.Sim.AidFor(_m.Command.UnitAt(GetViewport().GetMousePosition(), pawnsFirst: false)) is { } aid) o.Add((Rmb, aid));
                // our engines chosen alone: the right button rolls them where it points
                bool engines = true;
                foreach (var u in _m.Selected) if (!VoxelTower.Core.Kinds.IsOurEngine(u.T)) { engines = false; break; }
                bool captains = true;
                foreach (var u in _m.Selected) if (u.T != VoxelTower.Core.Kinds.Captain) { captains = false; break; }
                o.Add((Rmb, engines ? "перекатить машину" : captains ? "место, которое командир держит" : "идти, охотиться, атаковать"));
                o.Add((Lmb, "выбрать других"));
                if (_m.Selected.Count == 1) o.Add((L(Act.Possess), "вселиться"));
                o.Add(("Ctrl+1…5", "запомнить отряд"));
                o.Add(("Esc", "снять выбор"));
                return;
            case Tool.None:
                o.Add((Lmb, "выбрать людей"));
                o.Add(($"{L(Act.ToolBrush)}…{L(Act.ToolRepair)}", "инструменты"));
                o.Add(($"{Rmb} и тянуть", "сдвинуть вид"));
                o.Add(("Колесо", "приблизить"));
                o.Add((L(Act.Help), "все клавиши"));
                if (Main.Atlas != null) o.Add((L(Act.Board), "карта долин"));
                o.Add(("Alt", "клавиши на кнопках"));
                return;
            case Tool.Brush:
                o.Add((Lmb, dig ? "копать" : "рисовать"));
                o.Add((size, "размер"));
                o.Add((height, dig ? "глубина" : "высота"));
                o.Add((L(Act.Form), "столб или шар"));
                if (!dig) o.Add(("Shift", "кирка на мазок"));
                break;
            case Tool.Line when dig && t.Opt.Mine:
                o.Add((Lmb, "тянуть подкоп"));
                o.Add((height, "глубина"));
                o.Add((L(Act.Form), "ров"));
                break;
            case Tool.Line:
                o.Add((Lmb, dig ? "тянуть ров" : "тянуть стену"));
                if (dig) o.Add((L(Act.Form), "подкоп"));
                o.Add((size, dig ? "ширина" : "толщина"));
                o.Add((height, dig ? "глубина" : "высота"));
                o.Add((L(Act.Pick), dig ? "стена" : "ров"));
                o.Add((L(Act.Grid), "сетка"));
                break;
            case Tool.Box:
                o.Add((Lmb, dig ? "котлован от угла до угла" : "от угла до угла"));
                o.Add((L(Act.Form), "помост, стенки или дом"));
                if (t.Opt.House && !dig) o.Add((L(Act.Rotate), t.Opt.HouseDoor < 0 ? "сторона двери" : "дверь " + VoxelTower.Core.Shapes.SideWords[t.Opt.HouseDoor]));
                o.Add((height, dig ? "глубина" : "высота"));
                o.Add((L(Act.Pick), dig ? "стройка" : "котлован"));
                o.Add((L(Act.Grid), "сетка"));
                break;
            case Tool.Circle:
                o.Add((Lmb, "от центра к краю"));
                o.Add((L(Act.Form), "кольцо или сплошной"));
                o.Add((size, "толщина"));
                o.Add((height, dig ? "глубина" : "высота"));
                o.Add((L(Act.Pick), dig ? "стройка" : "яма"));
                break;
            case Tool.Extrude:
                o.Add((Lmb, dig ? "углубить грань" : "нарастить грань"));
                o.Add((L(Act.Pick), dig ? "наращивать" : "углублять"));
                break;
            case Tool.Unmark:
                o.Add((Lmb, "снять разметку рамкой или щелчком"));
                break;
            case Tool.Ladder:
                o.Add((Lmb, "по стене или обрыву"));
                break;
            case Tool.Gate:
                // over a gate: walled up; over a house's wall: a door of the house
                o.Add((Lmb, t.HoverGate != null ? "заложить ворота" : "по стене, по стене дома — дверь"));
                if (t.HoverGate == null) o.Add(("ЛКМ по воротам", "заложить"));
                break;
            case Tool.Repair:
                o.Add((Lmb, "по разрушенной постройке"));
                break;
            case Tool.Bridge:
                o.Add((Lmb, "от берега до берега"));
                break;
            case Tool.Gather:
                o.Add((Lmb, "обвести участок"));
                o.Add(($"{L(Act.MatStone)} {L(Act.MatWood)} {L(Act.MatEarth)} {L(Act.MatMetal)}", "камень, лес, земля, руда"));
                break;
            case Tool.Capture:
                o.Add((Lmb, "обвести постройку"));
                break;
            case Tool.Wand:
                o.Add((Lmb, t.Opt.WandCapture ? "снять чертёж" : dig ? "снести выделенное" : "переложить выделенное"));
                o.Add(("Shift", "добавить к выделению"));
                if (t.WandHeld > 0) o.Add(("Esc", "сбросить выделение"));
                o.Add((L(Act.Form), t.Opt.WandExact ? "любой оттенок" : "точно такой"));
                o.Add((L(Act.Pick), dig ? "переложить" : "снести"));
                break;
            case Tool.Building:
                o.Add((Lmb, "поставить здание"));
                o.Add((L(Act.Rotate), "повернуть"));
                o.Add((Rmb, "убрать"));
                return;
            case Tool.Yard when t.Opt.YardPick < BuildTools.WayCount:
                o.Add((Lmb, "протянуть дорожку"));
                o.Add((size, "ширина"));
                o.Add((L(Act.Form), "следующая"));
                break;
            case Tool.Yard:
                o.Add((Lmb, "поставить"));
                o.Add((L(Act.Rotate), "повернуть"));
                o.Add((L(Act.Form), "следующая"));
                break;
            case Tool.Template when t.DecorTab:
                o.Add((Lmb, "поставить"));
                o.Add((L(Act.Rotate), "повернуть"));
                o.Add((L(Act.Form), "следующая"));
                break;
            case Tool.Template when t.TemplatePick.Key == "house":
                o.Add((Lmb, "поставить дом"));
                o.Add((L(Act.Rotate), "повернуть, дверь туда же"));
                o.Add((height, "длина"));
                o.Add((size, "глубина"));
                o.Add((L(Act.Form), "следующий"));
                break;
            case Tool.Template when t.TemplatePick.Line:
                o.Add((Lmb, "протянуть"));
                o.Add((height, "высота"));
                o.Add((L(Act.Form), "следующий"));
                break;
            case Tool.Template:
                o.Add((Lmb, "поставить"));
                o.Add(("Shift", "ряд по протяжке"));
                o.Add((L(Act.Rotate), "повернуть"));
                if (t.TemplatePick.SizeMax > 0) o.Add((height, "высота"));
                o.Add((L(Act.Form), "следующий"));
                break;
            case Tool.Paste:
                o.Add((Lmb, "поставить копию"));
                o.Add((L(Act.Rotate), "повернуть"));
                o.Add((Rmb, "убрать"));
                return;
            case Tool.Spell:
                o.Add((Lmb, "применить"));
                o.Add((Rmb, "убрать"));
                return;
        }
        // every tool that marks: put it away, and take back what was marked
        o.Add((Rmb, "убрать инструмент"));
        o.Add(("Ctrl+Z", "отменить"));
    }

    public override void _Draw()
    {
        float x = 0;
        foreach (var (key, what) in _items)
        {
            float kw = Ornament.SerifBold.GetStringSize(key, HorizontalAlignment.Left, -1, KeySize).X + 2 * Pad;
            var plate = new Rect2(x, 2, kw, H - 4);
            Craft.Plate(this, plate, .9f);
            DrawString(Ornament.SerifBold, new Vector2(x + Pad, H / 2 + KeySize * .36f), key, HorizontalAlignment.Left, -1, KeySize, Ornament.BronzeLight);
            x += kw + 6;
            Ornament.Lettering(this, Ornament.Serif, new Vector2(x, H / 2 + TextSize * .36f), what, TextSize, Ornament.Parchment);
            x += Ornament.Serif.GetStringSize(what, HorizontalAlignment.Left, -1, TextSize).X + Gap;
        }
    }
}
