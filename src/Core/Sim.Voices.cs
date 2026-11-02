using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>What is said aloud: a greeting, a word in talk, a laugh, a call at work, a commander's praise, a cheer, the horde's chant and its roar; the town's beasts' bark, cluck, whinny and bleat.</summary>
public enum Voice : byte { Greet, Chat, Laugh, Call, Praise, Cheer, Chant, Roar, Bark, Cluck, Neigh, Bleat }

/// <summary>
/// The valley's voices. Two of ours passing close greet each other, a nod or a raised hand and now and then a word for
/// the hour («Доброе утро!», «Бог в помощь!» to one at work); in a ring the one with the word says something now and
/// then and another may laugh; at dice the thrower calls the throw; a worker calls to the one working beside it, and
/// «Камень!» when something built comes down near people; after a raid beaten back the commanders praise their people
/// and the soldiers cheer. The horde chants as a band gathers where it came in, and roars as it goes in to storm. Only
/// words: nothing said changes what anyone does, and the leisure's own dice choose them, not the simulation's.
/// </summary>
public sealed partial class Sim
{
    /// <summary>Someone said something aloud: the view puts the words over its head and the sound gives it a voice.</summary>
    public event Action<Unit, string, Voice> Spoke;
    /// <summary>Words said aloud so far, and greetings given (a nod or a hand, with a word or without), for the bench.</summary>
    public int WordsSaid, Greetings;

    double _nextVoices, _lastWord, _lastCall;
    readonly Dictionary<long, double> _greeted = new();
    readonly List<(double at, Unit u, string word, Voice v)> _later = new();
    readonly Dictionary<int, double> _chantAt = new();
    readonly HashSet<int> _roared = new();

    static readonly string[] Hellos = { "Здравствуй!", "Здорово!", "Привет!", "Здравия!" };
    static readonly string[] SoldierTalk =
    {
        "Тихо сегодня.", "Смотри в оба.", "Есть хочу.", "Сапоги жмут.", "Скоро смена.", "Орда не спит.", "Где командир?",
        "Ночью было холодно.", "Лук бы новый.", "Слышал вой ночью?", "Стена крепкая.", "Ещё постоим.",
    };
    static readonly string[] WorkerTalk =
    {
        "Камня мало.", "Хорошая стена.", "Спина болит.", "Дождь будет.", "Завтра достроим.", "Дерево сырое.", "Глянь, какой дом.",
        "Пора обедать.", "Брёвна кончаются.", "Кирка тупая.", "Тут ров бы.", "Тихо в долине.",
    };
    static readonly string[] EveningTalk = { "Огонь греет.", "Спать пора.", "Длинный был день.", "Звёзды ясные.", "Завтра рано вставать." };
    static readonly string[] NoonTalk = { "Хлеб свежий.", "Похлёбка горячая.", "Передай соль.", "Вкусно." };
    static readonly string[] DiceWords = { "Шесть!", "Две!", "Моя взяла!", "Ещё раз!", "Опять ты!", "Пять!", "Не везёт." };
    static readonly string[] Laughs = { "Ха-ха!", "Ха!", "Хе-хе." };
    static readonly string[] LayCalls = { "Подай камень!", "Ровнее!", "Держи!", "Ещё ряд!" };
    static readonly string[] DigCalls = { "Глубже!", "Ещё немного!", "Тяжёлая земля." };
    static readonly string[] Praises = { "Славно бились!", "Молодцы!", "Выстояли!" };

    /// <summary>Says something aloud now.</summary>
    void Speak(Unit u, string word, Voice v)
    {
        if (u == null || u.Dead || u.Inside) return;
        WordsSaid++;
        Spoke?.Invoke(u, word, v);
    }

    /// <summary>Has one say something aloud now (for the scenes staged and the tests).</summary>
    public void SayAloud(Unit u, string word, Voice v) => Speak(u, word, v);

    /// <summary>Says something aloud a moment from now (a cheer after the praise, a laugh after the word).</summary>
    void SpeakLater(double after, Unit u, string word, Voice v) => _later.Add((Time + after, u, word, v));

