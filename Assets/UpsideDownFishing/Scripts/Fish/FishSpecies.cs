using System.Collections.Generic;
using UnityEngine;

namespace UpsideDownFishing
{
    public enum Rarity { Common, Uncommon, Rare, Legendary }
    public enum BodyShape { Classic, Round, Eel, Ray, Whale }

    /// <summary>Static data for one fish species. All species are original designs for this game.</summary>
    public class FishSpecies
    {
        public string id;
        public string displayName;       // shown to players
        public string englishName;
        public Rarity rarity;
        public BodyShape shape;
        public Color topColor, bellyColor, finColor;
        public Color emission = Color.black;
        public float length = 0.3f;      // metres
        public float swimSpeed = 0.35f;
        public float strength = 0.5f;    // pull force 0..1+
        public float stamina = 4f;       // seconds of full-tension fighting to tire it out
        public float spawnWeight = 1f;
        public int coins = 10;
        public int unlockAfterCatches;   // total catches before it starts spawning
        public int minRodTier;           // rod tier needed to attract it
        /// <summary>Beginner fish: bites quickly, keeps biting after a missed strike, fights
        /// briefly and gently and hardly ever throws the hook or snaps the line.</summary>
        public bool beginner;
        public string description;

        public static readonly List<FishSpecies> All = new List<FishSpecies>
        {
            new FishSpecies { id = "mochi_fish", displayName = "Mochi Fish", englishName = "Mochi Fish", rarity = Rarity.Common, shape = BodyShape.Round,
                topColor = new Color(1f, 0.72f, 0.68f), bellyColor = new Color(1f, 0.96f, 0.9f), finColor = new Color(0.55f, 0.9f, 0.9f),
                length = 0.2f, swimSpeed = 0.3f, strength = 0.18f, stamina = 1.5f, spawnWeight = 8, coins = 3, beginner = true,
                description = "Round, slow and always hungry. Once it bites, it never lets go. A beginner's best friend." },
            new FishSpecies { id = "bubble_guppy", displayName = "Bubble Guppy", englishName = "Bubble Guppy", rarity = Rarity.Common, shape = BodyShape.Classic,
                topColor = new Color(1f, 0.55f, 0.2f), bellyColor = new Color(1f, 0.9f, 0.6f), finColor = new Color(1f, 0.35f, 0.4f),
                length = 0.22f, swimSpeed = 0.45f, strength = 0.35f, stamina = 2.5f, spawnWeight = 10, coins = 5,
                description = "Blows heart-shaped bubbles and loves to circle the ceiling lights." },
            new FishSpecies { id = "cloud_minnow", displayName = "Cloud Minnow", englishName = "Cloud Minnow", rarity = Rarity.Common, shape = BodyShape.Classic,
                topColor = new Color(0.55f, 0.8f, 1f), bellyColor = Color.white, finColor = new Color(0.85f, 0.95f, 1f),
                length = 0.18f, swimSpeed = 0.55f, strength = 0.3f, stamina = 2f, spawnWeight = 10, coins = 5,
                description = "A little puff of cloud. Once caught, it drifts lazily around the room." },
            new FishSpecies { id = "lantern_puff", displayName = "Lantern Puff", englishName = "Lantern Puff", rarity = Rarity.Uncommon, shape = BodyShape.Round,
                topColor = new Color(1f, 0.85f, 0.25f), bellyColor = new Color(1f, 0.98f, 0.8f), finColor = new Color(1f, 0.6f, 0.2f), emission = new Color(0.25f, 0.18f, 0.02f),
                length = 0.26f, swimSpeed = 0.25f, strength = 0.5f, stamina = 4f, spawnWeight = 5, coins = 15, unlockAfterCatches = 3,
                description = "Glows warm from the inside and puffs up with a pop when pulled from the water." },
            new FishSpecies { id = "ribbon_eel", displayName = "Ribbon Eel", englishName = "Ribbon Eel", rarity = Rarity.Uncommon, shape = BodyShape.Eel,
                topColor = new Color(0.65f, 0.4f, 1f), bellyColor = new Color(1f, 0.75f, 0.95f), finColor = new Color(1f, 0.5f, 0.85f),
                length = 0.6f, swimSpeed = 0.4f, strength = 0.65f, stamina = 5f, spawnWeight = 4, coins = 20, unlockAfterCatches = 5,
                description = "Swims like a silk ribbon, tying itself into bows." },
            new FishSpecies { id = "moon_koi", displayName = "Moon Koi", englishName = "Moon Koi", rarity = Rarity.Rare, shape = BodyShape.Classic,
                topColor = new Color(1f, 0.97f, 0.92f), bellyColor = new Color(1f, 0.6f, 0.55f), finColor = new Color(1f, 0.85f, 0.85f), emission = new Color(0.08f, 0.08f, 0.1f),
                length = 0.42f, swimSpeed = 0.3f, strength = 0.75f, stamina = 6f, spawnWeight = 2, coins = 50, unlockAfterCatches = 8, minRodTier = 1,
                description = "Only found in upside-down waters. Legend says it brings good luck." },
            new FishSpecies { id = "starlight_ray", displayName = "Starlight Ray", englishName = "Starlight Ray", rarity = Rarity.Rare, shape = BodyShape.Ray,
                topColor = new Color(0.15f, 0.2f, 0.55f), bellyColor = new Color(0.7f, 0.85f, 1f), finColor = new Color(0.3f, 0.45f, 0.95f), emission = new Color(0.05f, 0.1f, 0.25f),
                length = 0.5f, swimSpeed = 0.35f, strength = 0.85f, stamina = 7f, spawnWeight = 1.5f, coins = 70, unlockAfterCatches = 12, minRodTier = 1,
                description = "Its back is covered in stars. Floating in your room, it looks like a tiny night sky." },
            new FishSpecies { id = "crystal_whalelet", displayName = "Crystal Whalelet", englishName = "Crystal Whalelet", rarity = Rarity.Legendary, shape = BodyShape.Whale,
                topColor = new Color(0.4f, 0.95f, 0.95f), bellyColor = new Color(0.9f, 1f, 1f), finColor = new Color(0.55f, 1f, 0.9f), emission = new Color(0.05f, 0.2f, 0.2f),
                length = 0.8f, swimSpeed = 0.2f, strength = 1.0f, stamina = 10f, spawnWeight = 0.5f, coins = 200, unlockAfterCatches = 20, minRodTier = 2,
                description = "Guardian of the sea above. Extremely rare." },
        };

        public static FishSpecies Get(string id) => All.Find(s => s.id == id);

        public static Color RarityColor(Rarity r) => r switch
        {
            Rarity.Common => new Color(0.85f, 0.9f, 0.95f),
            Rarity.Uncommon => new Color(0.45f, 1f, 0.55f),
            Rarity.Rare => new Color(0.4f, 0.7f, 1f),
            _ => new Color(1f, 0.75f, 0.2f),
        };

        public static string RarityName(Rarity r) => r switch
        {
            Rarity.Common => "Common", Rarity.Uncommon => "Uncommon", Rarity.Rare => "Rare", _ => "Legendary",
        };
    }
}
