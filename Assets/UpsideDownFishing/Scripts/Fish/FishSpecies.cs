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
        public string displayName;       // Chinese
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
        public string description;

        public static readonly List<FishSpecies> All = new List<FishSpecies>
        {
            new FishSpecies { id = "bubble_guppy", displayName = "泡泡孔雀鱼", englishName = "Bubble Guppy", rarity = Rarity.Common, shape = BodyShape.Classic,
                topColor = new Color(1f, 0.55f, 0.2f), bellyColor = new Color(1f, 0.9f, 0.6f), finColor = new Color(1f, 0.35f, 0.4f),
                length = 0.22f, swimSpeed = 0.45f, strength = 0.35f, stamina = 2.5f, spawnWeight = 10, coins = 5,
                description = "会吐出心形泡泡的小鱼，最喜欢在天花板灯旁边打转。" },
            new FishSpecies { id = "cloud_minnow", displayName = "云朵米诺", englishName = "Cloud Minnow", rarity = Rarity.Common, shape = BodyShape.Classic,
                topColor = new Color(0.55f, 0.8f, 1f), bellyColor = Color.white, finColor = new Color(0.85f, 0.95f, 1f),
                length = 0.18f, swimSpeed = 0.55f, strength = 0.3f, stamina = 2f, spawnWeight = 10, coins = 5,
                description = "身体像一小团云，钓上来后会在房间里慢慢飘。" },
            new FishSpecies { id = "lantern_puff", displayName = "灯笼河豚", englishName = "Lantern Puff", rarity = Rarity.Uncommon, shape = BodyShape.Round,
                topColor = new Color(1f, 0.85f, 0.25f), bellyColor = new Color(1f, 0.98f, 0.8f), finColor = new Color(1f, 0.6f, 0.2f), emission = new Color(0.25f, 0.18f, 0.02f),
                length = 0.26f, swimSpeed = 0.25f, strength = 0.5f, stamina = 4f, spawnWeight = 5, coins = 15, unlockAfterCatches = 3,
                description = "体内发着暖光，被钓出水面时会“噗”地鼓起来。" },
            new FishSpecies { id = "ribbon_eel", displayName = "缎带鳗", englishName = "Ribbon Eel", rarity = Rarity.Uncommon, shape = BodyShape.Eel,
                topColor = new Color(0.65f, 0.4f, 1f), bellyColor = new Color(1f, 0.75f, 0.95f), finColor = new Color(1f, 0.5f, 0.85f),
                length = 0.6f, swimSpeed = 0.4f, strength = 0.65f, stamina = 5f, spawnWeight = 4, coins = 20, unlockAfterCatches = 5,
                description = "像一条飘带在水里打着蝴蝶结游动。" },
            new FishSpecies { id = "moon_koi", displayName = "月光锦鲤", englishName = "Moon Koi", rarity = Rarity.Rare, shape = BodyShape.Classic,
                topColor = new Color(1f, 0.97f, 0.92f), bellyColor = new Color(1f, 0.6f, 0.55f), finColor = new Color(1f, 0.85f, 0.85f), emission = new Color(0.08f, 0.08f, 0.1f),
                length = 0.42f, swimSpeed = 0.3f, strength = 0.75f, stamina = 6f, spawnWeight = 2, coins = 50, unlockAfterCatches = 8, minRodTier = 1,
                description = "只在倒置的水域里出现，传说会带来好运。" },
            new FishSpecies { id = "starlight_ray", displayName = "星光鳐", englishName = "Starlight Ray", rarity = Rarity.Rare, shape = BodyShape.Ray,
                topColor = new Color(0.15f, 0.2f, 0.55f), bellyColor = new Color(0.7f, 0.85f, 1f), finColor = new Color(0.3f, 0.45f, 0.95f), emission = new Color(0.05f, 0.1f, 0.25f),
                length = 0.5f, swimSpeed = 0.35f, strength = 0.85f, stamina = 7f, spawnWeight = 1.5f, coins = 70, unlockAfterCatches = 12, minRodTier = 1,
                description = "背上布满星星，在房间里漂浮时像一片小夜空。" },
            new FishSpecies { id = "crystal_whalelet", displayName = "水晶小鲸", englishName = "Crystal Whalelet", rarity = Rarity.Legendary, shape = BodyShape.Whale,
                topColor = new Color(0.4f, 0.95f, 0.95f), bellyColor = new Color(0.9f, 1f, 1f), finColor = new Color(0.55f, 1f, 0.9f), emission = new Color(0.05f, 0.2f, 0.2f),
                length = 0.8f, swimSpeed = 0.2f, strength = 1.0f, stamina = 10f, spawnWeight = 0.5f, coins = 200, unlockAfterCatches = 20, minRodTier = 2,
                description = "天花板之海的守护者，极其罕见。" },
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
            Rarity.Common => "普通", Rarity.Uncommon => "少见", Rarity.Rare => "稀有", _ => "传说",
        };
    }
}
