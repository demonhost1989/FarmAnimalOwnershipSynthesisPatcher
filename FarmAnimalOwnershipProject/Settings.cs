using Mutagen.Bethesda.Synthesis.Settings;


namespace FarmAnimalOwnershipProject
{

    // ObjectNameMember targets the class (not a member) and names the member whose live value
    // labels this entry when it appears inside a list. Without it, list rows render with a blank
    // header and are dropped from the breadcrumb, leaving entries indistinguishable in the UI.
    [SynthesisObjectNameMember(nameof(EditorID))]
    public class ManualFactionMatchEntry
    {

        [SynthesisSettingName("Cell or Location EditorID match")]
        [SynthesisTooltip("Tries an exact Cell or Location EditorID first, then a partial match in either direction, also trying IDs without trailing digits. '*' and '?' are literal here.")]
        public string EditorID { get; set; } = string.Empty;

        [SynthesisSettingName("Faction EditorID")]
        [SynthesisTooltip("Faction EditorID to try when the cell or location matches. Uses an exact ID first, then a partial Faction EditorID lookup.")]
        public string FactionEditorID { get; set; } = string.Empty;
    }


    public class VerboseSettings
    {

        [SynthesisSettingName("Placed NPCs seen, by plugin filename")]
        [SynthesisTooltip("Shows each plugin filename's total placed NPCs and how many matched a Race EditorID term before exclusions.")]
        public bool PerPluginCounts { get; set; } = false;

        [SynthesisSettingName("Skipped animals, by cell")]
        [SynthesisTooltip("Lists unowned animals skipped because no owner was found, grouped by cell and plugin filename, with their Base NPC EditorIDs.")]
        public bool SkippedAnimals { get; set; } = false;

        [SynthesisSettingName("Show Base NPC EditorIDs beside race counts")]
        [SynthesisTooltip("Shows each Base NPC EditorID and its patched count beside the race count in the General Summary. Use these IDs with 'Base NPC EditorID matches to exclude'.")]
        public bool PatchedNpcEditorIds { get; set; } = false;

        [SynthesisSettingName("Excluded animals, by race and by cell")]
        [SynthesisTooltip("Shows excluded animal counts by race and by cell, with the matched exclusion rule for each cell group.")]
        public bool ExcludedAnimals { get; set; } = false;

        [SynthesisSettingName("Exclusion details")]
        [SynthesisTooltip("Adds Base NPC EditorIDs and counts under each rule in the Exclusion Summary, plus the count of already-owned animals filtered out of voting in the General Summary.")]
        public bool ExclusionDetail { get; set; } = false;

        [SynthesisSettingName("Mark fuzzy Faction EditorID fallbacks")]
        [SynthesisTooltip("Adds '(fuzzy)' to patched ownership reasons when a loose Faction EditorID lookup succeeds after an exact lookup fails.")]
        public bool FuzzyMatchDetail { get; set; } = false;

        [SynthesisSettingName("Timing breakdown")]
        [SynthesisTooltip("Prints how long each phase of the run took. Useful when the patcher feels slow on a large load order.")]
        public bool Timing { get; set; } = false;
    }


    [SynthesisObjectNameMember(nameof(PluginName))]
    public class PluginFactionOverrideEntry
    {

        [SynthesisSettingName("Plugin filename match")]
        [SynthesisTooltip("Checks the winning placed-animal plugin filename. Entries without '*' or '?' use Contains match; entries with either use Wildcard match.")]
        public string PluginName { get; set; } = string.Empty;

        [SynthesisSettingName("Faction EditorID")]
        [SynthesisTooltip("Faction EditorID to try when the plugin filename matches. Uses an exact ID first, then a partial Faction EditorID lookup.")]
        public string FactionEditorID { get; set; } = string.Empty;
    }


    public class Settings
    {

        [SynthesisSettingName("Race EditorID matches")]
        [SynthesisTooltip("Checks Race EditorIDs. Entries without '*' or '?' use Contains match; entries with either use Wildcard match. 'Cock' matches 'mihailcockatricerace2'.")]
        public List<string> IncludeRaceTerms { get; set; } =
        [
            "Goat", "Chicken", "Cow", "Horse", "Pig", "Sheep", "Dog", "Cat", "Bunny", "Husky", "Geese",
            "Goose", "Rabbit", "Pet", "Duck", "Rooster", "Lamb", "Foal", "Puppy", "Kitten", "Calf", "Cock",
            "Domestic", "MihailGuar", "MihailKagouti", "BantamGuar",

        ];

