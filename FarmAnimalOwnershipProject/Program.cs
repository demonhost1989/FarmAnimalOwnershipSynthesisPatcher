using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Synthesis.States;
using Noggog;
using System.Text;
using System.Text.Json;
using static Mutagen.Bethesda.Skyrim.Furniture;

namespace FarmAnimalOwnershipProject




{   // Classes //
    public class UserSettings
    {
        // Whether to enable verbose logging (kept for compatibility)
        public bool EnableLogging { get; set; } = false;

        // The animal races we are looking for
        public string[] IncludeRaceTerms { get; set; } = new[]
        {
            "Goat", "Chicken", "Cow", "Horse", "Pig", "Sheep", "Dog", "Cat", "Bunny", "Husky"
        };

        // Animal names we want to exclude
        public string[] ExcludeNameTerms { get; set; } = new[]
        {
            "Wild", "Bandit", "Forsworn", "Sabre", "Pigeon", "Zombie", "Draugr", "Durzog", "Stray"
        };

        // Plugin exclusion patterns (wildcards supported)
        public string[] ExcludePlugins { get; set; } = new[]
        {
            "Vigilant.esm", "*FollowerFramework*", "*SkyrimUnderground*", "*HearthFire*", "cc*"
        };

        // Cell exclusion patterns (wildcards supported)
        public string[] ExcludeCellRules { get; set; } = new[]
        {
            "BYOH*", "cc*"
        };


