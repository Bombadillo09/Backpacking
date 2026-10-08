using System;
using System.IO;
using System.Text;
using Backpacking.Audio;
using Backpacking.World;
using UnityEditor;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// How loud each generated placeholder sound is, and which recordings each sound group has, so new
    /// recordings can be levelled to match what the mixer settings were tuned for. Writes Logs/sound-levels.log.
    /// "run:Backpacking.EditorTools.SoundLevels.Report", or -executeMethod Backpacking.EditorTools.SoundLevels.Batch.
    /// </summary>
    public static class SoundLevels
    {
        const string LogPath = "Logs/sound-levels.log";

        static readonly string[] Groups =
        {
            "Wind", "Rain", "Crickets", "Water", "Fire", "Thunder", "Birds", "Owl", "Flutter", "DeerAlarm", "Chatter",
            "Swish", "Chop", "Heartbeat", "StepSoft", "StepLeaves", "StepHard", "StepSnow", "StepWater",
            "BowDraw", "BowRelease", "ArrowHit",
        };

        public static void Batch()
        {
            Report();
            EditorApplication.Exit(0);
        }

        public static void Report()
        {
            var report = new StringBuilder($"Sound levels {DateTime.Now:yyyy-MM-dd HH:mm}\n");
            report.AppendLine("Generated:");
            Measure(report, "Wind", SoundSynth.Wind());
            Measure(report, "Rain", SoundSynth.Rain());
            Measure(report, "Crickets", SoundSynth.Crickets());
            Measure(report, "Water", SoundSynth.Water());
            Measure(report, "Fire", SoundSynth.Fire());
            Measure(report, "Thunder", SoundSynth.Thunder());
            Measure(report, "Birds", SoundSynth.BirdCall(0));
            Measure(report, "Flutter", SoundSynth.Flutter());
            Measure(report, "DeerAlarm", SoundSynth.Snort());
            Measure(report, "Chatter", SoundSynth.Chatter());
            Measure(report, "Swish", SoundSynth.Swish());
            Measure(report, "Chop", SoundSynth.Chop(0));
            Measure(report, "Heartbeat", SoundSynth.Heartbeat());
            foreach (Surface surface in Enum.GetValues(typeof(Surface)))
                Measure(report, $"Step{surface}", SoundSynth.Footstep(surface, 0));

            report.AppendLine("Recorded:");
            foreach (string group in Groups)
            {
                AudioClip[] clips = Sounds.Recordings(group);
                report.Append($"  {group,-11} {clips.Length} take(s)");
                foreach (AudioClip clip in clips)
                    report.Append($"  {clip.name} {clip.length:0.00}s/{clip.channels}ch");
                report.AppendLine();
            }

            File.AppendAllText(LogPath, report.ToString());
            Debug.Log(report.ToString());
        }

        static void Measure(StringBuilder report, string label, AudioClip clip)
        {
            var samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);
            double sum = 0;
            float peak = 0f;
            foreach (float sample in samples)
            {
                sum += sample * sample;
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            }
            float rms = (float)Math.Sqrt(sum / Mathf.Max(1, samples.Length));
            report.AppendLine($"  {label,-11} {clip.length,6:0.00}s rms {Db(rms),6:0.0} dB  peak {Db(peak),5:0.0} dB");
        }

        static float Db(float level) => 20f * Mathf.Log10(Mathf.Max(level, 1e-9f));
    }
}
