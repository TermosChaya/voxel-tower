using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The work table, as in RimWorld: a row for every pawn, a column for every kind of work with its priority in it —
/// 1 first, 4 last, empty never — and what it brings in by itself, whether a worker stands and fights, and what it is
/// doing now. A click on a cell raises its priority, the right button lowers it; a click on a column's head sets the
/// whole column like its first row raised; a click on a name picks that pawn.
/// </summary>
public partial class WorkTable : Control
{
    Main _m;
    readonly List<Unit> _rows = new();
    int _hoverRow = -1, _hoverCol = -1;

    public const float Head = 30, Row = 30;
    const float FaceW = 30, NameW = 150, CellW = 70, DutyW = 92, MilitiaW = 82;
    /// <summary>The columns past the kinds of work: what it brings in, the militia, what it is doing.</summary>
    const int DutyCol = Work.Kinds + 1, MilitiaCol = Work.Kinds + 2, DoingCol = Work.Kinds + 3;
    /// <summary>Where the column of a kind of work begins.</summary>
    public static float WorkX(int k) => FaceW + NameW + k * CellW;
    static float DutyX => WorkX(Work.Kinds);
    static float MilitiaX => DutyX + DutyW;
    static float DoingX => MilitiaX + MilitiaW;
    public const float Width = 880;

    static readonly string[] DutyNames = { "—", "камень", "брёвна", "землю" };
    static readonly int[] Duties = { Stockpile.None, Stockpile.Stone, Stockpile.Wood, Stockpile.Earth };

    public void Init(Main m)
    {
        _m = m;
        MouseFilter = MouseFilterEnum.Stop;
        MouseExited += () => { _hoverRow = _hoverCol = -1; QueueRedraw(); };
    }

    public override void _Process(double delta)
    {
        if (_m == null || !IsVisibleInTree()) return;
        _rows.Clear();
        foreach (var u in _m.Sim.Units) if (!u.Dead && u.Team == Team.Player) _rows.Add(u);
        _rows.Sort((a, b) => a.T == b.T ? a.Id.CompareTo(b.Id) : Array.IndexOf(Kinds.PlayerKinds, a.T).CompareTo(Array.IndexOf(Kinds.PlayerKinds, b.T)));
        CustomMinimumSize = new Vector2(Width - 40, Head + Math.Max(1, _rows.Count) * Row + 34);
        QueueRedraw();
    }

