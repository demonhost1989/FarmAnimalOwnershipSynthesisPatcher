using Mutagen.Bethesda.WPF.Reflection.Attributes;

namespace FarmAnimalOwnershipProject
{
    public class ConventionOverrideEntry
    {
        [SettingName("Cell or Location EditorID")]
        [Tooltip("The EditorID of the CELL or LOCATION record you want to force a match for.")]
        public string EditorID { get; set; } = string.Empty;

        [SettingName("Faction EditorID")]
        [Tooltip("The EditorID of the faction that should own animals in that cell/location.")]
        public string FactionEditorID { get; set; } = string.Empty;
    }

    public class Settings
    {

        // The animal races we are looking for
        [SettingName("Races to patch (partial matching)")]
        [Tooltip("IncludeRaceTerms")]
        public List<string> IncludeRaceTerms { get; set; } =
        [
            "Goat", "Chicken", "Cow", "Horse", "Pig", "Sheep", "Dog", "Cat", "Bunny", "Husky",
        ];

        // Animal names we want to exclude
        [SettingName("Actor names to exclude from patching (partial matching)")]
        [Tooltip("ExcludeNameTerms")]
        public List<string> ExcludeNameTerms { get; set; } =
        [
            "Wild", "Bandit", "Forsworn", "Sabre", "Pigeon", "Zombie", "Draugr", "Durzog", "Stray", "Dead",
        ];

        // Plugin exclusion  (wildcards supported)
        [SettingName("Plugins to exclude from patching (wildcard support)")]
        [Tooltip("ExcludePlugins")]
        public List<string> ExcludePlugins { get; set; } =
        [
            "Vigilant.esm", "*FollowerFramework*", "*SkyrimUnderground*", "*HearthFire*", "cc*", "Glenmoril.esm",
        ];

        // Cell exclusion  (wildcards supported)
        [SettingName("Cells to exclude from patching (wildcard support)")]
        [Tooltip("ExcludeCellRules")]
        public List<string> ExcludeCellRules { get; set; } =
        [
            "BYOH*", "cc*",
        ];

        // LocType exclusion
        [SettingName("Location Types to exclude (exact matches)")]
        [Tooltip("ExcludeLocTypeRules")]
        public List<string> ExcludeLocTypeRules { get; set; } =
        [
            "LocTypeDungeon", "LocTypeAnimalDen", "LocTypeBanditCamp", "LocTypeDragonLair", "LocTypeDragonPriestLair",
            "LocTypeDraugrCrypt", "LocTypeDwarvenAutomatons", "LocTypeFalmerHive", "LocTypeGiantCamp", "LocTypeHagravenNest",
            "LocTypeSprigganGrove", "LocTypeVampireLair", "LocTypeWarlockLair", "LocTypeWerewolfLair", "LocTypeForswornCamp",
            "LocSetCave", "LocSetDwarvenRuin", "LocSetNordicRuin", "LocSetCaveIce", "LocTypePlayerHouse",
        ];

        // Manual cell/location -> faction overrides for matching
        [SettingName("Convention overrides (Cell/Location EditorID -> Faction EditorID) Partial Matching")]
        [Tooltip("Be carefull not to you too broad terms! EditorID can be either a CELL or a LOCATION EditorID.")]
        public List<ConventionOverrideEntry> ConventionOverrides { get; set; } =
        [
            // Exact or particular matches
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
            // Overrides for locations and cells that include town names
            // Capitals
            new() { EditorID = "Whiterun", FactionEditorID = "TownWhiterunFaction" },
            new() { EditorID = "Solitude", FactionEditorID = "TownSolitudeFaction" },
            new() { EditorID = "Riften", FactionEditorID = "TownRiftenFaction" },
            new() { EditorID = "Windhelm", FactionEditorID = "TownWindhelmFaction" },
            new() { EditorID = "Markarth", FactionEditorID = "TownMarkarthFaction" },
            // Cities
            new() { EditorID = "Falkreath", FactionEditorID = "TownFalkreathFaction" },
            new() { EditorID = "Morthal", FactionEditorID = "TownMorthalFaction" },
            new() { EditorID = "Dawnstar", FactionEditorID = "TownDawnstarFaction" },
            new() { EditorID = "RavenRock", FactionEditorID = "DLC2CrimeRavenRockFaction" },
            // Towns, villages and settlements
            new() { EditorID = "DragonBridge", FactionEditorID = "TownDragonBridgeFaction" },
            new() { EditorID = "Ivarstead", FactionEditorID = "TownIvarsteadFaction" },
            new() { EditorID = "Karthwasten", FactionEditorID = "TownKarthwastenFaction" },
            new() { EditorID = "Riverwood", FactionEditorID = "TownRiverwoodFaction" },
            new() { EditorID = "Rorikstead", FactionEditorID = "TownRoriksteadFaction" },
            new() { EditorID = "Kynesgrove", FactionEditorID = "TownKynesgroveFaction" },
            new() { EditorID = "Nightgate", FactionEditorID = "NightgateInnFaction" },
            new() { EditorID = "OldHroldan", FactionEditorID = "TownOldHroldanFaction" },
            new() { EditorID = "DarkwaterCrossing", FactionEditorID = "TownDarkwaterCrossingFaction" },
            new() { EditorID = "LeftHandMine", FactionEditorID = "TownLeftHandMineFaction" },
            new() { EditorID = "Stonehills", FactionEditorID = "TownStonehillsFaction" },
            new() { EditorID = "TelMithryn", FactionEditorID = "TelMithrynFaction" },
            // Orc Strongholds
            new() { EditorID = "DushnikhYal", FactionEditorID = "TownDushnikhYalFaction" },
            new() { EditorID = "Largashbur", FactionEditorID = "TownLargashburFaction" },
            new() { EditorID = "MorKhazgur", FactionEditorID = "TownMorKhazgurFaction" },
            new() { EditorID = "Narzulbur", FactionEditorID = "TownNarzulburFaction" },

        ];
    }
}
