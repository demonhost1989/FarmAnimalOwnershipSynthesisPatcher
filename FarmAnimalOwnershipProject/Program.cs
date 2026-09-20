using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using System.Diagnostics;
using System.Text.RegularExpressions;


namespace FarmAnimalOwnershipProject
{
    public class Program
    {
        // ------------------------------------------------------------------
        // Settings
        // ------------------------------------------------------------------

        static Lazy<Settings> LazySettings = new();

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

        private static bool MatchesPattern(string pattern, string value)
        {
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(value))
                return false;

            if (!pattern.Contains('*') && !pattern.Contains('?'))
                return value.Contains(pattern, StringComparison.OrdinalIgnoreCase);

            var anchoredGlob = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(value, anchoredGlob, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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

        private static (IFactionGetter? Faction, bool WasFuzzy) ResolveOverrideFaction(string factionEdid, Dictionary<string, IFactionGetter> factionsByEdid)
        {
            if (string.IsNullOrWhiteSpace(factionEdid))
                return (null, false);

            if (factionsByEdid.TryGetValue(factionEdid, out var exact))
                return (exact, false);

            return (TryFuzzyFactionMatch(factionEdid, factionsByEdid), true);
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

        private static (IFactionGetter? Faction, string? Reason) TryGetPluginLocalFactionMatch(
            string pluginName,
            ILocationGetter? location,
            ICellGetter? cell,
            Dictionary<string, List<IFactionGetter>> factionsByPlugin)
        {
            if (!factionsByPlugin.TryGetValue(pluginName, out var localFactions) || localFactions.Count == 0)
                return (null, null);

            string?[] editorIds = [cell?.EditorID, location?.EditorID];

            foreach (var edid in editorIds)
            {
                if (edid == null)
                    continue;

                foreach (var root in GetTownRootCandidates(edid))
                {
                    var match = localFactions.FirstOrDefault(f =>
                        f.EditorID != null &&
                        (f.EditorID.Contains(root, StringComparison.OrdinalIgnoreCase)
                            || root.Contains(f.EditorID, StringComparison.OrdinalIgnoreCase)));

                    if (match != null)
                        return (match, "Plugin-Derived faction match");
                }
            }

            return (null, null);
        }

        private static (IFactionGetter? Faction, string? Reason, bool WasFuzzy) TryGetTownFaction(
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
                    return (cellTownFactionResult.Faction, "Cell-Town faction match", cellTownFactionResult.WasFuzzy);

                var cellFarmFactionResult = TryFindFactionByConvention(
                    cell.EditorID,
                    stripSuffix: "Exterior",
                    buildCandidateName: baseName => $"{baseName}",
                    factionsByEdid,
                    extraRoots: GetTownRootCandidates(cell.EditorID));
                if (cellFarmFactionResult.Faction != null)
                    return (cellFarmFactionResult.Faction, "Cell-Name faction match", cellFarmFactionResult.WasFuzzy);

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
                    return (townFactionResult.Faction, "Location-Town faction match", townFactionResult.WasFuzzy);

                // <Name>FarmFaction
                var farmFactionResult = TryFindFactionByConvention(
                    location.EditorID,
                    stripSuffix: "FarmLocation",
                    buildCandidateName: baseName => $"{baseName}FarmFaction",
                    factionsByEdid,
                    fuzzyRequiredSuffix: "FarmFaction");
                if (farmFactionResult.Faction != null)
                    return (farmFactionResult.Faction, "Location-Farm faction match", farmFactionResult.WasFuzzy);

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
                    return (millFactionResult.Faction, "Location-Mill faction match", millFactionResult.WasFuzzy);
            }

            // Manual faction match: exact match.
            foreach (var edid in editorIds)
            {
                if (edid != null && ManualFactionMatches.TryGetValue(edid, out var overrideEdid))
                {
                    var (faction, wasFuzzy) = ResolveOverrideFaction(overrideEdid, factionsByEdid);
                    if (faction != null)
                        return (faction, "Manual faction match (exact)", wasFuzzy);
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
                        var (faction, wasFuzzy) = ResolveOverrideFaction(overrideEdid, factionsByEdid);
                        if (faction != null)
                            return (faction, "Manual faction match (partial)", wasFuzzy);
                    }
                }
            }

            return (null, null, false);
        }

