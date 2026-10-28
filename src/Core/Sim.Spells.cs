using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

public enum SpellKind : byte
{
    Hand, Rift, Mire, Skin, Fall, FireRain, Thunder, Weather, BloodMoon, BlackWind,
    Dread, DeadFog, Plague, Chains, Raise, Oath, Eye, Recall, Telekinesis, Explosion,
    Quake, Meteor, Whirlwind, Awaken,
    Hail, FirePillar, Guardian, Cherub, Destroyer, Host,
}

/// <summary>A spell: what it does, its circle, what learning it takes, what a casting costs, and how far it reaches.</summary>
public sealed class Spell
{
    public SpellKind Kind;
    public string Name = "", About = "";
    public int Circle;
    /// <summary>Learnt for intervention (the first of each circle) or for revelations, and how much.</summary>
    public bool ByFavour;
    public int Learn;
    /// <summary>Intervention a casting takes, and seconds before it can be cast again.</summary>
    public double Cost, Cooldown;
    /// <summary>How far it reaches on the field, in voxels: the circle it fills; 0 for the whole valley.</summary>
    public float Radius;
    /// <summary>Drawn as a stroke, from where the button goes down to where it comes up.</summary>
    public bool Stroke;
    /// <summary>Aimed at the very voxel under the cursor (a wall, a rock, a building), not at the ground there.</summary>
    public bool AtVoxel;
    /// <summary>
    /// Of the dark faith, kept for a side to come (the author's word of 28.10.2026): not on the tree nor offered, though
    /// what it does still works.
    /// </summary>
    public bool Draft;
}

/// <summary>A patch of ground made a bog by a spell, and till when.</summary>
public sealed class Mire { public float X, Z, R; public double Until; }

/// <summary>A gobbet of burning pitch falling out of the sky: where it comes down, and when.</summary>
public sealed class Drop { public float X, Y, Z; public double At; }

/// <summary>A place a spell lies on for a while — stone skin on our walls, the fog of the dead — and till when.</summary>
public sealed class Patch { public float X, Y, Z, R; public double Until; }

/// <summary>A whirlwind walking the field: where it is, which way it goes, how wide it is, and till when it lasts.</summary>
public sealed class Whirl { public float X, Z, VX, VZ, R; public double Until, Next; }

/// <summary>
/// Spells: the player's own hand in the fight, paid for in intervention — the household's faith, of the Bible and of
/// the angels. Four circles — the earth, the sky, the angels and the miracles — learnt on the intervention's page of the
/// lore tree: the first spell of each for intervention, the rest for revelations, each only once what it rests on is
/// learnt, up to the greatest of the circle at its end. Each costs intervention to cast and wants a while before it can
/// be cast again; grace makes them cheaper, readier and stronger. The dark spells of before (the blood moon, the black
/// wind, the fog of the dead, the plague, the raising of the dead) are drafts now, for a side to come.
/// </summary>
public sealed partial class Sim
{
    public static readonly string[] Circles = { "Круг земли", "Круг неба", "Круг ангелов", "Круг чудес" };

    /// <summary>The weathers the spell can call down, in the order offered.</summary>
    public static readonly Weather[] Summonable = { Weather.Rain, Weather.Fog, Weather.Wind };

