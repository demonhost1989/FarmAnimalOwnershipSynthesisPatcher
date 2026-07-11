using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Newtonsoft.Json;
using Noggog;


namespace FarmAnimalOwnershipProject
{

    // Classes //
    public class Program
    {
        public static Settings Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return new Settings();
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<Settings>(json) ?? new Settings();
        }

        public void Save(string path)
        {
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(path, json);
        }
        static Lazy<Settings> LazySettings = new();
        static Settings Settings => LazySettings.Value;


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
        private static void PrintShorterDivider()
        {
            if (_lastWasDivider) return;
            Console.WriteLine("------------------------------");
            _lastWasDivider = true;
        }
        private static void ConsoleWriteLine(string text)
        {
            Console.WriteLine(text);
            _lastWasDivider = false;
        }

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
                list = [];
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

        // Location categories
        public enum LocationCategory
        {
            Town, Farm, Unknown, Mill, Wilderness, Stable
        }

 
        
        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {

            // --- Config load/generate block ---
            string[] tryNames = ["Settings.json", "settings.json"];
            string? configContent = null;

            foreach (var name in tryNames)
            {
                try
                {
                    configContent = state.RetrieveConfigFile(name);
                    break;
                }
                catch (FileNotFoundException)
                {
                    // try next name
                }
            }

            if (configContent is null)
            {
                var defaultSettings = LazySettings.Value;
                configContent = JsonConvert.SerializeObject(defaultSettings, Formatting.Indented);
                try
                {
                    var outPath = Path.Combine(Environment.CurrentDirectory, tryNames[0]);
                    File.WriteAllText(outPath, configContent);
                    ConsoleWriteLine($"Generated default config file: {tryNames[0]}");
                }
                catch (IOException ioEx)
                {
                    ConsoleWriteLine($"WARNING: Failed to write default config file: {ioEx.Message}");
                }
            }

            Settings settings;
            try
            {
                settings = JsonConvert.DeserializeObject<Settings>(configContent!) ?? LazySettings.Value;
            }
            catch (JsonException)
            {
                ConsoleWriteLine("WARNING: Could not parse Settings File; using defaults.");
                settings = LazySettings.Value;
            }
            // End of config

            // Faction dictionary
            var factionsByEdid = new Dictionary<string, IFactionGetter>(StringComparer.OrdinalIgnoreCase);
            foreach (var fac in state.LoadOrder.PriorityOrder.Faction().WinningOverrides())
            {
                if (fac.EditorID != null)
                    factionsByEdid.TryAdd(fac.EditorID, fac);
            }

            // Populate ConventionOverrides from Settings (user-editable list -> lookup dictionary)
            ConventionOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var duplicateOverrideEdids = new List<string>();
            foreach (var entry in Settings.ConventionOverrides ?? [])
            {
                if (string.IsNullOrWhiteSpace(entry.EditorID) || string.IsNullOrWhiteSpace(entry.FactionEditorID))
                    continue;

                var key = entry.EditorID.Trim();
                var value = entry.FactionEditorID.Trim();

                if (!ConventionOverrides.TryAdd(key, value))
                    duplicateOverrideEdids.Add(key);
            }

            if (duplicateOverrideEdids.Count > 0)
            {
                ConsoleWriteLine($"WARNING: Duplicate Convention Override EditorIDs were ignored (first entry wins): {string.Join(", ", duplicateOverrideEdids)}");
            }

            //  Debug: show loaded convention overrides count
            //if (ConventionOverrides.Count > 0)
            //{
            //      ConsoleWriteLine($"Loaded {ConventionOverrides.Count} convention overrides: {string.Join(", ", ConventionOverrides.Keys)}");
            //}

            // Keep track of seen NPCs to avoid duplicates
            var seen = new HashSet<FormKey>();

            // Dictionaries to track patched and skipped animals by cell
            var patchedAnimalsByCell = new Dictionary<string, List<(string Animal, string Plugin, string? OwnerFaction)>>(StringComparer.OrdinalIgnoreCase);
            var skippedAnimalsByCell = new Dictionary<string, List<(string Animal, string Plugin, string Reason)>>(StringComparer.OrdinalIgnoreCase);
            var excludedAnimalsByPlugin = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var excludedCellsByRule = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var excludedLocTypesByRule = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var excludedNamesByRule = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var animalRaceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var patchedRaceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // Counters for summary
            int unknownCount = 0;
            int missingFactionCount = 0;
            int patchedCount = 0;
            int alreadyOwnedCount = 0;
            int excludedCount = 0;
            // Distinct sets for summary (unique EditorIDs)
            var unknownSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var missingFactionSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var patchedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var alreadyOwnedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var excludedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            PrintShortDivider();
            ConsoleWriteLine("PATCHING...".PadLeft(35));
            PrintShortDivider();

            // loops through all placed NPCs in the load order
            foreach (var context in state.LoadOrder.PriorityOrder.PlacedNpc().WinningContextOverrides(state.LinkCache))
            {
                var placedNpc = context.Record;
                var containingCell = FindContainingCell(context, state.LinkCache);
                string cellEdid;

                // Use the actual EditorID when present. Cells without an EditorID are unknown.
                if (containingCell?.EditorID != null)
                {
                    cellEdid = containingCell.EditorID;
                }
                else
                {
                    cellEdid = "Unknown cell"; // exterior cells with no EDID are treated as unknown
                }

                if (!seen.Add(placedNpc.FormKey))
                    continue;

                var npc = placedNpc.Base.TryResolve(state.LinkCache);
                if (npc == null)
                    continue;

                var animalLabel = npc.EditorID ?? "UnknownNPC";
                var pluginName = placedNpc.FormKey.ModKey.FileName;

                // Race check first — only farm-animal races are candidates at all.
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

                // (name-based exclusion is handled later so we record it there under the matched rule)

                // Exclude owned animals
                if (!placedNpc.Owner.IsNull)
                {
                    alreadyOwnedCount++;
                    alreadyOwnedSet.Add(animalLabel);
                    continue;
                }
                // Wildcard-aware cell exclusion
                bool cellExcluded = false;
                if (Settings.ExcludeCellRules != null && Settings.ExcludeCellRules.Count > 0)
                {
                    foreach (var rule in Settings.ExcludeCellRules)
                    {
                        if (RuleMatchesCell(rule, cellEdid))
                        {
                            cellExcluded = true;
                            if (!excludedCellsByRule.TryGetValue(rule, out var cellList))
                                excludedCellsByRule[rule] = cellList = [];

                            // Record the animal name that triggered this cell-rule exclusion so we can count animals per rule
                            cellList.Add(animalLabel);
                            break;
                        }
                    }
                }

                // Location type exclusion (match against location keywords like "LocTypeDungeon")
                if (!cellExcluded && Settings.ExcludeLocTypeRules != null && Settings.ExcludeLocTypeRules.Count > 0)
                {
                    var loc = containingCell?.Location.TryResolve(state.LinkCache);
                    var keywordEdids = loc?.Keywords?
                        .Select(k => k.TryResolve(state.LinkCache)?.EditorID)
                        .Where(e => e != null)
                        .Select(e => e!)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    if (keywordEdids != null && keywordEdids.Count > 0)
                    {
                        foreach (var rule in Settings.ExcludeLocTypeRules)
                        {
                            // Convert wildcard pattern to regex
                            string regexPattern = "^" +
                                System.Text.RegularExpressions.Regex.Escape(rule)
                                    .Replace("\\*", ".*")
                                    .Replace("\\?", ".") +
                                "$";

                            if (keywordEdids.Any(k => System.Text.RegularExpressions.Regex.IsMatch(k, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                            {
                                cellExcluded = true;
                                if (!excludedLocTypesByRule.TryGetValue(rule, out var list))
                                    excludedLocTypesByRule[rule] = list = [];

                                list.Add(animalLabel);
                                break;
                            }
                        }
                    }
                }

                if (cellExcluded)
                {
                    excludedCount++;
                    excludedSet.Add(animalLabel);
                    continue;
                }

                // Wildcard-aware plugin exclusion
                if (IsPluginExcluded(pluginName))
                {
                    if (!excludedAnimalsByPlugin.TryGetValue(pluginName, out var list))
                    {
                        list = [];
                        excludedAnimalsByPlugin[pluginName] = list;
                    }

                    list.Add(animalLabel);
                    excludedCount++;
                    excludedSet.Add(animalLabel);

                    continue;
                }

                // Exclude animals by name terms
                if (Settings.ExcludeNameTerms != null && Settings.ExcludeNameTerms.Count > 0 && Settings.ExcludeNameTerms.Any(term => animalLabel.Contains(term, StringComparison.OrdinalIgnoreCase)))

                {
                    // record in simple plugin list
                    var matchedTerm = Settings.ExcludeNameTerms.First(term => animalLabel.Contains(term, StringComparison.OrdinalIgnoreCase));
                    if (!excludedNamesByRule.TryGetValue(matchedTerm, out var list))
                    {
                        list = [];
                        excludedNamesByRule[matchedTerm] = list;
                    }
                    list.Add(animalLabel);

                    excludedCount++;
                    excludedSet.Add(animalLabel);

                    continue;
                }

                // Matching
                var location = containingCell?.Location.TryResolve(state.LinkCache);
                var (category, matched) = CategorizeLocation(location, state.LinkCache, containingCell);

                string combinedReason = "";

                var townFaction = TryGetTownFaction(matched, factionsByEdid, containingCell);
                if (townFaction == null)
                {
                    combinedReason += "No suitable owner, ";         // Location has no obvious owner faction
                    missingFactionCount++;
                    missingFactionSet.Add(animalLabel);

                    // Only unknown if BOTH category and matched are unknown
                    if (category == LocationCategory.Unknown)
                    {
                        combinedReason += "No suitable location";
                        unknownCount++;
                        unknownSet.Add(animalLabel);
                    }
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
                patchedSet.Add(animalLabel);

                // track patched race counts
                patchedRaceCounts.TryGetValue(displayRace, out var patchedRaceCount);
                patchedRaceCounts[displayRace] = patchedRaceCount + 1;

                if (!patchedAnimalsByCell.TryGetValue(cellEdid, out var patchedList))
                {
                    patchedList = [];
                    patchedAnimalsByCell[cellEdid] = patchedList;
                }

                patchedList.Add((animalLabel, pluginName, townFaction?.EditorID));

            }   // End of main loop
            {

                // Total patched across all cells
                var totalPatched = patchedAnimalsByCell.Values.SelectMany(v => v).Count();

                // PATCHED BY CELL
                // Printout for patched animals by cell
                _lastWasDivider = false;
                PrintShortDivider();
                ConsoleWriteLine("PATCHED BY CELL".PadLeft(36));
                ConsoleWriteLine($"Total patched: {totalPatched}".PadLeft(37));
                PrintShortDivider();

                foreach (var kvp in patchedAnimalsByCell.OrderByDescending(k => k.Value.Count))
                {
                    var cellLabel = kvp.Key;
                    var animals = kvp.Value;

                    var cellPlugin = animals
                        .Select(a => a.Plugin)
                        .Distinct()
                        .FirstOrDefault() ?? "(unknown plugin)";

                    ConsoleWriteLine($"{cellLabel}   ({animals.Count} patched)");

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
                        ConsoleWriteLine($"     [{pluginGroup.Plugin}] ({pluginGroup.Count})");

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
                            ConsoleWriteLine($"          {entry.Count} {entry.Animal}   -> now owned by:  {entry.OwnerFaction}");
                        }
                    }

                    PrintDivider();
                }
                // Total skipped across all cells
                var totalSkipped = skippedAnimalsByCell.Values.SelectMany(v => v).Count();

                // SKIPPED BY CELL
                // Printout for skipped animals by cell
                _lastWasDivider = false;
                PrintShortDivider();
                ConsoleWriteLine("SKIPPED BY CELL".PadLeft(35));
                ConsoleWriteLine($"Total skipped: {totalSkipped}".PadLeft(36));
                PrintShortDivider();

                foreach (var kvp in skippedAnimalsByCell.OrderByDescending(k => k.Value.Count))
                {
                    var cellLabel = kvp.Key;
                    var animals = kvp.Value;

                    ConsoleWriteLine($"{cellLabel} ({animals.Count} skipped)");

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
                        ConsoleWriteLine($"     [{pluginGroup.Plugin}] ({pluginGroup.Count})");

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
                            ConsoleWriteLine($"          {entry.Count} {entry.Animal}   Returned: {entry.Reason}");
                        }
                    }

                    PrintDivider();
                }

                // EXCLUSION SUMMARY
                // Summary printout
                _lastWasDivider = false;
                PrintShortDivider();
                ConsoleWriteLine("EXCLUSION SUMMARY".PadLeft(37));
                PrintShortDivider();

                // Build combined list
                var combined = new List<(string Rule, int Count, string Type)>();

                // Plugin rules (wildcard patterns) - count excluded animal INSTANCES for matching plugins
                foreach (var rule in Settings.ExcludePlugins ?? [])
                {
                    int count = excludedAnimalsByPlugin
                        .Where(kv => RuleMatchesPlugin(rule, kv.Key))
                        .SelectMany(kv => kv.Value)
                        .Count();

                    if (count > 0)
                        combined.Add((Rule: rule, Count: count, Type: "plugin"));
                }

                // Cell rules - count excluded animal INSTANCES per matching cell rule
                foreach (var rule in Settings.ExcludeCellRules ?? [])
                {
                    if (excludedCellsByRule.TryGetValue(rule, out var cells))
                    {
                        int count = cells.Count;
                        if (count > 0)
                            combined.Add((Rule: rule, Count: count, Type: "cell"));
                    }
                }

                // Location type rules - count excluded animal INSTANCES per matching loctype rule
                foreach (var rule in Settings.ExcludeLocTypeRules ?? [])
                {
                    if (excludedLocTypesByRule.TryGetValue(rule, out var names))
                    {
                        int count = names.Count;
                        if (count > 0)
                            combined.Add((Rule: rule, Count: count, Type: "loctype"));
                    }
                }

                // Name term rules
                foreach (var term in Settings.ExcludeNameTerms ?? [])
                {
                    // Count excluded animal INSTANCES recorded under any rule/key that matches this term
                    int count = excludedNamesByRule
                        .Where(kvp => kvp.Key.Contains(term, StringComparison.OrdinalIgnoreCase))
                        .SelectMany(kvp => kvp.Value)
                        .Count();

                    if (count > 0)
                        combined.Add((Rule: term, Count: count, Type: "name"));
                }

                // Actual Exclusion Summary printout
                // Sort by count descending
                foreach (var entry in combined.OrderByDescending(e => e.Count))
                {
                    ConsoleWriteLine($"The rule: {entry.Rule} ({entry.Type}) excluded {entry.Count} animals");
                }

                // Summary printout
                _lastWasDivider = false;
                PrintShortDivider();
                ConsoleWriteLine("GENERAL SUMMARY".PadLeft(35));
                PrintShortDivider();

                var summaryLines = new List<(string Label, int Count, bool ShowRaces)>
                            {
                                ("Farm animals have been asigned owners", patchedCount, true),
                                ("Farm animals were already owned", alreadyOwnedCount, false),
                                ("Farm animals had no suitable owner", missingFactionCount, false),
                                ("Farm animals were in an unsuitable location", unknownCount, false),
                                ("Farm animals were excluded by rules", excludedCount, false)
                            };

                foreach (var (Label, Count, ShowRaces) in summaryLines.OrderByDescending(l => l.Count))
                {
                    ConsoleWriteLine($"{Count} {Label}");

                    if (ShowRaces)
                    {
                        foreach (var kvp in patchedRaceCounts.OrderByDescending(k => k.Value))
                        {
                            ConsoleWriteLine($"    {kvp.Value}  {kvp.Key}");
                        }
                    }
                }

                PrintDivider();
                ConsoleWriteLine("Patching is complete: Scroll up to read a detailed report on what was patched, skipped, and excluded.");
                ConsoleWriteLine("A couple of notes on the report: In the General Summary there is typically a massive overlap between no suitable owner and an unsuitable locationsince they can both be true.");
                ConsoleWriteLine("The Exclusion Summary will only display the NPCs who otherwise matched all requirements");
                PrintDivider();

                return;
            }
        }

