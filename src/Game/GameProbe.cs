using System.Reflection;
using System.Text.Json;
using MelonLoader;
using UnityEngine;

namespace CrimeSceneCoop;

/// <summary>
/// Walks game assemblies once and records stain / player / clean types.
/// Writes UserData/TwoClean/probe.json so we can tighten hooks without guessing.
/// </summary>
internal static class GameProbe
{
    public static List<Type> StainTypes { get; } = new();
    public static List<MethodInfo> CleanMethods { get; } = new();
    public static List<MethodInfo> StrokeMethods { get; } = new();
    public static Type? PlayerType { get; private set; }
    public static MethodInfo? PlayerMoveMethod { get; private set; }

    private static readonly string[] StainHints =
    {
        "Stain", "Blood", "Dirt", "Mess", "Decal", "Cleanable", "Filth", "Gore",
        "Splatter", "Spot", "Puddle", "Residue", "Contamination", "Grime", "Smear",
        "Blob", "Goo", "Viscera", "CrimeDirt", "Washable", "Moppable"
    };

    private static readonly string[] CleanHints =
    {
        "Clean", "Mop", "Wash", "RemoveStain", "Erase", "ClearDirt", "OnClean",
        "ApplyClean", "CleanStain", "Subtract", "AddClean", "DoClean", "FinishClean",
        "RemoveDirt", "ClearStain", "Wipe", "Scrub", "PowerWash", "Spray"
    };

    private static readonly string[] PlayerHints =
    {
        "PlayerController", "FirstPersonController", "FPSController", "PlayerMovement",
        "Kovalsky", "CharacterControllerPlayer", "Player", "FPController"
    };

    public static void Run()
    {
        StainTypes.Clear();
        CleanMethods.Clear();
        StrokeMethods.Clear();

        var extraStain = CoopConfig.Current.ExtraStainTypeNames;
        var extraClean = CoopConfig.Current.ExtraCleanMethodNames;
        var report = new List<string>();

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = asm.GetName().Name ?? "";
            if (!name.Contains("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("CSharp", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("Il2Cpp", StringComparison.Ordinal))
                continue;
            if (name.StartsWith("Il2CppInterop", StringComparison.Ordinal) ||
                name.StartsWith("MelonLoader", StringComparison.Ordinal))
                continue;

            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
            catch { continue; }

            foreach (var t in types)
            {
                if (t == null || t.IsAbstract) continue;
                var tn = t.Name;

                if (Matches(tn, StainHints) || extraStain.Contains(t.FullName) || extraStain.Contains(tn))
                {
                    StainTypes.Add(t);
                    report.Add("stain  " + t.FullName);
                    CollectMethods(t, CleanHints, extraClean, CleanMethods, report, "clean");
                    CollectStroke(t, StrokeMethods, report);
                }

                if (PlayerType == null && (Matches(tn, PlayerHints) ||
                    string.Equals(tn, CoopConfig.Current.PlayerTypeName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.FullName, CoopConfig.Current.PlayerTypeName, StringComparison.OrdinalIgnoreCase)))
                {
                    if (typeof(MonoBehaviour).IsAssignableFrom(t) || tn.Contains("Player"))
                    {
                        PlayerType = t;
                        report.Add("player " + t.FullName);
                    }
                }
            }
        }

        // Also scan every MonoBehaviour for Clean* methods even if the type name is opaque
        if (CleanMethods.Count == 0)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (t == null) continue;
                    CollectMethods(t, CleanHints, extraClean, CleanMethods, report, "clean");
                    if (CleanMethods.Count > 40) break;
                }
                if (CleanMethods.Count > 40) break;
            }
        }

        try
        {
            Directory.CreateDirectory(CoopConfig.Dir);
            File.WriteAllText(
                Path.Combine(CoopConfig.Dir, "probe.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }

        CoopLog.Info($"Probe: {StainTypes.Count} stain types, {CleanMethods.Count} clean methods, player={(PlayerType?.Name ?? "auto")}");
    }

    public static bool IsStainComponent(Component c)
    {
        if (c == null) return false;
        var t = c.GetType();
        foreach (var s in StainTypes)
            if (s.IsAssignableFrom(t)) return true;
        var n = t.Name;
        return Matches(n, StainHints);
    }

    private static void CollectMethods(Type t, string[] hints, List<string> extra, List<MethodInfo> into, List<string> report, string tag)
    {
        MethodInfo[] methods;
        try { methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
        catch { return; }
        foreach (var m in methods)
        {
            if (m.IsSpecialName || m.IsConstructor) continue;
            if (!Matches(m.Name, hints) && !extra.Contains(m.Name) && !extra.Contains(t.Name + "." + m.Name))
                continue;
            if (into.Any(x => x.DeclaringType == m.DeclaringType && x.Name == m.Name && x.GetParameters().Length == m.GetParameters().Length))
                continue;
            into.Add(m);
            report.Add($"{tag}   {t.FullName}.{m.Name}");
        }
    }

    private static void CollectStroke(Type t, List<MethodInfo> into, List<string> report)
    {
        MethodInfo[] methods;
        try { methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
        catch { return; }
        foreach (var m in methods)
        {
            var ps = m.GetParameters();
            if (ps.Length < 1) continue;
            var hasVec = ps.Any(p => p.ParameterType == typeof(Vector3) || p.ParameterType.Name.Contains("Vector3"));
            if (!hasVec) continue;
            if (!Matches(m.Name, CleanHints) && !m.Name.Contains("Paint") && !m.Name.Contains("Stroke") && !m.Name.Contains("Apply"))
                continue;
            into.Add(m);
            report.Add("stroke " + t.FullName + "." + m.Name);
        }
    }

    private static bool Matches(string name, string[] hints)
    {
        foreach (var h in hints)
            if (name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }
}
