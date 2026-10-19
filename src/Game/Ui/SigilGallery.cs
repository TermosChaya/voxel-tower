using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Every sigil of the troop's page and the page of intervention, with the branches' and the pages' emblems, laid out on
/// iron to be looked over: each one large with its key under it, and beside it at the size the tree shows it, in a bronze
/// medallion, in the four inks a sigil is drawn in — lit, glowing, learnt, out of reach. The lore's own sigils follow on
/// the next page, to set the new ones against. Shown by the "sigils" pose; --look=N turns to page N.
/// </summary>
public partial class SigilGallery : Control
{
    /// <summary>Which page of the gallery is shown.</summary>
    public int Page;

    public static readonly string[] TroopKeys =
    {
        "spearmen", "shieldmen", "halberds", "shieldwall", "longshafts", "knights", "slingers", "crossbows", "leadshot", "bodkins", "firepots", "bombards",
        "hounds", "riders", "hunt", "horsearchers", "charge", "lancers", "barracks", "drillyard", "bunks", "trolls", "giants", "boulders",
    };

    public static readonly string[] RiteKeys =
    {
        "spell.Hand", "spell.Rift", "spell.Mire", "spell.Skin", "spell.Fall", "spell.Quake", "spell.FireRain", "spell.Thunder", "spell.Weather", "spell.BlackWind",
        "spell.BloodMoon", "spell.Meteor", "spell.Dread", "spell.DeadFog", "spell.Chains", "spell.Plague", "spell.Raise", "reap", "spell.Telekinesis", "spell.Recall",
        "spell.Eye", "spell.Explosion", "spell.Oath", "spell.Whirlwind", "rosary", "vessel", "altar", "litany", "vigil", "might",
    };

    public static readonly string[] BranchNames =
    {
        "Пехота", "Стрелки", "Конница", "Великаны", "Круг земли", "Круг огня и неба", "Круг смерти", "Круг воли", "Благодать",
    };

    const float Margin = 16, Big = 64, Small = 28, Rim = 18, CellW = 156, CellH = 88, HeadH = 26;

    static readonly Color[] Inks = { Ornament.BronzeLight, Ornament.EmberGlow, Ornament.Aged, Ornament.Fade(Ornament.Faded, .6f) };

    /// <summary>What is laid out, in order: a section's heading (no painter) or a sigil with its label.</summary>
    static List<(string label, Icons.Painter icon)> Laid()
    {
        var all = new List<(string, Icons.Painter)> { ("Войско", null) };
        foreach (string k in TroopKeys) all.Add((k, Icons.SigilFor(k)));
        all.Add(("Вмешательство", null));
        foreach (string k in RiteKeys) all.Add((k, Icons.SigilFor(k)));
        all.Add(("Ветви и страницы", null));
        foreach (string b in BranchNames) all.Add((b, Icons.BranchMark(b)));
        for (int p = 0; p < 3; p++) all.Add(($"страница {p}", Icons.PageMark(p)));
        all.Add(("Умения — для сравнения", null));
        foreach (var l in Sim.Lores) all.Add((l.Key, Icons.SigilFor(l.Key)));
        return all;
    }

    public override void _Draw()
    {
        var whole = new Rect2(Vector2.Zero, Size);
        Ornament.Gradient(this, whole, Ornament.IronMid, Ornament.IronDark);
        DrawTextureRect(Ornament.Grain, whole, true, new Color(1, 1, 1, .05f));
        float x = Margin, y = Margin;
        int page = 0;
        bool fresh = true;
        foreach (var (label, icon) in Laid())
        {
            if (icon == null)
            {
                if (!fresh) { x = Margin; y += CellH; }
                if (y + HeadH + CellH > Size.Y - Margin) { page++; y = Margin; }
                if (page == Page) Ornament.Lettering(this, Ornament.Caps, new Vector2(Margin, y + 17), label.ToUpperInvariant(), 13, Ornament.BronzeLight);
                y += HeadH;
                fresh = true;
                continue;
            }
            if (x + CellW > Size.X - Margin + 6) { x = Margin; y += CellH; }
            if (y + CellH > Size.Y - Margin + 8) { page++; y = Margin; x = Margin; }
            if (page == Page) Cell(new Vector2(x, y), label, icon);
            x += CellW;
            fresh = false;
        }
        // after the sheets, one more: every sigil at the sizes round the tree's, set off the pixel grid, to see that none of them falls apart
        if (Page > page)
        {
            Ornament.Lettering(this, Ornament.Caps, new Vector2(Margin, Margin + 17), "ВСЕ РАЗМЕРЫ: 20–34 ПК", 13, Ornament.BronzeLight);
            float sx = Margin, sy = Margin + HeadH;
            foreach (var (label, icon) in Laid())
            {
                if (icon == null) continue;
                if (sx + 224 > Size.X - Margin) { sx = Margin; sy += 50; }
                DrawString(Ornament.Serif, new Vector2(sx, sy + 9), label, HorizontalAlignment.Left, 220, 9, Ornament.Faded);
                float ix = sx;
                foreach (int s in new[] { 20, 22, 24, 26, 28, 30, 34 })
                {
                    icon(this, new Rect2(new Vector2(ix + .37f, sy + 46 - s + .61f), new Vector2(s, s)), Ornament.BronzeLight);
                    ix += s + 3;
                }
                sx += 224;
            }
        }
        Ornament.Lettering(this, Ornament.Serif, new Vector2(Size.X - Margin - 200, Size.Y - 6), $"страница {Page + 1} из {page + 2}", 12, Ornament.Aged,
            HorizontalAlignment.Right, 200);
    }

    void Cell(Vector2 at, string label, Icons.Painter icon)
    {
        icon(this, new Rect2(at, new Vector2(Big, Big)), Ornament.BronzeLight);
        DrawString(Ornament.Serif, at + new Vector2(0, Big + 13), label, HorizontalAlignment.Left, CellW - 4, 11, Ornament.Aged);
        for (int k = 0; k < Inks.Length; k++)
            Medal(at + new Vector2(Big + 6 + Rim + (k % 2) * 40, Rim + 2 + (k / 2) * 40), icon, Inks[k]);
    }

    /// <summary>A round bronze-rimmed medallion with the sigil in it at the tree's size.</summary>
    void Medal(Vector2 c, Icons.Painter icon, Color ink)
    {
        DrawCircle(c + new Vector2(1, 2), Rim + 1, new Color(0, 0, 0, .5f), true, -1, true);
        DrawCircle(c, Rim, Ornament.IronDark, true, -1, true);
        DrawCircle(c, Rim - 2, Ornament.IronMid, true, -1, true);
        DrawArc(c, Rim - 1, 0, MathF.Tau, 40, Ornament.Bronze, 1.6f, true);
        DrawArc(c, Rim - 3.5f, 0, MathF.Tau, 40, Ornament.BronzeDeep, 1f, true);
        icon(this, new Rect2(c - new Vector2(Small / 2, Small / 2), new Vector2(Small, Small)), ink);
    }
}
