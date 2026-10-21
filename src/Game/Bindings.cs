using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

namespace VoxelTower.Game;

/// <summary>What a key can be bound to: every keyboard command of the game that the player may put on a key of his own.</summary>
public enum Act
{
    CamForward, CamBack, CamLeft, CamRight, CamTurnLeft, CamTurnRight, CamTop, CamHome, CamZoomIn, CamZoomOut,
    ToolBrush, ToolLine, ToolBox, ToolCircle, ToolExtrude, ToolUnmark, ToolLadder, ToolGather, ToolGate, ToolRepair, ToolBridge, ToolCapture,
    MatStone, MatWood, MatEarth, Pick, WidthLess, WidthMore, HeightLess, HeightMore, Form, Level, Grid, Rotate,
    Keep, Alarm, Possess,
    CallWave, Pause, Speed, Trails, QuickSave, QuickLoad, Help,
    FpForward, FpBack, FpLeft, FpRight, FpRun, FpUp, FpDown, FpFire, FpStone, FpWood, FpEarth,
}

/// <summary>
/// The keys of the game, the player's own: every command with its name, what it does, the key it starts on (and a
/// second one some have, the arrows beside WASD) and the group it is shown in on the page of the controls. A key is
/// bound once among the commands of the field and once among those of the eyes (first person); put on a key another
/// command had, that one takes the key this one leaves. Kept in user://settings.cfg with the other settings; a
/// scripted run (a test, a screenshot) keeps to the keys the game starts with. A tooltip or a line of the chronicle
/// names a key as {Act}, and is shown with the key it is on now.
/// </summary>
public static class Bindings
{
    /// <summary>The field (the camera, the brushes, the orders, the game) or the eyes of one gone into.</summary>
    public enum Scope { Field, Eyes }

    public sealed record Def(Act Act, string Group, string Name, string About, Key Key, Key Alt, Scope Scope);

    public const string Camera = "Камера", Brushes = "Кисти", BrushOptions = "Материал и настройки кисти", Orders = "Дружина и приказы",
        Game = "Время и игра", Eyes = "От первого лица";

    /// <summary>The groups in the order the page shows them.</summary>
    public static readonly string[] Groups = { Camera, Brushes, BrushOptions, Orders, Game, Eyes };

    static Def D(Act a, string group, string name, string about, Key key, Key alt = Key.None) =>
        new(a, group, name, about, key, alt, group == Eyes ? Scope.Eyes : Scope.Field);

