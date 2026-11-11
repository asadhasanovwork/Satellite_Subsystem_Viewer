# Academy – Procedural Satellite Prototype

Generates satellite hardware 3D models from code and assembles complete satellites from JSON files.
No imported 3D models are used. Three reference satellites are included, one per orbit class:

| Orbit | File | Satellite |
| --- | --- | --- |
| LEO | `cubesat_3u_prototype.json` | Academy-3U: 3U CubeSat with deployable wings and UHF antenna |
| MEO | `meo_navsat_prototype.json` | NavStar-M: navigation satellite with L-band helix array, laser retroreflector, 2-panel wings |
| GEO | `geo_comsat_prototype.json` | GeoComm-1: communications satellite with 4-panel accordion wings, two 2.3 m offset reflectors and feeds, apogee engine |

## Try it

1. Open the project in Unity 6 and wait for scripts to compile.
2. Menu **Academy → Prototype →** one of
   *Create Procedural CubeSat Scene (LEO)*, *Create Navigation Satellite Scene (MEO)*,
   *Create Communications Satellite Scene (GEO)*.
   Each creates and saves a scene in `Assets/Academy/Prototype/Scenes/` with a camera, sun light
   and the builder. The satellite appears immediately in the Scene view.
3. Press **Play**: everything starts stowed for launch, then deploys (solar wings unfold,
   reflectors swing out, antennas extend); reaction wheels spin.
   Drag to orbit, scroll to zoom. The panel in the top-left switches between the three satellites
   (gallery buttons) and has Deploy / Explode sliders, *Replay deployment* and *Reload JSON*.
   The camera rescales automatically: a CubeSat is 0.34 m, the GEO satellite spans about 18 m.

On the CubeSat the -Z face is left without a solar panel so you can see inside
(electronics stack, reaction wheels, battery).

## Change the satellite

Edit `Assets/StreamingAssets/Academy/cubesat_3u_prototype.json`, then press *Reload JSON*
(Play mode) or right-click the builder component → *Rebuild From JSON* (Edit mode).

Each part has `type`, `position`, `forward` (the part's local +Z / outward direction) and
`up` (local +Y), plus type-specific fields:

| type | key fields |
| --- | --- |
| `cubesat_frame` | `units`, `unitSize`, `unitHeight`, `railWidth`, `pcbStack` |
| `solar_panel` | `width`, `height`, `cellRows`, `cellColumns`, `deployable`, `deployAngle`, `cellsBothSides` |
| `reaction_wheel` | `radius`, `wheelHeight`, `spinRpm`, `openHousing` |
| `antenna` | `style` = `turnstile` (`elements`, `length`, `plateSize`) or `patch` (`patchSize`) |
| `thruster` | `throatRadius`, `nozzleExitRadius`, `nozzleLength` |
| `box` | `size`, `material` |
| `bus` | `size`, `facePX`…`faceNZ` (`osr_radiator`, `gold_foil`, `black_mli`, `white_paint`, …), `edgeBeams` |
| `solar_wing` | `panelCount`, `width`, `height` (panel length), `yokeLength`, `yokeStowedAngle`, `sadaLength`, `sadaRadius`, `cellRows`, `cellColumns` |
| `reflector` | `diameter`, `focalLength`, `offsetHeight`, `vertexOffset`, `stowedAngle`, `deployAngle` (offset paraboloid; focus = vertexOffset + (0,0,focalLength)) |
| `feed_horn` | `radius` (aperture), `length` |
| `antenna` (more styles) | `omni` (`length`), `helix` / `helix_array` (`helixRadius`, `helixTurns`, `length`, `elements`, `arrayRadius`) |
| `sensor` | `style` = `earth_sensor` / `star_tracker` / `sun_sensor`, `size`, `length` (baffle) |
| `retroreflector` | `width`, `height`, `cellColumns`, `cellRows` |

Every part also accepts `scale` (uniform size multiplier, e.g. a CubeSat thruster ×22 becomes a GEO apogee engine)
and `deployStart` / `deployEnd` (0–1) to sequence deployments, e.g. wings first, reflectors after.

The GEO reflector geometry was solved so each dish's focus sits exactly on its feed horn's aperture,
and each dish folds against its east/west face without passing through the bus.

All values are metres and generic teaching values, not taken from any real product.

## Code

- `Runtime/ProceduralMeshBuilder.cs` – boxes, cylinders, lathed profiles, tubes, offset paraboloids, crinkled MLI, transform stack, multi-material mesh baking
- `Runtime/PartGenerators.cs` – CubeSat-scale generators, deployable joints (hinge / tape extension), part dispatch
- `Runtime/BusAndWingGenerators.cs` – panel-built bus (radiators, MLI) and accordion-folding solar wings
- `Runtime/RfGenerators.cs` – offset reflectors, feed horns, omni / helix antennas, retroreflector, attitude sensors
- `Runtime/PartMaterialLibrary.cs` – runtime URP Lit materials (aluminium, solar cell, PCB, gold, steel, ...)
- `Runtime/ProceduralSatelliteBuilder.cs` – loads JSON, builds, explode / deploy pose, on-screen controls
- `Runtime/PrototypeOrbitCamera.cs` – orbit camera (Input System)
- `Editor/ProceduralSatelliteMenu.cs` – the *Academy* menu

Generated objects use `HideFlags.DontSave`: they are never written into the scene, only rebuilt from JSON.
