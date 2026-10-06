using System;
using System.Collections.Generic;
using System.IO;
using Backpacking.Camp;
using Backpacking.Gathering;
using Backpacking.Interaction;
using Backpacking.Navigation;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.Trade;
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
    /// On Play, offers to continue the saved trip.
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
        [SerializeField] string fileName = "trip.json";
        [Tooltip("How close to a route stop counts as being 'near' it in the save summary, in metres.")]
        [SerializeField] float nearbyDistance = 400f;

        // Set before reloading the scene for a quick-load, so the fresh scene applies the save straight away.
        static bool loadOnSceneStart;

        readonly Dictionary<string, GameObject> pickupsAtStart = new();
        SaveData offeredSave;
        GUIStyle titleStyle, textStyle;

        string SavePath => Path.Combine(Application.persistentDataPath, fileName);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => loadOnSceneStart = false;

        void Awake()
        {
            // Remember every pickup in the scene, so a save can list the ones that have been collected.
            foreach (FirewoodPickup pickup in FindObjectsByType<FirewoodPickup>())
                if (pickup.TryGetComponent(out SaveId saveId))
                    pickupsAtStart[saveId.Id] = pickup.gameObject;
        }

        void OnEnable() => activity.WokeUp += OnWokeUp;
        void OnDisable() => activity.WokeUp -= OnWokeUp;

        void OnWokeUp(bool inTent)
        {
            if (inTent)
                Save();
        }

        void Start()
        {
            SaveData save = ReadSave();
            if (save == null)
                return;

            if (loadOnSceneStart)
            {
                loadOnSceneStart = false;
                Apply(save);
                return;
            }

            // Pause and ask before anything happens.
            offeredSave = save;
            Time.timeScale = 0f;
            PlayerControlLock.Lock(this, needsCursor: true);
        }

        void Update()
        {
            if (offeredSave != null)
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
            if (!HasSave)
            {
                Notifications.Post("No saved game yet.");
                return;
            }
            loadOnSceneStart = true;
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
                playerPosition = player.transform.position,
                playerYaw = player.transform.eulerAngles.y,
                vitals = vitals.CaptureState(),
                backpack = backpack.CaptureState(),
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

            foreach ((CampItem kind, GameObject instance) in placer.PlacedItems)
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
                data.placedItems.Add(state);
            }
            return data;
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

            // A CharacterController overrides direct moves, so switch it off while teleporting.
            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.SetPositionAndRotation(data.playerPosition, Quaternion.Euler(0f, data.playerYaw, 0f));
            controller.enabled = true;

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

            foreach (PlacedItemState item in data.placedItems)
            {
                GameObject instance = placer.Spawn(item.kind, item.position, item.rotation);
                if (instance.TryGetComponent(out Campfire fire))
                    fire.Restore(item.fuelHours, item.burning);
                if (instance.TryGetComponent(out Snare snare))
                    snare.HasCatch = item.hasCatch;
            }

            Notifications.Post($"Welcome back. {data.summary}.");
        }

        // ---------- Continue prompt ----------

        void ResolveOffer(bool continueTrip)
        {
            SaveData save = offeredSave;
            offeredSave = null;
            Time.timeScale = 1f;
            PlayerControlLock.Unlock(this);
            if (continueTrip)
                Apply(save);
        }

        void OnGUI()
        {
            if (offeredSave == null)
                return;

            titleStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            textStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, wordWrap = true };

            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            const float width = 460f, height = 230f;
            var area = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
            GUI.Box(area, GUIContent.none);
            GUI.Box(area, GUIContent.none);
            GUI.Label(new Rect(area.x, area.y + 18f, width, 34f), "Continue your trip?", titleStyle);
            GUI.Label(new Rect(area.x + 20f, area.y + 62f, width - 40f, 50f), $"Saved: {offeredSave.summary}", textStyle);

            if (GUI.Button(new Rect(area.x + 30f, area.y + 126f, width - 60f, 36f), "Continue saved trip"))
                ResolveOffer(continueTrip: true);
            else if (GUI.Button(new Rect(area.x + 30f, area.y + 172f, width - 60f, 36f), "Start a new trip (your next save replaces the old one)"))
                ResolveOffer(continueTrip: false);
        }
    }
}