    public static readonly Def[] Defs =
    {
        D(Act.CamForward, Camera, "Вперёд", "Сдвинуть вид по долине вперёд.", Key.W, Key.Up),
        D(Act.CamBack, Camera, "Назад", "Сдвинуть вид назад.", Key.S, Key.Down),
        D(Act.CamLeft, Camera, "Влево", "Сдвинуть вид влево.", Key.A, Key.Left),
        D(Act.CamRight, Camera, "Вправо", "Сдвинуть вид вправо.", Key.D, Key.Right),
        D(Act.CamTurnLeft, Camera, "Повернуть влево", "Вращать вид вокруг точки, на которую он смотрит.", Key.Q),
        D(Act.CamTurnRight, Camera, "Повернуть вправо", "Вращать вид в другую сторону.", Key.E),
        D(Act.CamTop, Camera, "Вид сверху", "Смотреть на долину ровно сверху, как на карту, и обратно. Кнопка с картой на кольце карты — то же.", Key.Y),
        D(Act.CamHome, Camera, "Вернуть вид", "Вернуть вид к крепости, как в начале игры. Кнопка с башней на кольце карты — то же.", Key.H, Key.Home),
        D(Act.CamZoomIn, Camera, "Приблизить", "Ближе, как колесом мыши от себя.", Key.Pageup, Key.KpAdd),
        D(Act.CamZoomOut, Camera, "Отдалить", "Дальше, как колесом мыши на себя.", Key.Pagedown, Key.KpSubtract),

        D(Act.ToolBrush, Brushes, "Кисть", "От руки: столбы кладки до заданной высоты или шар, налипающий на поверхность.", Key.Key1),
        D(Act.ToolLine, Brushes, "Линия", "Прямая стена от точки до точки; с киркой — ров или траншея.", Key.Key2),
        D(Act.ToolBox, Brushes, "Прямоугольник", "От угла до угла: помост, двор, квадратная башня; с киркой — котлован.", Key.Key3),
        D(Act.ToolCircle, Brushes, "Круг", "Из центра к краю: круглая башня, бастион; с киркой — яма или кольцевой ров.", Key.Key4),
        D(Act.ToolExtrude, Brushes, "Выдавить", "Щелчок по грани: она нарастёт на шаг или, с киркой, уйдёт на шаг вглубь.", Key.Key5),
        D(Act.ToolUnmark, Brushes, "Снять разметку", "Рамкой или щелчком снять размеченную работу; сделанное остаётся.", Key.Key6),
        D(Act.ToolLadder, Brushes, "Лестница", "Щелчок по боковой грани стены или обрыва: рабочие приставят лестницу.", Key.Key7),
        D(Act.ToolGather, Brushes, "Делянка", "Участок, откуда свободные пешки сами берут камень, лес или землю; там же — убежище, святилище, жилой дом.", Key.Key8),
        D(Act.ToolGate, Brushes, "Ворота", "Щелчок по стене: прорубят проём и навесят дубовые створки.", Key.Key9),
        D(Act.ToolRepair, Brushes, "Починка", "Щелчок по разрушенной постройке: рабочие вернут ей прежний вид.", Key.Key0),
        D(Act.ToolBridge, Brushes, "Мост", "От берега до берега: мост аркой или подъёмный над рвом.", Key.M),
        D(Act.ToolCapture, Brushes, "Снять чертёж", "Обвести свою постройку рамкой: она станет чертежом, который ставится снова одним щелчком.", Key.B),

        D(Act.MatStone, BrushOptions, "Камень", "Класть камнем; у делянки — добывать камень.", Key.Z),
        D(Act.MatWood, BrushOptions, "Брёвна", "Класть брёвнами; у делянки — рубить лес.", Key.X),
        D(Act.MatEarth, BrushOptions, "Земля", "Класть утоптанной землёй; у делянки — копать землю.", Key.C),
        D(Act.Pick, BrushOptions, "Кирка", "Кисти размечают не кладку, а работу кирки: копать, рубить, ломать камень, разбирать своё. Ещё раз — снова кладка.", Key.V),
        D(Act.WidthLess, BrushOptions, "Тоньше", "Толщина стены, ширина рва, размер кисти — на шаг меньше.", Key.Bracketleft),
        D(Act.WidthMore, BrushOptions, "Толще", "Толщина, ширина, размер — на шаг больше.", Key.Bracketright),
        D(Act.HeightLess, BrushOptions, "Ниже", "Высота кладки на шаг меньше; с киркой — мельче.", Key.Minus),
        D(Act.HeightMore, BrushOptions, "Выше", "Высота на шаг больше; с киркой — глубже.", Key.Equal),
        D(Act.Form, BrushOptions, "Форма или заливка", "Столб или шар у кисти; сплошной или только стенки у прямоугольника и круга.", Key.F),
        D(Act.Level, BrushOptions, "Ровно или по рельефу", "Верх кладки (дно рва) на одной высоте от начала штриха или над своей землёй у каждого столбца.", Key.G),
        D(Act.Grid, BrushOptions, "Сетка", "Привязка к сетке от центра цитадели: концы штрихов встают в её узлы, линии идут вдоль осей или под 45°. Шаг — в консоли: 50 см, 1 м, 2 м.", Key.N),
        D(Act.Rotate, BrushOptions, "Повернуть чертёж", "Копию чертежа — на четверть круга.", Key.R),

        D(Act.Keep, Orders, "Цитадель", "Набор войска в цитадели; ещё раз — показать её.", Key.K),
        D(Act.Alarm, Orders, "Тревога", "Мирные уходят в убежища и цитадель, солдаты — на посты; ещё раз — отбой.", Key.T),
        D(Act.Possess, Orders, "Вселиться", "Играть выбранным от первого лица; выйти — Esc.", Key.I),

        D(Act.CallWave, Game, "Позвать орду", "Начать волну раньше: золота за это нет, зато орда меньше узнает о вашей обороне.", Key.Space),
        D(Act.Pause, Game, "Пауза", "Остановить время и пустить снова. Размечать можно и на паузе.", Key.P),
        D(Act.Speed, Game, "Скорость", "×1, ×2, ×3 по кругу.", Key.Tab),
        D(Act.Trails, Game, "Пути орды", "Показать или скрыть, куда пойдут ватаги.", Key.J),
        D(Act.QuickSave, Game, "Быстрое сохранение", "Записать игру в ячейку быстрого сохранения.", Key.F5),
        D(Act.QuickLoad, Game, "Загрузить быстрое", "Вернуться к быстрому сохранению.", Key.F9),
        D(Act.Help, Game, "Управление", "Открыть этот список.", Key.F1),

        D(Act.FpForward, Eyes, "Идти вперёд", "Туда, куда смотрите.", Key.W, Key.Up),
        D(Act.FpBack, Eyes, "Назад", "Пятиться.", Key.S, Key.Down),
        D(Act.FpLeft, Eyes, "Влево", "Шаг в сторону.", Key.A, Key.Left),
        D(Act.FpRight, Eyes, "Вправо", "Шаг в другую сторону.", Key.D, Key.Right),
        D(Act.FpRun, Eyes, "Бегом", "Держать — бежать.", Key.Shift),
        D(Act.FpUp, Eyes, "Вверх по лестнице", "Держать у лестницы.", Key.Space),
        D(Act.FpDown, Eyes, "Вниз по лестнице", "Держать у лестницы.", Key.C, Key.Ctrl),
        D(Act.FpFire, Eyes, "Огненные стрелы", "Лучник поджигает стрелы или гасит, если огонь изучен.", Key.F),
        D(Act.FpStone, Eyes, "Класть камень", "Правой кнопкой будет класться камень.", Key.Key1),
        D(Act.FpWood, Eyes, "Класть брёвна", "Правой кнопкой — брёвна.", Key.Key2),
        D(Act.FpEarth, Eyes, "Класть землю", "Правой кнопкой — землю.", Key.Key3),
    };