        // Finds a faction for animals placed by a specific plugin (Settings.PluginFactionOverrides,
        // partial plugin-name matching). First matching entry that resolves to a real faction wins.
        private static (IFactionGetter? Faction, string? Reason, bool WasFuzzy) TryGetPluginFactionOverride(
            string pluginName,
            Settings settings,
            Dictionary<string, IFactionGetter> factionsByEdid)
        {
            foreach (var entry in settings.PluginFactionOverrides)
            {
                if (string.IsNullOrWhiteSpace(entry.PluginName) || string.IsNullOrWhiteSpace(entry.FactionEditorID))
                    continue;

                if (!MatchesPattern(entry.PluginName.Trim(), pluginName))
                    continue;

                var (faction, wasFuzzy) = ResolveOverrideFaction(entry.FactionEditorID.Trim(), factionsByEdid);
                if (faction != null)
                    return (faction, "Plugin-Name faction match", wasFuzzy);
            }

            return (null, null, false);
        }

        // Caches the resolved winning ICellGetter by cell FormKey. The chain-walk to find the immediate
        // containing cell (following context.Parent pointers) is cheap in-memory traversal, but the
        // linkCache.TryResolve<ICellGetter> call at the end is not — and many placed NPCs routinely
        // share the same containing cell, so that resolve was being repeated redundantly for the same
        // cell over and over. Caching by FormKey collapses it to once per unique cell in the load order.
        private static readonly Dictionary<FormKey, ICellGetter?> ResolvedCellCache = new();

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
                    if (ResolvedCellCache.TryGetValue(cell.FormKey, out var cached))
                        return cached;

                    ICellGetter? resolved = linkCache.TryResolve<ICellGetter>(cell.FormKey, out var winningCell)
                        ? winningCell
                        : cell;

                    ResolvedCellCache[cell.FormKey] = resolved;
                    return resolved;
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

        private sealed class PatchedRaceSummary
        {
            public int Count { get; set; }
            public Dictionary<string, int>? NpcEditorIdCounts { get; set; }
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var overallStopwatch = Stopwatch.StartNew();

            var settings = LazySettings.Value;
            PopulateManualFactionMatches(settings);
            ResolvedCellCache.Clear();


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

            var factionLookupStopwatch = Stopwatch.StartNew();
            var factionsByEdid = new Dictionary<string, IFactionGetter>(StringComparer.OrdinalIgnoreCase);
            var factionsByPlugin = new Dictionary<string, List<IFactionGetter>>(StringComparer.OrdinalIgnoreCase);
            foreach (var fac in state.LoadOrder.PriorityOrder.Faction().WinningOverrides())
            {
                if (fac.EditorID != null)
                    factionsByEdid.TryAdd(fac.EditorID, fac);

                // Grouped by the plugin that ORIGINALLY defined the faction (FormKey.ModKey), not
                // whichever plugin's override happens to be winning — that's what "factions available
                // in the plugin" means for the Plugin-local faction match check below.
                string originPlugin = fac.FormKey.ModKey.FileName;
                if (!factionsByPlugin.TryGetValue(originPlugin, out var pluginFactions))
                    factionsByPlugin[originPlugin] = pluginFactions = [];

                pluginFactions.Add(fac);
            }
            factionLookupStopwatch.Stop();

            var ownerEdidCache = new Dictionary<FormKey, string?>();

            // Caches the "is this base NPC record a farm-animal race, and what's its label/race info?"
            // classification by the base NPC's FormKey. This used to be recomputed — including a full
            // placedNpc.Base.TryResolve(...) LinkCache call plus a Race.TryResolve(...) call — for EVERY
            // placed NPC instance with zero caching, even though many placed animals routinely share the
            // exact same base NPC template (e.g. one "Cow01" record placed hundreds of times across the
            // world). Caching by FormKey means each unique base NPC template only gets resolved once.
            var npcBaseCache = new Dictionary<FormKey, (bool Resolved, string AnimalLabel, string DisplayRace, bool IsFarmAnimalRace)>();

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
            var excludedDetails = new List<(string Animal, string Race, string Cell, string Plugin, string Rule, string RuleType)>();
            var patchedRaces = new Dictionary<string, PatchedRaceSummary>(StringComparer.OrdinalIgnoreCase);

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

            PrintShortDivider();
            ConsoleWriteLine("SCANNING...".PadLeft(35));
            PrintShortDivider();

            var findCellStopwatch = new Stopwatch();
            var npcResolveStopwatch = new Stopwatch();

