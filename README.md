# SeaHunter: Periscope Up

> 🚧 **Under construction** – this project is actively being developed. Features, balancing and this README may be out of date.

A retro submarine arcade game built with **C#** and **Avalonia UI** (.NET). You command a submarine through three different stages: hunting ships from below, fighting destroyers through the periscope, and limping home through a minefield.

This is a private continuation of an earlier school project, *SeaHunter*.

---

## The three stages

### 1. Side view – the hunt
Move the submarine under the surface and torpedo the ships above you. Patrol boats and battleships drop depth charges. Every 1000 points takes you to a new level with more ships and more bombs.

### 2. Periscope Up
When you lose your last life, the boat is **damaged** but not sunk. Raise the periscope and fight on.

- Torpedoes are earned in stage 1: 3 per level played
- Three torpedo tubes that reload one at a time
- Torpedoes home in on ships they pass close to
- Destroyers come straight at you – watch the red dots on the radar
- Sink destroyers to earn sonar pings for stage 3

### 3. Limping Home – the minefield
The destroyer forces a **crash dive**. Your hull is badly damaged, and a minefield lies between you and the harbor.

- Minesweeper-style: numbers show how many mines are around each cell
- Use sonar pings to scan ahead
- The submarine creeps forward on its own – think fast
- Three hull hits and you are sunk

### Patrols
Make it home and you start a new patrol:

| | Patrol 1 | Patrol 2 | Patrol 3 | Later |
|---|---|---|---|---|
| Start level | 1 | 5 | 10 | +5 each |
| Torpedoes (stage 1) | 5 | 6 | 7 | +1, max 12 |
| Bubble shields | – | 3 | 3 | 3 |
| Destroyers at once | 1 | 2 | 3 | max 4 |
| Way home | 40 cells | 60 cells | 80 cells | max 120 |

---

## Controls

| Stage | Key | Action |
|---|---|---|
| Side view | A D / ← → / W S / ↑ ↓ | Move |
| | Space | Fire torpedo |
| | Q | Bubble shield (earned by making it home) |
| Periscope | A D / ← → | Turn the periscope (hold to turn faster) |
| | Q / T | Swing towards the closest destroyer |
| | Space | Fire |
| Minefield | W S / ↑ ↓ | Change depth |
| | D / → | Move forward |
| | Space | Sonar ping |
| | F | Flag the cell ahead |

The start screen also has a **TEST** mode to jump straight to any patrol and stage. Test runs are not saved to the high score list.

---

## Build and run

Requires the [.NET SDK](https://dotnet.microsoft.com/download).

```
dotnet run
```

Build a single Windows `.exe`:

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

---

## Project structure

```
SeaHunter-PeriscopeUp/
├── Assets/              Sprites and backgrounds
├── Game/
│   ├── Gameworld.cs     Stage 1: side view
│   ├── PeriscopeWorld.cs Stage 2: periscope
│   ├── MinefieldWorld.cs Stage 3: minefield
│   └── ...              Ships, torpedoes, input, high scores
├── GameCanvas.cs        Game loop and all drawing
└── MainWindow.axaml     Menus and screens between the stages
```

---

## Status

🚧 Work in progress. Planned / possible next steps:

- Balancing of all three stages
- Sound effects
- More enemy types

Graphics are pixel art made with the help of AI image tools.
