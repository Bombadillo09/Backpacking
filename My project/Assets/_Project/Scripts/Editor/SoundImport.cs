using UnityEditor;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Import settings for the field recordings in Resources/Sounds: the long ambience loops stay compressed in
    /// memory, short one-shots are decompressed for instant playback. Only the 2D loops (wind, rain, crickets) keep
    /// stereo; everything else plays from a point in the world and is made mono.
    /// </summary>
    public class SoundImport : AssetPostprocessor
    {
        const string Root = "Assets/_Project/Resources/Sounds/";
        static readonly string[] StereoLoops = { "Wind", "Rain", "Crickets" };
        static readonly string[] Loops = { "Wind", "Rain", "Crickets", "Water", "Fire" };

        public override uint GetVersion() => 1;

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root))
                return;
            string group = assetPath.Substring(Root.Length).Split('/')[0];
            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = System.Array.IndexOf(StereoLoops, group) < 0;
            importer.loadInBackground = false;
            bool loop = System.Array.IndexOf(Loops, group) >= 0;
            importer.defaultSampleSettings = new AudioImporterSampleSettings
            {
                loadType = loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad,
                compressionFormat = AudioCompressionFormat.Vorbis,
                quality = loop ? 0.6f : 0.75f,
                sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate,
                preloadAudioData = true,
            };
        }
    }
}
