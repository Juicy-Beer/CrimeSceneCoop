using System.Reflection;
using UnityEngine;

namespace CrimeSceneCoop;

/// <summary>
/// Local mop is instant (the game already cleaned it). The packet goes out
/// immediately; the friend applies the same clean on their client.
/// A 2 Hz snapshot heals any missed event so both floors stay identical.
/// </summary>
internal static class StainSync
{
    public static void Tick(float dt)
    {
        // snapshot send lives in CoopSession; nothing per-frame here
    }

    public static void ApplyRemoteClean(uint id, float amount)
    {
        CoopSession.SuppressHooks = true;
        try
        {
            if (!StainRegistry.TryGet(id, out var c) || c == null)
            {
                StainRegistry.MarkLocal(id, amount);
                return;
            }
            ApplyCleanTo(c, amount);
            StainRegistry.MarkLocal(id, amount);
        }
        finally
        {
            CoopSession.SuppressHooks = false;
        }
    }

    public static void ApplyRemoteStroke(BinaryReader r)
    {
        var id = r.ReadUInt32();
        var pos = NetProtocol.ReadVec3(r);
        var nrm = NetProtocol.ReadVec3(r);
        var radius = r.ReadSingle();
        var strength = r.ReadSingle();
        r.ReadByte();
        CoopSession.SuppressHooks = true;
        try
        {
            if (!StainRegistry.TryGet(id, out var c) || c == null) return;
            InvokeStroke(c, pos, nrm, radius, strength);
        }
        finally
        {
            CoopSession.SuppressHooks = false;
        }
    }

    public static void ApplySnapshot(BinaryReader r)
    {
        var scene = NetProtocol.ReadString(r);
        var n = r.ReadUInt16();
        var seen = new HashSet<uint>();
        for (var i = 0; i < n; i++)
        {
            var id = r.ReadUInt32();
            var amount = r.ReadByte() / 255f;
            seen.Add(id);
            if (amount <= 0.02f) ApplyRemoteClean(id, 0f);
        }
        // Anything we still have that the host no longer lists is gone
        foreach (var (id, amount) in StainRegistry.Snapshot())
        {
            if (!seen.Contains(id) && amount > 0)
                ApplyRemoteClean(id, 0f);
        }
        _ = scene;
    }

    private static void ApplyCleanTo(Component c, float amount)
    {
        var t = c.GetType();
        foreach (var m in GameProbe.CleanMethods)
        {
            if (m.DeclaringType != null && m.DeclaringType.IsAssignableFrom(t))
            {
                try
                {
                    var ps = m.GetParameters();
                    if (ps.Length == 0) m.Invoke(c, null);
                    else if (ps.Length == 1 && ps[0].ParameterType == typeof(float))
                        m.Invoke(c, new object[] { amount });
                    else continue;
                    if (amount <= 0.02f) Hide(c);
                    return;
                }
                catch { }
            }
        }

        foreach (var n in new[] { "amount", "Amount", "dirt", "Dirt", "cleanProgress", "health" })
        {
            var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null && f.FieldType == typeof(float)) f.SetValue(c, amount);
            var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanWrite && p.PropertyType == typeof(float)) p.SetValue(c, amount);
        }

        if (amount <= 0.02f) Hide(c);
    }

    private static void InvokeStroke(Component c, Vector3 pos, Vector3 nrm, float radius, float strength)
    {
        foreach (var m in GameProbe.StrokeMethods)
        {
            if (m.DeclaringType == null || !m.DeclaringType.IsAssignableFrom(c.GetType())) continue;
            try
            {
                var ps = m.GetParameters();
                var args = new object[ps.Length];
                for (var i = 0; i < ps.Length; i++)
                {
                    var pt = ps[i].ParameterType;
                    if (pt == typeof(Vector3) && i == 0) args[i] = pos;
                    else if (pt == typeof(Vector3)) args[i] = nrm;
                    else if (pt == typeof(float) && i < 3) args[i] = radius;
                    else if (pt == typeof(float)) args[i] = strength;
                    else args[i] = pt.IsValueType ? Activator.CreateInstance(pt)! : null!;
                }
                m.Invoke(c, args);
                return;
            }
            catch { }
        }
    }

    private static void Hide(Component c)
    {
        if (c == null) return;
        try
        {
            foreach (var r in c.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;
            foreach (var col in c.GetComponentsInChildren<Collider>(true))
                col.enabled = false;
            c.gameObject.SetActive(false);
        }
        catch { }
    }
}
