using Newtonsoft.Json;
using System.ComponentModel;


namespace FarmAnimalOwnershipProject
{

    [JsonObject]
    public class ManualFactionMatchEntry
    {

        [DisplayName("Match Pattern")]
        [Description("A substring to match against a cell or location EditorID (partial matching).")]
        [JsonProperty]
        public string EditorID { get; set; } = string.Empty;

        [DisplayName("Faction EditorID")]
        [Description("The EditorID of the faction that should own animals matching the pattern above.")]
        [JsonProperty]
        public string FactionEditorID { get; set; } = string.Empty;
    }


    [JsonObject]
    public class PluginFactionOverrideEntry
    {

        [DisplayName("Plugin Name (partial matching)")]
        [Description("A substring of the plugin file name that placed the animal (e.g. 'MyFarmMod').")]
        [JsonProperty]
        public string PluginName { get; set; } = string.Empty;

        [DisplayName("Faction EditorID")]
        [Description("The EditorID of the faction that should own animals placed by matching plugins.")]
        [JsonProperty]
        public string FactionEditorID { get; set; } = string.Empty;
    }


    [JsonObject]
    public class Settings
    {

        [DisplayName("Races to patch")]
        [Description("The races the patcher is looking for")]
        [JsonProperty]
        public List<string> IncludeRaceTerms { get; set; } =
        [
            "Goat", "Chicken", "Cow", "Horse", "Pig", "Sheep", "Dog", "Cat", "Bunny", "Husky", "Geese", 
            "Goose", "Rabbit", "Pet", "Duck", "Rooster", "Lamb", "Foal", "Puppy", "Kitten", "Calf", "Cock",
            "Domestic", "MihailGuar", "MihailKagouti", "BantamGuar", 

        ];

        [DisplayName("Owners to never assign")]
        [Description("Factions to never assign as owners")]
        [JsonProperty]
        public List<string> ExcludeOwnerNames { get; set; } =
        [
            "Player", "PlayerFaction", "CW", "Bandit", "Hagraven", "Fort", "Draugr", "JobMerchantFaction",
            "Fake", "CarriageDriver", "CarriageSystemFaction",
        ];

        [DisplayName("Minimum owned animals required for a majority")]
        [Description("A cell needs at least this many already-owned animals before the ownership by voting system is active")]
        [JsonProperty]
        public int MinimumOwnedObjectsForMajority { get; set; } = 1;

        [DisplayName("Names to exclude")]
        [Description("Actor name terms to exclude from patching")]
        [JsonProperty]
        public List<string> ExcludeNameTerms { get; set; } =
        [
            "Wild", "Bandit", "Forsworn", "Sabre", "Pigeon", "Zombie", "Draugr", "Durzog", "Stray", "Dead", "Ghost",
            "Vampire", "Necromancer", "Bone", "Feral", "Giant", "Dragon", "Troll",
        ];

        [DisplayName("Plugins to exclude")]
        [Description("Plugins that are entirely excluded from patching")]
        [JsonProperty]
        public List<string> ExcludePlugins { get; set; } =
        [
            "Vigilant", "SkyrimUnderground", "HearthFire", "cc", "Glenmoril", "HorrorOfMorthal",
        ];

        [DisplayName("Cells to exclude")]
        [Description("Cells that are entirely excluded from patching")]
        [JsonProperty]
        public List<string> ExcludeCellRules { get; set; } =
        [
            "BYOH", "cc", "Helgen",
        ];

        [DisplayName("Location Types to exclude")]
        [Description("Location types that are entirely excluded from patching")]
        [JsonProperty]
        public List<string> ExcludeLocTypeRules { get; set; } =
        [
            "Dungeon", "AnimalDen", "Bandit", "DragonLair", "Draugr", "Dwarven",
            "Falmer", "GiantCamp", "Hagraven", "Spriggan", "Vampire", "Warlock",
            "Werewolf", "Forsworn", "Cave", "Ruin", "PlayerHouse", "Lair",
        ];

