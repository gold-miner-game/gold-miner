using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Owner: Claude (sound director)
// Proves the sounds come from real gameplay, not from a test calling them: loads real levels, lets the
// miners play, nudges the player like a person would (a K press, walking onto gold), and lists every
// cue the game itself triggered.
//   Unity -batchmode -projectPath <project> -executeMethod AudioGameplayProbe.Run -logFile <log>
// Prints AUDIO_PROBE lines; exit 0 when the core gameplay cues were heard.
public static class AudioGameplayProbe
{
    const string Pending = "AudioGameplayProbe.Pending";
    static readonly (string scene, float seconds)[] Steps = { ("level_02", 40f), ("level_roam", 30f), ("world_map", 12f), ("Menu", 4f) };
    static readonly List<string> songs = new List<string>();
    static float nextCharge;
    static readonly Dictionary<string, int> heard = new Dictionary<string, int>();
    static int step = -1;
    static float stepAt;
    static bool skillPressed, walkedToGold;
    static string scene;
    static string character = "?";

    public static void Run() { SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode(); }

    [InitializeOnLoadMethod]
    static void Resume() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling || !SessionState.GetBool(Pending, false)) return;
        try
        {
            if (step < 0)
            {
                SoundDirector.Played += id => { string k = scene + ":" + id; heard[k] = heard.TryGetValue(k, out int n) ? n + 1 : 1; };
                Next();
                return;
            }
            float t = Time.realtimeSinceStartup - stepAt;
            if (scene == "level_02") Nudge(t);
            if (scene == "level_roam") Brawl(t);
            if (t < Steps[step].seconds) return;
            if (step + 1 < Steps.Length) { Next(); return; }
            Finish();
        }
        catch (Exception e) { Debug.LogException(e); Finish(); }
    }

    static void Next()
    {
        if (step >= 0 && SoundDirector.Instance != null) songs.Add(scene + "=" + SoundDirector.Instance.CurrentTrack);
        step++;
        nextCharge = 0f;
        scene = Steps[step].scene;
        skillPressed = walkedToGold = false;
        LevelLoader.Requested = null;
        SceneManager.LoadScene(scene);
        stepAt = Time.realtimeSinceStartup;
    }

    // What a player would do: press K once, then walk onto a piece of gold
    static void Nudge(float t)
    {
        PlayerCharacter player = UnityEngine.Object.FindFirstObjectByType<PlayerCharacter>();
        if (player == null) return;
        if (!skillPressed && t > 6f)
        {
            skillPressed = true;
            player.GetType().GetMethod("UseSkill", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy)?.Invoke(player, null);
        }
        if (!walkedToGold && t > 10f && Treasure.Active.Count > 0)
        {
            walkedToGold = true;
            Treasure gold = Treasure.Active.FirstOrDefault(x => x != null);
            if (gold != null)
            {
                var body = player.GetComponent<Rigidbody2D>();
                if (body != null) body.position = gold.transform.position; else player.transform.position = gold.transform.position;
            }
        }
    }

    // The roam level's Brawlers: stand next to the nearest one and use the skill whenever it is ready
    static void Brawl(float t)
    {
        PlayerCharacter player = UnityEngine.Object.FindFirstObjectByType<PlayerCharacter>();
        if (player == null || t < 3f || t < nextCharge) return;
        character = SoundCues.Voice(player);
        nextCharge = t + 0.5f;
        RedEnemy enemy = UnityEngine.Object.FindObjectsByType<RedEnemy>(FindObjectsSortMode.None)
            .Where(e => e.isActiveAndEnabled).OrderBy(e => Vector2.Distance(e.transform.position, player.transform.position)).FirstOrDefault();
        if (enemy != null)
        {
            Vector2 spot = (Vector2)enemy.transform.position + Vector2.left * 1.2f;
            var body = player.GetComponent<Rigidbody2D>();
            if (body != null) body.position = spot; else player.transform.position = spot;
        }
        if (player.SkillReadyIn <= 0f)
            player.GetType().GetMethod("UseSkill", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy)?.Invoke(player, null);
    }

    static void Finish()
    {
        if (SoundDirector.Instance != null) songs.Add(scene + "=" + SoundDirector.Instance.CurrentTrack);
        Debug.Log("AUDIO_PROBE songs by scene: " + string.Join(", ", songs));
        SessionState.SetBool(Pending, false);
        foreach (var pair in heard.OrderBy(p => p.Key)) Debug.Log("AUDIO_PROBE heard " + pair.Key + " x" + pair.Value);
        // Only the pig's block and the rhino's charge knock Brawlers back (the cat rolls through them)
        var core = new List<string> { "hook_fire", "eat_munch", "round_start", "skill_ready" };
        if (character == "pig" || character == "rhino") core.Add("enemy_yowl");
        Debug.Log("AUDIO_PROBE character in the roam level: " + character);
        string[] missing = core.Where(c => !heard.Keys.Any(k => k.EndsWith(":" + c))).ToArray();
        SoundDirector d = SoundDirector.Instance;
        Debug.Log("AUDIO_PROBE music now: " + (d != null ? d.CurrentTrack : "-") + ", cues heard: " + heard.Count);
        if (missing.Length == 0) Debug.Log("AUDIO_PROBE_PASS");
        else Debug.LogError("AUDIO_PROBE_FAIL missing " + string.Join(", ", missing));
        EditorApplication.Exit(missing.Length == 0 ? 0 : 1);
    }
}
