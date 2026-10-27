using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The tutorial, a mode of its own: the valley of a new game, the frame let out part by part, and a card that leads
/// the player by the hand through everything the game does — the view, the household, the stores and their taking,
/// the mending of the old fort, the brushes and the grid, the order of the work, the time, the horde (a band of
/// three is let in for the lesson), the muster, the lore and the old powers, the eyes of one of ours, the menu and
/// the controls. Each step says what to do and is done when the player has done it; a bronze frame shows where on
/// the frame, a beacon where in the valley. The horde does not come by itself while it lasts: the clock stands at
/// the forenoon. At the end the player starts a siege of his own, stays in this valley, or goes on to the campaign's
/// part (Tutorial.Campaign.cs): a small campaign of its own on the big map, its step kept while the scene begins again
/// for each valley gone into, the cell it is about framed on the map.
/// </summary>
public partial class Tutorial : Node
{
    sealed class Step
    {
        public string Chapter, Title, Text, Task;
        /// <summary>Done: the step is over when it holds; none is a step to read, gone on from with «Дальше».</summary>
        public Func<bool> Done;
        public Action Enter;
        /// <summary>What on the frame to point at (a key of <see cref="Hud.Target"/>), and where in the valley, in voxels.</summary>
        public string Point;
        public Func<(int x, int z)?> Mark;
        /// <summary>A line of how far it has got, under the task.</summary>
        public Func<string> Status;
        /// <summary>The hex of the big map the step is about, framed on it while the map is open (the campaign's part).</summary>
        public Func<(int col, int row)?> Hex;
    }

    Main _m;
    readonly List<Step> _steps = new();
    int _at = -1;
    double _clock, _doneAt = -1;
    bool _over;
    /// <summary>The campaign's part (Tutorial.Campaign.cs), not the valley's.</summary>
    bool _campaign;
    /// <summary>The step the campaign's part is at, kept while the scene begins again for each valley; −1 when it is not on.</summary>
    public static int CampaignAt = -1;
    Card _card;
    Pointer _pointer;
    Beacon _beacon;

    /// <summary>Where the clock is held while the tutorial lasts: the forenoon, so the valley is seen in good light.</summary>
    const double HoldAt = Waves.Prep * .55;

    public bool Over => _over;
    /// <summary>The step it is at, from 0, and how many there are (tests and screenshots).</summary>
    public int At => _at;
    public string Title => _at >= 0 ? _steps[_at].Title : "";
    public int Count => _steps.Count;

    public bool Campaign => _campaign;

    public void Init(Main m, int from = 0, bool campaign = false)
    {
        _m = m;
        _campaign = campaign;
        if (campaign) BuildCampaign(); else Build();
        // (the campaign's part has the whole frame out: the valley's part let it out piece by piece)
        if (!campaign) _m.Hud.Conceal();
        _card = new Card(this);
        _m.Hud.Mount(_card);
        _pointer = new Pointer();
        _m.Hud.Mount(_pointer);
        _beacon = new Beacon();
        _m.AddChild(_beacon);
        // the horde's ways are shown when the lesson comes to the horde
        if (!campaign) _m.Sim.WantTrails = false;
        Go(Math.Clamp(from, 0, _steps.Count - 1));
    }

    // ---------------------------------------------------------------- going through

    void Go(int i)
    {
        _at = i;
        _doneAt = -1;
        if (_campaign) CampaignAt = i;
        var s = _steps[i];
        // whatever the steps up to here let out is out, even when the player skipped them
        for (int k = 0; k <= i; k++) LetOut(k);
        s.Enter?.Invoke();
        _card.Show(s, i, _steps.Count, Chapters(i));
        Juice.Appear(_card, .97f, .22);
    }

    /// <summary>What step k lets out of the frame: every step names it in its own Enter; these are the parts by step.</summary>
    void LetOut(int k)
    {
        foreach (var (step, part) in _parts) if (step == k) _m.Hud.Reveal(part);
        foreach (var (step, tool) in _tools) if (step == k) _m.Hud.Unlock(tool);
        if (k == _matsAt) _m.Hud.UnlockMaterials();
        if (k == _pickAt) _m.Hud.UnlockPick();
    }

    readonly List<(int step, Hud.Part part)> _parts = new();
    readonly List<(int step, Tool tool)> _tools = new();
    int _matsAt = int.MaxValue, _pickAt = int.MaxValue;

    (int n, int of, string name) Chapters(int i)
    {
        var names = new List<string>();
        foreach (var s in _steps) if (names.Count == 0 || names[^1] != s.Chapter) names.Add(s.Chapter);
        return (names.IndexOf(_steps[i].Chapter) + 1, names.Count, _steps[i].Chapter);
    }

    public void Next() { if (_at < _steps.Count - 1) Go(_at + 1); }
    public void Back() { if (_at > 0) Go(_at - 1); }

