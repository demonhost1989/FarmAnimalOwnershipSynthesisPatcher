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
        public List<string> IncludeRaceTerms { get; set; } = new()
        {
            "Goat", "Chicken", "Cow", "Horse", "Pig", "Sheep", "Dog", "Cat", "Bunny", "Husky"
        };

        // Animal names we want to exclude
        [SettingName("Actor names to exclude from patching (partial matching")]
        [Tooltip("ExcludeNameTerms")]
        public List<string> ExcludeNameTerms { get; set; } = new()
        {
            "Wild", "Bandit", "Forsworn", "Sabre", "Pigeon", "Zombie", "Draugr", "Durzog", "Stray"
        };

        // Plugin exclusion  (wildcards supported)
        [SettingName("Plugins to exclude from patching (wildcard support with *")]
        [Tooltip("ExcludePlugins")]
        public List<string> ExcludePlugins { get; set; } = new()
        {
            "Vigilant.esm", "*FollowerFramework*", "*SkyrimUnderground*", "*HearthFire*", "cc*", "Glenmoril.esm"
        };

        // Cell exclusion  (wildcards supported)
        [SettingName("Cells to exclude from patching (wildcard support with *")]
        [Tooltip("ExcludeCellRules")]
        public List<string> ExcludeCellRules { get; set; } = new()
        {
            "BYOH*", "cc*"
        };

        // Manual cell/location -> faction overrides for cases automatic matching gets wrong or misses entirely
        [SettingName("Convention overrides (Cell/Location EditorID -> Faction EditorID) !Exact Matches Only!")]
        [Tooltip("Manual overrides for locations where automatic ownership matching fails or picks the wrong faction. EditorID can be either a CELL or a LOCATION EditorID.")]
        public List<ConventionOverrideEntry> ConventionOverrides { get; set; } = new()
        {
            new() { EditorID = "DawnstarSanctuaryLocation", FactionEditorID = "DarkBrotherhoodFaction" },
            new() { EditorID = "DLC2SkaalVillageLocation", FactionEditorID = "DLC2SVGreathallFaction" },
            new() { EditorID = "BearsCaveMillLocation", FactionEditorID = "RG439BearsCaveMillFaction" },
            new() { EditorID = "DLC2RavenRockLocation", FactionEditorID = "DLC2RRBulwarkFaction" },
            new() { EditorID = "KynesgroveFarmsLocationTGCoKG", FactionEditorID = "KynesgroveRagnasAndHerleifsHouseFactionTGCoKG" },
            new() { EditorID = "KynesgroveGalasSteadLocationTGCoKG", FactionEditorID = "KynesgroveGalasHouseFactionTGCoKG" },
            new() { EditorID = "RoriksteadLemkilsFarmLocation", FactionEditorID = "RoriksteadLemkilsFarmFaction" },
   //       new() { EditorID = "GoldenglowEstateLocation", FactionEditorID = "TownGoldenglowEstateFaction" },
   //       new() { EditorID = "BlackBriarLodgeLocation", FactionEditorID = "MS03ChaletOwnershipFaction" },
            new() { EditorID = "HonningbrewMeaderyExterior01", FactionEditorID = "HonningbrewMeaderyFaction" },
            new() { EditorID = "0BearQOrigin", FactionEditorID = "TownWindhelmFaction" },
            new() { EditorID = "DragonBridgeFourShieldsTavern", FactionEditorID = "DragonBridgeFourShieldsInnFaction" },
            new() { EditorID = "NightgateInnExterior01", FactionEditorID = "NG_JobMerchantFaction" },
            new() { EditorID = "DLC1DawnguardHQ01", FactionEditorID = "DLC1DawnguardFaction" },
            new() { EditorID = "DLC1HunterWorldFort01", FactionEditorID = "DLC1DawnguardFaction" },
            new() { EditorID = "DLC1HunterWorldFort02", FactionEditorID = "DLC1DawnguardFaction" },
        };
    }
}