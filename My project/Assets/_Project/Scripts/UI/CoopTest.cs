#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Backpacking.Camp;
using Backpacking.Character;
using Backpacking.Interaction;
using Backpacking.Net;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.Vehicles;
using Backpacking.Wildlife;
using Backpacking.World;
using UnityEngine;

namespace Backpacking.UI
{
    /// <summary>
    /// An automatic check of co-op between two games on one PC, by address. The host is the editor in Play mode
    /// (started by Temp/backpacking-cooptest-request, see the editor's CoopTestRun); the guest is a development
    /// build started with -coop-test-guest. Each side checks what it can see of the other: the guest is placed
    /// beside the host and each sees the other's hiker; a door opened by the host opens for the guest; the guest
    /// rides as the host's passenger and is carried along; the shared truck bed; the clock speeding up only when
    /// both ask; a fire the host lit before the guest joined; the guest's stove, theirs alone to pack away; the host's
    /// rabbits and deer shown in the guest's game, one shot and taken there; the late joiner's starter kit; and the
    /// guest's hiker kept by the host when they leave. They take turns by watching each other's moves (the door, the seats, the clock). Results go to
    /// Logs/cooptest-host.log and cooptest-guest.log (next to the build for the guest); FAIL lines are problems.
    /// </summary>
    public class CoopTest : MonoBehaviour
    {
        public const string RequestPath = "Temp/backpacking-cooptest-request";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        readonly StringBuilder log = new();
        bool guest;
        int failures, errors;
        string logPath;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void MaybeStart()
        {
            bool asGuest = Environment.GetCommandLineArgs().Contains("-coop-test-guest");
#if UNITY_EDITOR
            bool asHost = File.Exists(RequestPath);
#else
            bool asHost = false;
#endif
            if (!asGuest && !asHost)
                return;
            var test = new GameObject("Co-op Test").AddComponent<CoopTest>();
            test.guest = asGuest;
        }

        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;

        void OnLog(string message, string stack, LogType type)
        {
            if (type is LogType.Exception or LogType.Error && !message.StartsWith("[CoopTest]"))
            {
                errors++;
                log.AppendLine($"ERROR {message}\n{stack}");
            }
        }

        void Check(bool ok, string what)
        {
            log.AppendLine((ok ? "ok    " : "FAIL  ") + what);
            if (!ok)
                failures++;
            Flush();
        }

        void Note(string what)
        {
            log.AppendLine("      " + what);
            Flush();
        }

        void Flush()
        {
            try
            {
                File.WriteAllText(logPath, log.ToString());
            }
            catch (IOException)
            {
            }
        }

        IEnumerator Start()
        {
#if UNITY_EDITOR
            if (!guest)
                File.Delete(RequestPath);
            logPath = guest ? "Logs/cooptest-guest.log" : "Logs/cooptest-host.log";
#else
            logPath = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "cooptest-guest.log");
#endif
            Application.runInBackground = true;
            log.AppendLine($"=== Co-op test, {(guest ? "guest" : "host")}, {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Flush();
            yield return new WaitForSecondsRealtime(1f);
            IEnumerator steps = guest ? GuestSteps() : HostSteps();
            while (true)
            {
                object current;
                try
                {
                    if (!steps.MoveNext())
                        break;
                    current = steps.Current;
                }
                catch (Exception exception)
                {
                    Check(false, $"the test itself threw: {exception}");
                    break;
                }
                yield return current;
            }
            Finish();
        }

        // ---------- Shared helpers ----------

        void BeginTrip(string hikerName, bool asGuest)
        {
            var menus = FindAnyObjectByType<GameMenus>();
            var profile = new CharacterProfile { name = hikerName, hiker = asGuest ? "Female_Adult_04" : "Male_Adult_04", background = Background.WeekendHiker };
            typeof(GameMenus).GetMethod("BeginTrip", Private, null, new[] { typeof(CharacterProfile), typeof(bool), typeof(bool) }, null)
                .Invoke(menus, new object[] { profile, true, asGuest });
        }