    public static readonly Spell[] Spells =
    {
        new() { Kind = SpellKind.Hand, Circle = 0, ByFavour = true, Learn = 20, Name = "Длань земли", Cost = 20, Cooldown = 45, Stroke = true,
            About = "Поднимает вал земли вдоль проведённой линии: 2 м в ширину, 2,5 м в высоту, до 16 м в длину. Вал " +
            "встаёт за 3 секунды." },
        new() { Kind = SpellKind.Rift, Circle = 0, Learn = 2, Name = "Разверзнуть", Cost = 30, Cooldown = 50, Radius = 2.5f * VoxelWorld.PerMetre,
            About = "Земля в круге 2,5 м проваливается на 2,5 м. Кто стоял там, падает и получает урон, постройки и " +
            "осадные башни рушатся. Остаётся ров. У самой цитадели не действует." },
        new() { Kind = SpellKind.Mire, Circle = 0, Learn = 2, Name = "Трясина", Cost = 25, Cooldown = 40, Radius = 6 * VoxelWorld.PerMetre,
            About = "Круг 6 м на минуту становится топью. Все в нём, и орда, и наши, еле двигаются." },
        new() { Kind = SpellKind.Skin, Circle = 0, Learn = 2, Name = "Каменная кожа", Cost = 25, Cooldown = 60, Radius = 3 * VoxelWorld.PerMetre, AtVoxel = true,
            About = "Наша кладка, срубы и ворота в радиусе 3 м на 3 минуты становятся втрое прочнее." },
        new() { Kind = SpellKind.Fall, Circle = 0, Learn = 2, Name = "Обвал", Cost = 30, Cooldown = 45, Radius = 2.5f * VoxelWorld.PerMetre, AtVoxel = true,
            About = "Выбивает шар 2,5 м из стены, скалы или осадной башни. То, что держалось на нём, падает на тех, кто " +
            "внизу." },
        new() { Kind = SpellKind.FireRain, Circle = 1, ByFavour = true, Learn = 25, Name = "Огонь и сера", Cost = 30, Cooldown = 50, Radius = 5 * VoxelWorld.PerMetre,
            About = "3 секунды на круг 5 м с неба падает горящая сера. Поджигает дерево и налётчиков. В дождь вдвое слабее." },
        new() { Kind = SpellKind.Thunder, Circle = 1, Learn = 2, Name = "Перст грома", Cost = 25, Cooldown = 25, Radius = 3 * VoxelWorld.PerMetre,
            About = "Молния бьёт в налётчика у курсора: 150 урона, в том числе машинам, и поджог. Соседей задевает слабее. " +
            "В дождь в 1,5 раза сильнее." },
        new() { Kind = SpellKind.Weather, Circle = 1, Learn = 2, Name = "Призвать непогоду", Cost = 35, Cooldown = 180,
            About = "На 3 минуты задаёт погоду. Дождь гасит пожары и смолу, молния сильнее в 1,5 раза. Туман прячет врагов " +
            "от лучников и наших от орды. Ветер вдвое быстрее разносит огонь и сносит стрелы." },
        new() { Kind = SpellKind.BloodMoon, Circle = 1, Learn = 3, Draft = true, Name = "Кровавая луна", Cost = 40, Cooldown = 240,
            About = "На 90 секунд луна светит красным, и ночью становится светло: лучники стреляют в полную силу. Только " +
            "ночью." },
        new() { Kind = SpellKind.BlackWind, Circle = 1, Learn = 3, Draft = true, Name = "Чёрный ветер", Cost = 30, Cooldown = 60, Radius = 6 * VoxelWorld.PerMetre,
            About = "Вихрь в круге 6 м срывает лестницы и настилы орды и отбрасывает налётчиков от цитадели. Упавшие " +
            "получают урон, сброшенные со стены гибнут. Тролли отлетают недалеко, машины и скованные стоят на " +
            "месте." },
        new() { Kind = SpellKind.Dread, Circle = 2, Learn = 2, Name = "Страх Божий", Cost = 30, Cooldown = 60, Radius = 7 * VoxelWorld.PerMetre,
            About = "Над кругом 7 м вспыхивает свет. Налётчики в нём бросают всё и бегут на запад. Вожаки и тролли поддаются через раз, машины " +
            "никогда." },
        new() { Kind = SpellKind.DeadFog, Circle = 2, Learn = 2, Draft = true, Name = "Туман мёртвых", Cost = 30, Cooldown = 60, Radius = 6 * VoxelWorld.PerMetre,
            About = "Туман на 45 секунд накрывает круг 6 м. Налётчики в нём теряют по 3 здоровья в секунду и двигаются " +
            "вдвое медленнее. Наших и мертвецов не трогает." },
        new() { Kind = SpellKind.Plague, Circle = 2, Learn = 3, Draft = true, Name = "Мор", Cost = 40, Cooldown = 90, Radius = 5 * VoxelWorld.PerMetre,
            About = "Налётчики в круге 5 м заболевают и 20 секунд теряют по 2 здоровья в секунду. Мор передаётся тем, кто " +
            "стоит рядом с больным." },
        new() { Kind = SpellKind.Chains, Circle = 2, Learn = 2, Name = "Узы", Cost = 25, Cooldown = 40, Radius = 5 * VoxelWorld.PerMetre,
            About = "Ангел связывает налётчиков в круге 5 м на 10 секунд: они не двигаются и не бьют. Скованного мечник с приказом «Брать живым» " +
            "связывает уже при 60 % здоровья." },
        new() { Kind = SpellKind.Raise, Circle = 2, Learn = 3, Draft = true, Name = "Поднять мёртвых", Cost = 45, Cooldown = 90, Radius = 8 * VoxelWorld.PerMetre,
            About = "До 6 павших налётчиков в круге 8 м встают мертвецами и минуту дерутся за вас. Тела лежат 30 секунд." },
        new() { Kind = SpellKind.Oath, Circle = 3, Learn = 2, Name = "Обет назорея", Cost = 35, Cooldown = 90, Radius = 6 * VoxelWorld.PerMetre,
            About = "Наши в круге 6 м получают силу Самсона: восстанавливают 40 % здоровья, кровотечение останавливается, и " +
            "минуту они бьют в 1,5 раза сильнее." },
        new() { Kind = SpellKind.Eye, Circle = 3, Learn = 2, Name = "Око", Cost = 20, Cooldown = 120,
            About = "2 минуты видна вся орда: над каждым налётчиком метка, видная сквозь стены и лес. Туман не мешает " +
            "лучникам." },
        new() { Kind = SpellKind.Recall, Circle = 3, Learn = 1, Name = "Зов", Cost = 15, Cooldown = 30, Radius = 3 * VoxelWorld.PerMetre,
            About = "Ближайший к курсору наш человек (в радиусе 3 м) сразу переносится в цитадель: из боя, из огня, из рук " +
            "вора. Выйдет после тревоги или по приказу." },
        new() { Kind = SpellKind.Telekinesis, Circle = 3, ByFavour = true, Learn = 30, Name = "Телекинез", Cost = 30, Cooldown = 25, Radius = VoxelWorld.PerMetre, AtVoxel = true,
            About = "Хватает налётчика, зверя, своего или машину, а если там никого нет, вырывает шар 2 м из камня, стены " +
            "или земли. Пока кнопка зажата (до 8 секунд), схваченное летит за курсором. Отпустите, и оно полетит " +
            "дальше. Брошенный камень давит и пробивает кладку, брошенный налётчик разбивается. Своего можно " +
            "опустить бережно." },
        new() { Kind = SpellKind.Explosion, Circle = 3, Learn = 3, Name = "Взрыв", Cost = 40, Cooldown = 60, Radius = 2.5f * VoxelWorld.PerMetre, AtVoxel = true,
            About = "Взрыв шаром 5 м. Камни, брёвна и земля разлетаются и давят тех, на кого падают, постройки над " +
            "взорванным рушатся. Налётчиков рядом ранит и отбрасывает, машинам до 200 урона, дерево загорается. " +
            "Цитадель не задевает." },
        new() { Kind = SpellKind.Quake, Circle = 0, Learn = 4, Name = "Землетрясение", Cost = 45, Cooldown = 120, Radius = 8 * VoxelWorld.PerMetre,
            About = "Земля в круге 8 м трясётся. Налётчиков подбрасывает и сбивает с ног, лестницы, настилы и мосты орды " +
            "разваливаются, осадные машины получают по 120 урона. Нашу кладку не трогает." },
        new() { Kind = SpellKind.Meteor, Circle = 1, Learn = 4, Name = "Звезда Полынь", Cost = 55, Cooldown = 150, Radius = 3 * VoxelWorld.PerMetre,
            About = "С неба падает горящая глыба и выбивает воронку 3 м. Налётчиков рядом ранит и раскидывает, машинам до " +
            "300 урона, всё вокруг загорается. В воронке остаётся лава: она жжёт, поджигает дерево и за пару минут " +
            "застывает камнем, а водой гаснет сразу. Глыба катится дальше и давит тех, кто на пути." },
        new() { Kind = SpellKind.Whirlwind, Circle = 3, Learn = 4, Name = "Смерч", Cost = 50, Cooldown = 120, Radius = 2.5f * VoxelWorld.PerMetre,
            About = "Смерч шириной 5 м 10 секунд идёт по полю на орду. Подхватывает и отбрасывает налётчиков, срывает их " +
            "лестницы и настилы. Наших обходит." },
        new() { Kind = SpellKind.Awaken, Circle = 0, Learn = 12, Name = "Явление", Cost = 300, Cooldown = 1800, Radius = 8 * VoxelWorld.PerMetre,
            About = "Вершина веры. Его вымаливают все жрецы вместе: нужно 3 жреца и 300 вмешательства. С неба в выбранное место " +
            "сходит Архангел высотой 62 м и 3 минуты ходит по долине. Пламенный меч рубит и поджигает орду, огненный столп " +
            "бьёт с неба далеко впереди, взмах крыльев сносит орду с её лестницами, он перелетает через долину и трубит в " +
            "трубу. Наших и нашу кладку не трогает. В него можно вселиться. Остановить его может только бог орды. Сходит за 10 " +
            "секунд, не ближе 14 м к цитадели." },
        new() { Kind = SpellKind.Hail, Circle = 1, Learn = 2, Name = "Град", Cost = 30, Cooldown = 60, Radius = 7 * VoxelWorld.PerMetre,
            About = "5 секунд на круг 7 м падает град с кулак и больше. Каждая градина бьёт и сбивает с ног, ломает " +
            "лестницы, настилы и мосты орды. Нашу кладку не трогает. В дождь вдвое сильнее." },
        new() { Kind = SpellKind.FirePillar, Circle = 1, Learn = 3, Name = "Столп огненный", Cost = 35, Cooldown = 120, Radius = 3 * VoxelWorld.PerMetre,
            About = "Столп огня высотой 30 м стоит на месте 90 секунд. Налётчиков, что подходят ближе 3 м, жжёт и " +
            "отбрасывает. Ночью светит на 30 м вокруг: лучники там стреляют в полную силу." },
        new() { Kind = SpellKind.Guardian, Circle = 2, ByFavour = true, Learn = 25, Name = "Ангел-хранитель", Cost = 25, Cooldown = 60, Radius = 6 * VoxelWorld.PerMetre,
            About = "Ангел высотой 4 м минуту парит над местом. Наши в круге 6 м получают втрое меньше урона." },
        new() { Kind = SpellKind.Cherub, Circle = 2, Learn = 3, Name = "Херувим у врат", Cost = 40, Cooldown = 120, Radius = 7 * VoxelWorld.PerMetre,
            About = "Херувим с пламенным мечом, 7 м, минуту стоит на месте. Налётчиков в 7 м от него рубит, поджигает и " +
            "отбрасывает. Наших и нашу кладку не трогает." },
        new() { Kind = SpellKind.Destroyer, Circle = 2, Learn = 3, Name = "Ангел-губитель", Cost = 45, Cooldown = 150, Radius = 10 * VoxelWorld.PerMetre,
            About = "Тёмный ангел пролетает над кругом 10 м. Каждый третий налётчик в нём падает замертво, вожаки и тролли " +
            "вдвое реже. Машины не трогает." },
        new() { Kind = SpellKind.Host, Circle = 2, Learn = 4, Name = "Небесное воинство", Cost = 55, Cooldown = 180, Radius = 6 * VoxelWorld.PerMetre,
            About = "С неба в столпе света сходят 6 ангелов с мечами. Минуту они бьются за вас, потом возносятся." },
    };

