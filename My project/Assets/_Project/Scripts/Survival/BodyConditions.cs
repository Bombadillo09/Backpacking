using System.Collections.Generic;
using UnityEngine;

namespace Backpacking.Survival
{
    public enum BodyPart
    {
        Head,
        Torso,
        Arms,
        Legs,
        Feet,
    }

    /// <summary>Something wrong, or going wrong, with part of the body.</summary>
    public readonly struct BodyCondition
    {
        public readonly string Name;
        public readonly BodyPart Part;
        /// <summary>1 developing, 2 a problem, 3 serious.</summary>
        public readonly int Severity;
        public readonly string Description;
        public readonly string Treatment;

        public BodyCondition(string name, BodyPart part, int severity, string description, string treatment)
        {
            Name = name;
            Part = part;
            Severity = severity;
            Description = description;
            Treatment = treatment;
        }
    }

    /// <summary>Reads the vitals into a list of conditions by body part, for the journal's Status page.</summary>
    public static class BodyConditions
    {
        public static List<BodyCondition> Evaluate(Vitals vitals, Backpack backpack)
        {
            var list = new List<BodyCondition>();

            // ---------- Feet ----------
            if (vitals.IsInfected)
                list.Add(new BodyCondition("Foot infection", BodyPart.Feet, vitals.Infection > 50f ? 3 : 2,
                    vitals.OnAntibiotics ? "Red, hot and swollen. The antibiotics are working on it."
                        : vitals.Infection > 50f ? "Spreading. It's draining your strength and will start to harm your health."
                        : "Red, hot and swollen. Blisters won't heal while it lasts, and it will get worse.",
                    "Take antibiotics (Backpack > First aid), or sit by a lit campfire (Z) with your boots off (E) to hold your feet in the smoke."));
            else if (vitals.InfectionRisk > 0.3f)
                list.Add(new BodyCondition("Infection brewing", BodyPart.Feet, vitals.InfectionRisk > 0.7f ? 2 : 1,
                    $"Your feet have been {(vitals.FeetWet && vitals.HasBlisters ? "wet and blistered" : vitals.FeetWet ? "wet" : "blistered")} too long. "
                    + $"An infection is {Mathf.RoundToInt(vitals.InfectionRisk * 100f)}% of the way to setting in.",
                    "Air and dry them: sit down (Z) with your boots off (E), by a fire if you can. Waterproof boots keep them dry."));

            if (vitals.Feet < 70f)
                list.Add(new BodyCondition(vitals.Feet < 40f ? "Raw feet" : "Blisters", BodyPart.Feet, vitals.Feet < 40f ? 3 : 2,
                    vitals.Feet < 40f ? "Every step is agony. You're limping badly and can't run." : "Every step stings. You're limping and slower.",
                    "Sit with your boots off (Z, then E) and sleep: both heal them. Better boots blister less."));
            if (vitals.FootStrain > 35f)
                list.Add(new BodyCondition(vitals.FootStrain > 60f ? "Aching feet" : "Tired feet", BodyPart.Feet, vitals.FootStrain > 60f ? 2 : 1,
                    vitals.FootStrain > 60f ? "Keep walking on them and you'll get blisters." : "You've been on your feet a while.",
                    "Stop and rest: sit down (Z) and take your boots off (E)."));
            if (vitals.FeetWet)
                list.Add(new BodyCondition("Wet feet", BodyPart.Feet, 1,
                    "Your boots are letting water in. Wet feet blister and get infected.",
                    "Sit with your boots off to dry them, best by a fire. Waterproof boots stay dry."));

            // ---------- Head ----------
            if (vitals.Hydration < 30f)
                list.Add(new BodyCondition(vitals.IsDehydrated ? "Dehydrated" : "Thirsty", BodyPart.Head, vitals.IsDehydrated ? 3 : 1,
                    vitals.IsDehydrated ? "Headache and dizziness. You're losing health." : "Your mouth is dry.",
                    "Drink (Backpack). Boil or filter lake water first."));
            if (vitals.Energy < 30f)
                list.Add(new BodyCondition(vitals.IsExhausted ? "Exhausted" : "Tired", BodyPart.Head, vitals.IsExhausted ? 3 : 1,
                    vitals.IsExhausted ? "You'll collapse soon." : "Your eyelids are heavy.",
                    "Sleep, ideally in your tent."));

            // ---------- Torso ----------
            if (vitals.Warmth < 40f)
                list.Add(new BodyCondition(vitals.IsHypothermic ? "Hypothermia" : vitals.Warmth < 20f ? "Very cold" : "Cold", BodyPart.Torso,
                    vitals.IsHypothermic ? 3 : vitals.Warmth < 20f ? 2 : 1,
                    vitals.IsHypothermic ? "Your body can't keep itself warm. You're losing health fast." : "Shivering.",
                    "Put on layers, get dry, sit by a fire, or get into your sleeping bag."));
            if (vitals.Wetness > 50f)
                list.Add(new BodyCondition("Soaked", BodyPart.Torso, 1, "Wet clothes lose most of their warmth.",
                    "Get out of the rain, wear your rain shell, and dry off by a fire."));
            if (vitals.Satiety < 30f)
                list.Add(new BodyCondition(vitals.IsStarving ? "Starving" : "Hungry", BodyPart.Torso, vitals.IsStarving ? 3 : 1,
                    vitals.IsStarving ? "You're losing health." : "Your stomach is growling.",
                    "Eat (Backpack). Forage, fish or snare for more."));
            if (vitals.IsSick)
                list.Add(new BodyCondition("Stomach bug", BodyPart.Torso, 2,
                    "From untreated water or bad food. You dry out and tire faster.",
                    "It passes on its own. Drink plenty, and boil or filter your water from now on."));
            if (vitals.Health < 50f)
                list.Add(new BodyCondition("Weak", BodyPart.Torso, vitals.Health < 25f ? 3 : 2,
                    "Your health is low. If it runs out, you'll need rescuing.",
                    "Fix whatever is hurting you, then eat, drink, stay warm and rest to recover."));

            // ---------- Arms & legs ----------
            if (vitals.Warmth < 25f)
                list.Add(new BodyCondition("Numb hands", BodyPart.Arms, vitals.Warmth < 12f ? 2 : 1,
                    "Your fingers are stiff with cold.", "Warm up: gloves, a fire, or your sleeping bag."));
            if (backpack.IsOverloaded)
                list.Add(new BodyCondition("Overloaded", BodyPart.Legs, 2,
                    "Your legs are buckling under the pack: very slow, tiring, and hard on your feet.", "Drop or sell something."));
            else if (backpack.CarriedWeight > backpack.ComfortableLoad)
                list.Add(new BodyCondition("Heavy pack", BodyPart.Legs, 1,
                    "Slower going, more tiring, and your feet ache sooner.", "Lighten your pack."));

            list.Sort((a, b) => b.Severity.CompareTo(a.Severity));
            return list;
        }

        /// <summary>The worst severity on a part, 0 if it's fine.</summary>
        public static int Worst(List<BodyCondition> conditions, BodyPart part)
        {
            int worst = 0;
            foreach (BodyCondition condition in conditions)
                if (condition.Part == part && condition.Severity > worst)
                    worst = condition.Severity;
            return worst;
        }
    }
}
