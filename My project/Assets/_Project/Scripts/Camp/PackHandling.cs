using System;
using Backpacking.Character;
using Backpacking.Survival;
using Backpacking.UI;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>Where the pack and the tent bag are, for the save file.</summary>
    [Serializable]
    public class PackState
    {
        public bool worn = true;
        public Vector3 packPosition;
        public Quaternion packRotation = Quaternion.identity;
        public bool tentBagOut;
        public Vector3 tentBagPosition;
        public Quaternion tentBagRotation = Quaternion.identity;
    }

    /// <summary>
    /// Taking the backpack off and putting it on, and getting the tent in and out of it. Off your back, the pack
    /// is an object on the ground: you walk lighter, the backpack screen only opens beside it, and you're
    /// reminded if you walk off without it. The tent comes out in its bag, which lies on the ground until you
    /// unpack it where you want to pitch (or put it back).
    /// </summary>
    public class PackHandling : MonoBehaviour
    {
        [SerializeField] Backpack backpack;
        [SerializeField] GameObject groundPackPrefab;
        [SerializeField] GameObject tentBagPrefab;
        [Tooltip("How close you need to be to the pack to use what's in it, in metres.")]
        [SerializeField] float reach = 3f;
        [Tooltip("Walk this far from the pack and you're reminded it's still on the ground.")]
        [SerializeField] float forgottenDistance = 25f;

        static PackHandling current;
        bool warnedForgotten;
        PlayerAvatar avatar;

        public static PackHandling Current => current;
        public GroundPack Pack { get; private set; }
        public TentBag TentBag { get; private set; }
        public bool IsWorn => backpack.IsWorn;
        public float Reach => reach;

        /// <summary>The pack is on your back, or near enough to reach into.</summary>
        public bool CanReachPack => backpack.IsWorn || (Pack != null && DistanceTo(Pack.transform) <= reach);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => current = null;

        void Awake()
        {
            current = this;
            avatar = GetComponent<PlayerAvatar>();
        }

        void OnDestroy()
        {
            if (current == this)
                current = null;
        }

        float DistanceTo(Transform thing)
        {
            Vector3 offset = thing.position - transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }

        /// <summary>How far away the pack is, or 0 if you're wearing it.</summary>
        public float PackDistance => Pack != null ? DistanceTo(Pack.transform) : 0f;

        // ---------- The pack ----------

        /// <summary>Takes the pack off and sets it down just in front of you.</summary>
        public void SetDown()
        {
            if (!backpack.IsWorn)
                return;
            Vector3 spot = transform.position + transform.forward * 0.7f + transform.right * 0.25f;
            SetDownAt(spot, Quaternion.LookRotation(-transform.forward));
            Notifications.Post("You swing your pack off and set it down.", 2.5f);
        }

        /// <summary>Sets the pack down at a spot (on the ground beneath it).</summary>
        public void SetDownAt(Vector3 spot, Quaternion rotation)
        {
            backpack.IsWorn = false;
            warnedForgotten = false;
            if (Pack == null)
                Pack = Instantiate(groundPackPrefab).GetComponent<GroundPack>();
            MovePack(spot, rotation);
            if (avatar != null)
                Pack.SetColour(avatar.Profile.packColour);
        }

        public void MovePack(Vector3 spot, Quaternion rotation)
        {
            if (Pack == null)
                return;
            Pack.transform.SetPositionAndRotation(OnGround(spot), Quaternion.Euler(0f, rotation.eulerAngles.y, 0f));
        }

        public void PickUp()
        {
            if (Pack == null)
                return;
            Destroy(Pack.gameObject);
            Pack = null;
            backpack.IsWorn = true;
            Notifications.Post("You shoulder your pack and buckle the hip belt.", 2.5f);
        }

        // ---------- The tent bag ----------

        /// <summary>Pulls the tent out of the pack in its bag and lays the bag beside the pack.</summary>
        public bool TakeOutTent()
        {
            if (backpack.IsWorn || Pack == null || !backpack.HasTent || TentBag != null)
                return false;
            backpack.HasTent = false;
            Transform pack = Pack.transform;
            DropTentBag(pack.position + pack.right * 0.55f + pack.forward * 0.15f, pack.rotation * Quaternion.Euler(0f, 80f, 0f));
            Notifications.Post("You pull the tent bag out of your pack. Look at it to unpack it where you want to pitch.", 4f);
            return true;
        }

        /// <summary>Lays the tent bag on the ground at a spot (when taking the tent out, or after taking it down).</summary>
        public void DropTentBag(Vector3 spot, Quaternion rotation)
        {
            if (TentBag == null)
                TentBag = Instantiate(tentBagPrefab).GetComponent<TentBag>();
            TentBag.transform.SetPositionAndRotation(OnGround(spot), Quaternion.Euler(0f, rotation.eulerAngles.y, 0f));
            TentBag.SetModel(backpack.TentModel);
        }

        /// <summary>Why the tent bag can't go back in the pack right now, or null.</summary>
        public string TentBagProblem() =>
            TentBag == null ? "" :
            !CanReachPack ? "Bring it to your pack first (or put the pack on)" :
            null;

        public void PackTentBag()
        {
            if (TentBag == null || TentBagProblem() != null)
                return;
            Destroy(TentBag.gameObject);
            TentBag = null;
            backpack.HasTent = true;
            Notifications.Post("The tent's back in your pack.", 2.5f);
        }

        /// <summary>The bag is used up when the tent is unpacked and laid out from it.</summary>
        public void ConsumeTentBag()
        {
            if (TentBag != null)
                Destroy(TentBag.gameObject);
            TentBag = null;
        }

        // ---------- Upkeep ----------

        void Update()
        {
            if (Pack == null || backpack.IsWorn)
                return;
            float distance = DistanceTo(Pack.transform);
            if (!warnedForgotten && distance > forgottenDistance)
            {
                warnedForgotten = true;
                Notifications.Post("You've left your pack behind! Everything you own is in it. Go back for it.", 6f);
            }
            else if (warnedForgotten && distance < forgottenDistance * 0.6f)
                warnedForgotten = false;
        }

        /// <summary>The ground under a spot, ignoring the player.</summary>
        Vector3 OnGround(Vector3 spot)
        {
            RaycastHit[] hits = Physics.RaycastAll(spot + Vector3.up * 2f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                // Not on yourself or your own gear: a pack set down inside the tent sits on its floor.
                if (hit.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<GroundPack>() != null
                    || hit.collider.GetComponentInParent<TentBag>() != null || hit.collider.GetComponentInParent<Tent>() != null)
                    continue;
                return hit.point;
            }
            return spot;
        }

        // ---------- Saving ----------

        public PackState CaptureState() => new()
        {
            worn = backpack.IsWorn,
            packPosition = Pack != null ? Pack.transform.position : Vector3.zero,
            packRotation = Pack != null ? Pack.transform.rotation : Quaternion.identity,
            tentBagOut = TentBag != null,
            tentBagPosition = TentBag != null ? TentBag.transform.position : Vector3.zero,
            tentBagRotation = TentBag != null ? TentBag.transform.rotation : Quaternion.identity,
        };

        public void RestoreState(PackState state)
        {
            if (Pack != null)
                Destroy(Pack.gameObject);
            Pack = null;
            ConsumeTentBag();
            backpack.IsWorn = true;
            if (state == null)
                return;
            if (!state.worn)
                SetDownAt(state.packPosition, state.packRotation);
            if (state.tentBagOut)
                DropTentBag(state.tentBagPosition, state.tentBagRotation);
        }
    }
}
