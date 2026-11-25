using UnityEditor;
using UnityEngine;

// Owner: Claude (sound director)
// Import settings for everything under Assets/Resources/Audio, so a new sound needs no manual set-up.
//   Sfx/    short effects: mono, decoded once when loaded (no CPU cost per play), Vorbis in the build
//   Music/  long stereo songs: streamed from disk (not held in memory), loaded in the background
// WebGL uses the browser's own decoder and ignores the load type; the compression still applies.
public class AudioImportRules : AssetPostprocessor
{
    const string Sfx = "Assets/Resources/Audio/Sfx/";
    const string Music = "Assets/Resources/Audio/Music/";

    void OnPreprocessAudio()
    {
        bool sfx = assetPath.StartsWith(Sfx), music = assetPath.StartsWith(Music);
        if (!sfx && !music) return;
        var importer = (AudioImporter)assetImporter;
        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        settings.preloadAudioData = sfx;

        if (sfx)
        {
            importer.forceToMono = true;
            importer.loadInBackground = false;
            settings.loadType = assetPath.Contains("/amb_") ? AudioClipLoadType.CompressedInMemory
                                                            : AudioClipLoadType.DecompressOnLoad;
            settings.quality = 0.7f;
        }
        else
        {
            importer.forceToMono = false;
            importer.loadInBackground = true;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.quality = 0.55f;
        }
        importer.defaultSampleSettings = settings;
    }
}
