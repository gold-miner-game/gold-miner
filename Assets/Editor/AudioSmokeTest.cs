using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Owner: Claude (sound director)
// Batch check of the whole sound layer in Play mode:
//   Unity -batchmode -projectPath <project> -executeMethod AudioSmokeTest.Run -logFile <log>
// Checks that every cue has takes (mono, short, not silent), every music track loads, every cue and
// every watched event plays without errors, the volume settings round-trip, and the real scenes pick
// the right music and ambience. Exit code 0 = pass. Prints AUDIO_SMOKE_PASS / AUDIO_SMOKE_FAIL.
public static class AudioSmokeTest
{
    const string Pending = "AudioSmokeTest.Pending";
    static readonly List<string> failures = new List<string>();
    static int checks;
    static int stage;
    static float stageAt;
    static readonly string[] Tracks = { "under_the_dirt_map", "hog_wild_swing", "velvet_claws", "lantern_light_shuffle",
                                        "evil_meowmeow", "hook_line_and_oinky" };
    // scene -> what should play, checked by really loading the scenes in this order.
    // "radio" = the same radio song as before (no change on scene loads); survival has its own song.
    static readonly (string scene, string track, bool ambience)[] Places =
    {
        ("Menu", "radio", false),
        ("Store", "radio", false),
        ("level_01", "radio", true),
        ("level_survival", "hook_line_and_oinky", true),
        ("level_08", "radio", true),
        ("world_map", "radio", true),
    };
    static string radioSong;

    public static void Run()
    {
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void Resume() { EditorApplication.update += Poll; }

    static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) { failures.Add(what); Debug.LogError("AUDIO_SMOKE check failed: " + what); }
    }

    static void Poll()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling || !SessionState.GetBool(Pending, false)) return;
        try
        {
            if (stage == 0) { StaticChecks(); stage = 1; stageAt = Time.realtimeSinceStartup; return; }
            int place = stage - 1;
            if (place < Places.Length)
            {
                float since = Time.realtimeSinceStartup - stageAt;
                if (since < 0.05f) return;
                if (!loaded) { LevelLoader.Requested = null; SceneManager.LoadScene(Places[place].scene); loaded = true; stageAt = Time.realtimeSinceStartup; return; }
                if (since < 2.5f) return;   // let the crossfade start and the scene settle
                SoundDirector d = SoundDirector.Instance;
                string expect = Places[place].track;
                if (expect == "radio")
                {
                    if (radioSong == null) radioSong = d != null ? d.CurrentTrack : null;
                    expect = radioSong;
                    Check(Array.Exists(SoundDirector.Radio, s => s.file == expect), "radio song '" + expect + "' is not on the radio");
                }
                Check(d != null && d.CurrentTrack == expect,
                      Places[place].scene + " plays '" + (d != null ? d.CurrentTrack : "-") + "', expected '" + expect + "'");
                Check(d != null && d.MusicPlaying, Places[place].scene + ": music is not playing");
                Check(d != null && (d.CurrentAmbience == "amb_mine") == Places[place].ambience,
                      Places[place].scene + ": ambience '" + (d != null ? d.CurrentAmbience : "-") + "'");
                loaded = false; stage++; stageAt = Time.realtimeSinceStartup;
                return;
            }
            // The radio's skip: N moves to the next song, B comes back
            string before = SoundDirector.Instance.CurrentTrack;
            SoundDirector.NextSong();
            Check(SoundDirector.Instance.CurrentTrack != before, "NextSong did not change the song");
            SoundDirector.PreviousSong();
            Check(SoundDirector.Instance.CurrentTrack == before, "PreviousSong did not come back");
            // Every song has its own title
            var titles = new HashSet<string>();
            foreach (var song in SoundDirector.Radio) Check(titles.Add(song.title), "duplicate song title " + song.title);
            Check(titles.Add(SoundDirector.SurvivalSong.title), "survival song shares a title");
            Finish();
        }
        catch (Exception e) { Debug.LogException(e); failures.Add(e.Message); Finish(); }
    }
    static bool loaded;

    static void StaticChecks()
    {
        SoundDirector director = SoundDirector.Instance;
        Check(director != null, "SoundDirector did not create itself");

        foreach (var pair in SoundCues.All)
        {
            AudioClip[] takes = Resources.LoadAll<AudioClip>("Audio/Sfx/" + pair.Key);
            Check(takes.Length >= 1, pair.Key + ": no takes");
            foreach (AudioClip clip in takes)
            {
                Check(clip.channels == 1, pair.Key + "/" + clip.name + ": not mono");
                Check(clip.length > 0.05f && clip.length < 4f, pair.Key + "/" + clip.name + ": length " + clip.length);
                Check(Loudness(clip) > 0.01f, pair.Key + "/" + clip.name + ": silent");
            }
        }
        AudioClip[] amb = Resources.LoadAll<AudioClip>("Audio/Sfx/amb_mine");
        Check(amb.Length >= 1 && amb[0].length > 8f, "amb_mine missing or too short");

        foreach (string track in Tracks)
        {
            AudioClip clip = Resources.Load<AudioClip>("Audio/Music/" + track);
            Check(clip != null && clip.length > 120f && clip.channels == 2, "music '" + track + "' missing or wrong");
        }

        // Every cue plays
        if (director != null)
            foreach (string cue in SoundCues.All.Keys)
            {
                Sfx.Play(cue, Vector3.zero);
                Check(director.CuePlaying(cue), cue + " did not start playing");
            }

        // Every watched event plays without throwing
        string[] events = { "hook_aim", "hook_blocked", "treasure_stolen", "digest_blocked", "player_hit", "hook_damaged",
                            "round_won", "round_lost", "block_unlocked", "sector_cleared", "squeeze_start", "squeeze_closed",
                            "squeeze_crushed", "squeeze_rescue", "exit_open", "exit_reached", "portal_revealed_battle_01",
                            "picked_mr_hyde", "picked_jk_rolling", "picked_scrat", "picked_rero_rero", "picked_penguin", "picked_rhino" };
        foreach (string e in events) TutorialEvents.Fire(e);
        checks++;

        // Volume settings round-trip (and are restored)
        float music = SoundDirector.GetVolume(SoundDirector.Volume.Music);
        bool muted = SoundDirector.Muted;
        SoundDirector.SetVolume(SoundDirector.Volume.Music, 0.25f);
        Check(Mathf.Approximately(SoundDirector.GetVolume(SoundDirector.Volume.Music), 0.25f), "music volume did not stick");
        SoundDirector.Muted = true; Check(SoundDirector.Muted, "mute did not stick");
        SoundDirector.SetVolume(SoundDirector.Volume.Music, music);
        SoundDirector.Muted = muted;
    }

    static float Loudness(AudioClip clip)
    {
        if (clip.loadType == AudioClipLoadType.Streaming) return 1f;
        if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        var data = new float[Mathf.Min(clip.samples * clip.channels, 44100 * 4)];
        if (!clip.GetData(data, 0)) return 1f;   // compressed in memory: can't read, trust the import
        float peak = 0f;
        foreach (float v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
        return peak;
    }

    static void Finish()
    {
        SessionState.SetBool(Pending, false);
        if (failures.Count == 0) Debug.Log("AUDIO_SMOKE_PASS: " + checks + " checks");
        else Debug.LogError("AUDIO_SMOKE_FAIL: " + failures.Count + " of " + checks + " checks failed");
        EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
    }
}
