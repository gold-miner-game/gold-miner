using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Art: the papercraft "camera look", added at runtime so no scene or asset changes are needed.
// Warm grade, a soft bloom that makes gold-leaf highlights glow, a dark umber vignette that frames
// the diorama, and fine grain like paper fibre. Purely visual.
public static class PaperGrade
{
    static Volume volume;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Apply();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

    static void Apply()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;
        }
        if (volume != null) return;

        var go = new GameObject("Paper Grade");
        Object.DontDestroyOnLoad(go);
        volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10f;
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.sharedProfile = profile;

        Bloom bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(0.82f);
        bloom.intensity.Override(0.55f);
        bloom.scatter.Override(0.6f);
        bloom.tint.Override(new Color(1f, 0.86f, 0.6f));

        Vignette vignette = profile.Add<Vignette>(true);
        vignette.color.Override(new Color(0.09f, 0.05f, 0.02f));
        vignette.intensity.Override(0.3f);
        vignette.smoothness.Override(0.45f);

        ColorAdjustments grade = profile.Add<ColorAdjustments>(true);
        grade.contrast.Override(8f);
        grade.saturation.Override(6f);
        grade.colorFilter.Override(new Color(1f, 0.97f, 0.92f));

        WhiteBalance warm = profile.Add<WhiteBalance>(true);
        warm.temperature.Override(6f);

        FilmGrain grain = profile.Add<FilmGrain>(true);
        grain.type.Override(FilmGrainLookup.Thin2);
        grain.intensity.Override(0.22f);
        grain.response.Override(0.85f);
    }
}
