using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

namespace VoxelTower.Game;

/// <summary>What a key can be bound to: every keyboard command of the game that the player may put on a key of his own.</summary>
public enum Act
{
    CamForward, CamBack, CamLeft, CamRight, CamTurnLeft, CamTurnRight, CamTop, CamHome, CamZoomIn, CamZoomOut,
    ToolBrush, ToolLine, ToolBox, ToolCircle, ToolExtrude, ToolUnmark, ToolLadder, ToolGather, ToolGate, ToolRepair, ToolBridge, ToolCapture, ToolWand, ToolYard, ToolTemplate,
    MatStone, MatWood, MatEarth, MatMetal, Pick, WidthLess, WidthMore, HeightLess, HeightMore, Form, Level, Grid, Rotate,
    Keep, Alarm, Possess,
    CallWave, Summon, Pause, Speed, Trails, QuickSave, QuickLoad, Help,
    FpForward, FpBack, FpLeft, FpRight, FpRun, FpUp, FpDown, FpFire, FpStone, FpWood, FpEarth,
    FpKick, FpSwitch, FpLadder, FpOrder, FpFourth,
    Board, FpLeave, EnterCell,
}

/// <summary>
/// The keys of the game, the player's own: every command with its name, what it does, the key it starts on (and a
/// second one some have, the arrows beside WASD) and the group it is shown in on the page of the controls. A key is
/// bound once among the commands of the field and once among those of the eyes (first person); put on a key another
/// command had, that one takes the key this one leaves. The two mouse buttons can be swapped on the field and in the
/// eyes (the panels keep to the left one). Kept in user://settings.cfg with the other settings; a scripted run (a test,
/// a screenshot) keeps to the keys the game starts with. A tooltip or a line of the chronicle names a key as {Act}, and
/// the buttons of the field as {Lmb} and {Rmb} ({LmbBy} and {RmbBy}: «левой кнопкой»), shown as they are now.
/// </summary>
public static class Bindings
{
    /// <summary>The field (the camera, the brushes, the orders, the game) or the eyes of one gone into.</summary>
    public enum Scope { Field, Eyes }

    public sealed record Def(Act Act, string Group, string Name, string About, Key Key, Key Alt, Scope Scope);

    public const string Camera = "Камера", Brushes = "Кисти", BrushOptions = "Материал и настройки кисти", Orders = "Дружина и приказы",
        Game = "Время и игра", Campaign = "Кампания", Eyes = "От первого лица";

    /// <summary>The groups in the order the page shows them.</summary>
    public static readonly string[] Groups = { Camera, Brushes, BrushOptions, Orders, Game, Campaign, Eyes };

    static Def D(Act a, string group, string name, string about, Key key, Key alt = Key.None) =>
        new(a, group, name, about, key, alt, group == Eyes ? Scope.Eyes : Scope.Field);

