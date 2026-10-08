using System;
using System.Collections.Generic;
using Backpacking.World;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Backpacking.Audio
{
    /// <summary>
    /// Every sound in the game, by name. Field recordings come from <c>Resources/Sounds/&lt;group&gt;/</c>; a group
    /// folder can hold several takes, and one is picked at random each time. A group with no recordings falls back
    /// to the generated placeholder from <see cref="SoundSynth"/>, so a sound can be swapped by dropping files in.
    /// </summary>
    public static class Sounds
    {
        static readonly Dictionary<string, AudioClip[]> groups = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => groups.Clear();

        // ---------- Loops ----------

        public static AudioClip Wind() => Any("Wind") ?? SoundSynth.Wind();
        public static AudioClip Rain() => Any("Rain") ?? SoundSynth.Rain();
        public static AudioClip Fire() => Any("Fire") ?? SoundSynth.Fire();
        public static AudioClip Water() => Any("Water") ?? SoundSynth.Water();
        public static AudioClip Crickets() => Any("Crickets") ?? SoundSynth.Crickets();

        // ---------- One-shots ----------

        public static AudioClip BirdCall() => Any("Birds") ?? SoundSynth.BirdCall(Random.Range(0, SoundSynth.BirdVariants));

        /// <summary>An owl hooting at night, or null if there are no recordings (there's no generated owl).</summary>
        public static AudioClip Owl() => Any("Owl");

        public static AudioClip Thunder() => Any("Thunder") ?? SoundSynth.Thunder();
        public static AudioClip Chatter() => Any("Chatter") ?? SoundSynth.Chatter();

        /// <summary>A startled deer's alarm call.</summary>
        public static AudioClip DeerAlarm() => Any("DeerAlarm") ?? SoundSynth.Snort();

        public static AudioClip Flutter() => Any("Flutter") ?? SoundSynth.Flutter();
        public static AudioClip Swish() => Any("Swish") ?? SoundSynth.Swish();
        public static AudioClip Chop() => Any("Chop") ?? SoundSynth.Chop(Random.Range(0, SoundSynth.ChopVariants));
        public static AudioClip Heartbeat() => Any("Heartbeat") ?? SoundSynth.Heartbeat();

        /// <summary>One footstep on <paramref name="surface"/>, never the same take as <paramref name="last"/>.</summary>
        public static AudioClip Footstep(Surface surface, ref int last)
        {
            AudioClip[] steps = Recordings($"Step{surface}");
            int count = steps.Length > 0 ? steps.Length : SoundSynth.FootstepVariants;
            int take = count < 2 ? 0 : Random.Range(0, count - 1);
            if (count >= 2 && take >= last)
                take++;
            last = take;
            return steps.Length > 0 ? steps[take] : SoundSynth.Footstep(surface, take);
        }

        /// <summary>All recordings in a group, in name order (empty if there are none).</summary>
        public static AudioClip[] Recordings(string group)
        {
            if (groups.TryGetValue(group, out AudioClip[] clips))
                return clips;
            clips = Resources.LoadAll<AudioClip>($"Sounds/{group}");
            Array.Sort(clips, (a, b) => string.CompareOrdinal(a.name, b.name));
            groups[group] = clips;
            return clips;
        }

        static AudioClip Any(string group)
        {
            AudioClip[] clips = Recordings(group);
            return clips.Length > 0 ? clips[Random.Range(0, clips.Length)] : null;
        }
    }
}