    /// <summary>What the mouse and the held keys do, which are not bound: shown on the page with the group they belong to.</summary>
    public static readonly (string group, string what, string about)[] Fixed =
    {
        (Camera, "Колесо мыши", "Приблизить и отдалить; на тачпаде — щипок или два пальца вверх и вниз."),
        (Camera, "ПКМ и тянуть", "Тянуть карту за землю."),
        (Camera, "СКМ или Alt+ЛКМ и тянуть", "Повернуть и наклонить вид."),
        (Camera, "ЛКМ по карте", "Смотреть на это место долины."),
        (Camera, "Мышь у края экрана", "Вести вид в ту сторону (выключается в настройках: «Прокрутка у края»)."),
        (Brushes, "ЛКМ", "Рисовать кистью: зажать и вести, у линии, прямоугольника и круга — от точки до точки."),
        (Brushes, "ПКМ", "Бросить начатый штрих и убрать кисть."),
        (Brushes, "Esc", "Убрать кисть из рук."),
        (Brushes, "Ctrl+Z (⌘Z)", "Отменить последнюю разметку, до сорока шагов назад."),
        (BrushOptions, "Shift с кистью", "Кирка на один мазок."),
        (Orders, "ЛКМ", "Выбрать пешку; рамкой — нескольких; Shift — добавить или убрать."),
        (Orders, "ЛКМ по лицу", "Лица дружины под верхней балкой: выбрать, ещё раз — показать."),
        (Orders, "ПКМ", "Выбранных — туда: по земле идти строем, по зверю — охотиться, по врагу — атаковать."),
        (Orders, "Ctrl+1…5", "Запомнить выбранных отрядом."),
        (Orders, "Shift+1…5", "Выбрать отряд; дважды — показать его."),
        (Orders, "Esc", "Снять выбор; когда снимать нечего — меню."),
        (Game, "Ctrl и + или −", "Интерфейс крупнее или мельче; Ctrl+0 — как был (на Mac — ⌘)."),
        (Eyes, "Мышь", "Смотреть."),
        (Eyes, "ЛКМ", "Удар, выстрел или кирка."),
        (Eyes, "ПКМ", "Класть выбранный материал."),
        (Eyes, "Esc", "Выйти из него."),
    };

    static readonly Dictionary<Act, Def> _defs = new();
    static readonly Dictionary<Act, Key> _keys = new();
    const string File = "user://settings.cfg";

    /// <summary>A key was put on another command: the console's letters and the hints show it.</summary>
    public static event Action Changed;

    static Bindings()
    {
        foreach (var d in Defs) { _defs[d.Act] = d; _keys[d.Act] = d.Key; }
    }

    public static Def Of(Act a) => _defs[a];
    public static Key KeyOf(Act a) => _keys[a];

    /// <summary>The second key, while no command of its scope has it as its own.</summary>
    static Key AltOf(Act a)
    {
        var d = _defs[a];
        if (d.Alt == Key.None) return Key.None;
        foreach (var (o, k) in _keys) if (k == d.Alt && _defs[o].Scope == d.Scope) return Key.None;
        return d.Alt;
    }

    /// <summary>This key pressed is this command.</summary>
    public static bool Is(InputEventKey e, Act a)
    {
        var k = e.PhysicalKeycode;
        return k != Key.None && (k == _keys[a] || k == AltOf(a));
    }

    /// <summary>The command of that scope on this key, if any.</summary>
    public static Act? Find(Key key, Scope scope)
    {
        if (key == Key.None) return null;
        foreach (var (a, k) in _keys) if (k == key && _defs[a].Scope == scope) return a;
        foreach (var d in Defs) if (d.Scope == scope && AltOf(d.Act) == key) return d.Act;
        return null;
    }