        [DisplayName("Plugin overrides (Plugin name -> Faction EditorID)")]
        [Description("Animals placed by a matching plugin are assigned to the given faction, taking precedence over location-based matching.")]
        [JsonProperty]
        public List<PluginFactionOverrideEntry> PluginFactionOverrides { get; set; } =
        [
            new() { PluginName = "Whiterun", FactionEditorID = "TownWhiterunFaction" },
            new() { PluginName = "Solitude", FactionEditorID = "TownSolitudeFaction" },
            new() { PluginName = "Riften", FactionEditorID = "TownRiftenFaction" },
            new() { PluginName = "Windhelm", FactionEditorID = "TownWindhelmFaction" },
            new() { PluginName = "Markarth", FactionEditorID = "TownMarkarthFaction" },
            new() { PluginName = "Falkreath", FactionEditorID = "TownFalkreathFaction" },
            new() { PluginName = "Morthal", FactionEditorID = "TownMorthalFaction" },
            new() { PluginName = "Dawnstar", FactionEditorID = "TownDawnstarFaction" },
            new() { PluginName = "Winterhold", FactionEditorID = "TownWinterholdFaction" },
            new() { PluginName = "DragonBridge", FactionEditorID = "TownDragonBridgeFaction" },
            new() { PluginName = "Ivarstead", FactionEditorID = "TownIvarsteadFaction" },
            new() { PluginName = "Karthwasten", FactionEditorID = "TownKarthwastenFaction" },
            new() { PluginName = "Riverwood", FactionEditorID = "TownRiverwoodFaction" },
            new() { PluginName = "Rorikstead", FactionEditorID = "TownRoriksteadFaction" },
            new() { PluginName = "Kynesgrove", FactionEditorID = "TownKynesgroveFaction" },
            new() { PluginName = "Nightgate", FactionEditorID = "Hadring" },
            new() { PluginName = "OldHroldan", FactionEditorID = "TownOldHroldanFaction" },
            new() { PluginName = "ShorsStone", FactionEditorID = "TownShorsStoneFaction" },
            new() { PluginName = "Shor's Stone", FactionEditorID = "TownShorsStoneFaction" },
            new() { PluginName = "DarkwaterCrossing", FactionEditorID = "TownDarkwaterCrossingFaction" },
            new() { PluginName = "Skaal", FactionEditorID = "DLC2SVGreathallFaction" },
        ];

