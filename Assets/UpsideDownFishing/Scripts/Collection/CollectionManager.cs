using System;
using System.Collections.Generic;
using UnityEngine;

namespace UpsideDownFishing
{
    public class RodDef
    {
        public string id, displayName;
        public int unlockCatches;
        public float lineStrength;   // tension budget multiplier
        public float reelSpeed;      // m/s at full trigger
        public float luck;           // multiplies rare spawn weights
        public Color color, accent;
    }

    public class DecorDef
    {
        public string id, displayName, requirement;
        public Func<CollectionManager.SaveData, bool> isUnlocked;
    }

    /// <summary>Catch log, coins, and unlocks (rods / decorations). Persisted in PlayerPrefs as JSON.</summary>
    public class CollectionManager : MonoBehaviour
    {
        [Serializable] public class Entry { public string id; public int count; public float biggest; }

        [Serializable]
        public class SaveData
        {
            public List<Entry> caught = new List<Entry>();
            public int coins;
            public int totalCatches;
            public int rodTier;
            public List<string> unlockedRods = new List<string> { "bamboo" };
            public List<string> unlockedDecor = new List<string>();

            public int Count(string id) { var e = caught.Find(x => x.id == id); return e?.count ?? 0; }
            public bool HasRarity(Rarity r) => caught.Exists(e => e.count > 0 && FishSpecies.Get(e.id)?.rarity >= r);
        }

        public static readonly List<RodDef> Rods = new List<RodDef>
        {
            new RodDef { id = "bamboo", displayName = "Bamboo Rod", unlockCatches = 0, lineStrength = 1.0f, reelSpeed = 0.35f, luck = 1.0f, color = new Color(0.85f, 0.7f, 0.4f), accent = new Color(0.4f, 0.75f, 0.35f) },
            new RodDef { id = "coral", displayName = "Coral Rod", unlockCatches = 6, lineStrength = 1.3f, reelSpeed = 0.45f, luck = 1.6f, color = new Color(1f, 0.55f, 0.6f), accent = new Color(1f, 0.85f, 0.5f) },
            new RodDef { id = "starlight", displayName = "Starlight Rod", unlockCatches = 15, lineStrength = 1.7f, reelSpeed = 0.55f, luck = 2.5f, color = new Color(0.35f, 0.4f, 0.9f), accent = new Color(1f, 0.95f, 0.5f) },
        };

        public static readonly List<DecorDef> Decor = new List<DecorDef>
        {
            new DecorDef { id = "jelly_lantern", displayName = "Jelly Lantern", requirement = "Catch 3 fish", isUnlocked = d => d.totalCatches >= 3 },
            new DecorDef { id = "bubble_ring", displayName = "Bubble Ring", requirement = "Catch a Lantern Puff", isUnlocked = d => d.Count("lantern_puff") > 0 },
            new DecorDef { id = "coral_chime", displayName = "Coral Chime", requirement = "Catch any Rare fish", isUnlocked = d => d.HasRarity(Rarity.Rare) },
            new DecorDef { id = "star_mobile", displayName = "Star Mobile", requirement = $"Collect all {FishSpecies.All.Count} species", isUnlocked = d => d.caught.Count >= FishSpecies.All.Count },
        };

        const string Key = "UDF_Save_v1";
        public SaveData Data { get; private set; }
        public RodDef CurrentRod => Rods[Mathf.Clamp(Data.rodTier, 0, Rods.Count - 1)];

        public event Action<FishSpecies, bool, int> Caught;     // species, isNewSpecies, coins
        public event Action<string> Unlocked;                  // message
        public event Action<RodDef> RodChanged;
        public event Action<DecorDef> DecorUnlocked;

        void Awake() { Load(); }

        public void Load()
        {
            try
            {
                var json = PlayerPrefs.GetString(Key, "");
                Data = string.IsNullOrEmpty(json) ? new SaveData() : JsonUtility.FromJson<SaveData>(json) ?? new SaveData();
            }
            catch (Exception e) { Debug.LogWarning("[UDF] Save corrupted, resetting: " + e.Message); Data = new SaveData(); }
        }

        public void Save() { PlayerPrefs.SetString(Key, JsonUtility.ToJson(Data)); PlayerPrefs.Save(); }

        public void ResetProgress() { Data = new SaveData(); Save(); RodChanged?.Invoke(CurrentRod); }

        public bool IsSpeciesAvailable(FishSpecies sp) =>
            Data.totalCatches >= sp.unlockAfterCatches && Data.rodTier >= sp.minRodTier;

        public void RegisterCatch(FishSpecies sp, float size)
        {
            var e = Data.caught.Find(x => x.id == sp.id);
            bool isNew = e == null;
            if (isNew) { e = new Entry { id = sp.id }; Data.caught.Add(e); }
            e.count++;
            e.biggest = Mathf.Max(e.biggest, size);
            Data.totalCatches++;
            Data.coins += sp.coins;
            Caught?.Invoke(sp, isNew, sp.coins);

            foreach (var rod in Rods)
                if (!Data.unlockedRods.Contains(rod.id) && Data.totalCatches >= rod.unlockCatches)
                {
                    Data.unlockedRods.Add(rod.id);
                    Unlocked?.Invoke($"New rod unlocked: {rod.displayName}! (Press B to switch)");
                }
            foreach (var d in Decor)
                if (!Data.unlockedDecor.Contains(d.id) && d.isUnlocked(Data))
                {
                    Data.unlockedDecor.Add(d.id);
                    Unlocked?.Invoke($"New decoration unlocked: {d.displayName}!");
                    DecorUnlocked?.Invoke(d);
                }
            foreach (var s in FishSpecies.All)
                if (s.unlockAfterCatches == Data.totalCatches && s.unlockAfterCatches > 0 && Data.rodTier >= s.minRodTier)
                    Unlocked?.Invoke("New shadows are swimming in the sea above...");
            Save();
        }

        public void CycleRod()
        {
            int start = Data.rodTier;
            for (int i = 1; i <= Rods.Count; i++)
            {
                int t = (start + i) % Rods.Count;
                if (Data.unlockedRods.Contains(Rods[t].id)) { Data.rodTier = t; break; }
            }
            if (Data.rodTier != start) { Save(); RodChanged?.Invoke(CurrentRod); }
        }

        /// <summary>Weighted random species respecting unlocks and rod luck.</summary>
        public FishSpecies PickSpecies()
        {
            float total = 0;
            foreach (var s in FishSpecies.All) if (IsSpeciesAvailable(s)) total += Weight(s);
            float r = UnityEngine.Random.value * total;
            foreach (var s in FishSpecies.All)
            {
                if (!IsSpeciesAvailable(s)) continue;
                r -= Weight(s);
                if (r <= 0) return s;
            }
            return FishSpecies.All[0];
        }

        /// <summary>Still learning: beginner fish are common in the sea and go for the lure first.</summary>
        public bool IsNovice => Data.totalCatches < 5;

        float Weight(FishSpecies s) => s.spawnWeight * (s.rarity >= Rarity.Rare ? CurrentRod.luck : 1f) * (s.beginner && IsNovice ? 3f : 1f);
    }
}
