using System.Collections.Generic;
using System.Linq;
using System.Text;
using Backpacking.Interaction;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Checks that things you can interact with can actually be aimed at: from eye height a couple of metres
    /// away, in several directions, what does the interaction ray hit first? Run with the AutoRebuild request
    /// "run:Backpacking.EditorTools.InteractionDiagnostics.Firewood".
    /// </summary>
    public static class InteractionDiagnostics
    {
        public static string Firewood()
        {
            var report = new StringBuilder();
            var blockers = new Dictionary<string, int>();
            int pieces = 0, reachable = 0, tries = 0, clear = 0;
            foreach (FirewoodPickup wood in Object.FindObjectsByType<FirewoodPickup>(FindObjectsSortMode.None))
            {
                pieces++;
                Vector3 target = wood.transform.position + Vector3.up * 0.08f;
                bool any = false;
                for (int a = 0; a < 8; a++)
                {
                    Vector3 flat = Quaternion.Euler(0f, a * 45f, 0f) * Vector3.forward;
                    Vector3 eye = target + flat * 1.8f;
                    // Stand on the ground there.
                    if (Physics.Raycast(eye + Vector3.up * 5f, Vector3.down, out RaycastHit ground, 10f, ~0, QueryTriggerInteraction.Ignore))
                        eye = ground.point + Vector3.up * 1.68f;
                    else
                        eye += Vector3.up * 1.6f;
                    tries++;
                    if (!Physics.Raycast(eye, (target - eye).normalized, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Collide))
                        continue;
                    if (hit.collider.GetComponentInParent<FirewoodPickup>() == wood)
                    {
                        clear++;
                        any = true;
                    }
                    else
                    {
                        string name = Describe(hit.collider);
                        blockers[name] = blockers.TryGetValue(name, out int n) ? n + 1 : 1;
                    }
                }
                if (any)
                    reachable++;
            }
            report.AppendLine($"{pieces} firewood, {reachable} reachable from at least one side; {clear}/{tries} sight lines clear.");
            foreach (var blocker in blockers.OrderByDescending(b => b.Value).Take(15))
                report.AppendLine($"  blocked {blocker.Value}x by {blocker.Key}");
            return report.ToString();
        }

        static string Describe(Collider collider)
        {
            Transform t = collider.transform;
            string path = t.name;
            for (int i = 0; i < 3 && t.parent != null; i++)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return $"{path} ({collider.GetType().Name}{(collider.isTrigger ? ", trigger" : "")})";
        }
    }
}
