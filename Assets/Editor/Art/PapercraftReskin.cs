using System;
using UnityEditor;
using UnityEngine;

// Art: puts the papercraft sprites (Assets/Resources/Art) on the object prefabs. Only SpriteRenderer
// sprite/colour change: transforms, colliders and gameplay components stay as they are. Things whose
// tint means something to gameplay (rocks: green = rhino-proof, map wall colours) keep their tint and
// get greyscale art. Safe to run again. Batch: -executeMethod PapercraftReskin.ApplyBatch
public static class PapercraftReskin
{
    struct Swap
    {
        public string prefab, art;
        public bool keepTint;
        public Swap(string prefab, string art, bool keepTint) { this.prefab = prefab; this.art = art; this.keepTint = keepTint; }
    }

    static readonly Swap[] Objects =
    {
        new Swap("Assets/Prefabs/Object/Gold_Small.prefab",  "Objects/GoldSmall",  false),
        new Swap("Assets/Prefabs/Object/Gold_Medium.prefab", "Objects/GoldMedium", false),
        new Swap("Assets/Prefabs/Object/Gold_Large.prefab",  "Objects/GoldLarge",  false),
        new Swap("Assets/Prefabs/Reference/Diamond.prefab",  "Objects/Diamond",    false),
        new Swap("Assets/Prefabs/Life.prefab",               "Objects/Heart",      false),
    };

    [MenuItem("Tools/Gold Miner/Art/Apply Papercraft Reskin")]
    public static void Apply()
    {
        AssetDatabase.ImportAsset("Assets/Resources/Art", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        foreach (Swap swap in Objects) SwapRoot(swap);
        RemapPackagePrimitives();
        AssetDatabase.SaveAssets();
        Debug.Log("PAPERCRAFT_RESKIN_PASS");
    }

    // Some parts still used the 2D package's default shapes (Circle, Triangle, Capsule, Square), which
    // can't be repainted. They point at the project's felt/paper copies instead: same size, pivot and
    // pixels per unit, same outlines. Pure-black enemy felt becomes charcoal so grain, stitches and the
    // red features read on the dark mine floor.
    static readonly string[] PrimitiveUsers =
    {
        "Assets/Prefabs/Enemy/EvilMeowmeow.prefab", "Assets/Prefabs/Enemy/GreedyMeowmeow.prefab",
        "Assets/Prefabs/Object/Exit.prefab", "Assets/Prefabs/Reference/CatTired.prefab",
        "Assets/Prefabs/Teams/Cat.prefab", "Assets/Prefabs/Teams/Frog.prefab", "Assets/Prefabs/Teams/Squirrel.prefab",
    };
    static readonly Color Charcoal = new Color(0.17f, 0.14f, 0.15f, 1f);

    static void RemapPackagePrimitives()
    {
        foreach (string path in PrimitiveUsers)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool enemy = path.Contains("/Enemy/");
                int changed = 0;
                foreach (SpriteRenderer r in root.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    Sprite paper = PaperCopy(r.sprite);
                    if (paper != null) { r.sprite = paper; changed++; }
                    if (enemy && r.color.r < 0.05f && r.color.g < 0.05f && r.color.b < 0.05f)
                    {
                        r.color = new Color(Charcoal.r, Charcoal.g, Charcoal.b, r.color.a);
                        changed++;
                    }
                }
                if (changed > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("Paper primitives " + path + ": " + changed);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // Scenes (Select.unity) are remapped by tools/art/remap_primitive_guids.py: saving a scene
        // through Unity would reorder the whole file.
    }

    static Sprite PaperCopy(Sprite sprite)
    {
        if (sprite == null) return null;
        string from = AssetDatabase.GetAssetPath(sprite);
        if (!from.StartsWith("Packages/com.unity.2d.sprite/")) return null;
        Sprite paper = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/" + sprite.name + ".png");
        return paper;
    }

    public static void ApplyBatch()
    {
        try { Apply(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static Sprite Art(string name)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Art/" + name + ".png");
        if (sprite == null) throw new InvalidOperationException("Missing art sprite " + name);
        return sprite;
    }

    static void SwapRoot(Swap swap)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(swap.prefab);
        try
        {
            SpriteRenderer look = root.GetComponent<SpriteRenderer>();
            if (look == null) throw new InvalidOperationException(swap.prefab + " has no root SpriteRenderer");
            look.sprite = Art(swap.art);
            if (!swap.keepTint) look.color = Color.white;
            PrefabUtility.SaveAsPrefabAsset(root, swap.prefab);
            Debug.Log("Reskinned " + swap.prefab + " -> " + swap.art);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
