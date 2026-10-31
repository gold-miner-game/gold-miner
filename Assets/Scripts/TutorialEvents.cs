using System;

// Owner: Penny (Level map & tutorial)
// Named events that TutorialSteps can start or end on (Trigger = Event). Any script can fire one,
// e.g. TutorialEvents.Fire("hook_fired"), so new triggers need no change to the tutorial code.
// Every TutorialStep also fires "<step name>.start" and "<step name>.end" itself.
public static class TutorialEvents
{
    public static event Action<string> Fired;

    public static void Fire(string name)
    {
        if (!string.IsNullOrEmpty(name)) Fired?.Invoke(name);
    }
}