    public static Spell SpellOf(SpellKind k) => Spells[(int)k];

    /// <summary>The spells the household knows.</summary>
    public readonly HashSet<SpellKind> Known = new();
    readonly double[] _spellReady = new double[Spells.Length];

    /// <summary>Seconds before a spell can be cast again.</summary>
    public double ReadyIn(SpellKind k) => Math.Max(0, _spellReady[(int)k] - Time);

    /// <summary>A spell ready again at once (the tests ask).</summary>
    public void ReadyNow(SpellKind k) => _spellReady[(int)k] = 0;

    /// <summary>Whether a spell can be learnt now: not known yet, what it rests on in its circle known, and what it takes at hand.</summary>
    public bool CanLearnSpell(Spell s) => !Known.Contains(s.Kind) && Opened(LoreOf(SpellKey(s.Kind))) && (s.ByFavour ? Favour >= s.Learn : Revelations >= s.Learn);

    /// <summary>Learns a spell for what it takes: intervention for the first of a circle, revelations for the rest.</summary>
    public bool LearnSpell(SpellKind k)
    {
        var s = SpellOf(k);
        if (!CanLearnSpell(s)) return false;
        if (s.ByFavour) Favour -= s.Learn; else Revelations -= s.Learn;
        Known.Add(k);
        Say($"Изучено заклинание «{s.Name}». {s.About}", 1);
        return true;
    }

    /// <summary>Bogs made by a spell; gobbets of burning pitch on their way down.</summary>
    public readonly List<Mire> Mires = new();
    public readonly List<Drop> Drops = new();
    /// <summary>Our walls made hard as rock for a while, and the fog of the dead lying on the field.</summary>
    public readonly List<Patch> Skins = new(), DeadFogs = new();
    /// <summary>Till when the moon shines red, and till when the whole horde is seen.</summary>
    public double BloodMoonUntil = -1, EyeUntil = -1;
    /// <summary>The earth rising under the Hand, lowest first; how much of it is up, and since when it rises.</summary>
    readonly List<(int x, int y, int z)> _rising = new();
    int _risenEarth;
    double _riseFrom;
    const double RiseFor = 3;

    /// <summary>A spell cast, where: for whatever shows it.</summary>
    public event Action<SpellKind, float, float, float> Spelled;
    /// <summary>A bolt of lightning, from the sky to where it struck.</summary>
    public event Action<float, float, float> Struck;

