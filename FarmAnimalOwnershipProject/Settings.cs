using Mutagen.Bethesda.WPF.Reflection.Attributes;
using Newtonsoft.Json;
using System;

namespace FarmAnimalOwnershipProject
{
    // Settings class designed for Synthesis UI (uses Mutagen WPF attributes)
    // Lists are exposed as semicolon-separated strings for easy editing in the UI.
    public class PatcherSettings
    {
        [SettingName("Enable Logging")]
        [Tooltip("Enable verbose logging (kept for compatibility)")]
        [JsonProperty]
        public bool EnableLogging { get; set; } = false;

        [SettingName("Include Race Terms")]
        [Tooltip("Semicolon-separated list of race EDID substrings to include (e.g. Goat;Cow)")]
        [JsonProperty]
        public string IncludeRaceTerms { get; set; } = "Goat;Chicken;Cow;Horse;Pig;Sheep;Dog;Cat;Bunny;Husky";

        [SettingName("Exclude Name Terms")]
        [Tooltip("Semicolon-separated list of NPC name substrings to exclude (e.g. Wild;Bandit)")]
        [JsonProperty]
        public string ExcludeNameTerms { get; set; } = "Wild;Bandit;Forsworn;Sabre;Pigeon;Zombie;Draugr;Durzog";

        [SettingName("Exclude Plugins")]
        [Tooltip("Semicolon-separated list of plugin wildcard patterns to exclude (e.g. Vigilant.esm;*FollowerFramework*)")]
        [JsonProperty]
        public string ExcludePlugins { get; set; } = "Vigilant.esm;*FollowerFramework*;*SkyrimUnderground*;*HearthFire*;cc*";

        [SettingName("Exclude Cell Rules")]
        [Tooltip("Semicolon-separated list of cell EDID wildcard rules to exclude (e.g. BYOH*)")]
        [JsonProperty]
        public string ExcludeCellRules { get; set; } = "BYOH*";
    }
}