    /// <summary>Out of the tutorial: the whole frame out, the clock let go — a game of one's own in this valley.</summary>
    public void Leave()
    {
        if (_over) return;
        _over = true;
        _m.Hud.RevealAll();
        _m.Sim.WantTrails = true;
        if (_campaign)
        {
            CampaignAt = -1;
            if (_m.WorldMap != null) _m.WorldMap.Chosen = (-1, -1);
            _m.Hud.Log("Обучение кампании окончено. Кампания идёт дальше.", Hud.Kind.Ok);
        }
        else
        {
            if (!_m.Sim.Raids.Active) _m.Sim.Waves.Countdown = Waves.Prep;
            _m.Hud.Log("Обучение окончено. Орда придёт через 10 минут.", Hud.Kind.Warn);
        }
        _card.QueueFree();
        _pointer.QueueFree();
        _beacon.QueueFree();
        _m.Hud.Refresh();
    }

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("Tutorial");
        if (_over || _at < 0) return;
        _clock += delta;
        var sim = _m.Sim;
        // the horde keeps away while the lesson is not about it (in the campaign the map's own peace keeps it away)
        if (!_campaign && !sim.Raids.Active) { sim.Waves.Countdown = HoldAt; sim.Waves.DawnLeft = Math.Min(sim.Waves.DawnLeft, 2); }
        var s = _steps[_at];
        if (_doneAt < 0 && s.Done != null && s.Done())
        {
            _doneAt = _clock;
            _card.Done();
            _m.Sound?.Chime();
        }
        if (_doneAt >= 0 && _clock - _doneAt > 1.1 && _at < _steps.Count - 1) Next();
        _card.Status(s.Status?.Invoke());
        _pointer.Target = s.Point != null && !_m.Hud.MenuOpen ? _m.Hud.Target(s.Point) : null;
        var mark = s.Mark?.Invoke();
        _beacon.Visible = mark != null && !_m.FP.On && !_m.AtlasOpen;
        if (mark is { } p) _beacon.Place(_m.World, p.x, p.z, _clock);
        // on the big map, the hex the step is about framed
        if (_m.WorldMap is { } map) map.Chosen = _m.AtlasOpen && s.Hex?.Invoke() is { } h ? (h.col, h.row) : (-1, -1);
    }

    // ---------------------------------------------------------------- the valley, for the steps

    Vector2 Aim() { var a = _m.Rig.Aim; return new Vector2(a.x, a.z); }

    /// <summary>The nearest tree to the citadel standing outside the old fort, for the felling and the plot of wood.</summary>
    (int x, int z)? _tree;
    (int x, int z)? TreeNear()
    {
        if (_tree != null && Mat.IsTrunk(_m.World.Get(_tree.Value.x, _m.World.SurfAt(_tree.Value.x, _tree.Value.z), _tree.Value.z))) return _tree;
        var w = _m.World; var site = _m.Site;
        int best = int.MaxValue;
        _tree = null;
        for (int z = site.KeepZ - 170; z <= site.KeepZ + 170; z += 2)
            for (int x = site.KeepX - 220; x <= site.KeepX + 60; x += 2)
            {
                if (x < 2 || z < 2 || x >= w.SX - 2 || z >= w.SZ - 2) continue;
                int dx = x - site.KeepX, dz = z - site.KeepZ, d = dx * dx + dz * dz;
                if (d >= best || d < (site.KeepR + 60) * (site.KeepR + 60)) continue;
                int y = w.SurfAt(x, z);
                if (Mat.IsTrunk(w.Get(x, y, z)) || Mat.IsTrunk(w.Get(x, y + 1, z))) { best = d; _tree = (x, z); }
            }
        return _tree;
    }

    (int x, int z)? Ruin()
    {
        Landmark best = null; long bd = long.MaxValue;
        foreach (var l in _m.Site.Landmarks)
        {
            if (l.Kind != LandmarkKind.Ruin) continue;
            long d = (long)(l.X - _m.Site.KeepX) * (l.X - _m.Site.KeepX) + (long)(l.Z - _m.Site.KeepZ) * (l.Z - _m.Site.KeepZ);
            if (d < bd) { bd = d; best = l; }
        }
        return best == null ? null : (best.X, best.Z);
    }

    /// <summary>The outcrop of ore nearest the citadel with its ore still in it.</summary>
    (int x, int z)? Outcrop()
    {
        (int x, int z)? best = null; long bd = long.MaxValue;
        foreach (var o in _m.Site.Outcrops)
        {
            long d = (long)(o.X - _m.Site.KeepX) * (o.X - _m.Site.KeepX) + (long)(o.Z - _m.Site.KeepZ) * (o.Z - _m.Site.KeepZ);
            if (d < bd && _m.Sim.OreLeft(o.X, o.Z, o.R) >= 40) { bd = d; best = (o.X, o.Z); }
        }
        return best;
    }

    Mine Copper()
    {
        Mine best = null; long bd = long.MaxValue;
        foreach (var m in _m.Sim.Mines)
        {
            if (m.Power) continue;
            long d = (long)(m.Place.X - _m.Site.KeepX) * (m.Place.X - _m.Site.KeepX) + (long)(m.Place.Z - _m.Site.KeepZ) * (m.Place.Z - _m.Site.KeepZ);
            if (d < bd) { bd = d; best = m; }
        }
        return best;
    }

    Unit Beast()
    {
        Unit best = null; float bd = float.MaxValue;
        foreach (var u in _m.Sim.Units)
        {
            if (u.Dead || u.Team != Team.Wild) continue;
            float d = (u.X - _m.Site.KeepX) * (u.X - _m.Site.KeepX) + (u.Z - _m.Site.KeepZ) * (u.Z - _m.Site.KeepZ);
            if (d < bd) { bd = d; best = u; }
        }
        return best;
    }

    (int x, int z)? FortWall()
    {
        var (x0, z0, x1, z1) = _m.Sim.FortArea;
        return x1 > x0 ? (x0 + 3, (z0 + z1) / 2) : (_m.Site.KeepX - _m.Site.KeepR - 20, _m.Site.KeepZ);
    }

    void Look(int x, int z, float zoom = 1.8f)
    {
        var a = _m.Rig.Aim;
        _m.Rig.Set(_m.World.WX(x), _m.World.WZ(z), a.yaw, _m.Rig.TopDown ? a.pitch : .62f, zoom, snap: false);
    }

    bool Chosen(UnitType t) { foreach (var u in _m.Selected) if (!u.Dead && u.T == t) return true; return false; }

    /// <summary>A new marking made since the step began, with the tool in hand now (and the pick or not).</summary>
    bool Marked(int since, params Tool[] tools) => _m.Sim.MarkingsMade > since && Array.IndexOf(tools, _m.Tools.Tool) >= 0;

    int Raiders() { int n = 0; foreach (var u in _m.Sim.Units) if (!u.Dead && u.Team == Team.Horde && !u.Guard && !u.Bound) n++; return n; }

    // ---------------------------------------------------------------- the steps

    void Add(Step s) => _steps.Add(s);
    void Opens(Hud.Part p) => _parts.Add((_steps.Count, p));
    void Opens(Tool t) => _tools.Add((_steps.Count, t));

    void Build()
    {
        var m = _m;
        Vector2 from = default;
        float yaw0 = 0, zoomLo = 1, zoomHi = 1;
        int n0 = 0, h0 = 0, clicks0 = 0;
        bool flag = false, moved = false, turned = false, zoomed = false;
        int learnt0 = 0, house0 = 0;
        Unit prey = null;
        double fought = 0;

        // ---- the view
        const string view = "Камера";
        Add(new Step
        {
            Chapter = view, Title = "Voxel Tower",
            Text = "Ваша цитадель стоит на плато. Орда приходит с запада: в осаде 12 волн, по одной раз в 10 минут. Вы " +
                "размечаете, что строить и копать, а ваши люди это делают.\n\nВ обучении орда не придёт, пока вы не " +
                "дойдёте до боя. Шаг засчитывается, когда задание выполнено. «Пропустить» переходит к следующему.",
        });
        Add(new Step
        {
            Chapter = view, Title = "Камера",
            Text = "{CamForward}{CamLeft}{CamBack}{CamRight} или стрелки двигают вид, правая кнопка мыши тоже. " +
                "{CamTurnLeft} и {CamTurnRight} поворачивают его. Колесо мыши приближает и отдаляет.",
            Task = "Сдвиньте, поверните и приблизьте вид",
            Enter = () => { from = Aim(); yaw0 = m.Rig.Aim.yaw; zoomLo = zoomHi = m.Rig.Aim.zoom; moved = turned = zoomed = false; },
            Done = () =>
            {
                float z = m.Rig.Aim.zoom;
                zoomHi = Math.Max(zoomHi, z); zoomLo = Math.Min(zoomLo, z);
                moved |= (Aim() - from).Length() > 3;
                turned |= MathF.Abs(m.Rig.Aim.yaw - yaw0) > .8f;
                zoomed |= zoomHi / zoomLo > 2.2f;
                return moved && turned && zoomed;
            },
            Status = () => $"{(moved ? "◆" : "◇")} сдвиг  {(turned ? "◆" : "◇")} поворот  {(zoomed ? "◆" : "◇")} приближение",
        });
        Opens(Hud.Part.Map);
        Add(new Step
        {
            Chapter = view, Title = "Вид сверху",
            Text = "{CamTop} или кнопка с картой у миникарты показывает долину сверху: так удобнее размечать по сетке. " +
                "Ещё одно нажатие возвращает обычный вид. {CamHome} или кнопка с башней возвращает вид к крепости.",
            Task = "Посмотрите сверху, потом верните вид к крепости",
            Point = "map:top",
            Enter = () => { flag = false; h0 = m.Rig.Homes; },
            Done = () => { if (m.Rig.TopDown) flag = true; return flag && !m.Rig.TopDown && m.Rig.Homes > h0; },
        });
        Add(new Step
        {
            Chapter = view, Title = "Карта",
            Text = "На карте вся долина. Светлая рамка показывает, что сейчас на экране. Цитадель отмечена ромбом, " +
                "орда приходит слева, с запада. Щелчок по карте переносит туда вид.",
            Task = "Щёлкните по карте",
            Point = "map",
            Enter = () => clicks0 = m.Hud.Map.Clicks,
            Done = () => m.Hud.Map.Clicks > clicks0,
        });

        // ---- the household
        const string folk = "Люди";
        Opens(Hud.Part.Faces);
        Opens(Hud.Part.Console);
        Add(new Step
        {
            Chapter = folk, Title = "Выбрать и послать",
            Text = "Под верхней панелью портреты ваших людей: 4 рабочих, лучник и мечник. Щелчок по портрету выбирает " +
                "человека. {Rmb} по земле посылает выбранных туда. Рабочий дойдёт и вернётся к работе, солдат займёт " +
                "это место как пост.",
            Task = "Выберите рабочего и пошлите его {RmbBy}",
            Point = "faces",
            Enter = () => { n0 = m.Command.Sent; m.Rig.Home(); },
            Done = () => m.Command.Sent > n0,
        });
        Add(new Step
        {
            Chapter = folk, Title = "Отряды",
            Text = "Зажмите левую кнопку и обведите людей рамкой. Shift и щелчок добавляет или убирает одного. Ctrl+1 " +
                "запоминает выбранных как отряд I, Shift+1 выбирает их снова. Отрядов 5. Esc снимает выбор.",
            Task = "Выберите троих и запомните их: Ctrl+1",
            Done = () => m.Command.Squads[0].Count >= 3,
        });
        Add(new Step
        {
            Chapter = folk, Title = "Охота",
            Text = "За каждого убитого зверя дают золото, а на золото нанимают войско. Выберите лучника и щёлкните " +
                "{RmbBy} по зверю.",
            Task = "Пошлите лучника на охоту",
            Point = "faces",
            Mark = () => prey is { Dead: false } b ? ((int)b.X, (int)b.Z) : null,
            Enter = () => { prey = Beast(); if (prey != null) Look((int)prey.X, (int)prey.Z, 1.4f); },
            Done = () => m.Sim.Units.Exists(u => !u.Dead && u.Team == Team.Player && u.Prey is { Dead: false, Team: Team.Wild }) || m.Sim.Stock.Gold > 150,
        });

        // ---- the stores
        const string stores = "Добыча";
        Opens(Hud.Part.Stores);
        _pickAt = _steps.Count;
        Opens(Tool.Line);
        Add(new Step
        {
            Chapter = stores, Title = "Кирка",
            Text = "Вверху запасы: золото, камень, брёвна, земля, металл, вмешательство и места в войске. Кисти не " +
                "строят сами, они размечают работу. Кирка ({Pick}) отмечает, что срубить или выкопать. Возьмите её и " +
                "щёлкните по дереву у маяка.",
            Task = "Кирка, затем щелчок по дереву",
            Point = "pick",
            Mark = TreeNear,
            Enter = () => { n0 = m.Sim.MarkingsMade; if (TreeNear() is { } t) Look(t.x, t.z, 2f); },
            Done = () => m.Sim.MarkingsMade > n0 && m.Sim.LastMarking?.What == "Срубить дерево",
        });
        Opens(Tool.Gather);
        _matsAt = _steps.Count;
        Add(new Step
        {
            Chapter = stores, Title = "Делянка",
            Text = "Каждое дерево размечать не нужно. Возьмите делянку ({ToolGather}), выберите брёвна ({MatWood}) и " +
                "обведите лес рамкой. Свободные рабочие будут рубить там лес, когда нет работы важнее.",
            Task = "Делянка на лес: {ToolGather}, {MatWood}, рамка",
            Point = "tool:Gather",
            Mark = TreeNear,
            Done = () => m.Sim.Zones.Exists(z => z.Kind == Stockpile.Wood),
        });
        Add(new Step
        {
            Chapter = stores, Title = "Камень и руда",
            Text = "Делянка с камнем ({MatStone}) на скале или руинах даёт камень. Делянка с металлом ({MatMetal}) на " +
                "ржавой скале становится рудником: рабочие роют до руды. Маяк стоит у руин. Esc или {Rmb} убирает кисть.",
            Task = "Делянка на камень или руду",
            Point = "mat:" + Mat.Stone,
            Mark = () => Ruin() ?? Outcrop(),
            Enter = () => { if ((Ruin() ?? Outcrop()) is { } r) Look(r.x, r.z, 1.6f); },
            Done = () => m.Sim.Zones.Exists(z => z.Kind is Stockpile.Stone or Stockpile.Metal),
        });

        // ---- the old fort
        const string mend = "Починка";
        Opens(Tool.Repair);
        Add(new Step
        {
            Chapter = mend, Title = "Старая крепость",
            Text = "Старая крепость наполовину разрушена. Возьмите починку ({ToolRepair}) и щёлкните по стене: " +
                "рабочие уберут завалы и восстановят кладку. Рамкой можно чинить по частям.",
            Task = "Почините крепость: {ToolRepair} и щелчок по стене",
            Point = "tool:Repair",
            Mark = FortWall,
            Enter = () => { n0 = m.Sim.MarkingsMade; m.Rig.Home(); },
            Done = () => Marked(n0, Tool.Repair),
        });
        Opens(Hud.Part.Goals);
        Add(new Step
        {
            Chapter = mend, Title = "Задачи и летопись",
            Text = "Во вкладке «Задачи» сверху поручения: одно главное и два малых, за каждое награда. Ниже цели осады, " +
                "ещё ниже летопись всех событий. Щелчок по строке со значком ◎ переносит вид к событию.",
            Task = "Откройте «Задачи»",
            Point = "tab:goals",
            Done = () => m.Hud.OpenTab == "goals",
        });

        // ---- the brushes
        const string build = "Стройка";
        Add(new Step
        {
            Chapter = build, Title = "Линия",
            Text = "Линия ({ToolLine}): зажмите левую кнопку и протяните, люди сложат стену. Жёлтый контур значит, что " +
                "запасов хватит, красный значит, что нет. Толщину меняют {WidthLess} и {WidthMore}, высоту {HeightLess} " +
                "и {HeightMore}. Камень ({MatStone}) прочнее всего. Брёвна ({MatWood}) строятся быстрее, но горят. Земля " +
                "({MatEarth}) кладётся быстрее всего.",
            Task = "Проведите стену линией",
            Point = "tool:Line",
            Enter = () => n0 = m.Sim.MarkingsMade,
            Done = () => Marked(n0, Tool.Line) && !m.Tools.Opt.Dig,
        });
        Add(new Step
        {
            Chapter = build, Title = "Сетка",
            Text = "Сетка ({Grid}) выравнивает стройку: концы линий встают в узлы, стены идут вдоль осей или под 45°. " +
                "Шаг сетки выбирают в нижней панели. С сеткой удобнее работать в виде сверху ({CamTop}).",
            Task = "Включите сетку и проведите линию по ней",
            Point = "console",
            Enter = () => { n0 = m.Sim.MarkingsMade; if (m.Tools.Tool == Tool.None) m.Tools.SetTool(Tool.Line); },
            Done = () => m.Tools.Opt.Grid > 0 && Marked(n0, Tool.Line, Tool.Box, Tool.Circle),
        });
        Opens(Tool.Box); Opens(Tool.Circle);
        Add(new Step
        {
            Chapter = build, Title = "Прямоугольник и круг",
            Text = "Прямоугольник ({ToolBox}) тянут от угла до угла, круг ({ToolCircle}) от центра к краю. {Form} " +
                "переключает сплошную фигуру и одни стенки.",
            Task = "Разметьте прямоугольник или круг",
            Point = "tool:Circle",
            Enter = () => n0 = m.Sim.MarkingsMade,
            Done = () => Marked(n0, Tool.Box, Tool.Circle),
        });
        Add(new Step
        {
            Chapter = build, Title = "Отменить",
            Text = "Ctrl+Z (на Mac ⌘Z) отменяет последнюю разметку, до 40 шагов назад. Уже построенное остаётся.",
            Task = "Отмените последнюю разметку",
            Enter = () => n0 = m.Sim.MarkingsUndone,
            Done = () => m.Sim.MarkingsUndone > n0,
        });
        Add(new Step
        {
            Chapter = build, Title = "Ров",
            Text = "С киркой ({Pick}) те же кисти размечают раскопки: линия даёт ров, прямоугольник котлован. Вынутая " +
                "земля идёт в запасы. Ров, доведённый до пруда, наполнится водой.",
            Task = "Разметьте ров: кирка и линия",
            Enter = () => n0 = m.Sim.MarkingsMade,
            Done = () => Marked(n0, Tool.Line, Tool.Box, Tool.Circle, Tool.Brush) && m.Tools.Opt.Dig,
        });
        Opens(Tool.Unmark);
        Add(new Step
        {
            Chapter = build, Title = "Снять разметку",
            Text = "{ToolUnmark} снимает разметку: рамкой всё внутри, щелчком то, что рядом. Сделанное остаётся.",
            Task = "Снимите разметку",
            Point = "tool:Unmark",
            Enter = () => n0 = m.Sim.MarkingsMade,
            Done = () => Marked(n0, Tool.Unmark),
        });
        Opens(Tool.Ladder); Opens(Tool.Gate); Opens(Tool.Bridge);
        Add(new Step
        {
            Chapter = build, Title = "Лестница, ворота, мост",
            Text = "Лестница ({ToolLadder}) ставится щелчком по стене, по ней люди поднимаются наверх. Ворота " +
                "({ToolGate}) пропускают своих, а орде их надо прорубить. Ворота в стене пруда работают как затвор: " +
                "откройте их, и вода собьёт орду с ног. Мост ({ToolBridge}) тянут от берега до берега.",
            Task = "Приставьте лестницу к стене",
            Point = "tool:Ladder",
            Mark = FortWall,
            Enter = () => n0 = m.Sim.MarkingsMade,
            Done = () => Marked(n0, Tool.Ladder, Tool.Gate, Tool.Bridge),
        });
        Opens(Tool.Brush); Opens(Tool.Extrude); Opens(Tool.Capture); Opens(Tool.Wand);
        Add(new Step
        {
            Chapter = build, Title = "Кисть и чертёж",
            Text = "Кисть ({ToolBrush}) рисует стену от руки. Выдавливание ({ToolExtrude}) наращивает или углубляет " +
                "грань: так делают ступени в скале. {ToolCapture} сохраняет постройку как чертёж. Волшебная палочка " +
                "({ToolWand}) выделяет всё прилегающее из одного материала, чтобы снести или переложить постройку целиком.",
            Point = "tool:Capture",
        });
        Opens(Tool.Template);
        Add(new Step
        {
            Chapter = build, Title = "Шаблоны",
            Text = "Шаблоны ({ToolTemplate}): готовые башни, стены с зубцами, частокол, надвратная башня, вышка, амбар, " +
                "лестница и настил. Выберите шаблон в нижней панели и щёлкните по земле, рабочие построят. {Rotate} " +
                "поворачивает. В разделе «Декор» костры, скамьи, колодец и другие вещи для поселения.",
            Task = "Поставьте шаблон",
            Point = "tool:Template",
            Enter = () => n0 = m.Sim.MarkingsMade,
            Done = () => Marked(n0, Tool.Template),
        });

        // ---- the order of the work
        const string work = "Работы";
        Opens(Hud.Part.Works);
        Add(new Step
        {
            Chapter = work, Title = "Работы",
            Text = "Во вкладке «Работы» задают, кто какую работу делает и в каком порядке: 1 в первую очередь, 4 в " +
                "последнюю, пусто значит никогда.",
            Task = "Откройте «Работы»",
            Point = "tab:works",
            Done = () => m.Hud.OpenTab == "works",
        });

        // ---- the time
        const string time = "Время";
        Opens(Hud.Part.Clock);
        Add(new Step
        {
            Chapter = time, Title = "Часы",
            Text = "Часы показывают, сколько осталось до орды. В обучении они стоят. {Pause} ставит паузу, размечать " +
                "можно и на паузе. {Speed} меняет скорость: ×1, ×2, ×3.",
            Task = "Поставьте паузу и смените скорость",
            Point = "clock",
            Enter = () => { flag = false; m.Hud.ClosePanel(); },
            Done = () => { if (m.Paused) flag = true; return flag && m.Speed > 1 && !m.Paused; },
        });
        Add(new Step
        {
            Chapter = time, Title = "Рудник",
            Text = "Рудник приносит золото, пока он ваш. Медный рудник отмечен маяком. Пошлите людей к входу, и через " +
                "несколько секунд он станет вашим. Оставьте охрану: орда попробует его отбить.",
            Task = "Займите Медный рудник",
            Mark = () => Copper() is { } c ? (c.Place.StandX > 0 ? c.Place.StandX : c.Place.X, c.Place.StandZ > 0 ? c.Place.StandZ : c.Place.Z) : null,
            Enter = () => { if (Copper() is { } c) Look(c.Place.X, c.Place.Z, 1.3f); },
            Done = () => Copper() is not { } c || c.Holder == Holder.Ours,
            Status = () => Copper() is { } c && c.Ours > 0 ? $"у рудника ваших: {c.Ours}" : null,
        });

        // ---- the horde
        const string horde = "Орда";
        Opens(Hud.Part.Horde);
        Add(new Step
        {
            Chapter = horde, Title = "Орда",
            Text = "Орда приходит ватагами, у каждой свой вожак. Во вкладке «Орда» видно, кто придёт и откуда. Линии на " +
                "земле показывают их пути ({Trails} скрывает их). Орда обходит стены, а если обхода нет, ломает их или " +
                "лезет по лестницам.",
            Task = "Откройте «Орду»",
            Point = "tab:horde",
            Enter = () => { if (!m.Sim.WantTrails) m.Command.ToggleTrails(); },
            Done = () => m.Hud.OpenTab == "horde",
        });
        Add(new Step
        {
            Chapter = horde, Title = "Тревога",
            Text = "По тревоге ({Alarm}) рабочие уходят в цитадель и убежища, а солдаты встают на посты. Когда трубит " +
                "рог орды, тревога поднимается сама.",
            Task = "Поднимите тревогу",
            Point = "console",
            Done = () => m.Sim.Alarm,
        });
        Add(new Step
        {
            Chapter = horde, Title = "Учебная ватага",
            Text = "С запада идут трое налётчиков. Лучник стреляет сам, мечник защищает свой пост. Чтобы приказать " +
                "атаковать, выберите солдат и щёлкните {RmbBy} по врагу. Ожидание ускоряет {Speed}.",
            Task = "Перебейте ватагу",
            Point = "faces",
            Enter = () =>
            {
                var sim = m.Sim;
                fought = _clock;
                if (sim.Raids.Active || sim.Raids.Count > 0) return;
                sim.Waves.StartNow(sim);
                // the lesson's band: the first three of the wave
                var keep = new List<(UnitType, Warband, Survivor)>();
                while (sim.Waves._queue.Count > 0 && keep.Count < 3) keep.Add(sim.Waves._queue.Dequeue());
                sim.Waves._queue.Clear();
                foreach (var e in keep) sim.Waves._queue.Enqueue(e);
            },
            Done = () => !m.Sim.Raids.Active && m.Sim.Raids.Count > 0,
            // a raider the posts do not reach is to be gone after: told so once the fight has gone on a while
            Status = () => !m.Sim.Raids.Active ? null : $"налётчиков в долине: {Raiders() + m.Sim.Waves.Coming}" +
                (_clock - fought > 75 ? Bindings.Fill(". Выберите солдат (портреты вверху) и щёлкните {RmbBy} по налётчику") : ""),
        });
        Add(new Step
        {
            Chapter = horde, Title = "Отбой",
            Text = "Волна отбита. После волны тревога снимается сама, а поднятую вручную снимает {Alarm}. За каждую " +
                "отбитую волну дают знания и золото.",
            Task = "Тревоги нет",
            Done = () => !m.Sim.Alarm,
        });

        // ---- the muster, the lore and the town
        const string lore = "Войско и город";
        Add(new Step
        {
            Chapter = lore, Title = "Цитадель",
            Text = "В цитадели ({Keep} или щелчок по ней) нанимают войско за золото. Нанятый сразу выходит из цитадели. " +
                "Сколько мест в войске, видно на верхней панели справа.",
            Task = "Наймите кого-нибудь",
            Point = "console",
            Enter = () => { house0 = m.Sim.Household(); if (m.Sim.Stock.Gold < 200) m.Sim.Stock.Gold = 200; },
            Done = () => m.Sim.Household() > house0,
        });
        Opens(Hud.Part.Lore);
        Add(new Step
        {
            Chapter = lore, Title = "Знания",
            Text = "Во вкладке «Знания» древо: «Город», «Войско» и «Вмешательство». Город и войско открывают здания. " +
                "Знания дают за отбитые волны, сначала их мало: выбирайте, что строить первым.",
            Task = "Изучите что-нибудь",
            Point = "tab:lore",
            Enter = () => { learnt0 = m.Sim.Learnt.Count; if (m.Sim.Knowledge < 3) m.Sim.Knowledge = 3; },
            Done = () => m.Sim.Learnt.Count > learnt0,
        });
        Opens(Hud.Part.Town);
        Add(new Step
        {
            Chapter = lore, Title = "Город",
            Text = "Изученное здание ставят во вкладке «Город»: выберите его и щёлкните по земле, рабочие построят. " +
                "Готовое здание нанимает войска и надстраивается вторым этажом. Там же видно уровень поселения и что " +
                "строить дальше.",
            Task = "Откройте вкладку «Город»",
            Point = "tab:town",
            Done = () => m.Hud.OpenTab == "town",
        });
        int dwellings0 = 0;
        Opens(Tool.Yard);
        Add(new Step
        {
            Chapter = lore, Title = "Жилой дом",
            Text = "Мест в войске столько, сколько лежанок: их дают цитадель и жилые дома. Возьмите прямоугольник " +
                "({ToolBox}) и в нижней панели выберите заливку «дом» ({Form} переключает). Рабочие построят стены, пол, " +
                "крышу и дверь, потом поставят лежанки. Дома можно ставить вплотную. Дорожки, костры и скамьи ставит " +
                "«Обустройство» ({ToolYard}).",
            Task = "Нарисуйте дом прямоугольником",
            Point = "tool:Box",
            Enter = () => dwellings0 = m.Sim.Zones.FindAll(z => z.Kind == Zone.Dwelling).Count,
            Done = () => m.Sim.Zones.FindAll(z => z.Kind == Zone.Dwelling).Count > dwellings0,
        });
        Add(new Step
        {
            Chapter = lore, Title = "Командир и архитектор",
            Text = "Командир сам ставит бойцов к бою и подаёт призывы. Архитектор нанимается во дворе каменщиков. Он " +
                "сам размечает стену, ворота, башни и ров вокруг цитадели или рудника и чинит их после боя. Рабочие " +
                "строят по его разметке.",
        });
        Opens(Hud.Part.Magic);
        Add(new Step
        {
            Chapter = lore, Title = "Таинства",
            Text = "Жрецы молятся в святилище и копят вмешательство, а на него вы колдуете. Святилище размечают " +
                "делянкой. На месте силы молитва даёт втрое больше. Всё это на странице «Таинства» во вкладке «Знания».",
            Task = "Откройте в «Знаниях» страницу «Таинства»",
            Point = "tab:lore",
            Done = () => m.Hud.OpenTab == "magic",
        });

        // ---- the eyes of one of ours
        const string eyes = "От первого лица";
        Add(new Step
        {
            Chapter = eyes, Title = "Вселиться",
            Text = "Выберите мечника и нажмите {Possess}, чтобы управлять им от первого лица. " +
                "{FpForward}{FpLeft}{FpBack}{FpRight} — идти, {FpRun} — бежать, мышь — смотреть, {Lmb} — " +
                "удар, держать — тяжёлый удар, {Rmb} — блок, {FpKick} — пинок.",
            Task = "Вселитесь в мечника",
            Point = "faces",
            Done = () => m.FP.On,
        });
        Add(new Step
        {
            Chapter = eyes, Title = "Выйти",
            Text = "{FpSwitch} — войти в другого из своих. В командире {FpStone}–{FpFourth} — призывы, {FpOrder} — " +
                "приказ взглядом. Esc возвращает обычный вид.",
            Task = "Выйдите: Esc",
            Done = () => !m.FP.On,
        });

        // ---- the last of it
        const string last = "Меню";
        Add(new Step
        {
            Chapter = last, Title = "Меню и управление",
            Text = "Esc открывает меню: сохранение, загрузка, настройки. В разделе «Управление» (кнопка «?» справа " +
                "вверху, {Help}) все клавиши, любую можно переназначить. Держите Alt, и на кнопках появятся их клавиши. " +
                "Быстрое сохранение {QuickSave}, загрузка {QuickLoad}.",
            Task = "Откройте «Управление»",
            Point = "help",
            Done = () => m.Hud.MenuPage == "controls",
        });
        Add(new Step
        {
            Chapter = last, Title = "Долина пройдена",
            Text = "Дальше осада: 12 волн раз в 10 минут, каждая сильнее прежней. {CallWave} зовёт орду раньше срока.\n\n" +
                "Вторая часть обучения про кампанию: большая карта, герои, рудники и гарнизоны, существа и орда со " +
                "своими героями и городами. Её можно пройти сейчас или потом из меню.",
        });
    }

    // ---------------------------------------------------------------- the card

    partial class Card : Control
    {
        readonly Tutorial _t;
        readonly Label _chapter, _title, _body, _task, _status;
        readonly HBoxContainer _buttons;
        readonly VBoxContainer _col;
        readonly MarginContainer _box;
        public const float W = 410;

        public Card() { }

        public Card(Tutorial t)
        {
            _t = t;
            MouseFilter = MouseFilterEnum.Stop;
            _box = new MarginContainer();
            foreach (var side in new[] { "margin_left", "margin_right" }) _box.AddThemeConstantOverride(side, 20);
            foreach (var side in new[] { "margin_top", "margin_bottom" }) _box.AddThemeConstantOverride(side, 16);
            AddChild(_box);
            _col = new VBoxContainer { CustomMinimumSize = new Vector2(W - 40, 0) };
            _col.AddThemeConstantOverride("separation", 7);
            _box.AddChild(_col);
            _chapter = Hud.CapsLabel("", 9);
            _col.AddChild(_chapter);
            _title = Hud.Text("", Ornament.Display, 22, Ornament.EmberGlow);
            _col.AddChild(_title);
            _body = Hud.Text("", Ornament.Serif, 14, Ornament.Parchment, wrap: true);
            _body.CustomMinimumSize = new Vector2(W - 40, 0);
            _col.AddChild(_body);
            _task = Hud.Text("", Ornament.SerifBold, 14, Ornament.BronzeLight, wrap: true);
            _task.CustomMinimumSize = new Vector2(W - 40, 0);
            _col.AddChild(_task);
            _status = Hud.Text("", Ornament.SerifItalic, 13, Ornament.Aged);
            _col.AddChild(_status);
            _buttons = new HBoxContainer();
            _buttons.AddThemeConstantOverride("separation", 8);
            _col.AddChild(_buttons);
        }

        static Button Btn(string text, string tip, Action act, bool strong = false)
        {
            var b = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 30), TooltipText = tip, MouseDefaultCursorShape = CursorShape.PointingHand };
            b.AddThemeFontOverride("font", strong ? Ornament.SerifBold : Ornament.Serif);
            b.AddThemeFontSizeOverride("font_size", 14);
            b.Pressed += act;
            return b;
        }

        public void Show(Step s, int i, int count, (int n, int of, string name) chapter)
        {
            _chapter.Text = $"{(_t._campaign ? "обучение кампании" : "обучение")} · глава {chapter.n} из {chapter.of} · {chapter.name} · шаг {i + 1} из {count}".ToUpperInvariant();
            _title.Text = s.Title;
            _body.Text = Bindings.Fill(s.Text);
            _task.Visible = s.Task != null;
            _task.Text = s.Task != null ? "◇ " + Bindings.Fill(s.Task) : "";
            _task.AddThemeColorOverride("font_color", Ornament.BronzeLight);
            _status.Text = "";
            _status.Visible = false;
            foreach (var c in _buttons.GetChildren()) c.QueueFree();
            bool last = i == count - 1;
            if (i > 0 && !last) _buttons.AddChild(Btn("‹", "Предыдущий шаг", _t.Back));
            if (last && _t._campaign)
            {
                _buttons.AddChild(Btn("Играть дальше", "Обучение окончено, кампания идёт дальше.", _t.Leave, true));
                _buttons.AddChild(Btn("Новая кампания", "Меню кампаний: размер карты и сложность орды.", () => { _t.Leave(); _t._m.Hud.OpenMenu("campaign"); }));
            }
            else if (last)
            {
                _buttons.AddChild(Btn("Обучение кампании", "Вторая часть обучения: большая карта, герои, рудники, гарнизоны и орда.", () => _t._m.StartCampaignTutorial(), true));
                _buttons.AddChild(Btn("Новая игра", "Начать осаду в новой долине.", () => _t._m.NewGame()));
                _buttons.AddChild(Btn("Остаться здесь", "Играть дальше в этой долине. Орда придёт через 10 минут.", _t.Leave));
            }
            else if (s.Done == null) _buttons.AddChild(Btn("Дальше", "", _t.Next, true));
            else _buttons.AddChild(Btn("Пропустить", "Перейти к следующему шагу", _t.Next));
            if (!last)
            {
                var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                _buttons.AddChild(spacer);
                _buttons.AddChild(Btn("Выйти из обучения", "Играть в этой долине без обучения. Орда придёт через 10 минут.", _t.Leave));
            }
        }

        public void Done()
        {
            _task.Text = "◆ " + _task.Text.TrimStart('◇', ' ');
            _task.AddThemeColorOverride("font_color", Ornament.Moss);
        }

        public void Status(string text)
        {
            _status.Visible = !string.IsNullOrEmpty(text);
            if (_status.Visible && _status.Text != text) _status.Text = text;
        }

        public override void _Process(double delta)
        {
            using var prof = Prof.Time("Tutorial2");
            var vp = GetViewportRect().Size;
            Size = _box.GetCombinedMinimumSize();
            _box.Size = Size;
            // under the clock at the right, clear of the faces and of the console; on the big map at the left, clear of
            // its panel at the right and its small map at the bottom
            Position = _t._m.AtlasOpen ? new Vector2(14, 58)
                : new Vector2(vp.X - Size.X - 14, Math.Min(WaveClock.H + 14, Math.Max(10, vp.Y - BottomConsole.Band - Size.Y - 10)));
            Visible = !_t._m.Hud.MenuOpen;
        }

        public override void _Draw() => Craft.Plate(this, new Rect2(Vector2.Zero, Size));
    }

    // ---------------------------------------------------------------- what it points at

    /// <summary>A bronze frame breathing round the part of the frame the step is about, a notch above it pointing down.</summary>
    partial class Pointer : Control
    {
        public Control Target;
        double _t;

        public Pointer()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        }

        public override void _Process(double delta)
        {
            using var prof = Prof.Time("Tutorial3");
            _t += delta;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (Target == null || !IsInstanceValid(Target) || !Target.IsVisibleInTree()) return;
            var r = Target.GetGlobalRect();
            r.Position -= GetGlobalPosition();
            float k = .5f + .5f * MathF.Sin((float)_t * 5);
            var grown = r.Grow(4 + 3 * k);
            var glow = new Color(Ornament.EmberGlow, .35f + .4f * k);
            DrawRect(grown.Grow(2), new Color(0, 0, 0, .35f), false, 4);
            DrawRect(grown, glow, false, 2.5f);
            // a notch pointing at it from above, or from below when it is at the top of the screen
            bool below = r.Position.Y < 120;
            float x = r.GetCenter().X, y = below ? grown.End.Y + 8 + 4 * k : grown.Position.Y - 8 - 4 * k, d = below ? 1 : -1;
            DrawColoredPolygon(new[] { new Vector2(x, y), new Vector2(x - 9, y + 12 * d), new Vector2(x + 9, y + 12 * d) }, glow);
        }
    }

    /// <summary>A pillar of ember light standing in the valley where the step is, a ring breathing on the ground at its foot.</summary>
    partial class Beacon : Node3D
    {
        readonly MeshInstance3D _pillar, _ring;

        public Beacon()
        {
            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(Ornament.EmberGlow, .45f), CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            float v = VoxelWorld.Vox;
            _pillar = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .6f * v, BottomRadius = 1.6f * v, Height = 40 * v, RadialSegments = 12 }, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(_pillar);
            var ringMat = (StandardMaterial3D)mat.Duplicate();
            ringMat.AlbedoColor = new Color(Ornament.EmberGlow, .8f);
            _ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 5.4f * v, OuterRadius = 6.4f * v, Rings = 32, RingSegments = 4 }, MaterialOverride = ringMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(_ring);
            Visible = false;
        }

        public void Place(VoxelWorld w, int x, int z, double t)
        {
            float v = VoxelWorld.Vox, k = .5f + .5f * MathF.Sin((float)t * 3);
            var foot = new Vector3(w.WX(x + .5f), w.WY(w.SurfAt(x, z)), w.WZ(z + .5f));
            _pillar.Position = foot + new Vector3(0, 20 * v, 0);
            _ring.Position = foot + new Vector3(0, .4f * v, 0);
            _ring.Scale = Vector3.One * (1 + .25f * k);
        }
    }
}
