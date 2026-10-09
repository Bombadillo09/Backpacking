using System;
using System.Collections.Generic;
using Backpacking.Survival;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Backpacking.Player
{
    /// <summary>
    /// Gear with no model of its own to hold, shaped from simple forms like <see cref="HeldFood"/>: the fishing rod
    /// (or a hand line on its spool), the flashlight, and anything else carried to hand from the pack, by its picture's
    /// key (a stuffed tent, a rolled mat, a folded fleece). Origin at the grip, +Y along the item, +Z its front.
    /// </summary>
    public static class HeldGear
    {
        /// <summary>The pictures gear can be held as, numbered for co-op (0 is "something else").</summary>
        static readonly string[] Icons =
        {
            null, "tent", "sleepingbag", "mat", "airmat", "stove", "gas", "matches", "firewood", "snare", "chair", "filter",
            "pelt", "hide", "boots", "garment-base", "garment-fleece", "garment-shell", "garment-down", "garment-pants", "garment-hat",
            "water", "bandage", "antibiotics", "bow", "rod", "fishing", "flashlight", "machete",
        };

        public static byte IconCode(string icon)
        {
            int index = Array.IndexOf(Icons, icon);
            return (byte)Mathf.Max(0, index);
        }

        public static string IconOf(byte code) => code < Icons.Length ? Icons[code] : null;

        /// <summary>The model for a fishing rod, flashlight or other gear slot; null for anything else.</summary>
        public static GameObject Build(HotbarSlot slot, bool goodRod, Material plain, List<Material> owned)
        {
            if (plain == null)
                return null;
            return slot.kind switch
            {
                HotbarKind.FishingRod => goodRod ? Rod(plain, owned) : HandLine(plain, owned),
                HotbarKind.Flashlight => Flashlight(plain, owned),
                HotbarKind.Gear => Gear(slot.icon, plain, owned),
                _ => null,
            };
        }

        sealed class Parts
        {
            public readonly GameObject Root;
            readonly Material plain;
            readonly List<Material> owned;

            public Parts(string name, Material plain, List<Material> owned)
            {
                Root = new GameObject(name);
                this.plain = plain;
                this.owned = owned;
            }

            public Material Tint(Color colour, float smoothness = 0.35f, Color? glow = null)
            {
                var material = new Material(plain);
                material.SetColor("_BaseColor", colour);
                material.SetFloat("_Smoothness", smoothness);
                if (glow != null)
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", glow.Value);
                }
                owned.Add(material);
                return material;
            }

            public void Add(PrimitiveType type, Vector3 position, Vector3 euler, Vector3 scale, Material material)
            {
                GameObject part = GameObject.CreatePrimitive(type);
                if (Application.isPlaying)
                    Object.Destroy(part.GetComponent<Collider>());
                else
                    Object.DestroyImmediate(part.GetComponent<Collider>());
                part.transform.SetParent(Root.transform, false);
                part.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(euler));
                part.transform.localScale = scale;
                part.GetComponent<Renderer>().sharedMaterial = material;
            }

            /// <summary>A round rod from <paramref name="a"/> to <paramref name="b"/>.</summary>
            public void Rod(Vector3 a, Vector3 b, float radius, Material material)
            {
                GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                if (Application.isPlaying)
                    Object.Destroy(part.GetComponent<Collider>());
                else
                    Object.DestroyImmediate(part.GetComponent<Collider>());
                part.transform.SetParent(Root.transform, false);
                part.transform.SetLocalPositionAndRotation((a + b) / 2f, Quaternion.FromToRotation(Vector3.up, b - a));
                part.transform.localScale = new Vector3(radius * 2f, Vector3.Distance(a, b) / 2f, radius * 2f);
                part.GetComponent<Renderer>().sharedMaterial = material;
            }
        }

        /// <summary>A telescopic rod held at the cork grip, angled up and out ahead, with the reel under the hand.</summary>
        static GameObject Rod(Material plain, List<Material> owned)
        {
            var parts = new Parts("Held fishing rod", plain, owned);
            Vector3 along = new Vector3(0f, 0.62f, 0.78f).normalized;
            Material cork = parts.Tint(new Color(0.72f, 0.56f, 0.38f), 0.2f), blank = parts.Tint(new Color(0.12f, 0.13f, 0.15f), 0.7f);
            parts.Rod(-along * 0.12f, along * 0.14f, 0.013f, cork);
            parts.Rod(along * 0.14f, along * 0.9f, 0.007f, blank);
            parts.Rod(along * 0.9f, along * 1.75f, 0.0035f, blank);
            // Line guides, and the reel hanging under the grip.
            Material steel = parts.Tint(new Color(0.75f, 0.76f, 0.78f), 0.85f);
            foreach (float at in new[] { 0.5f, 0.95f, 1.35f, 1.7f })
                parts.Add(PrimitiveType.Sphere, along * at - Vector3.Cross(along, Vector3.right) * 0.008f, Vector3.zero, Vector3.one * 0.008f, steel);
            Vector3 under = -Vector3.Cross(Vector3.right, along).normalized;
            parts.Rod(along * 0.02f, along * 0.02f + under * 0.05f, 0.004f, steel);
            parts.Add(PrimitiveType.Cylinder, along * 0.02f + under * 0.065f, new Vector3(0f, 0f, 90f), new Vector3(0.05f, 0.012f, 0.05f), parts.Tint(new Color(0.55f, 0.55f, 0.58f), 0.7f));
            return parts.Root;
        }

        /// <summary>A hand line: a yellow plastic spool with green line wound on it, and a hook and sinker dangling.</summary>
        static GameObject HandLine(Material plain, List<Material> owned)
        {
            var parts = new Parts("Held hand line", plain, owned);
            Material spool = parts.Tint(new Color(0.92f, 0.72f, 0.1f), 0.5f);
            foreach (float side in new[] { -0.017f, 0.017f })
                parts.Add(PrimitiveType.Cylinder, new Vector3(side, 0.04f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.075f, 0.002f, 0.075f), spool);
            parts.Add(PrimitiveType.Cylinder, new Vector3(0f, 0.04f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.05f, 0.016f, 0.05f), parts.Tint(new Color(0.3f, 0.7f, 0.35f), 0.4f));
            parts.Rod(new Vector3(0f, 0.04f, 0.03f), new Vector3(0f, -0.08f, 0.04f), 0.0007f, parts.Tint(new Color(0.85f, 0.85f, 0.8f)));
            parts.Add(PrimitiveType.Sphere, new Vector3(0f, -0.085f, 0.04f), Vector3.zero, Vector3.one * 0.01f, parts.Tint(new Color(0.4f, 0.4f, 0.42f), 0.6f));
            return parts.Root;
        }

        /// <summary>A flashlight pointing ahead (+Z), held round the barrel, its lens lit.</summary>
        static GameObject Flashlight(Material plain, List<Material> owned)
        {
            var parts = new Parts("Held flashlight", plain, owned);
            Material body = parts.Tint(new Color(0.1f, 0.11f, 0.12f), 0.55f), grip = parts.Tint(new Color(0.18f, 0.2f, 0.22f), 0.25f);
            parts.Rod(new Vector3(0f, 0f, -0.07f), new Vector3(0f, 0f, 0.08f), 0.016f, body);
            parts.Rod(new Vector3(0f, 0f, -0.04f), new Vector3(0f, 0f, 0.04f), 0.0175f, grip);
            parts.Rod(new Vector3(0f, 0f, 0.08f), new Vector3(0f, 0f, 0.125f), 0.023f, body);
            parts.Rod(new Vector3(0f, 0f, 0.124f), new Vector3(0f, 0f, 0.128f), 0.02f, parts.Tint(new Color(1f, 0.97f, 0.88f), 0.9f, new Color(1.6f, 1.5f, 1.2f)));
            // The switch on top, under the thumb.
            parts.Add(PrimitiveType.Cube, new Vector3(0f, 0.016f, 0.03f), Vector3.zero, new Vector3(0.008f, 0.006f, 0.014f), parts.Tint(new Color(0.6f, 0.15f, 0.1f)));
            return parts.Root;
        }

        /// <summary>Anything else from the pack, from its picture's key.</summary>
        static GameObject Gear(string icon, Material plain, List<Material> owned)
        {
            var parts = new Parts("Held gear", plain, owned);
            // A stuffed sack, held by its drawcord end.
            void Sack(Color colour, float radius, float length)
            {
                Material fabric = parts.Tint(colour, 0.25f);
                parts.Add(PrimitiveType.Capsule, new Vector3(0f, length / 2f, 0f), Vector3.zero, new Vector3(radius * 2f, length / 2f, radius * 2f), fabric);
                parts.Add(PrimitiveType.Cylinder, new Vector3(0f, 0.005f, 0f), Vector3.zero, new Vector3(radius * 1.2f, 0.01f, radius * 1.2f), parts.Tint(new Color(0.12f, 0.12f, 0.12f)));
            }
            // A folded garment, carried flat.
            void Folded(Color colour, float puff = 1f) =>
                parts.Add(PrimitiveType.Cube, new Vector3(0f, 0.06f, 0.02f), new Vector3(10f, 0f, 0f), new Vector3(0.2f, 0.14f, 0.05f * puff), parts.Tint(colour, 0.2f));

            switch (icon)
            {
                case "tent":
                    Sack(new Color(0.36f, 0.42f, 0.27f), 0.065f, 0.34f);
                    break;
                case "sleepingbag":
                    Sack(new Color(0.5f, 0.12f, 0.1f), 0.1f, 0.3f);
                    break;
                case "airmat":
                    Sack(new Color(0.85f, 0.4f, 0.12f), 0.045f, 0.2f);
                    break;
                case "chair":
                    Sack(new Color(0.2f, 0.25f, 0.32f), 0.055f, 0.36f);
                    break;
                case "mat":
                    // A foam roll, held across the middle.
                    parts.Add(PrimitiveType.Cylinder, new Vector3(0f, 0.04f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.15f, 0.27f, 0.15f), parts.Tint(new Color(0.85f, 0.75f, 0.2f), 0.15f));
                    break;
                case "stove":
                case "gas":
                {
                    Material paint = parts.Tint(new Color(0.15f, 0.35f, 0.6f), 0.5f);
                    parts.Add(PrimitiveType.Cylinder, new Vector3(0f, 0.045f, 0f), Vector3.zero, new Vector3(0.1f, 0.045f, 0.1f), paint);
                    if (icon == "stove")
                        parts.Add(PrimitiveType.Cylinder, new Vector3(0f, 0.11f, 0f), Vector3.zero, new Vector3(0.11f, 0.02f, 0.11f), parts.Tint(new Color(0.7f, 0.7f, 0.72f), 0.7f));
                    break;
                }
                case "matches":
                    parts.Add(PrimitiveType.Cube, new Vector3(0f, 0.02f, 0f), Vector3.zero, new Vector3(0.05f, 0.035f, 0.015f), parts.Tint(new Color(0.75f, 0.12f, 0.1f)));
                    break;
                case "firewood":
                    parts.Add(PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0.05f), new Vector3(70f, 0f, 0f), new Vector3(0.07f, 0.22f, 0.07f), parts.Tint(new Color(0.36f, 0.25f, 0.15f), 0.1f));
                    break;
                case "snare":
                    parts.Add(PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0f), new Vector3(90f, 0f, 0f), new Vector3(0.08f, 0.002f, 0.08f), parts.Tint(new Color(0.72f, 0.56f, 0.28f), 0.7f));
                    parts.Rod(Vector3.zero, new Vector3(0f, 0.14f, 0f), 0.006f, parts.Tint(new Color(0.33f, 0.22f, 0.13f)));
                    break;
                case "filter":
                    parts.Add(PrimitiveType.Sphere, new Vector3(0f, 0.06f, 0f), Vector3.zero, new Vector3(0.07f, 0.12f, 0.03f), parts.Tint(new Color(0.2f, 0.45f, 0.75f), 0.5f));
                    break;
                case "pelt":
                    parts.Add(PrimitiveType.Sphere, new Vector3(0f, 0.08f, 0f), new Vector3(0f, 0f, 15f), new Vector3(0.14f, 0.2f, 0.025f), parts.Tint(new Color(0.5f, 0.42f, 0.33f), 0.1f));
                    break;
                case "hide":
                    parts.Add(PrimitiveType.Cube, new Vector3(0f, 0.1f, 0.03f), new Vector3(10f, 0f, 0f), new Vector3(0.3f, 0.22f, 0.07f), parts.Tint(new Color(0.62f, 0.45f, 0.3f), 0.1f));
                    break;
                case "garment-base":
                    Folded(new Color(0.35f, 0.4f, 0.45f));
                    break;
                case "garment-fleece":
                    Folded(new Color(0.8f, 0.42f, 0.15f), 1.4f);
                    break;
                case "garment-shell":
                    Folded(new Color(0.15f, 0.35f, 0.6f));
                    break;
                case "garment-down":
                    Folded(new Color(0.7f, 0.15f, 0.12f), 2f);
                    break;
                case "garment-pants":
                    Folded(new Color(0.25f, 0.27f, 0.25f), 1.3f);
                    break;
                case "garment-hat":
                    parts.Add(PrimitiveType.Sphere, new Vector3(0f, 0.05f, 0f), Vector3.zero, new Vector3(0.16f, 0.1f, 0.16f), parts.Tint(new Color(0.45f, 0.2f, 0.15f), 0.15f));
                    break;
                case "boots":
                    parts.Add(PrimitiveType.Cube, new Vector3(0f, 0.06f, 0.04f), Vector3.zero, new Vector3(0.1f, 0.13f, 0.26f), parts.Tint(new Color(0.35f, 0.22f, 0.13f), 0.3f));
                    break;
                default:
                    parts.Add(PrimitiveType.Cube, new Vector3(0f, 0.05f, 0f), Vector3.zero, new Vector3(0.1f, 0.1f, 0.06f), parts.Tint(new Color(0.45f, 0.45f, 0.42f)));
                    break;
            }
            return parts.Root;
        }
    }
}
