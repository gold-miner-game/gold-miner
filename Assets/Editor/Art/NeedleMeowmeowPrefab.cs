using UnityEditor;
using UnityEngine;

// Art: builds Assets/Prefabs/Enemy/NeedleMeowmeow.prefab, the look of the sprint-stab cat
// (NeedleMeowmeowArt + Body / Thread / Telegraph children, pose sprites assigned). Look only: no
// collider, body or gameplay script; whoever writes its gameplay adds those to the root and drives
// the art with SetPose / Aim. Rebuilding overwrites the prefab, so add gameplay parts in a variant
// or after the last rebuild.
//   Tools > Gold Miner > Art > Build Needle Meowmeow Prefab
//   or batch: -executeMethod NeedleMeowmeowPrefab.Build
public static class NeedleMeowmeowPrefab
{
    const string PrefabPath = "Assets/Prefabs/Enemy/NeedleMeowmeow.prefab";
    const string Art = "Assets/Resources/Art/Enemies/Needle_";

    [MenuItem("Tools/Gold Miner/Art/Build Needle Meowmeow Prefab")]
    public static void Build()
    {
        var root = new GameObject("NeedleMeowmeow");
        try
        {
            var art = root.AddComponent<NeedleMeowmeowArt>();
            art.EnsureParts();
            art.prowl = Load("Prowl");
            art.windup = Load("Windup");
            art.dash = Load("Dash");
            art.stab = Load("Stab");
            art.stuck = Load("Stuck");
            art.hit = Load("Hit");

            var body = root.transform.Find("Body").GetComponent<SpriteRenderer>();
            body.sprite = art.prowl;
            var thread = root.transform.Find("Thread").GetComponent<LineRenderer>();
            thread.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            thread.startColor = thread.endColor = art.threadColor;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
            Debug.Log(saved ? "NeedleMeowmeowPrefab: saved " + PrefabPath : "NeedleMeowmeowPrefab: FAILED to save " + PrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static Sprite Load(string pose)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + pose + ".png");
        if (sprite == null) Debug.LogWarning("NeedleMeowmeowPrefab: missing " + Art + pose + ".png");
        return sprite;
    }
}
