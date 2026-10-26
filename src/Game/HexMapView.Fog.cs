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

    // ------------------------------------------------------------ the small map

    Minimap _mini;
    bool _miniStale = true;
    /// <summary>The dots on the small map: where the heroes and the horde's armies are (the map's voxels) and their colour.</summary>
    public Func<IEnumerable<(float X, float Z, Color C)>> MiniDots;

    void MiniReady()
    {
        _mini = new Minimap(this);
        _mini.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        _ui.AddChild(_mini);
    }

    void MiniLay()
    {
        var lay = Layout;
        // the small map at most 260 wide and 230 high, the map's own shape
        float w = lay.FogW * HexMapLayout.HalfW, h = lay.FogH * HexMapLayout.RowStep, k = MathF.Min(260 / w, 230 / h);
        _mini.OffsetLeft = 16; _mini.OffsetRight = 16 + w * k + 12;
        _mini.OffsetTop = -16 - h * k - 12; _mini.OffsetBottom = -16;
        _miniStale = true;
    }

    bool OverMini(Vector2 p) => _mini != null && _mini.Visible && _mini.GetGlobalRect().HasPoint(p);

    /// <summary>Where the eye is on the small map, and where the heroes are, for the self-test: a point of the map (voxels) as the small map shows it.</summary>
    public Vector2 MiniPoint(float x, float z) => _mini.GetGlobalRect().Position + _mini.ToMini(x, z);

    sealed partial class Minimap : Control
    {
        readonly HexMapView _v;
        Image _img;
        ImageTexture _tex;
        byte[] _px;
        public Minimap(HexMapView v) { _v = v; MouseFilter = MouseFilterEnum.Stop; }

        static readonly Color Dark = new("15131a");

        float Scale => MathF.Min((Size.X - 12) / (_v.Layout.FogW * HexMapLayout.HalfW), (Size.Y - 12) / (_v.Layout.FogH * HexMapLayout.RowStep));
        public Vector2 ToMini(float x, float z) => new Vector2(6, 6) + new Vector2(x, z) * Scale;

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
            for (int j = 0; j < lay.FogH; j++)
                for (int i = 0; i < lay.FogW; i++)
                {
                    var (c, r) = lay.FogHex(i, j);
                    uint col = 0x15131a;
                    if (c >= 0 && _v.Source.Seen(c, r))
                    {
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
        }

        public override void _Draw()
        {
            if (_v.Layout == null) return;
            if (_v._miniStale || _tex == null) { Paint(); _v._miniStale = false; }
            DrawRect(new Rect2(Vector2.Zero, Size), Ornament.Fade(Ornament.Iron, .92f));
            DrawRect(new Rect2(Vector2.Zero, Size), Ornament.Fade(Ornament.Bronze, .6f), false, 1);
            var lay = _v.Layout;
            DrawTextureRect(_tex, new Rect2(ToMini(0, 0), new Vector2(lay.FogW * HexMapLayout.HalfW, lay.FogH * HexMapLayout.RowStep) * Scale), false);
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
            var v = (pos - new Vector2(6, 6)) / Scale;
            _v.Focus(v.X, v.Y);
            AcceptEvent();
        }
    }
}
