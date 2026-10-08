using System.IO;
using System.Reflection;
using System.Text;
using Backpacking.Vehicles;
using Backpacking.World;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Drives the pickup in the built scene without entering Play mode: steps its physics by hand with scripted
    /// input, and writes what happened to Logs/vehicletest.log. Checks it settles on its wheels, gets up to speed,
    /// can follow the whole road from home to the trailhead, and stops short when driven into the woods.
    /// Run with -executeMethod Backpacking.EditorTools.VehicleTest.RunBatch, or the AutoRebuild request
    /// "run:Backpacking.EditorTools.VehicleTest.Run". The scene isn't saved afterwards.
    /// </summary>
    public static class VehicleTest
    {
        const string LogPath = "Logs/vehicletest.log";

        public static void RunBatch() => Debug.Log(Run());

        public static string Run()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Prototype.unity", OpenSceneMode.Single);
            var pickup = Object.FindAnyObjectByType<Pickup>();
            var road = Object.FindAnyObjectByType<RoadPath>();
            if (pickup == null || road == null)
                return Write("No pickup or road in the scene. Rebuild it first.");

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(Pickup).GetMethod("Awake", flags).Invoke(pickup, null);
            MethodInfo fixedUpdate = typeof(Pickup).GetMethod("FixedUpdate", flags);
            var body = pickup.GetComponent<Rigidbody>();
            SimulationMode previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            const float dt = 0.02f;
            var log = new StringBuilder($"=== Vehicle test {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            Vector3 start = pickup.transform.position;
            Quaternion startRotation = pickup.transform.rotation;

            void Step(Vector2 input)
            {
                pickup.ScriptedInput = input;
                fixedUpdate.Invoke(pickup, null);
                Physics.Simulate(dt);
            }

            string State() =>
                $"{pickup.SpeedKmh,5:0} km/h, up {pickup.transform.up.y:0.00}, {road.OffRoad(pickup.transform.position, out _):0.0} m off the gravel";

            void Reset(Vector3 position, Quaternion rotation)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                pickup.transform.SetPositionAndRotation(position, rotation);
                body.position = position;
                body.rotation = rotation;
                Physics.SyncTransforms();
            }

            try
            {
                for (int i = 0; i < 150; i++)
                    Step(Vector2.zero);
                log.AppendLine($"Parked for 3 s: {State()}, sank {start.y - pickup.transform.position.y:0.00} m");

                for (int i = 0; i < 400; i++)
                    Step(new Vector2(0f, 1f));
                log.AppendLine($"Full throttle for 8 s: {State()}");
                for (int i = 0; i < 200; i++)
                    Step(new Vector2(0f, -1f));
                log.AppendLine($"Then braking for 4 s: {State()}");

                // Follow the road all the way to the trailhead, steering for a point a little ahead.
                Reset(start, startRotation);
                for (int i = 0; i < 100; i++)
                    Step(Vector2.zero);
                float worstOffRoad = 0f, lowestUp = 1f, time = 0f;
                int last = road.Points.Count - 1;
                bool arrived = false;
                while (time < 300f)
                {
                    int nearest = NearestPoint(road, pickup.transform.position);
                    Vector3 target = road.Points[Mathf.Min(nearest + 3, last)];
                    Vector3 local = pickup.transform.InverseTransformPoint(target);
                    float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                    float throttle = Mathf.Abs(angle) > 20f ? 0.3f : 0.75f;
                    Step(new Vector2(Mathf.Clamp(angle / 25f, -1f, 1f), throttle));
                    time += dt;
                    worstOffRoad = Mathf.Max(worstOffRoad, road.OffRoad(pickup.transform.position, out _));
                    lowestUp = Mathf.Min(lowestUp, pickup.transform.up.y);
                    if (Mathf.Abs(time % 20f) < dt * 0.5f)
                        log.AppendLine($"  t={time:0}s at road point {nearest}/{last}: {State()}");
                    if (Vector3.Distance(pickup.transform.position, road.Points[last]) < 12f)
                    {
                        arrived = true;
                        break;
                    }
                }
                log.AppendLine(arrived
                    ? $"Drove the road home to trailhead in {time:0} s ({time / 60f:0.0} min). Furthest off the gravel {worstOffRoad:0.0} m, lowest up {lowestUp:0.00}."
                    : $"FAILED to reach the trailhead in {time:0} s; stuck at {pickup.transform.position} ({State()}).");

                // Turn off the road into the woods, full throttle, and see how far it gets.
                int middle = road.Points.Count / 2;
                Vector3 along = (road.Points[middle + 1] - road.Points[middle]).normalized;
                Vector3 across = Vector3.Cross(Vector3.up, along).normalized;
                Vector3 woodsStart = road.Points[middle] + Vector3.up * 0.3f;
                Reset(woodsStart, Quaternion.LookRotation(across));
                for (int i = 0; i < 100; i++)
                    Step(Vector2.zero);
                float furthest = 0f;
                for (int i = 0; i < 1500; i++)
                {
                    Step(new Vector2(0f, 1f));
                    furthest = Mathf.Max(furthest, road.OffRoad(pickup.transform.position, out _));
                }
                log.AppendLine($"Into the woods at full throttle for 30 s: got {furthest:0.0} m off the gravel; now {State()}");
                // And it can still get back: reverse toward the road.
                for (int i = 1; i <= 1000; i++)
                {
                    Step(new Vector2(0f, -1f));
                    if (i % 100 == 0)
                    {
                        road.OffRoad(pickup.transform.position, out Vector3 back);
                        log.AppendLine($"  reversing t={i * dt:0}s: {State()}, heading·road {Vector3.Dot(-pickup.transform.forward, back):0.00}, at {pickup.transform.position}");
                    }
                }
                log.AppendLine($"Reversing back for 20 s: {State()}");
            }
            finally
            {
                pickup.ScriptedInput = null;
                Physics.simulationMode = previousMode;
            }
            return Write(log.ToString());
        }

        static int NearestPoint(RoadPath road, Vector3 position)
        {
            int best = 0;
            float nearest = float.MaxValue;
            for (int i = 0; i < road.Points.Count; i++)
            {
                Vector3 offset = road.Points[i] - position;
                offset.y = 0f;
                if (offset.sqrMagnitude < nearest)
                {
                    nearest = offset.sqrMagnitude;
                    best = i;
                }
            }
            return best;
        }

        static string Write(string text)
        {
            File.AppendAllText(LogPath, text + "\n");
            return text;
        }
    }
}
