using System.Collections.Generic;
using Backpacking.Interaction;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.UI;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>
    /// Your tent on the ground, pitched the way a real one is: laid out flat, then the fibreglass poles set into
    /// their grommets, then the body clipped up and the rainfly staked out over it. Taking it down puts it back
    /// in its bag. Its look comes from <see cref="TentDesign"/> for the model you own.
    /// Once it's pitched you can crawl inside: you sit on the floor at the head end, look around, take your boots
    /// off, lay out your mat and unroll your sleeping bag from the pack in the porch, and get into the bag to sleep.
    /// Look at the doorway or your bedding for the options; crawl out through the door.
    /// </summary>
    public class Tent : MonoBehaviour, IInteractable
    {
        [SerializeField] TentMaterials materials;
        [SerializeField] float polesMinutes = 4f;
        [SerializeField] float fabricMinutes = 6f;
        [SerializeField] float takeDownMinutes = 8f;
        [SerializeField] float bagUpMinutes = 3f;
        [SerializeField] float layOutMinutes = 1f;

        /// <summary>Eye height sitting on the tent floor, in metres.</summary>
        const float SittingEyes = 0.72f;
        /// <summary>Sitting in a tent you can turn nearly all the way round, to look out of the door.</summary>
        const float SittingYawLimit = 170f;

        static Tent occupied;

        Transform visuals, seat, bed;
        GameObject doorway;
        Light glow;

        public TentModel Model { get; private set; }
        public TentStage Stage { get; private set; }
        public bool IsPitched => Stage == TentStage.Pitched;
        public TentMaterials Materials => materials;
        public bool MatLaidOut { get; private set; }
        public bool BagLaidOut { get; private set; }
        /// <summary>The tent you're sitting in, or null.</summary>
        public static Tent Occupied => occupied;
        public bool PlayerInside => occupied == this;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => occupied = null;

        void OnDestroy()
        {
            if (occupied == this)
                occupied = null;
        }

        public string DisplayName => Stage switch
        {
            TentStage.Pitched => TentDesign.Of(Model).Name,
            TentStage.Poled => "Tent (poles up)",
            _ => "Tent (laid out)",
        };

        /// <summary>Shows the tent as this model at this stage.</summary>
        public void Setup(TentModel model, TentStage stage)
        {
            Model = model;
            Stage = stage;
            if (visuals != null)
                Destroy(visuals.gameObject);
            visuals = new GameObject("Visuals").transform;
            visuals.SetParent(transform, false);
            Bounds bounds = TentDesign.Build(visuals, model, stage, materials);

            // Solid once it stands; flat fabric is just something to step over.
            var box = GetComponent<BoxCollider>();
            if (box == null)
                box = gameObject.AddComponent<BoxCollider>();
            float height = stage == TentStage.Pitched ? bounds.size.y : stage == TentStage.Poled ? bounds.size.y : 0.08f;
            box.center = new Vector3(bounds.center.x, height / 2f, bounds.center.z);
            box.size = new Vector3(bounds.size.x, height, bounds.size.z);
            box.isTrigger = stage == TentStage.Poled;
            BuildInterior();
        }

        public void GetOptions(Interactor interactor, List<InteractionOption> options)
        {
            PlayerActivity activity = interactor.Activity;
            switch (Stage)
            {
                case TentStage.LaidOut:
                    options.Add(new InteractionOption($"Assemble and set the poles ({polesMinutes:0} min)", () =>
                        activity.Begin("Snapping the poles together and setting them in the grommets", polesMinutes, () =>
                        {
                            Setup(Model, TentStage.Poled);
                            Notifications.Post("The poles are up. Next, raise the tent fabric onto them.", 4f);
                        })));
                    options.Add(new InteractionOption($"Roll it back into its bag ({bagUpMinutes:0} min)", () =>
                        activity.Begin("Rolling the tent into its bag", bagUpMinutes, () => BagUp(interactor)), CampOwner.PackProblem(gameObject)));
                    break;

                case TentStage.Poled:
                    options.Add(new InteractionOption($"Put up the tent fabric and stake it out ({fabricMinutes:0} min)", () =>
                        activity.Begin("Clipping the tent to the poles, throwing the rainfly over and staking it out", fabricMinutes, () =>
                        {
                            Setup(Model, TentStage.Pitched);
                            Notifications.Post("Your tent is pitched. Crawl inside to lay out your mat and sleeping bag.", 4f);
                            Trip.TripLog.Note($"Pitched the {TentDesign.Of(Model).Name}.");
                        })));
                    options.Add(new InteractionOption("Take the poles down (2 min)", () =>
                        activity.Begin("Taking the poles down", 2f, () => Setup(Model, TentStage.LaidOut))));
                    break;

                default:
                    if (PlayerInside)
                        return;
                    options.Add(new InteractionOption("Crawl inside", () => CrawlIn(interactor)));
                    options.Add(new InteractionOption("Crawl in and sleep", () => CrawlInAndSleep(interactor)));
                    // The tent's bulk is in the way of looking at a pack stowed in it, so it's reached through the tent.
                    if (PackStowed())
                        options.Add(new InteractionOption("Put your pack on", () => PackHandling.Current.PickUp()));
                    if (PackHandling.Current != null && PackHandling.Current.Pack != null && Model != TentModel.OnePerson)
                        options.Add(new InteractionOption("Bring your pack inside", () => PackHandling.Current.MovePack(PackSpot(), transform.rotation),
                            PackInside() ? "It's already inside" : null));
                    options.Add(new InteractionOption($"Take down the tent ({takeDownMinutes:0} min)", () =>
                        activity.Begin("Pulling the stakes, folding the fly and breaking down the poles", takeDownMinutes, () => BagUp(interactor)),
                        CampOwner.PackProblem(gameObject) ?? (MatLaidOut || BagLaidOut ? "Pack your mat and sleeping bag away first (crawl inside)" : null)));
                    break;
            }
        }

        // ---------- Inside ----------

        /// <summary>What you can do sitting inside: lay out and pack away your bedding, sleep, boots, crawl out.</summary>
        public void GetInsideOptions(Interactor interactor, List<InteractionOption> options)
        {
            Backpack backpack = interactor.Backpack;
            PlayerActivity activity = interactor.Activity;
            string packProblem = PackProblem();
            // A friend's tent: shelter for the night, but the bedding in it is theirs.
            if (CampOwner.IsOthers(gameObject, out string owner))
            {
                options.Add(new InteractionOption($"Sleep in {owner}'s tent, in your clothes", () => activity.Sleep(inTent: true, inBag: false, onMat: false)));
                AddBootsOption(options);
                options.Add(new InteractionOption("Crawl out", () => CrawlOut(interactor.GetComponent<FirstPersonController>())));
                return;
            }
            if (!MatLaidOut && backpack.HasMat)
                options.Add(new InteractionOption($"Lay out your {backpack.MatName.ToLowerInvariant()} ({layOutMinutes:0} min)", () =>
                    activity.Begin(backpack.MatRecovery >= 1.4f ? "Blowing up the mat" : "Unrolling the mat", layOutMinutes, () => LayOutMat(backpack, true)),
                    backpack.MatLaidOut ? "It's laid out in another tent" : packProblem));
            if (!BagLaidOut && backpack.HasSleepingBag)
                options.Add(new InteractionOption($"Unroll your sleeping bag ({layOutMinutes:0} min)", () =>
                    activity.Begin("Pulling the sleeping bag out of its stuff sack and shaking it out", layOutMinutes, () => LayOutBag(backpack, true)),
                    backpack.BagLaidOut ? "It's laid out in another tent" : packProblem));
            options.Add(new InteractionOption(BagLaidOut ? "Get into your sleeping bag and sleep" : "Sleep in your clothes", () =>
                activity.Sleep(inTent: true, inBag: BagLaidOut, onMat: MatLaidOut)));

            AddBootsOption(options);
            if (BagLaidOut)
                options.Add(new InteractionOption("Stuff the sleeping bag back in the pack", () =>
                    activity.Begin("Stuffing the sleeping bag into its sack", layOutMinutes, () => LayOutBag(backpack, false)), packProblem));
            if (MatLaidOut)
                options.Add(new InteractionOption("Roll up the mat and pack it", () =>
                    activity.Begin(backpack.MatRecovery >= 1.4f ? "Letting the air out of the mat and rolling it" : "Rolling up the mat", layOutMinutes,
                        () => LayOutMat(backpack, false)), packProblem));
            options.Add(new InteractionOption("Crawl out", () => CrawlOut(interactor.GetComponent<FirstPersonController>())));
        }

        static void AddBootsOption(List<InteractionOption> options)
        {
            RestMode rest = RestMode.Current;
            if (rest != null)
                options.Add(new InteractionOption(rest.BootsOff ? "Put your boots back on" : "Take your boots off", () =>
                {
                    rest.SetBootsOff(!rest.BootsOff);
                    Notifications.Post(rest.BootsOff ? "You unlace your boots and set them by the door. Your feet can rest and air." : "You pull your boots back on.", 3f);
                }));
        }

        /// <summary>Bedding comes out of and goes back into the pack, so it has to be in the porch (or inside) within reach.</summary>
        static string PackProblem()
        {
            PackHandling pack = PackHandling.Current;
            return pack == null || pack.CanReachPack ? null : "Your pack's out of reach: bring it to the tent";
        }

        void LayOutMat(Backpack backpack, bool laidOut)
        {
            MatLaidOut = laidOut;
            backpack.SetMatLaidOut(laidOut);
            BuildBed(backpack);
        }

        void LayOutBag(Backpack backpack, bool laidOut)
        {
            BagLaidOut = laidOut;
            backpack.SetBagLaidOut(laidOut);
            BuildBed(backpack);
        }

        /// <summary>Restores laid-out bedding from a save.</summary>
        public void RestoreBed(bool mat, bool bag, Backpack backpack)
        {
            MatLaidOut = mat && backpack.HasMat;
            BagLaidOut = bag && backpack.HasSleepingBag;
            BuildBed(backpack);
        }

        /// <summary>Shows a friend's bedding as they've laid it out (in the colours of <paramref name="looks"/>: what this game knows of).</summary>
        public void ShowBed(bool mat, bool bag, Backpack looks)
        {
            if (mat == MatLaidOut && bag == BagLaidOut && (bed != null) == (IsPitched && (mat || bag)))
                return;
            MatLaidOut = mat;
            BagLaidOut = bag;
            BuildBed(looks);
        }

        public void CrawlIn(Interactor interactor)
        {
            var player = interactor.GetComponent<FirstPersonController>();
            if (player == null || player.Mounted || !IsPitched)
                return;
            if (RestMode.SeatedNow)
                RestMode.Current.StandUp();
            // You don't take your pack in with you: it goes down in the porch (or inside, if there's room).
            PackHandling pack = PackHandling.Current;
            if (pack != null && pack.IsWorn)
                pack.SetDownAt(PackSpot(), transform.rotation);
            BuildInterior();
            occupied = this;
            SetGlow(true);
            player.MountAt(seat, new Vector3(0f, SittingEyes, 0f), SittingYawLimit, onGround: true);
            BuildBed(interactor.Backpack);
        }

        /// <summary>In you go, the mat and bag come out of the pack and get laid out, and you're asleep.</summary>
        void CrawlInAndSleep(Interactor interactor)
        {
            CrawlIn(interactor);
            if (!PlayerInside)
                return;
            if (CampOwner.IsOthers(gameObject, out _))
            {
                interactor.Activity.Sleep(inTent: true, inBag: false, onMat: false);
                return;
            }
            Backpack backpack = interactor.Backpack;
            bool mat = !MatLaidOut && backpack.HasMat && !backpack.MatLaidOut && PackProblem() == null;
            bool bag = !BagLaidOut && backpack.HasSleepingBag && !backpack.BagLaidOut && PackProblem() == null;
            if (!mat && !bag)
            {
                interactor.Activity.Sleep(inTent: true, inBag: BagLaidOut, onMat: MatLaidOut);
                return;
            }
            interactor.Activity.Begin("Laying out your bed", layOutMinutes * 2f, () =>
            {
                if (mat)
                    LayOutMat(backpack, true);
                if (bag)
                    LayOutBag(backpack, true);
                interactor.Activity.Sleep(inTent: true, inBag: BagLaidOut, onMat: MatLaidOut);
            });
        }

        public void CrawlOut(FirstPersonController player)
        {
            if (!PlayerInside || player == null)
                return;
            RestMode rest = RestMode.Current;
            if (rest != null && rest.BootsOff)
            {
                rest.SetBootsOff(false);
                Notifications.Post("You pull your boots on in the doorway.", 3f);
            }
            occupied = null;
            SetGlow(false);
            player.Dismount(ExitPosition, Quaternion.Euler(0f, transform.eulerAngles.y, 0f), keepLook: false);
        }

        /// <summary>
        /// Daylight through the fabric, tinted by the fly, so you can see inside: brighter by day, a dim glow at
        /// night (your headlamp, as it were). Only while you're in.
        /// </summary>
        void SetGlow(bool on)
        {
            if (glow == null)
            {
                var lightObject = new GameObject("Inside Light");
                lightObject.transform.SetParent(transform, false);
                lightObject.transform.localPosition = new Vector3(0f, 0.35f, 0.1f);
                glow = lightObject.AddComponent<Light>();
                glow.type = LightType.Point;
                glow.range = 3.2f;
                glow.shadows = LightShadows.None;
            }
            TentDesign.Spec spec = TentDesign.Of(Model);
            glow.color = Color.Lerp(spec.Fly, Color.white, 0.55f);
            World.TimeOfDay time = FindAnyObjectByType<World.TimeOfDay>();
            bool day = time == null || (time.Hour > 6.5f && time.Hour < 19.5f);
            glow.intensity = day ? 1f : 0.45f;
            glow.enabled = on;
        }

        /// <summary>Just outside the door, beside the porch: where you stand after crawling out.</summary>
        public Vector3 ExitPosition
        {
            get
            {
                TentDesign.Spec spec = TentDesign.Of(Model);
                Vector3 spot = transform.TransformPoint(new Vector3(-0.35f, 0f, spec.HalfLength * (1f + spec.Vestibule) + 0.55f));
                if (Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(transform))
                    spot = hit.point;
                return spot + Vector3.up * 0.05f;
            }
        }

        /// <summary>
        /// The seat you sit at inside (under the highest part, at the head end, facing down your bed) and a doorway
        /// behind it to look at for the inside options. Only once pitched.
        /// </summary>
        void BuildInterior()
        {
            if (!IsPitched)
            {
                if (doorway != null)
                    doorway.SetActive(false);
                return;
            }
            TentDesign.Spec spec = TentDesign.Of(Model);
            float headZ = Model == TentModel.OnePerson ? 0.5f : 0.2f;
            if (seat == null)
            {
                seat = new GameObject("Seat").transform;
                seat.SetParent(transform, false);
            }
            seat.SetLocalPositionAndRotation(new Vector3(BedX, 0.01f, headZ), Quaternion.Euler(0f, 180f, 0f));
            if (doorway == null)
            {
                doorway = new GameObject("Doorway");
                doorway.transform.SetParent(transform, false);
                var trigger = doorway.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                var part = doorway.AddComponent<TentPart>();
                part.tent = this;
                part.partName = "Tent door";
            }
            doorway.SetActive(true);
            doorway.transform.localPosition = new Vector3(0f, 0.4f, spec.HalfLength - 0.05f);
            var box = doorway.GetComponent<BoxCollider>();
            box.size = new Vector3(spec.HalfWidth * 1.6f, 0.8f, 0.12f);
        }

        /// <summary>Your bed lies along one side of a dome (the pack fits beside it), down the middle of the tunnel.</summary>
        float BedX => Model == TentModel.OnePerson ? 0f : -0.2f;

        /// <summary>The mat and sleeping bag as laid out on the floor, each something to look at for the inside options.</summary>
        void BuildBed(Backpack backpack)
        {
            if (bed != null)
                Destroy(bed.gameObject);
            bed = null;
            if (!IsPitched || (!MatLaidOut && !BagLaidOut))
                return;
            TentDesign.Spec spec = TentDesign.Of(Model);
            bed = new GameObject("Bed").transform;
            bed.SetParent(transform, false);
            const float length = 1.9f;
            float centreZ = -spec.HalfLength + 0.08f + length / 2f;
            bed.localPosition = new Vector3(BedX, 0f, centreZ);
            var tint = new MaterialPropertyBlock();
            float top = 0.01f;

            GameObject Part(string name, PrimitiveType type, Vector3 position, Quaternion rotation, Vector3 scale, Color colour)
            {
                GameObject part = GameObject.CreatePrimitive(type);
                part.name = name;
                part.transform.SetParent(bed, false);
                part.transform.SetLocalPositionAndRotation(position, rotation);
                part.transform.localScale = scale;
                var renderer = part.GetComponent<Renderer>();
                renderer.sharedMaterial = materials.inner;
                tint.SetColor("_BaseColor", colour);
                renderer.SetPropertyBlock(tint);
                var tentPart = part.AddComponent<TentPart>();
                tentPart.tent = this;
                tentPart.partName = name;
                return part;
            }

            if (MatLaidOut)
            {
                bool air = backpack.MatRecovery >= 1.4f;
                float thickness = air ? 0.065f : 0.018f;
                Color colour = air ? new Color(0.75f, 0.22f, 0.15f) : new Color(0.85f, 0.72f, 0.2f);
                Part("Sleeping mat", PrimitiveType.Cube, new Vector3(0f, thickness / 2f, 0f), Quaternion.identity, new Vector3(0.52f, thickness, 1.8f), colour);
                // Foam mats are ridged; air mats have baffles.
                for (int i = 0; i < 9; i++)
                {
                    GameObject ridge = Part("Sleeping mat", PrimitiveType.Cube, new Vector3(0f, thickness + 0.003f, -0.8f + i * 0.2f), Quaternion.identity,
                        new Vector3(0.5f, 0.006f, 0.03f), Color.Lerp(colour, Color.black, 0.25f));
                    Destroy(ridge.GetComponent<Collider>());
                }
                top = thickness;
            }
            if (BagLaidOut)
            {
                float comfort = backpack.SleepingBagComfort;
                Color colour = comfort <= -8f ? new Color(0.6f, 0.12f, 0.1f) : comfort <= 2f ? new Color(0.2f, 0.35f, 0.25f) : new Color(0.2f, 0.32f, 0.55f);
                colour = Color.Lerp(colour, new Color(0.15f, 0.15f, 0.18f), backpack.BagWetness * 0.6f);
                // A mummy bag lying flat: wide at the shoulders, tapering to the feet, with its hood at the head end.
                Part("Sleeping bag", PrimitiveType.Capsule, new Vector3(0f, top + 0.08f, -0.05f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.58f, 0.85f, 0.16f), colour);
                Part("Sleeping bag", PrimitiveType.Sphere, new Vector3(0f, top + 0.08f, 0.8f), Quaternion.identity, new Vector3(0.42f, 0.16f, 0.36f), Color.Lerp(colour, Color.black, 0.2f));
                Part("Sleeping bag", PrimitiveType.Cube, new Vector3(0.18f, top + 0.15f, 0f), Quaternion.identity, new Vector3(0.02f, 0.02f, 1.3f), Color.Lerp(colour, Color.white, 0.3f));
            }
        }

        // ---------- The pack ----------

        /// <summary>Where the pack goes overnight: inside if the tent has room for it, else in the vestibule.</summary>
        Vector3 PackSpot()
        {
            TentDesign.Spec spec = TentDesign.Of(Model);
            Vector3 local = Model == TentModel.OnePerson
                ? new Vector3(0.15f, 0f, spec.HalfLength + spec.Vestibule * spec.HalfLength * 0.45f)
                : new Vector3(spec.HalfWidth * 0.55f, 0f, -spec.HalfLength * 0.45f);
            return transform.TransformPoint(local);
        }

        bool PackStowed()
        {
            GroundPack pack = PackHandling.Current != null ? PackHandling.Current.Pack : null;
            return pack != null && GetComponent<BoxCollider>().bounds.Contains(pack.transform.position + Vector3.up * 0.1f);
        }

        bool PackInside()
        {
            GroundPack pack = PackHandling.Current != null ? PackHandling.Current.Pack : null;
            return pack != null && Vector3.Distance(pack.transform.position, PackSpot()) < 0.3f;
        }

        /// <summary>Back into its stuff sack, lying where the tent was. The pack goes in by hand afterwards.</summary>
        void BagUp(Interactor interactor)
        {
            if (PackHandling.Current != null)
                PackHandling.Current.DropTentBag(transform.position + transform.right * 0.3f, transform.rotation);
            Notifications.Post("The tent's in its bag. Put it back in your pack before you move on.", 4f);
            Destroy(gameObject);
        }
    }
}
