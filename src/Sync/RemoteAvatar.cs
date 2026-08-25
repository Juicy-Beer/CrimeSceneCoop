using System;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace CrimeSceneCoop;

/// <summary>
/// Visible stand-in for the other player. Prefers a stripped clone of the local
/// body; falls back to a capsule so you always see your friend.
/// </summary>
internal sealed class RemoteAvatar : MonoBehaviour
{
    private Vector3 _target;
    private float _yaw;
    private Transform? _visual;

    public void Build(Transform? localRoot)
    {
        if (localRoot != null)
        {
            try
            {
                var clone = Instantiate(localRoot.gameObject, transform);
                clone.name = "Visual";
                StripLocalOnly(clone);
                _visual = clone.transform;
                _visual.localPosition = Vector3.zero;
                _visual.localRotation = Quaternion.identity;
            }
            catch (Exception ex)
            {
                CoopLog.Warn("Clone avatar failed, using capsule: " + ex.Message);
            }
        }
        if (_visual == null) BuildCapsule();
    }

    public void Apply(Vector3 pos, float yaw, float pitch, float dt)
    {
        _target = pos;
        _yaw = yaw;
        transform.position = Vector3.Lerp(transform.position, _target, 1f - Mathf.Exp(-12f * dt));
        var q = Quaternion.Euler(0f, _yaw, 0f);
        transform.rotation = Quaternion.Slerp(transform.rotation, q, 1f - Mathf.Exp(-10f * dt));
        _ = pitch;
    }

    private void BuildCapsule()
    {
        var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        cap.name = "Visual";
        cap.transform.SetParent(transform, false);
        cap.transform.localScale = new Vector3(0.45f, 0.9f, 0.45f);
        cap.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        var col = cap.GetComponent<Collider>();
        if (col != null) col.enabled = false;
        var rend = cap.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material = new Material(Shader.Find("Standard") ?? rend.material.shader);
            rend.material.color = new Color(0.78f, 0.8f, 0.84f, 1f);
        }
        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.transform.SetParent(cap.transform, false);
        head.transform.localScale = new Vector3(0.7f, 0.35f, 0.7f);
        head.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        var hc = head.GetComponent<Collider>();
        if (hc != null) hc.enabled = false;
        _visual = cap.transform;
    }

    private static void StripLocalOnly(GameObject go)
    {
        foreach (var cam in go.GetComponentsInChildren<Camera>(true))
            cam.enabled = false;

        foreach (var comp in go.GetComponentsInChildren<Component>(true))
        {
            if (comp != null && comp.GetIl2CppType().Name == "AudioListener")
            {
                if (comp is Behaviour b) b.enabled = false;
            }
        }

        foreach (var cc in go.GetComponentsInChildren<CharacterController>(true))
            cc.enabled = false;

        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
        }

        foreach (var b in go.GetComponentsInChildren<Behaviour>(true))
        {
            if (b == null) continue;
            var n = b.GetType().Name;
            if (n.IndexOf("Input", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Look", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Mouse", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Audio", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Listener", StringComparison.OrdinalIgnoreCase) >= 0)
                b.enabled = false;
        }
    }
}
