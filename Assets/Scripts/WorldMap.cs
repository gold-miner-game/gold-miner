using System.Collections.Generic;
using UnityEngine;

// Owner: Penny (Level map & tutorial)
// The ordered list of nodes on the world map. One asset, edited in the Inspector, so adding a
// level is: add a node here + drop a level JSON. See docs/level-map-tutorial-design.md.
public enum NodeType
{
    Tutorial,   // a Game-scene level with stages/guide; completed on win
    Battle,     // a normal Game-scene level; completed on win
    RolePick,   // the character picker; completed on leave
    Store,      // the shop (Zhan Li); completed on leave, re-enterable
    Survival,   // reserved for Butian's survival mode
}

// One icon on the map.
[System.Serializable]
public class MapNode
{
    [Tooltip("Stable id, e.g. \"tut_basics\" or \"battle_03\". Used by the save file; don't rename.")]
    public string id;
    public NodeType type = NodeType.Battle;
    [Tooltip("Level JSON for Tutorial / Battle nodes (a TextAsset from Resources/Levels).")]
    public TextAsset levelAsset;
    public string displayName = "Level";
    [Tooltip("Icon position in normalised screen space: (0,0) top-left, (1,1) bottom-right.")]
    public Vector2 mapPosition = new Vector2(0.5f, 0.5f);
}

[CreateAssetMenu(fileName = "WorldMap", menuName = "Gold Miner/World Map")]
public class WorldMap : ScriptableObject
{
    public List<MapNode> nodes = new List<MapNode>();

    public MapNode Find(string id)
    {
        for (int i = 0; i < nodes.Count; i++)
            if (nodes[i] != null && nodes[i].id == id) return nodes[i];
        return null;
    }

    public int IndexOf(MapNode node) => nodes.IndexOf(node);

    // A node is reachable if it's the first one, already cleared, or the one right after a cleared
    // node. Cleared nodes stay clickable so the player can replay or revisit the store.
    public bool IsUnlocked(int index)
    {
        if (index <= 0) return true;
        if (index >= nodes.Count) return false;
        if (ProgressStore.IsCompleted(nodes[index].id)) return true;
        return ProgressStore.IsCompleted(nodes[index - 1].id);
    }
}