    /// <summary>
    /// Casts a spell at a spot (voxels: for most the ground there; for those aimed at a voxel, the voxel the cursor is on;
    /// for the Hand a stroke from the first spot to the second). The weather called down is picked by the variant. Null
    /// when it is cast; otherwise why it cannot be.
    /// </summary>
    public string Cast(SpellKind k, float x, float y, float z, float x2 = 0, float z2 = 0, int variant = 0)
    {
        var s = SpellOf(k);
        if (Over) return "осада окончена";
        if (!Known.Contains(k)) return "это заклинание ещё не изучено";
        if (Time < _spellReady[(int)k]) return $"ещё не готово: {_spellReady[(int)k] - Time:0} с";
        if (Favour < CostOf(s)) return $"мало вмешательства: нужно {CostOf(s):0}";
        string why = k switch
        {
            SpellKind.Hand => RaiseBank(x, z, x2, z2),
            SpellKind.Rift => Open(x, z, s.Radius),
            SpellKind.Mire => BogDown(x, z, s.Radius),
            SpellKind.Skin => Harden((int)x, (int)y, (int)z, s.Radius),
            SpellKind.Fall => BringDown((int)x, (int)y, (int)z, s.Radius),
            SpellKind.FireRain => RainFireOn(x, z, s.Radius),
            SpellKind.Thunder => Thunderbolt(x, y, z, s.Radius),
            SpellKind.Weather => CallWeather(variant),
            SpellKind.BloodMoon => Clock.Darkness < .3f ? "кровавая луна восходит только ночью" : Moon(),
            SpellKind.BlackWind => Gale(x, z, s.Radius),
            SpellKind.Dread => Terrify(x, z, s.Radius),
            SpellKind.DeadFog => Shroud(x, z, s.Radius),
            SpellKind.Plague => Sicken(x, z, s.Radius),
            SpellKind.Chains => Chain(x, z, s.Radius),
            SpellKind.Raise => RaiseDead(x, z, s.Radius),
            SpellKind.Oath => Swear(x, z, s.Radius),
            SpellKind.Recall => CallHome(x, z, s.Radius),
            SpellKind.Telekinesis => Grab(x, y, z),
            SpellKind.Explosion => Blast((int)x, (int)y, (int)z, s.Radius),
            SpellKind.Quake => Shake(x, z, s.Radius),
            SpellKind.Meteor => Fallstar(x, z, s.Radius),
            SpellKind.Whirlwind => Unleash(x, z, s.Radius),
            SpellKind.Awaken => Prayers() < 3 ? "нужно 3 жреца: Явление вымаливают вместе" : Awaken(DeityKind.Archangel, x, z),
            SpellKind.Hail => CallHail(x, z, s.Radius),
            SpellKind.FirePillar => RaisePillar(x, z),
            SpellKind.Guardian => Awaken(DeityKind.Guardian, x, z, 60 * Might),
            SpellKind.Cherub => Awaken(DeityKind.Cherub, x, z, 60 * Might),
            SpellKind.Destroyer => SendDestroyer(x, z),
            SpellKind.Host => SendHost(x, z, s.Radius),
            _ => Watch(),
        };
        if (why != null) return why;
        Favour -= CostOf(s);
        _spellReady[(int)k] = Time + CooldownOf(s);
        Spelled?.Invoke(k, x, y, z);
        Say($"Заклинание: «{s.Name}».", 1, x, z);
        // the horde's shamans answer a god with theirs, if its faith will carry it (Sim.HordeFaith.cs)
        if (k == SpellKind.Awaken) Answer();
        return null;
    }

    string Moon() { BloodMoonUntil = Time + 90 * Might; return null; }

    string Watch() { EyeUntil = Time + 120 * Might; return null; }

    /// <summary>Whether a spot lies in a bog of the spell.</summary>
    bool InMire(float x, float z)
    {
        foreach (var m in Mires) if ((x - m.X) * (x - m.X) + (z - m.Z) * (z - m.Z) < m.R * m.R) return true;
        return false;
    }

    // ------------------------------------------------------------ the circle of earth

    /// <summary>The Hand: a bank of earth along the stroke, rounded on top, lower at its ends, rising from the ground up.</summary>
    string RaiseBank(float x0, float z0, float x1, float z1)
    {
        float dx = x1 - x0, dz = z1 - z0, len = MathF.Sqrt(dx * dx + dz * dz);
        if (len < 2 * VoxelWorld.PerMetre) return "проведите линию подлиннее: от 2 м";
        float max = 16 * VoxelWorld.PerMetre;
        if (len > max) { x1 = x0 + dx / len * max; z1 = z0 + dz / len * max; len = max; }
        float ux = dx / len, uz = dz / len;
        const float Half = VoxelWorld.PerMetre, High = 20;
        var cols = new Dictionary<(int x, int z), int>();
        for (float t = 0; t <= len; t += .5f)
            for (float c = -Half; c <= Half; c += .5f)
            {
                int x = (int)MathF.Floor(x0 + ux * t - uz * c), z = (int)MathF.Floor(z0 + uz * t + ux * c);
                if (x < 1 || z < 1 || x >= World.SX - 1 || z >= World.SZ - 1 || Site.Forbidden(x, z)) continue;
                float end = MathF.Min(1, (len / 2 - MathF.Abs(t - len / 2)) / 8 + .45f);
                int h = (int)(High * MathF.Sqrt(Math.Max(0, 1 - c / Half * (c / Half))) * end);
                if (h <= 0) continue;
                cols[(x, z)] = Math.Max(cols.GetValueOrDefault((x, z)), h);
            }
        // what is still rising of the last one goes up at once
        RiseNow(_rising.Count);
        _rising.Clear(); _risenEarth = 0;
        foreach (var ((x, z), h) in cols)
        {
            int g = World.SurfAt(x, z);
            for (int y = g; y < g + h && y < World.SY - 1; y++) if (Brushes.Free(World.Get(x, y, z))) _rising.Add((x, y, z));
        }
        if (_rising.Count == 0) return "здесь валу не подняться";
        _rising.Sort((a, b) => a.y.CompareTo(b.y));
        _riseFrom = Time;
        return null;
    }

