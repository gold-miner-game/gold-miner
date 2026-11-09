using UnityEngine;

// Owner: Penny (Level map & tutorial)
// One-time gold on the world map (the evil cat's eyes). Baked onto a gold piece under Level > Items
// by HubSceneBaker from the map file's "gold". Eating it pays its value straight into the wallet the
// Store spends (like the old maze's gold) and it never comes back; one the player already ate is
// gone when the map loads.
[RequireComponent(typeof(Treasure))]
public class MapGold : MonoBehaviour
{
    [Tooltip("Remembers that this piece was eaten (ProgressStore). Unique on the map")]
    public string goldId = "map_gold";

    Treasure treasure;

    void Awake()
    {
        treasure = GetComponent<Treasure>();
        if (ProgressStore.MapGoldTaken(goldId)) gameObject.SetActive(false);
    }

    void OnEnable() { Treasure.Eaten += OnEaten; }
    void OnDisable() { Treasure.Eaten -= OnEaten; }

    void OnEaten(Treasure eaten)
    {
        if (eaten != treasure) return;
        ProgressStore.MarkMapGoldTaken(goldId);
        CrossLevelStatus.Ensure().addWallet(treasure.value);
    }
}
