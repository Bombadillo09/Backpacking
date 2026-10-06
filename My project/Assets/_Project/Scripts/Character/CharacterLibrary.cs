using System;
using UnityEngine;

namespace Backpacking.Character
{
    /// <summary>
    /// Everything needed to build a hiker: the roster of Microsoft Rocketbox people to choose from, the bare feet
    /// shown when boots come off, gear materials and the animator. Created by the editor's character setup from
    /// Art/Characters/Rocketbox.
    /// </summary>
    [CreateAssetMenu(menuName = "Backpacking/Character Library", fileName = "CharacterLibrary")]
    public class CharacterLibrary : ScriptableObject
    {
        [Serializable]
        public class Hiker
        {
            [Tooltip("Folder and model name, e.g. Male_Adult_05. Saved in the profile.")]
            public string id;
            [Tooltip("Shown in the creator, e.g. 'Field vest'.")]
            public string label;
            public bool female;
            public GameObject model;
            [Tooltip("Humanoid avatar defined from a proper T-pose (palms down), so animations put the hands the right way round.")]
            public Avatar avatar;
            public Material body, head, hair;
            [Tooltip("Height of the top of their shoes or boots, standing (metres). Below it they're cut away for bare feet, and that piece is the footwear set down beside them.")]
            public float footwearTop = 0.12f;
            [Tooltip("Tint for the borrowed bare feet so they match this person's skin.")]
            public Color skinTint = Color.white;
        }

        public Hiker[] hikers;

        [Header("Bare feet (lower legs of the swimwear models, same skeleton)")]
        public GameObject maleFeet;
        public GameObject femaleFeet;
        public Material maleFeetSkin;
        public Material femaleFeetSkin;

        [Header("Gear (tinted per hiker at runtime)")]
        public Material pack;
        public Material socks;

        public RuntimeAnimatorController animator;

        /// <summary>The hiker with this id, or the first one if it's unknown (e.g. a save from before the roster).</summary>
        public Hiker Find(string id)
        {
            if (hikers == null || hikers.Length == 0)
                return null;
            foreach (Hiker hiker in hikers)
                if (hiker.id == id && hiker.model != null)
                    return hiker;
            foreach (Hiker hiker in hikers)
                if (hiker.model != null)
                    return hiker;
            return null;
        }
    }
}
