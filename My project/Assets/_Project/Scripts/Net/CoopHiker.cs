using System;
using System.Collections.Generic;
using Backpacking.Camp;
using Backpacking.Character;
using Backpacking.Interaction;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.Vehicles;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Backpacking.Net
{
    /// <summary>Everything others need to show a hiker, sent several times a second.</summary>
    public struct HikerState : INetworkSerializable
    {
        [Flags]
        public enum Flag : ushort
        {
            Grounded = 1,
            Crouching = 2,
            Busy = 4,
            Seated = 8,
            InChair = 16,
            PackOnBack = 32,
            Sleeping = 64,
            Pad = 128,
            Tent = 256,
            Chair = 512,
            Bottle = 1024,
            Machete = 2048,
            Sprinting = 4096,
            Flashlight = 8192,
        }

        public Vector3 position;
        public float yaw, pitch, speed;
        public Flag flags;
        /// <summary>Seat in the truck (0 driving), or -1.</summary>
        public sbyte seat;
        /// <summary>What's in hand (<see cref="HotbarKind"/>).</summary>
        public byte held;
        /// <summary>Counts machete swings, so a missed packet doesn't lose one.</summary>
        public byte swings;
        /// <summary>
        /// Which food is in hand (<see cref="FoodKind"/>), when it's food; for gear, its picture (<see cref="HeldGear.IconCode"/>);
        /// for the fishing kit, 1 for a proper rod.
        /// </summary>
        public byte heldFood;
        /// <summary>Counts bites, drinks and bandages, like <see cref="swings"/>.</summary>
        public byte uses;
        /// <summary>The bow raised to aim and the string drawn, 0 to 255.</summary>
        public byte bowAim, bowDraw;

        public bool Has(Flag flag) => (flags & flag) != 0;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref position);
            serializer.SerializeValue(ref yaw);
            serializer.SerializeValue(ref pitch);
            serializer.SerializeValue(ref speed);
            ushort bits = (ushort)flags;
            serializer.SerializeValue(ref bits);
            flags = (Flag)bits;
            serializer.SerializeValue(ref seat);
            serializer.SerializeValue(ref held);
            serializer.SerializeValue(ref swings);
            serializer.SerializeValue(ref heldFood);
            serializer.SerializeValue(ref uses);
            serializer.SerializeValue(ref bowAim);
            serializer.SerializeValue(ref bowDraw);
        }
    }

    /// <summary>
    /// One hiker in a co-op trip (NGO's player object). On its owner's game it reads that player's hiker (where they
    /// are, how they move, what they carry) and sends it; on everyone else's it shows them, as a
    /// <see cref="RemoteHikerBody"/>. Their looks and name come from their <see cref="CharacterProfile"/>.
    /// </summary>
    public class CoopHiker : NetworkBehaviour
    {
        [SerializeField] CharacterLibrary library;
        [Tooltip("State updates per second.")]
        [SerializeField] float sendRate = 15f;

        static readonly List<CoopHiker> all = new();

        readonly NetworkVariable<FixedString512Bytes> profileJson = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        FirstPersonController player;
        PlayerActivity activity;
        Backpack backpack;
        PlayerAvatar avatar;
        RemoteHikerBody body;
        Hunting.Bow bow;
        HikerState latest;
        float nextSend;
        byte swings, uses;
        string sentProfile;

        /// <summary>Every hiker in the session, this player's own included.</summary>
        public static IReadOnlyList<CoopHiker> All => all;
        /// <summary>The latest state from this hiker's player.</summary>
        public HikerState State => latest;
        public string HikerName { get; private set; } = "Hiker";
        /// <summary>The body shown for another player's hiker (null for this player's own).</summary>
        public RemoteHikerBody Body => body;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => all.Clear();

        /// <summary>Removes every other hiker's body (the session ended).</summary>
        public static void ClearAll()
        {
            foreach (CoopHiker hiker in all)
                if (hiker != null && hiker.body != null)
                    Destroy(hiker.body.gameObject);
            all.Clear();
        }

        public static CoopHiker Of(ulong clientId)
        {
            foreach (CoopHiker hiker in all)
                if (hiker != null && hiker.OwnerClientId == clientId)
                    return hiker;
            return null;
        }

        public override void OnNetworkSpawn()
        {
            all.Add(this);
            if (IsOwner)
            {
                FindLocalHiker();
                Undergrowth.Swung += OnSwung;
                Hotbar.Used += OnUsed;
            }
            else
            {
                profileJson.OnValueChanged += (_, json) => ApplyProfile(json.ToString());
                ApplyProfile(profileJson.Value.ToString());
            }
        }

        public override void OnNetworkDespawn()
        {
            all.Remove(this);
            Undergrowth.Swung -= OnSwung;
            Hotbar.Used -= OnUsed;
            if (!IsOwner)
                World.OtherHikers.Remove(OwnerClientId);
            if (body != null)
                Destroy(body.gameObject);
        }

        void FindLocalHiker()
        {
            player = FindAnyObjectByType<FirstPersonController>();
            if (player == null)
                return;
            activity = player.GetComponent<PlayerActivity>();
            backpack = player.GetComponent<Backpack>();
            avatar = player.GetComponent<PlayerAvatar>();
            bow = player.GetComponentInChildren<Hunting.Bow>();
            if (bow == null)
                bow = FindAnyObjectByType<Hunting.Bow>();
        }

        void OnSwung() => swings++;
        void OnUsed(HotbarKind kind) => uses++;

        void Update()
        {
            if (!IsSpawned)
                return;
            if (IsOwner)
                SendOwnState();
        }

        void LateUpdate()
        {
            // After their body has moved this frame.
            if (IsSpawned && !IsOwner && body != null && latest.position != Vector3.zero)
            {
                // The world (animals, snares, wildlife) takes them into account.
                World.OtherHikers.Set(new World.OtherHiker
                {
                    id = OwnerClientId,
                    position = body.transform.position,
                    forward = Quaternion.Euler(0f, latest.yaw, 0f) * Vector3.forward,
                    speed = latest.speed,
                    crouching = latest.Has(HikerState.Flag.Crouching),
                    sprinting = latest.Has(HikerState.Flag.Sprinting),
                });
            }
        }

        // ---------- This player's own hiker ----------

        void SendOwnState()
        {
            if (player == null)
            {
                FindLocalHiker();
                if (player == null)
                    return;
            }
            // The profile goes once (and again if it changes: a new hiker made in the creator).
            CharacterProfile profile = avatar != null ? avatar.Profile : null;
            if (profile != null)
            {
                string json = JsonUtility.ToJson(profile);
                if (json != sentProfile && json.Length < FixedString512Bytes.UTF8MaxLengthInBytes)
                {
                    sentProfile = json;
                    profileJson.Value = new FixedString512Bytes(json);
                    HikerName = profile.name;
                }
            }
            if (Time.unscaledTime < nextSend)
                return;
            nextSend = Time.unscaledTime + 1f / sendRate;
            latest = Capture();
            StateRpc(latest);
        }

        HikerState Capture()
        {
            Transform view = player.CameraPivot;
            var state = new HikerState
            {
                position = player.transform.position,
                yaw = player.transform.eulerAngles.y,
                pitch = view != null ? view.localEulerAngles.x : 0f,
                speed = player.HorizontalSpeed,
                seat = (sbyte)(Pickup.Current != null ? Pickup.Current.LocalSeat : -1),
                swings = swings,
            };
            if (state.pitch > 180f)
                state.pitch -= 360f;
            if (player.Mounted && view != null)
                state.yaw = view.eulerAngles.y;
            HikerState.Flag flags = 0;
            if (player.IsGrounded || player.Mounted)
                flags |= HikerState.Flag.Grounded;
            if (player.IsCrouching)
                flags |= HikerState.Flag.Crouching;
            if (player.IsSprinting)
                flags |= HikerState.Flag.Sprinting;
            if (activity != null && activity.IsBusy && !activity.IsSleeping)
                flags |= HikerState.Flag.Busy;
            if (activity != null && activity.IsSleeping)
                flags |= HikerState.Flag.Sleeping;
            if (player.Seated)
                flags |= HikerState.Flag.Seated;
            if ((player.Mounted && !player.MountedOnGround) || (RestMode.Current != null && RestMode.Current.InChair))
                flags |= HikerState.Flag.InChair;
            if (backpack != null && backpack.IsWorn && backpack.HasPack && !player.Mounted)
            {
                flags |= HikerState.Flag.PackOnBack;
                HotbarSlot held = Hotbar.Current != null ? Hotbar.Current.Held : default;
                backpack.OutsideGear(out bool pad, out bool tent, out bool chair, out bool bottle, out bool machete);
                if (pad)
                    flags |= HikerState.Flag.Pad;
                if (tent)
                    flags |= HikerState.Flag.Tent;
                if (chair)
                    flags |= HikerState.Flag.Chair;
                if (bottle && held.kind != HotbarKind.Water)
                    flags |= HikerState.Flag.Bottle;
                if (machete && held.kind != HotbarKind.Machete)
                    flags |= HikerState.Flag.Machete;
            }
            HotbarSlot inHand = Hotbar.Current != null ? Hotbar.Current.Held : new HotbarSlot(HotbarKind.Empty);
            if (Hotbar.Current != null && Hotbar.Current.Shining)
                flags |= HikerState.Flag.Flashlight;
            state.flags = flags;
            state.held = (byte)inHand.kind;
            state.heldFood = inHand.kind switch
            {
                HotbarKind.Gear => HeldGear.IconCode(inHand.icon),
                HotbarKind.FishingRod => (byte)(backpack != null && backpack.HasGoodRod ? 1 : 0),
                _ => (byte)inHand.food,
            };
            state.uses = uses;
            if (bow != null && bow.isActiveAndEnabled)
            {
                state.bowAim = (byte)Mathf.RoundToInt(Mathf.Clamp01(bow.Aim) * 255f);
                state.bowDraw = (byte)Mathf.RoundToInt(Mathf.Clamp01(bow.Draw) * 255f);
            }
            return state;
        }

        [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
        void StateRpc(HikerState state)
        {
            latest = state;
            if (body != null)
                body.Receive(state);
        }

        // ---------- Someone else's hiker ----------

        void ApplyProfile(string json)
        {
            if (string.IsNullOrEmpty(json))
                return;
            CharacterProfile profile;
            try
            {
                profile = JsonUtility.FromJson<CharacterProfile>(json);
            }
            catch (ArgumentException)
            {
                return;
            }
            if (profile == null)
                return;
            HikerName = profile.name;
            if (body == null)
                body = RemoteHikerBody.Create(this, library);
            body.Build(profile);
            body.Receive(latest);
        }
    }
}
