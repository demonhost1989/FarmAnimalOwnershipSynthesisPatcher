using Mutagen.Bethesda.WPF.Reflection.Attributes;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;




namespace FarmAnimalOwnershipProject
{
    // FarmAnimalOwnership settings moved into this shared Settings.cs file
    public class Settings
    {

        // Whether to enable verbose logging (kept for compatibility)
        public bool EnableLogging { get; set; } = false;

        // The animal races we are looking for
        [SettingName("IncludeRaceTerms")]
        [Tooltip("Races to patch")]
        public List<string> IncludeRaceTerms { get; set; } = new()
        {
            "Goat", "Chicken", "Cow", "Horse", "Pig", "Sheep", "Dog", "Cat", "Bunny", "Husky"
        };

        // Animal names we want to exclude
        [SettingName("ExcludeNameTerms")]
        [Tooltip("Actor names to exclude from patching")]
        public List<string> ExcludeNameTerms { get; set; } = new()
        {
            "Wild", "Bandit", "Forsworn", "Sabre", "Pigeon", "Zombie", "Draugr", "Durzog", "Stray"
        };

        // Plugin exclusion  (wildcards supported)
        [SettingName("ExcludePlugins")]
        [Tooltip("Plugins to exclude from patching")]
        public List<string> ExcludePlugins { get; set; } = new()
        {
            "Vigilant.esm", "*FollowerFramework*", "*SkyrimUnderground*", "*HearthFire*", "cc*"
        };

        // Cell exclusion  (wildcards supported)
        [SettingName("ExcludeCellRules")]
        [Tooltip("Cells to exclude from patching")]
        public List<string> ExcludeCellRules { get; set; } = new()
        {
            "BYOH*", "cc*"
        };
    }

}