            // ---- Pass 1: race-check every placed NPC. ----
            var pass1Stopwatch = Stopwatch.StartNew();
            foreach (var context in state.LoadOrder.PriorityOrder.PlacedNpc().WinningContextOverrides(state.LinkCache))
            {
                var placedNpc = context.Record;

                findCellStopwatch.Start();
                var containingCell = FindContainingCell(context, state.LinkCache);
                findCellStopwatch.Stop();

                // Cells without an EditorID (e.g. many exterior cells) are treated as unknown.
                var cellEdid = containingCell?.EditorID ?? "Unknown cell";

                // Classification (resolve + race check) is cached by base NPC FormKey — see
                // npcBaseCache's declaration above for why this matters. The counting below still
                // happens once per PLACED INSTANCE using the cached classification, exactly as before.
                var baseFormKey = placedNpc.Base.FormKey;
                if (!npcBaseCache.TryGetValue(baseFormKey, out var npcInfo))
                {
                    npcResolveStopwatch.Start();
                    var npc = placedNpc.Base.TryResolve(state.LinkCache);
                    if (npc == null)
                    {
                        npcInfo = (Resolved: false, AnimalLabel: "", DisplayRace: "", IsFarmAnimalRace: false);
                    }
                    else
                    {
                        var resolvedAnimalLabel = npc.EditorID ?? "UnknownNPC";
                        var resolvedRaceEdid = npc.Race.TryResolve(state.LinkCache)?.EditorID ?? "UnknownRace";
                        bool resolvedIsFarmAnimalRace = settings.IncludeRaceTerms.Any(term =>
                            MatchesPattern(term, resolvedRaceEdid));
                        var resolvedDisplayRace = resolvedRaceEdid.EndsWith("Race", StringComparison.OrdinalIgnoreCase)
                            ? resolvedRaceEdid[..^"Race".Length]
                            : resolvedRaceEdid;

                        npcInfo = (Resolved: true, AnimalLabel: resolvedAnimalLabel, DisplayRace: resolvedDisplayRace, IsFarmAnimalRace: resolvedIsFarmAnimalRace);
                    }
                    npcResolveStopwatch.Stop();

                    npcBaseCache[baseFormKey] = npcInfo;
                }

                // Get the actual mod file providing this winning override in the load order
                string pluginName = context.ModKey.FileName;

                allPlacedNpcCountsByPlugin.TryGetValue(pluginName, out var allCount);
                allPlacedNpcCountsByPlugin[pluginName] = allCount + 1;

                if (!npcInfo.Resolved)
                {
                    unresolvedNpcBaseCount++;
                    continue;
                }

                var animalLabel = npcInfo.AnimalLabel;

                // Race check first: only farm-animal races are candidates at all.
                if (!npcInfo.IsFarmAnimalRace)
                    continue;

                raceMatchedCountsByPlugin.TryGetValue(pluginName, out var raceMatchedCount);
                raceMatchedCountsByPlugin[pluginName] = raceMatchedCount + 1;

                var displayRace = npcInfo.DisplayRace;

                if (!placedNpc.Owner.IsNull)
                {
                    alreadyOwnedCount++;

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
                            && settings.ExcludeOwnerNames.Any(term => MatchesPattern(term, ownerEdid));

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
            pass1Stopwatch.Stop();

            // ---- Pass 2: for each unowned candidate, run the existing exclusion + override
            // matching; if no override matches, fall back to the containing cell's ownership vote
            // (if it has enough tallied data to meet MinimumOwnedObjectsForMajority). ----
            PrintShortDivider();
            ConsoleWriteLine("PATCHING...".PadLeft(35));
            PrintShortDivider();

            // Memoizes per-cell work that used to be repeated for every candidate animal in that cell:
            // resolving the containing Location (previously resolved TWICE per candidate — once for the
            // loctype exclusion check, once again a few lines later for TryGetTownFaction — despite
            // being the exact same cell both times), resolving that Location's Keywords, and running
            // the ExcludeCellRules/ExcludeLocTypeRules checks. None of this depends on the specific
            // animal — only on the cell.
            var cellContextCache = new Dictionary<FormKey, (ILocationGetter? Location, bool CellRuleExcluded, string? CellRuleMatched, bool LocTypeExcluded, string? LocTypeRuleMatched)>();

            // Memoizes plugin exclusion by plugin name — same idea, trivial cost either way, but free to cache.
            var pluginExclusionCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            var pass2Stopwatch = Stopwatch.StartNew();
            foreach (var (context, animalLabel, pluginName, cellEdid, displayRace, containingCell) in candidates)
            {
                // Dictionary<FormKey,...> requires a non-nullable key, so "no containing cell" uses
                // FormKey.Null as a sentinel rather than an actual null.
                var cellCacheKey = containingCell?.FormKey ?? FormKey.Null;
                if (!cellContextCache.TryGetValue(cellCacheKey, out var cellCtx))
                {
                    ILocationGetter? loc = containingCell?.Location.TryResolve(state.LinkCache);

                    bool cellRuleExcluded = false;
                    string? cellRuleMatched = null;
                    foreach (var rule in settings.ExcludeCellRules)
                    {
                        if (MatchesPattern(rule, cellEdid))
                        {
                            cellRuleExcluded = true;
                            cellRuleMatched = rule;
                            break;
                        }
                    }

                    // Location-type exclusion (matched only against the location's LocType-prefixed
                    // keywords, e.g. LocTypeDungeon — deliberately ignoring unrelated keyword data
                    // like Civil War or world-interaction flags that can share vocabulary with these
                    // terms, the same way the clutter/consumables patchers do).
                    bool locTypeExcluded = false;
                    string? locTypeRuleMatched = null;
                    if (!cellRuleExcluded && settings.ExcludeLocTypeRules.Count > 0)
                    {
                        var keywordEdids = loc?.Keywords?
                            .Select(k => k.TryResolve(state.LinkCache)?.EditorID)
                            .Where(e => e != null && e.StartsWith("LocType", StringComparison.OrdinalIgnoreCase))
                            .Select(e => e!)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        if (keywordEdids != null && keywordEdids.Count > 0)
                        {
                            foreach (var rule in settings.ExcludeLocTypeRules)
                            {
                                if (keywordEdids.Any(k => MatchesPattern(rule, k)))
                                {
                                    locTypeExcluded = true;
                                    locTypeRuleMatched = rule;
                                    break;
                                }
                            }
                        }
                    }

                    cellCtx = (loc, cellRuleExcluded, cellRuleMatched, locTypeExcluded, locTypeRuleMatched);
                    cellContextCache[cellCacheKey] = cellCtx;
                }

                var location = cellCtx.Location;

                // Giving cellEdid this Location fallback would silently change which animals
                // ExcludeCellRules excludes.
                var cellDisplayLabel = containingCell?.EditorID != null
                    ? $"{containingCell.EditorID} [Cell]"
                    : location?.EditorID != null
                        ? $"{location.EditorID} [Location]"
                        : "Unknown cell";

                if (cellCtx.CellRuleExcluded)
                {
                    if (!excludedCellsByRule.TryGetValue(cellCtx.CellRuleMatched!, out var cellList))
                        excludedCellsByRule[cellCtx.CellRuleMatched!] = cellList = [];

                    cellList.Add(animalLabel);
                    excludedDetails.Add((animalLabel, displayRace, cellDisplayLabel, pluginName, cellCtx.CellRuleMatched!, "Cell EditorID"));
                    excludedCount++;
                    continue;
                }

                if (cellCtx.LocTypeExcluded)
                {
                    if (!excludedLocTypesByRule.TryGetValue(cellCtx.LocTypeRuleMatched!, out var list))
                        excludedLocTypesByRule[cellCtx.LocTypeRuleMatched!] = list = [];

                    list.Add(animalLabel);
                    excludedDetails.Add((animalLabel, displayRace, cellDisplayLabel, pluginName, cellCtx.LocTypeRuleMatched!, "LocType keyword EditorID"));
                    excludedCount++;
                    continue;
                }

                if (!pluginExclusionCache.TryGetValue(pluginName, out var matchedPluginRule))
                {
                    matchedPluginRule = settings.ExcludePlugins.FirstOrDefault(pattern => MatchesPattern(pattern, pluginName));
                    pluginExclusionCache[pluginName] = matchedPluginRule;
                }

                if (matchedPluginRule != null)
                {
                    if (!excludedAnimalsByPlugin.TryGetValue(pluginName, out var list))
                        excludedAnimalsByPlugin[pluginName] = list = [];

                    list.Add(animalLabel);
                    excludedDetails.Add((animalLabel, displayRace, cellDisplayLabel, pluginName, matchedPluginRule, "Plugin filename"));
                    excludedCount++;
                    continue;
                }

                var matchedNameTerm = settings.ExcludeNameTerms
                    .FirstOrDefault(term => MatchesPattern(term, animalLabel));
                if (matchedNameTerm != null)
                {
                    if (!excludedNamesByRule.TryGetValue(matchedNameTerm, out var list))
                        excludedNamesByRule[matchedNameTerm] = list = [];

                    list.Add(animalLabel);
                    excludedDetails.Add((animalLabel, displayRace, cellDisplayLabel, pluginName, matchedNameTerm, "Base NPC EditorID"));
                    excludedCount++;
                    continue;
                }

                bool hasNoLocationData = location == null && containingCell == null;

                var pluginLocalResult = TryGetPluginLocalFactionMatch(pluginName, location, containingCell, factionsByPlugin);
                IOwnerGetter? ownerRecord = pluginLocalResult.Faction;
                string? ownerReason = pluginLocalResult.Reason;
                bool ownerWasFuzzy = false;

                if (ownerRecord == null)
                {
                    var townFactionResult = TryGetTownFaction(location, factionsByEdid, containingCell);
                    ownerRecord = townFactionResult.Faction;
                    ownerReason = townFactionResult.Reason;
                    ownerWasFuzzy = townFactionResult.WasFuzzy;
                }

                if (ownerRecord == null)
                {
                    var pluginOverrideResult = TryGetPluginFactionOverride(pluginName, settings, factionsByEdid);
                    if (pluginOverrideResult.Faction != null)
                    {
                        ownerRecord = pluginOverrideResult.Faction;
                        ownerReason = pluginOverrideResult.Reason;
                        ownerWasFuzzy = pluginOverrideResult.WasFuzzy;
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
                            ownerReason = $"ownership vote ({voteWinningCount}/{totalOwnedInCell} ownership share before patching)";

                            rankCountsByCellOwner.TryGetValue((containingCell.FormKey, majorityOwnerFormKey), out var rankCounts);
                            rankToApply = PickRepresentativeRank(rankCounts);
                        }
                    }
                }

                if (ownerRecord == null)
                {
                    missingFactionCount++;

                    var reason = hasNoLocationData
                        ? "No suitable owner, No suitable location"
                        : "No suitable owner";

                    if (hasNoLocationData)
                    {
                        unknownCount++;
                    }

                    if (!skippedAnimalsByCell.TryGetValue(cellDisplayLabel, out var skippedList))
                        skippedAnimalsByCell[cellDisplayLabel] = skippedList = [];

                    skippedList.Add((animalLabel, pluginName, reason));
                    continue;
                }

                var patchNpc = context.GetOrAddAsOverride(state.PatchMod);
                patchNpc.Owner.SetTo(ownerRecord);
                patchNpc.FactionRank = rankToApply;
                patchedCount++;

                if (!patchedRaces.TryGetValue(displayRace, out var raceSummary))
                    patchedRaces[displayRace] = raceSummary = new();

                raceSummary.Count++;
                if (settings.Verbose.PatchedNpcEditorIds)
                {
                    var editorIdCounts = raceSummary.NpcEditorIdCounts ??= new(StringComparer.OrdinalIgnoreCase);
                    editorIdCounts.TryGetValue(animalLabel, out var editorIdCount);
                    editorIdCounts[animalLabel] = editorIdCount + 1;
                }

                if (!patchedAnimalsByCell.TryGetValue(cellDisplayLabel, out var patchedList))
                    patchedAnimalsByCell[cellDisplayLabel] = patchedList = [];

                var ownerLabel = (ownerRecord as IMajorRecordGetter)?.EditorID ?? "Unknown owner";
                var reasonLabel = ownerReason ?? "unknown";
                if (ownerWasFuzzy && settings.Verbose.FuzzyMatchDetail)
                    reasonLabel += " (fuzzy)";

                patchedList.Add((animalLabel, pluginName, ownerLabel, reasonLabel));
            }
            pass2Stopwatch.Stop();
            overallStopwatch.Stop();

            PrintReport(
                settings,
                patchedAnimalsByCell,
                skippedAnimalsByCell,
                excludedAnimalsByPlugin,
                excludedCellsByRule,
                excludedLocTypesByRule,
                excludedNamesByRule,
                excludedDetails,
                patchedRaces,
                allPlacedNpcCountsByPlugin,
                raceMatchedCountsByPlugin,
                patchedCount,
                alreadyOwnedCount,
                missingFactionCount,
                unknownCount,
                excludedCount,
                excludedOwnerVotesCount,
                unresolvedNpcBaseCount);

            if (settings.Verbose.Timing)
            {
                PrintTimingReport(
                    overallStopwatch,
                    factionLookupStopwatch,
                    pass1Stopwatch,
                    pass2Stopwatch,
                    findCellStopwatch,
                    npcResolveStopwatch,
                    npcBaseCache.Count,
                    candidates.Count);
            }
        }

