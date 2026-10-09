using System;
using System.Collections.Generic;
using System.IO;
using Backpacking.Camp;
using Backpacking.Character;
using Backpacking.Gathering;
using Backpacking.Interaction;
using Backpacking.Navigation;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.Trade;
using Backpacking.Trip;
using Backpacking.UI;
using Backpacking.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Backpacking.Saving
{
    /// <summary>
    /// Saves and loads the trip to a single JSON file. Saves automatically after sleeping in the tent,
    /// and when resting at a trading post. F5 / F9 quick-save and quick-load for testing.
    /// The title menu offers to continue the saved trip.
    /// </summary>
    // Start after everything else, so the scene has finished setting itself up before a save is applied.
    [DefaultExecutionOrder(1000)]
    public class SaveSystem : MonoBehaviour
    {
        [SerializeField] TimeOfDay timeOfDay;
        [SerializeField] FirstPersonController player;
        [SerializeField] Backpack backpack;
        [SerializeField] Vitals vitals;
        [SerializeField] CampPlacer placer;
        [SerializeField] PlayerActivity activity;
        [SerializeField] WeatherSystem weather;
        [SerializeField] TripLog trip;
        [SerializeField] GroundClearing clearing;
        [SerializeField] PlayerAvatar avatar;
        [SerializeField] UI.Tutorial tutorial;
        [SerializeField] PackHandling packHandling;
        [SerializeField] Vehicles.Pickup pickup;
        [SerializeField] ArrivalGuide arrival;
        [SerializeField] string fileName = "trip.json";
        [Tooltip("How close to a route stop counts as being 'near' it in the save summary, in metres.")]
        [SerializeField] float nearbyDistance = 400f;

        // Set before reloading the scene for a quick-load, so the fresh scene applies the save straight away.
        static bool loadOnSceneStart;

        readonly Dictionary<string, GameObject> pickupsAtStart = new();

        /// <summary>Co-op friends' hikers on this trip, by their key: kept in the save so they come back as they were.</summary>
        public Dictionary<string, HikerSave> Guests { get; } = new();

        /// <summary>The scene's pickups (firewood, the things to take from home) by their save id (null once collected).</summary>
        public IReadOnlyDictionary<string, GameObject> Pickups => pickupsAtStart;

        string SavePath => Path.Combine(Application.persistentDataPath, fileName);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => loadOnSceneStart = false;

        void Awake()
        {
            // Remember every pickup in the scene, so a save can list the ones that have been collected.
            foreach (FirewoodPickup pickup in FindObjectsByType<FirewoodPickup>())
                if (pickup.TryGetComponent(out SaveId saveId))
                    pickupsAtStart[saveId.Id] = pickup.gameObject;
            // And the things to take from home: the daypack, the water in the fridge, the trail mix in the cupboard.
            foreach (ItemPickup pickup in FindObjectsByType<ItemPickup>())
                if (pickup.TryGetComponent(out SaveId saveId))
                    pickupsAtStart[saveId.Id] = pickup.gameObject;
        }

        void OnEnable() => activity.WokeUp += OnWokeUp;
        void OnDisable() => activity.WokeUp -= OnWokeUp;

        void OnWokeUp(bool inTent)
        {
            if (inTent && !Net.CoopSession.IsGuest)
                Save();
        }

        /// <summary>True when the scene was reloaded to apply a save straight away, so the title menu is skipped.</summary>
        public static bool LoadingOnSceneStart => loadOnSceneStart;

        void Start()
        {
            if (!loadOnSceneStart)
                return;
            loadOnSceneStart = false;
            SaveData save = ReadSave();
            if (save != null)
                Apply(save);
        }

        void Update()
        {
            // Not from inside menus.
            if (PlayerControlLock.CursorNeeded)
                return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard.f5Key.wasPressedThisFrame)
                Save();
            else if (keyboard.f9Key.wasPressedThisFrame)
                QuickLoad();
        }

        public bool HasSave => File.Exists(SavePath);

        public void Save()
        {
            if (Net.CoopSession.IsGuest)
            {
                Notifications.Post("On a friend's trip, the host saves the game. Your hiker is kept in their save, ready for when you rejoin.");
                return;
            }
            SaveData data = Capture();
            try
            {
                File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
                Notifications.Post($"Game saved: {data.summary}.");
            }
            catch (Exception exception)
            {
                Debug.LogError($"Couldn't write save file {SavePath}: {exception}");
                Notifications.Post("Couldn't save the game. See the console for details.");
            }
        }

        /// <summary>Reloads the scene fresh and applies the save to it.</summary>
        public void QuickLoad()
        {
            if (Net.CoopSession.Active)
            {
                Notifications.Post("Leave co-op (pause menu) before loading a save.");
                return;
            }
            if (!HasSave)
            {
                Notifications.Post("No saved game yet.");
                return;
            }
            loadOnSceneStart = true;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>The saved trip's summary ("Day 3, 14:20 near Valley Crossing"), or null if there's no save.</summary>
        public string SavedSummary() => ReadSave()?.summary;

        /// <summary>Applies the saved trip to this freshly loaded scene. Used by the title menu.</summary>
        public void ContinueSavedTrip()
        {
            SaveData save = ReadSave();
            if (save != null)
                Apply(save);
        }

        /// <summary>Reloads the scene to the title menu. Unsaved progress is lost.</summary>
        public void ReturnToTitle()
        {
            // Out of co-op first: the reload waits until the session has shut down.
            if (Net.CoopSession.Active)
            {
                Net.CoopSession.Instance.Leave(ReturnToTitle);
                return;
            }
            loadOnSceneStart = false;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        SaveData ReadSave()
        {
            if (!HasSave)
                return null;
            try
            {
                return JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            }
            catch (Exception exception)
            {
                Debug.LogError($"Couldn't read save file {SavePath}: {exception}");
                return null;
            }
        }

        // ---------- Capture ----------

        SaveData Capture()
        {
            var data = new SaveData
            {
                day = timeOfDay.Day,
                hour = timeOfDay.Hour,
                // Saved inside the tent (on waking, say), you come back standing at its door.
                playerPosition = Tent.Occupied != null ? Tent.Occupied.ExitPosition : player.transform.position,
                playerYaw = player.transform.eulerAngles.y,
                vitals = vitals.CaptureState(),
                backpack = backpack.CaptureState(),
                weather = weather.CaptureState(),
                trip = trip != null ? trip.CaptureState() : null,
                character = avatar != null ? avatar.Profile.Clone() : null,
                tutorialStep = tutorial != null ? tutorial.CurrentStep : -1,
                summary = $"Day {timeOfDay.Day}, {timeOfDay.ClockText} {DescribeLocation()}",
            };

            foreach (KeyValuePair<string, GameObject> pickup in pickupsAtStart)
                if (pickup.Value == null)
                    data.collectedPickups.Add(pickup.Key);

            foreach (SaveId saveId in FindObjectsByType<SaveId>())
            {
                if (saveId.TryGetComponent(out NavigationPoint point) && point.Visited)
                    data.visitedPoints.Add(saveId.Id);
                if (saveId.TryGetComponent(out BerryBush bush) && !float.IsInfinity(bush.PickedAtHour))
                    data.bushes.Add(new BushState { id = saveId.Id, pickedAtHour = bush.PickedAtHour });
                if (saveId.TryGetComponent(out Vendor vendor))
                    data.vendors.Add(new VendorState { id = saveId.Id, stock = vendor.CaptureStock() });
            }

            if (clearing != null)
                data.clearings.AddRange(clearing.Cleared);
            if (packHandling != null)
                data.pack = packHandling.CaptureState();
            if (pickup != null)
            {
                data.truck = pickup.CaptureState();
                if (pickup.Bed != null)
                    data.truckBed = pickup.Bed.CaptureState();
            }
            if (arrival != null)
            {
                data.arrivalPhase = (int)arrival.Phase;
                data.tutorialPending = arrival.TutorialPending;
            }

            foreach ((CampItem kind, GameObject instance) in placer.PlacedItems)
                data.placedItems.Add(CapturePlaced(kind, instance));
            data.guests.AddRange(Guests.Values);
            return data;
        }

        /// <summary>A tent, fire, stove, snare or chair as it is now, and whose it is.</summary>
        public static PlacedItemState CapturePlaced(CampItem kind, GameObject instance)
        {
            var state = new PlacedItemState
            {
                kind = kind,
                position = instance.transform.position,
                rotation = instance.transform.rotation,
            };
            if (instance.TryGetComponent(out Campfire fire))
            {
                state.fuelHours = fire.FuelHours;
                state.burning = fire.IsBurning;
            }
            if (instance.TryGetComponent(out Snare snare))
                state.hasCatch = snare.HasCatch;
            if (instance.TryGetComponent(out Tent tent))
            {
                state.stage = tent.Stage;
                state.matLaidOut = tent.MatLaidOut;
                state.bagLaidOut = tent.BagLaidOut;
                state.beds = tent.CaptureBeds();
                state.tentModel = (int)tent.Model;
            }
            if (instance.TryGetComponent(out CampChair chair))
                state.chairStage = chair.Stage;
            state.owner = CampOwner.KeyOf(instance);
            state.ownerName = state.owner.Length > 0 && instance.TryGetComponent(out CampOwner owner) ? owner.ownerName : "";
            return state;
        }

        /// <summary>
        /// Puts gear into a saved (or a friend's) state: freshly spawned, or already standing and changed by someone
        /// else. <paramref name="backpack"/> is this player's, for your own tent's model and bedding.
        /// </summary>
        public static void ApplyPlaced(GameObject instance, PlacedItemState item, Backpack backpack, bool fresh)
        {
            if (!string.IsNullOrEmpty(item.owner))
                CampOwner.Set(instance, item.owner, item.ownerName);
            bool mine = CampOwner.KeyOf(instance).Length == 0;
            if (!fresh && ((instance.transform.position - item.position).sqrMagnitude > 0.0004f || Quaternion.Angle(instance.transform.rotation, item.rotation) > 0.5f))
                instance.transform.SetPositionAndRotation(item.position, item.rotation);
            if (instance.TryGetComponent(out Campfire fire) && (fresh || fire.IsBurning != item.burning || Mathf.Abs(fire.FuelHours - item.fuelHours) > 0.05f))
                fire.Restore(item.fuelHours, item.burning);
            if (instance.TryGetComponent(out Snare snare) && snare.HasCatch != item.hasCatch)
                snare.HasCatch = item.hasCatch;
            if (instance.TryGetComponent(out Tent tent))
            {
                // Older saves didn't say which tent: it's the one in your pack.
                TentModel model = item.tentModel >= 0 ? (TentModel)item.tentModel : backpack.TentModel;
                if (fresh || tent.Model != model || tent.Stage != item.stage)
                    tent.Setup(model, item.stage);
                List<TentBed> beds = item.beds;
                // Older saves only had your own bedding, in your own tent.
                if ((beds == null || beds.Count == 0) && mine && (item.matLaidOut || item.bagLaidOut))
                    beds = new List<TentBed> { new() { key = CampOwner.LocalKey, mat = item.matLaidOut, bag = item.bagLaidOut } };
                if (fresh || Tent.Signature(beds) != Tent.Signature(tent.CaptureBeds()))
                    tent.SetBeds(beds, backpack);
            }
            if (instance.TryGetComponent(out CampChair chair) && (fresh || chair.Stage != item.chairStage))
                chair.Setup(item.chairStage);
        }

        // ---------- One hiker (a co-op guest's) ----------

        /// <summary>This player's hiker on their own, for the host to keep while they're a guest.</summary>
        public HikerSave CaptureHiker(string key)
        {
            var hiker = new HikerSave
            {
                key = key,
                position = Tent.Occupied != null ? Tent.Occupied.ExitPosition : player.transform.position,
                yaw = player.transform.eulerAngles.y,
                vitals = vitals.CaptureState(),
                backpack = backpack.CaptureState(),
                trip = trip != null ? trip.CaptureState() : null,
                character = avatar != null ? avatar.Profile.Clone() : null,
                pack = packHandling != null ? packHandling.CaptureState() : null,
                arrivalPhase = arrival != null ? (int)arrival.Phase : (int)ArrivalPhase.OnTheTrail,
            };
            foreach (NavigationPoint point in NavigationPoint.All)
                if (point.Visited && point.TryGetComponent(out SaveId saveId))
                    hiker.visitedPoints.Add(saveId.Id);
            return hiker;
        }

        /// <summary>Back as they were: a guest rejoining a trip. Placed at <paramref name="at"/> instead of where they left, if given.</summary>
        public void ApplyHiker(HikerSave hiker, Vector3? at = null, float atYaw = 0f)
        {
            if (hiker.vitals != null)
                vitals.RestoreState(hiker.vitals);
            if (hiker.backpack != null)
                backpack.RestoreState(hiker.backpack);
            if (trip != null && hiker.trip != null)
                trip.RestoreState(hiker.trip);
            if (avatar != null && hiker.character != null && !string.IsNullOrEmpty(hiker.character.name))
                avatar.Apply(hiker.character);
            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.SetPositionAndRotation(at ?? hiker.position, Quaternion.Euler(0f, at.HasValue ? atYaw : hiker.yaw, 0f));
            controller.enabled = true;
            if (packHandling != null)
                packHandling.RestoreState(hiker.pack);
            var visited = new HashSet<string>(hiker.visitedPoints ?? new List<string>());
            foreach (NavigationPoint point in NavigationPoint.All)
                if (point.TryGetComponent(out SaveId saveId) && visited.Contains(saveId.Id))
                    point.SetVisited(true);
            if (arrival != null)
                arrival.Restore(hiker.arrivalPhase, false);
        }

        string DescribeLocation()
        {
            NavigationPoint nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (NavigationPoint point in NavigationPoint.All)
            {
                Vector3 offset = point.transform.position - player.transform.position;
                offset.y = 0f;
                if (offset.magnitude < nearestDistance)
                {
                    nearestDistance = offset.magnitude;
                    nearest = point;
                }
            }
            return nearest != null && nearestDistance < nearbyDistance ? $"near {nearest.DisplayName}" : "in the backcountry";
        }

        // ---------- Apply ----------

        void Apply(SaveData data)
        {
            timeOfDay.SetDayAndTime(data.day, data.hour);
            vitals.RestoreState(data.vitals);
            backpack.RestoreState(data.backpack);
            weather.RestoreState(data.weather);

            // A CharacterController overrides direct moves, so switch it off while teleporting.
            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.SetPositionAndRotation(data.playerPosition, Quaternion.Euler(0f, data.playerYaw, 0f));
            controller.enabled = true;
            if (trip != null)
                trip.RestoreState(data.trip);
            // Older saves have no hiker; keep whoever is shown.
            if (avatar != null && data.character != null && !string.IsNullOrEmpty(data.character.name))
                avatar.Apply(data.character);
            if (tutorial != null)
                tutorial.Resume(data.tutorialStep);

            foreach (string id in data.collectedPickups)
                if (pickupsAtStart.TryGetValue(id, out GameObject pickup) && pickup != null)
                    Destroy(pickup);

            var byId = new Dictionary<string, SaveId>();
            foreach (SaveId saveId in FindObjectsByType<SaveId>())
                byId[saveId.Id] = saveId;

            foreach (string id in data.visitedPoints)
                if (byId.TryGetValue(id, out SaveId saveId) && saveId.TryGetComponent(out NavigationPoint point))
                    point.SetVisited(true);
            foreach (BushState bush in data.bushes)
                if (byId.TryGetValue(bush.id, out SaveId saveId) && saveId.TryGetComponent(out BerryBush berryBush))
                    berryBush.PickedAtHour = bush.pickedAtHour;
            foreach (VendorState vendorState in data.vendors)
                if (byId.TryGetValue(vendorState.id, out SaveId saveId) && saveId.TryGetComponent(out Vendor vendor))
                    vendor.RestoreStock(vendorState.stock);

            // Clear campsites before putting tents back on them.
            if (clearing != null && data.clearings != null)
                foreach (Vector4 spot in data.clearings)
                    clearing.Restore(spot);

            foreach (PlacedItemState item in data.placedItems)
                ApplyPlaced(placer.Spawn(item.kind, item.position, item.rotation), item, backpack, fresh: true);
            Guests.Clear();
            if (data.guests != null)
                foreach (HikerSave guest in data.guests)
                    if (guest != null && !string.IsNullOrEmpty(guest.key))
                        Guests[guest.key] = guest;
            // Older saves have no pack state: it's on your back.
            if (packHandling != null)
                packHandling.RestoreState(data.pack);
            // Older saves have no truck: it stays parked at home, empty.
            if (pickup != null)
            {
                if (pickup.Bed != null && data.truckBed != null)
                    pickup.Bed.RestoreState(data.truckBed);
                if (data.truck != null)
                    pickup.RestoreState(data.truck);
            }
            if (arrival != null)
                arrival.Restore(data.arrivalPhase, data.tutorialPending);

            Notifications.Post($"Welcome back. {data.summary}.");
        }
    }
}
