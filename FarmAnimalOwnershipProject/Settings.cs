using System.ComponentModel;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace FarmAnimalOwnershipProject
{
    [JsonObject]
    public class ConventionOverrideEntry
    {
        [DisplayName("Cell or Location EditorID")]
        [Description("The EditorID of the CELL or LOCATION record you want to force a match for.")]
        [JsonProperty]
        public string EditorID { get; set; } = string.Empty;

        [DisplayName("Faction EditorID")]
        [Description("The EditorID of the faction that should own animals in that cell/location.")]
        [JsonProperty]
        public string FactionEditorID { get; set; } = string.Empty;
    }

    // Minimal PluginFilter enum to keep compatibility with existing code that references it.
    public enum PluginFilter
    {
        AllPlugins
    }

    [JsonObject]
    public class Settings
    {
        [DisplayName("Plugin Exclude List")]
        [Description("List of plugins to exclude (semicolon separated)")]
        [JsonProperty]
        public string PluginExcludeList { get; set; } = string.Empty;

        [DisplayName("Plugin Processing")]
        [Description("Choose which plugins to process")]
        [JsonProperty(ItemConverterType = typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public PluginFilter PluginFilter { get; set; } = PluginFilter.AllPlugins;

        [DisplayName("Races to patch (partial matching)")]
        [Description("IncludeRaceTerms")]
        [JsonProperty]
        public List<string> IncludeRaceTerms { get; set; } =
        [
            "Goat", "Chicken", "Cow", "Horse", "Pig", "Sheep", "Dog", "Cat", "Bunny", "Husky",
        ];

        [DisplayName("Actor names to exclude from patching (partial matching)")]
        [Description("ExcludeNameTerms")]
        [JsonProperty]
        public List<string> ExcludeNameTerms { get; set; } =
        [
            "Wild", "Bandit", "Forsworn", "Sabre", "Pigeon", "Zombie", "Draugr", "Durzog", "Stray", "Dead",
        ];

        [DisplayName("Plugins to exclude from patching (wildcard support)")]
        [Description("ExcludePlugins")]
        [JsonProperty]
        public List<string> ExcludePlugins { get; set; } =
        [
            "Vigilant.esm", "*FollowerFramework*", "*SkyrimUnderground*", "*HearthFire*", "cc*", "Glenmoril.esm",
        ];

        [DisplayName("Cells to exclude from patching (wildcard support)")]
        [Description("ExcludeCellRules")]
        [JsonProperty]
        public List<string> ExcludeCellRules { get; set; } =
        [
            "BYOH*", "cc*",
        ];

        [DisplayName("Location Types to exclude (exact matches)")]
        [Description("ExcludeLocTypeRules")]
        [JsonProperty]
        public List<string> ExcludeLocTypeRules { get; set; } =
        [
            "LocTypeDungeon", "LocTypeAnimalDen", "LocTypeBanditCamp", "LocTypeDragonLair", "LocTypeDragonPriestLair",
            "LocTypeDraugrCrypt", "LocTypeDwarvenAutomatons", "LocTypeFalmerHive", "LocTypeGiantCamp", "LocTypeHagravenNest",
            "LocTypeSprigganGrove", "LocTypeVampireLair", "LocTypeWarlockLair", "LocTypeWerewolfLair", "LocTypeForswornCamp",
            "LocSetCave", "LocSetDwarvenRuin", "LocSetNordicRuin", "LocSetCaveIce", "LocTypePlayerHouse",
        ];

        [DisplayName("Convention overrides (Cell/Location EditorID -> Faction EditorID) Partial Matching")]
        [Description("Be careful not to use too broad terms! EditorID can be either a CELL or a LOCATION EditorID.")]
        [JsonProperty]
        public List<ConventionOverrideEntry> ConventionOverrides { get; set; } =
        [
            new() { EditorID = "DawnstarSanctuaryLocation", FactionEditorID = "DarkBrotherhoodFaction" },
            new() { EditorID = "DLC2SkaalVillageLocation", FactionEditorID = "DLC2SVGreathallFaction" },
            new() { EditorID = "BearsCaveMillLocation", FactionEditorID = "RG439BearsCaveMillFaction" },
            new() { EditorID = "DLC2RavenRockLocation", FactionEditorID = "DLC2RRBulwarkFaction" },
            new() { EditorID = "KynesgroveFarmsLocationTGCoKG", FactionEditorID = "KynesgroveRagnasAndHerleifsHouseFactionTGCoKG" },
            new() { EditorID = "KynesgroveGalasSteadLocationTGCoKG", FactionEditorID = "KynesgroveGalasHouseFactionTGCoKG" },
            new() { EditorID = "RoriksteadLemkilsFarmLocation", FactionEditorID = "RoriksteadLemkilsFarmFaction" },
            new() { EditorID = "HonningbrewMeadery", FactionEditorID = "HonningbrewMeaderyFaction" },
            new() { EditorID = "0BearQOrigin", FactionEditorID = "TownWindhelmFaction" },
            new() { EditorID = "DragonBridgeFourShieldsTavern", FactionEditorID = "DragonBridgeFourShieldsInnFaction" },
            new() { EditorID = "NightgateInn", FactionEditorID = "NG_JobMerchantFaction" },
            new() { EditorID = "Dawnguard", FactionEditorID = "DLC1DawnguardFaction" },
            new() { EditorID = "HunterWorld", FactionEditorID = "DLC1DawnguardFaction" },
            new() { EditorID = "AngisCampExterior", FactionEditorID = "WIGenericCrimeFaction" },
            new() { EditorID = "0WindhelmExtDwelling", FactionEditorID = "WindhelmSurWheelhouseFaction" },
            new() { EditorID = "WBPT", FactionEditorID = "SolitudeBluePalaceFaction" },
            new() { EditorID = "Whiterun", FactionEditorID = "TownWhiterunFaction" },
            new() { EditorID = "Solitude", FactionEditorID = "TownSolitudeFaction" },
            new() { EditorID = "Riften", FactionEditorID = "TownRiftenFaction" },
            new() { EditorID = "Windhelm", FactionEditorID = "TownWindhelmFaction" },
            new() { EditorID = "Markarth", FactionEditorID = "TownMarkarthFaction" },
            new() { EditorID = "Falkreath", FactionEditorID = "TownFalkreathFaction" },
            new() { EditorID = "Morthal", FactionEditorID = "TownMorthalFaction" },
            new() { EditorID = "Dawnstar", FactionEditorID = "TownDawnstarFaction" },
            new() { EditorID = "RavenRock", FactionEditorID = "DLC2CrimeRavenRockFaction" },
            new() { EditorID = "DragonBridge", FactionEditorID = "TownDragonBridgeFaction" },
            new() { EditorID = "Ivarstead", FactionEditorID = "TownIvarsteadFaction" },
            new() { EditorID = "Karthwasten", FactionEditorID = "TownKarthwastenFaction" },
            new() { EditorID = "Riverwood", FactionEditorID = "TownRiverwoodFaction" },
            new() { EditorID = "Rorikstead", FactionEditorID = "TownRoriksteadFaction" },
            new() { EditorID = "Kynesgrove", FactionEditorID = "TownKynesgroveFaction" },
            new() { EditorID = "Nightgate", FactionEditorID = "NightgateInnFaction" },
        ];
    }
}