        private static void PrintTimingReport(
            Stopwatch overall,
            Stopwatch factionLookup,
            Stopwatch pass1,
            Stopwatch pass2,
            Stopwatch findCell,
            Stopwatch npcResolve,
            int uniqueBaseNpcCount,
            int candidateCount)
        {
            _lastWasDivider = false;
            PrintShortDivider();
            ConsoleWriteLine("TIMING BREAKDOWN".PadLeft(36));
            PrintShortDivider();

            ConsoleWriteLine($"Candidates carried into pass 2: {candidateCount}");
            ConsoleWriteLine($"Unique base NPC records classified: {uniqueBaseNpcCount}");
            PrintShortDivider();

            ConsoleWriteLine($"Faction lookup build:          {factionLookup.ElapsedMilliseconds,8} ms");
            ConsoleWriteLine($"Pass 1 (full load-order scan): {pass1.ElapsedMilliseconds,8} ms");
            ConsoleWriteLine($"  of which base-NPC resolve:   {npcResolve.ElapsedMilliseconds,8} ms  (once per unique base NPC template, not per placed instance)");
            ConsoleWriteLine($"Pass 2 (candidate processing): {pass2.ElapsedMilliseconds,8} ms");
            ConsoleWriteLine($"Cell-finding (combined, both passes): {findCell.ElapsedMilliseconds,8} ms  (included within Pass 1 above, broken out separately since it's a suspect)");
            PrintShortDivider();
            ConsoleWriteLine($"TOTAL:                          {overall.ElapsedMilliseconds,8} ms");

            PrintDivider();
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
            List<(string Animal, string Race, string Cell, string Plugin, string Rule, string RuleType)> excludedDetails,
            Dictionary<string, PatchedRaceSummary> patchedRaces,
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
            if (settings.Verbose.PerPluginCounts)
                PrintPlacedNpcsByPlugin(allPlacedNpcCountsByPlugin, raceMatchedCountsByPlugin);

            PrintDivider();

            PrintPatchedByCell(patchedAnimalsByCell);
            PrintOwnershipSourceSummary(patchedAnimalsByCell);

            if (settings.Verbose.SkippedAnimals)
                PrintSkippedByCell(skippedAnimalsByCell);

            PrintExclusionSummary(settings, excludedAnimalsByPlugin, excludedCellsByRule, excludedLocTypesByRule, excludedNamesByRule);

            if (settings.Verbose.ExcludedAnimals)
                PrintExcludedAnimals(excludedDetails);

            PrintGeneralSummary(
                settings,
                patchedRaces,
                patchedCount,
                alreadyOwnedCount,
                missingFactionCount,
                unknownCount,
                excludedCount,
                excludedOwnerVotesCount,
                unresolvedNpcBaseCount);

            PrintClosingNotes(settings);
        }

