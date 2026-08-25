using MelonLoader;

namespace CrimeSceneCoop;

internal static class CoopLog
{
    public static void Info(string msg) => MelonLogger.Msg("[TwoClean] " + msg);
    public static void Warn(string msg) => MelonLogger.Warning("[TwoClean] " + msg);
    public static void Error(string msg) => MelonLogger.Error("[TwoClean] " + msg);
}
