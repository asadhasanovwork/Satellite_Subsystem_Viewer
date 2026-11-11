# Academy – Mission Operations

Fly the satellite you built: a ground-station console in the spirit of NASA's open-source NOS3
(Operational Simulator for Space Systems), rebuilt for teaching inside Unity. No scene asset and no setup:
the scene is generated in code.

## How to open

- Main menu → **Mission Operations Training** (lessons and free flight), or
- Satellite Builder → *Review & launch* → **Fly it: Mission Operations** (your own design).

## What is inside (the 8 ideas taken from NOS3)

| # | NOS3 idea | Here |
|---|-----------|------|
| 1 | One "truth" engine (42) for orbit and environment | `Core/SpaceEnvironment.cs`, `KeplerOrbit.cs`, `OpsMath.cs`: Kepler orbit + J2 drift, Sun position, eclipse (cylindrical shadow), tilted-dipole magnetic field, Earth rotation, rigid-body attitude with wheel momentum |
| 2 | Component = flight app + ground tables + simulator | `Core/SimComponent.cs` and the component files: each Builder slot becomes a model with telemetry, commands and faults (`operations_models.json` maps slots to models) |
| 3 | Simple hardware models | Battery, solar arrays (cosine law, tracking wings, drive), heaters, release mechanism, reaction wheels (saturation, friction, failure), magnetorquers, thrusters + tank (rocket equation, burns), sensors (noise, bias, field of view), radios, computer (boot, safe mode, watchdog), storage, payloads (imager, clocks with warm-up, comms channels), one-node thermal |
| 4 | Attitude modes | `Core/AttitudeControl.cs`: PASSIVE, DETUMBLE (B-dot), SUNSAFE (with Sun search), NADIR, INERTIAL; attitude determination from star tracker / Earth + Sun sensors / magnetometer + Sun sensor / gyro propagation; momentum dumping with magnetorquers or thrusters |
| 5 | Fault back doors | `FaultInjector` (`SIM FAULT …`) + the **Instructor** panel |
| 6 | Limit checker + stored commands | `Core/OnboardSoftware.cs`: rules (`OBSW RULE id ON/OFF`), sequencer (`OBSW SCHEDULE_IN s cmd`), safe mode |
| 7 | Mission config + scenarios | `StreamingAssets/Academy/Operations/operations_models.json` (orbit, stations, thermal, attitude, rules per satellite type) and `Scenarios/*.json` (goals, situation, steps with checks, failure condition) |
| 8 | Ground station | `Unity/OperationsUI.cs`: passes (commands only reach the satellite in contact), telemetry (last downlinked values), command cards and console, stored commands, rules, events, scenario guide, ground-track map with station coverage, time warp |

## The console

- **Header**: satellite, UTC clock and mission time, time control (Space pauses, keys 1-6 set the warp),
  contact lamp with loss-of-signal / next-pass countdown, master alarm (flashes on any alarm; click to acknowledge).
- **Timeline ribbon**: the next few orbits at a glance: ground passes (teal), eclipses (violet), stored commands
  (amber diamonds), now cursor.
- **Left**: eight annunciator lamps (click one to filter telemetry; red lamps blink until clicked), vitals with
  trend lines (battery, net power, rotation, temperature), searchable telemetry.
- **Centre**: lesson card with step dots and hold bar, toasts for warnings and alarms, and the instrument dock (H):
  ground track with station coverage, payload's-eye view (Earth disk and Sun as seen from the payload axis),
  animated power flow.
- **Right**: command line with suggestions and history (Up/Down), command cards by subsystem; Plan (stored
  commands with "at next pass" / "before next eclipse" helpers); Rules; Log with filters.
- **Mission board** (Lessons): orbit-rings illustration, lessons per orbit, free flight; every lesson opens with a
  briefing (time paused) and ends with a debrief (time, commands, lowest battery, alarms, next lesson).
- **Instructor drawer** (I): fail any unit, tumble, battery, wheel momentum, training aids.

Palette: ink-blue surfaces; sun-amber = power and sunlight, ground-teal = contact and nominal, night-violet =
eclipse, orange = caution, red = alarm. Live numbers use a monospace OS font so digits do not jump.

## Lessons

LEO: first contact & commissioning, low power, stuck thruster tumble, wheel momentum dumping, imaging & downlink.
MEO: navigation payload into service (atomic-clock warm-up), eclipse season with a weak battery.
GEO: station keeping (east-west drift), equinox eclipse power, reaction-wheel failure.

Every lesson has been played through by a scripted test with the correct actions (all succeed), and the
energy lessons were checked to fail without them.

## Command syntax

`TARGET COMMAND [args]` — target is a component id from the Builder (e.g. `eps.battery`, `pl.main`,
`adcs.wheels`), `ADCS` or `OBSW`. Examples: `ADCS MODE NADIR`, `pl.main ON`, `str.hdrm DEPLOY`,
`prop.rea BURN RETROGRADE 0.073`, `OBSW SCHEDULE_IN 600 pl.main OFF`.
Instructor: `SIM FAULT <id> <fault> [value]`, `SIM CLEAR ALL`, `SIM TUMBLE 5`, `SIM SET_SOC 30`, `SIM WHEEL_MOMENTUM 75`.

## Notes

- All numbers are generic teaching values from public knowledge; ground stations are public city locations.
- The Earth is a generic globe with a latitude/longitude grid (no texture assets needed), lit by the real Sun direction.
- `Core/` has no UnityEngine dependency, so the physics can be tested outside Unity.
- Credit: architecture ideas from NASA NOS3 (NASA Open Source Agreement 1.3), https://github.com/nasa/nos3 — no NOS3 code is included.
