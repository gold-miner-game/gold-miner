using System.Collections.Generic;

// Owner: Claude (sound director)
// Every sound effect the game can play, and how it is mixed. A cue's clips live in
// Resources/Audio/Sfx/<cue>/ (one file per take); SoundDirector picks a take at random (never the
// same one twice in a row) and adds the cue's pitch jitter, so repeated sounds don't machine-gun.
//
//   volume     linear, before the bus volume
//   pitch      +/- random pitch range (0.06 = +/-6 %)
//   gap        seconds: the same cue never restarts faster than this (busy hooks, coin showers)
//   voices     how many copies of the cue may sound at once
//   bus        Sfx / Ui / Music (the volume sliders)
//   duck       how far the music dips while this cue plays (0 = no dip, 0.6 = to 40 %)
public static class SoundCues
{
    public enum Bus { Sfx, Ui, Music }

    public sealed class Cue
    {
        public string id;
        public float volume = 1f, pitch = 0.05f, gap = 0.05f, duck;
        public int voices = 3;
        public Bus bus = Bus.Sfx;
    }

    static Cue C(string id, float volume, float pitch = 0.05f, float gap = 0.05f, int voices = 3,
                 Bus bus = Bus.Sfx, float duck = 0f)
        => new Cue { id = id, volume = volume, pitch = pitch, gap = gap, voices = voices, bus = bus, duck = duck };

    public static readonly Dictionary<string, Cue> All = Build(
        // --- the miners' hooks: the most frequent sounds in a level, so kept small and varied
        C("hook_fire", 0.35f, 0.08f, 0.06f, 4),
        C("hook_aim_warn", 0.45f, 0.04f, 0.2f, 2),
        C("hook_grab", 0.45f, 0.07f, 0.05f, 3),
        C("hook_rock", 0.5f, 0.08f, 0.05f, 3),
        C("rock_break", 0.65f, 0.06f, 0.05f, 3),
        C("hook_blocked", 0.75f, 0.05f, 0.08f, 2),
        C("rope_cut", 0.7f, 0.06f, 0.05f, 3),
        C("gold_dropped", 0.6f, 0.06f, 0.08f, 2),
        C("miner_bank", 0.4f, 0.04f, 0.15f, 2),
        C("ice_break", 0.55f, 0.08f, 0.08f, 2),

        // --- the player eating and picking things up
        C("eat_munch", 0.6f, 0.08f, 0.05f, 2),
        C("coin_small", 0.5f, 0.05f, 0.04f, 3),
        C("coin_large", 0.6f, 0.03f, 0.05f, 2),
        C("diamond", 0.6f, 0.02f, 0.05f, 2),
        C("digest_full", 0.55f, 0.06f, 0.6f, 1),
        C("life_pickup", 0.65f, 0.02f, 0.1f, 2),

        // --- getting hurt: an impact plus the character's own voice
        C("hit_impact", 0.8f, 0.05f, 0.1f, 2),
        C("hurt_pig", 0.7f, 0.05f, 0.2f, 1),
        C("hurt_cat", 0.7f, 0.05f, 0.2f, 1),
        C("hurt_squirrel", 0.7f, 0.05f, 0.2f, 1),
        C("hurt_frog", 0.7f, 0.05f, 0.2f, 1),
        C("hurt_penguin", 0.7f, 0.05f, 0.2f, 1),
        C("hurt_rhino", 0.7f, 0.05f, 0.2f, 1),
        C("shield_soak", 0.75f, 0.05f, 0.1f, 2),

        // --- each character's K skill
        C("skill_block", 0.6f, 0.04f, 0.1f, 1),
        C("skill_roll", 0.6f, 0.05f, 0.1f, 1),
        C("skill_tail", 0.55f, 0.05f, 0.1f, 1),
        C("skill_tongue", 0.6f, 0.06f, 0.1f, 1),
        C("skill_breath", 0.6f, 0.03f, 0.2f, 1),
        C("skill_charge", 0.7f, 0.03f, 0.2f, 1),
        C("skill_ready", 0.45f, 0.02f, 0.5f, 1),   // K is usable again

        // --- enemies taking damage
        C("enemy_hit", 0.6f, 0.07f, 0.05f, 3),
        C("enemy_down", 0.65f, 0.05f, 0.05f, 2),
        C("enemy_yowl", 0.6f, 0.08f, 0.15f, 2),

        // --- the round
        C("round_start", 0.55f, 0f, 1f, 1),
        C("target_reached", 0.65f, 0f, 1f, 1, Bus.Sfx, 0.5f),
        C("round_won", 0.75f, 0f, 1f, 1, Bus.Sfx, 0.85f),
        C("round_lost", 0.7f, 0f, 1f, 1, Bus.Sfx, 0.85f),
        C("clock_tick", 0.45f, 0f, 0.3f, 1),
        C("unlock", 0.65f, 0f, 0.5f, 1, Bus.Sfx, 0.5f),

        // --- the world map, portals and the squeeze
        C("portal_enter", 0.6f, 0.03f, 0.5f, 1),
        C("portal_reveal", 0.65f, 0.02f, 0.5f, 2, Bus.Sfx, 0.4f),
        C("exit_open", 0.6f, 0.03f, 0.5f, 1),
        C("squeeze_rumble", 0.6f, 0.04f, 0.5f, 1),
        C("squeeze_slam", 0.75f, 0.04f, 0.2f, 2),
        C("revive", 0.6f, 0f, 0.5f, 1),

        // --- menus and the store (the UI slider)
        C("ui_click", 0.45f, 0.06f, 0.04f, 2, Bus.Ui),
        C("ui_hover", 0.2f, 0.08f, 0.05f, 2, Bus.Ui),
        C("ui_pause", 0.5f, 0.05f, 0.1f, 1, Bus.Ui),
        C("buy", 0.6f, 0.02f, 0.1f, 2, Bus.Ui),
        C("denied", 0.5f, 0.04f, 0.15f, 1, Bus.Ui),
        C("pick_confirm", 0.6f, 0f, 0.2f, 1, Bus.Ui),
        C("happy_pig", 0.65f, 0.04f, 0.2f, 1, Bus.Ui),
        C("happy_cat", 0.65f, 0.03f, 0.2f, 1, Bus.Ui),
        C("happy_squirrel", 0.65f, 0.03f, 0.2f, 1, Bus.Ui),
        C("happy_frog", 0.65f, 0.03f, 0.2f, 1, Bus.Ui),
        C("happy_penguin", 0.65f, 0.03f, 0.2f, 1, Bus.Ui),
        C("happy_rhino", 0.65f, 0.03f, 0.2f, 1, Bus.Ui)
    );

