using System.Collections.Generic;
using UnityEngine;

// Owner: Penny (Level map & tutorial)
// The ordered list of nodes on the world map. One asset, edited in the Inspector, so adding a
// level is: add a node here + drop a level JSON. See docs/level-map-tutorial-design.md.
public enum NodeType
{
    Tutorial,   // tutorial replay portal; the first visit runs through WorldMap.tutorialLevels
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
    [Tooltip("Starting spot for this node's card (x = branch spread, y = depth, 0..1). Only used when " +
             "Build Map UI first creates the card; after that, move the card in the Map scene.")]
    public Vector2 mapPosition = new Vector2(0.5f, 0.5f);
    [Tooltip("Ids of the nodes that unlock this one (its parents in the tree). Empty = a start node. " +
             "Completing ANY one unlocks it, so a node can branch into two and two can merge back.")]
    public string[] prerequisites = new string[0];
    [Tooltip("Not open yet: stays locked whatever is cleared, and a hidden portal stays a rock in the wall")]
    public bool comingSoon;
    [Tooltip("Deprecated: unlock is ProgressStore completed prerequisites only. Kept for asset compatibility.")]
    public int unlockCost;
}

[CreateAssetMenu(fileName = "WorldMap", menuName = "Gold Miner/World Map")]
public class WorldMap : ScriptableObject
{
    [Tooltip("Tutorial levels played back to back, in order, the first time the player opens the map. " +
             "They are not map nodes; finishing the last one opens the map.")]
    public List<TextAsset> tutorialLevels = new List<TextAsset>();

    [Tooltip("Optional node entered after the tutorial, by its id. Empty opens the map and reveals the first unlocked level.")]
    public string afterTutorialNode = "";

    [Tooltip("The world map as a walkable level: a \"grid\" level file whose portals are the nodes below " +
             "(MapHub). Empty = the card map in the Map scene.")]
    public TextAsset hubLevel;

    public List<MapNode> nodes = new List<MapNode>();

    public MapNode Find(string id)
    {
        for (int i = 0; i < nodes.Count; i++)
            if (nodes[i] != null && nodes[i].id == id) return nodes[i];
        return null;
    }

    public int IndexOf(MapNode node) => nodes.IndexOf(node);

    // A node is reachable if it is already cleared (so it can be replayed / the store revisited), or
    // it has no parents or ANY of its parents is cleared. "Any" is what
    // lets the tree branch (one parent -> two children) and merge (two parents -> one child).
    public bool IsUnlocked(int index)
    {
        if (index < 0 || index >= nodes.Count) return false;
        MapNode node = nodes[index];
        if (node == null || node.comingSoon) return false;
        // Old saves may have visited the Store when it started open. Its prerequisite still applies.
        if (node.type == NodeType.Store) return PrerequisiteCleared(node);
        if (ProgressStore.IsCompleted(node.id)) return true;
        return PrerequisiteCleared(node);
    }

    public bool IsUnlocked(MapNode node) => IsUnlocked(IndexOf(node));

    public bool PrerequisiteCleared(MapNode node)
    {
        if (node.prerequisites == null || node.prerequisites.Length == 0) return true;
        for (int i = 0; i < node.prerequisites.Length; i++)
            if (node.prerequisites[i] == ProgressStore.TutorialDoneId
                ? ProgressStore.TutorialDone : ProgressStore.IsCompleted(node.prerequisites[i])) return true;
        return false;
    }

}
