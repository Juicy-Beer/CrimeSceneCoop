using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using HarmonyInstance = HarmonyLib.Harmony;

namespace CrimeSceneCoop;

internal static class HarmonyHooks
{
    public static void Install(HarmonyInstance harmony)
    {
        var hooked = 0;
        foreach (var m in GameProbe.CleanMethods.Concat(GameProbe.StrokeMethods).Distinct())
        {
            if (m == null) continue;
            // Ignore getter methods like 'GetCleaned' to prevent frame-rate trampoline crashes
            if (m.Name.StartsWith("Get", StringComparison.OrdinalIgnoreCase) || m.ReturnType != typeof(void))
                continue;

            try
            {
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(HarmonyHooks), nameof(CleanPostfix)));
                hooked++;
            }
            catch (Exception ex)
            {
                CoopLog.Warn($"Could not patch {m.DeclaringType?.Name}.{m.Name}: {ex.Message}");
            }
        }

        foreach (var t in GameProbe.StainTypes)
        {
            // Filter out non-MonoBehaviour types to prevent AccessTools warning spam
            if (t == null || !typeof(Component).IsAssignableFrom(t)) continue;
            TryPatch(harmony, t, "OnDestroy", nameof(GonePostfix));
            TryPatch(harmony, t, "OnDisable", nameof(GonePostfix));
            TryPatch(harmony, t, "Destroy", nameof(GonePostfix));
        }

        CoopLog.Info($"Harmony hooked {hooked} clean/stroke methods.");
    }

    private static void TryPatch(HarmonyInstance harmony, Type t, string method, string postfix)
    {
        try
        {
            // Only search methods declared directly on the target class to prevent console noise
            var m = t.GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (m == null) return;
            harmony.Patch(m, postfix: new HarmonyMethod(typeof(HarmonyHooks), postfix));
        }
        catch { }
    }

    public static void CleanPostfix(object? __instance, MethodBase? __originalMethod, object[]? __args)
    {
        if (__instance == null || CoopSession.SuppressHooks || !CoopSession.IsActive) return;

        try
        {
            var id = StainRegistry.IdOf(__instance);
            if (id == 0) return;

            var amount = ReadAmount(__instance);
            CoopSession.SendStainCleaned(id, amount);

            Vector3 pos = default, nrm = Vector3.up;
            float radius = 0.25f, strength = 1f;
            if (TryVec3(__instance, out pos) || TryVec3Arg(__args, out pos))
            {
                TryVec3Arg(__args, 1, out nrm);
                CoopSession.SendStainStroke(id, pos, nrm, radius, strength, 0);
            }
            StainRegistry.MarkLocal(id, amount);
        }
        catch
        {
            // Suppress exception loops to protect frame rate
        }
    }

    public static void GonePostfix(object? __instance)
    {
        if (__instance == null || CoopSession.SuppressHooks || !CoopSession.IsActive) return;

        try
        {
            var id = StainRegistry.IdOf(__instance);
            if (id == 0) return;
            CoopSession.SendStainCleaned(id, 0f);
            StainRegistry.MarkLocal(id, 0f);
        }
        catch { }
    }

    private static float ReadAmount(object? inst)
    {
        if (inst == null) return 0f;

        foreach (var n in new[] { "amount", "Amount", "clean", "Clean", "dirt", "Dirt", "progress", "Progress", "health", "Health" })
        {
            try
            {
                var f = inst.GetType().GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && (f.FieldType == typeof(float) || f.FieldType == typeof(double)))
                {
                    var val = f.GetValue(inst);
                    if (val != null) return Convert.ToSingle(val);
                }

                var p = inst.GetType().GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.CanRead && (p.PropertyType == typeof(float) || p.PropertyType == typeof(double)))
                {
                    var val = p.GetValue(inst);
                    if (val != null) return Convert.ToSingle(val);
                }
            }
            catch { }
        }
        return 0f;
    }

    private static bool TryVec3(object? inst, out Vector3 v)
    {
        v = default;
        if (inst is Component c && c != null)
        {
            try
            {
                v = c.transform.position;
                return true;
            }
            catch { }
        }
        return false;
    }

    private static bool TryVec3Arg(object[]? args, out Vector3 v) => TryVec3Arg(args, 0, out v);

    private static bool TryVec3Arg(object[]? args, int start, out Vector3 v)
    {
        v = default;
        if (args == null) return false;
        for (var i = start; i < args.Length; i++)
        {
            if (args[i] is Vector3 vec) { v = vec; return true; }
        }
        return false;
    }
}
