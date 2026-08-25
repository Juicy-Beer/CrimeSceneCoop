using System.Runtime.InteropServices;

namespace CrimeSceneCoop;

/// <summary>
/// Attaches to the game's already-loaded steam_api64.dll. Does not shut Steam down.
/// Uses ManualDispatch so we don't fight the game's callback pump.
/// </summary>
internal static class SteamNative
{
    public static bool Available { get; private set; }
    public static ulong LocalSteamId { get; private set; }
    public static string PersonaName { get; private set; } = "Player";

    private static IntPtr _lib;
    private static IntPtr _mm;
    private static IntPtr _net;
    private static IntPtr _user;
    private static int _pipe;

    private static CreateLobbyDel? _createLobby;
    private static JoinLobbyDel? _joinLobby;
    private static LeaveLobbyDel? _leaveLobby;
    private static SetLobbyDataDel? _setLobbyData;
    private static SetLobbyTypeDel? _setLobbyType;
    private static SetLobbyJoinableDel? _setJoinable;
    private static RequestLobbyListDel? _requestList;
    private static AddStringFilterDel? _addFilter;
    private static GetLobbyByIndexDel? _lobbyByIndex;
    private static GetNumLobbyMembersDel? _numMembers;
    private static GetLobbyMemberByIndexDel? _memberByIndex;
    private static GetLobbyOwnerDel? _lobbyOwner;
    private static SendP2PDel? _sendP2P;
    private static ReadP2PDel? _readP2P;
    private static IsP2PAvailDel? _p2pAvail;
    private static AcceptP2PDel? _acceptP2P;
    private static AllowRelayDel? _allowRelay;
    private static GetSteamIdDel? _getSteamId;
    private static GetPersonaDel? _getPersona;
    private static RunFrameDel? _runFrame;
    private static GetNextCbDel? _getNextCb;
    private static FreeCbDel? _freeCb;
    private static GetCallResultDel? _getCallResult;

    public static event Action<ulong>? LobbyCreated;
    public static event Action<ulong, bool>? LobbyEntered;
    public static event Action<int>? LobbyList;
    public static event Action<ulong>? P2PRequest;

