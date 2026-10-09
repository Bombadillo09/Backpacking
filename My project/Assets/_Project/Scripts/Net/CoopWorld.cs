using System;
using System.Collections.Generic;
using System.Text;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.UI;
using Backpacking.Vehicles;
using Backpacking.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Backpacking.Net
{
    /// <summary>Who sits in each of the truck's four seats (a player's id, or <see cref="Empty"/>).</summary>
    public struct TruckSeats : INetworkSerializable, IEquatable<TruckSeats>
    {
        public const ulong Empty = ulong.MaxValue;
        public ulong driver, frontPassenger, rearLeft, rearRight;

        public static TruckSeats None => new() { driver = Empty, frontPassenger = Empty, rearLeft = Empty, rearRight = Empty };

        public ulong this[int seat]
        {
            get => seat switch { 0 => driver, 1 => frontPassenger, 2 => rearLeft, 3 => rearRight, _ => Empty };
            set
            {
                switch (seat)
                {
                    case 0: driver = value; break;
                    case 1: frontPassenger = value; break;
                    case 2: rearLeft = value; break;
                    case 3: rearRight = value; break;
                }
            }
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref driver);
            serializer.SerializeValue(ref frontPassenger);
            serializer.SerializeValue(ref rearLeft);
            serializer.SerializeValue(ref rearRight);
        }

        public bool Equals(TruckSeats other) =>
            driver == other.driver && frontPassenger == other.frontPassenger && rearLeft == other.rearLeft && rearRight == other.rearRight;
    }

    /// <summary>
    /// The world everyone in a co-op trip shares, kept the same in every game. The host's game keeps the clock and
    /// the weather, and everyone follows it; the clock only speeds up (sleeping, fast-forward, timed tasks) as far as
    /// everyone asks, so it never skips while someone is awake and hiking. Doors swing for everyone. The truck's
    /// seats are given out by the host, and the truck's physics run in the driver's game (the host's while it's
    /// parked) while everyone else's truck follows. The truck bed is shared: what anyone puts in or takes out of it,
    /// everyone sees. Spawned by the host when a session starts.
    /// </summary>
    public class CoopWorld : NetworkBehaviour
    {
        [Tooltip("Game minutes the clock may drift from the host's before it's set straight.")]
        [SerializeField] float clockTolerance = 1.5f;
        [SerializeField] float weatherEvery = 15f;

        readonly NetworkVariable<TruckSeats> seats = new(TruckSeats.None);

        TimeOfDay clock;
        WeatherSystem weather;
        Pickup truck;
        FirstPersonController player;
        Door[] doors = Array.Empty<Door>();
        // The host's record of how fast each player wants the clock to go.
        readonly Dictionary<ulong, float> wanted = new();
        float lastWanted = -1f;
        float nextClock, nextWeather, nextTruck, nextBedCheck, nextWantedResend;
        string bedJson;
        bool synced;
        int ready, everyone = 1;
        Label waitLine;

        public static CoopWorld Current { get; private set; }

        /// <summary>Who's in each truck seat.</summary>
        public TruckSeats Seats => seats.Value;

        public override void OnNetworkSpawn()
        {
            Current = this;
            clock = FindAnyObjectByType<TimeOfDay>();
            weather = FindAnyObjectByType<WeatherSystem>();
            truck = Pickup.Current;
            player = FindAnyObjectByType<FirstPersonController>();
            // Every game has the same scene, so the doors sort into the same order by where they are in it.
            doors = FindObjectsByType<Door>(FindObjectsSortMode.None);
            Array.Sort(doors, (a, b) => string.CompareOrdinal(PathOf(a.transform), PathOf(b.transform)));
            Door.Toggled += OnDoorToggled;
            Pickup.LocalSeatChanged += OnLocalSeatChanged;
            if (truck != null)
                truck.SeatTakenByOther = seat => seats.Value[seat] != TruckSeats.Empty && seats.Value[seat] != NetworkManager.LocalClientId;

            if (IsServer)
            {
                NetworkManager.OnClientDisconnectCallback += OnClientLeft;
                bedJson = BedJson();
                synced = true;
            }
            else
                // Ask for everything as it is now: weather, doors, the truck bed, and where to start.
                RequestSnapshotRpc();

            waitLine = UIBuild.Text("", "coop-wait", "shadowed");
            waitLine.SetVisible(false);
            GameUI.Current.Hud.Add(waitLine.IgnoreMouse());
        }

        public override void OnNetworkDespawn()
        {
            if (Current == this)
                Current = null;
            Door.Toggled -= OnDoorToggled;
            Pickup.LocalSeatChanged -= OnLocalSeatChanged;
            if (NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= OnClientLeft;
            if (clock != null)
                clock.SharedMultiplier = null;
            if (truck != null)
            {
                truck.SeatTakenByOther = null;
                truck.Simulated = true;
                truck.SetEngine(truck.Driving);
            }
            waitLine?.RemoveFromHierarchy();
        }

        static string PathOf(Transform transform) =>
            transform.parent == null ? transform.name : PathOf(transform.parent) + "/" + transform.name + "#" + transform.GetSiblingIndex();

        void Update()
        {
            if (!IsSpawned)
                return;
            UpdateClock();
            UpdateTruck();
            UpdateBed();
            if (IsServer && Time.unscaledTime >= nextWeather && weather != null)
            {
                nextWeather = Time.unscaledTime + weatherEvery;
                WeatherRpc(JsonUtility.ToJson(weather.CaptureState()));
            }
        }

        // ---------- The clock ----------

        void UpdateClock()
        {
            if (clock == null)
                return;
            // Tell the host how fast this player wants the clock (when it changes, and now and then in case it was lost).
            float mine = clock.RequestedMultiplier;
            if (!Mathf.Approximately(mine, lastWanted) || Time.unscaledTime >= nextWantedResend)
            {
                lastWanted = mine;
                nextWantedResend = Time.unscaledTime + 2f;
                if (IsServer)
                    wanted[NetworkManager.LocalClientId] = mine;
                else
                    WantSpeedRpc(mine);
            }

            if (IsServer && Time.unscaledTime >= nextClock)
            {
                nextClock = Time.unscaledTime + 0.25f;
                // The slowest anyone wants: time only skips when everyone is sleeping or fast-forwarding.
                float agreed = float.MaxValue;
                int asking = 0, total = 0;
                foreach (ulong id in NetworkManager.ConnectedClientsIds)
                {
                    float speed = wanted.TryGetValue(id, out float value) ? value : 1f;
                    agreed = Mathf.Min(agreed, speed);
                    if (speed > 1.01f)
                        asking++;
                    total++;
                }
                if (agreed == float.MaxValue)
                    agreed = 1f;
                clock.SharedMultiplier = agreed;
                ready = asking;
                everyone = total;
                ClockRpc(clock.Day, clock.Hour, agreed, (byte)asking, (byte)total);
            }

            // Waiting for the others before time can skip.
            bool waiting = everyone > 1 && clock.RequestedMultiplier > 1.01f && clock.TimeMultiplier < clock.RequestedMultiplier - 0.01f;
            waitLine.SetVisible(waiting);
            if (waiting)
                waitLine.text = $"Time only speeds up when everyone is ready: {ready} of {everyone} hikers are sleeping or fast-forwarding.";
        }

        [Rpc(SendTo.Server)]
        void WantSpeedRpc(float multiplier, RpcParams rpcParams = default) => wanted[rpcParams.Receive.SenderClientId] = multiplier;

        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
        void ClockRpc(int day, float hour, float agreed, byte asking, byte total)
        {
            if (clock == null)
                return;
            clock.SharedMultiplier = agreed;
            ready = asking;
            everyone = total;
            float theirs = (day - 1) * 24f + hour;
            if (Mathf.Abs(theirs - clock.TotalHours) * 60f > clockTolerance)
                clock.SetDayAndTime(day, hour);
        }

        // ---------- Weather ----------

        [Rpc(SendTo.NotServer)]
        void WeatherRpc(string json) => ApplyWeather(json);

        void ApplyWeather(string json)
        {
            if (weather == null || string.IsNullOrEmpty(json))
                return;
            WeatherState state = JsonUtility.FromJson<WeatherState>(json);
            if (state != null)
                weather.RestoreState(state);
        }

        // ---------- Joining: everything as it is now ----------

        [Rpc(SendTo.Server)]
        void RequestSnapshotRpc(RpcParams rpcParams = default)
        {
            ulong guest = rpcParams.Receive.SenderClientId;
            var open = new StringBuilder(doors.Length);
            foreach (Door door in doors)
                open.Append(door != null && door.IsOpen ? '1' : '0');
            string weatherJson = weather != null ? JsonUtility.ToJson(weather.CaptureState()) : "";
            StartSpot(out Vector3 spot, out float facing);
            SnapshotRpc(clock != null ? clock.Day : 1, clock != null ? clock.Hour : 8f, weatherJson, open.ToString(), BedJson(), spot, facing,
                RpcTarget.Single(guest, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void SnapshotRpc(int day, float hour, string weatherJson, string openDoors, string bed, Vector3 spot, float facing, RpcParams rpcParams)
        {
            if (clock != null)
                clock.SetDayAndTime(day, hour);
            ApplyWeather(weatherJson);
            for (int i = 0; i < doors.Length && i < openDoors.Length; i++)
                if (doors[i] != null)
                    doors[i].SetOpen(openDoors[i] == '1');
            ApplyBed(bed);
            synced = true;
            // Start beside the host.
            if (player != null && !player.Mounted)
            {
                var controller = player.GetComponent<CharacterController>();
                if (controller != null)
                    controller.enabled = false;
                player.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, facing, 0f));
                if (controller != null)
                    controller.enabled = true;
            }
        }

        /// <summary>
        /// Somewhere clear beside the host to start a guest: around them (indoors too, under the ceiling), or by the
        /// truck if they're in it.
        /// </summary>
        void StartSpot(out Vector3 spot, out float facing)
        {
            spot = player != null ? player.transform.position : Vector3.zero;
            facing = player != null ? player.transform.eulerAngles.y : 0f;
            if (player == null)
                return;
            Vector3 centre = player.Mounted && truck != null && truck.Aboard ? truck.transform.position - truck.transform.right * 1.6f : spot;
            Quaternion turn = Quaternion.Euler(0f, facing, 0f);
            foreach (float distance in new[] { 1.1f, 1.8f, 2.6f })
                for (int i = 0; i < 8; i++)
                {
                    // Beside first, then behind, then ahead.
                    float angle = new[] { -90f, 90f, 180f, -135f, 135f, -45f, 45f, 0f }[i];
                    Vector3 at = centre + turn * (Quaternion.Euler(0f, angle, 0f) * Vector3.forward) * distance;
                    if (!Physics.Raycast(at + Vector3.up * 1.2f, Vector3.down, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore))
                        continue;
                    Vector3 feet = hit.point + Vector3.up * 0.05f;
                    // On the same floor, not on top of the furniture.
                    if (Mathf.Abs(feet.y - centre.y) > 0.3f)
                        continue;
                    if (Physics.CheckCapsule(feet + Vector3.up * 0.4f, feet + Vector3.up * 1.5f, 0.32f, ~0, QueryTriggerInteraction.Ignore))
                        continue;
                    spot = feet;
                    return;
                }
        }

        // ---------- Doors ----------

        void OnDoorToggled(Door door)
        {
            int index = Array.IndexOf(doors, door);
            if (index >= 0)
                DoorRpc(index, door.IsOpen);
        }

        [Rpc(SendTo.NotMe)]
        void DoorRpc(int index, bool open)
        {
            if (index >= 0 && index < doors.Length && doors[index] != null)
                doors[index].SetOpen(open);
        }

        // ---------- The truck ----------

        void OnLocalSeatChanged(Pickup changed) => SeatRpc((sbyte)changed.LocalSeat);

        [Rpc(SendTo.Server)]
        void SeatRpc(sbyte seat, RpcParams rpcParams = default)
        {
            ulong who = rpcParams.Receive.SenderClientId;
            TruckSeats next = seats.Value;
            for (int i = 0; i < 4; i++)
                if (next[i] == who)
                    next[i] = TruckSeats.Empty;
            if (seat >= 0 && seat < 4)
            {
                if (next[seat] != TruckSeats.Empty)
                {
                    // Someone got there first.
                    SeatTakenRpc(RpcTarget.Single(who, RpcTargetUse.Temp));
                    seats.Value = next;
                    return;
                }
                next[seat] = who;
            }
            seats.Value = next;
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void SeatTakenRpc(RpcParams rpcParams)
        {
            Notifications.Post("Someone else got in that seat first.", 3f);
            if (truck != null)
                truck.LeaveSeat();
        }

        void OnClientLeft(ulong clientId)
        {
            wanted.Remove(clientId);
            TruckSeats next = seats.Value;
            for (int i = 0; i < 4; i++)
                if (next[i] == clientId)
                    next[i] = TruckSeats.Empty;
            seats.Value = next;
        }

        void UpdateTruck()
        {
            if (truck == null)
                return;
            // The driver's game runs the truck; parked, the host's does.
            ulong driver = seats.Value.driver;
            ulong runner = driver != TruckSeats.Empty ? driver : NetworkManager.ServerClientId;
            bool mine = runner == NetworkManager.LocalClientId;
            truck.Simulated = mine;
            if (!mine)
            {
                truck.SetEngine(driver != TruckSeats.Empty);
                return;
            }
            bool moving = truck.Velocity.sqrMagnitude > 0.01f || driver != TruckSeats.Empty;
            if (Time.unscaledTime < nextTruck)
                return;
            nextTruck = Time.unscaledTime + (moving ? 0.05f : 0.5f);
            TruckRpc(truck.transform.position, truck.transform.rotation, truck.Velocity, truck.SteerAngle, driver != TruckSeats.Empty);
        }

        [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
        void TruckRpc(Vector3 position, Quaternion rotation, Vector3 velocity, float steer, bool engineOn)
        {
            if (truck == null || truck.Simulated)
                return;
            truck.FollowPose(position, rotation, velocity, steer);
            truck.SetEngine(engineOn);
        }

        // ---------- The truck bed ----------

        string BedJson() => truck != null && truck.Bed != null ? JsonUtility.ToJson(truck.Bed.CaptureState()) : "";

        void UpdateBed()
        {
            // Until this game has the shared bed, its own (empty) one mustn't overwrite it.
            if (!synced || truck == null || truck.Bed == null || Time.unscaledTime < nextBedCheck)
                return;
            nextBedCheck = Time.unscaledTime + 0.4f;
            string json = BedJson();
            if (json == bedJson)
                return;
            bedJson = json;
            BedRpc(json);
        }

        [Rpc(SendTo.NotMe)]
        void BedRpc(string json) => ApplyBed(json);

        void ApplyBed(string json)
        {
            if (truck == null || truck.Bed == null || string.IsNullOrEmpty(json))
                return;
            BackpackState state = JsonUtility.FromJson<BackpackState>(json);
            if (state == null)
                return;
            truck.Bed.RestoreState(state);
            bedJson = BedJson();
        }
    }
}
