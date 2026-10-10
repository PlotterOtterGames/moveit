using UnityEditor;
using UnityEngine;

namespace MoveIt.EditorTools
{
    /// <summary>
    /// Import settings for the drop-in audio in Assets/Resources/Music and Assets/Resources/Sfx (see music/README.md):
    /// music streams from disk (little RAM on the box), sound effects are mono and decoded up front (no delay).
    /// Applies to any file you drop in, whatever program made it.
    /// </summary>
    sealed class AudioImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            bool music = assetPath.Contains("/Resources/Music/");
            bool sfx = assetPath.Contains("/Resources/Sfx/");
            if (!music && !sfx) return;
            var importer = (AudioImporter)assetImporter;
            var settings = importer.defaultSampleSettings;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = music ? 0.6f : 0.7f;
            settings.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = sfx;
            importer.loadInBackground = music;
        }
    }
}
