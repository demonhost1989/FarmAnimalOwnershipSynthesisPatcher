using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
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
        // Location categorization
        // ------------------------------------------------------------------

        public enum LocationCategory
        {
            Town, Farm, Unknown, Mill, Wilderness, Stable, Stronghold, Palace, Urban,
        }

        public static (LocationCategory category, ILocationGetter? matched) CategorizeLocation(
            ILocationGetter? location,
            ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
            ICellGetter? cell)
        {
            // Location-based keyword detection. Substring matching, because the real keyword
            // EditorIDs are "LocTypeFarm", "LocTypeTown", etc. — an exact set lookup of "Farm"
            // would never match anything.
            if (location != null)
            {
                var keywordEdids = location.Keywords?
                    .Select(k => k.TryResolve(linkCache)?.EditorID)
                    .Where(e => e != null)
                    .Select(e => e!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (keywordEdids != null)
                {
                    bool HasKeyword(string term) =>
                        keywordEdids.Any(k => k.Contains(term, StringComparison.OrdinalIgnoreCase));

                    if (HasKeyword("Farm"))
                        return (LocationCategory.Farm, location);

                    if (HasKeyword("Mill"))
                        return (LocationCategory.Mill, location);

                    if (HasKeyword("Settlement") || HasKeyword("Town")
                        || HasKeyword("City") || HasKeyword("Village"))
                        return (LocationCategory.Town, location);

                    if (HasKeyword("Castle") || HasKeyword("Palace") || HasKeyword("Temple"))
                        return (LocationCategory.Palace, location);

                    if (HasKeyword("OrcStronghold"))
                        return (LocationCategory.Stronghold, location);

                    if (HasKeyword("Cemetery")
                        || HasKeyword("Dwelling")
                        || HasKeyword("Guild")
                        || HasKeyword("Habitation")
                        || HasKeyword("Inn")
                        || HasKeyword("Store"))
                        return (LocationCategory.Urban, location);
                }
            }

            // Cell EditorID detection (fallback when there's no location or no useful keywords)
            if (cell?.EditorID is string edid)
            {

                if (edid.Contains("wilderness", StringComparison.OrdinalIgnoreCase))
                    return (LocationCategory.Wilderness, location);

                if (edid.Contains("farm", StringComparison.OrdinalIgnoreCase))
                    return (LocationCategory.Farm, location);

                if (edid.Contains("mill", StringComparison.OrdinalIgnoreCase))
                    return (LocationCategory.Mill, location);

                if (edid.Contains("stable", StringComparison.OrdinalIgnoreCase))
                    return (LocationCategory.Stable, location);

                if (edid.Contains("village", StringComparison.OrdinalIgnoreCase)
                    || edid.Contains("settlement", StringComparison.OrdinalIgnoreCase)
                    || edid.Contains("town", StringComparison.OrdinalIgnoreCase)
                    || edid.Contains("city", StringComparison.OrdinalIgnoreCase))
                    return (LocationCategory.Town, location);
                
                if (edid.Contains("castle", StringComparison.OrdinalIgnoreCase)
                    || edid.Contains("palace", StringComparison.OrdinalIgnoreCase)
                    || edid.Contains("temple", StringComparison.OrdinalIgnoreCase))
                    return (LocationCategory.Palace, location);

                if (edid.Contains("cemetary", StringComparison.OrdinalIgnoreCase)
                    || edid.Contains("dwelling", StringComparison.OrdinalIgnoreCase)
                    || edid.Contains("guild", StringComparison.OrdinalIgnoreCase)
                    || edid.Contains("habitation", StringComparison.OrdinalIgnoreCase)
                    || edid.Contains("inn", StringComparison.OrdinalIgnoreCase)
                    || edid.Contains("dwelling", StringComparison.OrdinalIgnoreCase)
                    || edid.Contains("store", StringComparison.OrdinalIgnoreCase))
                    return (LocationCategory.Urban, location);


            }

            return (LocationCategory.Unknown, null);
        }

        // ------------------------------------------------------------------
        // Faction resolution
        // ------------------------------------------------------------------

        // Cell/Location EditorID -> Faction EditorID convention overrides, populated from
        // Settings.ConventionOverrides at the start of each run (see RunPatch). Naming
        // conventions across mods aren't standardized, so this can't be fully caught by
        // logic alone.
        private static Dictionary<string, string> ConventionOverrides = new(StringComparer.OrdinalIgnoreCase);

        // Finds a convention override for a given EditorID using partial (substring, either
        // direction) matching. The longest matching key wins, so a specific key like
        // "KynesgroveFarmsLocationTGCoKG" beats a broad one like "Kynesgrove".
        private static bool TryFindPartialConventionOverride(string editorId, out string factionEdid)
        {
            factionEdid = string.Empty;
            if (string.IsNullOrWhiteSpace(editorId))
                return false;

            var match = ConventionOverrides
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
        private static IFactionGetter? TryFuzzyFactionMatch(string term, Dictionary<string, IFactionGetter> factionsByEdid)
        {
            if (string.IsNullOrWhiteSpace(term))
                return null;

            return factionsByEdid.Values.FirstOrDefault(f =>
                f.EditorID != null
                && f.EditorID.EndsWith("Faction", StringComparison.OrdinalIgnoreCase)
                && f.EditorID.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        // Shared logic for the Town/Farm/Mill naming-convention lookups: strips a known suffix off the
        // EditorID, builds the expected "<BaseName><Kind>Faction" candidate, and falls back to a fuzzy
        // match (and optionally a set of extra root candidates) if no exact match is found.
        private static IFactionGetter? TryFindFactionByConvention(
            string editorId,
            string stripSuffix,
            Func<string, string> buildCandidateName,
            Dictionary<string, IFactionGetter> factionsByEdid,
            IEnumerable<string>? extraRoots = null)
        {
            var baseName = editorId.EndsWith(stripSuffix, StringComparison.OrdinalIgnoreCase)
                ? editorId[..^stripSuffix.Length]
                : editorId;

            var candidateName = buildCandidateName(baseName);
            if (factionsByEdid.TryGetValue(candidateName, out var exact))
                return exact;

            var fuzzy = TryFuzzyFactionMatch(baseName, factionsByEdid);
            if (fuzzy != null)
                return fuzzy;

            if (extraRoots != null)
            {
                foreach (var root in extraRoots.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var rootMatch = TryFuzzyFactionMatch(root, factionsByEdid);
                    if (rootMatch != null)
                        return rootMatch;
                }
            }

            return null;
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
        // Precedence: exact convention overrides -> naming conventions (location, then cell)
        // -> partial convention overrides. Partial overrides go LAST so that a broad catch-all
        // key like "Riften" can't hijack e.g. Snow-Shod Farm (whose location EDID contains
        // "Riften") away from the more specific TownSnowShodFarmFaction naming convention.
        private static IFactionGetter? TryGetTownFaction(
            ILocationGetter? location,
            Dictionary<string, IFactionGetter> factionsByEdid,
            ICellGetter? cell)
        {
            string?[] editorIds = [cell?.EditorID, location?.EditorID];

            // 1) Exact convention overrides always win.
            foreach (var edid in editorIds)
            {
                if (edid != null && ConventionOverrides.TryGetValue(edid, out var overrideEdid))
                {
                    var faction = ResolveOverrideFaction(overrideEdid, factionsByEdid);
                    if (faction != null)
                        return faction;
                }
            }

            // 2) Naming conventions against the location EditorID.
            if (location?.EditorID != null)
            {
                // Town<Name>Faction
                var townFaction = TryFindFactionByConvention(
                    location.EditorID,
                    stripSuffix: "Location",
                    buildCandidateName: baseName => $"Town{baseName}Faction",
                    factionsByEdid,
                    extraRoots: GetTownRootCandidates(
                        location.EditorID.EndsWith("Location", StringComparison.OrdinalIgnoreCase)
                            ? location.EditorID[..^"Location".Length]
                            : location.EditorID));
                if (townFaction != null)
                    return townFaction;

                // <Name>FarmFaction
                var farmFaction = TryFindFactionByConvention(
                    location.EditorID,
                    stripSuffix: "FarmLocation",
                    buildCandidateName: baseName => $"{baseName}FarmFaction",
                    factionsByEdid);
                if (farmFaction != null)
                    return farmFaction;

                // <Name>MillFaction, with an extra fallback against the cell's EditorID roots
                var millFaction = TryFindFactionByConvention(
                    location.EditorID,
                    stripSuffix: "MillLocation",
                    buildCandidateName: baseName => $"{baseName}MillFaction",
                    factionsByEdid,
                    extraRoots: GetRootsFromEditorId(cell?.EditorID));
                if (millFaction != null)
                    return millFaction;
               
                // <Name>SawmillFaction, with an extra fallback against the cell's EditorID roots
                var sawMillFaction = TryFindFactionByConvention(
                    location.EditorID,
                    stripSuffix: "SawmillLocation",
                    buildCandidateName: baseName => $"{baseName}SawmillFaction",
                    factionsByEdid,
                    extraRoots: GetRootsFromEditorId(cell?.EditorID));
                if (sawMillFaction != null)
                    return sawMillFaction;
            }

            // 3) Naming conventions against the cell EditorID, for cells whose location is missing
            // or prefixed in a way the location pass can't strip (e.g. cell "SnowShodFarmExterior"
            // -> root "SnowShodFarm" -> TownSnowShodFarmFaction, even though the location EDID
            // carries a "Riften" prefix).
            if (cell?.EditorID != null)
            {
                var cellFaction = TryFindFactionByConvention(
                    cell.EditorID,
                    stripSuffix: "Exterior",
                    buildCandidateName: baseName => $"Town{baseName}Faction",
                    factionsByEdid,
                    extraRoots: GetTownRootCandidates(cell.EditorID));
                if (cellFaction != null)
                    return cellFaction;
            }

            // 4) Partial convention overrides as the broad catch-all fallback.
            foreach (var edid in editorIds)
            {
                if (edid == null)
                    continue;

                foreach (var candidate in GetRootsFromEditorId(edid))
                {
                    if (TryFindPartialConventionOverride(candidate, out var overrideEdid))
                    {
                        var faction = ResolveOverrideFaction(overrideEdid, factionsByEdid);
                        if (faction != null)
                            return faction;
                    }
                }
            }

            return null;
        }

        // Finds a faction for animals placed by a specific plugin (Settings.PluginFactionOverrides,
        // partial plugin-name matching). First matching entry that resolves to a real faction wins.
        private static IFactionGetter? TryGetPluginFactionOverride(
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
                    return faction;
            }

            return null;
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
        // Main patching pass
        // ------------------------------------------------------------------

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var settings = LoadRunSettings(state);
            PopulateConventionOverrides(settings);

            var factionsByEdid = new Dictionary<string, IFactionGetter>(StringComparer.OrdinalIgnoreCase);
            foreach (var fac in state.LoadOrder.PriorityOrder.Faction().WinningOverrides())
            {
                if (fac.EditorID != null)
                    factionsByEdid.TryAdd(fac.EditorID, fac);
            }

            var seen = new HashSet<FormKey>();

            var patchedAnimalsByCell = new Dictionary<string, List<(string Animal, string Plugin, string? OwnerFaction)>>(StringComparer.OrdinalIgnoreCase);
            var skippedAnimalsByCell = new Dictionary<string, List<(string Animal, string Plugin, string Reason)>>(StringComparer.OrdinalIgnoreCase);
            var excludedAnimalsByPlugin = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var excludedCellsByRule = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var excludedLocTypesByRule = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var excludedNamesByRule = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var animalRaceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var patchedRaceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            int unknownCount = 0;
            int missingFactionCount = 0;
            int patchedCount = 0;
            int alreadyOwnedCount = 0;
            int excludedCount = 0;

            var unknownSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var missingFactionSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var patchedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var alreadyOwnedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var excludedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            PrintShortDivider();
            ConsoleWriteLine("PATCHING...".PadLeft(35));
            PrintShortDivider();

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
                    continue;

                var animalLabel = npc.EditorID ?? "UnknownNPC";
                string pluginName = placedNpc.FormKey.ModKey.FileName;

                // Race check first: only farm-animal races are candidates at all.
                var raceEdid = npc.Race.TryResolve(state.LinkCache)?.EditorID ?? "UnknownRace";
                bool isFarmAnimalRace = settings.IncludeRaceTerms.Any(term =>
                    raceEdid.Contains(term, StringComparison.OrdinalIgnoreCase));

                if (!isFarmAnimalRace)
                    continue;

                var displayRace = raceEdid.EndsWith("Race", StringComparison.OrdinalIgnoreCase)
                    ? raceEdid[..^"Race".Length]
                    : raceEdid;

                animalRaceCounts.TryGetValue(displayRace, out var raceCount);
                animalRaceCounts[displayRace] = raceCount + 1;

                if (!placedNpc.Owner.IsNull)
                {
                    alreadyOwnedCount++;
                    alreadyOwnedSet.Add(animalLabel);
                    continue;
                }

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

                // Location-type exclusion (matched against location keywords like "Dungeon")
                if (!cellExcluded && settings.ExcludeLocTypeRules.Count > 0)
                {
                    var loc = containingCell?.Location.TryResolve(state.LinkCache);
                    var keywordEdids = loc?.Keywords?
                        .Select(k => k.TryResolve(state.LinkCache)?.EditorID)
                        .Where(e => e != null)
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

                // Matching. Plugin overrides beat location-based matching; the raw location is
                // passed to TryGetTownFaction so convention lookups still run even when
                // categorization came up Unknown (the category only affects the skip reason).
                var location = containingCell?.Location.TryResolve(state.LinkCache);
                var (category, _) = CategorizeLocation(location, state.LinkCache, containingCell);

                var townFaction = TryGetPluginFactionOverride(pluginName, settings, factionsByEdid)
                    ?? TryGetTownFaction(location, factionsByEdid, containingCell);
                if (townFaction == null)
                {
                    missingFactionCount++;
                    missingFactionSet.Add(animalLabel);

                    var reason = category == LocationCategory.Unknown
                        ? "No suitable owner, No suitable location"
                        : "No suitable owner";

                    if (category == LocationCategory.Unknown)
                    {
                        unknownCount++;
                        unknownSet.Add(animalLabel);
                    }

                    AddSkip(skippedAnimalsByCell, animalLabel, pluginName, cellEdid, reason);
                    continue;
                }

                var patchNpc = context.GetOrAddAsOverride(state.PatchMod);
                patchNpc.Owner.SetTo(townFaction);
                patchNpc.FactionRank = 0;
                patchedCount++;
                patchedSet.Add(animalLabel);

                patchedRaceCounts.TryGetValue(displayRace, out var patchedRaceCount);
                patchedRaceCounts[displayRace] = patchedRaceCount + 1;

                if (!patchedAnimalsByCell.TryGetValue(cellEdid, out var patchedList))
                    patchedAnimalsByCell[cellEdid] = patchedList = [];

                patchedList.Add((animalLabel, pluginName, townFaction.EditorID));
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
                patchedCount,
                alreadyOwnedCount,
                missingFactionCount,
                unknownCount,
                excludedCount);
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

        // Populates the ConventionOverrides lookup from Settings.ConventionOverrides for this run.
        private static void PopulateConventionOverrides(Settings settings)
        {
            ConventionOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var duplicates = new List<string>();

            foreach (var entry in settings.ConventionOverrides)
            {
                if (string.IsNullOrWhiteSpace(entry.EditorID) || string.IsNullOrWhiteSpace(entry.FactionEditorID))
                    continue;

                var key = entry.EditorID.Trim();
                var value = entry.FactionEditorID.Trim();

                if (!ConventionOverrides.TryAdd(key, value))
                    duplicates.Add(key);
            }

            if (duplicates.Count > 0)
            {
                ConsoleWriteLine($"WARNING: Duplicate Convention Override EditorIDs were ignored (first entry wins): {string.Join(", ", duplicates)}");
            }
        }

        // ------------------------------------------------------------------
        // Reporting
        // ------------------------------------------------------------------

        private static void PrintReport(
            Settings settings,
            Dictionary<string, List<(string Animal, string Plugin, string? OwnerFaction)>> patchedAnimalsByCell,
            Dictionary<string, List<(string Animal, string Plugin, string Reason)>> skippedAnimalsByCell,
            Dictionary<string, List<string>> excludedAnimalsByPlugin,
            Dictionary<string, List<string>> excludedCellsByRule,
            Dictionary<string, List<string>> excludedLocTypesByRule,
            Dictionary<string, List<string>> excludedNamesByRule,
            Dictionary<string, int> patchedRaceCounts,
            int patchedCount,
            int alreadyOwnedCount,
            int missingFactionCount,
            int unknownCount,
            int excludedCount)
        {
            var totalPatched = patchedAnimalsByCell.Values.SelectMany(v => v).Count();

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
                        .GroupBy(a => new { a.Animal, a.OwnerFaction })
                        .Select(g => new { g.Key.Animal, g.Key.OwnerFaction, Count = g.Count() })
                        .OrderByDescending(a => a.Count);

                    foreach (var entry in byAnimal)
                    {
                        ConsoleWriteLine($"          {entry.Count} {entry.Animal}(s)   now owned by:   {entry.OwnerFaction}");
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
                ("Farm animals had no suitable owner", missingFactionCount, false),
                ("Farm animals were in an unsuitable location", unknownCount, false),
                ("Farm animals were excluded by rules", excludedCount, false),
            };

            foreach (var (label, count, showRaces) in summaryLines.OrderByDescending(l => l.Count))
            {
                ConsoleWriteLine($"{count} {label}");

                if (showRaces)
                {
                    foreach (var kvp in patchedRaceCounts.OrderByDescending(k => k.Value))
                    {
                        ConsoleWriteLine($"    {kvp.Value}  {kvp.Key}");
                    }
                }
            }

            PrintDivider();
            ConsoleWriteLine("Patching is complete! Scroll up to read a report on what was patched, skipped, and excluded.");
            ConsoleWriteLine("A couple of notes on the summaries: In the General Summary there is typically a large overlap between no suitable owner and an unsuitable location, since they can both be true.");
            ConsoleWriteLine("The Exclusion Summary displays the NPCs who would have been patched by the logic were it not for exclusion rules.");
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