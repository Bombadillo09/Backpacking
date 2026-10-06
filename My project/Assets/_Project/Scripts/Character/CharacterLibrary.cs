using System;
using UnityEngine;

namespace Backpacking.Character
{
    /// <summary>
    /// Everything needed to build a hiker: the two bodies, hairstyles, materials and the animator.
    /// Created by the editor's character setup from the Quaternius models in Art/Characters.
    /// </summary>
    [CreateAssetMenu(menuName = "Backpacking/Character Library", fileName = "CharacterLibrary")]
    public class CharacterLibrary : ScriptableObject
    {
        [Serializable]
        public class HairStyle
        {
            public string name;
            public GameObject model;
            [Tooltip("Which bodies it suits; both if both are ticked.")]
            public bool male = true, female = true;
        }

        public GameObject maleBody;
        public GameObject femaleBody;
        [Tooltip("The first entry is 'none' (no model).")]
        public HairStyle[] hairStyles;
        public GameObject beard;

        [Header("Materials (tinted per hiker at runtime)")]
        public Material maleSkin;
        public Material femaleSkin;
        public Material hairShort;
        public Material hairLong;
        public Material eyes;
        public Material clothing;
        public Material boots;
        public Material pack;

        public RuntimeAnimatorController animator;
    }
}
