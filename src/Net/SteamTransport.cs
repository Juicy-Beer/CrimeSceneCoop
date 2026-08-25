namespace CrimeSceneCoop;

internal sealed class SteamTransport : ITransport
{
    private ulong _remote;
    private readonly byte[] _buf = new byte[64 * 1024];
    public bool IsConnected { get; private set; }
    public string Status => _remote == 0 ? "steam waiting" : "steam " + _remote;
    public event Action<byte[]>? Message;
    public ulong Remote => _remote;

    public SteamTransport(ulong remote)
    {
        _remote = remote;
        IsConnected = remote != 0;
        SteamNative.P2PRequest += OnRequest;
    }

    private void OnRequest(ulong id)
    {
        if (_remote == 0) _remote = id;
        if (id == _remote) IsConnected = true;
    }

    public void Send(byte[] data, bool reliable)
    {
        if (_remote == 0) return;
        if (!SteamNative.SendP2P(_remote, data, reliable))
            CoopLog.Warn("Steam P2P send failed");
    }

    public void Pump()
    {
        while (SteamNative.TryReadP2P(_buf, out var size, out var from))
        {
            if (from == 0) continue;
            if (_remote == 0) _remote = from;
            if (from != _remote) continue;
            IsConnected = true;
            var copy = new byte[size];
            Buffer.BlockCopy(_buf, 0, copy, 0, (int)size);
            Message?.Invoke(copy);
        }
    }

    public void Dispose()
    {
        SteamNative.P2PRequest -= OnRequest;
        IsConnected = false;
    }
}
