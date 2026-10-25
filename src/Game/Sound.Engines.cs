using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Our engines loosing: a deep thud of the beam or the arm coming up against its stop, and the timbers creaking with
/// it; a ballista's string cracks dry and short.
/// </summary>
public partial class Sound
{
    void HookEngines(Sim sim) => sim.EngineLoosed += u =>
    {
        bool treb = u.T == Kinds.Trebuchet, bal = u.T == Kinds.Ballista;
        Play("stomp", u.X, u.Y, u.Z, .1, treb ? 1.2f : bal ? .45f : .85f, far: treb, pitch: treb ? .7f : bal ? 1.6f : 1f);
        Play("creak", u.X, u.Y + 4, u.Z, .1, bal ? .3f : .7f, pitch: treb ? .8f : 1.1f);
        if (bal) Play("bow", u.X, u.Y + 2, u.Z, .05, .9f, pitch: .55f);
    };
}