        // Start of main
        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .SetAutogeneratedSettings(
                    "Settings",
                    "settings.json",
                    out LazySettings)
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "FarmAnimalOverrides.esp")
                .Run(args);
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

        // Generate root candidates from an EditorID by removing common terms and digits
        // Ensure we return unique candidates only once (preserve a simple stable order)
        private static IEnumerable<string> GetRootsFromEditorId(string? editorId)
        {
            if (string.IsNullOrWhiteSpace(editorId))
                yield break;

            string cleaned = editorId.Trim();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ordered = new List<string>();
            void AddCandidate(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return;
                if (seen.Add(s.Trim())) ordered.Add(s.Trim());
            }

            // Always include the raw value first
            AddCandidate(cleaned);

            // Strip trailing digits
            var digitsStripped = cleaned;
            while (digitsStripped.Length > 0 && char.IsDigit(digitsStripped[^1]))
                digitsStripped = digitsStripped[..^1];
            if (!string.Equals(digitsStripped, cleaned, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(digitsStripped))
                AddCandidate(digitsStripped);

            // Remove the common words anywhere and return that variant (regex-based)
            var wordsToRemove = new[] { "Exterior", "Interior", "Location", "Farm", "House", "Meadery", "Mill", "Village", "Stead", "Homestead", "Hold", "HQ", "Fort", "Tavern", "Inn" };
            var pattern = "(" + string.Join("|", wordsToRemove.Select(w => System.Text.RegularExpressions.Regex.Escape(w))) + ")";
            var removed = System.Text.RegularExpressions.Regex.Replace(cleaned, pattern, "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            AddCandidate(removed);

            // Yield in the collected order, unique
            foreach (var s in ordered)
                yield return s;
        }

        // Dictionary for convention overrides for specific location/cell to faction mappings.
        // Since the naming conventions are not standardized this can't be fully caught by logic alone,
        // so it's now populated from Settings.ConventionOverrides at the start of each run (see RunPatch).
        private static Dictionary<string, string> ConventionOverrides = new(StringComparer.OrdinalIgnoreCase);

        // Try to find a convention override for a given EditorID using exact or partial matching.
        // Partial matching will accept keys that are substrings of the provided EditorID or vice versa.
        private static bool TryFindConventionOverride(string editorId, out string factionEdid)
        {
            factionEdid = string.Empty;
            if (string.IsNullOrWhiteSpace(editorId))
                return false;

            // Exact match first
            if (ConventionOverrides.TryGetValue(editorId, out var direct))
            {
                factionEdid = direct ?? string.Empty;
                return true;
            }

            // find a partial match for cellEdid
            // check if any convention key is contained within the provided editorId
            var match = ConventionOverrides
            .FirstOrDefault(kvp => !string.IsNullOrEmpty(kvp.Key) &&
            editorId.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(match.Value))
            {
                factionEdid = match.Value ?? string.Empty; // found by partial match
                return true;
            }

            // check the other direction: a key that contains the editorId
            var key = ConventionOverrides.Keys.FirstOrDefault(k =>
                !string.IsNullOrEmpty(k) && k.Contains(editorId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(key))
            {
                factionEdid = ConventionOverrides[key] ?? string.Empty;
                return true;
            }

            return false;
        }

        // Faction helper function
        private static IFactionGetter? TryGetTownFaction(
        ILocationGetter? location,
        Dictionary<string, IFactionGetter> factionsByEdid,
        ICellGetter? cell)
        {
            // Convention overrides mapping for cells to faction
            if (cell?.EditorID != null)
            {
                if (TryFindConventionOverride(cell.EditorID, out var cellOverrideEdid))
                {
                    //       ConsoleWriteLine($"Convention override found for cell '{cell.EditorID}' -> '{cellOverrideEdid}'");
                    if (factionsByEdid.TryGetValue(cellOverrideEdid, out var cellOverrideFaction))
                    {
                        //           ConsoleWriteLine($"  Resolved override faction: {cellOverrideFaction.EditorID}");
                        return cellOverrideFaction;
                    }

                    // Fallback: try to find a faction whose EditorID contains the mapped value (handles prefixes/suffixes)
                    var fallbackCellFaction = factionsByEdid.Values
                        .FirstOrDefault(f => f.EditorID != null && (
                            string.Equals(f.EditorID, cellOverrideEdid, StringComparison.OrdinalIgnoreCase) ||
                            f.EditorID.Contains(cellOverrideEdid, StringComparison.OrdinalIgnoreCase)));
                    if (fallbackCellFaction != null)
                    {
                        //            ConsoleWriteLine($"  Found fallback faction for override '{cellOverrideEdid}': {fallbackCellFaction.EditorID}");
                        return fallbackCellFaction;
                    }
                }

                // Also try stripped/root variants of the cell EditorID (handles numeric suffixes or embedded terms)
                foreach (var root in GetRootsFromEditorId(cell.EditorID))
                {
                    if (TryFindConventionOverride(root, out var roEdid) && factionsByEdid.TryGetValue(roEdid, out var roFaction))
                        return roFaction;
                }
            }

            // Convention override mapping for location to faction
            if (location?.EditorID != null)
            {
                if (TryFindConventionOverride(location.EditorID, out var overrideFactionEdid))
                {
                    //       ConsoleWriteLine($"Convention override found for location '{location.EditorID}' -> '{overrideFactionEdid}'");
                    if (factionsByEdid.TryGetValue(overrideFactionEdid, out var overrideFaction))
                    {
                        //            ConsoleWriteLine($"  Resolved override faction: {overrideFaction.EditorID}");
                        return overrideFaction;
                    }

                    // Fallback: try to find a faction whose EditorID contains the mapped value
                    var fallbackLocFaction = factionsByEdid.Values
                        .FirstOrDefault(f => f.EditorID != null && (
                            string.Equals(f.EditorID, overrideFactionEdid, StringComparison.OrdinalIgnoreCase) ||
                            f.EditorID.Contains(overrideFactionEdid, StringComparison.OrdinalIgnoreCase)));
                    if (fallbackLocFaction != null)
                    {
                        //            ConsoleWriteLine($"  Found fallback faction for override '{overrideFactionEdid}': {fallbackLocFaction.EditorID}");
                        return fallbackLocFaction;
                    }
                }

                // Also try stripped/root variants of the location EditorID
                foreach (var root in GetRootsFromEditorId(location.EditorID))
                {
                    if (TryFindConventionOverride(root, out var roEdid) && factionsByEdid.TryGetValue(roEdid, out var roFaction))
                        return roFaction;
                }
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

                // Fallback: some mods use slightly different EditorIDs (prefixes/suffixes or minor variations).
                // Try a best-effort search for a faction whose EditorID contains the baseName and ends with 'Faction'.
                var fallbackTown = factionsByEdid.Values
                    .FirstOrDefault(f => f.EditorID != null
                        && f.EditorID.EndsWith("Faction", StringComparison.OrdinalIgnoreCase)
                        && f.EditorID.Contains(baseName, StringComparison.OrdinalIgnoreCase));
                if (fallbackTown != null)
                    return fallbackTown;

                // Terms stripped from basename
                var roots = new List<string> { baseName };
                var suffixes = new[] { "Farm", "Farms", "House", "Houses", "Meadery", "Mill", "Village", "Stead", "Homestead", "Hold", "Location", "Exterior", "Interior" };
                foreach (var suf in suffixes)
                {
                    if (baseName.EndsWith(suf, StringComparison.OrdinalIgnoreCase) && baseName.Length > suf.Length)
                        roots.Add(baseName[..^suf.Length]);
                }
                // Strip trailing digits (e.g., Name01) as another candidate
                var digitsStripped = baseName;
                while (digitsStripped.Length > 0 && char.IsDigit(digitsStripped[^1]))
                    digitsStripped = digitsStripped[..^1];
                if (digitsStripped.Length > 0 && !roots.Contains(digitsStripped, StringComparer.OrdinalIgnoreCase))
                    roots.Add(digitsStripped);

                foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var match = factionsByEdid.Values
                        .FirstOrDefault(f => f.EditorID != null
                            && f.EditorID.EndsWith("Faction", StringComparison.OrdinalIgnoreCase)
                            && f.EditorID.Contains(root, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                        return match;
                }
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

                // Fallback: best-effort find matching farm faction by baseName
                var fallbackFarm = factionsByEdid.Values
                    .FirstOrDefault(f => f.EditorID != null
                        && f.EditorID.EndsWith("Faction", StringComparison.OrdinalIgnoreCase)
                        && f.EditorID.Contains(baseName, StringComparison.OrdinalIgnoreCase));
                if (fallbackFarm != null)
                    return fallbackFarm;

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

                // Fallback: best-effort find matching mill faction by baseName
                var fallbackMill = factionsByEdid.Values
                    .FirstOrDefault(f => f.EditorID != null
                        && f.EditorID.EndsWith("Faction", StringComparison.OrdinalIgnoreCase)
                        && f.EditorID.Contains(baseName, StringComparison.OrdinalIgnoreCase));
                if (fallbackMill != null)
                    return fallbackMill;

                // Additional fallback: try matching normalized roots from the cell EditorID
                foreach (var root in GetRootsFromEditorId(cell?.EditorID).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var match = factionsByEdid.Values
                        .FirstOrDefault(f => f.EditorID != null
                            && f.EditorID.EndsWith("Faction", StringComparison.OrdinalIgnoreCase)
                            && f.EditorID.Contains(root, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                        return match;
                }
            }

            return null;
        }

        // Cell helper function
        private static ICellGetter? FindContainingCell(
            IModContext<ISkyrimMod, ISkyrimModGetter, IPlacedNpc, IPlacedNpcGetter> context,
            ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            var current = context.Parent;
            while (current != null)
            {
                if (current.Record is ICellGetter cell)
                {
                    // Re-resolve through the link cache to guarantee we get the fully-merged winning
                    // override rather than a minimal stub from whichever plugin owns the placed ref
                    // (stubs can be missing the EDID subrecord entirely).
                    if (linkCache.TryResolve<ICellGetter>(cell.FormKey, out var winningCell))
                        return winningCell;

                    return cell;
                }

                current = current.Parent;
            }

            return null;
        }

        // Location helper function
        public static (LocationCategory category, ILocationGetter? matched)
        CategorizeLocation(ILocationGetter? location, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, ICellGetter? cell)
        {
            // Location-based keyword detection
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

                    if (keywordEdids.Contains("LocTypeMill")
                        || keywordEdids.Contains("LocTypeLumberMill"))
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
                    return (LocationCategory.Stable, location);

                if (cell.EditorID.Contains("village", StringComparison.OrdinalIgnoreCase) || cell.EditorID.Contains("settlement", StringComparison.OrdinalIgnoreCase) || cell.EditorID.Contains("town", StringComparison.OrdinalIgnoreCase) || cell.EditorID.Contains("city", StringComparison.OrdinalIgnoreCase))
                    return (LocationCategory.Town, location);


            }

            return (LocationCategory.Unknown, null);
        }
    }
}