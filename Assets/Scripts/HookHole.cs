using UnityEngine;

// Owner: Butian (Survival mode)
// The survival mode's hook "miner": a hole in the ground that shoots a red hook at the player, with
// no miner body. SurvivalLoader makes them around the player; LevelLoader makes one per hook origin
// for levels with "hook_holes": 1 (the dodge tutorial), so the tutorial teaches the real thing.
public static class HookHole
{
    public static readonly Color HoleColor = new Color(0.07f, 0.05f, 0.04f);
    public static readonly Color RimColor = new Color(0.42f, 0.3f, 0.18f);

    // Built inactive, so every Awake/OnEnable sees its references already set: the caller adds an
    // aimer or changes the AI's timing if it wants, then calls root.SetActive(true).
    // The hole is ai.transform; the hook and the telegraph are its siblings under root.
    public static SurvivalHookAI Build(string name, Vector2 at, Transform target, GameObject telegraphDot,
        Sprite holeSprite, Material spriteMaterial, float extendSpeed, float retractSpeed)
    {
        var root = new GameObject(name);
        root.SetActive(false);
        root.transform.position = at;

        var holeObject = new GameObject("Hole");
        holeObject.transform.SetParent(root.transform, false);
        SpriteRenderer holeRenderer = holeObject.AddComponent<SpriteRenderer>();
        holeRenderer.sprite = holeSprite;
        if (spriteMaterial != null) holeRenderer.sharedMaterial = spriteMaterial;
        holeRenderer.sortingOrder = -50;   // under treasure and the player

        // The hook's own sprite is hidden: it only lends the material and sorting to the rope
        var hookObject = new GameObject("Hook");
        hookObject.transform.SetParent(root.transform, false);
        SpriteRenderer hookRenderer = hookObject.AddComponent<SpriteRenderer>();
        if (spriteMaterial != null) hookRenderer.sharedMaterial = spriteMaterial;
        hookRenderer.sortingOrder = 5;
        Hook hook = hookObject.AddComponent<Hook>();
        hook.extendSpeed = extendSpeed;
        hook.retractSpeed = retractSpeed;
        hook.hitsPig = true;
        hook.grabsTreasure = false;
        hook.defaultColor = Color.red;

        var telegraphObject = new GameObject("Telegraph");
        telegraphObject.transform.SetParent(root.transform, false);
        TelegraphLine telegraph = telegraphObject.AddComponent<TelegraphLine>();
        telegraph.dotPrefab = telegraphDot;

        // Every shot is a red one at the player, none at treasure
        SurvivalHookAI ai = holeObject.AddComponent<SurvivalHookAI>();
        ai.hook = hook;
        ai.telegraph = telegraph;
        ai.hookOrigin = holeObject.transform;
        ai.target = target;
        return ai;
    }

    // A dark round hole with a lighter rim, one world unit across
    public static Sprite CreateSprite(Color hole, Color rim)
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var pixels = new Color[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                Color c = d < 0.72f ? hole : rim;
                c.a *= Mathf.Clamp01((1f - d) * r);   // soft edge
                pixels[y * size + x] = c;
            }
        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
