# TwoClean — Crime Scene Cleaner co-op

2-player private co-op for the Steam game **Crime Scene Cleaner**.
MelonLoader mod. No paid server. Host gets a code, friend types it, you both mop the same floor.

When you mop a blood stain, **your** client already cleaned it (instant). The other player receives that clean on the next packet — typically **20–80 ms** on Steam relay, faster on LAN. A 2 Hz snapshot heals anything that was missed, so both floors stay identical.

## Copy this folder into VSCode

```
~/Projects/CrimeSceneCoop/          ← open THIS folder in VSCode
  CrimeSceneCoop.csproj
  Directory.Build.props
  Directory.Build.props.user.example
  README.md
  .vscode/tasks.json
  src/
    CoopMod.cs                      entry (MelonMod)
    CoopLog.cs
    Config/CoopConfig.cs
    Net/RoomCode.cs
    Net/NetProtocol.cs
    Net/ITransport.cs
    Net/SteamNative.cs              Steam P2P + invisible lobby
    Net/SteamTransport.cs
    Net/UdpTransport.cs             LAN fallback
    Net/CoopSession.cs
    Game/GameProbe.cs               finds stain/player types at runtime
    Game/HarmonyHooks.cs            mop/clean patches
    Sync/StainRegistry.cs
    Sync/StainSync.cs               instant local + replicate
    Sync/PlayerSync.cs
    Sync/RemoteAvatar.cs
    Sync/SceneSync.cs
    UI/CoopOverlay.cs               F10 Host / Join
    UI/MenuInject.cs                main-menu Multiplayer button
```

After a Release build, copy **one file**:

```
bin/Release/CrimeSceneCoop.dll
        ↓
<game>/Mods/CrimeSceneCoop.dll
```

Game folder:

| OS | Path |
|---|---|
| Windows | `C:\Program Files (x86)\Steam\steamapps\common\Crime Scene Cleaner\` |
| Fedora / Proton | `~/.local/share/Steam/steamapps/common/Crime Scene Cleaner/` |

## 1. MelonLoader

Install [MelonLoader for Crime Scene Cleaner](https://www.nexusmods.com/crimescenecleaner/mods/11) (v0.7.1+).
Launch the game **once** so it generates `MelonLoader/Il2CppAssemblies/`. Quit.

Fedora launch option (Proton):

```
WINEDLLOVERRIDES="version=n,b" %command%
```

## 2. Point the project at the game

```
cp Directory.Build.props.user.example Directory.Build.props.user
```

Edit `GameDir` to your real path. Then in VSCode: **Terminal → Run Build Task**.

```
dotnet build -c Release
```

Needs the .NET 6/8 SDK (`sudo dnf install dotnet-sdk-8.0` on Fedora).

## 3. Play

1. Both players install the same `CrimeSceneCoop.dll` into `Mods/`.
2. Main menu: click **Multiplayer**, or press **F10**.
3. Host → **Host**. Read the 6-character code.
4. Friend → paste the code → **Join**.
5. Host starts a campaign mission. Friend is pulled into the same scene.
6. You see each other. Mopped stains disappear on both screens.

LAN-only (no Steam lobby): Direct tab, Host LAN, friend types `IP:27040`.

## If stains do not vanish for the friend

First launch writes `UserData/TwoClean/probe.json` (under the game folder).
That list is the real class names. Put matching names into `UserData/TwoClean/config.json`:

```json
{
  "ExtraStainTypeNames": ["TheExactStainClass"],
  "ExtraCleanMethodNames": ["Clean", "Mop"]
}
```

Then relaunch. TwoClean patches whatever the probe finds.

## Limits (2 players, campaign)

- Host is authoritative for world dirt. Guest sends input by playing locally; cleans replicate both ways and the snapshot agrees with the host.
- Some unique scripted events may still be single-player-only until their types show up in the probe.
- Two Steam accounts are required for Steam codes. Direct mode can test on one PC with two copies only if you can bind two game instances — normally you just use two machines.

No dedicated server. Steam's relay is free. Direct UDP is free.
