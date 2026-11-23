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

    static readonly Dictionary<Material, Material> twine = new Dictionary<Material, Material>();

    // Twisted twine texture along a rope line. Its colour (start/end colour, e.g. yellow or red hooks)
    // still comes from the line, so state colours keep their meaning. No art: the line stays plain.
    public static void Twine(LineRenderer line, float twistsPerUnit = 9f)
    {
        if (line == null || line.sharedMaterial == null) return;
        Sprite tile = Get("Tiles/Twine");
        if (tile == null) return;
        Material source = line.sharedMaterial;
        if (!twine.TryGetValue(source, out Material material) || material == null)
        {
            material = new Material(source) { name = source.name + " (Twine)", mainTexture = tile.texture };
            twine[source] = material;
        }
        line.sharedMaterial = material;
        line.textureMode = LineTextureMode.Tile;
        line.textureScale = new Vector2(twistsPerUnit, 1f);
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