        [DisplayName("Manual Faction Matches")]
        [Description("Matches a cell/location EditorID to a faction. Only consulted once naming conventions (Town/Farm/Mill patterns) have already had a chance to resolve one. Be careful not to use too broad terms! EditorID can be either a CELL or a LOCATION EditorID.")]
        [JsonProperty]
        public List<ManualFactionMatchEntry> ManualFactionMatches { get; set; } =
        [
            // Vanilla Towns
            new() { EditorID = "Whiterun", FactionEditorID = "TownWhiterunFaction" },
            new() { EditorID = "Solitude", FactionEditorID = "TownSolitudeFaction" },
            new() { EditorID = "Riften", FactionEditorID = "TownRiftenFaction" },
            new() { EditorID = "Windhelm", FactionEditorID = "TownWindhelmFaction" },
            new() { EditorID = "Markarth", FactionEditorID = "TownMarkarthFaction" },
            new() { EditorID = "Falkreath", FactionEditorID = "TownFalkreathFaction" },
            new() { EditorID = "Morthal", FactionEditorID = "TownMorthalFaction" },
            new() { EditorID = "Dawnstar", FactionEditorID = "TownDawnstarFaction" },
            new() { EditorID = "Winterhold", FactionEditorID = "TownWinterholdFaction" },
            new() { EditorID = "DragonBridge", FactionEditorID = "TownDragonBridgeFaction" },
            new() { EditorID = "Ivarstead", FactionEditorID = "TownIvarsteadFaction" },
            new() { EditorID = "Karthwasten", FactionEditorID = "TownKarthwastenFaction" },
            new() { EditorID = "Riverwood", FactionEditorID = "TownRiverwoodFaction" },
            new() { EditorID = "Rorikstead", FactionEditorID = "TownRoriksteadFaction" },
            new() { EditorID = "Kynesgrove", FactionEditorID = "TownKynesgroveFaction" },
            new() { EditorID = "Nightgate", FactionEditorID = "Hadring" },
            new() { EditorID = "OldHroldan", FactionEditorID = "TownOldHroldanFaction" },
            new() { EditorID = "ShorsStone", FactionEditorID = "TownShorsStoneFaction" },
            new() { EditorID = "Shor's Stone", FactionEditorID = "TownShorsStoneFaction" },
            new() { EditorID = "DarkwaterCrossing", FactionEditorID = "TownDarkwaterCrossingFaction" },
            new() { EditorID = "MixwaterMill", FactionEditorID = "MixwaterMillGilfreHouseFaction" },

            // Misc Locations
        //  new() { EditorID = "DawnstarSanctuaryLocation", FactionEditorID = "DarkBrotherhoodFaction" },
            new() { EditorID = "DragonBridgeFourShieldsTavern", FactionEditorID = "DragonBridgeFourShieldsInnFaction" },
            new() { EditorID = "HonningbrewMeadery", FactionEditorID = "HonningbrewMeaderyFaction" },
            new() { EditorID = "AngisCampExterior", FactionEditorID = "WIGenericCrimeFaction" },
            new() { EditorID = "LeftHandMine", FactionEditorID = "TownLeftHandMineFaction" },
            new() { EditorID = "Stonehills", FactionEditorID = "TownStonehillsFaction" },
            new() { EditorID = "BluePalace", FactionEditorID = "SolitudeBluePalaceFaction" },
            
            // Modded Locations
            new() { EditorID = "BearsCaveMillLocation", FactionEditorID = "RG439BearsCaveMillFaction" },
            new() { EditorID = "KynesgroveFarmsLocationTGCoKG", FactionEditorID = "KynesgroveRagnasAndHerleifsHouseFactionTGCoKG" },
            new() { EditorID = "KynesgroveGalasSteadLocationTGCoKG", FactionEditorID = "KynesgroveGalasHouseFactionTGCoKG" },
            new() { EditorID = "0BearQOrigin", FactionEditorID = "TownWindhelmFaction" },
            new() { EditorID = "NightgateInn", FactionEditorID = "Hadring" },
            new() { EditorID = "0WindhelmExtDwelling", FactionEditorID = "WindhelmSurWheelhouseFaction" },
            new() { EditorID = "GraniteHill", FactionEditorID = "TownGraniteHillFaction" },
            new() { EditorID = "HalloftheVigilant", FactionEditorID = "VigilantOfStendarrFaction" },
            new() { EditorID = "WBPT", FactionEditorID = "SolitudeBluePalaceFaction" },
            
            // DLC Locations
            new() { EditorID = "TelMithryn", FactionEditorID = "TelMithrynFaction" },
            new() { EditorID = "DLC2SkaalVillageLocation", FactionEditorID = "DLC2SVGreathallFaction" },
            new() { EditorID = "DLC2RavenRockLocation", FactionEditorID = "DLC2RRBulwarkFaction" },
            new() { EditorID = "Dawnguard", FactionEditorID = "DLC1DawnguardFaction" },
            new() { EditorID = "HunterWorld", FactionEditorID = "DLC1DawnguardFaction" },
            new() { EditorID = "RavenRock", FactionEditorID = "DLC2CrimeRavenRockFaction" },

            // Orc Strongholds
            new() { EditorID = "DushnikhYal", FactionEditorID = "TownDushnikhYalFaction" },
            new() { EditorID = "Largashbur", FactionEditorID = "TownLargashburFaction" },
            new() { EditorID = "MorKhazgur", FactionEditorID = "TownMorKhazgurFaction" },
            new() { EditorID = "Narzulbur", FactionEditorID = "TownNarzulburFaction" },
        ];
    }
}