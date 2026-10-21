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
        _m.Hud.Log("Обучение окончено. Орда придёт с запада на закате, через десять минут.", Hud.Kind.Warn);
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
        const string view = "Взгляд";
        Add(new Step
        {
            Chapter = view, Title = "Сумрачный вал",
            Text = "Цитадель на плато, долина вокруг. Каждые десять минут с запада приходит орда — двенадцать волн. Между волнами вы размечаете " +
                "стройку и раскопки, а дружина делает работу своими руками.\n\nЭто обучение: шаг за шагом вы пройдёте всё, что умеет игра. Пока оно идёт, " +
                "орда сама не придёт. Задание засчитывается, когда вы его сделаете; «Пропустить» — идти дальше без него.",
        });
        Add(new Step
        {
            Chapter = view, Title = "Сдвинуть вид",
            Text = "{CamForward}{CamLeft}{CamBack}{CamRight} или стрелки двигают вид по долине. Ещё можно зажать правую кнопку мыши и тянуть землю, как лист бумаги.",
            Task = "Сдвиньте вид в сторону",
            Enter = () => from = Aim(),
            Done = () => (Aim() - from).Length() > 3,
        });
        Add(new Step
        {
            Chapter = view, Title = "Повернуть",
            Text = "{CamTurnLeft} и {CamTurnRight} вращают вид вокруг точки, на которую он смотрит. Средняя кнопка мыши (или Alt и левая) с протяжкой — повернуть и наклонить.",
            Task = "Поверните вид",
            Enter = () => yaw0 = m.Rig.Aim.yaw,
            Done = () => MathF.Abs(m.Rig.Aim.yaw - yaw0) > .8f,
        });
        Add(new Step
        {
            Chapter = view, Title = "Ближе и дальше",
            Text = "Колесо мыши приближает и отдаляет вид; на тачпаде — щипок или два пальца вверх и вниз. Вблизи видно каждого человека и каждый камень.",
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
            Text = "{CamTop} или кнопка с картой на кольце карты — смотреть на долину ровно сверху, как на план. Так удобнее всего размечать стройку по сетке.",
            Task = "Посмотрите сверху",
            Point = "map:top",
            Done = () => m.Rig.TopDown,
        });
        Add(new Step
        {
            Chapter = view, Title = "Вернуть вид",
            Text = "Ещё раз {CamTop} — обратно наискосок. {CamHome} или кнопка с башней — вид вернётся к крепости, как в начале.",
            Task = "Верните вид к крепости",
            Point = "map:home",
            Enter = () => h0 = m.Rig.Homes,
            Done = () => !m.Rig.TopDown && m.Rig.Homes > h0,
        });
        Add(new Step
        {
            Chapter = view, Title = "Карта",
            Text = "В железном кольце — вся долина. Светлая рамка — то, что видно сейчас; цитадель — ромб справа, запад — слева: оттуда приходит орда. " +
                "Щелчок по карте переносит вид туда.",
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
            Text = "Под верхней балкой — ваша дружина: четыре рабочих, лучник и мечник. Щелчок по лицу выбирает, второй щелчок показывает его на поле.",
            Task = "Выберите рабочего",
            Point = "faces",
            Enter = () => m.Rig.Home(),
            Done = () => Chosen(Kinds.Worker),
        });
        Opens(Hud.Part.Console);
        Add(new Step
        {
            Chapter = folk, Title = "Послать",
            Text = "Внизу, в консоли, — кто выбран и чем занят. Правая кнопка по земле посылает выбранных туда; несколько идут строем. Работу они ищут сами — посылать нужно, чтобы занять место или увести от беды.",
            Task = "Пошлите выбранного правой кнопкой",
            Point = "console",
            Enter = () => n0 = m.Command.Sent,
            Done = () => m.Command.Sent > n0,
        });
        Add(new Step
        {
            Chapter = folk, Title = "Рамкой",
            Text = "Зажмите левую кнопку на земле и обведите рамкой нескольких — выберутся все в ней. Shift и щелчок добавляет одного или убирает.",
            Task = "Выберите троих или больше",
            Done = () => m.Selected.Count >= 3,
        });
        Add(new Step
        {
            Chapter = folk, Title = "Отряд",
            Text = "Ctrl+1 запоминает выбранных отрядом I, Shift+1 выбирает их снова (дважды — показывает). Отрядов пять, Ctrl+1…5.",
            Task = "Запомните выбранных: Ctrl+1",
            Done = () => m.Command.Squads[0].Count > 0,
        });
        Add(new Step
        {
            Chapter = folk, Title = "Снять выбор",
            Text = "Esc снимает выбор (а когда снимать нечего — открывает меню). Потом Shift+1 — и отряд снова выбран.",
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
            Text = "Звери долины — добыча: за каждого платят золотом. Выберите лучника и щёлкните правой кнопкой по зверю — он пойдёт на охоту. Золото нужно, чтобы набирать войско.",
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
            Text = "На балке — запасы: золото, камень, брёвна и земля в кубометрах, вмешательство старых сил и места в войске. Всё строится из запасов, а запасы добывают рабочие: рубят лес, ломают камень, копают землю.",
            Point = "stores",
        });
        _pickAt = _steps.Count;
        Opens(Tool.Line);
        Add(new Step
        {
            Chapter = stores, Title = "Кирка",
            Text = "Кисти ничего не строят сами: они размечают работу. Кирка ({Pick} или ячейка с киркой справа внизу) размечает работу кирки. Возьмите её и щёлкните по дереву у маяка — рабочие придут и срубят его.",
            Task = "Кирка — и щелчок по дереву",
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
            Text = "Размечать каждое дерево не нужно. Делянка ({ToolGather}): выберите брёвна ({MatWood}) и обведите рамкой лес — свободные рабочие сами будут брать оттуда понемногу, а разметка стен и рвов всегда идёт первой.",
            Task = "Делянка на лес: {ToolGather}, {MatWood}, рамка",
            Point = "tool:Gather",
            Mark = TreeNear,
            Enter = () => { n0 = m.Sim.Zones.Count; },
            Done = () => m.Sim.Zones.Exists(z => z.Kind == Stockpile.Wood),
        });
        Add(new Step
        {
            Chapter = stores, Title = "Каменоломня",
            Text = "Так же с камнем: делянка с камнем ({MatStone}) на скале, валунах или старых руинах — каменоломня. Её глубина — в консоли. Руины у маяка — хороший камень.",
            Task = "Делянка на камень: {ToolGather}, {MatStone}, рамка",
            Point = "mat:" + Mat.Stone,
            Mark = Ruin,
            Enter = () => { if (Ruin() is { } r) Look(r.x, r.z, 1.6f); },
            Done = () => m.Sim.Zones.Exists(z => z.Kind == Stockpile.Stone),
        });
        Add(new Step
        {
            Chapter = stores, Title = "Убрать кисть",
            Text = "Esc или правая кнопка мыши — убрать кисть из рук. Без кисти левая кнопка снова выбирает людей.",
            Task = "Уберите кисть",
            Done = () => m.Tools.Tool == Tool.None,
        });

        // ---- the old fort
        const string mend = "Починка";
        Opens(Tool.Repair);
        Add(new Step
        {
            Chapter = mend, Title = "Старая крепость",
            Text = "Крепость вокруг цитадели наполовину разрушена. Починка ({ToolRepair}): щелчок по её стене — рабочие разберут завалы и доложат кладку камень за камнем, как стояла. Рамка чинит только то, что в ней.",
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
            Text = "Вкладка «Задачи» на балке — чем заняться: починить крепость, навесить ворота, занять рудники, выстоять все волны. Цифра на вкладке — сколько осталось.",
            Task = "Откройте «Задачи»",
            Point = "tab:goals",
            Done = () => m.Hud.OpenTab == "goals",
        });

        // ---- the brushes
        const string build = "Стройка";
        Add(new Step
        {
            Chapter = build, Title = "Линия",
            Text = "Линия ({ToolLine}): зажмите левую кнопку и протяните — рабочие сложат прямую стену из запасов. Призрак показывает её заранее: янтарный — запасов хватит, красный — нет.",
            Task = "Проведите стену линией",
            Point = "tool:Line",
            Enter = () => n0 = m.Sim.MarkingsMade,
            Done = () => Marked(n0, Tool.Line) && !m.Tools.Opt.Dig,
        });
        Add(new Step
        {
            Chapter = build, Title = "Толщина и высота",
            Text = "Толщина — {WidthLess} и {WidthMore}, высота — {HeightLess} и {HeightMore}; или кнопками − и + в консоли. «Верх» — ровный от начала штриха или по рельефу ({Level}).",
            Task = "Измените толщину или высоту",
            Point = "console",
            Enter = () => { width0 = m.Tools.Opt.Width; height0 = m.Tools.Opt.Height; if (m.Tools.Tool == Tool.None) m.Tools.SetTool(Tool.Line); },
            Done = () => m.Tools.Opt.Width != width0 || m.Tools.Opt.Height != height0,
        });
        Add(new Step
        {
            Chapter = build, Title = "Сетка",
            Text = "Сетка ({Grid}) — привязка: начало и конец штриха встают в её узлы, линии идут вдоль осей или под 45°, и стены выходят ровными. Шаг — в консоли: 50 см, 1 м, 2 м. Линии через центр цитадели ярче — по ним стройте симметрично. Удобнее всего — сверху ({CamTop}).",
            Task = "Включите сетку и проведите линию по ней",
            Point = "console",
            Enter = () => { n0 = m.Sim.MarkingsMade; if (m.Tools.Tool == Tool.None) m.Tools.SetTool(Tool.Line); },
            Done = () => m.Tools.Opt.Grid > 0 && Marked(n0, Tool.Line, Tool.Box, Tool.Circle),
        });
        Add(new Step
        {
            Chapter = build, Title = "Материал",
            Text = "Чем класть: камень ({MatStone}) крепче всего, но ложится дольше; брёвна ({MatWood}) — быстрее, но горят и рубятся; земля ({MatEarth}) — быстрее всего, а гоблину без кирки сквозь неё не пройти. Внешний слой из камня и нутро из земли — дёшево и прочно.",
            Task = "Возьмите брёвна",
            Point = "mat:" + Mat.Wood,
            Done = () => m.Tools.Opt.Material == Mat.Wood && !m.Tools.Opt.Dig,
        });
        Opens(Tool.Box); Opens(Tool.Circle);
        Add(new Step
        {
            Chapter = build, Title = "Прямоугольник и круг",
            Text = "Прямоугольник ({ToolBox}) — от угла до угла, круг ({ToolCircle}) — из центра к краю. {Form} — сплошной (помост, бастион) или только стенки (двор, башня).",
            Task = "Разметьте прямоугольник или круг",
            Point = "tool:Circle",
            Enter = () => n0 = m.Sim.MarkingsMade,
            Done = () => Marked(n0, Tool.Box, Tool.Circle),
        });
        Add(new Step
        {
            Chapter = build, Title = "Отменить",
            Text = "Ошиблись — Ctrl+Z (на Mac ⌘Z): последняя разметка снимается, как не бывало. До сорока шагов назад. Что уже построено, остаётся.",
            Task = "Отмените последнюю разметку",
            Enter = () => n0 = m.Sim.MarkingsUndone,
            Done = () => m.Sim.MarkingsUndone > n0,
        });
        Add(new Step
        {
            Chapter = build, Title = "Ров",
            Text = "С киркой ({Pick}) те же кисти размечают работу кирки: линия — ров, прямоугольник — котлован, круг — кольцевой ров. Вынутая земля идёт в запасы. Ров с водой — лучшая преграда: прокопайте его к пруду.",
            Task = "Разметьте ров: кирка и линия",
            Enter = () => n0 = m.Sim.MarkingsMade,
            Done = () => Marked(n0, Tool.Line, Tool.Box, Tool.Circle, Tool.Brush) && m.Tools.Opt.Dig,
        });
        Opens(Tool.Unmark);
        Add(new Step
        {
            Chapter = build, Title = "Снять разметку",
            Text = "Снять разметку ({ToolUnmark}): рамкой — всё внутри, щелчком — поблизости. Что сделано, остаётся.",
            Task = "Снимите разметку",
            Point = "tool:Unmark",
            Enter = () => n0 = m.Sim.MarkingsMade,
            Done = () => Marked(n0, Tool.Unmark),
        });
        Opens(Tool.Ladder); Opens(Tool.Gate); Opens(Tool.Bridge);
        Add(new Step
        {
            Chapter = build, Title = "Лестница, ворота, мост",
            Text = "Лестница ({ToolLadder}) — щелчок по боковой грани стены: по ней ваши поднимаются наверх. Ворота ({ToolGate}) — проём со створками: свои проходят, орда рубит. Мост ({ToolBridge}) — от берега до берега, аркой или подъёмный над рвом.",
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
            Text = "Кисть ({ToolBrush}) — стены от руки или шар, налипающий на поверхность. Выдавить ({ToolExtrude}) — нарастить грань на шаг или вынуть вглубь: ступени, проходы в скале. Снять чертёж ({ToolCapture}) — обведите готовую постройку рамкой: она станет свитком во вкладке «Чертежи» и ставится снова одним щелчком ({Rotate} — повернуть).",
            Point = "tool:Capture",
        });

        // ---- the order of the work
        const string work = "Порядок";
        Opens(Hud.Part.Works);
        Add(new Step
        {
            Chapter = work, Title = "Работы",
            Text = "Вкладка «Работы» — кто что делает и в каком порядке: 1 — первым делом, 4 — в последнюю очередь, пусто — никогда. Здесь же — кто сам добывает и кто в ополчении.",
            Task = "Откройте «Работы»",
            Point = "tab:works",
            Done = () => m.Hud.OpenTab == "works",
        });
        Opens(Hud.Part.Chronicle);
        Add(new Step
        {
            Chapter = work, Title = "Летопись",
            Text = "Летопись — всё, что было: вести о волнах, рудниках, павших и сделанном. Последние строки и так видны над консолью; по строке с ◎ вид переносится на место.",
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
            Text = "Часы волны: сколько до орды; пока идёт обучение, они стоят. {Pause} — пауза (размечать можно и на паузе), {Speed} — скорость ×1, ×2, ×3, или кнопки у часов.",
            Task = "Поставьте паузу и смените скорость",
            Point = "clock",
            Enter = () => flag = false,
            Done = () => { if (m.Paused) flag = true; return flag && m.Speed > 1 && !m.Paused; },
        });
        Add(new Step
        {
            Chapter = time, Title = "Рудник",
            Text = "Рудники платят золотом, пока они ваши. Медный — у маяка, к востоку от крепости: пошлите к его входу людей, и он ваш через несколько секунд. Держите там кого-нибудь — орда придёт его отбивать.",
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
            Text = "Орда приходит с запада ватагами, у каждой — вожак. Вкладка «Орда» — кто придёт и откуда; линии на поле — их пути ({Trails} — показать или скрыть). Пути обходят ваши стены, а если обхода нет — орда ломает их, ставит лестницы, наводит мосты.",
            Task = "Откройте «Орду»",
            Point = "tab:horde",
            Enter = () => { if (!m.Sim.WantTrails) m.Command.ToggleTrails(); },
            Done = () => m.Hud.OpenTab == "horde",
        });
        Add(new Step
        {
            Chapter = horde, Title = "Тревога",
            Text = "Тревога ({Alarm}): мирные бросают работу и уходят в цитадель и убежища, солдаты — на посты. По рогу орды она поднимается сама.",
            Task = "Поднимите тревогу",
            Point = "console",
            Done = () => m.Sim.Alarm,
        });
        Add(new Step
        {
            Chapter = horde, Title = "Учебная ватага",
            Text = "С запада идёт учебная ватага — трое. Лучник бьёт издали и сам выбирает самых опасных, мечник держит землю и сбрасывает лестницы. Выберите солдат и щёлкните правой кнопкой по врагу — атаковать. Скорость ({Speed}) поможет дождаться.",
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
                (_clock - fought > 75 ? " — выберите солдат (лица под балкой) и щёлкните правой кнопкой по налётчику" : ""),
        });
        Add(new Step
        {
            Chapter = horde, Title = "Отбой",
            Text = "Волна отбита — рассветает. Тревога снимается сама после волны; поднятую вручную снимает {Alarm} ещё раз. За отбитую волну дают знания и золото.",
            Done = () => !m.Sim.Alarm,
            Task = "Тревоги нет",
        });

        // ---- the muster and the lore
        const string lore = "Войско";
        Add(new Step
        {
            Chapter = lore, Title = "Цитадель",
            Text = "Цитадель ({Keep}) или щелчок по ней — набор войска за золото: лучники, мечники, копейщики, позже — конница и жрецы. Набранные готовятся в цитадели по очереди; мест в войске — сколько на балке справа.",
            Task = "Наймите кого-нибудь",
            Point = "console",
            Enter = () => { house0 = m.Sim.Household() + m.Sim.Recruits.Count; if (m.Sim.Stock.Gold < 200) m.Sim.Stock.Gold = 200; },
            Done = () => m.Sim.Household() + m.Sim.Recruits.Count > house0,
        });
        Opens(Hud.Part.Lore);
        Add(new Step
        {
            Chapter = lore, Title = "Знания",
            Text = "Вкладка «Знания» — древо крепости: «Умения» (луки, кладка, труд, ловушки), «Войско» (кого набирать и чем усилить) и «Вмешательство» (заклинания). Знания дают отбитые волны, и на всё их не хватит — выбирайте.",
            Task = "Изучите что-нибудь",
            Point = "tab:lore",
            Enter = () => { learnt0 = m.Sim.Learnt.Count; if (m.Sim.Knowledge < 3) m.Sim.Knowledge = 3; },
            Done = () => m.Sim.Learnt.Count > learnt0,
        });
        Opens(Hud.Part.Magic);
        Add(new Step
        {
            Chapter = lore, Title = "Таинства",
            Text = "Это тёмное фэнтези: колдуют и орда, и вы. Жрецы молятся в святилище (делянка → «святилище»; на месте силы — втрое лучше) и копят вмешательство старых сил, а за него вы сами колдуете: дождь огня, молния, телекинез земли. Жертвы — пленные и туши с охоты.",
            Task = "Откройте «Таинства»",
            Point = "tab:magic",
            Done = () => m.Hud.OpenTab == "magic",
        });

        // ---- the eyes of one of ours
        const string eyes = "Своими руками";
        Add(new Step
        {
            Chapter = eyes, Title = "Вселиться",
            Text = "Выберите мечника и нажмите {Possess} — вы в нём: видите его глазами и бьёте его рукой. {FpForward}{FpLeft}{FpBack}{FpRight} — идти, {FpRun} — бегом, мышь — смотреть, левая кнопка — удар (у рабочего — кирка), правая — класть.",
            Task = "Вселитесь в мечника",
            Point = "faces",
            Done = () => m.FP.On,
        });
        Add(new Step
        {
            Chapter = eyes, Title = "Выйти",
            Text = "Пройдитесь, ударьте по камню или дереву. Esc — выйти из него.",
            Task = "Выйдите: Esc",
            Done = () => !m.FP.On,
        });

        // ---- the last of it
        const string last = "Напоследок";
        Add(new Step
        {
            Chapter = last, Title = "Меню и управление",
            Text = "Esc — меню: сохранить, загрузить, настройки. «Управление» там (или «?» справа вверху, {Help}) — все клавиши и что они делают; любую можно переназначить. Быстрое сохранение — {QuickSave}, загрузить его — {QuickLoad}.",
            Task = "Откройте «Управление»",
            Point = "help",
            Done = () => m.Hud.MenuPage == "controls",
        });
        Add(new Step
        {
            Chapter = last, Title = "Обучение пройдено",
            Text = "Дальше — настоящая осада: двенадцать волн, раз в десять минут, и каждая сильнее прежней. Пробел ({CallWave}) зовёт орду раньше: золота за это нет, зато она меньше знает о вашей обороне.\n\n" +
                "Начните новую игру — или оставайтесь в этой долине: часы пойдут, орда придёт на закате.",
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
            if (i > 0 && !last) _buttons.AddChild(Btn("‹", "Назад: перечитать прошлый шаг.", _t.Back));
            if (last)
            {
                _buttons.AddChild(Btn("Новая игра", "Начать настоящую осаду в новой долине.", () => _t._m.NewGame(), true));
                _buttons.AddChild(Btn("Остаться здесь", "Играть дальше в этой долине: часы пойдут, орда придёт через десять минут.", _t.Leave));
            }
            else if (s.Done == null) _buttons.AddChild(Btn("Дальше", "", _t.Next, true));
            else _buttons.AddChild(Btn("Пропустить", "Идти дальше, не делая этого.", _t.Next));
            if (!last)
            {
                var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                _buttons.AddChild(spacer);
                _buttons.AddChild(Btn("Выйти из обучения", "Открыть весь интерфейс и играть в этой долине; орда придёт через десять минут.", _t.Leave));
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