        private static void PrintPlacedNpcsByPlugin(
            Dictionary<string, int> allPlacedNpcCountsByPlugin,
            Dictionary<string, int> raceMatchedCountsByPlugin)
        {
            _lastWasDivider = false;
            PrintShortDivider();
            ConsoleWriteLine("PLACED NPCs SEEN, BY PLUGIN FILENAME".PadLeft(46));
            PrintShortDivider();
            ConsoleWriteLine("(diagnostic: shows every plugin the patcher saw ANY placed NPC from, and how many of");
            ConsoleWriteLine("those race-matched as farm animals — before any exclusion/ownership filtering runs)");
            PrintShortDivider();

            foreach (var kvp in allPlacedNpcCountsByPlugin.OrderByDescending(k => k.Value))
            {
                raceMatchedCountsByPlugin.TryGetValue(kvp.Key, out var raceMatched);
                ConsoleWriteLine($"{kvp.Key}   ({kvp.Value} placed NPCs total, {raceMatched} race-matched as farm animals)");
            }
        }

        private static void PrintPatchedByCell(
            Dictionary<string, List<(string Animal, string Plugin, string? OwnerFaction, string Reason)>> patchedAnimalsByCell)
        {
            var totalPatched = patchedAnimalsByCell.Values.SelectMany(v => v).Count();

            _lastWasDivider = false;
            PrintShortDivider();
            ConsoleWriteLine("PATCHED BY CELL".PadLeft(36));
            ConsoleWriteLine($"Total patched: {totalPatched}".PadLeft(37));
            PrintShortDivider();

            foreach (var kvp in patchedAnimalsByCell.OrderByDescending(k => k.Value.Count))
            {
                ConsoleWriteLine($"{kvp.Key}   ({kvp.Value.Count} patched)");

                var byPlugin = kvp.Value
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
                        ConsoleWriteLine($"          {entry.Count} x {entry.Animal}  now owned by:  {entry.OwnerFaction}  through:  {entry.Reason}");
                    }
                }

