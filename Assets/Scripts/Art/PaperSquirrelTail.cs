using UnityEngine;
using UnityEngine.SceneManagement;

// Art: Scrat's tail, finished. The prefab draws it as two plain felt circles stretched into tall ovals
// (Tail Crown: wide, behind; Tail Base: narrow, in front) tinted brown, which read as a blank oval.
// They become a painted two-tone felt tail: a bushy chestnut plume with tufted edges and a curl
// (Squirrel/TailCrown) and a lighter inner stripe (Squirrel/TailBase), Meshy-painted from the in-game
// squirrel (tools/art/make_squirrel_tail.py). Each painting is squeezed into the circle's square
// canvas by its oval's stretch, so the prefab's own scales draw it at its true shape and the same
// outline: SquirrelTailHeading's turning and resting poses, and every gameplay part, are untouched
// (the tail parts carry no colliders). Swapped wherever a squirrel appears: in play and in the camp.
public class PaperSquirrelTail : MonoBehaviour
{
    static PaperSquirrelTail instance;
    Sprite crown, stripe;
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper Squirrel Tail");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PaperSquirrelTail>();
        SceneManager.sceneLoaded += (s, m) => { if (instance != null) instance.nextScan = 0f; };
    }

    void Start()
    {
        crown = ArtLibrary.Get("Squirrel/TailCrown");
        stripe = ArtLibrary.Get("Squirrel/TailBase");
        if (crown == null || stripe == null) enabled = false;   // no art: the felt ovals stay
    }

    void Update()
    {
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 0.5f;
        foreach (SquirrelTailHeading tail in FindObjectsByType<SquirrelTailHeading>(FindObjectsSortMode.None))
        {
            SpriteRenderer c = Part(tail.transform, "Tail Crown"), b = Part(tail.transform, "Tail Base");
            if (c == null || b == null || c.sprite == crown) continue;
            c.sprite = crown;
            b.sprite = stripe;
            c.color = b.color = Color.white;                    // painted felt, not a tinted circle
            b.sortingOrder = c.sortingOrder + 1;                // the light stripe lies on the plume
        }
    }

    static SpriteRenderer Part(Transform root, string partName)
    {
        foreach (SpriteRenderer sr in root.GetComponentsInChildren<SpriteRenderer>(true))
            if (sr.name == partName) return sr;
        return null;
    }
}
