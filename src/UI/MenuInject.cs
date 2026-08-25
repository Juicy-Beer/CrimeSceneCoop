using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CrimeSceneCoop;

/// <summary>
/// Clones an existing main-menu button and labels it Multiplayer.
/// Uses reflection so we compile without UnityEngine.UI sitting on disk.
/// F10 still works if inject fails.
/// </summary>
internal static class MenuInject
{
    private static float _retry;
    private static bool _done;
    private static string _scene = "";

    public static void OnSceneLoaded(string scene)
    {
        _scene = scene;
        _done = false;
        _retry = 0.4f;
    }

    public static void Tick()
    {
        if (_done || !CoopConfig.Current.InjectMainMenuButton) return;
        _retry -= Time.unscaledDeltaTime;
        if (_retry > 0) return;
        _retry = 2f;
        TryInject();
    }

    private static void TryInject()
    {
        var buttonType = FindType("UnityEngine.UI.Button") ?? FindType("Button");
        if (buttonType == null) return;

        object[] buttons;
        try
        {
            var method = typeof(Resources).GetMethods()
            .FirstOrDefault(m => m.Name == "FindObjectsOfTypeAll" && m.IsGenericMethod && m.GetParameters().Length == 0);
            if (method == null) return;
            var arr = method.MakeGenericMethod(buttonType).Invoke(null, null) as Array;
            if (arr == null || arr.Length == 0) return;
            buttons = arr.Cast<object>().ToArray();
        }
        catch { return; }

        object? source = null;
        foreach (var b in buttons)
        {
            if (b is not Component c || !c.gameObject.scene.IsValid()) continue;
            var label = LabelOf(c);
            if (label.IndexOf("multiplayer", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _done = true;
                return;
            }
            if (source == null && IsPrimary(label)) source = b;
        }
        source ??= buttons.FirstOrDefault(b => b is Component c && c.gameObject.scene.IsValid());
        if (source is not Component srcComp) return;

        try
        {
            var clone = UnityEngine.Object.Instantiate(srcComp.gameObject, srcComp.transform.parent);
            clone.name = "TwoClean_Multiplayer";
            clone.SetActive(true);
            SetLabel(clone, "Multiplayer");
            var btn = clone.GetComponent(buttonType.FullName ?? buttonType.Name);
            if (btn != null)
            {
                var onClickProp = buttonType.GetProperty("onClick");
                var evt = onClickProp?.GetValue(btn);
                var add = evt?.GetType().GetMethod("AddListener", new[] { typeof(UnityEngine.Events.UnityAction) });
                add?.Invoke(evt, new object[] { (UnityEngine.Events.UnityAction)(() => CoopOverlay.Show()) });
            }
            var rt = clone.GetComponent<RectTransform>();
            var srcRt = srcComp.GetComponent<RectTransform>();
            if (rt != null && srcRt != null)
                rt.anchoredPosition = srcRt.anchoredPosition + new Vector2(0f, -srcRt.rect.height - 8f);
            _done = true;
            CoopLog.Info("Injected Multiplayer button on " + _scene);
        }
        catch (Exception ex)
        {
            CoopLog.Warn("Menu inject: " + ex.Message);
            _done = true;
        }
    }

    private static Type? FindType(string name)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType(name);
                if (t != null) return t;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(x => x != null).ToArray()!; }
                foreach (var x in types)
                    if (x != null && (x.Name == name || x.FullName == name)) return x;
            }
            catch { }
        }
        return null;
    }

    private static bool IsPrimary(string label)
    {
        label = label.ToLowerInvariant();
        return label.Contains("play") || label.Contains("new game") || label.Contains("continue") ||
        label.Contains("start") || label.Contains("campaign");
    }

    private static string LabelOf(Component b)
    {
        foreach (var c in b.GetComponentsInChildren<Component>())
        {
            if (c == null) continue;
            var p = c.GetType().GetProperty("text");
            if (p != null && p.PropertyType == typeof(string))
            {
                var v = p.GetValue(c) as string;
                if (!string.IsNullOrEmpty(v)) return v;
            }
        }
        return b.gameObject.name;
    }

    private static void SetLabel(GameObject go, string text)
    {
        foreach (var c in go.GetComponentsInChildren<Component>())
        {
            if (c == null) continue;
            var p = c.GetType().GetProperty("text");
            if (p != null && p.CanWrite && p.PropertyType == typeof(string))
            {
                try { p.SetValue(c, text); } catch { }
            }
        }
    }
}
