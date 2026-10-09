using System.Collections.Generic;
using Backpacking.Camp;
using Backpacking.Player;
using Backpacking.Trade;
using Backpacking.Wildlife;
using Backpacking.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Backpacking.UI
{
    /// <summary>
    /// Pointing things out: press G (or the middle mouse button) and whatever you're looking at, out to 600 m, gets a
    /// marker everyone on the trip sees for half a minute: "Sam: deer · 85 m". It says what it is (a deer, water, a
    /// camp, a trading post) or just "here". One each at a time; it's on the map and compass too.
    /// </summary>
    public class Pings : MonoBehaviour
    {
        const float Lifetime = 30f;
        const float Reach = 600f;

        sealed class Ping
        {
            public string who, label;
            public Vector3 position;
            public float until;
            public Label tag;
        }

        readonly Dictionary<string, Ping> pings = new();
        string myName = "You";

        public static Pings Current { get; private set; }

        /// <summary>This player pointed something out: (where, what). Co-op tells the others.</summary>
        public static event System.Action<Vector3, string> Placed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Current = null;
            Placed = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (Current == null && FindAnyObjectByType<FirstPersonController>() != null)
                new GameObject("Pings").AddComponent<Pings>();
        }

        void Awake() => Current = this;

        void OnDestroy()
        {
            if (Current == this)
                Current = null;
            foreach (Ping ping in pings.Values)
                ping.tag?.RemoveFromHierarchy();
        }

        public IEnumerable<(string who, string label, Vector3 position)> Active
        {
            get
            {
                foreach (Ping ping in pings.Values)
                    yield return (ping.who, ping.label, ping.position);
            }
        }

        void Update()
        {
            if (PlayerControlLock.MovementLocked || PlayerControlLock.CursorNeeded)
                return;
            bool pressed = (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
                           || (Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame);
            if (pressed)
                PingWhereLooking();
        }

        /// <summary>Marks what the view's centre is on.</summary>
        public void PingWhereLooking()
        {
            Camera view = Camera.main;
            if (view == null)
                return;
            Transform eye = view.transform;
            if (!Physics.Raycast(eye.position + eye.forward * 0.6f, eye.forward, out RaycastHit hit, Reach, ~0, QueryTriggerInteraction.Collide))
            {
                Notifications.Post("Nothing there to point out.", 1.5f);
                return;
            }
            Point(hit.point, Describe(hit.collider));
        }

        /// <summary>Points out a spot, as this player, for everyone.</summary>
        public void Point(Vector3 position, string label)
        {
            if (Trip.TripLog.HikerName is { Length: > 0 } name)
                myName = name;
            Add(myName, label, position);
            Placed?.Invoke(position, label);
        }

        /// <summary>What's been pointed at, in a word or two.</summary>
        static string Describe(Collider thing)
        {
            if (thing.GetComponentInParent<Animal>() is { } animal)
                return animal.Kind.ToString().ToLowerInvariant();
            if (thing.GetComponentInParent<WaterSource>() is { } water)
                return water.DisplayName.ToLowerInvariant();
            if (thing.GetComponentInParent<Campfire>() != null)
                return "fire";
            if (thing.GetComponentInParent<Tent>() != null)
                return "tent";
            if (thing.GetComponentInParent<GroundPack>() != null)
                return "pack";
            if (thing.GetComponentInParent<Vendor>() is { } vendor)
                return vendor.DisplayName;
            if (thing.GetComponentInParent<Interaction.FirewoodPickup>() != null)
                return "firewood";
            if (thing.GetComponentInParent<Gathering.BerryBush>() != null)
                return "berries";
            if (thing.GetComponentInParent<Net.RemoteHikerBody>() is { } friend)
                return friend.DisplayName;
            if (thing.GetComponentInParent<Vehicles.Pickup>() != null)
                return "the truck";
            return "here";
        }

        /// <summary>Shows someone's ping (replacing their last one).</summary>
        public void Add(string who, string label, Vector3 position)
        {
            if (pings.TryGetValue(who, out Ping old))
                old.tag?.RemoveFromHierarchy();
            var ping = new Ping { who = who, label = label, position = position, until = Time.unscaledTime + Lifetime };
            pings[who] = ping;
            MapPins.Set(new MapPin { id = $"ping-{who}", label = $"{who}: {label}", kind = PinKind.Ping, position = position, colour = new Color(0.95f, 0.5f, 0.15f) });
            Notifications.Post($"{who} points out {(label == "here" ? "a spot" : label)}.", 2f);
        }

        void LateUpdate()
        {
            Camera view = Camera.main;
            List<string> expired = null;
            foreach (Ping ping in pings.Values)
            {
                if (Time.unscaledTime > ping.until)
                {
                    (expired ??= new List<string>()).Add(ping.who);
                    continue;
                }
                if (ping.tag == null && GameUI.Current != null)
                {
                    ping.tag = UIBuild.Text("", "ping-tag", "shadowed");
                    ping.tag.style.position = Position.Absolute;
                    ping.tag.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-100f));
                    GameUI.Current.Hud.Add(ping.tag.IgnoreMouse());
                }
                if (ping.tag == null)
                    continue;
                Vector3 at = ping.position + Vector3.up * 0.6f;
                bool show = view != null && !PlayerControlLock.CursorNeeded && Vector3.Dot(at - view.transform.position, view.transform.forward) > 0.1f;
                ping.tag.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show || ping.tag.panel == null)
                    continue;
                Vector2 panel = RuntimePanelUtils.CameraTransformWorldToPanel(ping.tag.panel, at, view);
                ping.tag.style.left = panel.x;
                ping.tag.style.top = panel.y;
                float distance = Vector3.Distance(view.transform.position, ping.position);
                ping.tag.text = $"• {ping.who}: {ping.label} · {distance:0} m";
                // Fading out over its last few seconds.
                ping.tag.style.opacity = Mathf.Clamp01((ping.until - Time.unscaledTime) / 4f);
            }
            if (expired == null)
                return;
            foreach (string who in expired)
            {
                pings[who].tag?.RemoveFromHierarchy();
                pings.Remove(who);
                MapPins.Remove($"ping-{who}");
            }
        }
    }
}
