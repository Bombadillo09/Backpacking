using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.Vehicles;
using Backpacking.World;
using Backpacking.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// Your pickup: an old single-cab truck built from simple shapes, parked beside your home. It has a cab you
    /// can sit in and look out of, a bed for gear, and real wheels (WheelColliders) on a rigidbody.
    /// Local +Z is forward; the origin is on the ground midway between the axles.
    /// </summary>
    public static partial class PrototypeSceneBuilder
    {
        const float WheelRadius = 0.4f;
        const float AxleZ = 1.65f;
        const float TrackX = 0.84f;
        /// <summary>Top of the cab floor, where the driver's feet are.</summary>
        const float CabFloor = 0.58f;

        static void CreatePickup(Terrain terrain, FirstPersonController player)
        {
            if (home == null)
                return;
            Vector2 along = home.RoadDirection, side = new(-along.y, along.x);
            Vector2 parked = home.Centre + side * 5f + along * 1f;
            Vector3 position = OnGround(terrain, parked) + Vector3.up * 0.15f;

            var truck = new GameObject("Pickup");
            truck.transform.SetPositionAndRotation(position, Quaternion.LookRotation(new Vector3(along.x, 0f, along.y)));
            AddSaveId(truck, "pickup");

            Material paint = GetOrCreateMaterial("TruckPaint", new Color(0.2f, 0.33f, 0.45f), 0.55f);
            Material trim = GetOrCreateMaterial("TruckTrim", new Color(0.62f, 0.63f, 0.64f), 0.6f);
            Material black = GetOrCreateMaterial("TruckBlack", new Color(0.06f, 0.06f, 0.06f), 0.2f);
            Material interior = GetOrCreateMaterial("TruckInterior", new Color(0.2f, 0.19f, 0.18f), 0.1f);
            Material bedLiner = GetOrCreateMaterial("TruckBedLiner", new Color(0.12f, 0.12f, 0.12f), 0.05f);
            Material glass = GetOrCreateGlassMaterial();
            Material headlight = GetOrCreateEmissiveMaterial("TruckHeadlight", new Color(1f, 0.95f, 0.8f));
            Material taillight = GetOrCreateEmissiveMaterial("TruckTaillight", new Color(0.7f, 0.05f, 0.03f));

            BuildCab(truck, paint, black, interior, glass, trim);
            BuildFront(truck, paint, black, trim, headlight);
            BuildBed(truck, paint, bedLiner, trim, taillight, black);
            BuildDetails(truck, paint, black, trim, interior, headlight, taillight);
            BuildMoreDetails(truck, paint, black, trim, interior);
            // Glass casts no shadow (a transparent material still has a shadow pass), or the cab is dark and murky.
            foreach (Renderer part in truck.GetComponentsInChildren<Renderer>())
                if (part.sharedMaterial == glass)
                {
                    part.shadowCastingMode = ShadowCastingMode.Off;
                    part.receiveShadows = false;
                }

            // Solid parts, on the truck's rigidbody. The bed is open, so gear (and you) can be put in it.
            AddBox(truck, new Vector3(0f, 1.27f, 0.47f), new Vector3(1.95f, 1.38f, 1.65f));
            AddBox(truck, new Vector3(0f, 1.0f, 1.97f), new Vector3(1.95f, 0.56f, 1.35f));
            AddBox(truck, new Vector3(0f, 0.7f, 1.97f), new Vector3(1.4f, 0.3f, 1.35f));
            AddBox(truck, new Vector3(0f, 0.88f, -1.5f), new Vector3(1.95f, 0.1f, 2.3f));
            AddBox(truck, new Vector3(-0.94f, 1.08f, -1.5f), new Vector3(0.07f, 0.5f, 2.3f));
            AddBox(truck, new Vector3(0.94f, 1.08f, -1.5f), new Vector3(0.07f, 0.5f, 2.3f));
            AddBox(truck, new Vector3(0f, 1.08f, -2.62f), new Vector3(1.95f, 0.5f, 0.07f));
            AddBox(truck, new Vector3(0f, 0.62f, -1.5f), new Vector3(1.2f, 0.18f, 2.6f));

            var body = truck.AddComponent<Rigidbody>();
            body.mass = 1900f;
            body.linearDamping = 0.05f;
            body.angularDamping = 0.5f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.automaticCenterOfMass = false;
            body.centerOfMass = new Vector3(0f, 0.5f, 0.25f);

            var wheels = new WheelCollider[4];
            var visuals = new Transform[4];
            Vector2[] corners = { new(-TrackX, AxleZ), new(TrackX, AxleZ), new(-TrackX, -AxleZ), new(TrackX, -AxleZ) };
            string[] names = { "Front Left", "Front Right", "Rear Left", "Rear Right" };
            for (int i = 0; i < 4; i++)
            {
                var colliderObject = new GameObject($"Wheel {names[i]}");
                colliderObject.transform.SetParent(truck.transform, false);
                colliderObject.transform.localPosition = new Vector3(corners[i].x, WheelRadius + 0.15f, corners[i].y);
                WheelCollider wheel = colliderObject.AddComponent<WheelCollider>();
                wheel.radius = WheelRadius;
                wheel.mass = 25f;
                wheel.suspensionDistance = 0.3f;
                wheel.forceAppPointDistance = 0.1f;
                wheel.suspensionSpring = new JointSpring { spring = 38000f, damper = 4500f, targetPosition = 0.5f };
                wheel.forwardFriction = Friction(1.6f);
                wheel.sidewaysFriction = Friction(1.7f);
                wheels[i] = wheel;
                visuals[i] = BuildWheel(truck, $"Tyre {names[i]}", colliderObject.transform.position, black, trim);
            }

            // The driver sits on the left. Feet on the floor; the bench is at chair height.
            var seat = new GameObject("Driver Seat").transform;
            seat.SetParent(truck.transform, false);
            seat.localPosition = new Vector3(-0.42f, CabFloor, 0.12f);
            var driverExit = new GameObject("Driver Exit").transform;
            driverExit.SetParent(truck.transform, false);
            driverExit.localPosition = new Vector3(-1.55f, 0f, 0.4f);
            var passengerExit = new GameObject("Passenger Exit").transform;
            passengerExit.SetParent(truck.transform, false);
            passengerExit.localPosition = new Vector3(1.55f, 0f, 0.4f);
            Transform steering = BuildSteeringWheel(truck, black);

            var lights = new Light[2];
            for (int i = 0; i < 2; i++)
            {
                var lampObject = new GameObject(i == 0 ? "Headlight Left" : "Headlight Right");
                lampObject.transform.SetParent(truck.transform, false);
                lampObject.transform.SetLocalPositionAndRotation(new Vector3(i == 0 ? -0.68f : 0.68f, 1.0f, 2.7f), Quaternion.Euler(6f, 0f, 0f));
                Light lamp = lampObject.AddComponent<Light>();
                lamp.type = LightType.Spot;
                lamp.range = 45f;
                lamp.spotAngle = 70f;
                lamp.intensity = 4f;
                lamp.color = new Color(1f, 0.95f, 0.85f);
                lamp.shadows = LightShadows.None;
                lights[i] = lamp;
            }

            // The truck bed holds what you've bought and haven't packed: a Backpack with no one wearing it.
            var bedObject = new GameObject("Truck Bed");
            bedObject.transform.SetParent(truck.transform, false);
            bedObject.transform.localPosition = new Vector3(0f, 0.95f, -1.5f);
            var bed = bedObject.AddComponent<Backpack>();
            SetField(bed, "timeOfDay", Object.FindAnyObjectByType<TimeOfDay>());
            SetField(bed, "temperature", Object.FindAnyObjectByType<AmbientTemperature>());

            var pickup = truck.AddComponent<Pickup>();
            SetField(pickup, "bed", bed);
            SetField(pickup, "inputActions", AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath));
            SetField(pickup, "player", player);
            SetField(pickup, "vitals", Object.FindAnyObjectByType<Vitals>());
            SetField(pickup, "road", Object.FindAnyObjectByType<RoadPath>());
            SetObjectArray(pickup, "wheels", wheels);
            SetObjectArray(pickup, "wheelVisuals", visuals);
            SetField(pickup, "seat", seat);
            SetField(pickup, "steeringWheel", steering);
            SetField(pickup, "driverExit", driverExit);
            SetField(pickup, "passengerExit", passengerExit);
            SetObjectArray(pickup, "headlights", lights);

            var saves = Object.FindAnyObjectByType<Saving.SaveSystem>();
            SetField(saves, "pickup", pickup);
            var tutorial = Object.FindAnyObjectByType<Tutorial>();
            if (tutorial != null)
            {
                var guide = tutorial.gameObject.AddComponent<Trip.ArrivalGuide>();
                SetField(guide, "player", player);
                SetField(guide, "backpack", player.GetComponent<Backpack>());
                SetField(guide, "truck", pickup);
                SetField(guide, "store", storeDoor);
                SetField(guide, "trailhead", parkingCentre);
                SetField(guide, "tutorial", tutorial);
                SetField(saves, "arrival", guide);
            }
        }

        /// <summary>Barely tinted, so the view out isn't hazy; the shine still shows it's glass.</summary>
        static void SetGlassTint(Material material)
        {
            material.SetColor("_BaseColor", new Color(0.6f, 0.66f, 0.7f, 0.05f));
            // URP's "preserve specular lighting" switches transparent materials to premultiplied blending (source One),
            // which adds the glass's whole colour on top of the view: a milky sheet in game. Plain alpha blending instead.
            material.SetFloat("_BlendModePreserveSpecular", 0f);
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            // Sky reflections and highlights are what made the glass look milky; leave just a faint tint.
            material.SetFloat("_Smoothness", 0.5f);
            material.SetFloat("_SpecularHighlights", 0f);
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.SetFloat("_EnvironmentReflections", 0f);
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            EditorUtility.SetDirty(material);
        }

        static WheelFrictionCurve Friction(float stiffness) => new()
        {
            extremumSlip = 0.4f,
            extremumValue = 1f,
            asymptoteSlip = 0.8f,
            asymptoteValue = 0.5f,
            stiffness = stiffness,
        };

        static void AddBox(GameObject root, Vector3 centre, Vector3 size)
        {
            var box = root.AddComponent<BoxCollider>();
            box.center = centre;
            box.size = size;
        }

        /// <summary>
        /// The cab, built from panels rather than solid blocks so it looks right from inside: floor, roof, doors
        /// with windows, a raked windscreen, the back wall with its window, the dashboard and a bench seat.
        /// </summary>
        static void BuildCab(GameObject truck, Material paint, Material black, Material interior, Material glass, Material trim)
        {
            const float front = 1.3f, back = -0.35f, roof = 1.95f, belt = 1.25f;
            float length = front - back, middle = (front + back) / 2f;
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, CabFloor - 0.02f, middle), Quaternion.identity, new Vector3(1.85f, 0.04f, length), interior);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, roof, middle - 0.08f), Quaternion.identity, new Vector3(1.92f, 0.06f, length - 0.2f), paint);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, roof - 0.04f, middle - 0.08f), Quaternion.identity, new Vector3(1.84f, 0.02f, length - 0.25f), interior);
            foreach (float x in new[] { -0.95f, 0.95f })
            {
                // Lower door, its window, and the pillars either side.
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x, (CabFloor + belt) / 2f - 0.1f, middle), Quaternion.identity, new Vector3(0.05f, belt - CabFloor + 0.2f, length), paint);
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x, (belt + roof) / 2f, middle - 0.05f), Quaternion.identity, new Vector3(0.02f, roof - belt, length - 0.3f), glass);
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x, (belt + roof) / 2f, back + 0.04f), Quaternion.identity, new Vector3(0.06f, roof - belt, 0.1f), paint);
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x * 0.99f, (belt + roof) / 2f, front - 0.18f), Quaternion.Euler(-25f, 0f, 0f), new Vector3(0.07f, roof - belt + 0.05f, 0.08f), paint);
                // Side mirror.
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x * 1.08f, belt + 0.12f, front - 0.15f), Quaternion.identity, new Vector3(0.05f, 0.18f, 0.12f), black);
            }
            // Back wall: solid below the window.
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, (CabFloor + belt) / 2f, back), Quaternion.identity, new Vector3(1.9f, belt - CabFloor, 0.05f), paint);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, (belt + roof) / 2f, back), Quaternion.identity, new Vector3(1.3f, roof - belt - 0.15f, 0.02f), glass);
            foreach (float x in new[] { -0.8f, 0.8f })
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x, (belt + roof) / 2f, back), Quaternion.identity, new Vector3(0.3f, roof - belt, 0.05f), paint);
            // Windscreen, raked back from the hood.
            float rake = Mathf.Atan2(0.32f, roof - belt) * Mathf.Rad2Deg;
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, (belt + roof) / 2f, front - 0.16f), Quaternion.Euler(-rake, 0f, 0f), new Vector3(1.86f, (roof - belt) / Mathf.Cos(rake * Mathf.Deg2Rad), 0.02f), glass);
            // Dashboard and bench.
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, belt - 0.12f, front - 0.15f), Quaternion.identity, new Vector3(1.85f, 0.32f, 0.32f), black);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, CabFloor + 0.2f, 0.05f), Quaternion.identity, new Vector3(1.75f, 0.4f, 0.55f), interior);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, CabFloor + 0.65f, -0.24f), Quaternion.Euler(-10f, 0f, 0f), new Vector3(1.75f, 0.65f, 0.14f), interior);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, CabFloor + 0.3f, front - 0.05f), Quaternion.identity, new Vector3(1.85f, 0.6f, 0.05f), black);
            // Running board under the doors.
            foreach (float x in new[] { -1f, 1f })
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x, 0.45f, middle), Quaternion.identity, new Vector3(0.18f, 0.05f, length), trim);
        }

        static void BuildFront(GameObject truck, Material paint, Material black, Material trim, Material headlight)
        {
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 1.035f, 1.97f), Quaternion.identity, new Vector3(1.92f, 0.43f, 1.35f), paint);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 0.7f, 1.97f), Quaternion.identity, new Vector3(1.4f, 0.3f, 1.35f), black);
            // Front wings over the wheels.
            foreach (float x in new[] { -0.84f, 0.84f })
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x, 0.84f, AxleZ), Quaternion.identity, new Vector3(0.34f, 0.06f, 0.95f), paint);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 0.97f, 2.65f), Quaternion.identity, new Vector3(1.3f, 0.32f, 0.04f), black);
            for (int bar = 0; bar < 3; bar++)
                AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 0.87f + bar * 0.1f, 2.67f), Quaternion.identity, new Vector3(1.3f, 0.025f, 0.03f), trim);
            foreach (float x in new[] { -0.75f, 0.75f })
                AddVisual(PrimitiveType.Cylinder, truck, new Vector3(x, 1.0f, 2.66f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.2f, 0.02f, 0.2f), headlight);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 0.66f, 2.72f), Quaternion.identity, new Vector3(2f, 0.18f, 0.14f), trim);
        }

        static void BuildBed(GameObject truck, Material paint, Material liner, Material trim, Material taillight, Material black)
        {
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 0.88f, -1.5f), Quaternion.identity, new Vector3(1.86f, 0.1f, 2.26f), liner);
            foreach (float x in new[] { -0.94f, 0.94f })
            {
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x, 1.08f, -1.5f), Quaternion.identity, new Vector3(0.07f, 0.5f, 2.3f), paint);
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x, 1.345f, -1.5f), Quaternion.identity, new Vector3(0.1f, 0.03f, 2.3f), trim);
                // Wheel arch inside the bed, and the wing outside.
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x * 0.82f, 1.0f, -AxleZ), Quaternion.identity, new Vector3(0.28f, 0.2f, 0.9f), liner);
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x * 0.92f, 0.84f, -AxleZ), Quaternion.identity, new Vector3(0.3f, 0.06f, 0.95f), paint);
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x * 0.96f, 1.15f, -2.64f), Quaternion.identity, new Vector3(0.08f, 0.2f, 0.05f), taillight);
            }
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 1.08f, -0.42f), Quaternion.identity, new Vector3(1.86f, 0.5f, 0.05f), paint);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 1.08f, -2.62f), Quaternion.identity, new Vector3(1.86f, 0.5f, 0.07f), paint);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 0.66f, -2.7f), Quaternion.identity, new Vector3(2f, 0.16f, 0.14f), trim);
            // Chassis rails underneath.
            foreach (float x in new[] { -0.45f, 0.45f })
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x, 0.55f, 0f), Quaternion.identity, new Vector3(0.1f, 0.14f, 5.2f), black);
        }

        /// <summary>
        /// The small things that make it read as a real truck: door seams, handles and window seals, wheel-arch
        /// flares and mud flaps, marker lights, number plates, wipers, an antenna, the exhaust and a tow hitch;
        /// inside, gauges, a radio, a gear lever, sun visors, a rear-view mirror and seat belts.
        /// </summary>
        static void BuildDetails(GameObject truck, Material paint, Material black, Material chrome, Material interior, Material headlight, Material taillight)
        {
            Material amber = GetOrCreateEmissiveMaterial("TruckMarker", new Color(1f, 0.55f, 0.1f));
            Material plate = GetOrCreateMaterial("NumberPlate", new Color(0.92f, 0.9f, 0.82f));
            Material gauge = GetOrCreateEmissiveMaterial("TruckGauge", new Color(0.55f, 0.85f, 0.6f));
            Material rubber = GetOrCreateMaterial("TruckRubber", new Color(0.03f, 0.03f, 0.03f), 0.1f);

            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * 0.978f;
                // Door shut lines, front and back, and along the sill.
                foreach (float z in new[] { 1.24f, -0.3f })
                    AddVisual(PrimitiveType.Cube, truck, new Vector3(x, 0.86f, z), Quaternion.identity, new Vector3(0.006f, 0.8f, 0.012f), rubber);
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x, 0.47f, 0.47f), Quaternion.identity, new Vector3(0.006f, 0.012f, 1.55f), rubber);
                // Handle, keyhole, the rubber seal at the bottom of the window.
                AddVisual(PrimitiveType.Cube, truck, new Vector3(side * 0.99f, 1.13f, -0.08f), Quaternion.identity, new Vector3(0.025f, 0.035f, 0.17f), chrome);
                AddVisual(PrimitiveType.Cube, truck, new Vector3(side * 0.983f, 1.25f, 0.47f), Quaternion.identity, new Vector3(0.02f, 0.025f, 1.6f), rubber);
                // Mirror arm.
                AddVisual(PrimitiveType.Cube, truck, new Vector3(side * 1.0f, 1.32f, 1.15f), Quaternion.identity, new Vector3(0.1f, 0.02f, 0.03f), black);
                // Black flares round the wheel arches.
                foreach (float axle in new[] { AxleZ, -AxleZ })
                    for (int k = 0; k < 5; k++)
                    {
                        float angle = Mathf.Lerp(20f, 160f, k / 4f) * Mathf.Deg2Rad;
                        var at = new Vector3(side * 0.93f, WheelRadius + 0.05f + Mathf.Sin(angle) * 0.47f, axle + Mathf.Cos(angle) * 0.47f);
                        AddVisual(PrimitiveType.Cube, truck, at, Quaternion.Euler(-(angle * Mathf.Rad2Deg - 90f), 0f, 0f), new Vector3(0.12f, 0.035f, 0.2f), black);
                    }
                // Mud flaps behind each wheel.
                AddVisual(PrimitiveType.Cube, truck, new Vector3(side * 0.84f, 0.3f, AxleZ - 0.52f), Quaternion.identity, new Vector3(0.26f, 0.32f, 0.015f), rubber);
                AddVisual(PrimitiveType.Cube, truck, new Vector3(side * 0.84f, 0.3f, -AxleZ - 0.52f), Quaternion.identity, new Vector3(0.26f, 0.32f, 0.015f), rubber);
                // Side markers: amber at the front, red at the back.
                AddVisual(PrimitiveType.Cube, truck, new Vector3(side * 0.965f, 0.98f, 2.45f), Quaternion.identity, new Vector3(0.02f, 0.05f, 0.1f), amber);
                AddVisual(PrimitiveType.Cube, truck, new Vector3(side * 0.98f, 1.0f, -2.45f), Quaternion.identity, new Vector3(0.02f, 0.05f, 0.1f), taillight);
                // Indicators beside the headlights, and chrome rings round them.
                AddVisual(PrimitiveType.Cube, truck, new Vector3(side * 0.9f, 0.86f, 2.655f), Quaternion.identity, new Vector3(0.12f, 0.06f, 0.02f), amber);
                AddVisual(PrimitiveType.Cylinder, truck, new Vector3(side * 0.75f, 1.0f, 2.652f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.24f, 0.01f, 0.24f), chrome);
                // Tie-down hooks on the bed rails.
                foreach (float z in new[] { -0.6f, -2.4f })
                    AddVisual(PrimitiveType.Cube, truck, new Vector3(side * 0.9f, 1.25f, z), Quaternion.identity, new Vector3(0.03f, 0.06f, 0.06f), chrome);
                // Wipers resting at the bottom of the windscreen.
                AddVisual(PrimitiveType.Cube, truck, new Vector3(side * 0.42f - 0.05f, 1.29f, 1.2f), Quaternion.Euler(-25f, side * 8f, 0f), new Vector3(0.6f, 0.015f, 0.02f), black);
                // Sun visors and seat belts inside.
                // Folded up flat against the roof, out of the way of the view.
                AddVisual(PrimitiveType.Cube, truck, new Vector3(side * 0.45f, 1.9f, 1.02f), Quaternion.identity, new Vector3(0.6f, 0.015f, 0.2f), interior);
                AddVisual(PrimitiveType.Cube, truck, new Vector3(side * 0.42f, 1.15f, -0.17f), Quaternion.Euler(0f, 0f, side * 35f), new Vector3(0.05f, 0.75f, 0.01f), black);
            }

            // Grille surround, hood bulge, the bonnet's front edge.
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 1.15f, 2.66f), Quaternion.identity, new Vector3(1.34f, 0.03f, 0.03f), chrome);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 0.8f, 2.66f), Quaternion.identity, new Vector3(1.34f, 0.03f, 0.03f), chrome);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 1.26f, 1.9f), Quaternion.identity, new Vector3(0.75f, 0.03f, 1.2f), paint);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 1.24f, 2.64f), Quaternion.identity, new Vector3(1.9f, 0.012f, 0.012f), black);
            // Number plates and the tailgate's handle and shut lines.
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 0.66f, 2.8f), Quaternion.identity, new Vector3(0.34f, 0.16f, 0.02f), plate);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 0.92f, -2.66f), Quaternion.identity, new Vector3(0.34f, 0.16f, 0.02f), plate);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 1.24f, -2.66f), Quaternion.identity, new Vector3(0.22f, 0.05f, 0.03f), black);
            foreach (float x in new[] { -0.92f, 0.92f })
                AddVisual(PrimitiveType.Cube, truck, new Vector3(x, 1.08f, -2.66f), Quaternion.identity, new Vector3(0.01f, 0.5f, 0.01f), rubber);
            // Roof markers and a third brake light.
            for (int i = -1; i <= 1; i++)
                AddVisual(PrimitiveType.Cube, truck, new Vector3(i * 0.3f, 1.99f, 1.12f), Quaternion.identity, new Vector3(0.1f, 0.03f, 0.05f), amber);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 1.9f, -0.39f), Quaternion.identity, new Vector3(0.35f, 0.05f, 0.03f), taillight);
            // Back window: a sliding pane's frame.
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 1.6f, -0.37f), Quaternion.identity, new Vector3(0.03f, 0.55f, 0.02f), black);
            // Antenna on the front wing, exhaust and tow hitch at the back.
            AddVisual(PrimitiveType.Cylinder, truck, new Vector3(0.9f, 1.65f, 1.55f), Quaternion.identity, new Vector3(0.008f, 0.4f, 0.008f), black);
            AddVisual(PrimitiveType.Cylinder, truck, new Vector3(0.62f, 0.38f, -2.6f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.07f, 0.2f, 0.07f), chrome);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 0.52f, -2.88f), Quaternion.identity, new Vector3(0.08f, 0.08f, 0.3f), black);
            AddVisual(PrimitiveType.Sphere, truck, new Vector3(0f, 0.6f, -3f), Quaternion.identity, Vector3.one * 0.06f, chrome);

            // The dashboard: two dials in front of the driver, a radio in the middle, a glovebox opposite.
            Quaternion facingDriver = Quaternion.Euler(-70f, 0f, 0f);
            foreach (float x in new[] { -0.52f, -0.32f })
            {
                AddVisual(PrimitiveType.Cylinder, truck, new Vector3(x, CabFloor + 0.63f, 1.0f), facingDriver, new Vector3(0.13f, 0.01f, 0.13f), black);
                AddVisual(PrimitiveType.Cylinder, truck, new Vector3(x, CabFloor + 0.632f, 0.995f), facingDriver, new Vector3(0.1f, 0.01f, 0.1f), gauge);
            }
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, CabFloor + 0.52f, 1.0f), Quaternion.Euler(-15f, 0f, 0f), new Vector3(0.24f, 0.07f, 0.02f), gauge);
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0.45f, CabFloor + 0.5f, 0.985f), Quaternion.identity, new Vector3(0.4f, 0.16f, 0.01f), interior);
            // Gear lever on the floor between the seats.
            AddVisual(PrimitiveType.Cylinder, truck, new Vector3(0.05f, CabFloor + 0.3f, 0.55f), Quaternion.Euler(-15f, 0f, 0f), new Vector3(0.02f, 0.3f, 0.02f), black);
            AddVisual(PrimitiveType.Sphere, truck, new Vector3(0.05f, CabFloor + 0.6f, 0.47f), Quaternion.identity, Vector3.one * 0.06f, black);
            // Rear-view mirror.
            AddVisual(PrimitiveType.Cube, truck, new Vector3(0f, 1.8f, 1.05f), Quaternion.identity, new Vector3(0.24f, 0.07f, 0.02f), black);
        }

        /// <summary>A tyre and hub. The returned transform is moved to the wheel's pose every frame.</summary>
        static Transform BuildWheel(GameObject truck, string name, Vector3 position, Material tyre, Material hub)
        {
            var wheel = new GameObject(name);
            wheel.transform.SetParent(truck.transform, false);
            wheel.transform.position = position;
            AddVisual(PrimitiveType.Cylinder, wheel, Vector3.zero, Quaternion.Euler(0f, 0f, 90f), new Vector3(WheelRadius * 2f, 0.13f, WheelRadius * 2f), tyre);
            // Tread: raised blocks round the tyre.
            const int blocks = 18;
            for (int i = 0; i < blocks; i++)
            {
                Quaternion around = Quaternion.Euler(i * 360f / blocks, 0f, 0f);
                AddVisual(PrimitiveType.Cube, wheel, around * new Vector3(0f, WheelRadius, 0f), around, new Vector3(0.135f, 0.02f, 0.07f), tyre);
            }
            AddVisual(PrimitiveType.Cylinder, wheel, Vector3.zero, Quaternion.Euler(0f, 0f, 90f), new Vector3(0.48f, 0.135f, 0.48f), hub);
            // Hub cap and five lug nuts on each face.
            foreach (float side in new[] { -1f, 1f })
            {
                AddVisual(PrimitiveType.Cylinder, wheel, new Vector3(side * 0.068f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.16f, 0.01f, 0.16f), hub);
                for (int i = 0; i < 5; i++)
                {
                    Quaternion around = Quaternion.Euler(i * 72f, 0f, 0f);
                    AddVisual(PrimitiveType.Cylinder, wheel, new Vector3(side * 0.072f, 0f, 0f) + around * new Vector3(0f, 0.1f, 0f), Quaternion.Euler(0f, 0f, 90f),
                        new Vector3(0.025f, 0.01f, 0.025f), tyre);
                }
            }
            return wheel.transform;
        }

        /// <summary>
        /// A rim of short segments with two spokes, on a column, facing the driver. It turns about its local Z
        /// (toward the driver).
        /// </summary>
        static Transform BuildSteeringWheel(GameObject truck, Material material)
        {
            var pivot = new GameObject("Steering Wheel");
            pivot.transform.SetParent(truck.transform, false);
            pivot.transform.SetLocalPositionAndRotation(new Vector3(-0.42f, CabFloor + 0.72f, 0.84f), Quaternion.LookRotation(new Vector3(0f, 0.55f, -1f)));
            const int segments = 16;
            const float radius = 0.19f;
            float segmentLength = 2f * Mathf.PI * radius / segments + 0.01f;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * 360f / segments;
                Quaternion around = Quaternion.Euler(0f, 0f, angle);
                AddVisual(PrimitiveType.Cube, pivot, around * new Vector3(radius, 0f, 0f), around, new Vector3(0.028f, segmentLength, 0.028f), material);
            }
            AddVisual(PrimitiveType.Cube, pivot, Vector3.zero, Quaternion.identity, new Vector3(radius * 2f, 0.03f, 0.02f), material);
            AddVisual(PrimitiveType.Cube, pivot, new Vector3(0f, -radius / 2f, 0f), Quaternion.identity, new Vector3(0.03f, radius, 0.02f), material);
            AddVisual(PrimitiveType.Cylinder, pivot, new Vector3(0f, 0f, 0.005f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.07f, 0.02f, 0.07f), material);
            AddVisual(PrimitiveType.Cylinder, pivot, new Vector3(0f, 0f, -0.2f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.05f, 0.2f, 0.05f), material);
            return pivot.transform;
        }

        static Material GetOrCreateGlassMaterial()
        {
            string path = $"{GeneratedFolder}/TruckGlass.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                SetGlassTint(material);
                return material;
            }
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            SetGlassTint(material);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
