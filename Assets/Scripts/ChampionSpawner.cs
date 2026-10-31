using UnityEngine;

// Put this in the Game scene (it's on the LevelLoader object), with the character prefabs in the
// same order as the select buttons. LevelLoader calls Spawn before it looks for the player: the
// character placed in the scene (Mr. Hyde) is swapped for the picked one, at the same spot.
// In the tutorial LevelLoader always asks for Mr. Hyde.
public class ChampionSpawner : MonoBehaviour
{
    public const int MrHyde = 0;

    [Header("Same order as the select buttons: 0 Mr. Hyde, 1 JK. Rolling, 2 Scrat, 3 Rero Rero")]
    public GameObject[] champions = new GameObject[4];

    // Set by ChampionSelect. Static, so it survives scene loads (and level reloads). -1 = not picked yet
    public static int Chosen = -1;

    // Each champion's cross-level record, in the same order. Mr. Hyde keeps Zhan Li's PigStatus
    static readonly System.Type[] recordTypes =
        { typeof(PigStatus), typeof(CatStatus), typeof(SquirrelStatus), typeof(FrogStatus) };

    public static System.Type RecordType(int index)
    {
        return index >= 0 && index < recordTypes.Length ? recordTypes[index] : typeof(PigStatus);
    }

    // Reset statics when entering Play Mode (in case domain reload is turned off)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Chosen = -1;
    }

    // Returns the character to play with: the scene's own if it's already the right kind,
    // else a new one from the prefab. Null: nothing picked (keep the scene's character)
    public PlayerCharacter Spawn(int index)
    {
        if (index < 0) return null;
        GameObject prefab = index < champions.Length ? champions[index] : null;
        PlayerCharacter wanted = prefab != null ? prefab.GetComponent<PlayerCharacter>() : null;
        if (wanted == null)
        {
            Debug.LogWarning($"ChampionSpawner: no character prefab in slot {index}, keeping the scene's one");
            return null;
        }

        PlayerCharacter placed = FindFirstObjectByType<PlayerCharacter>();
        if (placed != null && placed.GetType() == wanted.GetType()) return placed;

        Vector3 at = placed != null ? placed.transform.position : transform.position;
        if (placed != null)
        {
            placed.gameObject.SetActive(false);   // gone at once, so nothing else finds it
            Destroy(placed.gameObject);
        }

        GameObject spawned = Instantiate(prefab, at, Quaternion.identity);
        spawned.name = prefab.name;
        return spawned.GetComponent<PlayerCharacter>();
    }
}