        [SynthesisSettingName("Owner EditorID matches excluded from voting")]
        [SynthesisTooltip("Checks current Owner EditorIDs on already-owned animals for the vote only. Entries without '*' or '?' use Contains match; entries with either use Wildcard match. Other faction matches can still choose that owner.")]
        public List<string> ExcludeOwnerNames { get; set; } =
        [
            "Player", "CW", "Bandit", "Hagraven", "Fort", "Draugr", "JobMerchantFaction",
            "Fake", "CarriageDriver", "CarriageSystemFaction", "RiverwoodCamillaFaction", "Service",
        ];

        [SynthesisSettingName("Minimum owned animals for ownership vote")]
        [SynthesisTooltip("Minimum number of eligible already-owned animals in a cell before the ownership-vote fallback can be used.")]
        public int MinimumOwnedObjectsForMajority { get; set; } = 1;

        [SynthesisSettingName("Base NPC EditorID matches to exclude")]
        [SynthesisTooltip("Checks Base NPC EditorIDs of unowned race-matched animals, not Race EditorIDs or display names. Entries without '*' or '?' use Contains match; entries with either use Wildcard match. 'Cockatrice' matches any Base NPC EditorID containing it.")]
        public List<string> ExcludeNameTerms { get; set; } =
        [
            "Wild", "Bandit", "Forsworn", "Sabre", "Pigeon", "Zombie", "Draugr", "Durzog", "Stray", "Dead", "Ghost", "Cockatrice",
            "Vampire", "Necromancer", "Bone", "Feral", "Giant", "Dragon", "Troll", "ShellBug", "Netch", "BYOH",
            "Player", "CW",
        ];

        [SynthesisSettingName("Plugin filename matches to exclude")]
        [SynthesisTooltip("Checks the winning placed-animal plugin filename. Entries without '*' or '?' use Contains match; entries with either use Wildcard match.")]
        public List<string> ExcludePlugins { get; set; } =
        [
            "Vigilant", "SkyrimUnderground", "HearthFire", "cc", "Glenmoril", "HorrorOfMorthal", "BattleAftermath",
            "CWB",
        ];

        [SynthesisSettingName("Cell EditorID matches to exclude")]
        [SynthesisTooltip("Checks the containing Cell EditorID, not its Location EditorID. Entries without '*' or '?' use Contains match; entries with either use Wildcard match.")]
        public List<string> ExcludeCellRules { get; set; } =
        [
            "BYOH", "cc", "Helgen", "Labyrinthian", "POI", "CW", "DrelassCottage", "Attack",
        ];

        [SynthesisSettingName("LocType keyword EditorID matches to exclude")]
        [SynthesisTooltip("Checks LocType-prefixed keyword EditorIDs on the cell's Location. Entries without '*' or '?' use Contains match; entries with either use Wildcard match.")]
        public List<string> ExcludeLocTypeRules { get; set; } =
        [
            "Dungeon", "AnimalDen", "Bandit", "DragonLair", "Draugr", "Dwarven",
            "Falmer", "GiantCamp", "Hagraven", "Spriggan", "Vampire", "Warlock",
            "Werewolf", "Forsworn", "Cave", "Ruin", "PlayerHouse", "Lair",
        ];

        [SynthesisSettingName("Plugin faction fallback (plugin filename -> faction EditorID)")]
        [SynthesisTooltip("After earlier owner rules fail, checks the winning plugin filename with Contains match or Wildcard match, then tries the listed Faction EditorID.")]
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

        [SynthesisSettingName("Manual faction matches (cell/location EditorID -> faction EditorID)")]
        [SynthesisTooltip("Tries an exact Cell or Location EditorID first, then a partial match in either direction, also trying IDs without trailing digits. '*' and '?' are literal here.")]
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

        [SynthesisSettingName("Verbose logging")]
        [SynthesisTooltip("Optional report details, all off by default.")]
        public VerboseSettings Verbose { get; set; } = new();
    }
}
