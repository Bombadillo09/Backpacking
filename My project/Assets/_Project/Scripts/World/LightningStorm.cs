using System.Collections;
using System.Collections.Generic;
using Backpacking.Audio;
using Backpacking.UI;
using Backpacking.Survival;
using UnityEngine;

namespace Backpacking.World
{
    /// <summary>
    /// Lightning in thunderstorms: forked bolts striking the ground (and flashes hidden in the cloud) at random
    /// distances, each lighting up the sky and land with a flicker, with its thunder arriving after the sound
    /// has travelled. Out in the open on high ground in a storm, strikes come close: you're warned to get down
    /// into the trees, and a strike close enough can hurt you.
    /// </summary>
    public class LightningStorm : MonoBehaviour
    {
        [SerializeField] WeatherSystem weather;
        [SerializeField] TimeOfDay timeOfDay;
        [SerializeField] ForestAtmosphere atmosphere;
        [SerializeField] AmbienceAudio ambience;
        [SerializeField] Vitals vitals;
        [SerializeField] Transform player;
        [Tooltip("Additive, unfogged material for the bolts.")]
        [SerializeField] Material boltMaterial;

        [Tooltip("Real seconds between strikes in a full storm, min and max.")]
        [SerializeField] Vector2 strikeInterval = new(5f, 22f);
        [Tooltip("How far away strikes usually land, metres.")]
        [SerializeField] Vector2 strikeDistance = new(700f, 5000f);
        [Tooltip("Chance a strike comes close to a player who's exposed on high ground.")]
        [SerializeField, Range(0f, 1f)] float exposedStrikeChance = 0.3f;
        [Tooltip("A strike this close hurts (ground current and the blast).")]
        [SerializeField] float dangerRadius = 25f;
        [SerializeField] Color boltColour = new(0.8f, 0.85f, 1f);

        Light flashLight;
        readonly List<LineRenderer> pieces = new();
        float nextStrike;
        float nextWarning;
        float prominence;
        float nextProminenceCheck;

        /// <summary>Out in the open on high ground: where lightning strikes.</summary>
        public bool PlayerExposed { get; private set; }

        void Start()
        {
            flashLight = new GameObject("Lightning Flash").AddComponent<Light>();
            flashLight.transform.SetParent(transform, false);
            flashLight.type = LightType.Directional;
            flashLight.color = boltColour;
            flashLight.shadows = LightShadows.None;
            flashLight.intensity = 0f;
            flashLight.enabled = false;
            nextStrike = Time.time + Random.Range(strikeInterval.x, strikeInterval.y);
        }

        void Update()
        {
            if (Time.time >= nextProminenceCheck)
            {
                nextProminenceCheck = Time.time + 2f;
                prominence = Prominence(player.position);
            }
            float canopy = atmosphere != null ? atmosphere.Canopy : 0f;
            PlayerExposed = canopy < 0.25f && prominence > 0.75f && !vitals.IsSleeping;

            // Storms bring lightning; heavy rain under a dark sky the odd strike.
            float storminess = Mathf.InverseLerp(0.7f, 1f, weather.RainIntensity) * Mathf.InverseLerp(0.85f, 1f, weather.Overcast);
            if (storminess <= 0.05f)
            {
                nextStrike = Mathf.Max(nextStrike, Time.time + strikeInterval.x);
                return;
            }
            if (PlayerExposed && Time.time >= nextWarning)
            {
                nextWarning = Time.time + 90f;
                Notifications.Post("Lightning! You're exposed up here. Get down off the high ground, into the trees.", 5f);
            }
            if (Time.time < nextStrike)
                return;
            nextStrike = Time.time + Random.Range(strikeInterval.x, strikeInterval.y) / storminess;
            Strike();
        }

        /// <summary>How much of the ground around is lower than here, 0–1: near 1 on summits and ridges.</summary>
        static float Prominence(Vector3 position)
        {
            int lower = 0;
            const int samples = 12;
            for (int i = 0; i < samples; i++)
            {
                Vector3 around = position + Quaternion.Euler(0f, i * 360f / samples, 0f) * Vector3.forward * 120f;
                if (GroundCover.HeightAt(around) < position.y - 6f)
                    lower++;
            }
            return lower / (float)samples;
        }

        /// <summary>A strike this far away right now, with a bolt to see (for testing and screenshots).</summary>
        public void StrikeNow(float distance) => Strike(distance, forceBolt: true);

        void Strike() => Strike(PickDistance(), forceBolt: false);

        /// <summary>Usually kilometres away; close by when the player is exposed on high ground.</summary>
        float PickDistance()
        {
            if (PlayerExposed && Random.value < exposedStrikeChance)
                // Lightning finds the high ground: this one's close.
                return Random.Range(8f, 90f);
            return Random.value < 0.15f ? Random.Range(200f, strikeDistance.x) : Random.Range(strikeDistance.x, strikeDistance.y);
        }

