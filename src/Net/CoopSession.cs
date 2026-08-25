using UnityEngine;

namespace CrimeSceneCoop;

internal static class CoopSession
{
    public static bool IsHost { get; private set; }
    public static bool IsActive => _transport != null && _transport.IsConnected;
    public static bool SuppressHooks { get; set; }
    public static string Status { get; private set; } = "offline";
    public static string RoomCodeValue { get; private set; } = "";
    public static string DirectEndpoint { get; private set; } = "";
    public static byte LocalId { get; private set; } = 1;
    public static byte RemoteId { get; private set; } = 2;

    private static ITransport? _transport;
    private static ulong _lobby;
    private static ulong _remoteSteam;
    private static float _keepAlive;
    private static float _snapTimer;
    private static bool _welcomeSent;

    public static void HostSteam()
    {
        Shutdown();
        if (!SteamNative.TryAttach())
        {
            Status = "Steam not available — use Direct";
            return;
        }
        IsHost = true;
        LocalId = 1;
        RemoteId = 2;
        RoomCodeValue = RoomCode.Generate();
        Status = "creating room…";
        SteamNative.LobbyCreated += OnLobbyCreated;
        SteamNative.LobbyEntered += OnLobbyEntered;
        SteamNative.CreateInvisibleLobby(2);
    }

    public static void JoinSteam(string code)
    {
        Shutdown();
        if (!SteamNative.TryAttach())
        {
            Status = "Steam not available — use Direct";
            return;
        }
        IsHost = false;
        LocalId = 2;
        RemoteId = 1;
        RoomCodeValue = RoomCode.Normalize(code);
        Status = "searching…";
        SteamNative.LobbyList += OnLobbyList;
        SteamNative.LobbyEntered += OnLobbyEntered;
        SteamNative.SearchByCode(RoomCodeValue);
    }

    public static void HostDirect()
    {
        Shutdown();
        IsHost = true;
        LocalId = 1;
        RemoteId = 2;
        var port = CoopConfig.Current.DirectPort;
        DirectEndpoint = GetLocalIp() + ":" + port;
        RoomCodeValue = DirectEndpoint;
        _transport = new UdpTransport(port, null, port, host: true);
        _transport.Message += OnMessage;
        Status = "direct host " + DirectEndpoint;
        CoopLog.Info("Direct host on " + DirectEndpoint);
    }

    public static void JoinDirect(string hostPort)
    {
        Shutdown();
        if (!RoomCode.TryParseDirect(hostPort, out var host, out var port))
        {
            Status = "bad address";
            return;
        }
        IsHost = false;
        LocalId = 2;
        RemoteId = 1;
        RoomCodeValue = host + ":" + port;
        _transport = new UdpTransport(0, host, port, host: false);
        _transport.Message += OnMessage;
        Status = "direct joining " + RoomCodeValue;
        Send(NetProtocol.Hello(SteamNative.PersonaName), reliable: true);
    }

    public static void Shutdown()
    {
        SteamNative.LobbyCreated -= OnLobbyCreated;
        SteamNative.LobbyEntered -= OnLobbyEntered;
        SteamNative.LobbyList -= OnLobbyList;
        if (_lobby != 0)
        {
            try { SteamNative.LeaveLobby(_lobby); } catch { }
            _lobby = 0;
        }
        _transport?.Dispose();
        _transport = null;
        _remoteSteam = 0;
        _welcomeSent = false;
        IsHost = false;
        Status = "offline";
        RoomCodeValue = "";
        DirectEndpoint = "";
        PlayerSync.Reset();
    }

    public static void Send(byte[] data, bool reliable = true)
        => _transport?.Send(data, reliable);

    public static void SendStainCleaned(uint id, float amount)
    {
        if (!IsActive || SuppressHooks) return;
        Send(NetProtocol.StainCleaned(id, amount), reliable: true);
    }

    public static void SendStainStroke(uint id, Vector3 pos, Vector3 nrm, float radius, float strength, byte tool)
    {
        if (!IsActive || SuppressHooks) return;
        Send(NetProtocol.StainStroke(id, pos, nrm, radius, strength, tool), reliable: true);
    }

    public static void Tick(float dt)
    {
        _transport?.Pump();
        if (!IsActive) return;

        _keepAlive += dt;
        if (_keepAlive > 2f)
        {
            _keepAlive = 0;
            Send(NetProtocol.KeepAlive(), reliable: false);
        }

        if (IsHost)
        {
            _snapTimer += dt;
            if (_snapTimer >= 1f / Mathf.Max(0.5f, CoopConfig.Current.SnapshotHz))
            {
                _snapTimer = 0;
                Send(NetProtocol.WorldSnapshot(SceneSync.CurrentScene, StainRegistry.Snapshot()), reliable: true);
            }
        }
    }