                PrintDivider();
            }
        }

        private static void PrintOwnershipSourceSummary(
            Dictionary<string, List<(string Animal, string Plugin, string? OwnerFaction, string Reason)>> patchedAnimalsByCell)
        {
            _lastWasDivider = false;
            PrintShortDivider();
            ConsoleWriteLine("OWNERSHIP SOURCE SUMMARY".PadLeft(41));
            PrintShortDivider();

            var bySource = patchedAnimalsByCell.Values
                .SelectMany(v => v)
                .GroupBy(a => a.Reason.StartsWith("ownership vote", StringComparison.OrdinalIgnoreCase) ? "Ownership vote" : a.Reason)
                .Select(g => new { Reason = g.Key, Count = g.Count() })
                .OrderByDescending(a => a.Count);

            foreach (var entry in bySource)
            {
                ConsoleWriteLine($"{entry.Count} farm animals were assigned an owner via: {entry.Reason}");
            }
        }

        private static void PrintSkippedByCell(
            Dictionary<string, List<(string Animal, string Plugin, string Reason)>> skippedAnimalsByCell)
        {
            var totalSkipped = skippedAnimalsByCell.Values.SelectMany(v => v).Count();

            _lastWasDivider = false;
            PrintShortDivider();
            ConsoleWriteLine("SKIPPED BY CELL".PadLeft(35));
            ConsoleWriteLine($"Total skipped: {totalSkipped}".PadLeft(36));
            PrintShortDivider();

            foreach (var kvp in skippedAnimalsByCell.OrderByDescending(k => k.Value.Count))
            {
                ConsoleWriteLine($"{kvp.Key}   ({kvp.Value.Count} skipped)");

                var byPlugin = kvp.Value
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
                        ConsoleWriteLine($"          {entry.Count} x {entry.Animal}   skipped: {entry.Reason}");
                    }
                }

