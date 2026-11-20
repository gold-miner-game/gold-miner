using System;
using System.Collections.Generic;
using UnityEngine;

// A map barrier stays broken across scene changes until map death or a new game/play session.
// Keep the original object locally so reviving can restore it without reloading.
public sealed class MapRockDamage : MonoBehaviour
{
    public const string SaveKey = "gm.map.brokenRocks.v1";
    [Serializable] class SaveData { public List<string> rocks = new List<string>(); }
    static HashSet<string> broken;
    string id;
    Rock rock;
    SpriteRenderer[] sprites;
    Collider2D[] colliders;
    bool[] spriteEnabled, colliderEnabled;
    bool originalActive;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { broken = null; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void BeginNewPlay() { ResetDamage(); }
    static HashSet<string> Broken
    {
        get
        {
            if (broken != null) return broken;
            SaveData data = null;
            try { data = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(SaveKey, "")); }
            catch (ArgumentException) { }
            broken = data != null && data.rocks != null ? new HashSet<string>(data.rocks) : new HashSet<string>();
            return broken;
        }
    }

    public static void Bind(MapHub hub, Transform bakedRoot = null)
    {
        if (!Application.isPlaying || hub == null) return;
        var mapScene = bakedRoot != null ? bakedRoot.gameObject.scene : hub.gameObject.scene;
        foreach (Rock rock in FindObjectsByType<Rock>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            // The shared Game scene owns MapHub; baked barriers belong to the
            // additive level-layout scene. Both are part of this map visit.
            if (rock.gameObject.scene != hub.gameObject.scene && rock.gameObject.scene != mapScene) continue;
            var state = rock.GetComponent<MapRockDamage>();
            if (state == null) state = rock.gameObject.AddComponent<MapRockDamage>();
            state.Initialize(rock.gameObject.scene.path);
        }
    }

    void Initialize(string map)
    {
        if (id != null) return;
        Vector3 at = transform.position;
        id = map + "|" + Mathf.RoundToInt(at.x * 10000f) + "," + Mathf.RoundToInt(at.y * 10000f) + "," + Mathf.RoundToInt(at.z * 10000f);
        rock = GetComponent<Rock>(); originalActive = gameObject.activeSelf;
        sprites = GetComponentsInChildren<SpriteRenderer>(true);
        colliders = GetComponentsInChildren<Collider2D>(true);
        spriteEnabled = new bool[sprites.Length]; colliderEnabled = new bool[colliders.Length];
        for (int i = 0; i < sprites.Length; i++) spriteEnabled[i] = sprites[i].enabled;
        for (int i = 0; i < colliders.Length; i++) colliderEnabled[i] = colliders[i].enabled;
        if (Broken.Contains(id)) gameObject.SetActive(false);
    }

    public void RememberSmash()
    {
        if (id == null || !Broken.Add(id)) return;
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(new SaveData { rocks = new List<string>(Broken) }));
        PlayerPrefs.Save();
    }

    public static void ResetDamage()
    {
        Broken.Clear(); PlayerPrefs.DeleteKey(SaveKey); PlayerPrefs.Save();
        foreach (MapRockDamage state in FindObjectsByType<MapRockDamage>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            state.Restore();
    }
    void Restore()
    {
        if (id == null || rock == null) return;
        rock.ResetMapSmash();
        for (int i = 0; i < sprites.Length; i++) if (sprites[i] != null) sprites[i].enabled = spriteEnabled[i];
        for (int i = 0; i < colliders.Length; i++) if (colliders[i] != null) colliders[i].enabled = colliderEnabled[i];
        gameObject.SetActive(originalActive);
    }
}