    private static void OnLobbyCreated(ulong lobby)
    {
        _lobby = lobby;
        SteamNative.SetLobbyData(lobby, "tc", RoomCodeValue);
        SteamNative.SetJoinable(lobby, true);
        BindSteam(0);
        Status = "room " + RoomCodeValue;
        CoopLog.Info("Private room code: " + RoomCodeValue);
    }

    private static void OnLobbyList(int n)
    {
        SteamNative.LobbyList -= OnLobbyList;
        if (n <= 0)
        {
            Status = "code not found";
            CoopLog.Warn("No lobby for code " + RoomCodeValue);
            return;
        }
        var id = SteamNative.LobbyByIndex(0);
        Status = "joining…";
        SteamNative.JoinLobby(id);
    }

    private static void OnLobbyEntered(ulong lobby, bool ok)
    {
        if (!ok)
        {
            Status = "join failed";
            return;
        }
        _lobby = lobby;
        var members = SteamNative.LobbyMembers(lobby);
        var owner = SteamNative.LobbyOwner(lobby);
        if (IsHost)
        {
            Status = "waiting for friend (" + RoomCodeValue + ")";
            return;
        }
        _remoteSteam = owner;
        BindSteam(_remoteSteam);
        Send(NetProtocol.Hello(SteamNative.PersonaName), reliable: true);
        Status = "connected";
    }

    private static void BindSteam(ulong remote)
    {
        if (_transport is SteamTransport)
        {
            if (remote != 0) _remoteSteam = remote;
            return;
        }
        _transport?.Dispose();
        _remoteSteam = remote;
        _transport = new SteamTransport(remote);
        _transport.Message += OnMessage;
    }

    private static void OnMessage(byte[] data)
    {
        if (data.Length < 1) return;
        try
        {
            using var r = NetProtocol.Reader(data);
            var type = (Msg)r.ReadByte();
            switch (type)
            {
                case Msg.Hello:
                    r.ReadByte();
                    var name = NetProtocol.ReadString(r);
                    CoopLog.Info("Peer hello: " + name);
                    if (IsHost && !_welcomeSent)
                    {
                        if (_remoteSteam == 0)
                        {
                            var members = SteamNative.LobbyMembers(_lobby);
                            foreach (var m in members)
                                if (m != 0 && m != SteamNative.LocalSteamId) _remoteSteam = m;
                            if (_remoteSteam != 0) BindSteam(_remoteSteam);
                        }
                        _welcomeSent = true;
                        Send(NetProtocol.Welcome(2), reliable: true);
                        Send(NetProtocol.SceneChange(SceneSync.CurrentScene), reliable: true);
                        Send(NetProtocol.WorldSnapshot(SceneSync.CurrentScene, StainRegistry.Snapshot()), reliable: true);
                        Status = "in session with " + name;
                    }
                    break;
                case Msg.Welcome:
                    LocalId = r.ReadByte();
                    Status = "in session";
                    Send(NetProtocol.RequestSnapshot(), reliable: true);
                    break;
                case Msg.KeepAlive:
                    break;
                case Msg.PlayerPose:
                    PlayerSync.ApplyRemote(r);
                    break;
                case Msg.StainCleaned:
                    StainSync.ApplyRemoteClean(r.ReadUInt32(), r.ReadSingle());
                    break;
                case Msg.StainStroke:
                    StainSync.ApplyRemoteStroke(r);
                    break;
                case Msg.WorldSnapshot:
                    StainSync.ApplySnapshot(r);
                    break;
                case Msg.SceneChange:
                    SceneSync.ApplyRemote(NetProtocol.ReadString(r));
                    break;
                case Msg.ObjectPose:
                    PlayerSync.ApplyObject(r);
                    break;
                case Msg.RequestSnapshot:
                    if (IsHost)
                        Send(NetProtocol.WorldSnapshot(SceneSync.CurrentScene, StainRegistry.Snapshot()), reliable: true);
                    break;
            }
        }
        catch (Exception ex)
        {
            CoopLog.Warn("Bad packet: " + ex.Message);
        }
    }

    private static string GetLocalIp()
    {
        try
        {
            using var s = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, 0);
            s.Connect("8.8.8.8", 65530);
            if (s.LocalEndPoint is System.Net.IPEndPoint ep) return ep.Address.ToString();
        }
        catch { }
        return "127.0.0.1";
    }
}