    public static bool TryAttach()
    {
        if (Available) return true;
        try
        {
            if (!NativeLibrary.TryLoad("steam_api64", out _lib) &&
                !NativeLibrary.TryLoad("steam_api", out _lib))
            {
                CoopLog.Warn("steam_api64.dll not loaded — Steam rooms disabled. Direct IP still works.");
                return false;
            }

            var init = Get<InitDel>("SteamAPI_Init");
            var getPipe = Get<GetPipeDel>("SteamAPI_GetHSteamPipe");
            var getUser = Get<GetUserDel>("SteamAPI_GetHSteamUser");
            var mdInit = Get<Action>("SteamAPI_ManualDispatch_Init");

            _pipe = getPipe?.Invoke() ?? 0;
            if (_pipe == 0)
            {
                init?.Invoke();
                _pipe = getPipe?.Invoke() ?? 0;
            }
            if (_pipe == 0)
            {
                CoopLog.Warn("Steam pipe is 0 — game may not have initialized Steam yet.");
                return false;
            }

            mdInit?.Invoke();
            _runFrame = Get<RunFrameDel>("SteamAPI_ManualDispatch_RunFrame");
            _getNextCb = Get<GetNextCbDel>("SteamAPI_ManualDispatch_GetNextCallback");
            _freeCb = Get<FreeCbDel>("SteamAPI_ManualDispatch_FreeLastCallback");
            _getCallResult = Get<GetCallResultDel>("SteamAPI_ManualDispatch_GetAPICallResult");

            _mm = GetIface(
                "SteamAPI_SteamMatchmaking_v009",
                "SteamAPI_SteamMatchmaking_v010",
                "SteamAPI_SteamMatchmaking_v011");
            _net = GetIface(
                "SteamAPI_SteamNetworking_v006",
                "SteamAPI_SteamNetworking_v005");
            _user = GetIface(
                "SteamAPI_SteamUser_v023",
                "SteamAPI_SteamUser_v021",
                "SteamAPI_SteamUser_v022");
            var friends = GetIface(
                "SteamAPI_SteamFriends_v017",
                "SteamAPI_SteamFriends_v018");

            if (_mm == IntPtr.Zero || _net == IntPtr.Zero)
            {
                CoopLog.Warn("Steam matchmaking/networking interfaces missing.");
                return false;
            }

            _createLobby = Get<CreateLobbyDel>("SteamAPI_ISteamMatchmaking_CreateLobby");
            _joinLobby = Get<JoinLobbyDel>("SteamAPI_ISteamMatchmaking_JoinLobby");
            _leaveLobby = Get<LeaveLobbyDel>("SteamAPI_ISteamMatchmaking_LeaveLobby");
            _setLobbyData = Get<SetLobbyDataDel>("SteamAPI_ISteamMatchmaking_SetLobbyData");
            _setLobbyType = Get<SetLobbyTypeDel>("SteamAPI_ISteamMatchmaking_SetLobbyType");
            _setJoinable = Get<SetLobbyJoinableDel>("SteamAPI_ISteamMatchmaking_SetLobbyJoinable");
            _requestList = Get<RequestLobbyListDel>("SteamAPI_ISteamMatchmaking_RequestLobbyList");
            _addFilter = Get<AddStringFilterDel>("SteamAPI_ISteamMatchmaking_AddRequestLobbyListStringFilter");
            _lobbyByIndex = Get<GetLobbyByIndexDel>("SteamAPI_ISteamMatchmaking_GetLobbyByIndex");
            _numMembers = Get<GetNumLobbyMembersDel>("SteamAPI_ISteamMatchmaking_GetNumLobbyMembers");
            _memberByIndex = Get<GetLobbyMemberByIndexDel>("SteamAPI_ISteamMatchmaking_GetLobbyMemberByIndex");
            _lobbyOwner = Get<GetLobbyOwnerDel>("SteamAPI_ISteamMatchmaking_GetLobbyOwner");

            _sendP2P = Get<SendP2PDel>("SteamAPI_ISteamNetworking_SendP2PPacket");
            _readP2P = Get<ReadP2PDel>("SteamAPI_ISteamNetworking_ReadP2PPacket");
            _p2pAvail = Get<IsP2PAvailDel>("SteamAPI_ISteamNetworking_IsP2PPacketAvailable");
            _acceptP2P = Get<AcceptP2PDel>("SteamAPI_ISteamNetworking_AcceptP2PSessionWithUser");
            _allowRelay = Get<AllowRelayDel>("SteamAPI_ISteamNetworking_AllowP2PPacketRelay");

            _getSteamId = Get<GetSteamIdDel>("SteamAPI_ISteamUser_GetSteamID");
            _getPersona = Get<GetPersonaDel>("SteamAPI_ISteamFriends_GetPersonaName");

            _allowRelay?.Invoke(_net, true);

            if (_user != IntPtr.Zero && _getSteamId != null)
                LocalSteamId = _getSteamId(_user);
            if (friends != IntPtr.Zero && _getPersona != null)
            {
                var n = _getPersona(friends);
                if (n != IntPtr.Zero) PersonaName = Marshal.PtrToStringAnsi(n) ?? "Player";
            }

            Available = true;
            CoopLog.Info($"Steam attached as {PersonaName} ({LocalSteamId}).");
            return true;
        }
        catch (Exception ex)
        {
            CoopLog.Warn("Steam attach failed: " + ex.Message);
            return false;
        }
    }

    public static void Pump()
    {
        if (!Available || _runFrame == null || _getNextCb == null || _freeCb == null) return;
        try
        {
            _runFrame(_pipe);
            while (_getNextCb(_pipe, out var msg))
            {
                try { HandleCallback(msg); }
                finally { _freeCb(_pipe); }
            }
        }
        catch (Exception ex)
        {
            CoopLog.Warn("Steam pump: " + ex.Message);
        }
    }

    private static void HandleCallback(CallbackMsg msg)
    {
        // P2PSessionRequest_t = k_iSteamNetworkingCallbacks (1200) + 2
        if (msg.iCallback == 1202 && msg.cubParam >= 8)
        {
            var id = (ulong)Marshal.ReadInt64(msg.pubParam);
            _acceptP2P?.Invoke(_net, id);
            P2PRequest?.Invoke(id);
            return;
        }

        // LobbyCreated_t = 513
        if (msg.iCallback == 513 && msg.cubParam >= 12)
        {
            var result = Marshal.ReadInt32(msg.pubParam);
            var lobby = (ulong)Marshal.ReadInt64(msg.pubParam, 4);
            if (result == 1 /* OK */) LobbyCreated?.Invoke(lobby);
            else CoopLog.Warn("Lobby create failed, EResult=" + result);
            return;
        }

        // LobbyEnter_t = 504
        if (msg.iCallback == 504 && msg.cubParam >= 16)
        {
            var lobby = (ulong)Marshal.ReadInt64(msg.pubParam);
            var response = Marshal.ReadInt32(msg.pubParam, 16);
            LobbyEntered?.Invoke(lobby, response == 1);
            return;
        }

        // LobbyMatchList_t = 510
        if (msg.iCallback == 510 && msg.cubParam >= 4)
        {
            var n = Marshal.ReadInt32(msg.pubParam);
            LobbyList?.Invoke(n);
        }
    }

