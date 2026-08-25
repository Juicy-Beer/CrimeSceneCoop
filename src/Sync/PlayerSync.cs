using UnityEngine;

namespace CrimeSceneCoop;

internal static class PlayerSync
{
    private static Transform? _local;
    private static RemoteAvatar? _ghost;
    private static float _poseAcc;
    private static Vector3 _remotePos;
    private static float _remoteYaw;
    private static float _remotePitch;
    private static bool _hasRemote;

    public static void Reset()
    {
        if (_ghost != null)
        {
            UnityEngine.Object.Destroy(_ghost.gameObject);
            _ghost = null;
        }
        _hasRemote = false;
        _local = null;
    }

    public static void OnSceneLoaded()
    {
        _local = null;
        if (_ghost != null)
        {
            UnityEngine.Object.Destroy(_ghost.gameObject);
            _ghost = null;
        }
    }

    public static void Tick(float dt)
    {
        if (!CoopSession.IsActive) return;
        EnsureLocal();
        if (_local == null) return;

        _poseAcc += dt;
        var interval = 1f / Mathf.Max(5f, CoopConfig.Current.PoseHz);
        if (_poseAcc >= interval)
        {
            _poseAcc = 0;
            var yaw = _local.eulerAngles.y;
            var pitch = 0f;
            var cam = Camera.main;
            if (cam != null) pitch = cam.transform.eulerAngles.x;
            CoopSession.Send(
                NetProtocol.PlayerPose(CoopSession.LocalId, _local.position, yaw, pitch, 0),
                reliable: false);
        }

        if (_hasRemote)
        {
            EnsureGhost();
            _ghost?.Apply(_remotePos, _remoteYaw, _remotePitch, dt);
        }
    }

    public static void ApplyRemote(BinaryReader r)
    {
        r.ReadByte(); // id
        _remotePos = NetProtocol.ReadVec3(r);
        _remoteYaw = r.ReadSingle();
        _remotePitch = r.ReadSingle();
        r.ReadByte();
        _hasRemote = true;
    }

    public static void ApplyObject(BinaryReader r)
    {
        r.ReadUInt32();
        NetProtocol.ReadVec3(r);
        r.ReadSingle(); r.ReadSingle(); r.ReadSingle(); r.ReadSingle();
    }

    private static void EnsureLocal()
    {
        if (_local != null) return;
        var cam = Camera.main;
        if (cam == null) return;
        var t = cam.transform;
        while (t.parent != null) t = t.parent;
        _local = t;
        var cc = UnityEngine.Object.FindObjectOfType<CharacterController>();
        if (cc != null) _local = cc.transform;
    }

    private static void EnsureGhost()
    {
        if (_ghost != null) return;
        var go = new GameObject("TwoClean_RemotePlayer");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _ghost = go.AddComponent<RemoteAvatar>();
        _ghost.Build(_local);
    }
}