        // Load merged settings from default locations (exe folder then %APPDATA%) without running the pipeline.
        private static UserSettings LoadMergedSettings()
        {
            // Start with defaults
            var result = new UserSettings();

            try
            {
                // Honor an explicit override via env var or command-line
                string? overridePath = Environment.GetEnvironmentVariable("FAO_SETTINGS_PATH");
                if (string.IsNullOrWhiteSpace(overridePath))
                {
                    var cmd = Environment.GetCommandLineArgs();
                    for (int i = 0; i < cmd.Length; ++i)
                    {
                        var a = cmd[i];
                        if (string.Equals(a, "--settings-path", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-s", StringComparison.OrdinalIgnoreCase))
                        {
                            if (i + 1 < cmd.Length)
                                overridePath = cmd[i + 1];
                        }
                        else if (a.StartsWith("--settings-path=", StringComparison.OrdinalIgnoreCase))
                        {
                            overridePath = a.Substring("--settings-path=".Length);
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
                {
                    var json = File.ReadAllText(overridePath);
                    var fileSettings = JsonSerializer.Deserialize<UserSettings>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (fileSettings != null)
                    {
                        if (fileSettings.IncludeRaceTerms is not null)
                            result.IncludeRaceTerms = fileSettings.IncludeRaceTerms;
                        if (fileSettings.ExcludeNameTerms is not null)
                            result.ExcludeNameTerms = fileSettings.ExcludeNameTerms;
                        if (fileSettings.ExcludePlugins is not null)
                            result.ExcludePlugins = fileSettings.ExcludePlugins;
                        if (fileSettings.ExcludeCellRules is not null)
                            result.ExcludeCellRules = fileSettings.ExcludeCellRules;
                        result.EnableLogging = fileSettings.EnableLogging;
                    }
                }
                else
                {
                    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    // Prefer a settings file located in the project folder (three levels up from the build output),
                    // then the executable folder, then the per-user APPDATA location.
                    var projectLevel = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FarmAnimalOwnershipSettings.json"));
                    // Also check solution-level (one directory above project) in case the generator ran from solution root
                    var solutionLevel = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "FarmAnimalOwnershipSettings.json"));
                    var exeLevel = Path.Combine(AppContext.BaseDirectory, "FarmAnimalOwnershipSettings.json");
                    var appDataLevel = Path.Combine(appData, "FarmAnimalOwnershipSynthesis", "FarmAnimalOwnershipSettings.json");
                    var candidates = new[] { projectLevel, solutionLevel, exeLevel, appDataLevel };

                    foreach (var settingsPath in candidates)
                    {
                        if (!File.Exists(settingsPath))
                            continue;

                        var json = File.ReadAllText(settingsPath);
                        var fileSettings = JsonSerializer.Deserialize<UserSettings>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (fileSettings != null)
                        {
                            if (fileSettings.IncludeRaceTerms is not null)
                                result.IncludeRaceTerms = fileSettings.IncludeRaceTerms;
                            if (fileSettings.ExcludeNameTerms is not null)
                                result.ExcludeNameTerms = fileSettings.ExcludeNameTerms;
                            if (fileSettings.ExcludePlugins is not null)
                                result.ExcludePlugins = fileSettings.ExcludePlugins;
                            if (fileSettings.ExcludeCellRules is not null)
                                result.ExcludeCellRules = fileSettings.ExcludeCellRules;
                            result.EnableLogging = fileSettings.EnableLogging;

                            // Stop at the first valid file
                            break;
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            return result;
        }



        public class Program
        {
            public static class FarmAnimalOwnershipPatcher
            {

                // Current settings instance (populated by Synthesis UI)
                private static UserSettings Settings = new UserSettings();

                // Autogenerated settings container populated by Synthesis when available
                // SetAutogeneratedSettings expects an out Lazy<T> variable; initialize with null-forgiving to be assigned by the pipeline.
                private static Lazy<PatcherSettings> AutogeneratedSettings = null!;
            // Optional override for where to load/save the JSON settings file.
            // Can be set via the FAO_SETTINGS_PATH environment variable or --settings-path / -s CLI flag.
            private static string? SettingsPathOverride = null;

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

                // Parse semicolon-separated list from UI settings
                private static string[] ParseList(string value)
                {
                    if (string.IsNullOrWhiteSpace(value))
                        return Array.Empty<string>();

                    return value.Split(';', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToArray();
                }

                // Convert PatcherSettings (UI-facing semicolon strings) into the runtime UserSettings.
                private static UserSettings ConvertFrom(PatcherSettings synthSettings)
                {
                    if (synthSettings == null)
                        return new UserSettings();

                    var us = new UserSettings();
                    var include = ParseList(synthSettings.IncludeRaceTerms);
                    if (include.Length > 0)
                        us.IncludeRaceTerms = include;

                    var excludeNames = ParseList(synthSettings.ExcludeNameTerms);
                    if (excludeNames.Length > 0)
                        us.ExcludeNameTerms = excludeNames;

                    var excludePlugins = ParseList(synthSettings.ExcludePlugins);
                    if (excludePlugins.Length > 0)
                        us.ExcludePlugins = excludePlugins;

                    var excludeCells = ParseList(synthSettings.ExcludeCellRules);
                    if (excludeCells.Length > 0)
                        us.ExcludeCellRules = excludeCells;

                    us.EnableLogging = synthSettings.EnableLogging;
                    return us;
                }

                // Compiled regex caches for wildcard rules to avoid recompiling on each check.
                private static System.Text.RegularExpressions.Regex[] _excludePluginRegexes = Array.Empty<System.Text.RegularExpressions.Regex>();
                private static System.Text.RegularExpressions.Regex[] _excludeCellRegexes = Array.Empty<System.Text.RegularExpressions.Regex>();

                private static void BuildRegexCaches(UserSettings runtimeSettings)
                {
                    try
                    {
                        _excludePluginRegexes = runtimeSettings.ExcludePlugins?
                            .Select(p => "^" + System.Text.RegularExpressions.Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$")
                            .Select(pat => new System.Text.RegularExpressions.Regex(pat, System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                            .ToArray() ?? Array.Empty<System.Text.RegularExpressions.Regex>();

                        _excludeCellRegexes = runtimeSettings.ExcludeCellRules?
                            .Select(r => "^" + System.Text.RegularExpressions.Regex.Escape(r).Replace("\\*", ".*").Replace("\\?", ".") + "$")
                            .Select(pat => new System.Text.RegularExpressions.Regex(pat, System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                            .ToArray() ?? Array.Empty<System.Text.RegularExpressions.Regex>();
                    }
                    catch
                    {
                        // If regex compilation fails for any pattern, fall back to empty caches to avoid exceptions during patching.
                        _excludePluginRegexes = Array.Empty<System.Text.RegularExpressions.Regex>();
                        _excludeCellRegexes = Array.Empty<System.Text.RegularExpressions.Regex>();
                    }
                }

                // Overload that accepts settings injected by newer Synthesis versions (if available).
                // This method will be picked up if you call AddPatch with a settings type.
                public static Task RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, PatcherSettings synthSettings)
                {
                    // Debug: log that settings overload was invoked and whether settings were provided
                    ConsoleWriteLine("RunPatch(settings) invoked");
                    if (synthSettings == null)
                    {
                        ConsoleWriteLine("No settings injected by Synthesis; using defaults or JSON fallback.");
                        return RunPatch(state);
                    }

                    ConsoleWriteLine($"Injected settings:" +
                        $"IncludeRaceTerms='{synthSettings.IncludeRaceTerms}', " +
                        $"ExcludeNameTerms='{synthSettings.ExcludeNameTerms}', " +
                        $"ExcludePlugins='{synthSettings.ExcludePlugins}', " +
                        $"ExcludeCellRules='{synthSettings.ExcludeCellRules}'");

                    // Convert and apply settings from synth UI, then rebuild regex caches
                    var converted = ConvertFrom(synthSettings);
                    if (converted != null)
                    {
                        Settings = converted;
                        BuildRegexCaches(Settings);
                    }

                    return RunPatch(state);
                }

                // Main entry point for the patcher
                public static async Task Main(string[] args)
                {
                    // Read env var and CLI override for settings path
                    var envPath = Environment.GetEnvironmentVariable("FAO_SETTINGS_PATH");
                    string? cliPath = null;
                    if (args != null)
                    {
                        for (int i = 0; i < args.Length; ++i)
                        {
                            var a = args[i];
                            if (string.Equals(a, "--settings-path", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-s", StringComparison.OrdinalIgnoreCase))
                            {
                                if (i + 1 < args.Length)
                                    cliPath = args[i + 1];
                            }
                            else if (a.StartsWith("--settings-path=", StringComparison.OrdinalIgnoreCase))
                            {
                                cliPath = a.Substring("--settings-path=".Length);
                            }
                        }
                    }

                    SettingsPathOverride = !string.IsNullOrWhiteSpace(cliPath) ? cliPath : (!string.IsNullOrWhiteSpace(envPath) ? envPath : null);

                    // Command-line helper: generate a default settings file and exit.
                    // Usage:
                    //   --generate-settings [path]
                    // If path is omitted, writes to %APPDATA%/FarmAnimalOwnershipSynthesis/FarmAnimalOwnershipSettings.json
                    if (args != null && args.Length > 0 && (args[0] == "--generate-settings" || args[0] == "-g"))
                    {
                        string outPath;
                        if (args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]))
                            outPath = args[1];
                        else
                        {
                            // Use explicit override if provided, otherwise fall back to per-user path
                            if (!string.IsNullOrWhiteSpace(SettingsPathOverride))
                            {
                                outPath = SettingsPathOverride;
                            }
                            else
                            {
                                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                                var dir = Path.Combine(appData, "FarmAnimalOwnershipSynthesis");
                                Directory.CreateDirectory(dir);
                                outPath = Path.Combine(dir, "FarmAnimalOwnershipSettings.json");
                            }
                        }

                        try
                        {
                            var options = new JsonSerializerOptions { WriteIndented = true };
                            var json = JsonSerializer.Serialize(Settings, options);
                            File.WriteAllText(outPath, json, Encoding.UTF8);
                            ConsoleWriteLine($"Wrote settings to: {outPath}");
                        }
                        catch (Exception ex)
                        {
                            ConsoleWriteLine($"Failed to write settings: {ex.Message}");
                        }

                        return;
                    }

                    // Command-line helper: print the effective merged settings as JSON and exit.
                    if (args != null && args.Length > 0 && (args[0] == "--print-settings" || args[0] == "-p"))
                    {
                        var merged = LoadMergedSettings();
                        var options = new JsonSerializerOptions { WriteIndented = true };
                        var json = JsonSerializer.Serialize(merged, options);
                        ConsoleWriteLine(json);
                        return;
                    }
                    // Ensure a fallback settings file exists alongside the executable so users
                    // who run the patcher outside of Synthesis can customize settings.
                    try
                    {
                        // If user provided an explicit path, write the default there and skip other locations.
                        if (!string.IsNullOrWhiteSpace(SettingsPathOverride))
                        {
                            try
                            {
                                var dirPath = Path.GetDirectoryName(SettingsPathOverride) ?? AppContext.BaseDirectory;
                                Directory.CreateDirectory(dirPath);
                                if (!File.Exists(SettingsPathOverride))
                                {
                                    var options = new JsonSerializerOptions { WriteIndented = true };
                                    var json = JsonSerializer.Serialize(Settings, options);
                                    File.WriteAllText(SettingsPathOverride, json, Encoding.UTF8);
                                }
                            }
                            catch
                            {
                                // ignore write errors for override path
                            }
                        }
                        else
                        {
                            // Prefer a per-user settings file in %APPDATA% to avoid permission issues.
                            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                            var dir = Path.Combine(appData, "FarmAnimalOwnershipSynthesis");
                            Directory.CreateDirectory(dir);
                            // Also ensure a copy exists in the project folder for convenience when running from the IDE
                            try
                            {
                                var projectLevel = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FarmAnimalOwnershipSettings.json"));
                                var solutionLevel = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "FarmAnimalOwnershipSettings.json"));
                                if (!File.Exists(projectLevel))
                                {
                                    try
                                    {
                                        var options2 = new JsonSerializerOptions { WriteIndented = true };
                                        var json2 = JsonSerializer.Serialize(Settings, options2);
                                        File.WriteAllText(projectLevel, json2, Encoding.UTF8);
                                    }
                                    catch
                                    {
                                        // ignore write errors to project level
                                    }
                                }
                                // If project-level didn't get written, also try solution-level for convenience
                                if (!File.Exists(projectLevel) && !File.Exists(solutionLevel))
                                {
                                    try
                                    {
                                        var options3 = new JsonSerializerOptions { WriteIndented = true };
                                        var json3 = JsonSerializer.Serialize(Settings, options3);
                                        File.WriteAllText(solutionLevel, json3, Encoding.UTF8);
                                    }
                                    catch
                                    {
                                        // ignore
                                    }
                                }
                            }
                            catch
                            {
                                // ignore errors creating project-level file
                            }

                            var settingsPath = Path.Combine(dir, "FarmAnimalOwnershipSettings.json");
                            if (!File.Exists(settingsPath))
                            {
                                var options = new JsonSerializerOptions { WriteIndented = true };
                                var json = JsonSerializer.Serialize(Settings, options);
                                File.WriteAllText(settingsPath, json, Encoding.UTF8);
                            }
                        }
                    }
                    catch
                    {
                        // If writing fails (permissions etc.), ignore and continue; the
                        // patcher will still run with defaults or UI-provided settings.
                    }

                    // Build the pipeline. Use 'dynamic' for optional AddSettings support so
                    // this compiles against Synthesis versions that may not include that API.
                    dynamic builder = SynthesisPipeline.Instance
                        .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch);

                    try
                    {
                        // If available in this Synthesis version, register runtime settings.
                        // Prefer registering the UI-facing PatcherSettings when available.
                        try
                        {
                            // Try PatcherSettings first (contains WPF attributes)
                            builder = builder.AddSettings<PatcherSettings>(new PatcherSettings());
                        }
                        catch
                        {
                            // Fall back to runtime UserSettings if only that overload exists
                            builder = builder.AddSettings<UserSettings>(Settings);
                        }
                    }
                    catch
                    {
                        // AddSettings not available; continue without it.
                    }

                    builder = builder.SetAutogeneratedSettings("Settings", "FarmAnimalOwnershipSettings.json", out AutogeneratedSettings)
                        .SetTypicalOpen(GameRelease.SkyrimSE, "FarmAnimalOwnership.esp");

                    await builder.Run(args);
                }

                // Location categories
                public enum LocationCategory
                {
                    Town, Farm, Unknown, Mill, Wilderness, Stable
                }

                // Main patching logic
                public static Task RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
                {
                    // Note: Settings are registered with Synthesis via AddSettings<UserSettings>().
                    // Some Synthesis versions automatically inject settings into the patch function;
                    // if your Synthesis version supports passing settings, we can accept a second
                    // parameter here. For compatibility, this implementation uses the static
                    // Settings instance (defaults or previously populated).

                    // Attempt to load settings from a JSON file placed alongside the executable.
                    // First, if Synthesis populated autogenerated settings, use them (UI settings).
                    if (AutogeneratedSettings != null)
                    {
                        try
                        {
                            var synthSettings = AutogeneratedSettings.Value;
                            if (synthSettings != null)
                            {
                                // Convert synth UI settings into the runtime settings and rebuild caches
                                var converted = ConvertFrom(synthSettings);
                                if (converted != null)
                                {
                                    Settings = converted;
                                    BuildRegexCaches(Settings);
                                }
                            }
                        }
                        catch
                        {
                            // ignore errors reading autogenerated settings and fall back to file
                        }
                    }

                    // This is a fallback for Synthesis versions that do not provide AddSettings UI
                    // wiring. File name: "FarmAnimalOwnershipSettings.json"
                    try
                    {
                        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                        var altPath = Path.Combine(appData, "FarmAnimalOwnershipSynthesis", "FarmAnimalOwnershipSettings.json");
                        var projectLevel = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FarmAnimalOwnershipSettings.json"));
                        var solutionLevel = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "FarmAnimalOwnershipSettings.json"));
                        var exeLevel = Path.Combine(AppContext.BaseDirectory, "FarmAnimalOwnershipSettings.json");
                        var candidates = new[] { projectLevel, solutionLevel, exeLevel, altPath };

                        foreach (var settingsPath in candidates)
                        {
                            if (!File.Exists(settingsPath))
                                continue;

                            var json = File.ReadAllText(settingsPath);
                            var fileSettings = JsonSerializer.Deserialize<UserSettings>(json, new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });

                            if (fileSettings != null)
                            {
                                // Merge non-null arrays from file into the runtime Settings
                                if (fileSettings.IncludeRaceTerms is not null)
                                    Settings.IncludeRaceTerms = fileSettings.IncludeRaceTerms;
                                if (fileSettings.ExcludeNameTerms is not null)
                                    Settings.ExcludeNameTerms = fileSettings.ExcludeNameTerms;
                                if (fileSettings.ExcludePlugins is not null)
                                    Settings.ExcludePlugins = fileSettings.ExcludePlugins;
                                if (fileSettings.ExcludeCellRules is not null)
                                    Settings.ExcludeCellRules = fileSettings.ExcludeCellRules;
                                Settings.EnableLogging = fileSettings.EnableLogging;

                                // Once we've loaded a file, stop searching further paths
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore errors reading settings file; proceed with defaults.
                    }

                    // Build regex caches from the effective runtime settings
                    BuildRegexCaches(Settings);

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

                        // Wildcard-aware cell exclusion (use compiled regex cache)
                        bool cellExcluded = false;
                        if (Settings.ExcludeCellRules != null && Settings.ExcludeCellRules.Length > 0 && _excludeCellRegexes.Length > 0)
                        {
                            for (int i = 0; i < _excludeCellRegexes.Length; ++i)
                            {
                                var rx = _excludeCellRegexes[i];
                                if (rx.IsMatch(cellEdid))
                                {
                                    var rule = (Settings.ExcludeCellRules.Length > i) ? Settings.ExcludeCellRules[i] : rx.ToString();
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
                                ("Animals have been asigned owners", patchedCount, true),
                                ("Animals were already owned", alreadyOwnedCount, false),
                                ("Animals had no suitable owner", missingFactionCount, false),
                                ("Animals were in an unsuitable location", unknownCount, false)
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

                        return Task.CompletedTask;
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
              //      ["MixwaterMillLocation"] = "MixwaterMillGilfreHouseFaction",    // note that the mill belongs to a house
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
    }
}