    /// <summary>The key of this command held down.</summary>
    public static bool Held(Act a)
    {
        var k = _keys[a]; var alt = AltOf(a);
        return (k != Key.None && Input.IsPhysicalKeyPressed(k)) || (alt != Key.None && Input.IsPhysicalKeyPressed(alt));
    }

    /// <summary>The key's name, as on the keyboard.</summary>
    public static string Name(Key k) => k switch
    {
        Key.None => "—", Key.Space => "Пробел", Key.Tab => "Tab", Key.Escape => "Esc", Key.Enter => "Enter", Key.Backspace => "Backspace",
        Key.Shift => "Shift", Key.Ctrl => "Ctrl", Key.Alt => "Alt", Key.Meta => "⌘", Key.Capslock => "Caps Lock",
        Key.Up => "↑", Key.Down => "↓", Key.Left => "←", Key.Right => "→",
        Key.Pageup => "PgUp", Key.Pagedown => "PgDn", Key.Home => "Home", Key.End => "End", Key.Insert => "Ins", Key.Delete => "Del",
        Key.Bracketleft => "[", Key.Bracketright => "]", Key.Minus => "−", Key.Equal => "=", Key.Semicolon => ";", Key.Apostrophe => "'",
        Key.Comma => ",", Key.Period => ".", Key.Slash => "/", Key.Backslash => "\\", Key.Quoteleft => "`",
        Key.KpAdd => "Num +", Key.KpSubtract => "Num −",
        >= Key.Key0 and <= Key.Key9 => ((int)(k - Key.Key0)).ToString(),
        _ => OS.GetKeycodeString(k),
    };

    /// <summary>The key this command is on now, by name.</summary>
    public static string Label(Act a) => Name(_keys[a]);

    /// <summary>The second key by name while it works, or nothing.</summary>
    public static string AltLabel(Act a) => AltOf(a) is var k && k != Key.None ? Name(k) : "";

    /// <summary>The key and its second one: "W или ↑".</summary>
    public static string LabelBoth(Act a) => AltOf(a) is var alt && alt != Key.None ? $"{Label(a)} или {Name(alt)}" : Label(a);

    static readonly Regex Token = new(@"\{([A-Za-z]+)\}", RegexOptions.Compiled);

    /// <summary>The text with every {Act} in it put as the key it is on now; anything else in braces stays.</summary>
    public static string Fill(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;
        return Token.Replace(text, m => Enum.TryParse<Act>(m.Groups[1].Value, out var a) ? Label(a) : m.Value);
    }

    /// <summary>Why this key cannot be had by this command, or null when it can.</summary>
    public static string Refused(Act a, Key key)
    {
        if (key == Key.Escape) return "Esc занят: он убирает кисть, снимает выбор и открывает меню";
        if (_defs[a].Scope == Scope.Field && key is Key.Shift or Key.Ctrl or Key.Alt or Key.Meta)
            return $"{Name(key)} нужен с мышью и цифрами — отряды, кирка на мазок, повороты";
        return null;
    }

    /// <summary>Puts the command on the key; the one that had it gets this one's old key. Returns that one, if any.</summary>
    public static Act? Bind(Act a, Key key)
    {
        var old = _keys[a];
        if (old == key) return null;
        Act? other = null;
        foreach (var (o, k) in _keys) if (o != a && k == key && _defs[o].Scope == _defs[a].Scope) { other = o; break; }
        _keys[a] = key;
        if (other is { } o2) _keys[o2] = old;
        Save();
        Changed?.Invoke();
        return other;
    }

    /// <summary>Every command back on the key it starts on.</summary>
    public static void Reset()
    {
        foreach (var d in Defs) _keys[d.Act] = d.Key;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Whether any command is on a key of the player's own.</summary>
    public static bool Customised
    {
        get { foreach (var d in Defs) if (_keys[d.Act] != d.Key) return true; return false; }
    }

    /// <summary>Reads the player's keys, if he set any; what is not there, or is no key, starts where it starts.</summary>
    public static void Load(ConfigFile cfg)
    {
        if (!cfg.HasSection("keys")) return;
        foreach (var d in Defs)
        {
            long v = cfg.GetValue("keys", d.Act.ToString(), (long)d.Key).AsInt64();
            if (v > 0) _keys[d.Act] = (Key)v;
        }
    }

    static void Save()
    {
        if (Settings.IsScripted) return;
        var cfg = new ConfigFile();
        cfg.Load(File);
        if (cfg.HasSection("keys")) cfg.EraseSection("keys");
        foreach (var d in Defs) if (_keys[d.Act] != d.Key) cfg.SetValue("keys", d.Act.ToString(), (long)_keys[d.Act]);
        cfg.Save(File);
    }
}
