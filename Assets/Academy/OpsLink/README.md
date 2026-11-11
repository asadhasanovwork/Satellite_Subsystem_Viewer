# Academy – OpsLink (Mission Operations in OpenC3 COSMOS)

Main menu → **Mission Operations Training** flies the satellite you built in the Builder inside
OpenC3 COSMOS, full screen, as if it were a real spacecraft. **← Academy** (bottom left) returns to the menu.

## What happens when you press the button

1. **Satellite** – the last complete Builder design (`SatelliteDesigns/last_built.design.json`, written
   automatically on the Review page and by *Save design*). No design → "Open the Builder".
2. **Docker** – `docker info`; if the engine is down, Docker Desktop is started and awaited (3 min).
3. **OpenC3** – if `http://localhost:2900` does not answer: `docker compose up -d` in the cosmos-project
   folder (first start downloads several GB), then waits for the API; sets the password on a fresh install.
4. **Simulator** – the Operations scene is generated and flies the design in the background
   (its Unity console is hidden).
5. **Plugin** – the command/telemetry dictionary is derived from the running simulator, a plugin is written
   and installed into OpenC3 (skipped when nothing changed). OpenC3 then opens full screen, logged in.

Errors show on the start-up screen with *Try again*, *Use the Unity console* (offline fallback) or *Open the Builder*.

## Settings

`Application.persistentDataPath/OpsLink/settings.json`

| key | default | |
|---|---|---|
| `openc3Url` | `http://localhost:2900` | OpenC3 web/API |
| `composeDir` | auto | folder with `compose.yaml`; auto = `<project>/OpenC3 Simulation/OpenC3 Simulation/cosmos-project`, `<exe>/OpenC3/cosmos-project` or `<exe>/cosmos-project` |
| `password` | `academy-test` | OpenC3 Core password (set on first start, used for auto-login) |
| `linkPort` | `7779` | TCP port the simulator listens on (OpenC3 connects via `host.docker.internal`) |

## Inside OpenC3

Target `ACADEMY_<DESIGN NAME>`:

- **Commands** – one per component command (`ADCS_MODE`, `PL_MAIN_ON`, `OBSW_SCHEDULE_IN`, …), enums as
  states, numbers and text as parameters; `RAW_LINE` sends any console line. Ground commands only reach the
  satellite during a pass (as in real operations); the answer is in packet `CMD_ACK`.
- **Instructor** – `SIM_SET_WARP` (1–1000x), `SIM_PAUSE`, `SIM_RESUME`, `SIM_LINE` (e.g. `FAULT adcs.wheels FAIL 1`).
- **Telemetry** – one packet per subsystem with units and limits, timestamped with *simulation* time,
  refreshed when the ground receives a downlink; `SIM_STATUS` (contact, next AOS/LOS, warp), `EVENT`.
- **Screens** – Telemetry Viewer screens per packet plus `MISSION_OVERVIEW`.

## Code

| file | |
|---|---|
| `Runtime/OpsLinkFlow.cs` | button flow + start-up screen |
| `Runtime/OpenC3Launcher.cs` | Docker / compose / API readiness, password |
| `Runtime/OpsLinkSession.cs` | dictionary → plugin → install (hash-skipped) → link |
| `Runtime/Oc3Dictionary.cs` | cmd/tlm dictionary from `SatelliteSim` |
| `Runtime/Oc3PluginWriter.cs` | plugin.txt, gemspec, cmd/tlm, screens |
| `Runtime/Oc3Installer.cs` | builds and loads the gem inside the cmd-tlm-api container |
| `Runtime/SpacecraftLink.cs` | TCP spacecraft endpoint (length-prefixed frames, big endian) |
| `Runtime/OpsBrowserScreen.cs` | full-screen UnityWebBrowser (CEF) + auto-login |
| `Runtime/Oc3Api.cs`, `ShellProcess.cs`, `OpsLinkSettings.cs` | helpers |

Notes: do not edit scripts while in Play mode (a domain reload resets the simulator). UnityWebBrowser needs
the Mono scripting backend (IL2CPP is not supported).
