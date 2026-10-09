using System.IO;
using Backpacking.Camp;
using Backpacking.Survival;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// The campfire: a ring of stones round a bed of ash, a teepee of logs, and when it's lit, glowing coals and
    /// layered fire: tongues of flame licking up and flickering, a bright core, sparks drifting up on the heat and a
    /// thin column of smoke, all lit by a warm, wavering light (Campfire flickers it).
    /// </summary>
    public static partial class PrototypeSceneBuilder
    {
        static GameObject BuildFireRing()
        {
            var root = new GameObject();
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.15f, 0f);
            collider.size = new Vector3(1.2f, 0.3f, 1.2f);
            var heat = root.AddComponent<HeatSource>();
            var campfire = root.AddComponent<Campfire>();

            Material stone = GetOrCreateMaterial("CairnStone", new Color(0.45f, 0.44f, 0.42f));
            Material sooty = GetOrCreateMaterial("SootyStone", new Color(0.24f, 0.23f, 0.22f));
            Material ash = GetOrCreateMaterial("Ash", new Color(0.36f, 0.35f, 0.34f), 0.05f);
            Material wood = GetOrCreateMaterial("Wood", new Color(0.33f, 0.22f, 0.13f));
            Material charred = GetOrCreateMaterial("CharredWood", new Color(0.09f, 0.075f, 0.07f), 0.15f);

            // Stones of different sizes round the ring, blackened on the inside.
            var random = new System.Random(17);
            float Jitter(float range) => ((float)random.NextDouble() * 2f - 1f) * range;
            Material darkStone = GetOrCreateMaterial("RingStoneDark", new Color(0.36f, 0.35f, 0.33f));
            for (int i = 0; i < 13; i++)
            {
                float angle = i / 13f * Mathf.PI * 2f + Jitter(0.08f);
                float radius = 0.5f + Jitter(0.04f);
                var position = new Vector3(Mathf.Cos(angle) * radius, 0.04f, Mathf.Sin(angle) * radius);
                // Flat, uneven stones, not pebbles.
                var size = new Vector3(0.22f + Jitter(0.06f), 0.11f + Jitter(0.035f), 0.17f + Jitter(0.05f));
                AddVisual(PrimitiveType.Sphere, root, position, Quaternion.Euler(Jitter(14f), -angle * Mathf.Rad2Deg + Jitter(25f), Jitter(14f)), size, i % 3 == 1 ? darkStone : stone);
                AddVisual(PrimitiveType.Sphere, root, position * 0.93f + Vector3.down * 0.01f, Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f), size * 0.8f, sooty);
            }
            // The bed of ash in the middle.
            AddVisual(PrimitiveType.Cylinder, root, new Vector3(0f, 0.012f, 0f), Quaternion.identity, new Vector3(0.82f, 0.012f, 0.82f), ash);

            // Logs stacked in a teepee, leaning in toward the middle, charred where the flames reach, and two across the base.
            var woodGroup = new GameObject("Wood");
            woodGroup.transform.SetParent(root.transform, false);
            for (int i = 0; i < 6; i++)
            {
                float angle = i / 6f * Mathf.PI * 2f + 0.4f + Jitter(0.12f);
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Quaternion tilt = Quaternion.FromToRotation(Vector3.up, (Vector3.up - outward * (0.75f + Jitter(0.1f))).normalized);
                // (A cylinder's height is twice its scale: these are 30-40 cm sticks.)
                float length = 0.17f + Jitter(0.025f), thick = 0.05f + Jitter(0.01f);
                Vector3 foot = outward * 0.16f + Vector3.up * 0.12f;
                AddVisual(PrimitiveType.Cylinder, woodGroup, foot, tilt, new Vector3(thick, length, thick), wood);
                // The upper half, toward the flames, burned black.
                AddVisual(PrimitiveType.Cylinder, woodGroup, foot + tilt * Vector3.up * (length * 0.5f), tilt, new Vector3(thick * 1.03f, length * 0.5f, thick * 1.03f), charred);
            }
            foreach (float angle in new[] { 0.3f, 1.9f })
            {
                var along = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                AddVisual(PrimitiveType.Cylinder, woodGroup, Vector3.up * 0.05f + along * 0.04f, Quaternion.FromToRotation(Vector3.up, along) * Quaternion.Euler(0f, 30f, 0f),
                    new Vector3(0.075f, 0.24f, 0.075f), charred);
            }

            // Glowing coals in the ash, shown while it burns.
            var coals = new GameObject("Coals");
            coals.transform.SetParent(root.transform, false);
            Material ember = GetOrCreateEmissiveMaterial("Embers", new Color(1f, 0.32f, 0.06f));
            ember.SetColor("_BaseColor", new Color(0.35f, 0.08f, 0.03f));
            ember.SetColor("_EmissionColor", new Color(1f, 0.32f, 0.06f) * 2.6f);
            Material dull = GetOrCreateEmissiveMaterial("EmbersDull", new Color(0.6f, 0.12f, 0.03f));
            dull.SetColor("_BaseColor", new Color(0.12f, 0.05f, 0.04f));
            dull.SetColor("_EmissionColor", new Color(0.7f, 0.14f, 0.03f) * 1.2f);
            for (int i = 0; i < 22; i++)
            {
                float angle = Jitter(Mathf.PI), radius = Mathf.Sqrt((float)random.NextDouble()) * 0.3f;
                var at = new Vector3(Mathf.Cos(angle) * radius, 0.03f, Mathf.Sin(angle) * radius);
                float size = 0.07f + (float)random.NextDouble() * 0.06f;
                AddVisual(PrimitiveType.Sphere, coals, at, Quaternion.Euler(0f, Jitter(180f), 0f), new Vector3(size, size * 0.5f, size * 0.8f), i % 3 == 0 ? dull : ember);
            }

            var lightObject = new GameObject("Fire Light");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            var fireLight = lightObject.AddComponent<Light>();
            fireLight.type = LightType.Point;
            fireLight.color = new Color(1f, 0.52f, 0.18f);
            fireLight.range = 11f;
            fireLight.intensity = 4.5f;
            fireLight.shadows = LightShadows.None;

            ParticleSystem flames = BuildFlames(root.transform);

            SetField(campfire, "woodVisual", woodGroup);
            SetField(campfire, "flames", flames);
            SetField(campfire, "coals", coals);
            SetField(campfire, "fireLight", fireLight);
            SetField(campfire, "heat", heat);
            return root;
        }

        /// <summary>The fire itself: flames, with the core, sparks and smoke as children so they play and stop together.</summary>
        static ParticleSystem BuildFlames(Transform parent)
        {
            Material flame = GetOrCreateFireMaterial("Fire Flame", GetOrCreateFlameTexture(), additive: true);
            Material spark = GetOrCreateFireMaterial("Fire Spark", GetOrCreateSparkTexture(), additive: true);

            // Tongues of flame: rising, swelling then thinning, flickering with noise, from yellow to orange to red.
            ParticleSystem flames = FireLayer("Flames", parent, new Vector3(0f, 0.08f, 0f), flame,
                lifetime: (0.45f, 0.8f), speed: (0.35f, 0.75f), size: (0.34f, 0.6f), rate: 52f, radius: 0.13f, coneAngle: 4f, max: 100,
                colours: new[] { (new Color(1f, 0.85f, 0.45f), 0f), (new Color(1f, 0.55f, 0.14f), 0.35f), (new Color(0.95f, 0.3f, 0.06f), 0.7f), (new Color(0.7f, 0.15f, 0.03f), 1f) },
                // Gone before they're dull red blobs drifting off on their own.
                alphas: new[] { (0f, 0f), (0.9f, 0.1f), (0.55f, 0.45f), (0f, 0.75f) },
                sizeCurve: new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(0.75f, 0.35f), new Keyframe(1f, 0.2f)),
                noise: (0.1f, 2.5f));
            ParticleSystem.MainModule main = flames.main;
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);

            // The bright heart of it, low in the logs.
            FireLayer("Core", flames.transform, Vector3.zero, flame,
                lifetime: (0.3f, 0.5f), speed: (0.2f, 0.45f), size: (0.24f, 0.4f), rate: 30f, radius: 0.1f, coneAngle: 4f, max: 40,
                colours: new[] { (new Color(1f, 0.97f, 0.8f), 0f), (new Color(1f, 0.75f, 0.3f), 0.5f), (new Color(1f, 0.45f, 0.1f), 1f) },
                alphas: new[] { (0f, 0f), (0.9f, 0.15f), (0f, 1f) },
                sizeCurve: new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.4f, 1f), new Keyframe(1f, 0.4f)),
                noise: (0.06f, 2.5f), localSpace: true);

            // Sparks: tiny, bright, drifting and wandering up on the heat, a few at a time and a puff now and then.
            ParticleSystem sparks = FireLayer("Sparks", flames.transform, new Vector3(0f, 0f, 0.15f), spark,
                lifetime: (1.2f, 2.6f), speed: (0.8f, 1.8f), size: (0.012f, 0.03f), rate: 7f, radius: 0.15f, coneAngle: 18f, max: 60,
                colours: new[] { (new Color(1f, 0.85f, 0.4f), 0f), (new Color(1f, 0.45f, 0.08f), 0.6f), (new Color(0.7f, 0.15f, 0.03f), 1f) },
                alphas: new[] { (1f, 0f), (1f, 0.6f), (0f, 1f) },
                sizeCurve: AnimationCurve.Linear(0f, 1f, 1f, 0.4f),
                noise: (0.9f, 0.8f));
            ParticleSystem.EmissionModule sparkBursts = sparks.emission;
            sparkBursts.SetBursts(new[] { new ParticleSystem.Burst(0.8f, 3, 8, 0, 2.7f) });

            // A thin column of smoke, rising slowly from above the flames and spreading as it goes.
            ParticleSystem smoke = FireLayer("Smoke", flames.transform, new Vector3(0f, 0f, 0.55f), GetOrCreateFogWispMaterial(),
                lifetime: (3.5f, 5.5f), speed: (0.35f, 0.6f), size: (0.35f, 0.55f), rate: 4.5f, radius: 0.08f, coneAngle: 6f, max: 40,
                colours: new[] { (new Color(0.32f, 0.31f, 0.3f), 0f), (new Color(0.5f, 0.5f, 0.5f), 1f) },
                alphas: new[] { (0f, 0f), (0.32f, 0.2f), (0.18f, 0.6f), (0f, 1f) },
                sizeCurve: AnimationCurve.Linear(0f, 1f, 1f, 3.2f),
                noise: (0.25f, 0.4f));
            ParticleSystem.RotationOverLifetimeModule turn = smoke.rotationOverLifetime;
            turn.enabled = true;
            turn.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            ParticleSystem.MainModule smokeMain = smoke.main;
            smokeMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            // Behind the flames, so they burn through it.
            smoke.GetComponent<ParticleSystemRenderer>().sortingFudge = 2f;
            return flames;
        }

        /// <summary>One layer of the fire: a cone emitter pointing up, its colour, fade, size and wander over each particle's life.</summary>
        static ParticleSystem FireLayer(string name, Transform parent, Vector3 position, Material material,
            (float min, float max) lifetime, (float min, float max) speed, (float min, float max) size, float rate, float radius, float coneAngle, int max,
            (Color colour, float time)[] colours, (float alpha, float time)[] alphas, AnimationCurve sizeCurve, (float strength, float frequency) noise,
            bool localSpace = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            // Cone emitters fire along local +Z: the top layer points it up; its children inherit that.
            if (parent.GetComponent<ParticleSystem>() == null)
                go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            var particles = go.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.min, lifetime.max);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.min, speed.max);
            main.startSize = new ParticleSystem.MinMaxCurve(size.min, size.max);
            main.startColor = Color.white;
            main.simulationSpace = localSpace ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = rate;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = radius;

            var fade = new Gradient();
            fade.SetKeys(System.Array.ConvertAll(colours, key => new GradientColorKey(key.colour, key.time)),
                System.Array.ConvertAll(alphas, key => new GradientAlphaKey(key.alpha, key.time)));
            ParticleSystem.ColorOverLifetimeModule colour = particles.colorOverLifetime;
            colour.enabled = true;
            colour.color = fade;

            ParticleSystem.SizeOverLifetimeModule growth = particles.sizeOverLifetime;
            growth.enabled = true;
            growth.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            ParticleSystem.NoiseModule wander = particles.noise;
            wander.enabled = true;
            wander.strength = noise.strength;
            wander.frequency = noise.frequency;
            wander.scrollSpeed = 0.6f;
            wander.quality = ParticleSystemNoiseQuality.Medium;

            var particleRenderer = go.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            particleRenderer.sharedMaterial = material;
            particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            particleRenderer.receiveShadows = false;
            return particles;
        }

        /// <summary>URP's unlit particle shader with a texture: added onto what's behind (flames, sparks) or blended.</summary>
        static Material GetOrCreateFireMaterial(string name, Texture2D texture, bool additive)
        {
            string path = $"{GeneratedFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", additive ? 2f : 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>A tongue of flame: broad and soft at the bottom, drawn up to a wavering point, ragged with noise.</summary>
        static Texture2D GetOrCreateFlameTexture() => GetOrCreateTexture("FireFlame.png", 128, (u, v) =>
        {
            // v runs bottom (0) to top (1); the flame is widest a third of the way up.
            float lean = (Mathf.PerlinNoise(v * 3f + 5f, 1.3f) - 0.5f) * 0.18f * v;
            float x = Mathf.Abs(u - 0.5f - lean) * 2f;
            float width = Mathf.Sin(Mathf.Clamp01(v * 1.05f) * Mathf.PI * 0.9f + 0.25f) * Mathf.Lerp(0.9f, 0.35f, v);
            float body = Mathf.Clamp01(1f - x / Mathf.Max(0.05f, width));
            float ragged = Mathf.PerlinNoise(u * 6f + 3f, v * 4f + 9f) * 0.6f + Mathf.PerlinNoise(u * 13f, v * 9f + 2f) * 0.4f;
            float alpha = Mathf.Clamp01(body * 1.6f - (1f - ragged) * 0.7f) * Mathf.SmoothStep(0f, 1f, v * 6f) * Mathf.SmoothStep(1f, 0f, (v - 0.75f) * 4f);
            return alpha * alpha * (3f - 2f * alpha);
        });

        /// <summary>A spark: a small bright dot with a soft glow round it.</summary>
        static Texture2D GetOrCreateSparkTexture() => GetOrCreateTexture("FireSpark.png", 32, (u, v) =>
        {
            float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            return Mathf.Clamp01(Mathf.Exp(-r * r * 6f) + Mathf.Clamp01(1f - r * 2.5f));
        });

        /// <summary>A white texture whose alpha is <paramref name="alpha"/>(u, v), saved in Generated (made once).</summary>
        static Texture2D GetOrCreateTexture(string file, int size, System.Func<float, float, float> alpha)
        {
            string path = $"{GeneratedFolder}/{file}";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha((x + 0.5f) / size, (y + 0.5f) / size))));
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
