using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>The gods the sides can call up out of the earth: ours, and the horde's.</summary>
public enum DeityKind : byte { Colossus, Devourer }

/// <summary>
/// What a god is at: breaking up out of the ground, standing, walking; one of its blows — the arm swept along the
/// ground, the fist brought down, a lump of the land torn up and thrown, the roar; going back under; fallen to pieces.
/// </summary>
public enum DeityAct : byte { Rising, Standing, Walking, Sweep, Slam, Lift, Throw, Roar, Sinking, Fallen }

/// <summary>
/// A kind of god: whose it is, how great, how it walks and strikes, how long the world holds it. The greatest thing in
/// the valley by far: a god a quarter of the valley high, nothing of either side stands against it but another god.
/// </summary>
public sealed class DeityType
{
    public DeityKind Kind;
    public string Key = "", Name = "";
    public Team Team;
    /// <summary>How tall it stands, voxels; how far apart its feet are planted and how far a stride takes a foot; how far a foot reaches from its middle.</summary>
    public float Height, Stance, Stride, Foot;
    /// <summary>Voxels a second it walks; radians a second it turns.</summary>
    public float Speed, Turn;
    /// <summary>How far its hand reaches in a sweep from its middle; how near and how far before it it brings the fist down.</summary>
    public float Reach, SlamNear, SlamFar;
    public float Hp;
    /// <summary>Seconds the world holds it before it goes back under.</summary>
    public double Lasts;
    /// <summary>Fire in it: what it strikes catches, its craters hold lava, its cracks glow.</summary>
    public bool Fiery;
    /// <summary>What it is called in the chronicle when it kills or breaks, «колосс», «пожиратель».</summary>
    public string Cause = "";
}

/// <summary>
/// A god in the valley. It stands between its two feet, each planted on the ground where it last came down; walking, one
/// foot is in the air at a time on its way from behind to ahead, and the body goes on between them. Its blows are acts
/// that take their time and land at one moment of it (the sweep along a stretch of it); the view poses it by the act
/// and how far through it is.
/// </summary>
public sealed class Deity
{
    public int Id;
    public DeityType T;
    public Team Team => T.Team;
    /// <summary>Its middle between its feet, on the ground (voxels), and the way it faces (as a unit's facing).</summary>
    public float X, Y, Z, Facing;
    public float Hp;
    /// <summary>When it came up, and when it goes back under.</summary>
    public double Born, Until;
    public DeityAct Act;
    public double ActFrom;
    /// <summary>What the act is at: the spot it strikes or throws at; which way a sweep goes (+1 from its right across to its left).</summary>
    public float AimX, AimY, AimZ;
    public int Side = 1;
    /// <summary>The act's blow has landed (a sweep: has begun to).</summary>
    public bool Landed;
    /// <summary>Its feet: where each stands, on the ground under it (voxels).</summary>
    public readonly float[] FootX = new float[2], FootY = new float[2], FootZ = new float[2];
    /// <summary>The foot in the air (−1 none), where it rose from, where it comes down, how high it goes, and since when.</summary>
    public int Lifted = -1;
    public float FromX, FromY, FromZ, ToX, ToY, ToZ, Lift;
    public double StepFrom, StepFor;
    /// <summary>How far up out of the ground it has come: 0 under it, 1 standing on it.</summary>
    public float Up;
    /// <summary>The hand that strikes, where it is now (voxels): for the blows and for the view.</summary>
    public float HandX, HandY, HandZ;
    /// <summary>A lump of the land in its hand, torn up to be thrown.</summary>
    public Flier Held;
    /// <summary>The player is in it.</summary>
    public bool Ridden;
    /// <summary>Where it means to go (NaN: nowhere), and when it next looks about for what to do.</summary>
    public float GoX = float.NaN, GoZ = float.NaN;
    public double NextThink;
    /// <summary>The god of the other side it is at, if one is in the valley.</summary>
    public Deity Foe;
    /// <summary>Those it has killed, and voxels of the world it has broken.</summary>
    public int Kills, Broke;
    /// <summary>Gone from the world (back under, or fallen to pieces): the valley forgets it.</summary>
    public bool Gone;
    /// <summary>What it tore a lump up for: the spot it throws it at, left to itself.</summary>
    public float ThrowX, ThrowZ;
    /// <summary>Where the hand was the tick before, in a sweep; whether the sweep has struck the other god yet.</summary>
    public float LastHandX, LastHandZ;
    public bool FoeStruck;
    /// <summary>The ground it broke out of: what is still to go up out of the pit, top first.</summary>
    internal List<(int x, int y, int z)> Pit;
    internal int PitAt, Bursts;

    /// <summary>Seconds into the act.</summary>
    public float Into(double now) => (float)(now - ActFrom);
}

/// <summary>The gods there are.</summary>
public static class Deities
{
    const float M = VoxelWorld.PerMetre;

    /// <summary>
    /// The Colossus: the old god of the land, chained under it, the household's to wake. Forty-eight metres of rock
    /// bound with roots and broken iron, a crown of crystal; its blows are the weight of a hill.
    /// </summary>
    public static readonly DeityType Colossus = new()
    {
        Kind = DeityKind.Colossus, Key = "colossus", Name = "Колосс", Team = Team.Player, Cause = "колосс",
        Height = 48 * M, Stance = 9 * M, Stride = 13 * M, Foot = 3.2f * M, Speed = 3.6f * M, Turn = .5f,
        Reach = 24 * M, SlamNear = 9 * M, SlamFar = 22 * M, Hp = 40000, Lasts = 180,
    };

    /// <summary>
    /// The Devourer: the horde's god, a hunched horned thing of black glass with fire inside it, forty metres of it.
    /// Where it strikes the ground burns, and its craters are left full of lava.
    /// </summary>
    public static readonly DeityType Devourer = new()
    {
        Kind = DeityKind.Devourer, Key = "devourer", Name = "Пожиратель", Team = Team.Horde, Cause = "пожиратель",
        Height = 40 * M, Stance = 11 * M, Stride = 12 * M, Foot = 3.6f * M, Speed = 4.2f * M, Turn = .6f,
        Reach = 22 * M, SlamNear = 8 * M, SlamFar = 20 * M, Hp = 36000, Lasts = 240, Fiery = true,
    };

    public static DeityType Of(DeityKind k) => k == DeityKind.Colossus ? Colossus : Devourer;
}
