# SeaAbove

**The ocean fell on your ceiling. Grab a rod and fish upward!**

SeaAbove is a cozy mixed reality fishing game for PICO headsets. It scans your real room and turns the ceiling into the surface of an upside-down sea. Cast your line up into the water, fight fish with a hand-cranked reel, collect quirky species and keep your favourites in an upside-down aquarium standing in your room.

- Unity **6000.0.85f2** · URP · PICO Unity SDK (XR, `picoxr` mode) · Android (PICO)
- Package name: `com.zhao.seaabove`
- 3D models (fish, rod, tackle, aquarium) generated with Tripo; water, UI and effects are procedural

## Features

- **A sea on your ceiling**: rippling water surface with caustics, an inverted sea floor, kelp hanging down and bubbles drifting toward the room. Fish in the ceiling sea swim belly-up.
- **Real fishing feel**: flick the rod up to cast, yank it down to set the hook, then grab the reel crank with your other hand and wind it in circles. Haptics on every bite and tug.
- **Fish that fight back**: rushes, side runs, head shakes, jumps and a last stand. Keep the line tension in range and use the drag to let line out on big runs.
- **Collection**: 8 species across 4 rarities, from the Mochi Fish to the legendary Crystal Whalelet. Rarer fish appear as your catch count and rod improve.
- **Upside-down aquarium**: a sealed lid, an open bottom and anti-gravity water. Lift a caught fish into it from below, or release it back into the ceiling sea.
- **Unlocks**: 3 rods (Bamboo, Coral, Starlight) with stronger line, faster reels and better luck, plus 4 room decorations.

## Getting started

1. Open the project with Unity **6000.0.85f2** (build target: Android).
2. Run **UpsideDownFishing → Setup Project (materials + Main scene)** once. It is safe to run again.
3. Open `Assets/UpsideDownFishing/Scenes/Main.unity` and press Play to try it with the keyboard/mouse simulator.
4. On device: **UpsideDownFishing → Build Android APK (Development)** writes `Builds/Android/SeaAbove.apk`; install it with `adb install -r`. On first launch, PICO room scanning starts if the headset has no room data yet.

## Controls

| Action | PICO controllers | Editor simulator |
|---|---|---|
| Cast | Hold right trigger, flick the rod up, release | Hold left mouse, flick mouse up, release |
| Set the hook | Yank the rod down when the bobber is pulled under | Left mouse |
| Reel in | Grip the reel crank with the left hand and wind it in circles | Hold R (Shift = faster) |
| Adjust drag | Left thumbstick up / down | Mouse wheel |
| Grab / release a landed fish | Left grip | G |
| Put a fish in the aquarium | Hold it under the open bottom and let go | G under the aquarium |
| Switch rod | Right B | B |
| Collection book | Left Y | Y |
| Rescan room | Left X | X |
| Look / move | Headset | Right mouse drag / WASD QE |

Debug keys in the simulator: **H** hooks the nearest fish, **L** lands one instantly.

## Project layout (`Assets/UpsideDownFishing/`)

| Path | Purpose |
|---|---|
| `Scripts/Core/` | `GameBootstrap` (single scene entry point, wires every system at runtime), XR input, editor simulator, mesh and material helpers, SFX |
| `Scripts/Room/` | PICO Scene Capture → room model (ceiling polygon and height, floor, furniture); furniture colliders and depth occluders |
| `Scripts/Water/` | Ceiling sea: water surface, sea volume, inverted seabed, bubbles, splashes |
| `Scripts/Fish/` | Species data, model loading, fish AI and schools |
| `Scripts/Fishing/` | Rod, casting, bite and hook-set, fish fight, reel crank, rod and tackle models |
| `Scripts/Aquarium/` | Upside-down aquarium, carrying caught fish, fish swimming in the tank |
| `Scripts/Collection/` | Collection book, coins, rod and decoration unlocks (saved in PlayerPrefs), room decorations |
| `Scripts/UI/` | Hints, catch cards, collection book, fight gauges (fish stamina and curved line-tension gauge) |
| `Shaders/` | Toon, ceiling water, water volume, underwater lighting, tank water, glass, glow, bubbles, icons, depth occluder |
| `Resources/UDF/` | Materials, icons and the Tripo models (`Models/Fish`, `Models/Aquarium`, tackle, rod) |
| `Editor/` | One-click setup, model importers, APK build |

Debug toggles on `GameBootstrap`: `forceSimulatedRoom`, `debugShowFurniture`, `resetSaveOnStart`.

## Known limitations

- Fish avoid furniture boxes from Scene Capture only; the spatial mesh is not used yet, and walls are approximated from the ceiling outline.
- Text uses legacy TextMesh with the built-in font.
- Sound effects are synthesized placeholders.
- Coins are counted but there is no shop yet.
