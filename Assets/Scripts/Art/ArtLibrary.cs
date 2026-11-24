using System.Collections.Generic;
using UnityEngine;

// Art: painted sprites live in Resources/Art (papercraft style, see docs/art-style.md).
// Code that used to draw a placeholder shape asks here first and keeps its own drawing as the fallback.
public static class ArtLibrary
{
    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    // "Objects/Heart" -> Resources/Art/Objects/Heart; null when that art does not exist.
    public static Sprite Get(string name)
    {
        if (!cache.TryGetValue(name, out Sprite sprite))
        {
            sprite = Resources.Load<Sprite>("Art/" + name);
            cache[name] = sprite;
        }
        return sprite;
    }

    static readonly Dictionary<Sprite, Sprite[]> boil = new Dictionary<Sprite, Sprite[]>();

    // Registered boil frames of a painted object (itself, NAME_b1, NAME_b2; tools/art/boil_frames.py):
    // identical outline, only the surface light changes. Null when it has none.
    public static Sprite[] BoilFrames(Sprite sprite)
    {
        if (sprite == null) return null;
        if (boil.TryGetValue(sprite, out Sprite[] frames)) return frames;
        Sprite b1 = Get("Objects/" + sprite.name + "_b1"), b2 = Get("Objects/" + sprite.name + "_b2");
        frames = b1 != null && b2 != null ? new[] { sprite, b1, b2 } : null;
        boil[sprite] = frames;
        if (frames != null) { boil[b1] = frames; boil[b2] = frames; }
        return frames;
    }

    // A mask shape (Fx/TornFill, Fx/CutDisc) as a fresh 1x1-unit sprite with the given pivot, for a
    // SpriteMask that code scales. A new sprite each call, so the caller may Destroy it. Null: no art.
    public static Sprite MaskShape(string name, Vector2 pivot)
    {
        Sprite art = Get(name);
        if (art == null) return null;
        Texture2D t = art.texture;
        return Sprite.Create(t, new Rect(0, 0, t.width, t.height), pivot, t.width, 0, SpriteMeshType.FullRect);
    }

    static readonly Dictionary<(Material, string), Material> lineArt = new Dictionary<(Material, string), Material>();

    // Twisted twine texture along a rope line. Its colour (start/end colour, e.g. yellow or red hooks)
    // still comes from the line, so state colours keep their meaning. No art: the line stays plain.
    // inPlace: put the texture on the line's own material (one made just for that line) instead of a
    // cached copy, so a short-lived line leaves nothing behind.
    public static void Twine(LineRenderer line, float twistsPerUnit = 9f, bool inPlace = false)
    {
        if (!LineTexture(line, "Tiles/Twine", inPlace)) return;
        line.textureMode = LineTextureMode.Tile;
        line.textureScale = new Vector2(twistsPerUnit, 1f);
    }

    // Rolled tin wire along a short line (the hook's claw arms): lit along its top, dark underneath,
    // stretched once over the arm. Tinted by the line colour like the twine.
    public static void Wire(LineRenderer line, bool inPlace = false)
    {
        if (!LineTexture(line, "Tiles/Wire", inPlace)) return;
        line.textureMode = LineTextureMode.Stretch;
    }

    static bool LineTexture(LineRenderer line, string tileName, bool inPlace = false)
    {
        if (line == null || line.sharedMaterial == null) return false;
        Sprite tile = Get(tileName);
        if (tile == null) return false;
        Material source = line.sharedMaterial;
        if (inPlace) { source.mainTexture = tile.texture; return true; }
        if (!lineArt.TryGetValue((source, tileName), out Material material) || material == null)
        {
            material = new Material(source) { name = source.name + " (" + tile.name + ")", mainTexture = tile.texture };
            lineArt[(source, tileName)] = material;
        }
        line.sharedMaterial = material;
        return true;
    }

    public static readonly Color Cream = new Color(0.97f, 0.9f, 0.74f);

    // IMGUI boxes (guides, survival info): a stitched felt card instead of a flat colour. Keeps the
    // style's layout; widens padding so text clears the stitching. False: no art, style unchanged.
    public static bool FeltBox(GUIStyle style)
    {
        Sprite felt = Get("UI/FeltPanel");
        if (style == null || felt == null) return false;
        style.normal.background = felt.texture;
        style.border = new RectOffset(22, 22, 22, 22);
        style.padding = new RectOffset(style.padding.left + 8, style.padding.right + 8, style.padding.top + 6, style.padding.bottom + 6);
        style.normal.textColor = Cream;
        return true;
    }

    // IMGUI key caps and tags: a light paper card (text keeps its own dark ink). False: no art.
    public static bool PaperBox(GUIStyle style)
    {
        Sprite paper = Get("UI/PaperCard");
        if (style == null || paper == null) return false;
        style.normal.background = paper.texture;
        style.border = new RectOffset(16, 16, 16, 16);
        return true;
    }

    // uGUI key caps: the paper card, sliced so its rounded cut edge keeps its shape at any size.
    public static bool PaperCap(UnityEngine.UI.Image image, float cornerScale = 2f)
    {
        Sprite paper = Get("UI/PaperCard");
        if (image == null || paper == null) return false;
        image.sprite = paper;
        image.type = UnityEngine.UI.Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = cornerScale;
        return true;
    }

    // Shows the painted sprite in its own colours (keeping the renderer's alpha). False: no such art,
    // the caller keeps its placeholder. Only for things whose tint carries no gameplay meaning.
    public static bool ApplyPainted(SpriteRenderer renderer, string name)
    {
        if (renderer == null) return false;
        Sprite sprite = Get(name);
        if (sprite == null) return false;
        renderer.sprite = sprite;
        renderer.color = new Color(1f, 1f, 1f, renderer.color.a);
        return true;
    }
}
