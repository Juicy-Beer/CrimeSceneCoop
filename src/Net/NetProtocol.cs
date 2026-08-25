using System.Text;
using UnityEngine;

namespace CrimeSceneCoop;

internal enum Msg : byte
{
    Hello = 1,
    Welcome = 2,
    KeepAlive = 3,
    PlayerPose = 4,
    StainCleaned = 5,
    StainStroke = 6,
    WorldSnapshot = 7,
    SceneChange = 8,
    ObjectPose = 9,
    RequestSnapshot = 10,
}

internal static class NetProtocol
{
    public const byte Version = 1;

    public static byte[] Hello(string name)
    {
        using var w = Writer();
        w.Write((byte)Msg.Hello);
        w.Write(Version);
        WriteString(w, name);
        return w.ToArray();
    }

    public static byte[] Welcome(byte peerId)
    {
        using var w = Writer();
        w.Write((byte)Msg.Welcome);
        w.Write(peerId);
        return w.ToArray();
    }

    public static byte[] KeepAlive() => new[] { (byte)Msg.KeepAlive };

    public static byte[] PlayerPose(byte id, Vector3 p, float yaw, float pitch, byte tool)
    {
        using var w = Writer();
        w.Write((byte)Msg.PlayerPose);
        w.Write(id);
        WriteVec3(w, p);
        w.Write(yaw);
        w.Write(pitch);
        w.Write(tool);
        return w.ToArray();
    }

    public static byte[] StainCleaned(uint id, float amount)
    {
        using var w = Writer();
        w.Write((byte)Msg.StainCleaned);
        w.Write(id);
        w.Write(amount);
        return w.ToArray();
    }

    public static byte[] StainStroke(uint id, Vector3 pos, Vector3 nrm, float radius, float strength, byte tool)
    {
        using var w = Writer();
        w.Write((byte)Msg.StainStroke);
        w.Write(id);
        WriteVec3(w, pos);
        WriteVec3(w, nrm);
        w.Write(radius);
        w.Write(strength);
        w.Write(tool);
        return w.ToArray();
    }

    public static byte[] WorldSnapshot(string scene, IReadOnlyList<(uint id, byte amount)> stains)
    {
        using var w = Writer();
        w.Write((byte)Msg.WorldSnapshot);
        WriteString(w, scene);
        w.Write((ushort)stains.Count);
        foreach (var (id, amount) in stains)
        {
            w.Write(id);
            w.Write(amount);
        }
        return w.ToArray();
    }

    public static byte[] SceneChange(string scene)
    {
        using var w = Writer();
        w.Write((byte)Msg.SceneChange);
        WriteString(w, scene);
        return w.ToArray();
    }

    public static byte[] ObjectPose(uint id, Vector3 p, Quaternion q)
    {
        using var w = Writer();
        w.Write((byte)Msg.ObjectPose);
        w.Write(id);
        WriteVec3(w, p);
        w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w);
        return w.ToArray();
    }

    public static byte[] RequestSnapshot() => new[] { (byte)Msg.RequestSnapshot };

    public static BinaryReader Reader(byte[] data) => new(new MemoryStream(data, writable: false), Encoding.UTF8, leaveOpen: false);

    public static void WriteVec3(BinaryWriter w, Vector3 v)
    {
        w.Write(v.x); w.Write(v.y); w.Write(v.z);
    }

    public static Vector3 ReadVec3(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    public static void WriteString(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s ?? "");
        w.Write((ushort)bytes.Length);
        w.Write(bytes);
    }

    public static string ReadString(BinaryReader r)
    {
        var n = r.ReadUInt16();
        return Encoding.UTF8.GetString(r.ReadBytes(n));
    }

    private static BinaryWriter Writer() => new(new MemoryStream(64), Encoding.UTF8, leaveOpen: false);
}

internal static class BinaryWriterExt
{
    public static byte[] ToArray(this BinaryWriter w)
    {
        w.Flush();
        return ((MemoryStream)w.BaseStream).ToArray();
    }
}
