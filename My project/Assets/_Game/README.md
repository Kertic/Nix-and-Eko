# Nix & Eko — Character Controller & Level Building Blocks

A retro-styled 2D archery metroidvania foundation for Unity 6 (URP 2D + Input System).
Placeholder art uses the **PICO-8 palette** with a Pixel Perfect Camera for a Celeste-classic
chunky-pixel look. Swap in real tile/sprite art when ready.

## Quick start
1. Open the project in Unity 6 (`6000.0.48f1`).
2. Let scripts compile, then run **Tools ▸ Nix & Eko ▸ Build Test Level**.
   - Creates a `Ground` layer, a `PlayerConfig` asset (`Assets/_Game/Data/`), the player,
     and one of every building block — all wired up in the currently-open scene.
3. Press **Play**.

### Controls (default `InputSystem_Actions`, "Player" map)
| Action | Binding | Notes |
|--------|---------|-------|
| Move | A/D / left stick | |
| Jump | Space / South button | Hold = higher (variable height). Coyote-time + jump buffer. |
| Crouch | Ctrl/C | Crouch+Jump drops through wooden one-way platforms. |
| Shoot | Hold LMB (Attack), **drag to aim**, release to fire | Pressing LMB pins a ring at the cursor; dragging away from it picks the fire direction, snapped to **8 directions**. Until the drag clears the deadzone the aim holds its last direction, defaulting to facing. Reticle + trajectory arc show aim/charge (sticky — it holds its direction until you move well past the 45° boundary). Firing **recoils you opposite the shot**: shoot down to pogo upward, shoot sideways in the air to boost the other way. Scales with draw. |
| Wall-slide | Hold into a wall while airborne | Slows your fall. **No wall-jump** — slide only. Try the shaft on the left. |

## Architecture
```
Scripts/
  Core/StateMachine.cs        Tiny FSM (IState + StateMachine).
  Player/
    PlayerConfig.cs           ScriptableObject: all movement/combat tuning.
    PlayerInputReader.cs      Wraps the InputActionAsset (no generated wrapper needed).
    PlayerController.cs        Rigidbody, ground/wall sensing, facing, timers, owns the FSM.
    States/                    Idle, Move, Jump, Fall, WallSlide, Crouch, Hurt.
  Combat/
    Bow.cs / Arrow.cs          Charge-and-release archery. Stuck arrows are cosmetic (not standable).
    IArrowHittable.cs          Anything an arrow can trigger (switch, breakable, enemy).
  Environment/
    Health.cs, Hazard.cs, Checkpoint.cs,
    OneWayPlatform.cs, MovingPlatform.cs,
    TargetSwitch.cs, Gate.cs, BreakableWall.cs
  Util/
    SpriteFactory.cs           Procedural point-filtered placeholder sprites.
    ProceduralSprite.cs        Component that builds a sprite from params (no art assets).
    ProceduralLine.cs          Keeps the trajectory LineRenderer's material valid on reload.
    DebugHud.cs                On-screen HP/state/charge readout for playtesting.
Editor/
  TestLevelBuilder.cs          Menu command that assembles the whole test level in code.
  EditorAutoRefresh.cs         Enables auto-recompile on save (incl. during Play mode).
```

### Extending
- **Tune feel:** edit the `PlayerConfig` asset — no prefab edits needed.
- **New locomotion state:** subclass `PlayerStateBase`, add a field + `new` in
  `PlayerController.Awake`, and route transitions from the relevant states.
- **New shootable:** implement `IArrowHittable.OnArrowHit` (return `false` to consume the
  arrow, `true` to let it stick).
- **Real art:** swap `ProceduralSprite` for a `SpriteRenderer` + your sprites/animator;
  nothing else depends on the placeholder visuals.

Layers: solid terrain, platforms, gates and breakable walls live on the `Ground` layer
(the player's `groundMask`). Hazards/checkpoints/switches use triggers; stuck arrows are inert.
