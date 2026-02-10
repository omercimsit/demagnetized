# Demagnetized

> First-person puzzle game where you record yourself, then cooperate with your past clones to solve puzzles.

![banner](Screenshots/banner.png)

Built solo in Unity HDRP. Inspired by SUPERHOT's time mechanics — but instead of slowing time, you record your actions and replay them as clones. Combined with real portals, kinetic grab mechanics, and a VHS visual style.

**Engine:** Unity 2022.3 LTS (HDRP) | **Language:** C#

---

## Systems

### Clone Recording & Playback

Record player movement, rotation, and bone poses frame-by-frame, then instantiate clones that replay those actions. Uses Catmull-Rom interpolation for smooth playback, keyframe compression to reduce memory, and object pooling for zero-allocation spawning.

![clone system](Screenshots/clone-system.gif)

### Portal System

Portals with correct camera-space matrix transforms — not just the usual oblique-projection approximation. Handles HDRP render callbacks, oblique near clip planes, and render texture management. Separate compatibility layer for preserving character animation state during teleport.

![portals](Screenshots/portal.gif)

### Procedural Foot IK

Custom IK bone creation tools, foot-ground placement via sphere casting, full-body IK with pole targets, and a delta-rotation approach to prevent 180-degree foot flips during solver passes. Editor tools for bone remapping across different skeleton rigs.

![foot ik](Screenshots/footik.gif)

### Kinetic Tension (Grab & Hold)

Physics-based chain grab mechanic. Detect nearby interactables, grab with IK-driven hands, record grab state into the clone system for cooperative playback. Time-freeze visual feedback on activation.

![tension](Screenshots/tension-grab.gif)

### NVIDIA DLSS Integration

Runtime DLSS mode switching for HDRP — including DLAA, Quality, Balanced, Performance, and Ultra Performance. Modifies HDRP asset internals and camera frame settings via reflection since Unity doesn't expose a public API for this.

### Cinematic Flood Sequence

Procedural Gerstner wave mesh that chases the player through corridors. Integrates with HDRP Water system (WaterSurface + WaterDeformer) and VFX Graph for splash particles.

![flood](Screenshots/flood.gif)

### Menu System

Full main menu and pause menu drawn with IMGUI. VHS tracking lines, scanlines, tape-insert animations, live background scene rendering. Modular pause menu with separated state, rendering, and effect modules.

![menu](Screenshots/menu.png)

### Runtime Optimization

- **FrameBudgetManager** — distributes heavy work across frames within a ms budget
- **AdvancedCullingSystem** — per-layer distance culling for HDRP
- **ShaderWarmup** — precompiles shaders over N frames during loading to prevent hitches
- **GCManager** — controls garbage collection timing to avoid mid-frame spikes
- **ObjectPool\<T\>** — generic pooling with auto-expand and pre-warm

---

## Architecture

```
Scripts/
├── Core/            ServiceLocator, ObjectPool, EventBus, Settings
├── CloneSystem/     Recording, playback, UI, footstep sync
├── Portal/          Matrix portal, HDRP integration, animation compat
├── Graphics/        DLSS manager, quality presets
├── Animation/       IK tuning, leg separation
├── Interaction/     Kinetic tension, chain grab, time freeze
├── FPS/             Footsteps, head bob, camera stabilization
├── Menu/            Main menu, pause menu (modular)
├── Cinematic/       Flood waves, opening sequence, shadow chase
├── Optimization/    Frame budget, culling, shader warmup, GC
├── Streaming/       Seamless scene loading, area streaming
├── Puzzle/          Doors, sensors, timers, wheels
├── UI/              VHS effects, loading screen
└── Editor/          Material fixer, hierarchy optimizer, IK tools
```

Core services are registered through a `ServiceLocator` pattern. Game-wide events use a static `GameEvents` bus with typed Action delegates. Scene transitions use additive loading with overlap to hide load times.

---

## Not included in this repo

- Unity project files, HDRP settings, scenes, and prefabs
- Third-party packages (KINEMATION, Cinemachine, etc.)
- Art assets, audio, materials, shaders

This repository contains only the C# gameplay and systems code I wrote.
