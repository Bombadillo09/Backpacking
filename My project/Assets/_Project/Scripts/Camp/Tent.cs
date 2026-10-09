using System;
using System.Collections.Generic;
using Backpacking.Interaction;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.UI;
using Backpacking.World;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>
    /// One sleeping place in a tent: whose it is, and their mat and sleeping bag if laid out there (with how they
    /// look, so friends see your bedding as it is).
    /// </summary>
    [Serializable]
    public class TentBed
    {
        /// <summary>Whose bed (<see cref="CampOwner.LocalKey"/> in their game); empty when nothing's laid out.</summary>
        public string key = "";
        public string name = "";
        public bool mat, bag;
        public bool airMat;
        public float bagComfort, bagWetness;

        public bool Empty => !mat && !bag;

        public TentBed Clone() => (TentBed)MemberwiseClone();
    }

    /// <summary>
    /// Your tent on the ground, pitched the way a real one is: laid out flat, then the fibreglass poles set into
    /// their grommets, then the body clipped up and the rainfly staked out over it. Taking it down puts it back
    /// in its bag. Its look comes from <see cref="TentDesign"/> for the model you own.
    /// Once it's pitched you can crawl inside: you sit on the floor at the head end of a bed, look around, take your
    /// boots off, lay out your mat and unroll your sleeping bag from the pack in the porch, and get into the bag to
    /// sleep. The dome and the mountain tent sleep two side by side: on a trip with friends, whoever crawls in takes
    /// the free bed and lays out their own bedding, and two in a tent sleep warmer. Look at the doorway or your
    /// bedding for the options; crawl out through the door.
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
        /// <summary>How much warmer it is with someone else in the tent, in °C.</summary>
        public const float SharedWarmth = 2f;

        static Tent occupied;
        static readonly List<Tent> all = new();

        Transform visuals, seat, bed;
        GameObject doorway;
        Light glow;
        readonly List<TentBed> beds = new();
        // Which bed you're at while inside, and which beds have a friend asleep in them (their bag looks filled).
        int mySlot = -1;
        bool[] sleepers = Array.Empty<bool>();
        Vitals vitals;

        public TentModel Model { get; private set; }
        public TentStage Stage { get; private set; }
        public bool IsPitched => Stage == TentStage.Pitched;
        public TentMaterials Materials => materials;
        /// <summary>Your own mat and sleeping bag are laid out in this tent.</summary>
        public bool MatLaidOut => MyBed is { mat: true };
        public bool BagLaidOut => MyBed is { bag: true };
        public IReadOnlyList<TentBed> Beds => beds;
        /// <summary>The tent you're sitting in, or null.</summary>
        public static Tent Occupied => occupied;
        public static IReadOnlyList<Tent> All => all;
        public bool PlayerInside => occupied == this;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            occupied = null;
            all.Clear();
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

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

            int sleeps = TentDesign.Sleeps(model);
            while (beds.Count < sleeps)
                beds.Add(new TentBed());
            if (beds.Count > sleeps)
                beds.RemoveRange(sleeps, beds.Count - sleeps);
            if (sleepers.Length != sleeps)
                sleepers = new bool[sleeps];
            BuildInterior();
            BuildBed();
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
                            Notifications.Post("The tent is pitched. Crawl inside to lay out your mat and sleeping bag.", 4f);
                            Trip.TripLog.Note($"Pitched the {TentDesign.Of(Model).Name}.");
                        })));
                    options.Add(new InteractionOption("Take the poles down (2 min)", () =>
                        activity.Begin("Taking the poles down", 2f, () => Setup(Model, TentStage.LaidOut))));
                    break;

                default:
                    if (PlayerInside)
                        return;
                    string room = FreeSlot() < 0 ? NoRoom() : null;
                    options.Add(new InteractionOption("Crawl inside", () => CrawlIn(interactor), room));
                    options.Add(new InteractionOption("Crawl in and sleep", () => CrawlInAndSleep(interactor), room));
                    // The tent's bulk is in the way of looking at a pack stowed in it, so it's reached through the tent.
                    if (PackStowed())
                        options.Add(new InteractionOption("Put your pack on", () => PackHandling.Current.PickUp()));
                    if (PackHandling.Current != null && PackHandling.Current.Pack != null && Model != TentModel.OnePerson)
                        options.Add(new InteractionOption("Bring your pack inside", () => PackHandling.Current.MovePack(PackSpot(), transform.rotation),
                            PackInside() ? "It's already inside" : null));
                    options.Add(new InteractionOption($"Take down the tent ({takeDownMinutes:0} min)", () =>
                        activity.Begin("Pulling the stakes, folding the fly and breaking down the poles", takeDownMinutes, () => BagUp(interactor)),
                        CampOwner.PackProblem(gameObject) ?? TakeDownProblem()));
                    break;
            }
        }

        /// <summary>Why it can't come down yet: bedding still laid out in it, or someone inside.</summary>
        string TakeDownProblem()
        {
            foreach (TentBed place in beds)
                if (!place.Empty)
                    return place.key == CampOwner.LocalKey ? "Pack your mat and sleeping bag away first (crawl inside)"
                        : $"{(string.IsNullOrEmpty(place.name) ? "A friend" : place.name)}'s bedding is still inside";
            return OthersInside() ? "Someone's inside" : null;
        }

        string NoRoom() => beds.Count == 1 ? "No room: it's a one-person tent" : "No room: both places are taken";

        // ---------- Beds ----------

        TentBed MyBed
        {
            get
            {
                foreach (TentBed place in beds)
                    if (place.key == CampOwner.LocalKey && !place.Empty)
                        return place;
                return null;
            }
        }

        /// <summary>
        /// Where you'd be inside: your own bed if your bedding's here; else, for the tent's owner the first place, for
        /// a friend the second; else any place with nothing laid out and nobody sitting in it. -1 if there's no room.
        /// </summary>
        int FreeSlot()
        {
            for (int i = 0; i < beds.Count; i++)
                if (beds[i].key == CampOwner.LocalKey && !beds[i].Empty)
                    return i;
            bool friendsTent = CampOwner.IsOthers(gameObject, out _);
            for (int n = 0; n < beds.Count; n++)
            {
                int i = friendsTent ? (n + 1) % beds.Count : n;
                if (beds[i].Empty && !OtherHikers.AnyWithin(transform.TransformPoint(SeatSpot(i)), 0.45f))
                    return i;
            }
            return -1;
        }

        /// <summary>Which place someone at <paramref name="position"/> is at (the nearest head end).</summary>
        public int SlotAt(Vector3 position)
        {
            int best = 0;
            float nearest = float.MaxValue;
            for (int i = 0; i < beds.Count; i++)
            {
                float distance = (transform.TransformPoint(SeatSpot(i)) - position).sqrMagnitude;
                if (distance < nearest)
                {
                    nearest = distance;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>True if <paramref name="position"/> is inside the tent, over its floor (not out in the porch).</summary>
        public bool Contains(Vector3 position)
        {
            if (!IsPitched)
                return false;
            TentDesign.Spec spec = TentDesign.Of(Model);
            Vector3 local = transform.InverseTransformPoint(position);
            return Mathf.Abs(local.x) < spec.HalfWidth + 0.05f && Mathf.Abs(local.z) < spec.HalfLength + 0.05f && local.y > -0.3f && local.y < spec.Height;
        }

        bool OthersInside()
        {
            foreach (OtherHiker other in OtherHikers.All)
                if (Contains(other.position))
                    return true;
            return false;
        }

        /// <summary>The beds as they are, to save or send.</summary>
        public List<TentBed> CaptureBeds() => beds.ConvertAll(place => place.Clone());

        /// <summary>
        /// The beds from a save or a friend's game. Your own bedding counts only if your pack says it's out
        /// (<paramref name="backpack"/>), and it shows as your bedding looks now.
        /// </summary>
        public void SetBeds(List<TentBed> saved, Backpack backpack)
        {
            for (int i = 0; i < beds.Count; i++)
            {
                TentBed place = saved != null && i < saved.Count && saved[i] != null ? saved[i].Clone() : new TentBed();
                if (place.key == CampOwner.LocalKey && backpack != null)
                {
                    place.mat &= backpack.HasMat;
                    place.bag &= backpack.HasSleepingBag;
                    Describe(place, backpack);
                }
                if (place.Empty)
                    place.key = place.name = "";
                beds[i] = place;
            }
            BuildBed();
        }

        /// <summary>A string that changes whenever the beds do, to notice a change worth sending.</summary>
        public static string Signature(List<TentBed> list)
        {
            if (list == null)
                return "";
            var text = new System.Text.StringBuilder();
            foreach (TentBed place in list)
                text.Append(place.key).Append(place.mat ? 'm' : '-').Append(place.bag ? 'b' : '-').Append(place.airMat ? 'a' : '-')
                    .Append(Mathf.RoundToInt(place.bagComfort)).Append(Mathf.RoundToInt(place.bagWetness * 10f)).Append('|');
            return text.ToString();
        }

        /// <summary>A friend is asleep in a place (their bag is shown filled out), or no longer.</summary>
        public void SetSleeper(int slot, bool asleep)
        {
            if (slot < 0 || slot >= sleepers.Length || sleepers[slot] == asleep)
                return;
            sleepers[slot] = asleep;
            BuildBed();
        }

        static void Describe(TentBed place, Backpack backpack)
        {
            place.airMat = backpack.MatRecovery >= 1.4f;
            place.bagComfort = backpack.SleepingBagComfort;
            place.bagWetness = backpack.BagWetness;
        }

        // ---------- Inside ----------

        /// <summary>What you can do sitting inside: lay out and pack away your bedding, sleep, boots, crawl out.</summary>
        public void GetInsideOptions(Interactor interactor, List<InteractionOption> options)
        {
            Backpack backpack = interactor.Backpack;
            PlayerActivity activity = interactor.Activity;
            string packProblem = PackProblem();
            TentBed mine = mySlot >= 0 && mySlot < beds.Count ? beds[mySlot] : null;
            bool mat = mine is { mat: true }, bag = mine is { bag: true };
            if (!mat && backpack.HasMat)
                options.Add(new InteractionOption($"Lay out your {backpack.MatName.ToLowerInvariant()} ({layOutMinutes:0} min)", () =>
                    activity.Begin(backpack.MatRecovery >= 1.4f ? "Blowing up the mat" : "Unrolling the mat", layOutMinutes, () => LayOutMat(backpack, true)),
                    backpack.MatLaidOut ? "It's laid out in another tent" : packProblem));
            if (!bag && backpack.HasSleepingBag)
                options.Add(new InteractionOption($"Unroll your sleeping bag ({layOutMinutes:0} min)", () =>
                    activity.Begin("Pulling the sleeping bag out of its stuff sack and shaking it out", layOutMinutes, () => LayOutBag(backpack, true)),
                    backpack.BagLaidOut ? "It's laid out in another tent" : packProblem));
            options.Add(new InteractionOption(bag ? "Get into your sleeping bag and sleep" : "Sleep in your clothes", () => Sleep(interactor)));
            AddBootsOption(options);
            if (bag)
                options.Add(new InteractionOption("Stuff the sleeping bag back in the pack", () =>
                    activity.Begin("Stuffing the sleeping bag into its sack", layOutMinutes, () => LayOutBag(backpack, false)), packProblem));
            if (mat)
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
            if (!SetMine(backpack, place => place.mat = laidOut))
                return;
            backpack.SetMatLaidOut(laidOut);
        }

        void LayOutBag(Backpack backpack, bool laidOut)
        {
            if (!SetMine(backpack, place => place.bag = laidOut))
                return;
            backpack.SetBagLaidOut(laidOut);
        }

        /// <summary>Changes your bedding at the place you're at; it's yours while anything of yours is laid out there.</summary>
        bool SetMine(Backpack backpack, Action<TentBed> change)
        {
            if (mySlot < 0 || mySlot >= beds.Count)
                return false;
            TentBed place = beds[mySlot];
            change(place);
            if (place.Empty)
                place.key = place.name = "";
            else
            {
                place.key = CampOwner.LocalKey;
                place.name = Trip.TripLog.HikerName;
                Describe(place, backpack);
            }
            BuildBed();
            return true;
        }

        void Sleep(Interactor interactor)
        {
            TentBed mine = mySlot >= 0 && mySlot < beds.Count ? beds[mySlot] : null;
            UpdateShelter();
            interactor.Activity.Sleep(inTent: true, inBag: mine is { bag: true }, onMat: mine is { mat: true });
        }

        void Update()
        {
            if (PlayerInside)
                UpdateShelter();
        }

        /// <summary>How much this tent keeps you warm tonight: its own shelter, and a little more with company.</summary>
        void UpdateShelter()
        {
            if (vitals == null)
                vitals = FindAnyObjectByType<Vitals>();
            if (vitals != null)
                vitals.SleepShelter = TentDesign.Shelter(Model) + (OthersInside() ? SharedWarmth : 0f);
        }

        public void CrawlIn(Interactor interactor)
        {
            var player = interactor.GetComponent<FirstPersonController>();
            if (player == null || player.Mounted || !IsPitched)
                return;
            int slot = FreeSlot();
            if (slot < 0)
            {
                Notifications.Post(NoRoom() + ".", 3f);
                return;
            }
            if (RestMode.SeatedNow)
                RestMode.Current.StandUp();
            mySlot = slot;
            // You don't take your pack in with you: it goes down in the porch (or inside, if there's room).
            PackHandling pack = PackHandling.Current;
            if (pack != null && pack.IsWorn && interactor.Backpack.HasPack)
                pack.SetDownAt(PackSpot(), transform.rotation);
            BuildInterior();
            occupied = this;
            SetGlow(true);
            UpdateShelter();
            player.MountAt(seat, new Vector3(0f, SittingEyes, 0f), SittingYawLimit, onGround: true);
            if (MyBed != null)
                Describe(MyBed, interactor.Backpack);
            BuildBed();
        }

        /// <summary>In you go, the mat and bag come out of the pack and get laid out, and you're asleep.</summary>
        void CrawlInAndSleep(Interactor interactor)
        {
            CrawlIn(interactor);
            if (!PlayerInside)
                return;
            Backpack backpack = interactor.Backpack;
            bool mat = !MatLaidOut && backpack.HasMat && !backpack.MatLaidOut && PackProblem() == null;
            bool bag = !BagLaidOut && backpack.HasSleepingBag && !backpack.BagLaidOut && PackProblem() == null;
            if (!mat && !bag)
            {
                Sleep(interactor);
                return;
            }
            interactor.Activity.Begin("Laying out your bed", layOutMinutes * 2f, () =>
            {
                if (mat)
                    LayOutMat(backpack, true);
                if (bag)
                    LayOutBag(backpack, true);
                Sleep(interactor);
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
            Vector3 exit = ExitPosition;
            occupied = null;
            mySlot = -1;
            SetGlow(false);
            player.Dismount(exit, Quaternion.Euler(0f, transform.eulerAngles.y, 0f), keepLook: false);
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

        /// <summary>
        /// Just outside the door, beside the porch: where you stand after crawling out (on your side of the door, so
        /// two of you don't come out on top of each other). On the ground itself, not on whoever's standing there.
        /// </summary>
        public Vector3 ExitPosition
        {
            get
            {
                TentDesign.Spec spec = TentDesign.Of(Model);
                float side = beds.Count > 1 && mySlot == 1 ? 0.45f : -0.45f;
                Vector3 spot = transform.TransformPoint(new Vector3(side, 0f, spec.HalfLength * (1f + spec.Vestibule) + 0.55f));
                spot.y = GroundCover.HeightAt(spot);
                return spot + Vector3.up * 0.05f;
            }
        }

        /// <summary>
        /// The seat you sit at inside (under the highest part, at the head end of your bed, facing down it) and a
        /// doorway behind it to look at for the inside options. Only once pitched.
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
            if (seat == null)
            {
                seat = new GameObject("Seat").transform;
                seat.SetParent(transform, false);
            }
            seat.SetLocalPositionAndRotation(SeatSpot(Mathf.Max(0, mySlot)), Quaternion.Euler(0f, 180f, 0f));
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

        /// <summary>Where you sit at a bed's head end, in the tent's space.</summary>
        Vector3 SeatSpot(int slot) => new(BedX(slot), 0.01f, Model == TentModel.OnePerson ? 0.5f : 0.2f);

        /// <summary>A bed lies down the middle of the tunnel; in the two-person tents, two lie side by side.</summary>
        float BedX(int slot) => beds.Count < 2 ? 0f : slot == 0 ? -0.28f : 0.28f;

        /// <summary>Everyone's mat and sleeping bag as laid out on the floor, each something to look at for the inside options.</summary>
        void BuildBed()
        {
            if (bed != null)
                Destroy(bed.gameObject);
            bed = null;
            if (!IsPitched)
                return;
            bool any = false;
            foreach (TentBed place in beds)
                any |= !place.Empty;
            if (!any)
                return;
            TentDesign.Spec spec = TentDesign.Of(Model);
            bed = new GameObject("Bed").transform;
            bed.SetParent(transform, false);
            const float length = 1.9f;
            float centreZ = -spec.HalfLength + 0.08f + length / 2f;
            var tint = new MaterialPropertyBlock();

            for (int slot = 0; slot < beds.Count; slot++)
            {
                TentBed place = beds[slot];
                if (place.Empty)
                    continue;
                var holder = new GameObject(string.IsNullOrEmpty(place.name) ? "Bed place" : $"{place.name}'s bed").transform;
                holder.SetParent(bed, false);
                holder.localPosition = new Vector3(BedX(slot), 0f, centreZ);
                string whose = place.key == CampOwner.LocalKey || string.IsNullOrEmpty(place.name) ? "" : $"{place.name}'s ";

                GameObject Part(string name, PrimitiveType type, Vector3 position, Quaternion rotation, Vector3 scale, Color colour)
                {
                    GameObject part = GameObject.CreatePrimitive(type);
                    part.name = name;
                    part.transform.SetParent(holder, false);
                    part.transform.SetLocalPositionAndRotation(position, rotation);
                    part.transform.localScale = scale;
                    var renderer = part.GetComponent<Renderer>();
                    renderer.sharedMaterial = materials.inner;
                    tint.SetColor("_BaseColor", colour);
                    renderer.SetPropertyBlock(tint);
                    var tentPart = part.AddComponent<TentPart>();
                    tentPart.tent = this;
                    tentPart.partName = whose.Length > 0 ? whose + name.ToLowerInvariant() : name;
                    return part;
                }

                float top = 0.01f;
                if (place.mat)
                {
                    float thickness = place.airMat ? 0.065f : 0.018f;
                    Color colour = place.airMat ? new Color(0.75f, 0.22f, 0.15f) : new Color(0.85f, 0.72f, 0.2f);
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
                if (place.bag)
                {
                    float comfort = place.bagComfort;
                    Color colour = comfort <= -8f ? new Color(0.6f, 0.12f, 0.1f) : comfort <= 2f ? new Color(0.2f, 0.35f, 0.25f) : new Color(0.2f, 0.32f, 0.55f);
                    colour = Color.Lerp(colour, new Color(0.15f, 0.15f, 0.18f), place.bagWetness * 0.6f);
                    // Someone asleep in it fills it out.
                    float filled = slot < sleepers.Length && sleepers[slot] ? 1.6f : 1f;
                    // A mummy bag lying flat: wide at the shoulders, tapering to the feet, with its hood at the head end.
                    Part("Sleeping bag", PrimitiveType.Capsule, new Vector3(0f, top + 0.08f * filled, -0.05f), Quaternion.Euler(90f, 0f, 0f),
                        new Vector3(0.58f, 0.85f, 0.16f * filled), colour);
                    Part("Sleeping bag", PrimitiveType.Sphere, new Vector3(0f, top + 0.08f * filled, 0.8f), Quaternion.identity,
                        new Vector3(0.42f, 0.16f * filled, 0.36f), Color.Lerp(colour, Color.black, 0.2f));
                    Part("Sleeping bag", PrimitiveType.Cube, new Vector3(0.18f, top + 0.15f * filled, 0f), Quaternion.identity,
                        new Vector3(0.02f, 0.02f, 1.3f), Color.Lerp(colour, Color.white, 0.3f));
                }
            }
        }

        // ---------- The pack ----------

        /// <summary>
        /// Where the pack goes overnight: inside beside your bed if the tent has room for it, else in the vestibule
        /// (on your side of the door when you're sharing).
        /// </summary>
        Vector3 PackSpot()
        {
            TentDesign.Spec spec = TentDesign.Of(Model);
            int slot = Mathf.Max(0, mySlot);
            bool sharing = beds.Count > 1 && (!beds[1 - Mathf.Clamp(slot, 0, 1)].Empty || OthersInside());
            Vector3 local = Model == TentModel.OnePerson ? new Vector3(0.15f, 0f, spec.HalfLength + spec.Vestibule * spec.HalfLength * 0.45f)
                : sharing ? new Vector3(slot == 0 ? -0.3f : 0.3f, 0f, spec.HalfLength + spec.Vestibule * spec.HalfLength * 0.45f)
                : new Vector3(spec.HalfWidth * 0.55f * (slot == 0 ? 1f : -1f), 0f, -spec.HalfLength * 0.45f);
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
