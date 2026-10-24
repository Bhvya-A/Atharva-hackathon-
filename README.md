# The Other Side — Gameplay Framework

First-person horror foundation for Unity 6.3 URP: movement, interaction, HUD, and Reality / The Void dimension switching. Ghost AI, relics, and puzzles are not implemented yet.

## Setup

1. Open the project in **Unity 6000.3**.
2. Open `Assets/Scenes/SampleScene.unity`.
3. Press **Play**. `GameBootstrap` creates the player, floor, HUD, interactable, and dimension test objects at runtime. SampleScene is not permanently modified.

Project Input Actions (`Assets/InputSystem_Actions.inputactions`) are used by name. No generated C# wrapper is required.

## Controls (Player map)

| Action | Keyboard / Mouse | Gamepad |
| --- | --- | --- |
| Move | WASD / arrows | Left stick |
| Look | Mouse | Right stick |
| Sprint | Left Shift | Left stick press |
| Jump | Space | South button (A / Cross) |
| Interact | E | North button (Y / Triangle) |
| Switch dimension | **Q** | **Left Shoulder** |
| Pause | Esc (`UI/Cancel`) | Cancel / East |

`SwitchDimension` was added to the existing action asset. Q and Left Shoulder were unused (Crouch is C / East, Interact is E / North). If that action is missing, `GameInput` falls back to the same Q / Left Shoulder bindings and logs a warning.

## Architecture

- `GameManager` — Playing / Paused / GameOver / Won, lives, relic count.
- `GameInput` — resolves actions by name from `InputSystem_Actions`.
- `FirstPersonController` — CharacterController move, look, sprint, jump.
- `PlayerInteractor` + `IInteractable` — raycast interact + UI prompt.
- `DimensionManager` — current dimension, cooldown, UI flash, object swap. Player pose is not moved.
- `DimensionPresence` — object/group belongs to Reality, The Void, or Both.
- `HUDController` — lives, relics, prompt, dimension + cooldown, transition overlay.

### Ghost stun hook (no GhostAI yet)

Subscribe from a future ghost component:

```csharp
DimensionManager.Instance.GhostStunRequested += duration => { /* stun for duration seconds */ };
```

Also available: `DimensionChanged`, `IsGhostStunned`, `GhostStunRemaining`. Default stun is **5 seconds** (Inspector). Switching is blocked while paused, game over, won, transitioning, or on cooldown (default **2s**).

## What you should see in Play mode

- HUD: **Lives 5**, **Relics 0/4**, **Reality Ready**.
- Warm cube on the left (Reality only), purple cube on the right (Void only), interactable cube ahead (Both).
- Press **Q**: colour overlay, left cube disappears, right cube appears, HUD reads **The Void** then a cooldown. Press Q again after **Ready**.
- Look at the middle cube and press **E** to inspect.
- **Esc** pauses; dimension switching does not work while paused.
