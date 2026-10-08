using System;
using System.Collections.Generic;
using Backpacking.Camp;
using Backpacking.Character;
using Backpacking.Survival;
using Backpacking.Trip;
using Backpacking.World;
using UnityEngine;

namespace Backpacking.Saving
{
    /// <summary>Everything written to the save file. Scene objects are referred to by their <see cref="SaveId"/>.</summary>
    [Serializable]
    public class SaveData
    {
        public int version = 1;
        /// <summary>Shown when offering to continue, e.g. "Day 3, 14:20 near Valley Crossing".</summary>
        public string summary;

        public int day;
        public float hour;

        public Vector3 playerPosition;
        public float playerYaw;

        public VitalsState vitals;
        public BackpackState backpack;
        public WeatherState weather;
        public TripState trip;
        public CharacterProfile character;
        /// <summary>The tutorial step in progress, or -1 if it's done or skipped.</summary>
        public int tutorialStep = -1;

        public List<string> collectedPickups = new();
        public List<string> visitedPoints = new();
        public List<BushState> bushes = new();
        public List<VendorState> vendors = new();
        public List<PlacedItemState> placedItems = new();
        /// <summary>Whether the pack is on your back, and where it and the tent bag lie if not.</summary>
        public PackState pack;
        /// <summary>Campsites cleared and brush cut with the machete: centre in xyz, radius in w.</summary>
        public List<Vector4> clearings = new();

        /// <summary>Where the pickup is, and whether you're driving it. Null in older saves: it's at home.</summary>
        public Vehicles.PickupState truck;
        /// <summary>What's in the truck bed. Null in older saves: it's empty.</summary>
        public BackpackState truckBed;
        /// <summary>How far through the run-up to the trail (home, store, packing, drive). Older saves are on the trail.</summary>
        public int arrivalPhase = (int)Trip.ArrivalPhase.OnTheTrail;
        /// <summary>The tutorial is waiting to start at the trailhead.</summary>
        public bool tutorialPending;
    }

    [Serializable]
    public class BushState
    {
        public string id;
        public float pickedAtHour;
    }

    [Serializable]
    public class VendorState
    {
        public string id;
        public int[] stock;
    }

    /// <summary>A tent, fire ring, stove or snare the player set up.</summary>
    [Serializable]
    public class PlacedItemState
    {
        public CampItem kind;
        public Vector3 position;
        public Quaternion rotation;
        public float fuelHours;
        public bool burning;
        public bool hasCatch;
        /// <summary>For a tent, how far it's pitched. Saves from before staged pitching were fully pitched.</summary>
        public TentStage stage = TentStage.Pitched;
        public ChairStage chairStage = ChairStage.Ready;
    }
}
