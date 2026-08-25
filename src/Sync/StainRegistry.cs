using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CrimeSceneCoop;

internal static class StainRegistry
{
    private static readonly Dictionary<uint, Component> _byId = new();
    private static readonly Dictionary<int, uint> _instToId = new();
    private static readonly Dictionary<uint, float> _amount = new();
    private static string _scene = "";

    public static void Rebuild(string scene)
    {
        _scene = scene;
        _byId.Clear();
        _instToId.Clear();
        _amount.Clear();

        var all = Resources.FindObjectsOfTypeAll<Transform>();
        foreach (var tr in all)
        {
            if (tr == null) continue;
            if (!tr.gameObject.scene.IsValid() || tr.gameObject.scene.name != scene) continue;

            Component? stain = null;

            // Prefer real CleanableDecal component
            foreach (var c in tr.GetComponents<Component>())
            {
                if (c == null) continue;
                var name = c.GetType().Name;
                if (name == "CleanableDecal" || name == "TaintMaterialCleaner" || name == "CleanableTarget")
                {
                    stain = c;
                    break;
                }
            }

            if (stain == null)
            {
                foreach (var c in tr.GetComponents<Component>())
                {
                    if (c != null && GameProbe.IsStainComponent(c))
                    {
                        stain = c;
                        break;
                    }
                }
            }

            if (stain == null)
            {
                if (!LooksLikeStainName(tr.name)) continue;
                stain = tr;
            }

            var id = StableId(scene, tr);
            _byId[id] = stain;
            _instToId[stain.GetInstanceID()] = id;
            _amount[id] = ReadCurrentAmount(stain);
        }

        CoopLog.Info($"Indexed {_byId.Count} stains in '{scene}'.");
    }

    public static uint IdOf(object instance)
    {
        if (instance is Component c)
        {
            if (_instToId.TryGetValue(c.GetInstanceID(), out var id)) return id;
            if (c.transform != null)
            {
                id = StableId(_scene, c.transform);
                _byId[id] = c;
                _instToId[c.GetInstanceID()] = id;
                return id;
            }
        }
        return 0;
    }

    public static void MarkLocal(uint id, float amount) => _amount[id] = amount;

    public static bool TryGet(uint id, out Component c) => _byId.TryGetValue(id, out c!);

    public static IReadOnlyList<(uint id, byte amount)> Snapshot()
    {
        var list = new List<(uint, byte)>(_amount.Count);
        foreach (var kv in _amount)
        {
            if (!_byId.TryGetValue(kv.Key, out var c) || c == null) continue;
            var b = (byte)Mathf.Clamp(Mathf.RoundToInt(kv.Value * 255f), 0, 255);
            list.Add((kv.Key, b));
        }
        return list;
    }

    public static uint StableId(string scene, Transform tr)
    {
        var path = scene + "/" + PathOf(tr);
        unchecked
        {
            uint h = 2166136261;
            foreach (var ch in path)
                h = (h ^ ch) * 16777619;
            var p = tr.position;
            h ^= (uint)Mathf.RoundToInt(p.x * 20f) * 73856093u;
            h ^= (uint)Mathf.RoundToInt(p.y * 20f) * 19349663u;
            h ^= (uint)Mathf.RoundToInt(p.z * 20f) * 83492791u;
            return h == 0 ? 1 : h;
        }
    }

    private static float ReadCurrentAmount(Component c)
    {
        if (c == null) return 1f;
        try
        {
            var t = c.GetType();
            foreach (var n in new[] { "Intensity", "intensity", "amount", "Amount", "coveragePercentage" })
            {
                var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.CanRead && p.PropertyType == typeof(float))
                {
                    var val = p.GetValue(c);
                    if (val != null) return Convert.ToSingle(val);
                }
                var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(float))
                {
                    var val = f.GetValue(c);
                    if (val != null) return Convert.ToSingle(val);
                }
            }
        }
        catch { }
        return 1f;
    }

    private static string PathOf(Transform tr)
    {
        var s = tr.name;
        var p = tr.parent;
        var guard = 0;
        while (p != null && guard++ < 24)
        {
            s = p.name + "/" + s;
            p = p.parent;
        }
        return s;
    }

    private static bool LooksLikeStainName(string n)
    {
        n = n.ToLowerInvariant();
        return n.Contains("stain") || n.Contains("blood") || n.Contains("dirt") ||
        n.Contains("decal") || n.Contains("splatter") || n.Contains("gore") ||
        n.Contains("mess") || n.Contains("filth") || n.Contains("puddle");
    }
}
