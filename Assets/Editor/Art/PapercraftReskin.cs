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
        AssetDatabase.SaveAssets();
        Debug.Log("PAPERCRAFT_RESKIN_PASS");
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
