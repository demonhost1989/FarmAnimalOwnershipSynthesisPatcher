using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Newtonsoft.Json;
using Noggog;


namespace FarmAnimalOwnershipProject
{
    public class Program
    {
        // ------------------------------------------------------------------
        // Settings load/save
        // ------------------------------------------------------------------

        // Without Replace, Json.NET appends deserialized list entries onto the defaults from the
        // property initializers, duplicating every rule/override once a settings file exists.
        private static readonly JsonSerializerSettings SettingsJsonOptions = new()
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        };

        public static Settings Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return new Settings();
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<Settings>(json, SettingsJsonOptions) ?? new Settings();
        }

        public void Save(string path)
        {
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(path, json);
        }

        static Lazy<Settings> LazySettings = new();
        static Settings Settings => LazySettings.Value;

        // ------------------------------------------------------------------
        // Console output helpers
        // ------------------------------------------------------------------

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

        // ------------------------------------------------------------------
        // Small utility helpers
        // ------------------------------------------------------------------

        // Adds an entry to a "skipped animals by cell" dictionary, creating the list if needed.
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

        // Partial-match (substring) plugin exclusion.
        private static bool IsPluginExcluded(string pluginName)
        {
            return Settings.ExcludePlugins.Any(pattern =>
                pluginName.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        // Partial-match (substring) cell exclusion.
        private static bool RuleMatchesCell(string rule, string cellEdid)
        {
            return cellEdid.Contains(rule, StringComparison.OrdinalIgnoreCase);
        }

        // Partial-match (substring) plugin rule, used only in the summary/report section.
        private static bool RuleMatchesPlugin(string rule, string pluginName)
        {
            return pluginName.Contains(rule, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------
        // Faction resolution
        // ------------------------------------------------------------------

        // Cell/Location EditorID -> Faction EditorID manual faction matches, populated from
        // Settings.ManualFactionMatches at the start of each run (see RunPatch). Naming
        // conventions across mods aren't standardized, so this can't be fully caught by
        // logic alone.
        private static Dictionary<string, string> ManualFactionMatches = new(StringComparer.OrdinalIgnoreCase);

        // Finds a manual faction match for a given EditorID using partial (substring, either
        // direction) matching. The longest matching key wins, so a specific key like
        // "KynesgroveFarmsLocationTGCoKG" beats a broad one like "Kynesgrove".
        private static bool TryFindPartialManualMatch(string editorId, out string factionEdid)
        {
            factionEdid = string.Empty;
            if (string.IsNullOrWhiteSpace(editorId))
                return false;

            var match = ManualFactionMatches
                .Where(kvp => !string.IsNullOrEmpty(kvp.Key)
                    && (editorId.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase)
                        || kvp.Key.Contains(editorId, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(kvp => kvp.Key.Length)
                .FirstOrDefault();

            if (string.IsNullOrEmpty(match.Value))
                return false;

            factionEdid = match.Value;
            return true;
        }

        // Resolves an override's faction EditorID to an actual faction record (exact first, then fuzzy).
        private static IFactionGetter? ResolveOverrideFaction(string factionEdid, Dictionary<string, IFactionGetter> factionsByEdid)
        {
            if (string.IsNullOrWhiteSpace(factionEdid))
                return null;

            if (factionsByEdid.TryGetValue(factionEdid, out var exact))
                return exact;

            return TryFuzzyFactionMatch(factionEdid, factionsByEdid);
        }

        // Generates root candidates from an EditorID by stripping trailing digits (e.g. "Name01" -> "Name").
        // Always yields the raw value first; each candidate is returned at most once.
        private static IEnumerable<string> GetRootsFromEditorId(string? editorId)
        {
            if (string.IsNullOrWhiteSpace(editorId))
                yield break;

            var cleaned = editorId.Trim();
            yield return cleaned;

            var digitsStripped = cleaned;
            while (digitsStripped.Length > 0 && char.IsDigit(digitsStripped[^1]))
                digitsStripped = digitsStripped[..^1];

            if (digitsStripped.Length > 0 && !string.Equals(digitsStripped, cleaned, StringComparison.OrdinalIgnoreCase))
                yield return digitsStripped;
        }

        // Common suffixes stripped from a location's base name to build extra faction-name candidates
        // (e.g. "LemkilsFarmLocation" -> base "LemkilsFarm" -> also try "Lemkils").
        private static readonly string[] LocationNameSuffixes =
            ["Farm", "House", "Meadery", "Mill", "Village", "Stead", "Hold", "Location", "Exterior", "Interior", "Faction"];

        // Tries to find a faction whose EditorID ends with "Faction" and contains the given term.
        // This is the fuzzy fallback used when no exact "<BaseName><Kind>Faction" candidate exists,
        // to tolerate mods that use slightly different naming (prefixes/suffixes/minor variations).
        private static IFactionGetter? TryFuzzyFactionMatch(
            string term,
            Dictionary<string, IFactionGetter> factionsByEdid,
            string? requiredPrefix = null,
            string requiredSuffix = "Faction")
        {
            if (string.IsNullOrWhiteSpace(term))
                return null;

            return factionsByEdid.Values.FirstOrDefault(f =>
                f.EditorID != null
                && f.EditorID.EndsWith(requiredSuffix, StringComparison.OrdinalIgnoreCase)
                && (requiredPrefix == null || f.EditorID.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
                && f.EditorID.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        // Shared logic for the Town/Farm/Mill naming-convention lookups: strips a known suffix off the
        // EditorID, builds the expected "<BaseName><Kind>Faction" candidate, and falls back to a fuzzy
        // match (and optionally a set of extra root candidates) if no exact match is found.
        private static (IFactionGetter? Faction, bool WasFuzzy) TryFindFactionByConvention(
            string editorId,
            string stripSuffix,
            Func<string, string> buildCandidateName,
            Dictionary<string, IFactionGetter> factionsByEdid,
            IEnumerable<string>? extraRoots = null,
            string? fuzzyRequiredPrefix = null,
            string fuzzyRequiredSuffix = "Faction")
        {
            var baseName = editorId.EndsWith(stripSuffix, StringComparison.OrdinalIgnoreCase)
                ? editorId[..^stripSuffix.Length]
                : editorId;

            var candidateName = buildCandidateName(baseName);
            if (factionsByEdid.TryGetValue(candidateName, out var exact))
                return (exact, false);

            var fuzzy = TryFuzzyFactionMatch(baseName, factionsByEdid, fuzzyRequiredPrefix, fuzzyRequiredSuffix);
            if (fuzzy != null)
                return (fuzzy, true);

            if (extraRoots != null)
            {
                foreach (var root in extraRoots.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var rootMatch = TryFuzzyFactionMatch(root, factionsByEdid, fuzzyRequiredPrefix, fuzzyRequiredSuffix);
                    if (rootMatch != null)
                        return (rootMatch, true);
                }
            }

            return (null, false);
        }

        // Builds "TownXFaction"-style root candidates by stripping common location-name suffixes,
        // plus a digit-stripped variant (e.g. "Riverwood01" -> "Riverwood").
        private static IEnumerable<string> GetTownRootCandidates(string baseName)
        {
            var current = baseName;
            yield return current;

            while (true)
            {
                var stripped = StripOneLayer(current);
                if (stripped.Length == 0 || stripped == current)
                    yield break;

                current = stripped;
                yield return current;
            }
        }

        private static string StripOneLayer(string name)
        {
            // Trailing digits first, since they usually come after the suffix (e.g. "WhiterunExterior01")
            var end = name.Length;
            while (end > 0 && char.IsDigit(name[end - 1]))
                end--;
            if (end < name.Length)
                return name[..end];

            foreach (var suffix in LocationNameSuffixes)
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && name.Length > suffix.Length)
                    return name[..^suffix.Length];
            }

            return name; // nothing left to strip
        }

        // Resolves the owner faction for an animal at the given location/cell.
        //
        // Precedence: naming conventions (cell, then location) -> manual faction match (exact)
        // -> manual faction match (partial). Naming conventions run first because they're the
        // most reliable general-purpose signal; manual faction matches exist to correct or fill
        // in the cases naming conventions can't reach (non-standard faction names), so they only
        // get a turn once naming has had its shot. Partial matches still go LAST within that so
        // a broad catch-all key like "Riften" can't hijack e.g. Snow-Shod Farm (whose location
        // EDID contains "Riften") away from a more specific exact match or naming match.
        private static (IFactionGetter? Faction, string? Reason) TryGetTownFaction(
            ILocationGetter? location,
            Dictionary<string, IFactionGetter> factionsByEdid,
            ICellGetter? cell)
        {
            string?[] editorIds = [cell?.EditorID, location?.EditorID];

            // Matches Cell to town"cell"faction or cell to "Cell"faction
            if (cell?.EditorID != null)
            {
                var cellTownFactionResult = TryFindFactionByConvention(
                    cell.EditorID,
                    stripSuffix: "Exterior",
                    buildCandidateName: baseName => $"Town{baseName}Faction",
                    factionsByEdid,
                    extraRoots: GetTownRootCandidates(cell.EditorID),
                    fuzzyRequiredPrefix: "Town");
                if (cellTownFactionResult.Faction != null)
                    return (cellTownFactionResult.Faction, $"Cell-Town faction match");

                var cellFarmFactionResult = TryFindFactionByConvention(
                    cell.EditorID,
                    stripSuffix: "Exterior",
                    buildCandidateName: baseName => $"{baseName}",
                    factionsByEdid,
                    extraRoots: GetTownRootCandidates(cell.EditorID));
                if (cellFarmFactionResult.Faction != null)
                    return (cellFarmFactionResult.Faction, $"Cell faction match");

            }

            // Naming conventions against the location EditorID.
            if (location?.EditorID != null)
            {
                // Town<Name>Faction
                var townFactionResult = TryFindFactionByConvention(
                    location.EditorID,
                    stripSuffix: "Location",
                    buildCandidateName: baseName => $"Town{baseName}Faction",
                    factionsByEdid,
                    extraRoots: GetTownRootCandidates(
                        location.EditorID.EndsWith("Location", StringComparison.OrdinalIgnoreCase)
                            ? location.EditorID[..^"Location".Length]
                            : location.EditorID),
                    fuzzyRequiredPrefix: "Town");
                if (townFactionResult.Faction != null)
                    return (townFactionResult.Faction, $"Location-Town faction match");

                // <Name>FarmFaction
                var farmFactionResult = TryFindFactionByConvention(
                    location.EditorID,
                    stripSuffix: "FarmLocation",
                    buildCandidateName: baseName => $"{baseName}FarmFaction",
                    factionsByEdid,
                    fuzzyRequiredSuffix: "FarmFaction");
                if (farmFactionResult.Faction != null)
                    return (farmFactionResult.Faction, $"Location-Farm faction match");

                // <Name>MillFaction, with an extra fallback against the cell's EditorID roots.
                // This also catches sawmills: "SawmillLocation" ends with "MillLocation", so
                // stripping that suffix and rebuilding "<Name>MillFaction" reconstructs the same
                // string a dedicated sawmill convention would (case-insensitively), and the
                // fuzzy "MillFaction" suffix check matches "SawmillFaction" too.
                var millFactionResult = TryFindFactionByConvention(
                    location.EditorID,
                    stripSuffix: "MillLocation",
                    buildCandidateName: baseName => $"{baseName}MillFaction",
                    factionsByEdid,
                    extraRoots: GetRootsFromEditorId(cell?.EditorID),
                    fuzzyRequiredSuffix: "MillFaction");
                if (millFactionResult.Faction != null)
                    return (millFactionResult.Faction, $"Location-Mill faction match");
            }

            // Manual faction match: exact match.
            foreach (var edid in editorIds)
            {
                if (edid != null && ManualFactionMatches.TryGetValue(edid, out var overrideEdid))
                {
                    var faction = ResolveOverrideFaction(overrideEdid, factionsByEdid);
                    if (faction != null)
                        return (faction, "Manual faction match (exact)");
                }
            }

            // Manual faction match: partial match, as the broad catch-all fallback.
            foreach (var edid in editorIds)
            {
                if (edid == null)
                    continue;

                foreach (var candidate in GetRootsFromEditorId(edid))
                {
                    if (TryFindPartialManualMatch(candidate, out var overrideEdid))
                    {
                        var faction = ResolveOverrideFaction(overrideEdid, factionsByEdid);
                        if (faction != null)
                            return (faction, "Manual faction match (partial)");
                    }
                }
            }

            return (null, null);
        }

        // Finds a faction for animals placed by a specific plugin (Settings.PluginFactionOverrides,
        // partial plugin-name matching). First matching entry that resolves to a real faction wins.
        private static (IFactionGetter? Faction, string? Reason) TryGetPluginFactionOverride(
            string pluginName,
            Settings settings,
            Dictionary<string, IFactionGetter> factionsByEdid)
        {
            foreach (var entry in settings.PluginFactionOverrides)
            {
                if (string.IsNullOrWhiteSpace(entry.PluginName) || string.IsNullOrWhiteSpace(entry.FactionEditorID))
                    continue;

                if (!pluginName.Contains(entry.PluginName.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                var faction = ResolveOverrideFaction(entry.FactionEditorID.Trim(), factionsByEdid);
                if (faction != null)
                    return (faction, "Plugin faction match");
            }

            return (null, null);
        }

        // Walks up the placed-NPC's context chain to find its containing cell, re-resolving through
        // the link cache to guarantee the fully-merged winning override (rather than a minimal stub
        // from whichever plugin owns the placed reference, which can be missing the EDID subrecord).
        private static ICellGetter? FindContainingCell(
            IModContext<ISkyrimMod, ISkyrimModGetter, IPlacedNpc, IPlacedNpcGetter> context,
            ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            var current = context.Parent;
            while (current != null)
            {
                if (current.Record is ICellGetter cell)
                {
                    if (linkCache.TryResolve<ICellGetter>(cell.FormKey, out var winningCell))
                        return winningCell;

                    return cell;
                }

                current = current.Parent;
            }

            return null;
        }

        // ------------------------------------------------------------------
        // Ownership-by-voting fallback (used only when naming conventions, manual faction
        // matches, and plugin overrides have all failed to resolve a faction — see Pass 1/
        // Pass 2 in RunPatch)
        // ------------------------------------------------------------------

        // Picks the most common owner FormKey from a cell's tally. Ties are broken by preferring
        // a Faction owner over an NPC owner, if one of the tied candidates is a Faction; if the tie
        // is between owners of the same kind (or the Faction check can't resolve either), the first
        // encountered candidate wins, deterministically (Dictionary enumeration order is stable for
        // a given set of insertions within a single run).
        private static FormKey PickMajorityOwner(
            Dictionary<FormKey, int> ownerCounts,
            ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            int maxCount = ownerCounts.Values.Max();
            var topOwners = ownerCounts.Where(kv => kv.Value == maxCount).Select(kv => kv.Key).ToList();

            if (topOwners.Count == 1)
                return topOwners[0];

            foreach (var formKey in topOwners)
            {
                if (linkCache.TryResolve<IMajorRecordGetter>(formKey, out var rec) && rec is IFactionGetter)
                    return formKey;
            }

            return topOwners[0];
        }

        // Picks the most common FactionRank recorded alongside a given (cell, owner) pairing. Falls
        // back to 0 (matching the clutter/consumables patchers' convention) if nothing was recorded.
        private static int PickRepresentativeRank(Dictionary<int, int>? rankCounts)
        {
            if (rankCounts == null || rankCounts.Count == 0)
                return 0;

            return rankCounts.OrderByDescending(kv => kv.Value).First().Key;
        }

        // ------------------------------------------------------------------
        // Main patching pass
        // ------------------------------------------------------------------

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var settings = LoadRunSettings(state);
            PopulateManualFactionMatches(settings);


            PrintShortDivider();
            ConsoleWriteLine("LOADING...".PadLeft(35));
            PrintShortDivider();
            // Debug only // Print out all loaded plugins seen by the patcher //
            // var loadOrderModKeys = state.LoadOrder.ListedOrder.Select(m => m.ModKey.FileName).ToList();
            // PrintShortDivider();
            // ConsoleWriteLine($"FULL LOAD ORDER RESOLVED BY MUTAGEN ({loadOrderModKeys.Count} plugins)".PadLeft(66));
            // PrintShortDivider();
            // foreach (var modName in loadOrderModKeys)
            // {
            //     ConsoleWriteLine(modName);
            // }
            // PrintDivider();

            var factionsByEdid = new Dictionary<string, IFactionGetter>(StringComparer.OrdinalIgnoreCase);
            foreach (var fac in state.LoadOrder.PriorityOrder.Faction().WinningOverrides())
            {
                if (fac.EditorID != null)
                    factionsByEdid.TryAdd(fac.EditorID, fac);
            }

            var seen = new HashSet<FormKey>();
            var ownerEdidCache = new Dictionary<FormKey, string?>();

            // Tallies, keyed by the containing cell's FormKey — built in Pass 1, consulted in
            // Pass 2 only as a fallback once naming conventions, manual faction matches, and
            // plugin overrides have all had first crack.
            var ownerCountsByCell = new Dictionary<FormKey, Dictionary<FormKey, int>>();
            var rankCountsByCellOwner = new Dictionary<(FormKey Cell, FormKey Owner), Dictionary<int, int>>();

            // Unowned farm-animal candidates, collected in Pass 1, decided in Pass 2. Decisions are
            // per-animal (a plugin override can apply to one animal but not its cell-mate from a
            // different plugin), so candidates don't need to be bucketed by cell the way the
            // clutter/consumables patchers bucket theirs — each just looks up its own cell's tally.
            var candidates = new List<(
                IModContext<ISkyrimMod, ISkyrimModGetter, IPlacedNpc, IPlacedNpcGetter> Context,
                string AnimalLabel,
                string PluginName,
                string CellEdid,
                string DisplayRace,
                ICellGetter? ContainingCell)>();

            var patchedAnimalsByCell = new Dictionary<string, List<(string Animal, string Plugin, string? OwnerFaction, string Reason)>>(StringComparer.OrdinalIgnoreCase);
            var skippedAnimalsByCell = new Dictionary<string, List<(string Animal, string Plugin, string Reason)>>(StringComparer.OrdinalIgnoreCase);
            var excludedAnimalsByPlugin = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var excludedCellsByRule = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var excludedLocTypesByRule = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var excludedNamesByRule = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var animalRaceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var patchedRaceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // Diagnostics: which plugins are contributing placed NPCs at all (regardless of race),
            // and which are contributing race-matched farm animals specifically. Answers "are my
            // mods' animals even being seen by the patcher?" independent of any filter rule.
            var allPlacedNpcCountsByPlugin = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var raceMatchedCountsByPlugin = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            int unknownCount = 0;
            int missingFactionCount = 0;
            int patchedCount = 0;
            int alreadyOwnedCount = 0;
            int excludedCount = 0;
            int excludedOwnerVotesCount = 0;
            int unresolvedNpcBaseCount = 0;

            var unknownSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var missingFactionSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var patchedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var alreadyOwnedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var excludedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            PrintShortDivider();
            ConsoleWriteLine("SCANNING...".PadLeft(35));
            PrintShortDivider();

            // ---- Pass 1: race-check every placed NPC. ----
            foreach (var context in state.LoadOrder.PriorityOrder.PlacedNpc().WinningContextOverrides(state.LinkCache))
            {
                var placedNpc = context.Record;
                var containingCell = FindContainingCell(context, state.LinkCache);

                // Cells without an EditorID (e.g. many exterior cells) are treated as unknown.
                var cellEdid = containingCell?.EditorID ?? "Unknown cell";

                if (!seen.Add(placedNpc.FormKey))
                    continue;

                var npc = placedNpc.Base.TryResolve(state.LinkCache);
                if (npc == null)
                {
                    unresolvedNpcBaseCount++;
                    continue;
                }

                var animalLabel = npc.EditorID ?? "UnknownNPC";

                // Get the actual mod file providing this winning override in the load order
                string pluginName = context.ModKey.FileName;

                allPlacedNpcCountsByPlugin.TryGetValue(pluginName, out var allCount);
                allPlacedNpcCountsByPlugin[pluginName] = allCount + 1;

                // Race check first: only farm-animal races are candidates at all.
                var raceEdid = npc.Race.TryResolve(state.LinkCache)?.EditorID ?? "UnknownRace";
                bool isFarmAnimalRace = settings.IncludeRaceTerms.Any(term =>
                    raceEdid.Contains(term, StringComparison.OrdinalIgnoreCase));

                if (!isFarmAnimalRace)
                    continue;

                raceMatchedCountsByPlugin.TryGetValue(pluginName, out var raceMatchedCount);
                raceMatchedCountsByPlugin[pluginName] = raceMatchedCount + 1;

                var displayRace = raceEdid.EndsWith("Race", StringComparison.OrdinalIgnoreCase)
                    ? raceEdid[..^"Race".Length]
                    : raceEdid;

                animalRaceCounts.TryGetValue(displayRace, out var raceCount);
                animalRaceCounts[displayRace] = raceCount + 1;

                if (!placedNpc.Owner.IsNull)
                {
                    alreadyOwnedCount++;
                    alreadyOwnedSet.Add(animalLabel);

                    var ownerFormKeyNullable = placedNpc.Owner.FormKeyNullable;
                    if (ownerFormKeyNullable is { } ownerFormKey && containingCell != null)
                    {
                        if (!ownerEdidCache.TryGetValue(ownerFormKey, out var ownerEdid))
                        {
                            ownerEdid = state.LinkCache.TryResolve<IMajorRecordGetter>(ownerFormKey, out var ownerRec)
                                ? ownerRec.EditorID
                                : null;
                            ownerEdidCache[ownerFormKey] = ownerEdid;
                        }

                        bool ownerIsExcluded = ownerEdid != null
                            && settings.ExcludeOwnerNames.Any(term => ownerEdid.Contains(term, StringComparison.OrdinalIgnoreCase));

                        if (!ownerIsExcluded)
                        {
                            var cellFormKey = containingCell.FormKey;

                            if (!ownerCountsByCell.TryGetValue(cellFormKey, out var ownerCounts))
                                ownerCountsByCell[cellFormKey] = ownerCounts = [];

                            ownerCounts.TryGetValue(ownerFormKey, out var count);
                            ownerCounts[ownerFormKey] = count + 1;

                            var rankKey = (cellFormKey, ownerFormKey);
                            if (!rankCountsByCellOwner.TryGetValue(rankKey, out var rankCounts))
                                rankCountsByCellOwner[rankKey] = rankCounts = [];

                            var factionRank = placedNpc.FactionRank ?? 0;
                            rankCounts.TryGetValue(factionRank, out var rankCount);
                            rankCounts[factionRank] = rankCount + 1;
                        }
                        else
                        {
                            excludedOwnerVotesCount++;
                        }
                    }

                    continue;
                }

                candidates.Add((context, animalLabel, pluginName, cellEdid, displayRace, containingCell));
            }

            // ---- Pass 2: for each unowned candidate, run the existing exclusion + override
            // matching; if no override matches, fall back to the containing cell's ownership vote
            // (if it has enough tallied data to meet MinimumOwnedObjectsForMajority). ----
            PrintShortDivider();
            ConsoleWriteLine("PATCHING...".PadLeft(35));
            PrintShortDivider();

            foreach (var (context, animalLabel, pluginName, cellEdid, displayRace, containingCell) in candidates)
            {
                // Wildcard-aware... no, partial-match cell exclusion.
                bool cellExcluded = false;
                foreach (var rule in settings.ExcludeCellRules)
                {
                    if (RuleMatchesCell(rule, cellEdid))
                    {
                        cellExcluded = true;
                        if (!excludedCellsByRule.TryGetValue(rule, out var cellList))
                            excludedCellsByRule[rule] = cellList = [];

                        cellList.Add(animalLabel);
                        break;
                    }
                }

                // Location-type exclusion (matched only against the location's LocType-prefixed
                // keywords, e.g. LocTypeDungeon — deliberately ignoring unrelated keyword data
                // like Civil War or world-interaction flags that can share vocabulary with these
                // terms, the same way the clutter/consumables patchers do).
                if (!cellExcluded && settings.ExcludeLocTypeRules.Count > 0)
                {
                    var loc = containingCell?.Location.TryResolve(state.LinkCache);
                    var keywordEdids = loc?.Keywords?
                        .Select(k => k.TryResolve(state.LinkCache)?.EditorID)
                        .Where(e => e != null && e.StartsWith("LocType", StringComparison.OrdinalIgnoreCase))
                        .Select(e => e!)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    if (keywordEdids != null && keywordEdids.Count > 0)
                    {
                        foreach (var rule in settings.ExcludeLocTypeRules)
                        {
                            if (keywordEdids.Any(k => k.Contains(rule, StringComparison.OrdinalIgnoreCase)))
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

                if (IsPluginExcluded(pluginName))
                {
                    if (!excludedAnimalsByPlugin.TryGetValue(pluginName, out var list))
                        excludedAnimalsByPlugin[pluginName] = list = [];

                    list.Add(animalLabel);
                    excludedCount++;
                    excludedSet.Add(animalLabel);
                    continue;
                }

                var matchedNameTerm = settings.ExcludeNameTerms
                    .FirstOrDefault(term => animalLabel.Contains(term, StringComparison.OrdinalIgnoreCase));
                if (matchedNameTerm != null)
                {
                    if (!excludedNamesByRule.TryGetValue(matchedNameTerm, out var list))
                        excludedNamesByRule[matchedNameTerm] = list = [];

                    list.Add(animalLabel);
                    excludedCount++;
                    excludedSet.Add(animalLabel);
                    continue;
                }

                // Matching. Naming conventions and manual faction matches beat plugin-based
                // matching; the raw location is passed to TryGetTownFaction so these lookups
                // still run even when there's no location or cell record to go on (the lack
                // of records only affects the skip reason below).
                var location = containingCell?.Location.TryResolve(state.LinkCache);
                bool hasNoLocationData = location == null && containingCell == null;

                var townFactionResult = TryGetTownFaction(location, factionsByEdid, containingCell);
                IOwnerGetter? ownerRecord = townFactionResult.Faction;
                string? ownerReason = townFactionResult.Reason;

                if (ownerRecord == null)
                {
                    var pluginOverrideResult = TryGetPluginFactionOverride(pluginName, settings, factionsByEdid);
                    if (pluginOverrideResult.Faction != null)
                    {
                        ownerRecord = pluginOverrideResult.Faction;
                        ownerReason = pluginOverrideResult.Reason;
                    }
                }

                int rankToApply = 0;

                // Ownership-by-voting fallback: only consulted once both overrides have missed,
                // and only if the containing cell has enough tallied ownership data to trust.
                if (ownerRecord == null && containingCell != null
                    && ownerCountsByCell.TryGetValue(containingCell.FormKey, out var ownerCounts)
                    && ownerCounts.Count > 0)
                {
                    int totalOwnedInCell = ownerCounts.Values.Sum();
                    if (totalOwnedInCell >= settings.MinimumOwnedObjectsForMajority)
                    {
                        var majorityOwnerFormKey = PickMajorityOwner(ownerCounts, state.LinkCache);

                        if (state.LinkCache.TryResolve<IOwnerGetter>(majorityOwnerFormKey, out var majorityOwner))
                        {
                            ownerRecord = majorityOwner;

                            ownerCounts.TryGetValue(majorityOwnerFormKey, out var voteWinningCount);
                            ownerReason = $"decision by {voteWinningCount}/{totalOwnedInCell} owned animals";

                            rankCountsByCellOwner.TryGetValue((containingCell.FormKey, majorityOwnerFormKey), out var rankCounts);
                            rankToApply = PickRepresentativeRank(rankCounts);
                        }
                    }
                }

                if (ownerRecord == null)
                {
                    missingFactionCount++;
                    missingFactionSet.Add(animalLabel);

                    var reason = hasNoLocationData
                        ? "No suitable owner, No suitable location"
                        : "No suitable owner";

                    if (hasNoLocationData)
                    {
                        unknownCount++;
                        unknownSet.Add(animalLabel);
                    }

                    AddSkip(skippedAnimalsByCell, animalLabel, pluginName, cellEdid, reason);
                    continue;
                }

                var patchNpc = context.GetOrAddAsOverride(state.PatchMod);
                patchNpc.Owner.SetTo(ownerRecord);
                patchNpc.FactionRank = rankToApply;
                patchedCount++;
                patchedSet.Add(animalLabel);

                patchedRaceCounts.TryGetValue(displayRace, out var patchedRaceCount);
                patchedRaceCounts[displayRace] = patchedRaceCount + 1;

                if (!patchedAnimalsByCell.TryGetValue(cellEdid, out var patchedList))
                    patchedAnimalsByCell[cellEdid] = patchedList = [];

                var ownerLabel = (ownerRecord as IMajorRecordGetter)?.EditorID ?? "Unknown owner";

                patchedList.Add((animalLabel, pluginName, ownerLabel, ownerReason ?? "unknown"));
            }

            PrintReport(
                settings,
                patchedAnimalsByCell,
                skippedAnimalsByCell,
                excludedAnimalsByPlugin,
                excludedCellsByRule,
                excludedLocTypesByRule,
                excludedNamesByRule,
                patchedRaceCounts,
                allPlacedNpcCountsByPlugin,
                raceMatchedCountsByPlugin,
                patchedCount,
                alreadyOwnedCount,
                missingFactionCount,
                unknownCount,
                excludedCount,
                excludedOwnerVotesCount,
                unresolvedNpcBaseCount);
        }

        // Loads (or generates) the settings file used for this run.
        private static Settings LoadRunSettings(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
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

            try
            {
                return JsonConvert.DeserializeObject<Settings>(configContent!, SettingsJsonOptions) ?? LazySettings.Value;
            }
            catch (JsonException)
            {
                ConsoleWriteLine("WARNING: Could not parse Settings File; using defaults.");
                return LazySettings.Value;
            }
        }

        // Populates the ManualFactionMatches lookup from Settings.ManualFactionMatches for this run.
        private static void PopulateManualFactionMatches(Settings settings)
        {
            ManualFactionMatches = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var duplicates = new List<string>();

            foreach (var entry in settings.ManualFactionMatches)
            {
                if (string.IsNullOrWhiteSpace(entry.EditorID) || string.IsNullOrWhiteSpace(entry.FactionEditorID))
                    continue;

                var key = entry.EditorID.Trim();
                var value = entry.FactionEditorID.Trim();

                if (!ManualFactionMatches.TryAdd(key, value))
                    duplicates.Add(key);
            }

            if (duplicates.Count > 0)
            {
                ConsoleWriteLine($"WARNING: Duplicate Manual Faction Match EditorIDs were ignored (first entry wins): {string.Join(", ", duplicates)}");
            }
        }

        // ------------------------------------------------------------------
        // Reporting
        // ------------------------------------------------------------------

        private static void PrintReport(
            Settings settings,
            Dictionary<string, List<(string Animal, string Plugin, string? OwnerFaction, string Reason)>> patchedAnimalsByCell,
            Dictionary<string, List<(string Animal, string Plugin, string Reason)>> skippedAnimalsByCell,
            Dictionary<string, List<string>> excludedAnimalsByPlugin,
            Dictionary<string, List<string>> excludedCellsByRule,
            Dictionary<string, List<string>> excludedLocTypesByRule,
            Dictionary<string, List<string>> excludedNamesByRule,
            Dictionary<string, int> patchedRaceCounts,
            Dictionary<string, int> allPlacedNpcCountsByPlugin,
            Dictionary<string, int> raceMatchedCountsByPlugin,
            int patchedCount,
            int alreadyOwnedCount,
            int missingFactionCount,
            int unknownCount,
            int excludedCount,
            int excludedOwnerVotesCount,
            int unresolvedNpcBaseCount)
        {
            var totalPatched = patchedAnimalsByCell.Values.SelectMany(v => v).Count();

            // Debugging only //
            // _lastWasDivider = false;
            // PrintShortDivider();
            // ConsoleWriteLine("PLACED NPCs SEEN, BY ORIGIN PLUGIN".PadLeft(52));
            // PrintShortDivider();
            // ConsoleWriteLine("(diagnostic: shows every plugin the patcher saw ANY placed NPC from, and how many of");
            // ConsoleWriteLine("those race-matched as farm animals — before any exclusion/ownership filtering runs)");
            // PrintShortDivider();
            //
            // foreach (var kvp in allPlacedNpcCountsByPlugin.OrderByDescending(k => k.Value))
            // {
            //     raceMatchedCountsByPlugin.TryGetValue(kvp.Key, out var raceMatched);
            //     ConsoleWriteLine($"{kvp.Key}   ({kvp.Value} placed NPCs total, {raceMatched} race-matched as farm animals)");
            // }

            PrintDivider();

            _lastWasDivider = false;
            PrintShortDivider();
            ConsoleWriteLine("PATCHED BY CELL".PadLeft(36));
            ConsoleWriteLine($"Total patched: {totalPatched}".PadLeft(37));
            PrintShortDivider();

            foreach (var kvp in patchedAnimalsByCell.OrderByDescending(k => k.Value.Count))
            {
                var cellLabel = kvp.Key;
                var animals = kvp.Value;

                ConsoleWriteLine($"{cellLabel}   ({animals.Count} patched)");

                var byPlugin = animals
                    .GroupBy(a => a.Plugin)
                    .Select(g => new { Plugin = g.Key, Count = g.Count(), Animals = g.ToList() })
                    .OrderByDescending(p => p.Count);

                foreach (var pluginGroup in byPlugin)
                {
                    ConsoleWriteLine($"     [{pluginGroup.Plugin}] ({pluginGroup.Count})");

                    var byAnimal = pluginGroup.Animals
                        .GroupBy(a => new { a.Animal, a.OwnerFaction, a.Reason })
                        .Select(g => new { g.Key.Animal, g.Key.OwnerFaction, g.Key.Reason, Count = g.Count() })
                        .OrderByDescending(a => a.Count);

                    foreach (var entry in byAnimal)
                    {
                        ConsoleWriteLine($"          {entry.Count} {entry.Animal}(s)  now owned by:  {entry.OwnerFaction}  through:  {entry.Reason}");
                    }
                }

                PrintDivider();
            }

            var totalSkipped = skippedAnimalsByCell.Values.SelectMany(v => v).Count();

            _lastWasDivider = false;
            PrintShortDivider();
            ConsoleWriteLine("SKIPPED BY CELL".PadLeft(35));
            ConsoleWriteLine($"Total skipped: {totalSkipped}".PadLeft(36));
            PrintShortDivider();

            foreach (var kvp in skippedAnimalsByCell.OrderByDescending(k => k.Value.Count))
            {
                var cellLabel = kvp.Key;
                var animals = kvp.Value;

                ConsoleWriteLine($"{cellLabel}   ({animals.Count} skipped)");

                var byPlugin = animals
                    .GroupBy(a => a.Plugin)
                    .Select(g => new { Plugin = g.Key, Count = g.Count(), Animals = g.ToList() })
                    .OrderByDescending(p => p.Count);

                foreach (var pluginGroup in byPlugin)
                {
                    ConsoleWriteLine($"     [{pluginGroup.Plugin}] ({pluginGroup.Count})");

                    var byAnimal = pluginGroup.Animals
                        .GroupBy(a => new { a.Animal, a.Reason })
                        .Select(g => new { g.Key.Animal, g.Key.Reason, Count = g.Count() })
                        .OrderByDescending(a => a.Count);

                    foreach (var entry in byAnimal)
                    {
                        ConsoleWriteLine($"          {entry.Count} {entry.Animal}   Returned: {entry.Reason}");
                    }
                }

                PrintDivider();
            }

            _lastWasDivider = false;
            PrintShortDivider();
            ConsoleWriteLine("EXCLUSION SUMMARY".PadLeft(37));
            PrintShortDivider();

            var combined = new List<(string Rule, int Count, string Type)>();

            foreach (var rule in settings.ExcludePlugins)
            {
                int count = excludedAnimalsByPlugin
                    .Where(kv => RuleMatchesPlugin(rule, kv.Key))
                    .SelectMany(kv => kv.Value)
                    .Count();

                if (count > 0)
                    combined.Add((rule, count, "plugin"));
            }

            foreach (var rule in settings.ExcludeCellRules)
            {
                if (excludedCellsByRule.TryGetValue(rule, out var cells) && cells.Count > 0)
                    combined.Add((rule, cells.Count, "cell"));
            }

            foreach (var rule in settings.ExcludeLocTypeRules)
            {
                if (excludedLocTypesByRule.TryGetValue(rule, out var names) && names.Count > 0)
                    combined.Add((rule, names.Count, "loctype"));
            }

            foreach (var term in settings.ExcludeNameTerms)
            {
                int count = excludedNamesByRule
                    .Where(kvp => kvp.Key.Contains(term, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(kvp => kvp.Value)
                    .Count();

                if (count > 0)
                    combined.Add((term, count, "name"));
            }

            foreach (var entry in combined.OrderByDescending(e => e.Count))
            {
                ConsoleWriteLine($"The rule: {entry.Rule} ({entry.Type}) excluded {entry.Count} animals");
            }

            _lastWasDivider = false;
            PrintShortDivider();
            ConsoleWriteLine("GENERAL SUMMARY".PadLeft(35));
            PrintShortDivider();

            var summaryLines = new List<(string Label, int Count, bool ShowRaces)>
            {
                ("Farm animals have been assigned owners", patchedCount, true),
                ("Farm animals were already owned", alreadyOwnedCount, false),
            //  ("Owned animals were excluded from voting by ExcludeOwnerNames", excludedOwnerVotesCount, false),
                ("Farm animals had no suitable owner", missingFactionCount, false),
                ("Farm animals were in an unknown location", unknownCount, false),
                ("Farm animals were excluded by rules", excludedCount, false),
                ("Placed NPCs (of any kind) didn't resolve as an NPC record", unresolvedNpcBaseCount, false),
            };

            foreach (var (label, count, showRaces) in summaryLines.OrderByDescending(l => l.Count))
            {
                ConsoleWriteLine($"{count} {label}");

                if (showRaces)
                {
                    foreach (var kvp in patchedRaceCounts.OrderByDescending(k => k.Value))
                    {
                        ConsoleWriteLine($"    {kvp.Value}  {kvp.Key}(s)");
                    }
                }
            }

            PrintDivider();
            ConsoleWriteLine("Patching is complete! Scroll up to read a report on what was patched, skipped, and excluded.");
            //  ConsoleWriteLine("A couple of notes on the summaries: In the General Summary there is typically a large overlap between no suitable owner and an unsuitable location, since they can both be true.");
            //  ConsoleWriteLine("The Exclusion Summary displays the NPCs who would have been patched by the logic were it not for exclusion rules.");
            //  ConsoleWriteLine("The \"Base didn't resolve as an NPC record\" count covers ALL placed NPCs, not just farm animals (race can't be checked until Base resolves) — a large number here is worth investigating (e.g. animals placed via a Leveled Actor list) but isn't itself a count of missed animals.");
            PrintDivider();
        }

        // ------------------------------------------------------------------
        // Entry point
        // ------------------------------------------------------------------

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
    }
}