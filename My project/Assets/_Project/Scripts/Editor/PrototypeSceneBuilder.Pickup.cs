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
            material.SetColor("_BaseColor", new Color(0.6f, 0.7f, 0.75f, 0.07f));
            material.SetFloat("_Smoothness", 0.92f);
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

        /// <summary>A tyre and hub. The returned transform is moved to the wheel's pose every frame.</summary>
        static Transform BuildWheel(GameObject truck, string name, Vector3 position, Material tyre, Material hub)
        {
            var wheel = new GameObject(name);
            wheel.transform.SetParent(truck.transform, false);
            wheel.transform.position = position;
            AddVisual(PrimitiveType.Cylinder, wheel, Vector3.zero, Quaternion.Euler(0f, 0f, 90f), new Vector3(WheelRadius * 2f, 0.13f, WheelRadius * 2f), tyre);
            AddVisual(PrimitiveType.Cylinder, wheel, Vector3.zero, Quaternion.Euler(0f, 0f, 90f), new Vector3(0.48f, 0.135f, 0.48f), hub);
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