    static Dictionary<string, Cue> Build(params Cue[] cues)
    {
        var map = new Dictionary<string, Cue>(cues.Length);
        foreach (Cue cue in cues) map[cue.id] = cue;
        return map;
    }

    // The character's voice suffix, in ChampionSpawner order (Mr. Hyde is the pig)
    public static string Voice(PlayerCharacter player)
    {
        if (player is CatController) return "cat";
        if (player is SquirrelController) return "squirrel";
        if (player is FrogController) return "frog";
        if (player is PenguinController) return "penguin";
        if (player is RhinoController) return "rhino";
        return "pig";
    }

    static readonly string[] PickVoices = { "pig", "cat", "squirrel", "frog", "penguin", "rhino" };
    public static string VoiceForPick(string pickedName)
    {
        switch (pickedName)
        {
            case "jk_rolling": return "cat";
            case "scrat": return "squirrel";
            case "rero_rero": return "frog";
            case "penguin": return "penguin";
            case "rhino": return "rhino";
            default: return "pig";
        }
    }
    public static string VoiceForIndex(int champion) =>
        champion >= 0 && champion < PickVoices.Length ? PickVoices[champion] : "pig";

    // The skill sound for whoever is playing
    public static string Skill(PlayerCharacter player)
    {
        if (player is CatController) return "skill_roll";
        if (player is SquirrelController) return "skill_tail";
        if (player is FrogController) return "skill_tongue";
        if (player is PenguinController) return "skill_breath";
        if (player is RhinoController) return "skill_charge";
        return "skill_block";
    }
}
