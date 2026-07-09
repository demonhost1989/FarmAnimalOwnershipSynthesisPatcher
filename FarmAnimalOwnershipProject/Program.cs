using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Synthesis.States;
using Noggog;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.Json;

namespace FarmAnimalOwnershipProject
{
    public class Program
    {

        // Custom divider and printout function for readability
        private static bool _lastWasDivider = false;
        private static void PrintDivider()
        {
            if (_lastWasDivider) return;
            Console.WriteLine("------------------------------------------------------------------------------------------------------------------------");
            _lastWasDivider = true;
        }

        private static void PrintShortDivider()
        {
            if (_lastWasDivider) return;
            Console.WriteLine("------------------------------------------------------------");
            _lastWasDivider = true;
        }

        private static void ConsoleWriteLine(string text)
        {
            Console.WriteLine(text);
            _lastWasDivider = false;
        }

        // Runtime settings instance (defaults from Settings class)
        private static readonly Settings Settings = new Settings();

        // Helper function to add skipped animals to the dictionary
        private static void AddSkip(
            Dictionary<string, List<(string Animal, string Plugin, string Reason)>> dict,
            string animal,
            string plugin,
            string cellEdid,
            string reason)
        {
            string key = cellEdid ?? "(unknown cell)";

            if (!dict.TryGetValue(key, out var list))
            {
                list = new List<(string Animal, string Plugin, string Reason)>();
                dict[key] = list;
            }

            list.Add((animal, plugin, reason));
        }

