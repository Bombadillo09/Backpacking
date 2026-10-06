using System;
using System.Collections.Generic;
using Backpacking.Camp;
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

        public List<string> collectedPickups = new();
        public List<string> visitedPoints = new();
        public List<BushState> bushes = new();
        public List<VendorState> vendors = new();
        public List<PlacedItemState> placedItems = new();
        /// <summary>Campsites cleared and brush cut with the machete: centre in xyz, radius in w.</summary>
        public List<Vector4> clearings = new();
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
    }
}
