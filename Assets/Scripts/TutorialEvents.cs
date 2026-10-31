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
        "exit_open", "exit_reached",
    };

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
