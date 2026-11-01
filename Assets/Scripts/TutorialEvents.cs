using System;
using UnityEngine;

// Owner: Penny (Level map & tutorial)
// Named events that TutorialSteps can start or end on (Trigger = Event). Any script can fire one,
// e.g. TutorialEvents.Fire("hook_fired"), so new triggers need no change to the tutorial code.
// Every TutorialStep also fires "<step name>.start" and "<step name>.end" itself.
public static class TutorialEvents
{
    public static event Action<string> Fired;

    // Print every event to the Console (Tools > Gold Miner > Log Tutorial Events)
    public static bool Log { get; set; }

    // The events the game fires, for the Inspector's event dropdown. Add new ones here
    public static readonly string[] Known =
    {
        "hook_aim", "hook_fire", "hook_dodged", "hook_blocked", "treasure_stolen", "player_hit", "life_lost",
        "stage_block", "stage_steal", "stage_destroy", "hook_damaged", "hook_destroyed",
        "round_won", "round_lost", "round_end",
        "squeeze_start", "squeeze_hook", "squeeze_rescue", "squeeze_closed", "squeeze_crushed",
        "exit_open", "exit_reached", "story_line",
    };

    // The world map's events (MapHub), one set per WorldMap node, e.g. "map_cleared_battle_01":
    //   map_open             every time the map opens
    //   map_back_from_<id>   the map opens after leaving that node (won or not)
    //   map_cleared_<id>     the map opens after leaving that node, and it is cleared
    //   portal_revealed_<id> a hidden portal has just turned from rock into portal
    // With a step's Only First Time, "map_cleared_battle_01" is a story for the first return after
    // clearing Level 1
    public const string MapOpen = "map_open";
    public static string MapBackFrom(string nodeId) => "map_back_from_" + nodeId;
    public static string MapCleared(string nodeId) => "map_cleared_" + nodeId;
    public static string PortalRevealed(string nodeId) => "portal_revealed_" + nodeId;

    public static System.Collections.Generic.List<string> MapEvents(WorldMap map)
    {
        var list = new System.Collections.Generic.List<string> { MapOpen };
        if (map == null) return list;
        foreach (MapNode node in map.nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.id)) continue;
            list.Add(MapBackFrom(node.id));
            list.Add(MapCleared(node.id));
            list.Add(PortalRevealed(node.id));
        }
        return list;
    }

    public static void Fire(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (Log) Debug.Log("Tutorial event: " + name);
        Fired?.Invoke(name);
    }
}

// On a string field: the Inspector shows it with a dropdown of the known events and the steps'
// own ".start" / ".end", and warns about a name nothing fires (a typo never triggers)
public class TutorialEventAttribute : PropertyAttribute
{
    public readonly bool customAllowed;   // the field may name a new event (a dialogue line fires it)
    public TutorialEventAttribute(bool customAllowed = false) { this.customAllowed = customAllowed; }
}