                PrintDivider();
            }
        }

        private static void PrintExclusionSummary(
            Settings settings,
            Dictionary<string, List<string>> excludedAnimalsByPlugin,
            Dictionary<string, List<string>> excludedCellsByRule,
            Dictionary<string, List<string>> excludedLocTypesByRule,
            Dictionary<string, List<string>> excludedNamesByRule)
        {
            _lastWasDivider = false;
            PrintShortDivider();
            ConsoleWriteLine("EXCLUSION SUMMARY".PadLeft(37));
            PrintShortDivider();

            var combined = new List<(string Rule, List<string> Animals, string Type)>();

            foreach (var rule in settings.ExcludePlugins)
            {
                var animals = excludedAnimalsByPlugin
                    .Where(kv => MatchesPattern(rule, kv.Key))
                    .SelectMany(kv => kv.Value)
                    .ToList();

                if (animals.Count > 0)
                    combined.Add((rule, animals, "Plugin filename"));
            }

            foreach (var rule in settings.ExcludeCellRules)
            {
                if (excludedCellsByRule.TryGetValue(rule, out var cells) && cells.Count > 0)
                    combined.Add((rule, cells, "Cell EditorID"));
            }

            foreach (var rule in settings.ExcludeLocTypeRules)
            {
                if (excludedLocTypesByRule.TryGetValue(rule, out var names) && names.Count > 0)
                    combined.Add((rule, names, "LocType keyword EditorID"));
            }

            foreach (var term in settings.ExcludeNameTerms)
            {
                var animals = excludedNamesByRule
                    .Where(kvp => MatchesPattern(term, kvp.Key))
                    .SelectMany(kvp => kvp.Value)
                    .ToList();

                if (animals.Count > 0)
                    combined.Add((term, animals, "Base NPC EditorID"));
            }

            foreach (var entry in combined.OrderByDescending(e => e.Animals.Count))
            {
                ConsoleWriteLine($"The rule: {entry.Rule} ({entry.Type}) excluded {entry.Animals.Count} animals");

                if (!settings.Verbose.ExclusionDetail)
                    continue;

                foreach (var group in entry.Animals.GroupBy(a => a, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()))
                {
                    ConsoleWriteLine($"     {group.Count()} x {group.Key}");
                }
            }
        }

        private static void PrintExcludedAnimals(
            List<(string Animal, string Race, string Cell, string Plugin, string Rule, string RuleType)> excludedDetails)
        {
            if (excludedDetails.Count == 0)
                return;

            _lastWasDivider = false;
            PrintShortDivider();
            ConsoleWriteLine("EXCLUDED ANIMALS BY RACE".PadLeft(42));
            ConsoleWriteLine($"Total excluded: {excludedDetails.Count}".PadLeft(43));
            PrintShortDivider();

            foreach (var byRace in excludedDetails.GroupBy(e => e.Race, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()))
            {
                ConsoleWriteLine($"    {byRace.Count()}  {byRace.Key}");
            }

            _lastWasDivider = false;
            PrintShortDivider();
            ConsoleWriteLine("EXCLUDED ANIMALS BY CELL".PadLeft(42));
            PrintShortDivider();

            foreach (var byCell in excludedDetails.GroupBy(e => e.Cell, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()))
            {
                ConsoleWriteLine($"{byCell.Key}   ({byCell.Count()} excluded)");

                foreach (var group in byCell.GroupBy(e => new { e.Race, e.Rule, e.RuleType }).OrderByDescending(g => g.Count()))
                {
                    ConsoleWriteLine($"     {group.Count()} x {group.Key.Race}   excluded by {group.Key.RuleType} rule: {group.Key.Rule}");
                }

                PrintDivider();
            }
        }

        private static void PrintGeneralSummary(
            Settings settings,
            Dictionary<string, PatchedRaceSummary> patchedRaces,
            int patchedCount,
            int alreadyOwnedCount,
            int missingFactionCount,
            int unknownCount,
            int excludedCount,
            int excludedOwnerVotesCount,
            int unresolvedNpcBaseCount)
        {
            _lastWasDivider = false;
            PrintShortDivider();
            ConsoleWriteLine("GENERAL SUMMARY".PadLeft(35));
            PrintShortDivider();

            var summaryLines = new List<(string Label, int Count, bool ShowRaces)>
            {
                ("Farm animals have been assigned owners", patchedCount, true),
                ("Farm animals were already owned", alreadyOwnedCount, false),
                ("Farm animals had no suitable owner", missingFactionCount, false),
                ("Farm animals were in an unknown location", unknownCount, false),
                ("Farm animals were excluded by rules", excludedCount, false),
                ("Placed NPCs (of any kind) didn't resolve as an NPC record", unresolvedNpcBaseCount, false),
            };

            if (settings.Verbose.ExclusionDetail)
                summaryLines.Add(("Owned animals excluded from voting by Owner EditorID terms", excludedOwnerVotesCount, false));

            foreach (var (label, count, showRaces) in summaryLines.OrderByDescending(l => l.Count))
            {
                ConsoleWriteLine($"{count} {label}");

                if (showRaces)
                {
                    foreach (var kvp in patchedRaces.OrderByDescending(k => k.Value.Count))
                    {
                        var raceLine = $"    {kvp.Value.Count}  {kvp.Key}(s)";

                        if (settings.Verbose.PatchedNpcEditorIds && kvp.Value.NpcEditorIdCounts is { } editorIdCounts)
                        {
                            var editorIds = string.Join(", ", editorIdCounts
                                .OrderByDescending(entry => entry.Value)
                                .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                                .Select(entry => $"{entry.Key} ({entry.Value})"));
                            raceLine += $"  [Base NPC EditorIDs: {editorIds}]";
                        }

                        ConsoleWriteLine(raceLine);
                    }
                }
            }
        }

        private static void PrintClosingNotes(Settings settings)
        {
            PrintDivider();
            ConsoleWriteLine("Patching is complete! Scroll up to read a report on what was patched, skipped, and excluded.");
            ConsoleWriteLine("In the General Summary, animals counted as being in an unknown location are also counted as having no suitable owner.");
            ConsoleWriteLine("The Exclusion Summary counts unowned race-matched NPCs filtered before owner selection.");

            if (settings.Verbose.PerPluginCounts)
                ConsoleWriteLine("The \"didn't resolve as an NPC record\" count covers ALL placed NPCs, not just farm animals (race can't be checked until Base resolves) — a large number here is worth investigating (e.g. animals placed via a Leveled Actor list) but isn't itself a count of missed animals.");

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