    /// <summary>Which column a point is in: 0 the face and name, then the kinds of work, what it brings in, the militia, what it is doing.</summary>
    static int Column(float x) => x < FaceW + NameW ? 0 : x < DutyX ? 1 + (int)((x - WorkX(0)) / CellW) : x < MilitiaX ? DutyCol : x < DoingX ? MilitiaCol : DoingCol;

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            int r = mm.Position.Y < Head ? -2 : (int)((mm.Position.Y - Head) / Row), c = Column(mm.Position.X);
            if (r >= _rows.Count) r = -1;
            if (r != _hoverRow || c != _hoverCol) { _hoverRow = r; _hoverCol = c; QueueRedraw(); }
            return;
        }
        if (e is not InputEventMouseButton { Pressed: true } mb || mb.ButtonIndex is not (MouseButton.Left or MouseButton.Right)) return;
        AcceptEvent();
        bool back = mb.ButtonIndex == MouseButton.Right;
        int col = Column(mb.Position.X);
        if (mb.Position.Y < Head)
        {
            // the head of a column: the whole column as its first row, raised
            if (_rows.Count == 0) return;
            if (col is >= 1 and <= Work.Kinds)
            {
                var k = (WorkKind)(col - 1);
                byte next = Work.Cycle(_rows[0].Priority[col - 1], back);
                foreach (var u in _rows) _m.Sim.SetPriority(u, k, next);
            }
            return;
        }
        int row = (int)((mb.Position.Y - Head) / Row);
        if (row < 0 || row >= _rows.Count) return;
        var p = _rows[row];
        switch (col)
        {
            case 0: _m.Command.SelectOne(p); break;
            case >= 1 and <= Work.Kinds:
                _m.Sim.SetPriority(p, (WorkKind)(col - 1), Work.Cycle(p.Priority[col - 1], back));
                break;
            case DutyCol:
                int at = Math.Max(0, Array.IndexOf(Duties, p.Duty));
                _m.Sim.SetDuty(new[] { p }, Duties[(at + (back ? Duties.Length - 1 : 1)) % Duties.Length]);
                break;
            case MilitiaCol:
                if (p.T.Work >= 1) _m.Sim.SetMilitia(new[] { p }, !p.Militia);
                break;
        }
        QueueRedraw();
    }

    public override string _GetTooltip(Vector2 at)
    {
        int col = Column(at.X);
        if (at.Y < Head)
            return col switch
            {
                0 => "Дружина\nЩелчок по имени — выбрать пешку, ещё раз — показать.",
                >= 1 and <= Work.Kinds => Work.Tips[col - 1] + "\n\nЩелчок по заголовку — весь столбец как у первой строки, следующим числом; ПКМ — обратно.",
                DutyCol => "Добывать самим\nЧто пешка добывает сама, когда размеченной работы нет: ломает валуны и руины, рубит лес или роет яму поодаль. Щелчок — по кругу.",
                MilitiaCol => "Ополчение\nРабочие не бегут от орды, а встречают её с кирками, как мечники. Солдатам не нужно.",
                _ => "Чем занят\nЧто пешка делает сейчас, и почему без дела, если без дела.",
            };
        return col is >= 1 and <= Work.Kinds ? Work.Tips[col - 1] + "\n\nЩелчок — 1, 2, 3, 4, никогда по очереди; ПКМ — обратно. 1 — первым делом." : "";
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);

    public override void _Draw()
    {
        if (_m == null) return;
        float w = Size.X;
        // the heads of the columns
        DrawString(Ornament.Caps, new Vector2(6, Head - 11), "ДРУЖИНА", HorizontalAlignment.Left, -1, 9, Ornament.Aged);
        for (int k = 0; k < Work.Kinds; k++)
        {
            float x = WorkX(k);
            bool hot = _hoverRow == -2 && _hoverCol == k + 1;
            PriorityCell.IconOf((WorkKind)k)(this, new Rect2(x + 4, 4, 16, 16), hot ? Ornament.BronzeLight : Ornament.Bronze);
            DrawString(Ornament.Caps, new Vector2(x + 22, Head - 11), Work.Names[k].ToUpperInvariant(), HorizontalAlignment.Left, CellW - 22, 8, hot ? Ornament.BronzeLight : Ornament.Aged);
        }
        DrawString(Ornament.Caps, new Vector2(DutyX + 4, Head - 11), "ДОБЫВАТЬ", HorizontalAlignment.Left, -1, 8, Ornament.Aged);
        DrawString(Ornament.Caps, new Vector2(MilitiaX + 4, Head - 11), "ОПОЛЧЕНИЕ", HorizontalAlignment.Left, -1, 8, Ornament.Aged);
        DrawString(Ornament.Caps, new Vector2(DoingX + 4, Head - 11), "ЧЕМ ЗАНЯТ", HorizontalAlignment.Left, -1, 8, Ornament.Aged);
        Ornament.Divider(this, new Vector2(4, Head - 3), new Vector2(w - 4, Head - 3), .7f);
        for (int r = 0; r < _rows.Count; r++)
        {
            var u = _rows[r];
            float y = Head + r * Row;
            if (r % 2 == 1) DrawRect(new Rect2(0, y, w, Row), new Color(1, .9f, .7f, .025f));
            if (r == _hoverRow) DrawRect(new Rect2(0, y, w, Row), new Color(Ornament.Bronze, .08f));
            if (_m.Selected.Contains(u)) DrawRect(new Rect2(0, y, 3, Row), Ornament.Ember);
            DrawTextureRect(Portraits.Of(u, 48), new Rect2(4, y + 2, Row - 4, Row - 4), false);
            DrawString(Ornament.SerifBold, new Vector2(FaceW + 4, y + 14), $"{u.T.Name} №{u.Id}", HorizontalAlignment.Left, NameW - 8, 13, Ornament.Parchment);
            float f = Math.Clamp(u.Hp / u.MaxHp, 0, 1);
            DrawRect(new Rect2(FaceW + 4, y + 19, NameW - 24, 4), new Color(0, 0, 0, .6f));
            DrawRect(new Rect2(FaceW + 4, y + 19, (NameW - 24) * f, 4), f > .6f ? Ornament.Moss : f > .3f ? Ornament.Ember : Ornament.Blood);
            for (int k = 0; k < Work.Kinds; k++)
            {
                var cell = new Rect2(WorkX(k) + 6, y + 3, CellW - 12, Row - 6);
                byte p = u.Priority[k];
                Ornament.Well(this, cell, r == _hoverRow && _hoverCol == k + 1, p == 1, false);
                if (p > 0) DrawString(Ornament.Display, new Vector2(cell.Position.X, cell.End.Y - 5), p.ToString(), HorizontalAlignment.Center, cell.Size.X, 19, PriorityCell.InkOf(p));
            }
            int duty = Math.Max(0, Array.IndexOf(Duties, u.Duty));
            DrawString(Ornament.Serif, new Vector2(DutyX + 6, y + 19), DutyNames[duty], HorizontalAlignment.Left, DutyW - 8, 13, duty == 0 ? Ornament.Faded : Ornament.BronzeLight);
            if (u.T.Work >= 1)
            {
                var box = new Rect2(MilitiaX + 8, y + 7, 16, 16);
                Ornament.Well(this, box, r == _hoverRow && _hoverCol == MilitiaCol, u.Militia, false);
                if (u.Militia) DrawString(Ornament.SerifBold, new Vector2(box.Position.X, box.End.Y - 2), "✓", HorizontalAlignment.Center, box.Size.X, 14, Ornament.EmberGlow);
            }
            else DrawString(Ornament.Serif, new Vector2(MilitiaX + 8, y + 19), "солдат", HorizontalAlignment.Left, MilitiaW - 10, 12, Ornament.Faded);
            string doing = Retinue.Doing(u, _m.Sim);
            bool hurt = u.Wounds != null && (u.Wounds.Wounds.Count > 0 || u.Wounds.Bleed > .15f);
            DrawString(Ornament.SerifItalic, new Vector2(DoingX + 4, y + 19), doing, HorizontalAlignment.Left, w - DoingX - 8, 12, hurt ? Ornament.Blood : u.Why != "" ? Ornament.BronzeLight : Ornament.Aged);
        }
        float foot = Head + Math.Max(1, _rows.Count) * Row + 8;
        if (_rows.Count == 0) DrawString(Ornament.SerifItalic, new Vector2(6, Head + 20), "Никого нет. Наймите людей у ворот.", HorizontalAlignment.Left, -1, 13, Ornament.Aged);
        DrawString(Ornament.SerifItalic, new Vector2(6, foot + 14),
            "Пешка идёт к ближайшей работе с высшим приоритетом (1 — первым делом); пусто — эту работу не берёт никогда. Делянки и добыча тоже по этим работам.",
            HorizontalAlignment.Left, w - 12, 12, Ornament.Faded);
    }
}