    public static readonly Def[] Defs =
    {
        D(Act.CamForward, Camera, "Вперёд", "", Key.W, Key.Up),
        D(Act.CamBack, Camera, "Назад", "", Key.S, Key.Down),
        D(Act.CamLeft, Camera, "Влево", "", Key.A, Key.Left),
        D(Act.CamRight, Camera, "Вправо", "", Key.D, Key.Right),
        D(Act.CamTurnLeft, Camera, "Повернуть влево", "Повернуть вид вокруг точки в центре экрана.", Key.Q),
        D(Act.CamTurnRight, Camera, "Повернуть вправо", "", Key.E),
        D(Act.CamTop, Camera, "Вид сверху", "Вид на долину сверху и обратно. То же делает кнопка с картой у миникарты.", Key.Y),
        D(Act.CamHome, Camera, "Вернуть вид", "Вернуть вид к крепости. То же делает кнопка с башней у миникарты.", Key.H, Key.Home),
        D(Act.CamZoomIn, Camera, "Приблизить", "", Key.Pageup, Key.KpAdd),
        D(Act.CamZoomOut, Camera, "Отдалить", "", Key.Pagedown, Key.KpSubtract),

        D(Act.ToolBrush, Brushes, "Кисть", "Стена от руки или шар, налипающий на поверхность.", Key.Key1),
        D(Act.ToolLine, Brushes, "Линия", "Прямая стена; с киркой — ров.", Key.Key2),
        D(Act.ToolBox, Brushes, "Прямоугольник", "Помост или стенки от угла до угла; с киркой — котлован.", Key.Key3),
        D(Act.ToolCircle, Brushes, "Круг", "Башня или бастион от центра к краю; с киркой — яма или кольцевой ров.", Key.Key4),
        D(Act.ToolExtrude, Brushes, "Выдавить", "Нарастить грань на шаг; с киркой — углубить.", Key.Key5),
        D(Act.ToolUnmark, Brushes, "Снять разметку", "Снять разметку рамкой или щелчком.", Key.Key6),
        D(Act.ToolLadder, Brushes, "Лестница", "Лестница на стену или обрыв.", Key.Key7),
        D(Act.ToolGather, Brushes, "Делянка", "Участок, где рабочие сами добывают камень, лес или землю. Также убежище, святилище, жилой дом.", Key.Key8),
        D(Act.ToolGate, Brushes, "Ворота", "Проём со створками в стене. По стене дома — новая дверь, по воротам — заложить их. В стене пруда это затвор: открытый, он выпускает воду волной.", Key.Key9),
        D(Act.ToolRepair, Brushes, "Починка", "Восстановить разрушенную постройку.", Key.Key0),
        D(Act.ToolBridge, Brushes, "Мост", "Мост от берега до берега, арочный или подъёмный.", Key.M),
        D(Act.ToolCapture, Brushes, "Снять чертёж", "Сохранить постройку как чертёж.", Key.B),
        D(Act.ToolWand, Brushes, "Волшебная палочка", "Выбрать всё прилегающее из одного материала: снести, переложить или снять чертёж.", Key.U),
        D(Act.ToolYard, Brushes, "Обустройство", "Дорожки между домами и вещи: костры, скамьи, факелы, колодец, телега, изгородь.", Key.L),
        D(Act.ToolTemplate, Brushes, "Шаблоны", "Готовые башни, стены, ворота, вышка, жилой дом, амбар, лестница и настил, вещи для поселения в разделе «Декор» и ваши чертежи. Рабочие строят их, как разметку.", Key.Semicolon),

        D(Act.MatStone, BrushOptions, "Камень", "Класть камень; на делянке — добывать камень.", Key.Z),
        D(Act.MatWood, BrushOptions, "Брёвна", "Класть брёвна; на делянке — рубить лес.", Key.X),
        D(Act.MatEarth, BrushOptions, "Земля", "Класть землю; на делянке — копать землю.", Key.C),
        D(Act.MatMetal, BrushOptions, "Металл", "Класть железо; на делянке — добывать руду.", Key.Comma),
        D(Act.Pick, BrushOptions, "Кирка", "Кисти размечают раскопки вместо стройки. Ещё раз — снова стройка.", Key.V),
        D(Act.WidthLess, BrushOptions, "Тоньше", "Толщина стены, ширина рва или размер кисти на шаг меньше.", Key.Bracketleft),
        D(Act.WidthMore, BrushOptions, "Толще", "Толщина, ширина или размер на шаг больше.", Key.Bracketright),
        D(Act.HeightLess, BrushOptions, "Ниже", "Высота на шаг меньше; с киркой — мельче.", Key.Minus),
        D(Act.HeightMore, BrushOptions, "Выше", "Высота на шаг больше; с киркой — глубже.", Key.Equal),
        D(Act.Form, BrushOptions, "Форма или заливка", "Столб или шар у кисти; сплошная фигура или стенки у прямоугольника и круга; у прямоугольника ещё дом; у линии с киркой ров или подкоп; у обустройства следующая дорожка или вещь; у здания города другой вид.", Key.F),
        D(Act.Level, BrushOptions, "Ровно или по рельефу", "Верх стены (дно рва) ровный от начала штриха или идёт по рельефу.", Key.G),
        D(Act.Grid, BrushOptions, "Сетка", "Привязка к сетке от центра цитадели: концы штрихов в узлах, линии вдоль осей или под 45°. Шаг выбирают в нижней панели.", Key.N),
        D(Act.Rotate, BrushOptions, "Повернуть чертёж или здание", "Повернуть копию чертежа, план здания или вещь обустройства на 90°; у дома выбрать сторону двери. Здание берут кнопкой «Поставить» во вкладке «Город».", Key.R),

        D(Act.Keep, Orders, "Цитадель", "Набор войска в цитадели; ещё раз — показать цитадель.", Key.K),
        D(Act.Alarm, Orders, "Тревога", "Рабочие уходят в убежища и цитадель, солдаты встают на посты. Ещё раз — отбой.", Key.T),
        D(Act.Possess, Orders, "Вселиться", "Управлять выбранным от первого лица. Выход — Esc.", Key.I),

        D(Act.CallWave, Game, "Позвать орду", "Начать волну раньше срока. Награды нет, но орда хуже знает вашу оборону. В песочнице орда приходит только так.", Key.Space),
        D(Act.Summon, Game, "Ватага орды", "Только в песочнице: ватага орды под курсором, с Shift — тяжёлая ватага, с Ctrl — войско с военачальником с ближайшей стороны долины.", Key.O),
        D(Act.Pause, Game, "Пауза", "Размечать можно и на паузе.", Key.P),
        D(Act.Speed, Game, "Скорость", "×1, ×2, ×3 по кругу.", Key.Tab),
        D(Act.Trails, Game, "Пути орды", "Показать или скрыть пути ватаг.", Key.J),
        D(Act.QuickSave, Game, "Быстрое сохранение", "", Key.F5),
        D(Act.QuickLoad, Game, "Быстрая загрузка", "", Key.F9),
        D(Act.Help, Game, "Управление", "Открыть этот список.", Key.F1),
        D(Act.Board, Campaign, "Карта долин", "Карта кампании: по ней ходят герои. Открыть, и обратно в долину.", Key.F2),
        D(Act.EnterCell, Campaign, "Войти в клетку", "На карте: выбранный герой с армией входит в долину, у которой стоит.", Key.Enter),

        D(Act.FpForward, Eyes, "Идти вперёд", "", Key.W, Key.Up),
        D(Act.FpBack, Eyes, "Назад", "", Key.S, Key.Down),
        D(Act.FpLeft, Eyes, "Влево", "", Key.A, Key.Left),
        D(Act.FpRight, Eyes, "Вправо", "", Key.D, Key.Right),
        D(Act.FpRun, Eyes, "Бегом", "Держать.", Key.Shift),
        D(Act.FpUp, Eyes, "Вверх по лестнице", "Держать у лестницы.", Key.Space),
        D(Act.FpDown, Eyes, "Вниз по лестнице", "Держать у лестницы.", Key.C, Key.Ctrl),
        D(Act.FpFire, Eyes, "Огненные стрелы", "Лучник зажигает или гасит стрелы, если огонь изучен.", Key.F),
        D(Act.FpStone, Eyes, "Класть камень", "{Rmb} кладёт камень. Командир: «За мной!».", Key.Key1),
        D(Act.FpWood, Eyes, "Класть брёвна", "{Rmb} кладёт брёвна. Командир: «Держать строй».", Key.Key2),
        D(Act.FpEarth, Eyes, "Класть землю", "{Rmb} кладёт землю. Командир: «Залп!» туда, куда смотрите.", Key.Key3),
        D(Act.FpFourth, Eyes, "Класть железо", "{Rmb} кладёт железо. Командир трубит в рог: наши рядом забывают боль и страх.", Key.Key4),
        D(Act.FpKick, Eyes, "Пинок", "Отталкивает врага перед собой или сбрасывает осадную лестницу.", Key.V),
        D(Act.FpSwitch, Eyes, "Войти в другого", "В того из своих, на кого смотрите, иначе в ближайшего.", Key.E),
        D(Act.FpLadder, Eyes, "Лестница", "Рабочий размечает лестницу к стене, на которую смотрит, и кладёт её {Rmb}.", Key.L),
        D(Act.FpOrder, Eyes, "Приказ взглядом", "Командир: бойцы рядом идут туда, куда он смотрит, или бьют того, на кого смотрит.", Key.G),
        D(Act.FpLeave, Eyes, "Уйти на карту", "В кампании у края долины: вы и наши в 20 м уходите на карту, в соседнюю долину. В отряде нужен командир.", Key.Enter),
    };

