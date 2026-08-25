using UnityEngine;

namespace CrimeSceneCoop;

/// <summary>
/// F10 panel. Also a small hint on the main menu so the Multiplayer entry is obvious.
/// </summary>
internal static class CoopOverlay
{
    public static bool Open { get; private set; }
    private static string _join = "";
    private static int _tab; // 0 steam, 1 direct
    private static Rect _win = new(40, 80, 420, 420);
    private static bool _copied;

    public static void Toggle() => Open = !Open;
    public static void Close() => Open = false;
    public static void Show() => Open = true;

    public static void Draw()
    {
        if (CoopConfig.Current.ShowMenuHint && LooksLikeMenu() && !Open)
            DrawHint();

        if (CoopSession.IsActive)
            DrawHudChip();

        if (!Open) return;

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        GUI.skin.box.fontSize = 14;
        GUI.skin.button.fontSize = 15;
        GUI.skin.label.fontSize = 14;
        GUI.skin.textField.fontSize = 16;
        _win = GUI.Window(0x7C0C, _win, (GUI.WindowFunction)DrawWindow, "TwoClean  ·  co-op");
    }

    private static void DrawWindow(int id)
    {
        GUILayout.Space(8);
        GUILayout.Label(CoopSession.Status);
        GUILayout.Space(6);

        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(_tab == 0, " Steam code ", "Button")) _tab = 0;
        if (GUILayout.Toggle(_tab == 1, " Direct LAN ", "Button")) _tab = 1;
        GUILayout.EndHorizontal();
        GUILayout.Space(8);

        if (_tab == 0)
        {
            GUILayout.Label("Private room · 2 players · no server");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host", GUILayout.Height(36)))
                CoopSession.HostSteam();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(CoopSession.RoomCodeValue) && CoopSession.IsHost && _tab == 0)
            {
                GUILayout.Label("Give this code to your friend");
                GUILayout.TextField(CoopSession.RoomCodeValue);
                if (GUILayout.Button(_copied ? "Copied" : "Copy code"))
                {
                    GUIUtility.systemCopyBuffer = CoopSession.RoomCodeValue;
                    _copied = true;
                }
            }
            GUILayout.Space(8);
            GUILayout.Label("Join with a code");
            _join = GUILayout.TextField(_join ?? "", 12);
            if (GUILayout.Button("Join", GUILayout.Height(36)))
                CoopSession.JoinSteam(_join);
        }
        else
        {
            GUILayout.Label("Same Wi-Fi, or port-forward 27040 UDP");
            if (GUILayout.Button("Host LAN", GUILayout.Height(36)))
                CoopSession.HostDirect();
            if (!string.IsNullOrEmpty(CoopSession.DirectEndpoint))
                GUILayout.TextField(CoopSession.DirectEndpoint);
            GUILayout.Space(8);
            GUILayout.Label("Join IP:port");
            _join = GUILayout.TextField(_join ?? "", 40);
            if (GUILayout.Button("Join LAN", GUILayout.Height(36)))
                CoopSession.JoinDirect(_join);
        }

        GUILayout.Space(12);
        if (CoopSession.IsActive || CoopSession.Status != "offline")
        {
            if (GUILayout.Button("Leave room"))
                CoopSession.Shutdown();
        }
        GUILayout.FlexibleSpace();
        GUILayout.Label("F10 close  ·  stains sync as you mop");
        GUI.DragWindow(new Rect(0, 0, 10000, 24));
    }

    private static void DrawHint()
    {
        var r = new Rect(16, 16, 220, 36);
        if (GUI.Button(r, "  Multiplayer    F10"))
            Open = true;
    }

    private static void DrawHudChip()
    {
        var label = CoopSession.IsHost ? "CO-OP HOST" : "CO-OP";
        GUI.Box(new Rect(16, Screen.height - 40, 140, 24), label);
    }

    private static bool LooksLikeMenu()
    {
        var n = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? "";
        n = n.ToLowerInvariant();
        return n.Contains("menu") || n.Contains("title") || n.Contains("boot") || n.Contains("main");
    }
}
