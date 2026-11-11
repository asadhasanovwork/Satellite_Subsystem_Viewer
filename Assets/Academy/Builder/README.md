# Academy – Satellite Builder

Build a satellite step by step: choose its type (which fixes the orbit), then fill every subsystem's
hardware slots from a catalogue. Each piece of hardware is generated procedurally in 3D the moment it
is chosen, in Play mode.

## Set up

Nothing to do. Open `Test_Demo`, press Play, then **Satellite Builder → Start building**.

- When the editor loads (and again before Play), `SatelliteBuilder.unity` is created automatically if it is
  missing, in the background, and it and `Test_Demo` are added to Build Settings.
- In Play mode the builder opens from Build Settings, or in the editor straight from the scene asset, or,
  if neither exists (e.g. a build without the scene), the same scene is generated in code
  (`Runtime/SatelliteBuilderSceneFactory.cs`).
- **Academy → Builder → Rebuild Satellite Builder Scene** regenerates the scene; **Open Satellite Builder Scene** opens it.

## Flow

1. **Choose your satellite type**: Academy-3U (LEO CubeSat), NavStar-M (MEO navigation), GeoComm-1 (GEO communications).
2. **Build each subsystem** (left list): Structure → Payload → Power → Computer → Communications → Attitude → Thermal → Propulsion.
   Each hardware card shows MANDATORY / CONDITIONAL / OPTIONAL, why the hardware exists, typical quantity,
   2–3 variants and, for some, sliders. "Fill required slots with recommended hardware" completes a subsystem quickly.
3. **Conditional hardware** switches on from your choices (e.g. the CubeSat camera needs reaction wheels,
   a star tracker and S-band; GEO electric orbit raising needs star trackers and power-processing units;
   a Ku-band payload needs a beacon).
4. **Review & launch**: missing items, mass, power use vs solar generation. Launch unlocks when nothing
   required is missing and the power margin is positive; it plays the stowed → deployed sequence.
   **Save design** writes `*.design.json` (selections) and `*.assembly.json` (all generated parts) to
   `Application.persistentDataPath/SatelliteDesigns/`.

Bottom bar: live mass / power / solar / margin, *Show inside* (hides the bus skin to reveal internal units),
*Explode*, *Stow for launch / Deploy*.

## Data

`Assets/StreamingAssets/Academy/Builder/`

- `builder_index.json` – list of templates
- `leo_cubesat_template.json`, `meo_navsat_template.json`, `geo_comsat_template.json`

Template → subsystems → slots (`requirement`: mandatory / conditional / optional, `conditions`: any-of
`{slot, variants}`) → variants (`massKg`, `powerW`, `powerGenW`, `parts` = PartSpecs with fixed mount points,
`parameters` = sliders that write PartSpec fields and adjust budgets linearly from their default).

Hardware lists follow the "Satellite Hardware Requirements – LEO, MEO, GEO" document. All masses, powers
and dimensions are generic teaching values from public knowledge, not from any specific satellite program.

## Code

- `Runtime/BuilderTemplateData.cs` – template / slot / variant / parameter data classes
- `Runtime/SatelliteDesign.cs` – selections, mandatory/conditional rules, budgets, save/load, export to assembly
- `Runtime/BuilderSatelliteView.cs` – per-slot procedural generation, deploy / explode / show-inside, highlight
- `Runtime/SatelliteBuilderUI.cs` – UI Toolkit screens (type selection, subsystem workspace, review)
- `Runtime/SatelliteBuilderSceneFactory.cs` – builds the scene content in code, opens the builder / main menu
- `Editor/SatelliteBuilderSceneMenu.cs` – automatic scene creation + Build Settings, rebuild/open menu
