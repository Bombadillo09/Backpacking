using System;
using System.Collections.Generic;
using Backpacking.Interaction;
using Backpacking.Navigation;
using Backpacking.Player;
using Backpacking.Survival;
using Backpacking.World;
using UnityEngine;

namespace Backpacking.Trip
{
    public enum TripStat
    {
        Fish,
        Rabbits,
        BerryPicks,
        NightsInTent,
        NightsOutside,
        Collapses,
        Rescues,
        Sicknesses,
        // New stats go at the end: saves store them by this number.
        Deer,
        ArrowsShot,
    }

    [Serializable]
    public class JournalEntry
    {
        public int day;
        public float hour;
        public string text;
    }

    /// <summary>Everything the journal remembers. Saved with the trip.</summary>
    [Serializable]
    public class TripState
    {
        public List<JournalEntry> entries = new();
        public int[] stats = new int[Enum.GetValues(typeof(TripStat)).Length];
        public float startTotalHours;
        public float distance, dayDistance;
        public float coldestFelt = float.MaxValue, dayColdestFelt = float.MaxValue;
        public float highestAltitude;
        public int dayFish, dayRabbits;
        public bool finished;
        public float finishedTotalHours;
    }

    /// <summary>
    /// The thru-hike's journal and statistics. Writes entries as things happen (arrivals, nights, first
    /// catches, close calls, storms) and a summary at the end of each day, and tracks distance walked, the
    /// coldest it got and the highest point reached. Signing the summit register finishes the trip.
    /// Other systems report catches with <see cref="Tally"/>.
    /// </summary>
    public class TripLog : MonoBehaviour
    {
        public const string Destination = "Lookout Summit";
        /// <summary>The outdoor store on the road in, where the kit is bought.</summary>
        public const string Outfitter = "Pine Hollow Outfitters";

        [SerializeField] TimeOfDay timeOfDay;
        [SerializeField] FirstPersonController player;
        [SerializeField] Vitals vitals;
        [SerializeField] PlayerActivity activity;
        [SerializeField] AmbientTemperature temperature;
        [SerializeField] WeatherSystem weather;
        [Tooltip("Ignore jumps further than this in one frame (rescues, loading), in metres.")]
        [SerializeField] float teleportDistance = 15f;

        static TripLog current;

        TripState state = new();
        Vector3 lastPosition;
        bool wasSleeping, wasSick, wasHypothermic;
        float sleepStartHours, nightLowest;
        int warnedColdOnDay;
        WeatherKind lastWeather;

        /// <summary>Raised when the register is signed.</summary>
        public static event Action Finished;

        public static TripLog Current => current;
        /// <summary>The hiker's name, for the journal and the summit register.</summary>
        public static string HikerName { get; set; } = "";
        public IReadOnlyList<JournalEntry> Entries => state.entries;
        public bool IsFinished => state.finished;
        public float DistanceKm => state.distance / 1000f;
        public float ColdestFelt => state.coldestFelt;
        public float HighestAltitude => state.highestAltitude;
        public int Stat(TripStat stat) => state.stats[(int)stat];

