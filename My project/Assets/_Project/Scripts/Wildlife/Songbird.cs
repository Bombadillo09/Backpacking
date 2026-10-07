using UnityEngine;

namespace Backpacking.Wildlife
{
    /// <summary>
    /// One animated songbird's voice and animation hooks. Its hop and song animations call back here by name
    /// (the clips' animation events): a hop resets itself when done, and the song animation plays one of its songs.
    /// <see cref="BirdFlock"/> drives everything else.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class Songbird : MonoBehaviour
    {
        public AudioClip[] songs;
        public AudioClip[] takeOff;

        static readonly int HopHash = Animator.StringToHash("hop");

        Animator animator;
        AudioSource voice;

        public Animator Animator => animator != null ? animator : animator = GetComponent<Animator>();

        void Awake()
        {
            voice = GetComponent<AudioSource>();
            if (voice == null)
                voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 1f;
            voice.minDistance = 3f;
            voice.maxDistance = 55f;
            voice.rolloffMode = AudioRolloffMode.Logarithmic;
            voice.dopplerLevel = 0f;
        }

        public void PlayTakeOff()
        {
            if (takeOff != null && takeOff.Length > 0)
                voice.PlayOneShot(takeOff[Random.Range(0, takeOff.Length)], 0.25f);
        }

        // ---------- Animation events ----------

        void ResetHopInt() => Animator.SetInteger(HopHash, 0);

        void PlaySong()
        {
            if (songs != null && songs.Length > 0)
                voice.PlayOneShot(songs[Random.Range(0, songs.Length)], Random.Range(0.6f, 1f));
        }

        void ResetFlyingLandingVariables() { }
    }
}