    /// <summary>What the mouse and the held keys do, which are not bound: shown on the page with the group they belong to.</summary>
    public static readonly (string group, string what, string about)[] Fixed =
    {
        (Camera, "Колесо мыши", "Приблизить и отдалить. На тачпаде — щипок или два пальца."),
        (Camera, "{Rmb} и тянуть", "Сдвинуть вид."),
        (Camera, "СКМ или Alt+{Lmb} и тянуть", "Повернуть и наклонить вид."),
        (Camera, "ЛКМ по миникарте", "Перенести туда вид."),
        (Camera, "Мышь у края экрана", "Сдвинуть вид (выключается в настройках)."),
        (Brushes, "{Lmb}", "Рисовать: зажать и вести."),
        (Brushes, "{Rmb}", "Отменить штрих и убрать кисть."),
        (Brushes, "Esc", "Убрать кисть."),
        (Brushes, "Ctrl+Z (⌘Z)", "Отменить последнюю разметку, до 40 шагов."),
        (Brushes, "Ворота ({ToolGate}) по стене дома", "Новая дверь там, где снаружи свободно. Ненужную дверь заложите щелчком воротами по ней."),
        (Brushes, "Дом вплотную к дому", "Стена между ними станет общей. Дверь в ней переедет на свободную сторону."),
        (BrushOptions, "Shift с кистью", "Кирка на один мазок."),
        (BrushOptions, "Shift+{Lmb} палочкой", "Добавить к выделению; щелчок без Shift применит ко всему выделенному, Esc сбросит."),
        (Orders, "{Lmb}", "Выбрать человека, рамкой — нескольких. С Shift — добавить или убрать."),
        (Orders, "ЛКМ по портрету", "Выбрать человека, ещё раз — показать его."),
        (Orders, "{Rmb} по портрету командира", "Карточка героя: уровень, опыт, умения. На новом уровне в ней выбирают умение. Медальон со шлемом наверху значит, что выбор ждёт."),
        (Orders, "{Rmb}", "По земле — идти, по зверю — охотиться, по врагу — атаковать."),
        (Orders, "{Rmb} по раненому", "Придавленного откопать, лежащего вынести к цитадели."),
        (Orders, "{Rmb} машиной", "Наша машина (катапульта, баллиста, требушет): расчёт разберёт её, перекатит туда и соберёт. Стреляет сама."),
        (Orders, "Ctrl+1…5", "Запомнить выбранных отрядом."),
        (Orders, "Shift+1…5", "Выбрать отряд, дважды — показать."),
        (Orders, "Esc", "Снять выбор. Если ничего не выбрано — меню."),
        (Game, "Ctrl и + или −", "Интерфейс крупнее или мельче, Ctrl+0 — обычный размер (на Mac ⌘)."),
        (Game, "Alt (держать)", "Показать клавиши на кнопках."),
        (Campaign, "Герой на карте", "Новая кампания начинается на карте: ваш герой стоит у цитадели, его армия с ним. Армию видно, когда герой войдёт в долину."),
        (Campaign, "{Lmb} по герою на карте", "Выбрать героя. Ещё раз — его карточка."),
        (Campaign, "{Lmb} по земле на карте", "Показать путь героя и время в пути: зелёным — сколько он пройдёт до ночи. Щелчок по концу пути — идти."),
        (Campaign, "{Rmb} по земле на карте", "Герой идёт туда сразу."),
        (Campaign, "Войти в клетку ({EnterCell})", "Герой с армией входит в долину, у которой стоит. Свои долины открываются и без героя: щелчок по долине, «Войти в долину»."),
        (Campaign, "Вывести на карту", "Выберите в долине командира и бойцов, откройте карту ({Board}) и нажмите «Вывести на карту». Или в карточке командира. Отряд выйдет у ближнего края."),
        (Campaign, "Герой дошёл до врага", "«Войти» — играть в той долине. «Бой сразу» — исход по силе отрядов. «Позже» — решить потом."),
        (Campaign, "Орда идёт", "Карточка на карте: «Войти и защищать», «Быстрый бой» у стен по силе, «Ждать»: долина защищается сама."),
        (Campaign, "Застава", "В ничьей долине: карта, «Поставить заставу». Рабочие строят башню у камня, и долина ваша."),
        (Campaign, "Места в войске", "Гости из другой долины держат места в своей, на шкале они после «+». За ушедшими в поход места остаются. На рассвете гости занимают свободные места."),
        (Campaign, "Командиры", "2 в начале и по 1 за каждый уровень ратуши."),
        (Campaign, "Герои", "Командир растёт до 10-го уровня: за врагов, павших рядом с ним, за отбитый набег, за взятую долину и бой на карте. Каждый уровень — умение на выбор. Павший командир теряет всё."),
        (Campaign, "Отряд на карте, щелчок ещё раз", "Карточка его командира."),
        (Campaign, "От первого лица у края", "{FpLeave} — уйти на карту с теми, кто рядом."),
        (Campaign, "Сокровища", "В ничьих долинах: заброшенный рудник, руины, логово зверей. Перебейте стражу и подойдите к сундуку: золото, на руднике и металл."),
        (Campaign, "Святилище испытаний", "Держите людей у алтаря 3 минуты против трёх волн мертвецов. Награда — благословение на всю кампанию."),
        (Campaign, "Стража", "Скелеты, волки и медведи нападают на всех, кто подойдёт. Наведите мышь на долину на карте: награда и сила стражи против выбранного отряда."),
        (Campaign, "Разведка", "До 3 лучников, пращников, всадников или псов без командира. Идёт быстрее и видит на 2 долины вокруг. Ратуша второго уровня тоже видит на 2 долины."),
        (Campaign, "Размер карты", "S 36×36, M 72×72, L 108×108, XL 144×144 клеток. Карта поделена на зоны, между ними перевалы под стражей."),
        (Campaign, "Ход героя", "На день у героя 1500 хода: клетка травы 100, дорога 50, песок и снег 150, болото 175, лес ещё 50. Не хватает на шаг — ждёт рассвета."),
        (Campaign, "До рассвета", "Кнопка на карте: время до утра сразу, ход героев снова полный. В долине время идёт в 8 раз быстрее до утра."),
        (Campaign, "Рудники", "Войдите героем в клетку рудника, и он ваш: каждое утро даёт золото, брёвна, камень, металл или редкое — ртуть, серу, кристаллы, самоцветы."),
        (Campaign, "Кучи, костры, сундуки", "Подбирает герой, один раз. Сундук: золото в казну или опыт герою, на выбор."),
        (Campaign, "Мельница, водяное колесо", "Раз в неделю: мельница даёт редкое, колесо — 75 золота."),
        (Campaign, "Жилища", "Каждую неделю готовы наняться бойцы. Нанятые идут с героем."),
        (Campaign, "Камень знаний, конюшни, обсерватория", "Опыт герою один раз; +400 хода до конца недели; туман рассеивается на 10 клеток."),
        (Campaign, "Таверна", "Выберите первую долину или героя у цитадели: «Таверна». Два героя по 600 золота, каждую неделю новые. Героев на карте 2 и по 1 за этаж ратуши."),
        (Campaign, "Торговец", "Герой у цитадели: «Торговец». Артефакты за золото и редкое, обмен редкого: продать по 20, купить по 80. Там же сокровищница цитадели."),
        (Campaign, "Артефакты", "8 мест на герое: голова, шея, тело, оружие, щит, ноги, кольцо, плащ. Дают умения, ход, обзор или доход. Лежат в сундуках и сокровищах. Карточка героя: щелчок снимает и надевает."),
        (Campaign, "Обоз", "Выберите свою долину с цитаделью или заставой: «Обоз». Везёт брёвна, камень и металл в другую вашу долину, медленно. Враг на пути отбирает груз."),
        (Campaign, "Верхние этажи", "В кампании второй этаж здания стоит втрое дороже, третий вчетверо. Дорогие здания просят редкое."),
        (Eyes, "Мышь", "Смотреть"),
        (Eyes, "{Lmb}", "Удар, выстрел или кирка"),
        (Eyes, "{Rmb}", "Положить блок выбранного материала"),
        (Eyes, "Esc", "Выйти"),
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

    /// <summary>The text with every {Act} in it put as the key it is on now, and the mouse's buttons as they are; anything else in braces stays.</summary>
    public static string Fill(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;
        return Token.Replace(text, m => m.Groups[1].Value switch
        {
            "Lmb" => Mouse(First), "Rmb" => Mouse(Second), "LmbBy" => MouseBy(First), "RmbBy" => MouseBy(Second),
            var s => Enum.TryParse<Act>(s, out var a) ? Label(a) : m.Value,
        });
    }

    /// <summary>
    /// The mouse's buttons as the player has them on the field and in the eyes: the first chooses, draws and strikes;
    /// the second sends, takes the brush away, drags the view and lays. Swapped at the player's wish.
    /// </summary>
    public static bool SwapMouse { get; private set; }
    public static MouseButton First => SwapMouse ? MouseButton.Right : MouseButton.Left;
    public static MouseButton Second => SwapMouse ? MouseButton.Left : MouseButton.Right;

    static string Mouse(MouseButton b) => b == MouseButton.Left ? "ЛКМ" : "ПКМ";
    static string MouseBy(MouseButton b) => b == MouseButton.Left ? "левой кнопкой" : "правой кнопкой";

    /// <summary>The mouse's buttons swapped, or put back.</summary>
    public static void SetSwapMouse(bool on)
    {
        if (SwapMouse == on) return;
        SwapMouse = on;
        Save();
        Changed?.Invoke();
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
        SwapMouse = false;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Whether any command is on a key of the player's own.</summary>
    public static bool Customised
    {
        get { foreach (var d in Defs) if (_keys[d.Act] != d.Key) return true; return SwapMouse; }
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
        SwapMouse = cfg.GetValue("keys", "SwapMouse", false).AsBool();
    }

    static void Save()
    {
        if (Settings.IsScripted) return;
        var cfg = new ConfigFile();
        cfg.Load(File);
        if (cfg.HasSection("keys")) cfg.EraseSection("keys");
        foreach (var d in Defs) if (_keys[d.Act] != d.Key) cfg.SetValue("keys", d.Act.ToString(), (long)_keys[d.Act]);
        if (SwapMouse) cfg.SetValue("keys", "SwapMouse", true);
        cfg.Save(File);
    }
}
