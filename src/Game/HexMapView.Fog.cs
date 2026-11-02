using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// What of the world map is seen, and the small map of all of it. The fog is a texture of half-hexes by rows
/// (HexMapLayout's fog grid), read by the shaders straight from a voxel's place: a hex newly seen comes out of the dark
/// over half a second, not at once. The small map in the lower left corner is the same grid in the hexes' colours
/// (HexFar.cs), the fog over it as dark, the things held marked blue and red, the heroes as dots, the ground the eye
/// sees as a frame; a click or a drag on it takes the eye there.
/// </summary>
public partial class HexMapView
{
    Image _fogImg;
    ImageTexture _fogTex;
    byte[] _fogPx;
    float[] _fogNow;
    bool[] _fogWant;
    bool _fogDirty, _fogMoving;
    /// <summary>How quickly a hex comes out of the dark, a share a second.</summary>
    const float Lift = 2.2f;

    void FogReady()
    {
        var lay = Layout;
        int n = lay.Cols * lay.Rows;
        _fogNow = new float[n];
        _fogWant = new bool[n];
        for (int r = 0; r < lay.Rows; r++)
            for (int c = 0; c < lay.Cols; c++)
            {
                bool s = Source.Seen(c, r);
                _fogWant[r * lay.Cols + c] = s;
                _fogNow[r * lay.Cols + c] = s ? 1 : 0;
            }
        _fogPx = new byte[lay.FogW * lay.FogH];
        _fogImg = Image.CreateEmpty(lay.FogW, lay.FogH, false, Image.Format.L8);
        _fogTex = ImageTexture.CreateFromImage(_fogImg);
        Bake();
    }

    /// <summary>What is seen kept up: the hexes' want read again when the source said something changed, the dark lifted (or come down) smoothly.</summary>
    void Fog(float dt)
    {
        var lay = Layout;
        if (_fogDirty)
        {
            _fogDirty = false;
            for (int r = 0; r < lay.Rows; r++)
                for (int c = 0; c < lay.Cols; c++)
                {
                    bool s = Source.Seen(c, r);
                    int i = r * lay.Cols + c;
                    if (_fogWant[i] != s) { _fogWant[i] = s; _fogMoving = true; }
                }
            _marksStale = true;
            _miniStale = true;
        }
        if (!_fogMoving) return;
        bool still = true;
        float step = dt * Lift;
        for (int i = 0; i < _fogNow.Length; i++)
        {
            float want = _fogWant[i] ? 1 : 0;
            if (_fogNow[i] == want) continue;
            _fogNow[i] = want > _fogNow[i] ? Math.Min(want, _fogNow[i] + step) : Math.Max(want, _fogNow[i] - step);
            if (_fogNow[i] != want) still = false;
        }
        Bake();
        if (still) _fogMoving = false;
    }

    /// <summary>The fog's texture from what each hex has of it now; the margin dark.</summary>
    void Bake()
    {
        var lay = Layout;
        for (int j = 0; j < lay.FogH; j++)
            for (int i = 0; i < lay.FogW; i++)
            {
                var (c, r) = lay.FogHex(i, j);
                float v = c < 0 ? 0 : _fogNow[r * lay.Cols + c];
                // eased, so the edge of the seen is soft but the seen is clear
                v = v * v * (3 - 2 * v);
                _fogPx[j * lay.FogW + i] = (byte)(v * 255);
            }
        _fogImg.SetData(lay.FogW, lay.FogH, false, Image.Format.L8, _fogPx);
        _fogTex.Update(_fogImg);
    }

    // ------------------------------------------------------------ the hexes' sides shown more plainly

    Image _gridImg;
    ImageTexture _gridTex;
    byte[] _gridPx;
    readonly HashSet<(int, int)> _gridPath = new(), _gridReach = new();
    bool _gridDirty;

    void GridReady()
    {
        var lay = Layout;
        _gridPx = new byte[lay.FogW * lay.FogH];
        _gridImg = Image.CreateEmpty(lay.FogW, lay.FogH, false, Image.Format.L8);
        _gridTex = ImageTexture.CreateFromImage(_gridImg);
        _gridPath.Clear(); _gridReach.Clear();
        _gridDirty = true;
    }

    /// <summary>The hexes a hero can get to today: their sides drawn plainly, the ground over them a little brighter, as in «Heroes».</summary>
    public void ShowReach(IEnumerable<(int Col, int Row)> hexes)
    {
        _gridReach.Clear();
        if (hexes != null) foreach (var h in hexes) _gridReach.Add(h);
        _gridDirty = true;
    }

    void GridPath(IReadOnlyList<(int Col, int Row, int Day)> steps)
    {
        _gridPath.Clear();
        if (steps != null) foreach (var s in steps) _gridPath.Add((s.Col, s.Row));
        _gridDirty = true;
    }