    /// <summary>Lays the rising earth up to the n-th voxel.</summary>
    void RiseNow(int n)
    {
        for (; _risenEarth < Math.Min(n, _rising.Count); _risenEarth++)
        {
            var (x, y, z) = _rising[_risenEarth];
            if (Brushes.Free(World.Get(x, y, z))) World.Set(x, y, z, Mat.Soil);
        }
    }

    string BogDown(float x, float z, float r)
    {
        Mires.Add(new Mire { X = x, Z = z, R = r, Until = Time + 60 * Might });
        return null;
    }

    /// <summary>The Fall: a ball knocked out of what is there; it comes down in pieces, and what it held up with it.</summary>
    string BringDown(int x, int y, int z, float r)
    {
        var ball = new HashSet<int>();
        int ri = (int)MathF.Ceiling(r);
        for (int dy = -ri; dy <= ri; dy++)
            for (int dz = -ri; dz <= ri; dz++)
                for (int dx = -ri; dx <= ri; dx++)
                {
                    if (dx * dx + dy * dy + dz * dz > r * r) continue;
                    int vx = x + dx, vy = y + dy, vz = z + dz;
                    if (!World.InBounds(vx, vy, vz) || vy < 4) continue;
                    byte m = World.Get(vx, vy, vz);
                    // the citadel, the black stone and the bedrock do not give; water and the air are not there to fall
                    if (m == Mat.Air || m == Mat.Water || Mat.Def[m].Group == MatGroup.Hard || Mat.Passable[m] && !Mat.IsLadder[m]) continue;
                    ball.Add(WaterPlanner.Pack(vx, vy, vz));
                }
        if (ball.Count == 0) return "здесь нечему рушиться: наведите на стену, скалу или постройку";
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside) continue;
            float d = MathF.Sqrt((u.X - x) * (u.X - x) + (u.Y - y) * (u.Y - y) * .5f + (u.Z - z) * (u.Z - z));
            if (Kinds.IsEngine(u.T) && d < r + 3 * VoxelWorld.PerMetre) Hurt(u, null, 280 * Might, "заклятый обвал");
            else if (d < r + 1.5f * VoxelWorld.PerMetre) Strike(u, 12, 6, 0, -1, 0, float.NaN, "заклятый обвал", 45 * Might);
        }
        Fall(ball, quiet: false);
        return null;
    }

    /// <summary>How deep the earth opens under the middle of a rift, in voxels.</summary>
    const int RiftDeep = 20;

    /// <summary>
    /// A rift: the natural ground in the circle sinks in a bowl, deepest in the middle, and is gone; whatever stood on it
    /// — a wall, a tree, an engine — comes down into it, and whoever stood there falls to the bottom.
    /// </summary>
    string Open(float x, float z, float r)
    {
        int ix = (int)x, iz = (int)z, ri = (int)MathF.Ceiling(r), taken = 0;
        if (Site.Forbidden(ix, iz)) return "у самой цитадели земля не разверзнется";
        for (int dz = -ri; dz <= ri; dz++)
            for (int dx = -ri; dx <= ri; dx++)
            {
                float d2 = dx * dx + dz * dz;
                if (d2 > r * r) continue;
                int cx = ix + dx, cz = iz + dz;
                if (cx < 2 || cz < 2 || cx >= World.SX - 2 || cz >= World.SZ - 2 || Site.Forbidden(cx, cz)) continue;
                int depth = (int)MathF.Round(RiftDeep * MathF.Sqrt(1 - d2 / (r * r)));
                if (depth <= 0) continue;
                int top = World.GroundTop(cx, cz);
                // the grass and the flowers on it go down with it
                for (int y = top; y < top + 3 && y < World.SY - 1; y++)
                    if (World.Get(cx, y, cz) is var g && g != Mat.Air && Mat.IsVeg[g] && !Mat.IsTrunk(g)) World.Set(cx, y, cz, Mat.Air);
                for (int y = top - 1; y >= Math.Max(5, top - depth); y--)
                {
                    byte m = World.Get(cx, y, cz);
                    if (m == Mat.Air) continue;
                    if (!Mat.IsNatural[m] || !Breakable(m) || Mat.IsVeg[m] || m is Mat.Ore or Mat.Ruin) break;
                    World.Set(cx, y, cz, Mat.Air);
                    if (Orders.At(cx, y, cz) != 0) Orders.Clear(cx, y, cz);
                    taken++;
                }
            }
        if (taken == 0) return "здесь земле некуда разверзнуться";
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null || (u.X - x) * (u.X - x) + (u.Z - z) * (u.Z - z) > r * r) continue;
            Hurt(u, null, 15 * Might, "провал");
            StopCarve(u); StopBuild(u); u.Path = null;
        }
        return null;
    }

    /// <summary>Stone skin: what of ours is built in the ball goes hard as rock for three minutes.</summary>
    string Harden(int x, int y, int z, float r)
    {
        int ri = (int)r;
        bool ours = false;
        for (int dy = -ri; dy <= ri && !ours; dy += 2)
            for (int dz = -ri; dz <= ri && !ours; dz += 2)
                for (int dx = -ri; dx <= ri && !ours; dx += 2)
                    if (dx * dx + dy * dy + dz * dz <= r * r && Mat.IsPlayer[World.Get(x + dx, y + dy, z + dz)]) ours = true;
        if (!ours) return "здесь нет нашей кладки: наведите на стену, сруб или ворота";
        Skins.Add(new Patch { X = x + .5f, Y = y + .5f, Z = z + .5f, R = r, Until = Time + 180 * Might });
        return null;
    }

    /// <summary>How much longer the horde hacks a voxel under a stone skin: three times, or no longer at all.</summary>
    internal float SkinHack(int x, int y, int z)
    {
        foreach (var s in Skins)
            if ((x - s.X) * (x - s.X) + (y - s.Y) * (y - s.Y) + (z - s.Z) * (z - s.Z) < s.R * s.R) return 1 / 3f;
        return 1;
    }

    // ------------------------------------------------------------ the circle of fire and the sky

    string RainFireOn(float x, float z, float r)
    {
        // the pitch finds the raiders under it, a gobbet for each (a dozen at most); the rest falls where it falls
        var under = HordeIn(x, z, r);
        for (int i = 0; i < 24; i++)
        {
            float dx, dz;
            if (i < Math.Min(12, under.Count)) { dx = under[i].X - x + (float)(Rng.NextDouble() - .5) * 6; dz = under[i].Z - z + (float)(Rng.NextDouble() - .5) * 6; }
            else
            {
                double a = Rng.NextDouble() * Math.PI * 2, d = Math.Sqrt(Rng.NextDouble()) * r;
                dx = (float)(Math.Cos(a) * d); dz = (float)(Math.Sin(a) * d);
            }
            int px = Math.Clamp((int)(x + dx), 0, World.SX - 1), pz = Math.Clamp((int)(z + dz), 0, World.SZ - 1);
            Drops.Add(new Drop { X = x + dx, Y = World.SurfAt(px, pz), Z = z + dz, At = Time + .4 + (i * 7 % 24) * 3.0 / 24 });
        }
        return null;
    }

    /// <summary>A gobbet of burning pitch comes down and splashes: what burns within a metre of it catches, whoever it falls on burns.</summary>
    void Land(Drop d)
    {
        float k = (Weather == Weather.Rain ? .5f : 1) * Might;
        int x = (int)d.X, z = (int)d.Z, top = World.SurfAt(Math.Clamp(x, 0, World.SX - 1), Math.Clamp(z, 0, World.SZ - 1));
        const int Splatter = VoxelWorld.PerMetre;
        SearRound(d.X, top, d.Z, Splatter * .6f);
        int lit = 0;
        for (int dz = -Splatter; dz <= Splatter && lit < 3; dz += 2)
            for (int dx = -Splatter; dx <= Splatter && lit < 3; dx += 2)
            {
                if (dx * dx + dz * dz > Splatter * Splatter) continue;
                int sx = x + dx, sz = z + dz;
                if (sx < 0 || sz < 0 || sx >= World.SX || sz >= World.SZ) continue;
                // the top of whatever stands there, and down it a little
                int st = World.SurfAt(sx, sz);
                for (int y = st - 1; y >= st - 3 && y >= top - 16; y--)
                    if (Mat.Burns(World.Get(sx, y, sz)) && (k >= 1 || Rng.NextDouble() < k) && Ignite(sx, y, sz)) { lit++; break; }
            }
        const float Splash = 1.5f * VoxelWorld.PerMetre;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || MathF.Abs(u.X - d.X) > Splash || MathF.Abs(u.Z - d.Z) > Splash || MathF.Abs(u.Y - top) > 12) continue;
            Hurt(u, null, 12 * k, "огненный дождь");
            if (!u.Dead) { u.Burn = Math.Max(u.Burn, 4 * k); u.BurnHeat = 1; }
        }
    }

    /// <summary>A bolt out of the sky at the raider nearest the spot, or at the ground there.</summary>
    string Thunderbolt(float x, float y, float z, float r)
    {
        Unit t = null; float bd = r;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Bound || u.Team != Team.Horde) continue;
            float d = MathF.Sqrt((u.X - x) * (u.X - x) + (u.Z - z) * (u.Z - z));
            if (d < bd) { bd = d; t = u; }
        }
        float k = (Weather == Weather.Rain ? 1.5f : 1) * Might;
        if (t != null) { (x, y, z) = (t.X, t.Y, t.Z); }
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u == t) continue;
            if (MathF.Abs(u.X - x) < 10 && MathF.Abs(u.Z - z) < 10 && MathF.Abs(u.Y - y) < 12) Hurt(u, null, 30 * k, "молния");
        }
        if (t != null)
        {
            Hurt(t, null, 150 * k, "молния");
            if (!t.Dead) { t.Burn = Math.Max(t.Burn, 5); t.BurnHeat = 1; }
        }
        int ix = (int)x, iz = (int)z, top = World.SurfAt(Math.Clamp(ix, 0, World.SX - 1), Math.Clamp(iz, 0, World.SZ - 1));
        if (Mat.Burns(World.Get(ix, top - 1, iz))) Ignite(ix, top - 1, iz);
        SearRound(x, t != null ? y : top, z, 3.5f);
        Struck?.Invoke(x, t != null ? y : top, z);
        return null;
    }

    /// <summary>The weather of the player's choosing, for three minutes.</summary>
    string CallWeather(int variant)
    {
        StartWeather(Summonable[Math.Clamp(variant, 0, Summonable.Length - 1)], 180 * Might);
        return null;
    }

    /// <summary>
    /// The black wind: out from the citadel through the circle it tears the horde's ladders and decks loose and carries
    /// them off in pieces, and throws the raiders there through the air — the lighter, the farther.
    /// </summary>
    string Gale(float x, float z, float r)
    {
        float wx = x - Site.KeepX, wz = z - Site.KeepZ, wl = MathF.Sqrt(wx * wx + wz * wz);
        if (wl < 1) { wx = -1; wz = 0; wl = 1; }
        wx /= wl; wz /= wl;
        // the ladders and decks, a cell's worth to a piece
        var pieces = new Dictionary<int, List<(int x, int y, int z)>>();
        int ix = (int)x, iz = (int)z, ri = (int)MathF.Ceiling(r);
        for (int dz = -ri; dz <= ri; dz++)
            for (int dx = -ri; dx <= ri; dx++)
            {
                if (dx * dx + dz * dz > r * r) continue;
                int cx = ix + dx, cz = iz + dz;
                if (cx < 2 || cz < 2 || cx >= World.SX - 2 || cz >= World.SZ - 2) continue;
                // the whole column: a ladder is ground enough to stand on, so it is no use asking where the ground is
                for (int y = 4, top = Math.Min(World.SY - 2, World.ColTop(cx, cz)); y < top; y++)
                {
                    byte m = World.Get(cx, y, cz);
                    if (m != Mat.SiegeLadder && m != Mat.Deck) continue;
                    int key = Nav.CellOf(cx, y, cz);
                    if (!pieces.TryGetValue(key, out var list)) pieces[key] = list = new List<(int, int, int)>();
                    list.Add((cx, y, cz));
                }
            }
        int torn = 0, thrown = 0;
        foreach (var (key, list) in pieces)
        {
            float s = (10 + key % 5) * VoxelWorld.PerMetre;
            if (Loose(list, wx * s + wz * (key % 3 - 1) * 12, (5 + key % 4) * VoxelWorld.PerMetre, wz * s - wx * (key % 3 - 1) * 12, "чёрный ветер") != null) torn++;
        }
        foreach (var u in HordeIn(x, z, r))
        {
            // the lighter fly the farther; a troll goes a little way, chains hold fast
            float k = MathF.Min(1, 15 / u.T.BodyH);
            float s = (9 + u.Id % 5 * .8f) * VoxelWorld.PerMetre * k, up = (7 + u.Id % 3) * VoxelWorld.PerMetre * MathF.Sqrt(k);
            if (Fling(u, wx * s, up, wz * s, "чёрный ветер") != null) thrown++;
        }
        if (torn + thrown == 0) return "в круге нет ни налётчиков, ни их лестниц";
        if (torn > 0) InvalidateFields();
        return null;
    }

    // ------------------------------------------------------------ the circle of death

    /// <summary>Raiders in the circle (not engines, not the bound), each of them.</summary>
    List<Unit> HordeIn(float x, float z, float r)
    {
        var list = new List<Unit>();
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Bound || u.Envoy || u.Team != Team.Horde || Kinds.IsEngine(u.T)) continue;
            if ((u.X - x) * (u.X - x) + (u.Z - z) * (u.Z - z) < r * r) list.Add(u);
        }
        return list;
    }

    string Terrify(float x, float z, float r)
    {
        var them = HordeIn(x, z, r);
        if (them.Count == 0) return "в круге нет налётчиков";
        int fled = 0;
        foreach (var u in them)
        {
            // a leader and a troll are made of harder stuff
            if ((u.Leader || u.T == Kinds.Troll) && Rng.NextDouble() < .5) continue;
            u.Routed = true; u.Raid = null; u.Path = null; u.Target = null;
            StopCarve(u); StopBuild(u);
            fled++;
        }
        Say(fled == 0 ? "Страх Божий не взял никого." : $"Страх Божий: бегут {fled} из {them.Count}.", fled > 0 ? 1 : 2, x, z);
        return null;
    }

    string Chain(float x, float z, float r)
    {
        var them = HordeIn(x, z, r);
        if (them.Count == 0) return "в круге нет налётчиков";
        foreach (var u in them) { u.ChainedUntil = Time + 10 * Might; u.Path = null; StopCarve(u); StopBuild(u); }
        return null;
    }

    /// <summary>The fallen of the horde in the circle stand up, ours for a minute.</summary>
    string RaiseDead(float x, float z, float r)
    {
        var fallen = new List<Unit>();
        foreach (var u in Units)
        {
            if (!u.Dead || u.Team != Team.Horde || Kinds.IsEngine(u.T) || u.DiedAt < 0 || Time - u.DiedAt > 30 || u.KilledBy == GivenToPowers) continue;
            if ((u.X - x) * (u.X - x) + (u.Z - z) * (u.Z - z) < r * r) fallen.Add(u);
        }
        if (fallen.Count == 0) return "здесь нет павших: тела лежат полминуты";
        fallen.Sort((a, b) => ((a.X - x) * (a.X - x) + (a.Z - z) * (a.Z - z)).CompareTo((b.X - x) * (b.X - x) + (b.Z - z) * (b.Z - z)));
        int n = 0;
        foreach (var c in fallen)
        {
            if (n >= 6) break;
            int cell = NearestStand((int)c.X, (int)c.Z, Kinds.Risen.Move.Height);
            if (cell < 0) continue;
            var r2 = Spawn(Kinds.Risen, cell);
            r2.CrumbleAt = Time + 60 * Might;
            r2.Facing = c.Facing;
            // the body it rose from is gone from where it lay
            c.DiedAt = -999;
            n++;
        }
        return n == 0 ? "павшим не встать: им негде стоять" : null;
    }

    /// <summary>
    /// One of the risen, or an angel of the host: at the nearest raider, and down it; after its minute, the risen falls
    /// apart and the angel goes up again, leaving nothing on the field.
    /// </summary>
    void RisenTick(Unit u, float dt)
    {
        if (Time >= u.CrumbleAt)
        {
            if (u.T != Kinds.Angel) { Kill(u, null, "рассыпался"); return; }
            Kill(u, null, AscendCause);
            u.DiedAt = -999;
            AngelRose?.Invoke(u.X, u.Y, u.Z);
            return;
        }
        const float Seek = 30 * VoxelWorld.PerMetre;
        if (u.Target == null || u.Target.Dead || u.Target.Bound || Time >= u.NextSearch)
        {
            u.NextSearch = Time + .7;
            u.Target = null; float bd = Seek;
            foreach (var o in Units)
            {
                if (o.Dead || o.Inside || o.Bound || o.Envoy || o.Lurking || o.Team != Team.Horde) continue;
                float d = Dist(u, o);
                if (d < bd) { bd = d; u.Target = o; }
            }
            if (u.Target != null && !InReach(u, u.Target) && !GoTo(u, u.Target.Cell, adjacent: true, budget: 6000)) u.Path = null;
        }
        var t = u.Target;
        if (t == null) { u.State = UnitState.Idle; u.Path = null; return; }
        if (InReach(u, t))
        {
            u.State = UnitState.Fight; u.Path = null;
            u.Facing = MathF.Atan2(t.Z - u.Z, t.X - u.X);
            if (u.Cooldown <= 0) { Strike(u, t); u.Cooldown = u.T.AttackTime * (.9f + (float)Rng.NextDouble() * .2f); }
            return;
        }
        if (u.Path != null && u.PathI < u.Path.Count) { u.State = UnitState.Walk; Walk(u, dt); }
        else u.State = UnitState.Idle;
    }

    /// <summary>The fog of the dead on the field, for 45 seconds.</summary>
    string Shroud(float x, float z, float r)
    {
        DeadFogs.Add(new Patch { X = x, Z = z, R = r, Until = Time + 45 * Might });
        return null;
    }

    /// <summary>Whether a spot lies in the fog of the dead.</summary>
    bool InDeadFog(float x, float z)
    {
        foreach (var f in DeadFogs) if ((x - f.X) * (x - f.X) + (z - f.Z) * (z - f.Z) < f.R * f.R) return true;
        return false;
    }

    /// <summary>What the fog of the dead and the plague take nothing from: engines, and the dead walking.</summary>
    static bool Deathless(Unit u) => Kinds.IsEngine(u.T) || u.T.Called;

    /// <summary>The plague on the raiders in the circle.</summary>
    string Sicken(float x, float z, float r)
    {
        var them = HordeIn(x, z, r);
        them.RemoveAll(Deathless);
        if (them.Count == 0) return "в круге нет налётчиков";
        foreach (var u in them) u.PlagueUntil = Time + PlagueFor * Might;
        return null;
    }

    /// <summary>How long the plague holds one it has taken, in seconds; how near it passes on, in voxels.</summary>
    const double PlagueFor = 20;
    const float PlagueReach = 2.5f * VoxelWorld.PerMetre;

    /// <summary>The blood oath: ours in the circle mend two fifths, stop bleeding, and strike harder for a minute.</summary>
    string Swear(float x, float z, float r)
    {
        int n = 0;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Team != Team.Player || u.T.Called) continue;
            if ((u.X - x) * (u.X - x) + (u.Z - z) * (u.Z - z) > r * r) continue;
            u.Hp = Math.Min(u.MaxHp, u.Hp + u.MaxHp * .4f);
            if (u.Wounds != null) u.Wounds.Bleed = 0;
            u.OathUntil = Time + 60 * Might;
            n++;
        }
        return n == 0 ? "в круге нет наших" : null;
    }

    /// <summary>How much harder one under the blood oath strikes and shoots.</summary>
    float Oath(Unit u) => u.OathUntil > Time ? 1.5f : 1;

    /// <summary>The call: the nearest of ours in the circle is in the citadel that instant, out of whatever held it.</summary>
    string CallHome(float x, float z, float r)
    {
        if (Gate < 0) return "у цитадели нет дверей";
        Unit one = null; float bd = r * r;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Team != Team.Player || u.T.Called) continue;
            float d = (u.X - x) * (u.X - x) + (u.Z - z) * (u.Z - z);
            if (d < bd) { bd = d; one = u; }
        }
        if (one == null) return "в круге нет наших";
        if (one.Airborne != null) { Fliers.Remove(one.Airborne); one.Airborne = null; }
        // out of the thief's hands; a prisoner it was leading stays where it is, bound
        if (one.Captor != null) { one.Captor.Captive = null; one.Captor = null; }
        if (one.Captive != null) { one.Captive.Captor = null; one.Captive = null; }
        StopCarve(one); StopBuild(one);
        one.Burn = 0;
        one.Cell = Gate;
        (one.X, one.Y, one.Z) = Nav.Feet(Gate);
        Enter(one);
        Say($"{one.T.Name} №{one.Id} отозван в цитадель.", 1);
        return null;
    }

    double _hexAt;

    /// <summary>Every half second: the fog of the dead and the plague take their toll, and the plague passes on.</summary>
    void TickHexes()
    {
        if (Time - _hexAt < .5) return;
        _hexAt = Time;
        if (Skins.Count > 0) Skins.RemoveAll(s => s.Until <= Time);
        if (DeadFogs.Count > 0) DeadFogs.RemoveAll(f => f.Until <= Time);
        TickMiasma();
        bool fog = DeadFogs.Count > 0;
        _sick.Clear();
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Team != Team.Horde || Deathless(u)) continue;
            if (u.PlagueUntil > Time) { _sick.Add(u); Hurt(u, null, Might, "мор"); }
            if (fog && !u.Dead && InDeadFog(u.X, u.Z)) Hurt(u, null, 1.5f * Might, "туман мёртвых");
        }
        if (_sick.Count == 0) return;
        // the plague passes to those standing by the sick, a chance in four each half second
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Bound || u.Team != Team.Horde || Deathless(u) || u.PlagueUntil > Time) continue;
            foreach (var s in _sick)
            {
                float dx = u.X - s.X, dz = u.Z - s.Z, dy = u.Y - s.Y;
                if (dx * dx + dz * dz + dy * dy > PlagueReach * PlagueReach) continue;
                if (Rng.NextDouble() < .25) u.PlagueUntil = Time + PlagueFor * Might;
                break;
            }
        }
    }

    readonly List<Unit> _sick = new();

    // ------------------------------------------------------------ the circle of the will

    /// <summary>The blast of the spell, at the voxel aimed at (see <see cref="Detonate"/>).</summary>
    string Blast(int x, int y, int z, float r) =>
        Detonate(x + .5f, y + .5f, z + .5f, r, 60 * Might, "взрыв") > 0 ? null : "здесь нечему взрываться: наведите на стену, скалу, землю или постройку";

    /// <summary>What the spells keep doing: the earth rising, the pitch coming down, the bogs drying up, the fog and the plague.</summary>
    void TickSpells()
    {
        if (_risenEarth < _rising.Count) RiseNow((int)(_rising.Count * Math.Clamp((Time - _riseFrom) / RiseFor, 0, 1)) + 1);
        for (int i = Drops.Count - 1; i >= 0; i--)
            if (Drops[i].At <= Time) { Land(Drops[i]); Drops.RemoveAt(i); }
        if (Mires.Count > 0) Mires.RemoveAll(m => m.Until <= Time);
        if (Whirls.Count > 0) TickWhirls();
        TickHexes();
    }
}
