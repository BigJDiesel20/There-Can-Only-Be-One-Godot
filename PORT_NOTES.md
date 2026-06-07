# Port Notes — Unity → Godot

This project is a full port of the Unity 6 version of *There Can Only Be One* to **Godot 4.6.3 (.NET / C#)**. The game logic was reimplemented to use Godot systems where the engines differ, keeping the original architecture (event-driven per-player hub + state machines) intact.

## Engine mapping

| Unity | Godot |
|---|---|
| `ScriptableObject` | `[GlobalClass] Resource` |
| `MonoBehaviour` | `Node` / `Node3D` |
| `[SerializeField]` | `[Export]` |
| `Rigidbody` | `CharacterBody3D` |
| `Collider` / `OverlapBox` | `Area3D` / `PhysicsDirectSpaceState3D.IntersectShape` |
| Coroutines | `async` / `await ToSignal` |
| `UnityAction` | `System.Action` |
| `Time.deltaTime` | `GameTime.Delta` (set each frame) |
| Rewired input | device-aware polling (`Input.IsJoyButtonPressed(device, …)`) |
| Split-screen via `Camera.rect` | one **`SubViewport`** per player sharing the main `World3D` |
| uGUI canvases | code-built `CanvasLayer` / `Control` UIs |
| Sprite sheets | `AtlasTexture` regions (re-rendered to `ImageTexture` where alignment mattered) |

## Systems ported

Input, per-player **state machine** (Battle / Comboing / Prone / Dialog / Menu / Spectate), movement, **camera** (orbit / follow / lock-on / side-view), **attack + combos + hit detection**, stats (health / stamina / aura), **aura-drain win loop**, **teaming** (invite / join / kick / mutiny + roles), dialog system, full game flow (Splash → Menu → Lobby → CharacterSelect → PreGame → Battle → PostGame), split-screen, HUD, characters, and the **virtual-controller debug tooling**.

Characters are Unity built-in primitives (capsule body + cylinder "face") — replicated natively in `Player.tscn`, so no model export was needed.

## Engine-specific gotchas & fixes

These are Godot-specific issues found and fixed during the port — worth knowing if you extend the code:

- **Handedness:** Unity forward is `+Z`, Godot is `-Z`. Cross products, strafe/right vectors, and shoulder-offset signs were flipped accordingly.
- **Stick deadzone:** Godot returns the raw joypad axis (incl. resting drift); Rewired applied a deadzone. Re-added a rescaled radial deadzone in `PlayerInput.GetAxis`.
- **Body → player resolution:** the character is a *sibling* of its `LocalPlayerManager` (not a child as in Unity). Resolve via a `PlayerDetection` child node, not `GetComponentInParent`.
- **State machine re-entrancy:** `GameManager.ChangeState` must assign `currentState` *before* calling `OnLoad()` — `PreGame.OnLoad` calls `ChangeState("Battle")`, and the old order overwrote it back to PreGame (game silently stuck, `Battle.OnUpdate` never ran).
- **HUD scaling:** Godot has no `CanvasScaler`; HUD metrics are sized as a **fraction of each viewport cell** so they're consistent from 1 to 16 players.
- **HUD bands:** flat bars + a clamped 1–1.5px outline (a gradient gloss showed as light/dark *bands* that scaled with bar height across player counts).
- **Symbols:** the source PNGs have off-center glyphs + transparent padding; loaded with an alpha-centroid **trim** so each symbol centers in the pie hole, plus a circular shader mask so it fills the hole without spilling onto the ring.
- **Aura flames:** the sprite-sheet frames aren't registered (each flame sits in a different spot in its cell), so they "jumped" when animated. Frames are re-rendered into a common canvas (centered + bottom-aligned) at load.
- **SubViewport sizing:** `SubViewportContainer.Stretch` only assigns the viewport size on the next layout pass, but `PreGame` builds characters the same frame — so `SplitScreenRig` sets each `SubViewport.Size` explicitly up front.
- **Lock-on cursor / per-viewport visibility:** all SubViewports share one `World3D`, so per-player 3D UI (the lock-on cursor) uses a **render layer + camera cull mask** keyed to the player's color index.
- **GUI focus theft:** in-game debug panels set `FocusMode = None` on every control so the gamepad can't accidentally activate them instead of driving the player.

## Not ported (optional)

- `SymbolGlowController` (HDRP emission/bloom glow on the cursor symbol) — needs a `WorldEnvironment` glow pass.
- Heat-distortion flame shader — the flames animate fine without it.
- Unity editor tooling (thumbnail/attack-config generators) — replaced by in-code factories or not needed.
