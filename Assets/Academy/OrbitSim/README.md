# Orbit Simulation (Academy bridge)

Main menu → **Satellite Orbit Simulation** opens the orbital-control simulator
(`Assets/ThirdParty/OrbitalControl`, MIT, see its `LICENSE.txt`) with the satellite last built in the Builder.

## What comes from the Builder
- **Orbit**: Review & launch → *Orbit* (altitude + inclination; GEO: inclination + slot longitude), saved in
  `DesignSave.orbit` (`DesignOrbit`). Mission Operations / OpenC3 free flight use the same orbit.
- **Mass, propellant, engine**: the same `SatelliteSim` Mission Operations builds (`MissionSetup.Create`):
  dry mass, tank propellant (finite), main engine = thruster set with the largest total thrust (thrust × count, Isp).
  No engine → zero thrust.
- **3D model**: `BuilderSatelliteView` replaces the simulator's placeholder (scaled to the simulator's
  exaggerated icon size); drag area from the model's bounds.

## Manoeuvre plan → Mission Operations / OpenC3
1. In the Orbit Simulation add manoeuvre nodes, set them and finalize them.
2. Press **Send plan to Mission Ops** → `persistentDataPath/OrbitPlans/<design>.plan.json`.
3. Start Mission Operations Training with the same satellite: the OpenC3 plugin gets the Script Runner procedure
   `targets/<TARGET>/procedures/maneuver_plan.rb`.
4. Run it in Script Runner during contact. It reads `SIM_STATUS MET`, finds the next pass of the planned point of the
   orbit (same circular orbit, period from the plan) and stores time-tagged commands on board:
   `OBSW SCHEDULE <met-30> <engine> ON` and `OBSW SCHEDULE <met> <engine> BURN <DIR> <m/s>`.

The simulator's Unity frame swaps ECI y/z (a mirror), so its *Normal* is the physical anti-normal; the plan stores
physical directions (`OrbitSimRuntime.DirectionFor`).

## Module changes (marked "Academy")
- `OcsInput` (Compat): legacy `Input` calls → Input System.
- `NativePhysics`: preloads PhysicsPlugin + MinGW DLLs from the module folder (editor) / Plugins (build).
- `BodyService.SkipSceneSatellites`, `SimulationBootstrap.Current`, `ManeuverNodeManager.GetManualPlan()`,
  `FrameUtils` made public.
