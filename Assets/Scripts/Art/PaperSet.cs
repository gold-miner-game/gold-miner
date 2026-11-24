using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Art: set dressing. In scenes with a playable character, a few muted papercraft props (planks, rope,
// a lantern, moss, mushrooms, a slab, rails, a crate, pebbles; Resources/Art/Props) are laid on the
// floor in the outer band of the view, dimmed so they recede into it like part of the diorama base.
// Never in the way: a spot is used only if no collider is near it (a read-only physics query), the
// props have no colliders, and they draw just above the floor, below every game piece. The layout is
// the same every time a given scene opens (seeded by its name).
public class PaperSet : MonoBehaviour
{
    static readonly string[] Props = { "Plank", "Rope", "Lantern", "Moss", "Mushrooms", "Rail", "Crate", "Pebbles" };
    static readonly Color Dim = new Color(0.6f, 0.56f, 0.52f, 0.9f);
    const int Count = 9;
    const float Band = 0.16f;   // share of the view, from each edge, that props may use

    static PaperSet instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper Set");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PaperSet>();
        SceneManager.sceneLoaded += (s, m) => { if (instance != null && m == LoadSceneMode.Single) instance.StartCoroutine(instance.Dress(s)); };
        instance.StartCoroutine(instance.Dress(SceneManager.GetActiveScene()));
    }

    static int StableHash(string s)
    {
        unchecked { int h = 17; foreach (char c in s) h = h * 31 + c; return h; }
    }

    IEnumerator Dress(Scene scene)
    {
        yield return new WaitForSeconds(0.4f);   // levels spawn their pieces after loading
        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic || FindAnyObjectByType<PlayerCharacter>() == null) yield break;
        if (!scene.IsValid() || !scene.isLoaded) yield break;

        var root = new GameObject("Set Dressing");
        SceneManager.MoveGameObjectToScene(root, scene);
        var rng = new System.Random(StableHash(scene.name));
        float h = cam.orthographicSize, w = h * cam.aspect;
        Vector2 c = cam.transform.position;
        int placed = 0;
        for (int tries = 0; tries < 80 && placed < Count; tries++)
        {
            // a point in the outer band: pick an edge, then a spot along it
            float u = (float)rng.NextDouble(), v = (float)rng.NextDouble() * Band;
            Vector2 p;
            switch (rng.Next(4))
            {
                case 0: p = new Vector2(Mathf.Lerp(-w, w, u), h - v * 2f * h); break;
                case 1: p = new Vector2(Mathf.Lerp(-w, w, u), -h + v * 2f * h); break;
                case 2: p = new Vector2(-w + v * 2f * w, Mathf.Lerp(-h, h, u)); break;
                default: p = new Vector2(w - v * 2f * w, Mathf.Lerp(-h, h, u)); break;
            }
            p += c;
            float size = Mathf.Lerp(0.7f, 1.2f, (float)rng.NextDouble());
            if (Physics2D.OverlapCircle(p, size * 0.75f) != null) continue;   // something of the game is near
            Sprite art = ArtLibrary.Get("Props/" + Props[rng.Next(Props.Length)]);
            if (art == null) yield break;
            var prop = new GameObject(art.name);
            prop.transform.SetParent(root.transform, false);
            prop.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, 0f, (float)rng.NextDouble() * 360f));
            prop.transform.localScale = Vector3.one * size;
            var look = prop.AddComponent<SpriteRenderer>();
            look.sprite = art;
            look.color = Dim;
            look.sortingOrder = short.MinValue + 2;   // above the floor and the strata (PaperStrata), under every game piece
            if (art.name == "Lantern") PaperLamps.Lantern(prop.transform);   // it casts a warm pool on the floor
            placed++;
        }
    }
}
