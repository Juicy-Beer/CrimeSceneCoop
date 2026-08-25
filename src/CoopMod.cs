using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

[assembly: MelonInfo(typeof(CrimeSceneCoop.CoopMod), "TwoClean", "1.0.0", "TwoClean")]
[assembly: MelonGame(null, null)]
[assembly: MelonColor(255, 200, 204, 212)]

namespace CrimeSceneCoop;

// MelonLoader entry drop CrimeSceneCoop.dll into the games Mods folder
// F10 in game for host/join stains replicate as they are mopped
public sealed class CoopMod : MelonMod
{
    public static CoopMod Instance { get; private set; } = null!;
    public static HarmonyLib.Harmony Patcher { get; private set; } = null!;

    public override void OnInitializeMelon()
    {
        Instance = this;
        Patcher = HarmonyInstance;
        CoopConfig.Load();
        SteamNative.TryAttach();
        GameProbe.Run();
        HarmonyHooks.Install(Patcher);
        SceneManager.sceneLoaded += (UnityAction<Scene, LoadSceneMode>)OnSceneLoaded;
        CoopLog.Info("TwoClean ready. F10 opens co-op. Host a private room or join with a code.");
    }

    public override void OnApplicationQuit()
    {
        SceneManager.sceneLoaded -= (UnityAction<Scene, LoadSceneMode>)OnSceneLoaded;
        CoopSession.Shutdown();
    }

    public override void OnUpdate()
    {
        SteamNative.Pump();
        CoopSession.Tick(Time.unscaledDeltaTime);
        PlayerSync.Tick(Time.unscaledDeltaTime);
        StainSync.Tick(Time.unscaledDeltaTime);
        MenuInject.Tick();

        if (Input.GetKeyDown(KeyCode.F10))
            CoopOverlay.Toggle();
        if (Input.GetKeyDown(KeyCode.Escape) && CoopOverlay.Open && !CoopSession.IsActive)
            CoopOverlay.Close();
    }

    public override void OnGUI()
    {
        CoopOverlay.Draw();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        CoopLog.Info($"Scene loaded: {scene.name}");
        StainRegistry.Rebuild(scene.name);
        PlayerSync.OnSceneLoaded();
        MenuInject.OnSceneLoaded(scene.name);
        SceneSync.OnLocalSceneLoaded(scene.name);
    }
}
