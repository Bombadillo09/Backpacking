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

        /// <summary>Friends' hikers from co-op on this trip, so they come back as they were when they rejoin.</summary>
        public List<HikerSave> guests = new();
    }

    /// <summary>One hiker on their own: where they are, how they are, and what they carry. A co-op guest's, kept in the host's save.</summary>
    [Serializable]
    public class HikerSave
    {
        /// <summary>Who they are (<see cref="Camp.CampOwner.LocalKey"/> in their game).</summary>
        public string key;
        public Vector3 position;
        public float yaw;
        public VitalsState vitals;
        public BackpackState backpack;
        public TripState trip;
        public CharacterProfile character;
        public PackState pack;
        public List<string> visitedPoints = new();
        public int arrivalPhase = (int)Trip.ArrivalPhase.OnTheTrail;
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
        /// <summary>For a pitched tent, whether your mat and sleeping bag are laid out in it.</summary>
        public bool matLaidOut, bagLaidOut;
        /// <summary>For a pitched tent, whose bedding is laid out in each place. Empty in older saves (see matLaidOut).</summary>
        public List<TentBed> beds = new();
        /// <summary>For a tent, which model (-1 in older saves: the one in your pack).</summary>
        public int tentModel = -1;
        /// <summary>Whose it is: empty for the save's own hiker, else a co-op friend's key and name.</summary>
        public string owner = "", ownerName = "";
    }
}