        void Strike(float distance, bool forceBolt)
        {
            Vector3 here = player.position;
            Vector3 direction = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            // A test strike goes where the camera's looking.
            if (forceBolt && Camera.main != null)
                direction = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
            Vector3 ground = here + direction * distance;
            ground.y = GroundCover.HeightAt(ground);

            // About a third of flashes stay up in the cloud: no bolt to see, just the sky lighting up.
            bool inCloud = !forceBolt && distance > 300f && Random.value < 0.35f;
            StartCoroutine(Flash(ground, distance, inCloud));
            ambience?.HearThunder(distance);

            if (distance < dangerRadius && !vitals.IsSleeping)
            {
                float hurt = Mathf.Lerp(55f, 12f, distance / dangerRadius);
                vitals.Hurt(hurt);
                Notifications.Post($"Lightning struck {distance:0} m away! The shock throws you to the ground.", 5f);
                Trip.TripLog.Note("Lightning struck right beside me on the high ground. Too close.");
            }
        }

        IEnumerator Flash(Vector3 ground, float distance, bool inCloud)
        {
            float far = Mathf.InverseLerp(100f, strikeDistance.y, distance);
            float strength = Mathf.Lerp(1f, 0.3f, far) * (inCloud ? 0.5f : 1f);
            // Fog swallows distant bolts (the flash still lights the cloud).
            float density = RenderSettings.fogDensity;
            float seen = Mathf.Exp(-Mathf.Pow(distance * density * 0.15f, 2f));
            if (!inCloud && seen > 0.03f)
                BuildBolt(ground, distance, seen);

            Vector3 toStrike = ground - player.position;
            toStrike.y = 0f;
            flashLight.transform.rotation = Quaternion.LookRotation(Vector3.down * 2f - toStrike.normalized);
            flashLight.enabled = true;
            // A return stroke flickers: a blink, a gap, a brighter blink, then it fades.
            float[] pattern = { 0.6f, 0.05f, 0f, 0.04f, 1f, 0.07f, 0.4f, 0.05f, 0f, 0.03f, 0.7f, 0.05f };
            for (int i = 0; i < pattern.Length; i += 2)
            {
                SetFlash(pattern[i] * strength, pattern[i] > 0f);
                yield return new WaitForSeconds(pattern[i + 1]);
            }
            for (float t = 0f; t < 0.25f; t += Time.deltaTime)
            {
                SetFlash(0.7f * strength * (1f - t / 0.25f), true);
                yield return null;
            }
            SetFlash(0f, false);
            flashLight.enabled = false;
            ClearBolt();
        }

        void SetFlash(float amount, bool boltVisible)
        {
            timeOfDay.Flash = amount;
            flashLight.intensity = amount * 2.5f;
            foreach (LineRenderer piece in pieces)
                piece.enabled = boltVisible;
        }

        /// <summary>A jagged main channel from the cloud to the ground, with a few forks that die out in the air.</summary>
        void BuildBolt(Vector3 ground, float distance, float seen)
        {
            ClearBolt();
            float top = Mathf.Clamp(weather.CloudBase, ground.y + 350f, ground.y + 1200f);
            // Thick enough to read at a distance.
            float width = Mathf.Max(1.5f, distance * 0.007f);
            Vector3 start = ground + new Vector3(Random.Range(-150f, 150f), top - ground.y, Random.Range(-150f, 150f));
            List<Vector3> main = Jagged(start, ground, 28, 0.09f);
            AddPiece(main, width, seen);
            int forks = Random.Range(2, 6);
            for (int f = 0; f < forks; f++)
            {
                Vector3 from = main[Random.Range(3, main.Count / 2)];
                Vector3 direction = (Random.insideUnitSphere + Vector3.down * 1.4f).normalized;
                Vector3 to = from + direction * Random.Range(0.15f, 0.35f) * (top - ground.y);
                AddPiece(Jagged(from, to, 10, 0.14f), width * 0.45f, seen * 0.7f);
            }
        }

        static List<Vector3> Jagged(Vector3 from, Vector3 to, int segments, float roughness)
        {
            var points = new List<Vector3>(segments + 1);
            float length = Vector3.Distance(from, to);
            Vector3 offset = Vector3.zero;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                if (i > 0 && i < segments)
                    offset += Random.insideUnitSphere * length / segments * roughness * 4f;
                // Pinned at both ends.
                points.Add(Vector3.Lerp(from, to, t) + offset * Mathf.Sin(t * Mathf.PI));
            }
            return points;
        }

        void AddPiece(List<Vector3> points, float width, float brightness)
        {
            var line = new GameObject("Bolt").AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.useWorldSpace = true;
            line.positionCount = points.Count;
            line.SetPositions(points.ToArray());
            line.widthMultiplier = width;
            line.widthCurve = AnimationCurve.Linear(0f, 0.7f, 1f, 1f);
            line.alignment = LineAlignment.View;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = boltMaterial;
            // Vertex colours can't go above 1: the brightness rides in alpha, and the material scales it up.
            Color colour = boltColour;
            colour.a = brightness;
            line.startColor = line.endColor = colour;
            pieces.Add(line);
        }

        void ClearBolt()
        {
            foreach (LineRenderer piece in pieces)
                if (piece != null)
                    Destroy(piece.gameObject);
            pieces.Clear();
        }

        void OnDisable()
        {
            if (timeOfDay != null)
                timeOfDay.Flash = 0f;
        }
    }
}
