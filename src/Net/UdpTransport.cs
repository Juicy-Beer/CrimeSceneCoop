using System.Net;
using System.Net.Sockets;

namespace CrimeSceneCoop;

/// <summary>
/// Direct UDP for LAN / port-forward. Same message blobs as Steam P2P.
/// A tiny ack layer makes stain events reliable without extra libraries.
/// </summary>
internal sealed class UdpTransport : ITransport
{
    private readonly UdpClient _udp;
    private readonly IPEndPoint _remote;
    private readonly bool _host;
    private IPEndPoint? _learned;
    private uint _seq;
    private readonly HashSet<uint> _seen = new();
    private readonly List<Pending> _pending = new();
    public bool IsConnected { get; private set; }
    public string Status => _learned != null || !_host ? $"udp {_learned ?? _remote}" : "udp listening";
    public event Action<byte[]>? Message;

    private struct Pending
    {
        public uint Seq;
        public byte[] Payload;
        public long NextMs;
        public int Tries;
    }

    public UdpTransport(int bindPort, string? remoteHost, int remotePort, bool host)
    {
        _host = host;
        _udp = new UdpClient(bindPort);
        _udp.Client.Blocking = false;
        _remote = remoteHost != null
            ? new IPEndPoint(IPAddress.Parse(remoteHost), remotePort)
            : new IPEndPoint(IPAddress.Any, remotePort);
        if (!host) IsConnected = true;
    }

    public void Send(byte[] data, bool reliable)
    {
        var dest = Dest();
        if (dest == null) return;
        if (!reliable)
        {
            WritePacket(0, 0, data, dest);
            return;
        }
        var seq = ++_seq;
        _pending.Add(new Pending
        {
            Seq = seq,
            Payload = data,
            NextMs = Now() + 80,
            Tries = 0,
        });
        WritePacket(1, seq, data, dest);
    }

    public void Pump()
    {
        try
        {
            while (_udp.Available > 0)
            {
                IPEndPoint from = new(IPAddress.Any, 0);
                var buf = _udp.Receive(ref from);
                if (buf.Length < 6) continue;
                _learned ??= from;
                IsConnected = true;
                var kind = buf[0];
                var seq = BitConverter.ToUInt32(buf, 1);
                if (kind == 2)
                {
                    _pending.RemoveAll(p => p.Seq == seq);
                    continue;
                }
                var payload = new byte[buf.Length - 5];
                Buffer.BlockCopy(buf, 5, payload, 0, payload.Length);
                if (kind == 1)
                {
                    WritePacket(2, seq, Array.Empty<byte>(), from);
                    if (!_seen.Add(seq)) continue;
                    if (_seen.Count > 2048) _seen.Clear();
                }
                Message?.Invoke(payload);
            }
        }
        catch (SocketException) { }

        var now = Now();
        var dest = Dest();
        if (dest == null) return;
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var p = _pending[i];
            if (p.Tries > 20) { _pending.RemoveAt(i); continue; }
            if (now < p.NextMs) continue;
            WritePacket(1, p.Seq, p.Payload, dest);
            p.Tries++;
            p.NextMs = now + 80 * (p.Tries + 1);
            _pending[i] = p;
        }
    }

    private IPEndPoint? Dest()
    {
        var dest = _learned ?? (_host ? null : _remote);
        if (dest == null || Equals(dest.Address, IPAddress.Any)) return null;
        return dest;
    }

    private void WritePacket(byte kind, uint seq, byte[] payload, IPEndPoint dest)
    {
        var pkt = new byte[5 + payload.Length];
        pkt[0] = kind;
        BitConverter.GetBytes(seq).CopyTo(pkt, 1);
        if (payload.Length > 0) Buffer.BlockCopy(payload, 0, pkt, 5, payload.Length);
        try { _udp.Send(pkt, pkt.Length, dest); } catch { }
    }

    private static long Now() => Environment.TickCount64;

    public void Dispose()
    {
        try { _udp.Dispose(); } catch { }
        IsConnected = false;
    }
}