        // Wildcard-aware plugin exclusion
        private static bool IsPluginExcluded(string pluginName)
        {
            foreach (var pattern in Settings.ExcludePlugins)
            {
                // Convert wildcard pattern to regex
                string regexPattern = "^" +
                    System.Text.RegularExpressions.Regex.Escape(pattern)
                        .Replace("\\*", ".*")
                        .Replace("\\?", ".") +
                        "$";

                if (System.Text.RegularExpressions.Regex.IsMatch(
                    pluginName, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        private static bool RuleMatchesCell(string rule, string cellEdid) // align
        {
            string regexPattern = "^" +
                System.Text.RegularExpressions.Regex.Escape(rule)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") +
                "$";

            return System.Text.RegularExpressions.Regex.IsMatch(
                cellEdid, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
           
        // Main entry point for the patcher
        public static async Task Main(string[] args)
        {
            await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "FarmAnimalOwnership.esp")
                .Run(args);
        }

        // Location categories
        public enum LocationCategory
        {
            Town, Farm, Unknown, Mill, Wilderness, Stable
        }
        
            // Main patching logic
            static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
                
            {

                _lastWasDivider = false;
                PrintDivider();
                ConsoleWriteLine("PATCHING...".PadLeft(32));
                PrintDivider();

                // Faction dictionary
                var factionsByEdid = new Dictionary<string, IFactionGetter>(StringComparer.OrdinalIgnoreCase);
                foreach (var fac in state.LoadOrder.PriorityOrder.Faction().WinningOverrides())
                {
                    if (fac.EditorID != null)
                        factionsByEdid.TryAdd(fac.EditorID, fac);
                }

                // Keep track of seen NPCs to avoid duplicates
                var seen = new HashSet<FormKey>();

                // Dictionaries to track patched and skipped animals by cell
                var patchedAnimalsByCell = new Dictionary<string, List<(string Animal, string Plugin, string? OwnerFaction)>>(StringComparer.OrdinalIgnoreCase);
                var skippedAnimalsByCell = new Dictionary<string, List<(string Animal, string Plugin, string Reason)>>(StringComparer.OrdinalIgnoreCase);
                var excludedAnimalsByPlugin = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                var excludedCellsByRule = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                var excludedNamesByRule = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                var animalRaceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var patchedRaceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                // Counters for summary
                int unknownCount = 0;
                int missingFactionCount = 0;
                int patchedCount = 0;
                int alreadyOwnedCount = 0;

                // Start of main
                // loops through all placed NPCs in the load order
                foreach (var context in state.LoadOrder.PriorityOrder.PlacedNpc().WinningContextOverrides(state.LinkCache))
                {
                    var placedNpc = context.Record;
                    var containingCell = FindContainingCell(context);
                    string cellEdid;

                    if (containingCell?.EditorID != null)
                    {
                        if (containingCell.EditorID.Contains("Wilderness", StringComparison.OrdinalIgnoreCase))
                            cellEdid = "Wilderness";
                        else
                            cellEdid = containingCell.EditorID;
                    }
                    else
                    {
                        cellEdid = "Wilderness"; // exterior cells with no EDID
                    }
                    {
                        if (!seen.Add(placedNpc.FormKey))
                            continue;
                    }
                    var npc = placedNpc.Base.TryResolve(state.LinkCache);
                    if (npc == null)
                        continue;

                    var animalLabel = npc.EditorID ?? "UnknownNPC";
                    var pluginName = placedNpc.FormKey.ModKey.FileName;

                    // Race check first — only farm-animal races are candidates at all.
                    // (Not a farm animal race: skip silently, don't count anywhere.)
                    var raceEdid = npc.Race.TryResolve(state.LinkCache)?.EditorID ?? "UnknownRace";
                    bool isFarmAnimalRace = Settings.IncludeRaceTerms != null &&
                        Settings.IncludeRaceTerms.Any(term => raceEdid.Contains(term, StringComparison.OrdinalIgnoreCase));

                    if (!isFarmAnimalRace)
                        continue;

                    // Clean display name for race (strip trailing "Race")
                    var displayRace = raceEdid.EndsWith("Race", StringComparison.OrdinalIgnoreCase)
                        ? raceEdid[..^"Race".Length]
                        : raceEdid;

                    // Track race counts for the summary
                    animalRaceCounts.TryGetValue(displayRace, out var raceCount);
                    animalRaceCounts[displayRace] = raceCount + 1;

                    // Now exclude farm animals by name terms (record them for the exclusion summary)
                    if (Settings.ExcludeNameTerms != null && Settings.ExcludeNameTerms.Length > 0 &&
                        Settings.ExcludeNameTerms.Any(term => animalLabel.Contains(term, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!excludedNamesByRule.TryGetValue(animalLabel, out var list))
                        {
                            list = new List<string>();
                            excludedNamesByRule[animalLabel] = list;
                        }
                        list.Add(animalLabel);

                        continue;
                    }

                    // Wildcard-aware cell exclusion
                    bool cellExcluded = false;
                    if (Settings.ExcludeCellRules != null && Settings.ExcludeCellRules.Length > 0)
                    {
                        foreach (var rule in Settings.ExcludeCellRules)
                        {
                            if (RuleMatchesCell(rule, cellEdid))
                            {
                                cellExcluded = true;
                                if (!excludedCellsByRule.TryGetValue(rule, out var cellList))
                                    excludedCellsByRule[rule] = cellList = new List<string>();

                                cellList.Add(cellEdid);
                                break;
                            }
                        }
                    }

                    if (cellExcluded)
                        continue;
                    // Wildcard-aware plugin exclusion
                    if (IsPluginExcluded(pluginName))
                    {
                        if (!excludedAnimalsByPlugin.TryGetValue(pluginName, out var list))
                        {
                            list = new List<string>();
                            excludedAnimalsByPlugin[pluginName] = list;
                        }

                        list.Add(animalLabel);

                        continue;
                    }


                    // Exclude animals by name terms first (record them for the exclusion summary)
                    if (Settings.ExcludeNameTerms != null && Settings.ExcludeNameTerms.Length > 0 && Settings.ExcludeNameTerms.Any(term => animalLabel.Contains(term, StringComparison.OrdinalIgnoreCase)))

                    {
                        // record in simple plugin list
                        var matchedTerm = Settings.ExcludeNameTerms.First(term => animalLabel.Contains(term, StringComparison.OrdinalIgnoreCase));
                        if (!excludedNamesByRule.TryGetValue(matchedTerm, out var list))
                        {
                            list = new List<string>();
                            excludedNamesByRule[matchedTerm] = list;
                        }
                        list.Add(animalLabel);

                        continue;
                    }


                    // Exclude owned animals
                    if (!placedNpc.Owner.IsNull)
                    {
                        alreadyOwnedCount++;
                        continue;
                    }

                    // Matching
                    var location = containingCell?.Location.TryResolve(state.LinkCache);
                    var (category, matched) = CategorizeLocation(location, state.LinkCache, containingCell);

                    string combinedReason = "";

                    var townFaction = TryGetTownFaction(matched, factionsByEdid, containingCell);
                    if (townFaction == null)
                    {
                        combinedReason += "No suitable owner ";         // Location has no obvious owner faction
                        missingFactionCount++;
                    }

                    // Only unknown if BOTH category and matched are unknown
                    if (category == LocationCategory.Unknown && matched == null)
                    {
                        combinedReason += "and no suitable location";
                        unknownCount++;
                    }


                    if (combinedReason.Length > 0)
                    {
                        combinedReason = combinedReason.TrimEnd(' ', ' ');
                        AddSkip(skippedAnimalsByCell, animalLabel, pluginName, cellEdid, combinedReason);
                        continue;
                    }

                    // Track patched races
                    var patchNpc = context.GetOrAddAsOverride(state.PatchMod);
                    patchNpc.Owner.SetTo(townFaction);
                    patchNpc.FactionRank = 0;
                    patchedCount++;

                    // track patched race counts
                    patchedRaceCounts.TryGetValue(displayRace, out var patchedRaceCount);
                    patchedRaceCounts[displayRace] = patchedRaceCount + 1;

                    if (!patchedAnimalsByCell.TryGetValue(cellEdid, out var patchedList))
                    {
                        patchedList = new List<(string Animal, string Plugin, string? OwnerFaction)>();
                        patchedAnimalsByCell[cellEdid] = patchedList;
                    }

                    patchedList.Add((animalLabel, pluginName, townFaction?.EditorID));

                }   // End of main loop
                {
                    // Printout for patched animals by cell
                    _lastWasDivider = false;
                    PrintShortDivider();
                    ConsoleWriteLine("PATCHED BY CELL".PadLeft(34));
                    PrintShortDivider();
                    _lastWasDivider = false;
                    PrintDivider();

                    foreach (var kvp in patchedAnimalsByCell.OrderByDescending(k => k.Value.Count))
                    {
                        var cellLabel = kvp.Key;
                        var animals = kvp.Value;

                        var cellPlugin = animals
                            .Select(a => a.Plugin)
                            .Distinct()
                            .FirstOrDefault() ?? "(unknown plugin)";

                        ConsoleWriteLine($"   {cellLabel}   ({animals.Count} patched)");

                        // Group by plugin first, then by animal+owner within each plugin.
                        var byPlugin = animals
                            .GroupBy(a => a.Plugin)
                            .Select(g => new
                            {
                                Plugin = g.Key,
                                Count = g.Count(),
                                Animals = g.ToList()
                            })
                            .OrderByDescending(p => p.Count);

                        foreach (var pluginGroup in byPlugin)
                        {
                            ConsoleWriteLine($"        [{pluginGroup.Plugin}] ({pluginGroup.Count})");

                            var byAnimal = pluginGroup.Animals
                                .GroupBy(a => new { a.Animal, a.OwnerFaction })
                                .Select(g => new
                                {
                                    g.Key.Animal,
                                    g.Key.OwnerFaction,
                                    Count = g.Count()
                                })
                                .OrderByDescending(a => a.Count);

                            foreach (var entry in byAnimal)
                            {
                                ConsoleWriteLine($"             {entry.Count} {entry.Animal}   Is now owned by:  {entry.OwnerFaction}");
                            }
                        }

                        PrintDivider();
                    }

                    // Printout for skipped animals by cell
                    _lastWasDivider = false;
                    PrintShortDivider();
                    ConsoleWriteLine("SKIPPED BY CELL".PadLeft(33));
                    PrintShortDivider();
                    _lastWasDivider = false;
                    PrintDivider();

                    foreach (var kvp in skippedAnimalsByCell.OrderByDescending(k => k.Value.Count))
                    {
                        var cellLabel = kvp.Key;
                        var animals = kvp.Value;

                        ConsoleWriteLine($"   {cellLabel} ({animals.Count} skipped)");

                        // Group by plugin first, then by animal+reason within each plugin.
                        var byPlugin = animals
                            .GroupBy(a => a.Plugin)
                            .Select(g => new
                            {
                                Plugin = g.Key,
                                Count = g.Count(),
                                Animals = g.ToList()
                            })
                            .OrderByDescending(p => p.Count);

                        foreach (var pluginGroup in byPlugin)
                        {
                            ConsoleWriteLine($"        [{pluginGroup.Plugin}] ({pluginGroup.Count})");

                            var byAnimal = pluginGroup.Animals
                                .GroupBy(a => new { a.Animal, a.Reason })
                                .Select(g => new
                                {
                                    g.Key.Animal,
                                    g.Key.Reason,
                                    Count = g.Count()
                                })
                                .OrderByDescending(a => a.Count);

                            foreach (var entry in byAnimal)
                            {
                                ConsoleWriteLine($"             {entry.Count} {entry.Animal}   Returned: {entry.Reason}");
                            }
                        }

                        PrintDivider();
                    }



                    // Summary printout
                    _lastWasDivider = false;
                    PrintShortDivider();
                    ConsoleWriteLine("EXCLUSION SUMMARY".PadLeft(37));
                    PrintShortDivider();

                    // Build combined list
                    var combined = new List<(string Rule, int Count, string Type)>();

                    // Plugin rules (wildcard patterns)
                    var excludedPluginNames = excludedAnimalsByPlugin.Keys.ToList();
                    foreach (var rule in Settings.ExcludePlugins ?? Array.Empty<string>())
                    {
                        int count = excludedPluginNames
                            .Where(pluginName => RuleMatchesPlugin(rule, pluginName))
                            .Distinct()
                            .Count();

                        if (count > 0)
                            combined.Add((Rule: rule, Count: count, Type: "plugin"));
                    }

                    // Cell rules
                    foreach (var rule in Settings.ExcludeCellRules ?? Array.Empty<string>())
                    {
                        if (excludedCellsByRule.TryGetValue(rule, out var cells))
                        {
                            int count = cells.Distinct().Count();
                            if (count > 0)
                                combined.Add((Rule: rule, Count: count, Type: "cell"));
                        }
                    }
                    // Name term rules

                    foreach (var term in Settings.ExcludeNameTerms ?? Array.Empty<string>())
                    {

                        int count = excludedNamesByRule
                            .Where(kvp => kvp.Key.Contains(term, StringComparison.OrdinalIgnoreCase))
                            .Distinct()
                            .Sum(kvp => kvp.Value.Count);

                        if (count > 0)
                            combined.Add((Rule: term, Count: count, Type: "name"));
                    }

                    // Sort by count descending
                    foreach (var entry in combined.OrderByDescending(e => e.Count))
                    {
                        ConsoleWriteLine($"The {entry.Type} {entry.Rule} was excluded {entry.Count} time(s)");
                    }

                    // Summary printout
                    _lastWasDivider = false;
                    PrintShortDivider();
                    ConsoleWriteLine("SUMMARY".PadLeft(32));
                    PrintShortDivider();

                    var summaryLines = new List<(string Label, int Count, bool ShowRaces)>
                            {
                                ("Farm animals have been asigned owners", patchedCount, true),
                                ("Farm animals were already owned", alreadyOwnedCount, false),
                                ("Farm animals had no suitable owner", missingFactionCount, false),
                                ("Farm animals were in an unsuitable location", unknownCount, false)
                            };

                    foreach (var line in summaryLines.OrderByDescending(l => l.Count))
                    {
                        ConsoleWriteLine($"{line.Count} {line.Label}");

                        if (line.ShowRaces)
                        {
                            foreach (var kvp in patchedRaceCounts.OrderByDescending(k => k.Value))
                            {
                                ConsoleWriteLine($"    {kvp.Value}  {kvp.Key}");
                            }
                        }
                    }

                    PrintShortDivider();
                    ConsoleWriteLine("Patching is complete: Scroll up to read a detailed report on what was patched, skipped, and excluded");
                    PrintShortDivider();

                }
            }
        
        // Summary exclusion helper
        private static bool RuleMatchesPlugin(string rule, string pluginName)
        {
            string regexPattern = "^" +
                System.Text.RegularExpressions.Regex.Escape(rule)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") +
                "$";

            return System.Text.RegularExpressions.Regex.IsMatch(
                pluginName, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        // Dictionary for convention overrides for specific location to faction mappings
        // Since the naming conventions are not standardized I couldn't make a clean catch all solution
        private static readonly Dictionary<string, string> ConventionOverrides = new(StringComparer.OrdinalIgnoreCase)
        {
            //      ["PelagiaFarmLocation"] = "PelagiaFarmFaction",
            //      ["HollyfrostFarmLocation"] = "HollyFrostFarmFaction",           // note the difference in capitalization
            //      ["HlaaluFarmLocation"] = "HlaaluFarmFaction",
            //      ["KatlasFarmLocation"] = "TownSolitudeFaction",                 // note how this location does not have their own faction
            //      ["ChillfurrowFarmLocation"] = "ChillfurrowFarmFaction",
            //      ["BrandyMugFarmLocation"] = "BrandyMugFarmFaction",
            //      ["BattleBornFarmLocation"] = "TownWhiterunFaction",
            ["DawnstarSanctuaryLocation"] = "DarkBrotherhoodFaction",
            ["DLC2SkaalVillageLocation"] = "DLC2SVGreathallFaction",        // note that the village has no town faction
            //      ["HalfmoonMillLocation"] = "HalfMoonMillFaction",
            //      ["SolitudeSawmillLocation"] = "SolitudeSawmillFaction",
            ["BearsCaveMillLocation"] = "RG439BearsCaveMillFaction",        // note that modded locations/factions often use an added prefix or suffix
            ["DLC2RavenRockLocation"] = "DLC2RRBulwarkFaction",
            //      ["FrostRiverFarmLocation"] = "FrostRiverFarmFaction",
            ["KynesgroveFarmsLocationTGCoKG"] = "KynesgroveRagnasAndHerleifsHouseFactionTGCoKG",
            ["KynesgroveGalasSteadLocationTGCoKG"] = "KynesgroveGalasHouseFactionTGCoKG",
            ["RoriksteadLemkilsFarmLocation"] = "RoriksteadLemkilsFarmFaction"
        };

        // Dictionary for hold capital prefixes to faction mappings
        private static readonly Dictionary<string, string> HoldCapitalPrefixes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Whiterun"] = "TownWhiterunFaction",
            ["Solitude"] = "TownSolitudeFaction",
            ["Riften"] = "TownRiftenFaction",
            ["Windhelm"] = "TownWindhelmFaction",
            ["Markarth"] = "TownMarkarthFaction",
            ["Falkreath"] = "TownFalkreathFaction",
            ["Morthal"] = "TownMorthalFaction",
            ["Dawnstar"] = "TownDawnstarFaction",
            ["Winterhold"] = "TownWinterholdFaction"
        };

        // Faction helper function
        private static IFactionGetter? TryGetTownFaction(
        ILocationGetter? location,
        Dictionary<string, IFactionGetter> factionsByEdid,
        ICellGetter? cell)
        {
            // Convention overrides
            if (location?.EditorID != null &&
                ConventionOverrides.TryGetValue(location.EditorID, out var overrideFactionEdid) &&
                factionsByEdid.TryGetValue(overrideFactionEdid, out var overrideFaction))
            {
                return overrideFaction;
            }

            // Location-based TownXFaction
            if (location?.EditorID != null)
            {
                var baseName = location.EditorID.EndsWith("Location")
                    ? location.EditorID[..^"Location".Length]
                    : location.EditorID;

                var townCandidate = $"Town{baseName}Faction";

                if (factionsByEdid.TryGetValue(townCandidate, out var faction))
                    return faction;
            }

            // Location-based FarmXFaction
            if (location?.EditorID != null)
            {
                var baseName = location.EditorID.EndsWith("FarmLocation", StringComparison.OrdinalIgnoreCase)
                    ? location.EditorID[..^"FarmLocation".Length]
                    : location.EditorID;

                var farmCandidate = $"{baseName}FarmFaction";

                var faction = factionsByEdid.Values
                    .FirstOrDefault(f => string.Equals(f.EditorID, farmCandidate, StringComparison.OrdinalIgnoreCase));

                if (faction != null)
                    return faction;
            }

            // Location-based MillXFaction
            if (location?.EditorID != null)
            {
                var baseName = location.EditorID.EndsWith("MillLocation", StringComparison.OrdinalIgnoreCase)
                    ? location.EditorID[..^"MillLocation".Length]
                    : location.EditorID;

                var millCandidate = $"{baseName}MillFaction";

                var faction = factionsByEdid.Values
                    .FirstOrDefault(f => string.Equals(f.EditorID, millCandidate, StringComparison.OrdinalIgnoreCase));

                if (faction != null)
                    return faction;
            }

            // Cell-based hold-capital prefix detection
            if (cell?.EditorID != null)
            {
                foreach (var kvp in HoldCapitalPrefixes)
                {
                    if (cell.EditorID.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        if (factionsByEdid.TryGetValue(kvp.Value, out var faction))
                            return faction;
                    }
                }
            }

            return null;
        }

        // Cell helper function
        private static ICellGetter? FindContainingCell(
            IModContext<ISkyrimMod, ISkyrimModGetter, IPlacedNpc, IPlacedNpcGetter> context)
        {
            var current = context.Parent;
            while (current != null)
            {
                if (current.Record is ICellGetter cell)
                    return cell;

                current = current.Parent;
            }

            return null;
        }

        // Location helper function
        public static (LocationCategory category, ILocationGetter? matched)
        CategorizeLocation(ILocationGetter? location, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, ICellGetter? cell)
        {
            // 1. Location-based keyword detection
            if (location != null)
            {
                var keywordEdids = location.Keywords?
                    .Select(k => k.TryResolve(linkCache)?.EditorID)
                    .Where(e => e != null)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (keywordEdids != null)
                {
                    if (keywordEdids.Contains("LocTypeFarm"))
                        return (LocationCategory.Farm, location);

                    if (keywordEdids.Contains("LocTypeMill"))
                        return (LocationCategory.Mill, location);

                    if (keywordEdids.Contains("LocTypeSettlement")
                        || keywordEdids.Contains("LocTypeTown")
                        || keywordEdids.Contains("LocTypeCity")
                        || keywordEdids.Contains("LocTypeVillage"))
                        return (LocationCategory.Town, location);
                }
            }

            // Cell EditorID detection
            if (cell?.EditorID != null)
            {
                if (cell.EditorID.Contains("wilderness", StringComparison.OrdinalIgnoreCase))
                    return (LocationCategory.Wilderness, location);

                if (cell.EditorID.Contains("farm", StringComparison.OrdinalIgnoreCase))
                    return (LocationCategory.Farm, location);

                if (cell.EditorID.Contains("mill", StringComparison.OrdinalIgnoreCase))
                    return (LocationCategory.Mill, location);

                if (cell.EditorID.Contains("stable", StringComparison.OrdinalIgnoreCase))
                    return (LocationCategory.Farm, location);

                if (cell.EditorID.Contains("village", StringComparison.OrdinalIgnoreCase) || cell.EditorID.Contains("settlement", StringComparison.OrdinalIgnoreCase) || cell.EditorID.Contains("town", StringComparison.OrdinalIgnoreCase))
                    return (LocationCategory.Town, location);

                // Hold-capital prefix detection for city districts
                foreach (var kvp in HoldCapitalPrefixes)
                {
                    if (cell.EditorID.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                        return (LocationCategory.Town, location);
                }
            }

            // Location EditorID fallback
            if (location?.EditorID != null)
            {
                foreach (var kvp in HoldCapitalPrefixes)
                {
                    if (location.EditorID.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                        return (LocationCategory.Town, location);
                }
            }

            return (LocationCategory.Unknown, null);
        }
        
    }
}