        /// <summary>Whole or part days on the trail, counting from setting out to now (or to the finish).</summary>
        public int DaysOnTrail
        {
            get
            {
                float end = state.finished ? state.finishedTotalHours : timeOfDay.TotalHours;
                return Mathf.Max(1, Mathf.CeilToInt((end - state.startTotalHours) / 24f));
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            current = null;
            Finished = null;
            HikerName = "";
        }

        /// <summary>Writes a line in the journal, stamped with the current day and time.</summary>
        public static void Note(string text) => current?.AddEntry(text);

        /// <summary>Counts a catch or event toward the trip's statistics.</summary>
        public static void Tally(TripStat stat, int amount = 1)
        {
            if (current == null)
                return;
            TripState s = current.state;
            bool first = s.stats[(int)stat] == 0;
            s.stats[(int)stat] += amount;
            if (stat == TripStat.Fish)
                s.dayFish += amount;
            if (stat == TripStat.Rabbits)
                s.dayRabbits += amount;
            if (first)
            {
                string firstTime = stat switch
                {
                    TripStat.Fish => "Caught my first trout of the trip.",
                    TripStat.Rabbits => "First rabbit in a snare. Dinner sorted.",
                    TripStat.BerryPicks => "Found berries and picked a handful.",
                    TripStat.Deer => "Took my first deer. More meat than I could ever carry.",
                    _ => null,
                };
                if (firstTime != null)
                    Note(firstTime);
            }
        }

        void Awake()
        {
            current = this;
            state.startTotalHours = timeOfDay.TotalHours;
            lastPosition = player.transform.position;
        }

        void OnDestroy()
        {
            if (current == this)
                current = null;
        }

        void OnEnable()
        {
            NavigationPoint.Arrived += OnArrived;
            vitals.Collapsed += OnCollapsed;
            vitals.Incapacitated += OnIncapacitated;
            activity.WokeUp += OnWokeUp;
            timeOfDay.DayStarted += OnDayStarted;
        }

        void OnDisable()
        {
            NavigationPoint.Arrived -= OnArrived;
            vitals.Collapsed -= OnCollapsed;
            vitals.Incapacitated -= OnIncapacitated;
            activity.WokeUp -= OnWokeUp;
            timeOfDay.DayStarted -= OnDayStarted;
        }

        void Start()
        {
            if (weather != null)
                lastWeather = weather.Current;
        }

        void AddEntry(string text) => state.entries.Add(new JournalEntry { day = timeOfDay.Day, hour = timeOfDay.Hour, text = text });

        void Update()
        {
            if (Time.deltaTime <= 0f)
                return;

            Vector3 position = player.transform.position;
            Vector3 moved = position - lastPosition;
            moved.y = 0f;
            lastPosition = position;
            if (moved.magnitude < teleportDistance && !activity.IsSleeping)
            {
                state.distance += moved.magnitude;
                state.dayDistance += moved.magnitude;
            }

            state.highestAltitude = Mathf.Max(state.highestAltitude, temperature.GetAltitude(position));
            if (!activity.IsSleeping)
            {
                state.coldestFelt = Mathf.Min(state.coldestFelt, vitals.FeltTemperature);
                state.dayColdestFelt = Mathf.Min(state.dayColdestFelt, vitals.FeltTemperature);
            }

            TrackSleep();
            TrackCondition();
            TrackWeather();
        }

        void TrackSleep()
        {
            bool sleeping = activity.IsSleeping;
            if (sleeping && !wasSleeping)
            {
                sleepStartHours = timeOfDay.TotalHours;
                nightLowest = float.MaxValue;
            }
            if (sleeping)
                nightLowest = Mathf.Min(nightLowest, temperature.GetTemperature(player.transform.position));
            wasSleeping = sleeping;
        }

        void TrackCondition()
        {
            if (vitals.IsSick && !wasSick)
            {
                Tally(TripStat.Sicknesses);
                Note("Came down sick. Probably the water. Should have boiled it.");
            }
            wasSick = vitals.IsSick;

            if (vitals.IsHypothermic && !wasHypothermic && warnedColdOnDay != timeOfDay.Day)
            {
                warnedColdOnDay = timeOfDay.Day;
                Note("Dangerously cold. Couldn't stop shivering.");
            }
            wasHypothermic = vitals.IsHypothermic;
        }

        void TrackWeather()
        {
            if (weather == null || weather.Current == lastWeather)
                return;
            lastWeather = weather.Current;
            string line = lastWeather switch
            {
                WeatherKind.Storm => "A storm rolled in.",
                WeatherKind.Rain => weather.IsSnowing ? "It started to snow." : "It started to rain.",
                WeatherKind.Fog => "Fog came down.",
                _ => null,
            };
            if (line != null)
                Note(line);
        }

        void OnArrived(NavigationPoint point)
        {
            string line = point.Kind switch
            {
                NavigationPointKind.TradingPost => $"Reached {point.DisplayName}. Time to restock.",
                NavigationPointKind.Summit => $"Made it to the top of {point.DisplayName}. Sign the register to finish.",
                _ => $"Passed the cairn at {point.DisplayName}.",
            };
            Note(line);
        }

        void OnCollapsed()
        {
            Tally(TripStat.Collapses);
            Note("Collapsed from exhaustion and woke up on the ground.");
        }

        void OnIncapacitated()
        {
            Tally(TripStat.Rescues);
            Note("Too far gone to go on. A search party had to carry me out.");
        }

        void OnWokeUp(bool inTent)
        {
            // Only a proper night counts, not a nap or a cold start after an hour.
            float slept = timeOfDay.TotalHours - sleepStartHours;
            if (slept < 4f)
            {
                if (slept > 0.5f && vitals.Warmth < 25f)
                    Note("Too cold to sleep. Woke up shivering.");
                return;
            }
            Tally(inTent ? TripStat.NightsInTent : TripStat.NightsOutside);
            string where = NearestPlace();
            string low = nightLowest < float.MaxValue ? $" Low of {nightLowest:0} °C." : "";
            Note(inTent ? $"Spent the night in the tent {where}.{low}" : $"Spent the night out in the open {where}.{low}");
        }

        void OnDayStarted(int newDay)
        {
            TripState s = state;
            var line = $"Day {newDay - 1} done: walked {s.dayDistance / 1000f:0.0} km";
            if (s.dayColdestFelt < float.MaxValue)
                line += $", coldest it felt was {s.dayColdestFelt:0} °C";
            if (s.dayFish + s.dayRabbits > 0)
                line += $", caught {Describe(s.dayFish, "trout")}{(s.dayFish > 0 && s.dayRabbits > 0 ? " and " : "")}{Describe(s.dayRabbits, "rabbit")}";
            // Stamp it as the end of the day that just finished.
            s.entries.Add(new JournalEntry { day = newDay - 1, hour = 23.99f, text = line + "." });
            s.dayDistance = 0f;
            s.dayColdestFelt = float.MaxValue;
            s.dayFish = s.dayRabbits = 0;
        }

        static string Describe(int count, string thing) => count == 0 ? "" : count == 1 ? $"one {thing}" : $"{count} {thing}s";

        string NearestPlace()
        {
            NavigationPoint nearest = null;
            float best = float.MaxValue;
            foreach (NavigationPoint point in NavigationPoint.All)
            {
                float distance = Vector3.Distance(point.transform.position, player.transform.position);
                if (distance < best)
                {
                    best = distance;
                    nearest = point;
                }
            }
            return nearest != null && best < 500f ? $"near {nearest.DisplayName}" : "in the backcountry";
        }

        /// <summary>Signs the summit register: the thru-hike is complete.</summary>
        public void Finish()
        {
            if (state.finished)
                return;
            state.finished = true;
            state.finishedTotalHours = timeOfDay.TotalHours;
            string signature = string.IsNullOrEmpty(HikerName) ? "" : $" as {HikerName}";
            Note($"Signed the register at {Destination}{signature}. Thru-hike complete in {DaysOnTrail} days and {DistanceKm:0.0} km.");
            Finished?.Invoke();
        }

        /// <summary>A name for how the hike went, for the end screen.</summary>
        public string TrailTitle()
        {
            int rescues = Stat(TripStat.Rescues);
            int collapses = Stat(TripStat.Collapses);
            if (rescues >= 3)
                return "Frequent Flyer: the rescue team knows you by name";
            if (rescues > 0)
                return "Survivor: you made it, with a little help";
            if (collapses > 0)
                return "Stubborn: you kept getting back up";
            if (DaysOnTrail <= 4)
                return "Mountain Goat: fast, fit and self-reliant";
            return "Thru-Hiker: every step on your own two feet";
        }

        public TripState CaptureState() => JsonUtility.FromJson<TripState>(JsonUtility.ToJson(state));

        /// <summary>Wipes the journal for a new trip and writes its first line.</summary>
        public void BeginTrip(string firstLine)
        {
            state = new TripState { startTotalHours = timeOfDay.TotalHours };
            lastPosition = player.transform.position;
            Note(firstLine);
        }

        public void RestoreState(TripState saved)
        {
            if (saved == null)
                return;
            state = saved;
            int statCount = Enum.GetValues(typeof(TripStat)).Length;
            if (state.stats == null || state.stats.Length < statCount)
                Array.Resize(ref state.stats, statCount);
            state.entries ??= new List<JournalEntry>();
            lastPosition = player.transform.position;
        }
    }
}
