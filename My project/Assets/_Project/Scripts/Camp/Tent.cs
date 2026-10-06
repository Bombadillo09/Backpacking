using System.Collections.Generic;
using Backpacking.Interaction;
using Backpacking.UI;
using UnityEngine;

namespace Backpacking.Camp
{
    /// <summary>
    /// Your tent on the ground, pitched the way a real one is: laid out flat, then the fibreglass poles set into
    /// their grommets, then the body clipped up and the rainfly staked out over it. Taking it down puts it back
    /// in its bag. Its look comes from <see cref="TentDesign"/> for the model you own.
    /// </summary>
    public class Tent : MonoBehaviour, IInteractable
    {
        [SerializeField] TentMaterials materials;
        [SerializeField] float polesMinutes = 4f;
        [SerializeField] float fabricMinutes = 6f;
        [SerializeField] float takeDownMinutes = 8f;
        [SerializeField] float bagUpMinutes = 3f;

        Transform visuals;

        public TentModel Model { get; private set; }
        public TentStage Stage { get; private set; }
        public bool IsPitched => Stage == TentStage.Pitched;
        public TentMaterials Materials => materials;

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
                        activity.Begin("Rolling the tent into its bag", bagUpMinutes, () => BagUp(interactor))));
                    break;

                case TentStage.Poled:
                    options.Add(new InteractionOption($"Put up the tent fabric and stake it out ({fabricMinutes:0} min)", () =>
                        activity.Begin("Clipping the tent to the poles, throwing the rainfly over and staking it out", fabricMinutes, () =>
                        {
                            Setup(Model, TentStage.Pitched);
                            Notifications.Post("Your tent is pitched.", 3f);
                            Trip.TripLog.Note($"Pitched the {TentDesign.Of(Model).Name}.");
                        })));
                    options.Add(new InteractionOption("Take the poles down (2 min)", () =>
                        activity.Begin("Taking the poles down", 2f, () => Setup(Model, TentStage.LaidOut))));
                    break;

                default:
                    options.Add(new InteractionOption("Sleep", () =>
                    {
                        // You don't sleep with your pack on: it goes down by the door (or inside, if there's room).
                        PackHandling pack = PackHandling.Current;
                        if (pack != null && pack.IsWorn)
                            pack.SetDownAt(PackSpot(), transform.rotation);
                        activity.Sleep(inTent: true);
                    }));
                    // The tent's bulk is in the way of looking at a pack stowed in it, so it's reached through the tent.
                    if (PackStowed())
                        options.Add(new InteractionOption("Put your pack on", () => PackHandling.Current.PickUp()));
                    if (PackHandling.Current != null && PackHandling.Current.Pack != null && Model != TentModel.OnePerson)
                        options.Add(new InteractionOption("Bring your pack inside", () => PackHandling.Current.MovePack(PackSpot(), transform.rotation),
                            PackInside() ? "It's already inside" : null));
                    options.Add(new InteractionOption($"Take down the tent ({takeDownMinutes:0} min)", () =>
                        activity.Begin("Pulling the stakes, folding the fly and breaking down the poles", takeDownMinutes, () => BagUp(interactor))));
                    break;
            }
        }

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