    /// <summary>The texture of how plainly each hex's sides show made again, if anything changed: the way laid out most, today's reach less.</summary>
    void GridBake()
    {
        if (!_gridDirty || _gridPx == null) return;
        _gridDirty = false;
        var lay = Layout;
        Array.Clear(_gridPx);
        void Mark((int c, int r) h, byte v)
        {
            if (!lay.OnField(h.c, h.r)) return;
            int j = h.r + HexMapLayout.MarginRows, i = 2 * h.c + HexMapLayout.MarginHalves + (h.r & 1);
            for (int k = 0; k < 2; k++) if (i + k < lay.FogW) _gridPx[j * lay.FogW + i + k] = Math.Max(_gridPx[j * lay.FogW + i + k], v);
        }
        foreach (var h in _gridReach) Mark(h, 46);
        foreach (var h in _gridPath) Mark(h, 255);
        _gridImg.SetData(lay.FogW, lay.FogH, false, Image.Format.L8, _gridPx);
        _gridTex.Update(_gridImg);
    }

    // ------------------------------------------------------------ the small map

    Minimap _mini;
    bool _miniStale = true;
    /// <summary>The dots on the small map: where the heroes and the horde's armies are (the map's voxels) and their colour.</summary>
    public Func<IEnumerable<(float X, float Z, Color C)>> MiniDots;
    /// <summary>The small map, and how many times a click on it sent the eye somewhere (the tutorial).</summary>
    public Control MiniMap => _mini;
    public int MiniJumps;

    void MiniReady()
    {
        _mini = new Minimap(this);
        _mini.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        _ui.AddChild(_mini);
    }

    /// <summary>Whether the small map is shown large ({MapBig}).</summary>
    public bool MiniBig { get; private set; }

    /// <summary>The small map shown large over the lower left corner, or back to its size.</summary>
    public void ToggleMiniBig()
    {
        MiniBig = !MiniBig;
        if (Layout != null) MiniLay();
    }

    void MiniLay()
    {
        // the small map 260 wide and 230 high, the land that is known filling it (Minimap.View); large, as much of the
        // screen's height as leaves the beam over it, and half its width at most
        var vp = GetViewport().GetVisibleRect().Size;
        float w = 272, h = 242;
        if (MiniBig) { float k = MathF.Min((vp.Y - 120) / h, vp.X * .5f / w); w *= k; h *= k; }
        _mini.OffsetLeft = 16; _mini.OffsetRight = 16 + w;
        _mini.OffsetTop = -16 - h; _mini.OffsetBottom = -16;
        _miniStale = true;
    }

    bool OverMini(Vector2 p) => _mini != null && _mini.Visible && _mini.GetGlobalRect().HasPoint(p);

    /// <summary>Where the eye is on the small map, and where the heroes are, for the self-test: a point of the map (voxels) as the small map shows it.</summary>
    public Vector2 MiniPoint(float x, float z) => _mini.GetGlobalRect().Position + _mini.ToMini(x, z);
    /// <summary>The part of the map the small map shows (its voxels): the land that is known and a little about it.</summary>
    public Rect2 MiniView => _mini.View;

    sealed partial class Minimap : Control
    {
        readonly HexMapView _v;
        Image _img;
        ImageTexture _tex;
        byte[] _px;
        public Minimap(HexMapView v) { _v = v; MouseFilter = MouseFilterEnum.Stop; TextureFilter = TextureFilterEnum.Nearest; }

        static readonly Color Dark = new("15131a");

        /// <summary>
        /// The part of the map it shows, in the map's voxels: the land that is known, two hexes about it, at least a
        /// dozen hexes across, so that the start of a campaign fills the small map rather than a speck of it.
        /// </summary>
        Rect2 _view;

        /// <summary>What it shows (the whole map until it is first painted).</summary>
        public Rect2 View => _view.Size.X > 0 ? _view : new Rect2(0, 0, _v.Layout.FogW * HexMapLayout.HalfW, _v.Layout.FogH * HexMapLayout.RowStep);
        float Scale => MathF.Min((Size.X - 12) / View.Size.X, (Size.Y - 12) / View.Size.Y);
        Vector2 Corner => new Vector2(6, 6) + ((Size - new Vector2(12, 12)) - View.Size * Scale) / 2;
        public Vector2 ToMini(float x, float z) => Corner + (new Vector2(x, z) - View.Position) * Scale;
        Vector2 FromMini(Vector2 p) => (p - Corner) / Scale + View.Position;