        /// <summary>Waits (real time) until something is true, or the time runs out; then says which.</summary>
        IEnumerator Until(Func<bool> condition, float seconds, Action<bool> result)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end)
                yield return null;
            result(condition());
        }

        /// <summary>A picture from a spare camera at <paramref name="from"/> looking at <paramref name="at"/>, saved beside the log.</summary>
        void Snap(string name, Vector3 from, Vector3 at, float near = 0.05f, Vector3? up = null)
        {
            var go = new GameObject("Test Camera");
            var camera = go.AddComponent<Camera>();
            camera.fieldOfView = 55f;
            camera.nearClipPlane = near;
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, up ?? Vector3.up));
            var target = new RenderTexture(960, 540, 24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var picture = new Texture2D(960, 540, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            RenderTexture.active = null;
            string path = Path.Combine(Path.GetDirectoryName(logPath) ?? ".", $"coop-{name}.png");
            File.WriteAllBytes(path, picture.EncodeToPNG());
            camera.targetTexture = null;
            Destroy(target);
            Destroy(picture);
            Destroy(go);
            Note($"picture: {path}");
        }

        /// <summary>From in front of a hiker's body, at eye height.</summary>
        void SnapHiker(string name, Transform body, float distance = 2.6f)
        {
            Vector3 chest = body.position + Vector3.up * 1.2f;
            Vector3 front = body.forward;
            front.y = 0f;
            Snap(name, chest + front.normalized * distance + Vector3.up * 0.3f + body.right * 0.8f, chest);
        }

        static CoopHiker Other() => CoopHiker.All.FirstOrDefault(hiker => hiker != null && !hiker.IsOwner);

        static Door FrontDoor() => FindObjectsByType<Door>(FindObjectsSortMode.None).FirstOrDefault(door => door.DisplayName == "Front door");

        // ---------- The host (the editor) ----------

        IEnumerator HostSteps()
        {
            var player = FindAnyObjectByType<FirstPersonController>();
            Pickup truck = Pickup.Current;
            var clock = FindAnyObjectByType<TimeOfDay>();
            BeginTrip("Hosty", asGuest: false);
            yield return new WaitForSecondsRealtime(0.5f);
            // Already on the trail, so the guest arrives as a late joiner; and a fire is burning before they come.
            FindAnyObjectByType<Trip.ArrivalGuide>().Skip();
            Vector3 firePlace = truck.transform.position - truck.transform.right * 6f;
            firePlace.y = GroundCover.HeightAt(firePlace);
            var placer = player.GetComponent<CampPlacer>();
            GameObject fire = placer.Spawn(CampItem.FireRing, firePlace, Quaternion.identity);
            fire.GetComponent<Campfire>().Build(3);
            fire.GetComponent<Campfire>().Restore(1.5f, true);
            Note($"lit a fire at {firePlace:F1}; {Animal.All.Count} animals about");
            Check(CoopSession.Instance.HostDirect(), "hosting by address");
            Note("waiting up to 6 minutes for the guest build to join");
            bool ok = false;
            yield return Until(() => CoopSession.PlayerCount >= 2 && Other() != null && Other().Body != null, 360f, r => ok = r);
            Check(ok, $"the guest joined and their hiker shows up ({CoopSession.PlayerCount} hikers)");
            if (!ok)
                yield break;
            CoopHiker other = Other();
            yield return Until(() => Vector3.Distance(other.Body.transform.position, player.transform.position) < 4f, 15f, r => ok = r);
            Check(ok, $"the guest started beside the host ({Vector3.Distance(other.Body.transform.position, player.transform.position):0.0} m away), "
                      + $"as {other.HikerName}");
            yield return new WaitForSecondsRealtime(1.5f);
            Transform home = GameObject.Find("Home")?.transform;
            if (home != null)
                Note($"in the house: host at {home.InverseTransformPoint(player.transform.position)}, guest at {home.InverseTransformPoint(other.Body.transform.position)}");
            SnapHiker("host-sees-guest", other.Body.transform);

            // The door: the guest is watching for it.
            yield return new WaitForSecondsRealtime(2f);
            Door door = FrontDoor();
            if (door != null && !door.IsOpen)
                door.Toggle();
            Note("opened the front door");

            // Into the truck, and wait for the guest to take the passenger seat.
            truck.GetIn(0);
            yield return Until(() => CoopWorld.Current != null && CoopWorld.Current.Seats.driver == 0, 10f, r => ok = r);
            Check(ok, "the host is given the driver's seat");
            yield return Until(() => CoopWorld.Current.Seats.frontPassenger != TruckSeats.Empty, 60f, r => ok = r);
            Check(ok, "the guest got in the front passenger seat");
            yield return Until(() => other.State.seat == 1 && other.Body.transform.parent == truck.SeatAt(1), 10f, r => ok = r);
            Check(ok, "the guest's hiker is shown sitting in that seat");
            yield return new WaitForSecondsRealtime(1f);
            Transform seat = truck.SeatAt(1);
            Bounds shown = other.Body.GetComponentsInChildren<Renderer>().Aggregate(new Bounds(other.Body.transform.position, Vector3.zero), (b, r) => { b.Encapsulate(r.bounds); return b; });
            Note($"passenger body: {other.Body.GetComponentsInChildren<Renderer>().Length} renderers, visible {other.Body.GetComponentsInChildren<Renderer>().Count(r => r.enabled && r.gameObject.activeInHierarchy)}, "
                 + $"bounds centre {truck.transform.InverseTransformPoint(shown.center)} size {shown.size} (truck space); seats 0 {truck.SeatAt(0).localPosition} 1 {truck.SeatAt(1).localPosition}");
            Snap("host-sees-passenger-top", truck.transform.position + truck.transform.up * 5f, truck.transform.position, 3.35f, truck.transform.forward);
            Snap("host-sees-passenger-inside", truck.SeatAt(0).position + Vector3.up * truck.Eye.y, seat.position + Vector3.up * 0.9f);
            Snap("host-sees-passenger", seat.position + truck.transform.right * 2.6f + Vector3.up * 1.3f + truck.transform.forward * 0.6f, seat.position + Vector3.up * 0.9f);
            Vector3 start = truck.transform.position;
            truck.ScriptedInput = new Vector2(0f, 0.7f);
            yield return new WaitForSecondsRealtime(8f);
            truck.ScriptedInput = new Vector2(0f, -1f);
            yield return Until(() => Mathf.Abs(truck.SpeedKmh) < 1f, 15f, r => ok = r);
            truck.ScriptedInput = null;
            float driven = Vector3.Distance(start, truck.transform.position);
            Check(driven > 15f, $"drove {driven:0} m with the guest aboard");
            // The guest gets out once stopped; then the host does.
            yield return Until(() => CoopWorld.Current.Seats.frontPassenger == TruckSeats.Empty, 40f, r => ok = r);
            Check(ok, "the guest got out");
            truck.LeaveSeat();
            yield return new WaitForSecondsRealtime(1f);

            // The shared truck bed: something goes in on the host's side.
            int before = truck.Bed.Contents().Count;
            truck.Bed.AddFood(FoodKind.Berries, 3);
            Note($"put 3 berries in the truck bed ({before} -> {truck.Bed.Contents().Count} kinds)");

            // The clock: the host asks to speed up, alone that does nothing.
            clock.RequestSpeed(this, 30f);
            yield return new WaitForSecondsRealtime(2f);
            Check(clock.TimeMultiplier <= 1.01f, $"fast-forwarding alone doesn't speed the clock ({clock.TimeMultiplier:0.#}x)");
            yield return Until(() => clock.TimeMultiplier > 25f, 60f, r => ok = r);
            Check(ok, $"with both asking, the clock speeds up ({clock.TimeMultiplier:0.#}x)");
            clock.ClearSpeed(this);

            // The guest's stove: here, and theirs.
            CampStove stove = null;
            yield return Until(() => (stove = FindObjectsByType<CampStove>(FindObjectsSortMode.None).FirstOrDefault(s => CampOwner.IsOthers(s.gameObject, out _))) != null,
                60f, r => ok = r);
            Check(ok, "the guest's stove shows up here");
            if (stove != null)
            {
                CampOwner.IsOthers(stove.gameObject, out string owner);
                var options = new System.Collections.Generic.List<InteractionOption>();
                stove.GetOptions(player.GetComponent<Interactor>(), options);
                InteractionOption packAway = options.FirstOrDefault(option => option.Label == "Pack the stove away");
                Check(owner == "Guesty" && packAway.Label != null && !packAway.Enabled, $"it's {owner}'s, and only they can pack it away");
            }
            yield return Until(() => stove == null, 60f, r => ok = r);
            Check(ok, "when the guest packs their stove away, it's gone here too");

            // The guest leaves when it's done.
            yield return Until(() => CoopSession.PlayerCount < 2, 90f, r => ok = r);
            Check(ok, "the guest left, and the host carries on");
            var saves = FindAnyObjectByType<Saving.SaveSystem>();
            yield return Until(() => saves.Guests.Values.Any(hiker => hiker.character != null && hiker.character.name == "Guesty"), 5f, r => ok = r);
            Saving.HikerSave kept = saves.Guests.Values.FirstOrDefault();
            Check(ok, $"the host kept the guest's hiker for when they rejoin ({saves.Guests.Count} kept"
                      + (kept != null ? $", ${kept.backpack?.money} and {kept.backpack?.food?.Count} kinds of food" : "") + ")");
            yield return new WaitForSecondsRealtime(1f);
            Check(Other() == null, "their hiker is gone");
            CoopSession.Instance.Leave();
            yield return new WaitForSecondsRealtime(1f);
            Check(!CoopSession.Active && truck.Simulated, "hosting stopped; the truck is this game's again");
        }

        // ---------- The guest (the build) ----------

        IEnumerator GuestSteps()
        {
            var player = FindAnyObjectByType<FirstPersonController>();
            Pickup truck = Pickup.Current;
            var clock = FindAnyObjectByType<TimeOfDay>();
            string address = "127.0.0.1";
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-coop-address");
            if (at >= 0 && at + 1 < args.Length)
                address = args[at + 1];

            BeginTrip("Guesty", asGuest: true);
            bool ok = false;
            // The host may still be starting: keep trying for a few minutes.
            float giveUp = Time.realtimeSinceStartup + 300f;
            while (!CoopSession.Active && Time.realtimeSinceStartup < giveUp)
            {
                CoopSession.Instance.JoinDirect(address);
                yield return Until(() => CoopSession.Active && CoopWorld.Current != null, 12f, r => ok = r);
                if (!ok)
                {
                    CoopSession.Instance.Leave();
                    yield return new WaitForSecondsRealtime(3f);
                }
            }
            Check(ok, $"joined the host at {address}");
            if (!ok)
                yield break;
            yield return Until(() => Other() != null && Other().Body != null, 20f, r => ok = r);
            Check(ok, "the host's hiker shows up");
            if (!ok)
                yield break;
            CoopHiker host = Other();
            yield return new WaitForSecondsRealtime(2f);
            float apart = Vector3.Distance(host.Body.transform.position, player.transform.position);
            Check(apart < 4f, $"started beside the host ({apart:0.0} m away), who is {host.HikerName}");
            SnapHiker("guest-sees-host", host.Body.transform);
            Check(truck != null && !truck.Simulated, "the parked truck follows the host's game");

            // Late to a trip already on the trail: kitted out from the starting money.
            var backpack = player.GetComponent<Backpack>();
            var arrival = FindAnyObjectByType<Trip.ArrivalGuide>();
            Check(backpack.HasPack && backpack.OwnsTent && backpack.Money < Trip.ArrivalGuide.StartingMoney && arrival.Phase == Trip.ArrivalPhase.OnTheTrail,
                $"a late joiner gets a starter kit: {(backpack.HasPack ? backpack.Pack.Name : "no pack")}, tent {backpack.OwnsTent}, ${backpack.Money} left, {arrival.Phase}");

            // The host's fire, lit before joining.
            Campfire fire = FindObjectsByType<Campfire>(FindObjectsSortMode.None).FirstOrDefault();
            Check(fire != null && fire.IsBurning && CampOwner.IsOthers(fire.gameObject, out _),
                $"the host's campfire is here, burning ({(fire != null ? $"{fire.FuelHours:0.0} h of wood" : "none")})");

            // Set up a stove of our own, for the host to see.
            var placer = player.GetComponent<CampPlacer>();
            Vector3 stovePlace = player.transform.position + player.transform.forward * 1.5f;
            stovePlace.y = GroundCover.HeightAt(stovePlace);
            GameObject stove = placer.Spawn(CampItem.Stove, stovePlace, Quaternion.identity);

            // The animals are the host's: copies here, none of this game's own.
            yield return Until(() => Animal.All.Any(animal => animal.Puppet) && !Animal.All.Any(animal => !animal.Puppet), 10f, r => ok = r);
            Check(ok, $"the rabbits and deer are the host's ({Animal.All.Count(animal => animal.Puppet)} copies, {Animal.All.Count(animal => !animal.Puppet)} of our own)");
            Animal rabbit = Animal.All.Where(animal => animal.Puppet && animal.Kind == AnimalKind.Rabbit && !animal.IsDead)
                .OrderBy(animal => Vector3.Distance(animal.transform.position, player.transform.position)).FirstOrDefault();
            if (rabbit == null)
                Note("no rabbits about to shoot");
            else
            {
                Vector3 centre = rabbit.GetComponentsInChildren<Collider>().First(collider => collider.isTrigger).bounds.center;
                rabbit.TakeArrow(centre, rabbit.transform.right);
                yield return Until(() => rabbit != null && rabbit.IsDead, 10f, r => ok = r);
                Check(ok, "a rabbit shot here dies in the host's game, and here");
                if (ok)
                {
                    var carcass = new System.Collections.Generic.List<InteractionOption>();
                    rabbit.GetOptions(player.GetComponent<Interactor>(), carcass);
                    int before = backpack.CountFood(FoodKind.RabbitCarcass);
                    carcass.FirstOrDefault(option => option.Label == "Take the rabbit").Execute?.Invoke();
                    Check(backpack.CountFood(FoodKind.RabbitCarcass) == before + 1, "took the rabbit");
                }
            }

            Door door = FrontDoor();
            yield return Until(() => door != null && door.IsOpen, 30f, r => ok = r);
            Check(ok, "the front door opens when the host opens it");

            // Ride along: once the host is driving, take the front passenger seat.
            yield return Until(() => CoopWorld.Current.Seats.driver != TruckSeats.Empty, 30f, r => ok = r);
            Check(ok, "the host is in the driver's seat");
            yield return new WaitForSecondsRealtime(1f);
            var options = new System.Collections.Generic.List<Interaction.InteractionOption>();
            truck.AddDoorOption(options, 1);
            Check(options.Count == 1 && options[0].Label == "Get in the front passenger seat", $"the right door offers: {(options.Count > 0 ? options[0].Label : "nothing")}");
            if (options.Count > 0)
                options[0].Execute();
            Check(truck.LocalSeat == 1 && player.Mounted, "sitting in the front passenger seat");
            yield return new WaitForSecondsRealtime(0.5f);
            Snap("guest-sees-driver", truck.SeatAt(1).position + Vector3.up * truck.Eye.y, truck.SeatAt(0).position + Vector3.up * 0.95f);
            Vector3 start = truck.transform.position;
            float worst = 0f;
            // Carried along while the host drives (they stop after about ten seconds).
            float until = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < until)
            {
                worst = Mathf.Max(worst, Vector3.Distance(player.transform.position, truck.SeatAt(1).position));
                if (Vector3.Distance(start, truck.transform.position) > 15f && Mathf.Abs(truck.SpeedKmh) < 0.5f)
                    break;
                yield return null;
            }
            float carried = Vector3.Distance(start, truck.transform.position);
            Check(carried > 15f, $"the truck carried the guest {carried:0} m");
            Check(worst < 0.05f, $"the guest stayed in their seat the whole way (at most {worst:0.00} m out)");
            yield return new WaitForSecondsRealtime(2f);
            truck.LeaveSeat();
            Check(!truck.Aboard && !player.Mounted, "got out");

            // The truck bed: the host's berries arrive.
            yield return Until(() => truck.Bed.Contents().Any(item => item.Name.Contains("erries")), 20f, r => ok = r);
            Check(ok, $"the host's berries show up in the shared truck bed ({truck.Bed.Contents().Count} kinds)");

            // The clock: the host is asking to speed up; when the guest asks too, it does.
            yield return Until(() => clock.TimeMultiplier <= 1.01f && clock.Hour >= 0f, 5f, r => ok = r);
            yield return new WaitForSecondsRealtime(4f);
            clock.RequestSpeed(this, 30f);
            yield return Until(() => clock.TimeMultiplier > 25f, 20f, r => ok = r);
            Check(ok, $"with both asking, the clock speeds up here too ({clock.TimeMultiplier:0.#}x)");
            yield return new WaitForSecondsRealtime(3f);
            clock.ClearSpeed(this);

            // Pack the stove away: it goes from the host's game too.
            yield return new WaitForSecondsRealtime(3f);
            var stoveOptions = new System.Collections.Generic.List<InteractionOption>();
            if (stove != null)
            {
                stove.GetComponent<CampStove>().GetOptions(player.GetComponent<Interactor>(), stoveOptions);
                InteractionOption packAway = stoveOptions.FirstOrDefault(option => option.Label == "Pack the stove away");
                Check(packAway.Label != null && packAway.Enabled, "our own stove can be packed away");
                packAway.Execute?.Invoke();
            }
            yield return new WaitForSecondsRealtime(3f);

            Check(CoopSession.IsGuest, "still a guest");
            CoopSession.Instance.Leave();
            yield return new WaitForSecondsRealtime(2f);
            Check(!CoopSession.Active, "left the trip");
        }

        void Finish()
        {
            Check(errors == 0, $"no errors or exceptions in the log ({errors})");
            log.AppendLine(failures == 0 ? "PASSED" : $"{failures} FAILED");
            Flush();
            Debug.Log($"[CoopTest] {(failures == 0 ? "passed" : failures + " failed")}; see {logPath}");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            // From the command line (-batchmode -executeMethod ...CoopTestRun.Run, without -quit): done.
            if (Application.isBatchMode)
                UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(failures == 0 ? 0 : 1);
#else
            Application.Quit();
#endif
        }
    }
}
#endif