    T Any<T>(T[] of) => of[_life.Next(of.Length)];

    /// <summary>Four times a second: words said later, greetings, a call at work, the horde's chant and its roar.</summary>
    void TickVoices()
    {
        for (int i = _later.Count - 1; i >= 0; i--)
            if (Time >= _later[i].at) { var (_, u, word, v) = _later[i]; _later.RemoveAt(i); Speak(u, word, v); }
        if (Time < _nextVoices) return;
        _nextVoices = Time + .25;
        Greet();
        WorkCalls();
        HordeVoices();
    }

    /// <summary>One of ours who greets and is greeted: on its feet, about, not asleep, praying or led away.</summary>
    static bool Greeter(Unit u) => !u.Dead && !u.Inside && u.Team == Team.Player && Sleeper(u) && !u.Bound && u.Captor == null && u.Airborne == null
        && u.State is UnitState.Walk or UnitState.Idle or UnitState.Work && u.Pastime is not (Pastime.Sleep or Pastime.Pray or Pastime.Kneel);

    /// <summary>
    /// Two of ours passing close (within 3 m), one of them walking: they greet each other, the same two not again for two
    /// minutes; one in three says a word for the hour.
    /// </summary>
    void Greet()
    {
        foreach (var u in Units)
        {
            if (!Greeter(u) || u.Path == null || u.PathI >= u.Path.Count || Time - u.GreetAt < 6) continue;
            foreach (var v in Units)
            {
                if (v == u || !Greeter(v) || Time - v.GreetAt < 6) continue;
                float dx = v.X - u.X, dz = v.Z - u.Z, d2 = dx * dx + dz * dz;
                if (d2 > 24 * 24 || d2 < 4 * 4 || MathF.Abs(v.Y - u.Y) > 6) continue;
                long key = u.Id < v.Id ? (long)u.Id << 32 | (uint)v.Id : (long)v.Id << 32 | (uint)u.Id;
                if (_greeted.TryGetValue(key, out double was) && Time - was < 120) continue;
                _greeted[key] = Time;
                u.GreetAt = v.GreetAt = Time;
                u.GreetTo = v; v.GreetTo = u;
                Greetings++;
                if (_life.NextDouble() < .35 && Time - _lastWord > 1.2)
                {
                    _lastWord = Time;
                    Speak(u, Hello(v), Voice.Greet);
                    // and a word back, now and then
                    if (v.State != UnitState.Work && _life.NextDouble() < .5) SpeakLater(.7 + _life.NextDouble() * .4, v, Hello(u), Voice.Greet);
                }
                break;
            }
        }
        if (_greeted.Count > 600)
        {
            var old = new List<long>();
            foreach (var (k, at) in _greeted) if (Time - at > 120) old.Add(k);
            foreach (var k in old) _greeted.Remove(k);
        }
    }

    /// <summary>A greeting for the hour, to one at work or to one passing.</summary>
    string Hello(Unit to)
    {
        if (to.State == UnitState.Work) return "Бог в помощь!";
        if (Clock.DawnLeft > 0 || !Clock.Night && Clock.Share < .12f) return "Доброе утро!";
        if (Clock.Darkness > .3f) return "Добрый вечер!";
        if (to.T == Kinds.Captain) return "Командир!";
        return Any(Hellos);
    }

    /// <summary>The word passed round a ring: now and then the one who has it says something, and another laughs; at dice the throw is called.</summary>
    void Chatter(Huddle g)
    {
        var s = g.Speaker;
        if (s == null || Time - _lastWord < .8) return;
        if (g.Does == Pastime.Dice)
        {
            if (_life.NextDouble() < .45) { _lastWord = Time; Speak(s, Any(DiceWords), Voice.Chat); }
            return;
        }
        if (_life.NextDouble() >= .5) return;
        _lastWord = Time;
        string[] lines = Clock.Darkness > .3f ? EveningTalk : Noon ? NoonTalk : s.T.Work < 1 ? SoldierTalk : WorkerTalk;
        Speak(s, Any(lines), Voice.Chat);
        if (_life.NextDouble() < .3)
            foreach (var m in g.Members)
                if (m != s) { SpeakLater(.9 + _life.NextDouble() * .5, m, Any(Laughs), Voice.Laugh); break; }
    }

