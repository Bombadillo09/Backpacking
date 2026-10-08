#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Backpacking.Hunting;
using Backpacking.Interaction;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.Wildlife;
using Backpacking.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Backpacking.UI
{
    /// <summary>
    /// An automatic hunting check, editor only. When Temp/backpacking-huntingtest-request exists as Play mode starts,
    /// it starts a trip, hands the hiker a bow, then looses real arrows at real animals: a deer through the gut (it
    /// should run off bleeding, lie down and die later), a deer through the heart and lungs (a short run, then down),
    /// a rabbit, and a miss into the ground (the arrow should lie there to be picked up). It field dresses the deer
    /// and takes meat and the hide. Results go to Logs/huntingtest.log, pictures to Temp/hunt-*.png; then it leaves
    /// Play mode. Started by the editor's HuntingTestRun.Run.
    /// </summary>
    public class HuntingTest : MonoBehaviour
    {
        public const string RequestPath = "Temp/backpacking-huntingtest-request";
        const string LogPath = "Logs/huntingtest.log";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        readonly StringBuilder log = new();
        bool finished;
        float startedAt;
        FirstPersonController player;
        Backpack backpack;
        TimeOfDay timeOfDay;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void MaybeStart()
        {
            if (File.Exists(RequestPath))
                new GameObject("Hunting Test").AddComponent<HuntingTest>();
        }

        void Note(string line)
        {
            log.AppendLine(line);
            Debug.Log($"[HuntingTest] {line}");
        }

        IEnumerator Start()
        {
            File.Delete(RequestPath);
            Application.runInBackground = true;
            yield return null;
            FindAnyObjectByType<GameMenus>().BeginBenchmarkTrip();
            yield return new WaitForSeconds(2.5f);

            player = FindAnyObjectByType<FirstPersonController>();
            // The player's pack, not the truck bed (which is a Backpack too).
            backpack = FindAnyObjectByType<Player.FirstPersonController>().GetComponent<Backpack>();
            timeOfDay = FindAnyObjectByType<TimeOfDay>();
            backpack.AddBow();
            backpack.AddArrows(12);
            Arrow.Trace = true;
            Note($"--- {System.DateTime.Now:yyyy-MM-dd HH:mm} --- bow {backpack.HasBow}, arrows {backpack.Arrows}, on hotbar {backpack.OnHotbar(new HotbarSlot(HotbarKind.Bow))}");

            yield return InHand();
            yield return Gut();
            yield return Vitals();
            yield return Rabbit();
            yield return Miss();
            yield return Butcher();

            Finish();
        }

        void Finish()
        {
            if (finished)
                return;
            finished = true;
            Arrow.Trace = false;
            File.AppendAllText(LogPath, log.ToString());
            UnityEditor.EditorApplication.ExitPlaymode();
        }

        // A step that throws stops the run; this makes sure the log is written and Play mode ends anyway.
        void Update()
        {
            if (startedAt == 0f)
                startedAt = Time.realtimeSinceStartup;
            if (!finished && Time.realtimeSinceStartup - startedAt > 240f)
            {
                Note("TIMED OUT (a step probably threw: see the Editor log).");
                Finish();
            }
        }

        /// <summary>The bow in hand, raised and at full draw, through the eyes.</summary>
        IEnumerator InHand()
        {
            Hotbar hotbar = FindAnyObjectByType<Hotbar>();
            int slot = backpack.Hotbar.ToList().FindIndex(entry => entry.kind == HotbarKind.Bow);
            typeof(Hotbar).GetMethod("Select", Private)!.Invoke(hotbar, new object[] { slot });
            yield return new WaitForSeconds(0.6f);
            Bow bow = FindAnyObjectByType<Bow>();
            ScreenCapture.CaptureScreenshot("Temp/hunt-bow-lowered.png");
            yield return new WaitForSeconds(0.3f);

            // Hold the real mouse button down, as a player would. An unfocused editor normally drops input, so input
            // ignores focus while the button's held. (The project has no input settings asset; these are the
            // in-memory defaults, put back afterwards.)
            InputSettings settings = InputSystem.settings;
            InputSettings.BackgroundBehavior ownBackground = settings.backgroundBehavior;
            InputSettings.EditorInputBehaviorInPlayMode ownEditorInput = settings.editorInputBehaviorInPlayMode;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            int arrowsBefore = backpack.Arrows;
            for (float t = 0f; t < 1.4f; t += Time.deltaTime)
            {
                Cursor.lockState = CursorLockMode.Locked;
                InputSystem.QueueStateEvent(Mouse.current, new MouseState().WithButton(MouseButton.Left));
                yield return null;
            }
            ScreenCapture.CaptureScreenshot("Temp/hunt-bow-drawn.png");
            yield return null;
            var appearance = FindAnyObjectByType<Character.CharacterAppearance>();
            Character.HikerPose pose = appearance.Pose;
            Animator animator = appearance.Animator;
            Note($"  drawn: drawing {typeof(Bow).GetField("drawing", Private)!.GetValue(bow)}, draw {typeof(Bow).GetField("draw", Private)!.GetValue(bow)}; " +
                 $"left hand {Vector3.Distance(pose.LeftHandTarget ?? Vector3.zero, animator.GetBoneTransform(HumanBodyBones.LeftHand).position):F2} m off its target, " +
                 $"right hand {Vector3.Distance(pose.HandTarget ?? Vector3.zero, animator.GetBoneTransform(HumanBodyBones.RightHand).position):F2} m off");
            player.ThirdPerson = true;
            for (float t = 0f; t < 0.6f; t += Time.deltaTime)
            {
                Cursor.lockState = CursorLockMode.Locked;
                InputSystem.QueueStateEvent(Mouse.current, new MouseState().WithButton(MouseButton.Left));
                yield return null;
            }
            ScreenCapture.CaptureScreenshot("Temp/hunt-bow-drawn-third.png");
            yield return null;
            Shoot("hunt-bow-drawn-side", player.transform.position + Vector3.up * 1.3f, 3f);
            player.ThirdPerson = false;
            // Let go: the arrow flies.
            InputSystem.QueueStateEvent(Mouse.current, new MouseState());
            yield return new WaitForSeconds(0.5f);
            Note($"  released: arrows {arrowsBefore} -> {backpack.Arrows}");
            settings.backgroundBehavior = ownBackground;
            settings.editorInputBehaviorInPlayMode = ownEditorInput;
            backpack.AddArrows(arrowsBefore - backpack.Arrows);
            Note($"In hand: held {hotbar.Held.kind}, bow model {(GameObject.Find("Held Bow") != null ? "shown" : "missing")}");
        }

        Animal Find(AnimalKind kind, Animal not = null) =>
            Animal.All.Where(a => a.Kind == kind && !a.IsDead && a != not && !a.KeepAround)
                .OrderBy(a => (a.transform.position - player.transform.position).sqrMagnitude).FirstOrDefault();

        /// <summary>
        /// Stands the animal side-on, <paramref name="distance"/> metres from the player, in the first direction round
        /// from straight ahead with nothing solid between the eyes and its body.
        /// </summary>
        void PlaceBroadside(Animal animal, float distance)
        {
            Vector3 ahead = player.transform.forward;
            ahead.y = 0f;
            Vector3 at = default;
            for (int turn = 0; turn < 24; turn++)
            {
                Vector3 direction = Quaternion.Euler(0f, turn * 15f, 0f) * ahead.normalized;
                at = player.transform.position + direction * distance;
                at.y = GroundCover.HeightAt(at);
                Vector3 eye = player.CameraPivot.position;
                if (!Physics.Linecast(eye, at + Vector3.up * 0.4f, ~0, QueryTriggerInteraction.Ignore)
                    && !Physics.Linecast(eye, at + Vector3.up * 1.2f, ~0, QueryTriggerInteraction.Ignore))
                {
                    ahead = direction;
                    break;
                }
            }
            float yaw = Quaternion.LookRotation(ahead).eulerAngles.y + 90f;
            animal.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            typeof(Animal).GetField("heading", Private)!.SetValue(animal, yaw);
            typeof(Animal).GetField("lastPosition", Private)!.SetValue(animal, at);
            // Calm, whatever startled it before (the practice shot, say).
            FieldInfo state = typeof(Animal).GetField("state", Private)!;
            state.SetValue(animal, System.Enum.Parse(state.FieldType, "Grazing"));
            typeof(Animal).GetField("stateTimer", Private)!.SetValue(animal, 30f);
        }

        /// <summary>Looses an arrow from the eyes at a point on the animal's body (along: −1 rump to 1 chest; height: 0 hooves to 1 top).</summary>
        void ShootAt(Animal animal, float along, float height)
        {
            var box = animal.GetComponentsInChildren<BoxCollider>().First(c => c.name == "Hitbox");
            Vector3 half = box.size * 0.5f;
            Vector3 target = box.transform.TransformPoint(box.center + new Vector3(0f, height * box.size.y - half.y, along * half.z));
            Vector3 eye = player.CameraPivot.position;
            bool blocked = Physics.Linecast(eye, target, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore);
            Note($"  aiming at {animal.Kind} ({State(animal)}) {Vector3.Distance(eye, target):0.0} m away: box centre {box.center:F2} size {box.size:F2}, " +
                 $"world bounds {box.bounds.center:F1}/{box.bounds.size:F1}, target {target:F1}" +
                 (blocked ? $"; LINE BLOCKED by {hit.collider.name} at {hit.distance:0.0} m" : ""));
            ShootAtPoint(target);
        }

        /// <summary>Where every arrow in the world is, and what it's stuck in.</summary>
        void ArrowReport(string when)
        {
            foreach (Arrow arrow in FindObjectsByType<Arrow>(FindObjectsSortMode.None))
                Note($"  {when}: arrow at {arrow.transform.position:F1}, in {(arrow.transform.parent != null ? arrow.transform.parent.name : "the world")}");
        }

        void ShootAtPoint(Vector3 target)
        {
            Vector3 from = player.CameraPivot.position;
            const float speed = 66f;
            // Aim off for the drop over the distance.
            float time = Vector3.Distance(from, target) / speed;
            Vector3 aim = target + Vector3.up * (0.5f * 9.81f * 0.75f * time * time);
            Vector3 direction = (aim - from).normalized;
            backpack.TryUseArrow();
            Arrow.Shoot(from + direction * 0.4f, direction * speed, player.transform);
        }

        string State(Animal animal) => animal == null ? "gone" : typeof(Animal).GetField("state", Private)!.GetValue(animal).ToString();

        IEnumerator Gut()
        {
            Animal deer = Find(AnimalKind.Deer);
            if (deer == null)
            {
                Note("Gut shot: SKIPPED, no deer about.");
                yield break;
            }
            PlaceBroadside(deer, 22f);
            yield return null;
            Vector3 start = deer.transform.position;
            ShootAt(deer, -0.45f, 0.62f);
            yield return new WaitForSeconds(0.6f);
            ArrowReport("gut shot");
            Note($"Gut shot: state now {State(deer)}, wounded {deer.KeepAround && !deer.IsDead}");
            float waited = 0f;
            while (deer != null && State(deer) == "Fleeing" && waited < 60f)
            {
                waited += Time.deltaTime;
                yield return null;
            }
            float ran = deer != null ? Vector3.Distance(start, deer.transform.position) : -1f;
            Note($"Gut shot: after {waited:0.0} s it's {State(deer)}, {ran:0} m from where it was hit");
            if (deer == null)
                yield break;
            Shoot("hunt-gut-bedded", deer.transform.position, 5f);
            Shoot("hunt-gut-trail-1", Vector3.Lerp(start, deer.transform.position, 0.05f), 6f);
            Shoot("hunt-gut-trail-2", Vector3.Lerp(start, deer.transform.position, 0.4f), 6f);
            Shoot("hunt-gut-hitspot", start, 3.5f);
            // Let it bleed out, with the clock run fast.
            float hourStart = timeOfDay.TotalHours;
            timeOfDay.RequestSpeed(this, 40f);
            while (deer != null && !deer.IsDead && timeOfDay.TotalHours - hourStart < 4f)
                yield return null;
            timeOfDay.ClearSpeed(this);
            Note($"Gut shot: {(deer != null && deer.IsDead ? "died" : "STILL ALIVE")} {timeOfDay.TotalHours - hourStart:0.0} game hours after bedding");
            yield return new WaitForSeconds(1.2f);
            if (deer != null)
                Shoot("hunt-gut-dead", deer.transform.position, 3f);
            gutDeer = deer;
        }

        Animal gutDeer;

        IEnumerator Vitals()
        {
            Animal deer = Find(AnimalKind.Deer, gutDeer);
            if (deer == null)
            {
                Note("Heart-lung shot: SKIPPED, no second deer about.");
                yield break;
            }
            PlaceBroadside(deer, 20f);
            yield return null;
            Vector3 start = deer.transform.position;
            ShootAt(deer, 0.3f, 0.66f);
            yield return new WaitForSeconds(0.6f);
            ArrowReport("heart-lung shot");
            float waited = 0f;
            while (deer != null && !deer.IsDead && waited < 15f)
            {
                waited += Time.deltaTime;
                yield return null;
            }
            Note($"Heart-lung shot: {(deer != null && deer.IsDead ? $"down after {waited:0.0} s, {Vector3.Distance(start, deer.transform.position):0} m on" : "NOT DEAD")}");
            yield return new WaitForSeconds(1.2f);
            if (deer != null)
                Shoot("hunt-vitals-dead", deer.transform.position, 3f);
            if (gutDeer == null)
                gutDeer = deer;
        }

        IEnumerator Rabbit()
        {
            Animal rabbit = Find(AnimalKind.Rabbit);
            if (rabbit == null)
            {
                Note("Rabbit: SKIPPED, none about.");
                yield break;
            }
            PlaceBroadside(rabbit, 12f);
            yield return null;
            ShootAt(rabbit, 0f, 0.5f);
            yield return new WaitForSeconds(1f);
            Note($"Rabbit: {(rabbit.IsDead ? "dead" : $"NOT DEAD ({State(rabbit)})")}");
            if (!rabbit.IsDead)
                yield break;
            var interactor = FindAnyObjectByType<Interactor>();
            var options = new System.Collections.Generic.List<InteractionOption>();
            rabbit.GetOptions(interactor, options);
            int before = backpack.CountFood(FoodKind.RabbitCarcass);
            options.FirstOrDefault(o => o.Label.StartsWith("Take")).Execute?.Invoke();
            Note($"Rabbit: options [{string.Join(" | ", options.Select(o => o.Label))}], carcasses {before} -> {backpack.CountFood(FoodKind.RabbitCarcass)}, arrows {backpack.Arrows}");
        }

        IEnumerator Miss()
        {
            Vector3 ahead = player.transform.forward;
            ahead.y = 0f;
            Vector3 ground = player.transform.position + ahead.normalized * 15f;
            ground.y = GroundCover.HeightAt(ground);
            int before = backpack.Arrows;
            ShootAtPoint(ground);
            yield return new WaitForSeconds(0.8f);
            Arrow lying = FindObjectsByType<Arrow>(FindObjectsSortMode.None).FirstOrDefault(a => a.transform.parent == null && a.GetComponent<Collider>() != null);
            if (lying == null)
            {
                Note("Miss: no arrow lying on the ground (it may have broken).");
                yield break;
            }
            Vector3 at = lying.transform.position;
            Shoot("hunt-arrow-lying", at, 1.2f);
            var options = new System.Collections.Generic.List<InteractionOption>();
            lying.GetOptions(FindAnyObjectByType<Interactor>(), options);
            options.FirstOrDefault().Execute?.Invoke();
            yield return null;
            Note($"Miss: arrow lying {Vector3.Distance(at, ground):0.00} m from the aim point; picked up: arrows {before - 1} -> {backpack.Arrows}");
        }

        IEnumerator Butcher()
        {
            Animal deer = gutDeer;
            if (deer == null || !deer.IsDead)
            {
                Note("Butcher: SKIPPED, no dead deer.");
                yield break;
            }
            var interactor = FindAnyObjectByType<Interactor>();
            var options = new System.Collections.Generic.List<InteractionOption>();
            deer.GetOptions(interactor, options);
            Note($"Butcher: options [{string.Join(" | ", options.Select(o => o.Label + (o.Enabled ? "" : " (off)")))}]");
            // The player walks over to it first.
            player.transform.position = deer.transform.position + Vector3.right * 1.5f + Vector3.up * 0.5f;
            options.First(o => o.Label.StartsWith("Field dress")).Execute();
            PlayerActivity activity = interactor.Activity;
            while (activity.IsBusy)
                yield return null;
            options.Clear();
            deer.GetOptions(interactor, options);
            Note($"Butcher: after dressing [{string.Join(" | ", options.Select(o => o.Label + (o.Enabled ? "" : " (off)")))}]");
            float weight = backpack.TotalWeight;
            options.First(o => o.Label.StartsWith("Take 4")).Execute();
            options.First(o => o.Label.StartsWith("Take the hide")).Execute();
            Note($"Butcher: venison {backpack.CountFood(FoodKind.RawVenison)}, hides {backpack.Hides}, weight {weight:0.0} -> {backpack.TotalWeight:0.0} kg, arrows {backpack.Arrows}");
        }

        /// <summary>Renders a spot with a camera of its own to Temp/&lt;name&gt;.png.</summary>
        static void Shoot(string name, Vector3 target, float distance, bool overhead = false)
        {
            var go = new GameObject("Hunting Test Camera");
            var camera = go.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.fieldOfView = 45f;
            go.transform.position = target + (overhead ? new Vector3(0.3f, 1f, -0.25f).normalized : new Vector3(0.6f, 0.55f, -0.6f).normalized) * distance;
            go.transform.LookAt(target);
            var texture = new RenderTexture(900, 600, 24);
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(900, 600, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 900, 600), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes($"Temp/{name}.png", image.EncodeToPNG());
            camera.targetTexture = null;
            Destroy(texture);
            Destroy(image);
            Destroy(go);
        }
    }
}
#endif
