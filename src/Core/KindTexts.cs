using System;
using System.Globalization;

namespace VoxelTower.Core;

/// <summary>
/// What each kind of ours does, in the words the interface shows over it (its face in the console, its tile in the
/// muster, its hiring button): its name on the first line, then what it does. The numbers in them are the kinds' own
/// (how far it shoots or reaches, the places it takes, the logs it costs, its crew) and the sim's (what the lore adds),
/// put in from where they are kept, so that the words do not drift from the game; the bench checks them.
/// </summary>
public static class KindTexts
{
    /// <summary>Voxels as metres, as the interface writes them: «24», «1,88», «1,75».</summary>
    public static string M(float voxels) => (voxels / VoxelWorld.PerMetre).ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');
    /// <summary>A share as a whole percent: 1.25 → «25», 0.75 → «25».</summary>
    public static string Pct(double k) => Fmt.Int(Math.Abs(k - 1) * 100);
    static string Logs(UnitType t) => t.Logs.ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',');
    static string Room(UnitType t) => $"занимает {t.Room} {Sim.Plural(t.Room, "место", "места", "мест")}";
    static string Crew(UnitType t) => $"Выходит с расчётом из {t.Crew} человек, {Room(t)}";

    /// <summary>The words over a kind; just its name for one with none.</summary>
    public static string Tip(UnitType t)
    {
        const string All = " Щелчок — выбрать всех.";
        if (t == Kinds.Worker) return "Рабочий\nКопает, рубит, добывает камень и строит по разметке. От орды убегает в цитадель. Щелчок — выбрать всех, ещё раз — показать.";
        if (t == Kinds.Archer) return $"Лучник\nСтреляет на {M(t.Range)} м. Сначала бьёт тех, кто ломает стены и ставит лестницы. Со стены стреляет точнее. " +
            $"Ночью видит врага на {M(t.Range * (1 - Sim.DarkLoss))} м, дальше только освещённых. Лучше всех охотится." + All;
        if (t == Kinds.Swordsman) return $"Мечник\nДержит землю на {M(Sim.HoldGround)} м вокруг поста, сбрасывает осадные лестницы." + All;
        if (t == Kinds.Captain) return $"Командир\nСам ведёт бойцов в {M(Sim.CaptainReach)} м вокруг себя: строит их у стены, к которой идёт орда, держит " +
            "стрелков на башнях и стенах, отводит раненых. Четыре призыва: «За мной!», «Держать строй», «Залп!» и «Рог». Растёт в уровнях и выбирает умения." + All;
        if (t == Kinds.Architect) return "Архитектор\nОсматривает, что уже стоит вокруг цитадели, и сам размечает рабочим остальное: чинит старые стены, " +
            "закрывает проломы, ставит ворота к дороге, башни, ров и колья. После набега велит заделать то, что сломала орда. Один на долину." + All;
        if (t == Kinds.Priest) return "Жрец\nДнём молится в святилище и копит вмешательство, на месте силы втрое быстрее. Ночью уходит в цитадель. " +
            "Не работает и почти не дерётся." + All;
        if (t == Kinds.Spearman) return $"Копейщик\nБьёт копьём на {M(t.Reach)} м, дальше мечника. Сбрасывает осадные лестницы. «Длинные древки» " +
            $"добавляют {Pct(Sim.LongShafts)} % дальности." + All;
        if (t == Kinds.Shieldbearer) return $"Щитоносец\nСамый живучий пехотинец. Со «Стеной щитов» наши в {M(Sim.WallReach)} м от него получают на " +
            $"{Pct(Sim.ShieldCover)} % меньше урона." + All;
        if (t == Kinds.Halberdier) return $"Алебардщик\nАлебарда бьёт сильнее топора и достаёт на {M(t.Reach)} м." + All;
        if (t == Kinds.Knight) return "Рыцарь\nВ полных латах: мечи и стрелы орды почти его не ранят, опасны только дубины и палицы. Дорогой и медленный." + All;
        if (t == Kinds.Slinger) return $"Пращник\nДешёвый стрелок, мечет камни на {M(t.Range)} м. С «Ядрами и горшками» бьёт на {Pct(Sim.LeadShot)} % сильнее, отбрасывает налётчика " +
            "и иногда бросает горящую смолу." + All;
        if (t == Kinds.Crossbowman) return $"Арбалетчик\nБолт бьёт сильно и на {M(t.Range)} м, но перезарядка долгая. Знания о луках на него не действуют. " +
            "С «Бронебойными болтами» болт пробивает налётчика насквозь. Может стрелять огнём." + All;
        if (t == Kinds.Bombardier) return $"Бомбардир\nБросает горшки с порохом на {M(t.Range)} м. Взрыв раскидывает налётчиков и ломает их лестницы, " +
            "настилы и машины, а наши стены не трогает. Не бросает, если рядом с врагом свой." + All;
        if (t == Kinds.OurAlchemist) return $"Алхимик\nБросает склянки с кислотой на {M(t.Range)} м: сперва в лестницы под налётчиками, в машины и настилы " +
            "орды, потом в толпу. Кислота проедает их за секунду-другую и обжигает налётчиков, а нашу кладку не трогает. С «Бомбардирами» " +
            "бросает в толпу и едкий дым. Держится подальше от схватки." + All;
        if (t == Kinds.Hound) return $"Боевой пёс\nСамый быстрый, догоняет бегущих налётчиков. С «Травлей» за бегущим на {Pct(Sim.HuntPace)} % быстрее " +
            "и бьёт его вдвое сильнее. Не работает и не лазит по лестницам." + All;
        if (t == Kinds.Rider) return "Всадник\nБыстрый, догоняет бегущих. С «Натиском» удар с разгона вдвое сильнее и сбивает с ног. Не работает и не " +
            $"лазит по лестницам, {Room(t)}." + All;
        if (t == Kinds.HorseArcher) return $"Конный лучник\nСтреляет с коня на {M(t.Range)} м и уходит от погони. Знания о луках на него действуют. " +
            $"Не работает и не лазит по лестницам, {Room(t)}." + All;
        if (t == Kinds.Lancer) return $"Конный рыцарь\nКаждый удар с разгона вдвое сильнее и сбивает с ног. Не работает и не лазит по лестницам, {Room(t)}." + All;
        if (t == Kinds.TameTroll) return $"Ручной тролль\nУдар дубины отбрасывает налётчика, раны быстро заживают. С «Глыбами» бросает камни во врага, {Room(t)}." + All;
        if (t == Kinds.Giant) return $"Великан\nМолот отбрасывает врагов. С «Глыбами» бросает камни, которые катятся сквозь орду, {Room(t)}." + All;
        if (t == Kinds.Mangonel) return $"Катапульта\nМечет камни на {M(t.Range)} м: сначала в машины орды, потом в гущу ватаги. Камень ломает лестницы и " +
            $"настилы и катится сквозь ряды, наших не задевает. Стоит {Logs(t)} м³ брёвен. {Crew(t)}. {{Rmb}} — откатить на место." + All;
        if (t == Kinds.Ballista) return $"Баллиста\nКопьё летит прямо на {M(t.Range)} м, пробивает до 4 налётчиков в ряд, останавливает великана. " +
            $"Стоит {Logs(t)} м³ брёвен. {Crew(t)}." + All;
        if (t == Kinds.Trebuchet) return $"Требушет\nМечет тяжёлые камни на {M(t.Range)} м, дальше камнемёта орды ({M(Kinds.Catapult.Range)} м). Перед дорогой " +
            $"его разбирают, на месте собирают 8 с. Стоит {Logs(t)} м³ брёвен. {Crew(t)}." + All;
        if (t == Kinds.Crewman) return "Механик\nЗаряжает и катит свою машину. Без машины работает как рабочий.";
        return t.Name;
    }
}