    public static void CreateInvisibleLobby(int maxMembers = 2)
        => _createLobby?.Invoke(_mm, 3 /* Invisible */, maxMembers);

    public static void JoinLobby(ulong id) => _joinLobby?.Invoke(_mm, id);

    public static void LeaveLobby(ulong id) => _leaveLobby?.Invoke(_mm, id);

    public static void SetLobbyData(ulong lobby, string key, string value)
        => _setLobbyData?.Invoke(_mm, lobby, key, value);

    public static void SetJoinable(ulong lobby, bool joinable)
        => _setJoinable?.Invoke(_mm, lobby, joinable);

    public static void SearchByCode(string code)
    {
        _addFilter?.Invoke(_mm, "tc", code, 0 /* Equal */);
        _requestList?.Invoke(_mm);
    }

    public static ulong LobbyByIndex(int i) => _lobbyByIndex?.Invoke(_mm, i) ?? 0;

    public static ulong LobbyOwner(ulong lobby) => _lobbyOwner?.Invoke(_mm, lobby) ?? 0;

    public static List<ulong> LobbyMembers(ulong lobby)
    {
        var list = new List<ulong>();
        var n = _numMembers?.Invoke(_mm, lobby) ?? 0;
        for (var i = 0; i < n; i++)
            list.Add(_memberByIndex?.Invoke(_mm, lobby, i) ?? 0);
        return list;
    }

    public static bool SendP2P(ulong remote, byte[] data, bool reliable)
        => _sendP2P?.Invoke(_net, remote, data, (uint)data.Length, reliable ? 2 : 1, 0) ?? false;

    public static bool TryReadP2P(byte[] buffer, out uint size, out ulong from)
    {
        size = 0; from = 0;
        uint avail = 0;
        if (_p2pAvail == null || !_p2pAvail(_net, ref avail, 0) || avail == 0) return false;
        return _readP2P?.Invoke(_net, buffer, (uint)buffer.Length, out size, out from, 0) ?? false;
    }

    private static IntPtr GetIface(params string[] names)
    {
        foreach (var n in names)
        {
            if (NativeLibrary.TryGetExport(_lib, n, out var p))
            {
                var fn = Marshal.GetDelegateForFunctionPointer<GetIfaceDel>(p);
                var iface = fn();
                if (iface != IntPtr.Zero) return iface;
            }
        }
        return IntPtr.Zero;
    }

    private static T? Get<T>(string name) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(_lib, name, out var p)) return null;
        return Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool InitDel();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetPipeDel();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetUserDel();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetIfaceDel();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void RunFrameDel(int pipe);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool GetNextCbDel(int pipe, out CallbackMsg msg);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FreeCbDel(int pipe);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool GetCallResultDel(int pipe, ulong call, IntPtr cub, int cubCallback, int iCallbackExpected, out bool failed);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong CreateLobbyDel(IntPtr self, int type, int max);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong JoinLobbyDel(IntPtr self, ulong lobby);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void LeaveLobbyDel(IntPtr self, ulong lobby);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool SetLobbyDataDel(IntPtr self, ulong lobby, [MarshalAs(UnmanagedType.LPStr)] string k, [MarshalAs(UnmanagedType.LPStr)] string v);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool SetLobbyTypeDel(IntPtr self, ulong lobby, int type);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool SetLobbyJoinableDel(IntPtr self, ulong lobby, bool j);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong RequestLobbyListDel(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AddStringFilterDel(IntPtr self, [MarshalAs(UnmanagedType.LPStr)] string k, [MarshalAs(UnmanagedType.LPStr)] string v, int cmp);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong GetLobbyByIndexDel(IntPtr self, int i);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetNumLobbyMembersDel(IntPtr self, ulong lobby);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong GetLobbyMemberByIndexDel(IntPtr self, ulong lobby, int i);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong GetLobbyOwnerDel(IntPtr self, ulong lobby);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool SendP2PDel(IntPtr self, ulong remote, byte[] data, uint cub, int sendType, int channel);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool ReadP2PDel(IntPtr self, byte[] dest, uint cub, out uint size, out ulong from, int channel);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool IsP2PAvailDel(IntPtr self, ref uint size, int channel);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool AcceptP2PDel(IntPtr self, ulong remote);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool AllowRelayDel(IntPtr self, bool allow);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong GetSteamIdDel(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetPersonaDel(IntPtr self);

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct CallbackMsg
    {
        public int hSteamUser;
        public int iCallback;
        public IntPtr pubParam;
        public int cubParam;
    }
}
