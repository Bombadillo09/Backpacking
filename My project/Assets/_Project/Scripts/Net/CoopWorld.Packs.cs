using System.Collections.Generic;
using Backpacking.Camp;
using Backpacking.Character;
using Backpacking.Survival;
using Backpacking.UI;
using Unity.Netcode;
using UnityEngine;

namespace Backpacking.Net
{
    /// <summary>
    /// Packs set down on the ground: anyone's shows in everyone's game, and friends can go through it, moving things
    /// between it and their own pack. While a hiker's pack is down, their game sends what's in it (when it changes,
    /// and every couple of seconds for anyone who joined since). A friend going through it works on a copy, and their
    /// game sends the copy back to the owner's, which takes it as the pack's contents. For a moment after sending,
    /// the friend's copy isn't overwritten by the owner's older news.
    /// </summary>
    public partial class CoopWorld
    {
        const float PackCheckEvery = 0.4f;
        const float PackResendEvery = 2f;
        const float PackEditHold = 1.5f;

        /// <summary>Someone else's pack on the ground here, and the copy of what's in it.</summary>
        sealed class FriendsPack
        {
            public GroundPack pack;
            public Backpack contents;
            /// <summary>The contents as last taken from the owner, or sent to them: anything else is a change made here.</summary>
            public string knownJson;
            public float holdUntil;
        }

        readonly Dictionary<ulong, FriendsPack> friendsPacks = new();
        PackHandling handling;
        PlayerAvatar avatar;
        bool packShown;
        string packJsonSent;
        Vector3 packPlaceSent;
        float nextPackCheck, nextPackResend, nextRummageNote;

        void SpawnPacks()
        {
            handling = player != null ? player.GetComponent<PackHandling>() : FindAnyObjectByType<PackHandling>();
            avatar = player != null ? player.GetComponent<PlayerAvatar>() : null;
        }

        void DespawnPacks()
        {
            foreach (FriendsPack friend in friendsPacks.Values)
                if (friend.pack != null)
                    Destroy(friend.pack.gameObject);
            friendsPacks.Clear();
        }

        /// <summary>Their pack, if it's on the ground here (for the co-op test).</summary>
        public GroundPack FriendsPackOf(ulong owner) => friendsPacks.TryGetValue(owner, out FriendsPack friend) ? friend.pack : null;

        void UpdatePacks()
        {
            if (!synced || Time.unscaledTime < nextPackCheck)
                return;
            nextPackCheck = Time.unscaledTime + PackCheckEvery;
            SendOwnPack();

            List<ulong> gone = null;
            foreach ((ulong owner, FriendsPack friend) in friendsPacks)
            {
                // They left the trip.
                if (friend.pack == null || CoopHiker.Of(owner) == null)
                {
                    (gone ??= new List<ulong>()).Add(owner);
                    continue;
                }
                // Something moved in or out of it here: tell its owner.
                string json = JsonUtility.ToJson(friend.contents.CaptureState());
                if (json == friend.knownJson)
                    continue;
                friend.knownJson = json;
                friend.holdUntil = Time.unscaledTime + PackEditHold;
                friend.pack.ShowGear(friend.contents, false, false);
                PackContentsRpc(json, LocalName(), RpcTarget.Single(owner, RpcTargetUse.Temp));
            }
            if (gone == null)
                return;
            foreach (ulong owner in gone)
                RemoveFriendsPack(owner);
        }

        /// <summary>Tells the others where this player's pack is lying and what's in it, or that it's been picked up.</summary>
        void SendOwnPack()
        {
            bool down = handling != null && handling.Pack != null && !handling.IsWorn && handling.Backpack.HasPack;
            if (!down)
            {
                if (packShown)
                {
                    packShown = false;
                    PackUpRpc(NetworkManager.LocalClientId);
                }
                return;
            }
            string json = JsonUtility.ToJson(handling.Backpack.CaptureState());
            Transform at = handling.Pack.transform;
            bool moved = (at.position - packPlaceSent).sqrMagnitude > 0.0004f;
            if (packShown && json == packJsonSent && !moved && Time.unscaledTime < nextPackResend)
                return;
            packShown = true;
            packJsonSent = json;
            packPlaceSent = at.position;
            nextPackResend = Time.unscaledTime + PackResendEvery;
            Color colour = avatar != null && avatar.Profile != null ? avatar.Profile.packColour : new Color(0.3f, 0.35f, 0.4f);
            PackDownRpc(NetworkManager.LocalClientId, at.position, at.eulerAngles.y, colour, LocalName(), json);
        }

        [Rpc(SendTo.NotMe)]
        void PackDownRpc(ulong owner, Vector3 position, float yaw, Color colour, string ownerName, string json)
        {
            if (owner == NetworkManager.LocalClientId || handling == null || handling.GroundPackPrefab == null)
                return;
            if (!friendsPacks.TryGetValue(owner, out FriendsPack friend) || friend.pack == null)
            {
                GameObject shown = Instantiate(handling.GroundPackPrefab);
                shown.name = $"Co-op pack ({ownerName})";
                // The copy of what's in it: a container no one wears, never ticking over (its owner's game does that).
                var holder = new GameObject("Contents");
                holder.transform.SetParent(shown.transform, false);
                var contents = holder.AddComponent<Backpack>();
                contents.enabled = false;
                friend = new FriendsPack { pack = shown.GetComponent<GroundPack>(), contents = contents };
                friend.pack.MakeFriends(ownerName, contents);
                friendsPacks[owner] = friend;
            }
            friend.pack.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            friend.pack.SetColour(colour);
            // Just changed here (sent, or about to be): the owner's game hasn't caught up yet.
            bool changedHere = friend.knownJson != null && JsonUtility.ToJson(friend.contents.CaptureState()) != friend.knownJson;
            if (Time.unscaledTime >= friend.holdUntil && !changedHere)
            {
                BackpackState state = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<BackpackState>(json);
                if (state != null)
                {
                    friend.contents.RestoreState(state);
                    friend.knownJson = JsonUtility.ToJson(friend.contents.CaptureState());
                }
            }
            friend.pack.ShowGear(friend.contents, false, false);
        }

        [Rpc(SendTo.NotMe)]
        void PackUpRpc(ulong owner) => RemoveFriendsPack(owner);

        void RemoveFriendsPack(ulong owner)
        {
            if (!friendsPacks.TryGetValue(owner, out FriendsPack friend))
                return;
            friendsPacks.Remove(owner);
            if (friend.pack != null)
                Destroy(friend.pack.gameObject);
        }

        /// <summary>A friend moved things in or out of this player's pack: that's what's in it now.</summary>
        [Rpc(SendTo.SpecifiedInParams)]
        void PackContentsRpc(string json, string who, RpcParams rpcParams)
        {
            if (handling == null || handling.Pack == null || handling.IsWorn)
                return;
            BackpackState state = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<BackpackState>(json);
            if (state == null)
                return;
            handling.Backpack.RestoreState(state);
            // Everyone else sees the change straight away.
            packJsonSent = null;
            nextPackCheck = 0f;
            if (Time.unscaledTime >= nextRummageNote)
            {
                nextRummageNote = Time.unscaledTime + 20f;
                Notifications.Post($"{who} is going through your pack.", 3f);
            }
        }
    }
}