    /// <summary>Now and then a worker at work calls to another working beside it (within 4 m), by the work: laying, or digging.</summary>
    void WorkCalls()
    {
        if (Time - _lastCall < 12 || _life.NextDouble() > .04) return;
        var working = new List<Unit>();
        foreach (var u in Units)
            if (!u.Dead && !u.Inside && u.Team == Team.Player && u.T == Kinds.Worker && u.State == UnitState.Work && u.JobI < u.Jobs.Count) working.Add(u);
        if (working.Count < 2) return;
        var a = working[_life.Next(working.Count)];
        foreach (var b in working)
        {
            if (b == a || (b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z) > 32 * 32) continue;
            bool dig = Orders.IsRemove(a.Jobs[a.JobI].m);
            _lastCall = Time;
            Speak(a, Any(dig ? DigCalls : LayCalls), Voice.Call);
            return;
        }
    }

    /// <summary>Something built came down (n voxels at x, y, z): the nearest of ours within 24 m, not under it, calls «Камень!».</summary>
    void CallStone(int n, float x, float y, float z)
    {
        if (n < 40 || Time - _lastCall < 3) return;
        Unit best = null;
        float bd = 192 * 192;
        foreach (var u in Units)
        {
            if (!Greeter(u)) continue;
            float d = (u.X - x) * (u.X - x) + (u.Z - z) * (u.Z - z);
            if (d < bd && d > 4 * 4 * 64) { bd = d; best = u; }
        }
        if (best == null) return;
        _lastCall = Time;
        Speak(best, "Камень!", Voice.Call);
    }

    /// <summary>
    /// A raid beaten back: each commander praises its people, and soldiers about cheer a moment after (with no commander,
    /// they cheer all the same).
    /// </summary>
    void Cheer()
    {
        var soldiers = new List<Unit>();
        foreach (var u in Units)
            if (!u.Dead && !u.Inside && u.Team == Team.Player && Fighter(u) && u.T != Kinds.Captain && !u.Down && Sleeper(u)) soldiers.Add(u);
        foreach (var cap in Units)
            if (!cap.Dead && !cap.Inside && cap.Team == Team.Player && cap.T == Kinds.Captain) Speak(cap, Any(Praises), Voice.Praise);
        for (int i = 0; i < Math.Min(6, soldiers.Count); i++)
        {
            var s = soldiers[_life.Next(soldiers.Count)];
            SpeakLater(.5 + i * .25 + _life.NextDouble() * .3, s, "Ура!", Voice.Cheer);
        }
    }

    /// <summary>
    /// The horde: a band gathering where it came in chants on a beat, two or three of it heard each time; a band going in
    /// to storm roars, its leader and three more.
    /// </summary>
    void HordeVoices()
    {
        foreach (var b in Bands)
        {
            if (b.Leader == null || b.Leader.Dead) continue;
            if (b.State == BandState.Forming)
            {
                if (_chantAt.TryGetValue(b.Id, out double next) && Time < next) continue;
                _chantAt[b.Id] = Time + 1.5;
                int said = 0;
                foreach (var m in b.Members)
                    if (!m.Dead && !m.Inside && m.State == UnitState.Idle && _life.NextDouble() < .3 && said++ < 3) Speak(m, "Хо!", Voice.Chant);
            }
            else if (b.State == BandState.Storm && Time - b.Since < 1 && _roared.Add(b.Id))
            {
                Speak(b.Leader, "Арра!", Voice.Roar);
                int n = 0;
                foreach (var m in b.Members)
                    if (m != b.Leader && !m.Dead && !m.Inside && n++ < 3) SpeakLater(.2 + n * .2, m, "Ваа!", Voice.Roar);
            }
        }
        if (_chantAt.Count > 64) _chantAt.Clear();
    }
}
