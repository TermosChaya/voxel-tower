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
/// the forenoon. At the end the player starts a siege of his own or stays in this valley, its clock let go.
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
    }

    Main _m;
    readonly List<Step> _steps = new();
    int _at = -1;
    double _clock, _doneAt = -1;
    bool _over;
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

    public void Init(Main m, int from = 0)
    {
        _m = m;
        Build();
        _m.Hud.Conceal();
        _card = new Card(this);
        _m.Hud.Mount(_card);
        _pointer = new Pointer();
        _m.Hud.Mount(_pointer);
        _beacon = new Beacon();
        _m.AddChild(_beacon);
        // the horde's ways are shown when the lesson comes to the horde
        _m.Sim.WantTrails = false;
        Go(Math.Clamp(from, 0, _steps.Count - 1));
    }

    // ---------------------------------------------------------------- going through

    void Go(int i)
    {
        _at = i;
        _doneAt = -1;
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
        if (!_m.Sim.Waves.Active) _m.Sim.Waves.Countdown = Waves.Prep;
        _m.Hud.Log("Обучение окончено. Орда придёт через 10 минут.", Hud.Kind.Warn);
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
        // the horde keeps away while the lesson is not about it
        if (!sim.Waves.Active) { sim.Waves.Countdown = HoldAt; sim.Waves.DawnLeft = Math.Min(sim.Waves.DawnLeft, 2); }
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
        _beacon.Visible = mark != null && !_m.FP.On;
        if (mark is { } p) _beacon.Place(_m.World, p.x, p.z, _clock);
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
        bool flag = false;
        int width0 = 0, height0 = 0;
        int learnt0 = 0, house0 = 0;
        Unit prey = null;
        double fought = 0;

        // ---- the view
        const string view = "Камера";
        Add(new Step
        {
            Chapter = view, Title = "Voxel Tower",
            Text = "Ваша цитадель стоит на плато. Каждые 10 минут с запада приходит орда, всего 12 волн. Между " +
                "волнами вы размечаете, что строить и копать, а дружина это делает.\n\nВ обучении орда не придёт, " +
                "пока вы не дойдёте до боя. Шаг засчитывается, когда задание выполнено. «Пропустить» переходит к " +
                "следующему шагу.",
        });
        Add(new Step
        {
            Chapter = view, Title = "Сдвинуть вид",
            Text = "{CamForward}{CamLeft}{CamBack}{CamRight} или стрелки двигают вид. Ещё можно тянуть землю правой " +
                "кнопкой мыши.",
            Task = "Сдвиньте вид в сторону",
            Enter = () => from = Aim(),
            Done = () => (Aim() - from).Length() > 3,
        });
        Add(new Step
        {
            Chapter = view, Title = "Повернуть",
            Text = "{CamTurnLeft} и {CamTurnRight} поворачивают вид. Средней кнопкой мыши (или Alt и левой) вид можно " +
                "повернуть и наклонить.",
            Task = "Поверните вид",
            Enter = () => yaw0 = m.Rig.Aim.yaw,
            Done = () => MathF.Abs(m.Rig.Aim.yaw - yaw0) > .8f,
        });
        Add(new Step
        {
            Chapter = view, Title = "Ближе и дальше",
            Text = "Колесо мыши приближает и отдаляет вид. На тачпаде для этого щипок или два пальца вверх и вниз.",
            Task = "Приблизьте вид, потом отдалите",
            Enter = () => { zoomLo = zoomHi = m.Rig.Aim.zoom; flag = false; },
            Done = () =>
            {
                float z = m.Rig.Aim.zoom;
                zoomHi = Math.Max(zoomHi, z); zoomLo = Math.Min(zoomLo, z);
                return zoomHi / zoomLo > 2.2f;
            },
        });
        Opens(Hud.Part.Map);
        Add(new Step
        {
            Chapter = view, Title = "Вид сверху",
            Text = "{CamTop} или кнопка с картой у миникарты показывает долину сверху. Так удобнее размечать стройку " +
                "по сетке.",
            Task = "Посмотрите сверху",
            Point = "map:top",
            Done = () => m.Rig.TopDown,
        });
        Add(new Step
        {
            Chapter = view, Title = "Вернуть вид",
            Text = "Ещё одно нажатие {CamTop} возвращает обычный вид. {CamHome} или кнопка с башней возвращает вид к " +
                "крепости.",
            Task = "Верните вид к крепости",
            Point = "map:home",
            Enter = () => h0 = m.Rig.Homes,
            Done = () => !m.Rig.TopDown && m.Rig.Homes > h0,
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
        const string folk = "Дружина";
        Opens(Hud.Part.Faces);
        Add(new Step
        {
            Chapter = folk, Title = "Лица дружины",
            Text = "Под верхней панелью портреты вашей дружины: 4 рабочих, лучник и мечник. Щелчок по портрету " +
                "выбирает человека, второй щелчок показывает его на поле.",
            Task = "Выберите рабочего",
            Point = "faces",
            Enter = () => m.Rig.Home(),
            Done = () => Chosen(Kinds.Worker),
        });
        Opens(Hud.Part.Console);
        Add(new Step
        {
            Chapter = folk, Title = "Послать",
            Text = "В нижней панели видно, кто выбран и чем занят. Правая кнопка по земле посылает выбранных туда. " +
                "Рабочий дойдёт и сам вернётся к работе, а посланный на делянку будет работать на ней. Солдат " +
                "займёт это место как пост.",
            Task = "Пошлите выбранного правой кнопкой",
            Point = "console",
            Enter = () => n0 = m.Command.Sent,
            Done = () => m.Command.Sent > n0,
        });
        Add(new Step
        {
            Chapter = folk, Title = "Рамкой",
            Text = "Зажмите левую кнопку и обведите рамкой нескольких человек. Shift и щелчок добавляет или убирает " +
                "одного.",
            Task = "Выберите троих или больше",
            Done = () => m.Selected.Count >= 3,
        });
        Add(new Step
        {
            Chapter = folk, Title = "Отряд",
            Text = "Ctrl+1 запоминает выбранных как отряд I, Shift+1 выбирает их снова, двойное нажатие показывает " +
                "их. Отрядов 5: Ctrl+1…5.",
            Task = "Запомните выбранных: Ctrl+1",
            Done = () => m.Command.Squads[0].Count > 0,
        });
        Add(new Step
        {
            Chapter = folk, Title = "Снять выбор",
            Text = "Esc снимает выбор. Если ничего не выбрано, Esc открывает меню. Потом нажмите Shift+1, и отряд " +
                "снова будет выбран.",
            Task = "Esc, затем Shift+1",
            Enter = () => flag = false,
            Done = () =>
            {
                if (m.Selected.Count == 0) flag = true;
                return flag && m.Selected.Count > 0 && m.Selected.SetEquals(m.Command.Squads[0]);
            },
        });
        Add(new Step
        {
            Chapter = folk, Title = "Охота",
            Text = "За каждого убитого зверя дают золото, а на золото набирают войско. Выберите лучника и щёлкните " +
                "правой кнопкой по зверю.",
            Task = "Пошлите лучника на охоту",
            Point = "faces",
            Mark = () => prey is { Dead: false } b ? ((int)b.X, (int)b.Z) : null,
            Enter = () => { prey = Beast(); if (prey != null) Look((int)prey.X, (int)prey.Z, 1.4f); },
            Done = () => m.Sim.Units.Exists(u => !u.Dead && u.Team == Team.Player && u.Prey is { Dead: false, Team: Team.Wild }) || m.Sim.Stock.Gold > 150,
        });

        // ---- the stores
        const string stores = "Добыча";
        Opens(Hud.Part.Stores);
        Add(new Step
        {
            Chapter = stores, Title = "Запасы",
            Text = "Вверху показаны запасы: золото, камень, брёвна, земля (в м³), вмешательство и места в войске. " +
                "Стройка тратит запасы, рабочие их пополняют.",
            Point = "stores",
        });
        _pickAt = _steps.Count;
        Opens(Tool.Line);
        Add(new Step
        {
            Chapter = stores, Title = "Кирка",
            Text = "Кисти не строят сами, они только размечают работу. Кирка ({Pick} или ячейка с киркой справа " +
                "внизу) отмечает, что срубить или выкопать. Возьмите её и щёлкните по дереву у маяка. Рабочие " +
                "придут и срубят его.",
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
                "обведите лес рамкой. Свободные рабочие будут понемногу рубить там лес, но стены и рвы делают в " +
                "первую очередь.",
            Task = "Делянка на лес: {ToolGather}, {MatWood}, рамка",
            Point = "tool:Gather",
            Mark = TreeNear,
            Enter = () => { n0 = m.Sim.Zones.Count; },
            Done = () => m.Sim.Zones.Exists(z => z.Kind == Stockpile.Wood),
        });
        Add(new Step
        {
            Chapter = stores, Title = "Каменоломня",
            Text = "Камень добывают так же: делянка с камнем ({MatStone}) на скале, валунах или руинах. Глубину " +
                "задают в нижней панели. Руины есть у маяка.",
            Task = "Делянка на камень: {ToolGather}, {MatStone}, рамка",
            Point = "mat:" + Mat.Stone,
            Mark = Ruin,
            Enter = () => { if (Ruin() is { } r) Look(r.x, r.z, 1.6f); },
            Done = () => m.Sim.Zones.Exists(z => z.Kind == Stockpile.Stone),
        });
        Add(new Step
        {
            Chapter = stores, Title = "Убрать кисть",
            Text = "Esc или правая кнопка мыши убирает кисть. Без кисти левая кнопка снова выбирает людей.",
            Task = "Уберите кисть",
            Done = () => m.Tools.Tool == Tool.None,
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
            Chapter = mend, Title = "Задачи",
            Text = "Во вкладке «Задачи» написано, чем заняться. В обычной игре сверху «Поручения»: одно главное и два " +
                "малых, за каждое дают награду. Ниже цели на всю осаду. Число на вкладке показывает, сколько целей " +
                "осталось.",
            Task = "Откройте «Задачи»",
            Point = "tab:goals",
            Done = () => m.Hud.OpenTab == "goals",
        });

        // ---- the brushes
        const string build = "Стройка";
        Add(new Step
        {
            Chapter = build, Title = "Линия",
            Text = "Линия ({ToolLine}): зажмите левую кнопку и протяните. Рабочие сложат прямую стену. Жёлтый контур " +
                "значит, что запасов хватит, красный — что не хватит.",
            Task = "Проведите стену линией",
            Point = "tool:Line",
            Enter = () => n0 = m.Sim.MarkingsMade,
            Done = () => Marked(n0, Tool.Line) && !m.Tools.Opt.Dig,
        });
        Add(new Step
        {
            Chapter = build, Title = "Толщина и высота",
            Text = "Толщину меняют {WidthLess} и {WidthMore}, высоту {HeightLess} и {HeightMore}, или кнопки − и + " +
                "внизу. {Level} переключает верх стены: ровный или по рельефу.",
            Task = "Измените толщину или высоту",
            Point = "console",
            Enter = () => { width0 = m.Tools.Opt.Width; height0 = m.Tools.Opt.Height; if (m.Tools.Tool == Tool.None) m.Tools.SetTool(Tool.Line); },
            Done = () => m.Tools.Opt.Width != width0 || m.Tools.Opt.Height != height0,
        });
        Add(new Step
        {
            Chapter = build, Title = "Сетка",
            Text = "Сетка ({Grid}) выравнивает стройку: концы линий встают в узлы, стены идут вдоль осей или под 45°. " +
                "Шаг сетки (50 см, 1 м, 2 м) выбирают внизу. Линии через центр цитадели ярче. С сеткой удобнее " +
                "работать в виде сверху ({CamTop}).",
            Task = "Включите сетку и проведите линию по ней",
            Point = "console",
            Enter = () => { n0 = m.Sim.MarkingsMade; if (m.Tools.Tool == Tool.None) m.Tools.SetTool(Tool.Line); },
            Done = () => m.Tools.Opt.Grid > 0 && Marked(n0, Tool.Line, Tool.Box, Tool.Circle),
        });
        Add(new Step
        {
            Chapter = build, Title = "Материал",
            Text = "Камень ({MatStone}) самый прочный, но строится дольше. Брёвна ({MatWood}) быстрее, но горят. " +
                "Земля ({MatEarth}) кладётся быстрее всего, и гоблины без кирки её не пройдут. Выгодно класть " +
                "снаружи камень, а внутрь землю.",
            Task = "Возьмите брёвна",
            Point = "mat:" + Mat.Wood,
            Done = () => m.Tools.Opt.Material == Mat.Wood && !m.Tools.Opt.Dig,
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
            Text = "С киркой ({Pick}) те же кисти размечают раскопки: линия даёт ров, прямоугольник котлован, круг " +
                "кольцевой ров. Вынутая земля идёт в запасы. Если довести ров до пруда, он наполнится водой.",
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
            Text = "Лестница ({ToolLadder}) ставится щелчком по стене, по ней ваши люди поднимаются наверх. Ворота " +
                "({ToolGate}) пропускают своих, а орде их надо прорубить. Мост ({ToolBridge}) тянут от берега до " +
                "берега, он бывает арочный или подъёмный.",
            Task = "Приставьте лестницу к стене",
            Point = "tool:Ladder",
            Mark = FortWall,
            Enter = () => n0 = m.Sim.MarkingsMade,
            Done = () => Marked(n0, Tool.Ladder, Tool.Gate, Tool.Bridge),
        });
        Opens(Tool.Brush); Opens(Tool.Extrude); Opens(Tool.Capture); Opens(Hud.Part.Blueprints);
        Add(new Step
        {
            Chapter = build, Title = "Кисть, выдавить, чертёж",
            Text = "Кисть ({ToolBrush}) рисует стену от руки или шаром. Выдавливание ({ToolExtrude}) наращивает грань " +
                "или углубляет её, так делают ступени и проходы в скале. {ToolCapture} сохраняет постройку как " +
                "чертёж: обведите её рамкой. Чертежи лежат во вкладке «Чертежи», {Rotate} поворачивает копию.",
            Point = "tool:Capture",
        });

        // ---- the order of the work
        const string work = "Работы";
        Opens(Hud.Part.Works);
        Add(new Step
        {
            Chapter = work, Title = "Работы",
            Text = "Во вкладке «Работы» задают, кто какую работу делает и в каком порядке: 1 в первую очередь, 4 в " +
                "последнюю, пусто — никогда. Там же видно, кто добывает сам и кто в ополчении.",
            Task = "Откройте «Работы»",
            Point = "tab:works",
            Done = () => m.Hud.OpenTab == "works",
        });
        Opens(Hud.Part.Chronicle);
        Add(new Step
        {
            Chapter = work, Title = "Летопись",
            Text = "В летописи записаны все события. Последние строки видны над нижней панелью. Щелчок по строке со " +
                "значком ◎ переносит вид на место события.",
            Task = "Откройте «Летопись»",
            Point = "tab:chronicle",
            Done = () => m.Hud.OpenTab == "chronicle",
        });

        // ---- the time
        const string time = "Время";
        Opens(Hud.Part.Clock);
        Add(new Step
        {
            Chapter = time, Title = "Часы",
            Text = "Часы показывают, сколько осталось до орды. В обучении они стоят. {Pause} ставит паузу (размечать " +
                "можно и на паузе), {Speed} меняет скорость: ×1, ×2, ×3.",
            Task = "Поставьте паузу и смените скорость",
            Point = "clock",
            Enter = () => flag = false,
            Done = () => { if (m.Paused) flag = true; return flag && m.Speed > 1 && !m.Paused; },
        });
        Add(new Step
        {
            Chapter = time, Title = "Рудник",
            Text = "Рудник приносит золото, пока он ваш. Медный рудник к востоку от крепости, у маяка. Пошлите людей " +
                "к входу, и через несколько секунд он станет вашим. Оставьте там охрану: орда попробует его отбить.",
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
            Text = "Орда приходит с запада ватагами, у каждой свой вожак. Во вкладке «Орда» видно, кто придёт и " +
                "откуда. Линии на земле показывают их пути ({Trails} скрывает их). Орда обходит стены, а если " +
                "обхода нет, ломает их или лезет по лестницам.",
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
                "атаковать, выберите солдат и щёлкните правой кнопкой по врагу. Ожидание можно ускорить клавишей " +
                "{Speed}.",
            Task = "Перебейте ватагу",
            Point = "faces",
            Enter = () =>
            {
                var sim = m.Sim;
                fought = _clock;
                if (sim.Waves.Active || sim.Waves.Number > 0) return;
                sim.Waves.StartNow(sim);
                // the lesson's band: the first three of the wave
                var keep = new List<(UnitType, Warband, Survivor)>();
                while (sim.Waves._queue.Count > 0 && keep.Count < 3) keep.Add(sim.Waves._queue.Dequeue());
                sim.Waves._queue.Clear();
                foreach (var e in keep) sim.Waves._queue.Enqueue(e);
            },
            Done = () => !m.Sim.Waves.Active && m.Sim.Waves.Number > 0,
            // a raider the posts do not reach is to be gone after: told so once the fight has gone on a while
            Status = () => !m.Sim.Waves.Active ? null : $"налётчиков в долине: {Raiders() + m.Sim.Waves.Coming}" +
                (_clock - fought > 75 ? ". Выберите солдат (портреты вверху) и щёлкните правой кнопкой по налётчику" : ""),
        });
        Add(new Step
        {
            Chapter = horde, Title = "Отбой",
            Text = "Волна отбита. Тревога после волны снимается сама, а поднятую вручную снимает {Alarm}. За каждую " +
                "отбитую волну дают знания и золото.",
            Done = () => !m.Sim.Alarm,
            Task = "Тревоги нет",
        });

        // ---- the muster and the lore
        const string lore = "Войско";
        Add(new Step
        {
            Chapter = lore, Title = "Цитадель",
            Text = "В цитадели ({Keep} или щелчок по ней) набирают войско за золото. Новобранцы готовятся по очереди. " +
                "Сколько мест в войске, видно на верхней панели справа.",
            Task = "Наймите кого-нибудь",
            Point = "console",
            Enter = () => { house0 = m.Sim.Household() + m.Sim.Recruits.Count; if (m.Sim.Stock.Gold < 200) m.Sim.Stock.Gold = 200; },
            Done = () => m.Sim.Household() + m.Sim.Recruits.Count > house0,
        });
        int dwellings0 = 0;
        Add(new Step
        {
            Chapter = lore, Title = "Жилой дом",
            Text = "Мест в войске столько, сколько лежанок: их дают цитадель и жилые дома. Возьмите прямоугольник ({ToolBox}) " +
                "и в нижней панели выберите заливку «дом» ({Form} переключает). Получатся стены, крыша из брёвен и ворота. " +
                "Когда дом встанет, рабочие поставят лежанки, каждая даёт место в войске.",
            Task = "Нарисуйте дом прямоугольником",
            Point = "tool:Box",
            Enter = () => dwellings0 = m.Sim.Zones.FindAll(z => z.Kind == Zone.Dwelling).Count,
            Done = () => m.Sim.Zones.FindAll(z => z.Kind == Zone.Dwelling).Count > dwellings0,
        });
        Opens(Hud.Part.Lore);
        Add(new Step
        {
            Chapter = lore, Title = "Знания",
            Text = "Во вкладке «Знания» древо улучшений на трёх страницах: «Умения», «Войско» и «Вмешательство». " +
                "Знания дают за отбитые волны. На всё их не хватит, придётся выбирать.",
            Task = "Изучите что-нибудь",
            Point = "tab:lore",
            Enter = () => { learnt0 = m.Sim.Learnt.Count; if (m.Sim.Knowledge < 3) m.Sim.Knowledge = 3; },
            Done = () => m.Sim.Learnt.Count > learnt0,
        });
        Opens(Hud.Part.Magic);
        Add(new Step
        {
            Chapter = lore, Title = "Таинства",
            Text = "Жрецы молятся в святилище и копят вмешательство, а на него вы колдуете. Святилище размечают " +
                "делянкой. На месте силы молитва даёт втрое больше. Вмешательство приносят и жертвы: пленные и " +
                "туши с охоты.",
            Task = "Откройте «Таинства»",
            Point = "tab:magic",
            Done = () => m.Hud.OpenTab == "magic",
        });

        // ---- the eyes of one of ours
        const string eyes = "От первого лица";
        Add(new Step
        {
            Chapter = eyes, Title = "Вселиться",
            Text = "Выберите мечника и нажмите {Possess}, чтобы управлять им от первого лица. " +
                "{FpForward}{FpLeft}{FpBack}{FpRight} — идти, {FpRun} — бежать, мышь — смотреть, левая кнопка — " +
                "удар, правая — положить блок.",
            Task = "Вселитесь в мечника",
            Point = "faces",
            Done = () => m.FP.On,
        });
        Add(new Step
        {
            Chapter = eyes, Title = "Выйти",
            Text = "Пройдитесь, ударьте по камню или дереву. Esc возвращает обычный вид.",
            Task = "Выйдите: Esc",
            Done = () => !m.FP.On,
        });

        // ---- the last of it
        const string last = "Меню";
        Add(new Step
        {
            Chapter = last, Title = "Меню и управление",
            Text = "Esc открывает меню: сохранение, загрузка, настройки. В разделе «Управление» (или кнопка «?» " +
                "справа вверху, {Help}) перечислены все клавиши, любую можно переназначить. Быстрое сохранение " +
                "{QuickSave}, загрузка {QuickLoad}.",
            Task = "Откройте «Управление»",
            Point = "help",
            Done = () => m.Hud.MenuPage == "controls",
        });
        Add(new Step
        {
            Chapter = last, Title = "Обучение пройдено",
            Text = "Дальше настоящая осада: 12 волн раз в 10 минут, каждая сильнее прежней. {CallWave} вызывает орду " +
                "раньше срока. Награды за это нет, зато орда хуже знает вашу оборону.\n\nМожно начать новую игру " +
                "или остаться в этой долине. Тогда часы пойдут, и орда придёт через 10 минут. В песочнице из меню " +
                "орда приходит, только когда её позовёте.",
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
            _chapter.Text = $"обучение · глава {chapter.n} из {chapter.of} · {chapter.name} · шаг {i + 1} из {count}".ToUpperInvariant();
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
            if (last)
            {
                _buttons.AddChild(Btn("Новая игра", "Начать осаду в новой долине.", () => _t._m.NewGame(), true));
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
            // under the clock at the right, clear of the faces and of the console
            Position = new Vector2(vp.X - Size.X - 14, Math.Min(WaveClock.H + 14, Math.Max(10, vp.Y - BottomConsole.Band - Size.Y - 10)));
            Visible = !_t._m.Hud.MenuOpen;
        }

        public override void _Draw() => Craft.Framed(this, new Rect2(Vector2.Zero, Size), 10);
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