        /// <summary>The picture of the small map made again: each half-hex its hex's colour, dark in the fog, a held thing its holder's colour.</summary>
        void Paint()
        {
            var lay = _v.Layout;
            if (_px == null || _px.Length != lay.FogW * lay.FogH * 3)
            {
                _px = new byte[lay.FogW * lay.FogH * 3];
                _img = Image.CreateEmpty(lay.FogW, lay.FogH, false, Image.Format.Rgb8);
                _tex = ImageTexture.CreateFromImage(_img);
            }
            int i0 = int.MaxValue, j0 = int.MaxValue, i1 = -1, j1 = -1;
            for (int j = 0; j < lay.FogH; j++)
                for (int i = 0; i < lay.FogW; i++)
                {
                    var (c, r) = lay.FogHex(i, j);
                    uint col = 0x15131a;
                    if (c >= 0 && _v.Source.Seen(c, r))
                    {
                        i0 = Math.Min(i0, i); i1 = Math.Max(i1, i); j0 = Math.Min(j0, j); j1 = Math.Max(j1, j);
                        var l = _v.Source.Look(c, r);
                        col = HexFar.ColourOf(l);
                        if (l.Owner == 1) col = 0x5b86c8;
                        else if (l.Owner == 2) col = 0xd24a33;
                        else if (l.Icon is >= HexIcon.MineLogs and <= HexIcon.MineGems) col = 0xe2b84a;
                    }
                    else if (c < 0) col = 0x0e0d12;
                    int k = (j * lay.FogW + i) * 3;
                    _px[k] = (byte)(col >> 16); _px[k + 1] = (byte)(col >> 8); _px[k + 2] = (byte)col;
                }
            _img.SetData(lay.FogW, lay.FogH, false, Image.Format.Rgb8, _px);
            _tex.Update(_img);
            // what it shows: the known land and two hexes about it, a dozen hexes across at the least, within the map
            float mw = lay.FogW * HexMapLayout.HalfW, mh = lay.FogH * HexMapLayout.RowStep;
            if (i1 < 0) { _view = new Rect2(0, 0, mw, mh); return; }
            float hx = 2 * HexMapLayout.HalfW, hz = HexMapLayout.RowStep;
            float x0 = i0 * HexMapLayout.HalfW - 2 * hx, x1 = (i1 + 1) * HexMapLayout.HalfW + 2 * hx;
            float z0 = j0 * HexMapLayout.RowStep - 2 * hz, z1 = (j1 + 1) * HexMapLayout.RowStep + 2 * hz;
            float least = 12 * hx;
            if (x1 - x0 < least) { float mx = (x0 + x1) / 2; x0 = mx - least / 2; x1 = mx + least / 2; }
            if (z1 - z0 < least) { float mz = (z0 + z1) / 2; z0 = mz - least / 2; z1 = mz + least / 2; }
            // the box's own shape, so that the map is not squeezed: the shorter way made longer
            float want = (Size.X - 12) / Math.Max(1, Size.Y - 12);
            if ((x1 - x0) / (z1 - z0) < want) { float mx = (x0 + x1) / 2, half = (z1 - z0) * want / 2; x0 = mx - half; x1 = mx + half; }
            else { float mz = (z0 + z1) / 2, half = (x1 - x0) / want / 2; z0 = mz - half; z1 = mz + half; }
            _view = new Rect2(x0, z0, x1 - x0, z1 - z0);
        }

        public override void _Draw()
        {
            if (_v.Layout == null) return;
            if (_v._miniStale || _tex == null) { Paint(); _v._miniStale = false; }
            DrawRect(new Rect2(Vector2.Zero, Size), Ornament.Fade(Ornament.Iron, .92f));
            DrawRect(new Rect2(Vector2.Zero, Size), Ornament.Fade(Ornament.Bronze, .6f), false, 1);
            var lay = _v.Layout;
            // the known land, cut to the box
            var inside = new Rect2(Vector2.One * 2, Size - Vector2.One * 4);
            var full = new Rect2(ToMini(0, 0), new Vector2(lay.FogW * HexMapLayout.HalfW, lay.FogH * HexMapLayout.RowStep) * Scale);
            var shown = full.Intersection(inside);
            if (shown.Size.X > 0 && shown.Size.Y > 0)
            {
                var src = new Rect2((shown.Position - full.Position) / full.Size * new Vector2(lay.FogW, lay.FogH), shown.Size / full.Size * new Vector2(lay.FogW, lay.FogH));
                DrawTextureRectRegion(_tex, shown, src);
            }
            if (_v.MiniDots != null)
                foreach (var (x, z, c) in _v.MiniDots())
                {
                    var p = ToMini(x, z);
                    DrawCircle(p, 3.2f, new Color(0, 0, 0, .7f));
                    DrawCircle(p, 2.4f, c);
                }
            // the ground the eye sees: the four corners of the screen on the level ground
            var size = _v.GetViewport().GetVisibleRect().Size;
            var q = new Vector2[5];
            var at = new[] { Vector2.Zero, new Vector2(size.X, 0), size, new Vector2(0, size.Y) };
            for (int i = 0; i < 4; i++)
            {
                var p = _v.Plane(at[i]) / VoxelWorld.Vox;
                q[i] = ToMini(p.X, p.Z);
                q[i] = new Vector2(Math.Clamp(q[i].X, 0, Size.X), Math.Clamp(q[i].Y, 0, Size.Y));
            }
            q[4] = q[0];
            DrawPolyline(q, Ornament.Parchment, 1);
        }

        public override void _GuiInput(InputEvent e)
        {
            if (_v.Layout == null) return;
            Vector2? at = e switch
            {
                InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } b => b.Position,
                InputEventMouseMotion { ButtonMask: MouseButtonMask.Left } m => m.Position,
                _ => null,
            };
            if (at is not { } pos) return;
            if (e is InputEventMouseButton) _v.MiniJumps++;
            var v = FromMini(pos);
            _v.Focus(v.X, v.Y);
            AcceptEvent();
        }
    }
}
