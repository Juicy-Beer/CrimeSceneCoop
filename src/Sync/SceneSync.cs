using UnityEngine.SceneManagement;

namespace CrimeSceneCoop;

internal static class SceneSync
{
    public static string CurrentScene { get; private set; } = "";
    private static bool _applying;

    public static void OnLocalSceneLoaded(string name)
    {
        CurrentScene = name;
        if (_applying)
        {
            _applying = false;
            return;
        }
        if (CoopSession.IsActive && CoopSession.IsHost)
            CoopSession.Send(NetProtocol.SceneChange(name), reliable: true);
    }

    public static void ApplyRemote(string scene)
    {
        if (string.IsNullOrEmpty(scene) || scene == CurrentScene) return;
        if (CoopSession.IsHost) return;
        _applying = true;
        CoopLog.Info("Friend loaded scene " + scene + " — following.");
        try { SceneManager.LoadScene(scene); }
        catch (Exception ex)
        {
            CoopLog.Warn("Could not load scene " + scene + ": " + ex.Message);
            _applying = false;
        }
    }
}
