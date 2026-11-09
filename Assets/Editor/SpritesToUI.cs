using UnityEditor;
using UnityEngine;

// Owner: Penny (Level map & tutorial)
// Tools > Gold Miner > Convert Sprites To UI (also right-click > Gold Miner in the Hierarchy): turns
// the selected sprite object (e.g. a character prefab dragged into a canvas, where it shows as a
// speck) into UI images of the same look, 200 pixels tall, in the same place in the canvas. Resize
// it afterwards with the Rect Tool like any UI. The original is removed (Undo brings it back).
public static class SpritesToUI
{
    const float Height = 200f;

    [MenuItem("Tools/Gold Miner/Convert Sprites To UI", false, 140)]
    [MenuItem("GameObject/Gold Miner/Convert Sprites To UI", false, 40)]
    static void Convert(MenuCommand command)
    {
        GameObject[] picked = command.context is GameObject one ? new[] { one } : Selection.gameObjects;
        if (picked.Length == 0)
        {
            EditorUtility.DisplayDialog("Pick a sprite object", "Select the sprite object (inside a canvas) in the Hierarchy, then try again.", "OK");
            return;
        }

        var made = new System.Collections.Generic.List<Object>();
        foreach (GameObject source in picked)
        {
            if (source == null) continue;
            if (source.GetComponentInChildren<SpriteRenderer>(true) == null)
            {
                Debug.LogWarning("Convert Sprites To UI: " + source.name + " has no sprites", source);
                continue;
            }
            Transform parent = source.transform.parent;
            if (parent == null || parent.GetComponentInParent<Canvas>() == null)
            {
                EditorUtility.DisplayDialog("Not in a canvas",
                    source.name + " isn't inside a canvas. Drag it into the canvas (e.g. under Background) in the Hierarchy first.", "OK");
                continue;
            }

            RectTransform copy = UISpriteCopy.Build(source, parent, Height);
            copy.SetSiblingIndex(source.transform.GetSiblingIndex());
            copy.localPosition = new Vector3(source.transform.localPosition.x, source.transform.localPosition.y, 0f);
            Undo.RegisterCreatedObjectUndo(copy.gameObject, "Convert Sprites To UI");
            Undo.DestroyObjectImmediate(source);
            made.Add(copy.gameObject);
        }
        if (made.Count > 0) Selection.objects = made.ToArray();
    }

    // The Hierarchy menu calls Convert once per selected object, each as its context
    [MenuItem("GameObject/Gold Miner/Convert Sprites To UI", true)]
    static bool CanConvert() => Selection.activeGameObject != null && !EditorApplication.isPlaying;
}
