# Project Specification: *Ana 3al Sellem!*

> **Purpose:** This document serves as the single source of truth for **Antigravity Agent** and the developers to implement, tune, and ship a 3D third-person physics-comedy game within a **5-day Game Jam** window.

---

## 1. Project Overview
* **Game Jam Theme:** **HIDDEN**
* **Genre:** 3D Third-Person Physics-Comedy / Time-Attack Scavenger Hunt
* **Core Pitch:** You overslept in your pajamas. Your ride is idling outside, honking, and leaving in 60–90 seconds. You must tear through your messy 5-room house—shoving and yeeting pillows, yanking open drawers, dodging slippery puddles and Legos, and wrestling with physics clutter—to uncover your **hidden** clothes and keys before sprinting out the front door.
* **Target Session Length:** 90 seconds default per run (configurable on `GameManager`; highly replayable via randomized hiding spots).
* **Timeline:** 5 Days.

### 1.1 Technical Stack & Imported Assets (Unity URP)
* **Engine:** Unity 6 (`6000.3.9f1`, 3D Universal Render Pipeline / URP)
* **Input System:** Unity New Input System (`UnityEngine.InputSystem`, reusing `Assets/InputSystem_Actions.inputactions`) — do NOT use legacy `Input.GetAxis` or `Input.GetKeyDown`.
* **UI Framework:** uGUI (Canvas + TextMeshPro) via `Phase1DebugHUD` plus runtime-generated `GameUI` (checklist, phone toasts, title card, rank breakdown) and `PauseMenuUI` (`Esc` pause menu, volume/sensitivity settings, controls card & hint strip).
* **Audio Architecture:** Procedural Web/Synth SFX & chiptune music loop synthesized in code (`Sfx.cs` + `AudioHub.cs` + `GameAudio.cs` + `ImpactSound.cs`) — no external audio files required.
* **Art & Post-Processing Pipeline:** URP PostFX (`Assets/_Project/Art/PostFX/SeptemberMorning.asset` + runtime panic vignette), 5 switchable color schemes via `PaletteBuilder`, and `Tidy Chaos/` Editor builders (`HouseBuilder`, `PlayerBuilder`, `GameSystemsBuilder`, `PaletteBuilder`, `PostFXBuilder`).
* **Workspace Rules:**
  * Put all new scripts, prefabs, scenes, editor tools, and ScriptableObjects inside `Assets/_Project/`.
  * **Do NOT modify, rename, or move** files inside third-party asset folders.
* **Available Art Packs:**
  * **Synty POLYGON Sampler Pack** (`Assets/SyntyStudios/`):
    * `Assets/SyntyStudios/PolygonPrototype/Prefabs/Buildings/Polygon/` & `Assets/SyntyStudios/PolygonPrototype/Prefabs/Props/` (props such as vases and bats)
    * `Assets/SyntyStudios/PolygonCity/Prefabs/Vehicles/` & `Props/` (outside idling `RideCar` vehicle + urban props/clutter)
    * `Assets/SyntyStudios/PolygonKnights/Prefabs/`, `Assets/SyntyStudios/PolygonAdventure/Prefabs/`, `Assets/SyntyStudios/PolygonStarter/Prefabs/`, and `Assets/SyntyStudios/PolygonSamples/` (kitchenware, laundry baskets/sacks, comical clutter & props)
  * **Kenney Furniture Kit** (`Assets/kenney-furniture-kit-interior-asset-collection/`): Use `Assets/kenney-furniture-kit-interior-asset-collection/Models/FBX format/` (140 `.fbx` interior walls, floors, furniture, articulated drawers/doors, and clutter props).
* **Physics Rule for Imported Props:** Any furniture prop or clutter made grabbable/throwable (`PhysicsClutter`) must use a `BoxCollider` or a **Convex** `MeshCollider` paired with a non-kinematic `Rigidbody` (never a non-convex `MeshCollider` on a dynamic `Rigidbody`).

---

## 2. Theme Integration: "HIDDEN"
Every core mechanic reinforces the theme:
1. **Physically Hidden:** Required items are never sitting out in plain sight. They are tucked *under* physics-enabled clutter (`UnderClutter`: pillows, laundry piles, towels, boxes), *inside* articulated furniture (`InsideDrawer`: drawers, cabinets, dressers), on high shelves (`HighShelf`), or tucked on the floor (`FloorHidden`).
2. **Hidden in Plain Sight (Decoys):** Mismatched, dirty, or ridiculous clothing/accessory items (*Soggy Sock, Holey Sock, Clown Pants, Inside-Out Shirt, Toy Keys*) spawn in `ObviousDecoySpot`s or leftover anchors to bait panicked players.
3. **Hidden Until Interacted:** Some hiding spots require multi-step rummaging (e.g., open cabinet door or drawer $\rightarrow$ shove/yeet clutter aside $\rightarrow$ equip hidden item).
4. **Dynamically Hidden:** Optional mid-run pet hazard (`PetAI`) that steals an exposed sock and hides it in another room.

---

## 3. Core Gameplay Loop

1. **Wake-Up / Start State:** `RunIntro` + `GameUI` hold the run in `Menu` with the `"ANA 3AL SELLEM!"` title card (*"90 Seconds to Find Your Clothes Before the Ride Leaves!"*) and looping alarm SFX; any key or click wakes the player and starts the run. Subsequent restarts skip straight into `Playing`.
2. **Objective Reveal:** `GameUI` displays a top-left checklist of the 5 required slots (*Left Foot, Right Foot, Legs, Torso, Accessory*) and live Dignity Score.
3. **Search & Rummage:** Player sprints, skids, bonks, crouches, and hops through the 5-room house, grabbing and yeeting 60+ physics clutter props, opening drawers/doors, and avoiding puddles/Legos.
4. **Equip / Compromise:**
   * Finding a **Matching Item** checks it off cleanly (`+20` Dignity) and visibly dresses the character via `OutfitVisuals`.
   * Finding a **Decoy/Trap Item** equips it if the slot is empty (`+10` Dignity + funny tag, visibly shown on the character—including clown pants!), or pockets it with a toast if you already wear the matching item.
5. **Escalation:** `TimerEscalationManager` + `RideCar` escalate audio/visual cues at `40s`, `25s`, and `12s`.
6. **Exit / Win Condition:** Player interacts with `FrontDoor_Exit` (`ExitDoor`, `E` tap) before time hits $t = 0$.
   * **Note:** The player *can* exit at any time—even pantsless or wearing a wet sock—triggering a rank (`S`–`D`) and funny outfit tags!
7. **Lose Condition:** Timer reaches $t = 0$ before exiting $\rightarrow$ `RideCar` peels away outside with tire screech + smoke puffs (`F`-Rank: *"Left in Pajamas"*).

---

## 4. Core Systems & Technical Specifications

### 4.1 Third-Person Goofy Physics Character (`PlayerController`, `ThirdPersonCamera`, `GoofyBody`, `OutfitVisuals`)
* **Physics Movement (`PlayerController`):** `Rigidbody` (`60 kg`, `ContinuousDynamic`, `Interpolate`, `PM_PlayerZeroFriction` `CapsuleCollider`) driven toward camera-relative velocity with finite acceleration—creating momentum, big skids when reversing, physical clutter shoving, coyote/buffered `Space` jump, and Hold/Toggle `C` / `Left Ctrl` crouch.
* **Wall Bonks & Knockdowns:** Sprinting into static walls above `bonkSpeed` knocks the character onto their back/face (`Knockdown`), dropping any held item and shaking the camera.
* **Camera (`ThirdPersonCamera`):** Over-the-shoulder orbit rig that detaches at runtime, performs `SphereCast` wall collision (ignoring dynamic clutter), hides the character mesh when zoomed closer than `0.65m`, applies sprint FOV boost + panic FOV/shake, enables URP PostFX + SMAA, and saves mouse sensitivity in `PlayerPrefs`.
* **Procedural Puppet (`GoofyBody` & `OutfitVisuals`):** Spring-driven pajama bean rig that leans into acceleration, waddles while walking, flails arms while panic-sprinting, squashes on landing, blinks, faceplants on knockdowns, and updates slippers/legs/torso/hip keys as items are collected.

### 4.2 Physics Interaction & "Yeet" System (`InteractionManager` & `HoverHighlighter`)
Uses third-person reach (`rayStartsAtPlayerDepth`) with center-screen `RaycastNonAlloc` (`2.5m`), `0.06m` `SphereCast` assist for small props, and automatic nearest-child door/drawer fallback (`childFurnitureRadius = 0.6m`) when aiming at cabinet bodies. `HoverHighlighter` pulses a `MaterialPropertyBlock` tint on the aimed target.

| Object Type | Interaction Input | Behavior |
| :--- | :--- | :--- |
| **Clutter Props** (`PhysicsClutter`) | `LMB` (Hold) / `RMB` (Click) | **Hold `LMB`** to carry at chest height (`carryOrigin`) with wall-clearance `SphereCast`. **Click `RMB`** (or release with flick) to **Yeet/Throw** toward the crosshair with impulse + tumble spin. |
| **Wearable / Objective Items** (`WearableItem`) | `E` or `LMB` (Tap) | Immediately equips/collects the item, plays procedural pop/squelch/wah SFX, updates `OutfitManager`, `OutfitVisuals`, and Checklist. |
| **Articulated Furniture** (`OpenableFurniture`) | `E` (Tap) | Smoothly slides drawers (`OpenMode.Slide`) or rotates hinged doors (`OpenMode.Hinge`) with procedural drawer/door SFX. |
| **Front Door (Exit)** (`ExitDoor`) | `E` (Tap) | Triggers immediate End-of-Run evaluation (`GameManager.TriggerWin()`). |

### 4.3 Randomized Hiding & Spawn System (`SpawnManager` & `HidingSpot`)
* **Spawn Anchors (`HidingSpot`):** 44 transform anchors placed across all 5 rooms (`Bedroom`: 13, `Bathroom`: 9, `LivingRoom`: 8, `Kitchen`: 8, `Hallway`: 6), categorized as `UnderClutter` (11), `InsideDrawer` (14, parented inside moving drawers/doors), `HighShelf` (7), `FloorHidden` (4), and `ObviousDecoySpot` (8).
* **Spawning Algorithm (On Run Start):**
  1. Shuffle and place the 5 required `ItemData` definitions into non-decoy `HidingSpot`s spread across at least `minRoomsForRequired` (3) rooms.
  2. Spawn `decoyCount` (default 4) trap `ItemData` items into `ObviousDecoySpot` or remaining anchors.
  3. Build procedural primitive visuals (`ItemVisuals`) with a kinematic `Rigidbody` + `BoxCollider` (`>= 0.08m` min bounds) so items ride inside drawers cleanly and never clip through floors.
  4. For `UnderClutter` anchors with no physics clutter overhead, `SpawnManager` drops a random cover clutter prefab (pillows, laundry piles, towels, newspaper) directly on top.

### 4.4 Item & Dignity Scoring System (`OutfitManager`)
Each wearable slot (`LeftFoot`, `RightFoot`, `Legs`, `Torso`, `Accessory`) tracks its equipped `ItemData`:
* `Empty`: `0` points (adds missing funny tag such as *"Barefoot Bandit"*, *"Pantsless Wonder"*, *"Pajama Top Chic"*, *"Locked Out"*)
* `Matching`: `+20` points (Full Dignity)
* `Trap` (Mismatched / Dirty / Wet): `+10` points + funny tag (*"Squishy Foot"*, *"Toe Out"*, *"Circus Commuter"*, *"Seams Legit"*, *"Toddler Chauffeur"*)

**Final Score Formula:**
$$\text{Total Score} = \text{DignityScore} + \lfloor t_{\text{remaining}} \times 2 \rfloor$$

**End-Screen Ranks:**
* **S-Rank** (100% Dignity + $\ge 20\text{s}$ remaining): *"Model Citizen"*
* **A-Rank** (100% Dignity + $< 20\text{s}$ remaining): *"Dressed for Success"*
* **B-Rank** (All 5 slots filled, with 1+ traps): *"Fashionably Chaotic"*
* **C-Rank** (Missing 1–2 slots): *"Forgot [Item], Caught the Ride"*
* **D-Rank** (Missing 3+ slots): *"Basically Still in Pajamas"*
* **F-Rank** (Missed Ride / $t = 0$): *"Left in Pajamas"*

### 4.5 Escalation & Ride Timer System (`TimerEscalationManager`, `RideCar`, `GameManager`)
Default round duration is `90s` (`GameManager.roundDurationSeconds`, configurable down to `60s`):
* **Start:** `RideCar` idles outside with 3D spatial engine audio; `GameAudio` starts procedural chiptune loop; initial phone toast: *"Outside. Leaving in 90s."*
* **$t \le 40\text{s}$:** Single polite car honk + phone toast: *"Yo I'm outside."*
* **$t \le 25\text{s}$:** Double impatient honk + music pitches up (`1.15x`) + phone toast: *"Seriously, I'm double-parked!"*
* **$t \le 12\text{s}$:** Rapid panic honks + revving + `1.3x` music pitch + pulsing runtime `PanicVolume` (red vignette + chromatic aberration) + heartbeat SFX + camera shake/FOV squeeze + phone toast: *"LEAVING IN 10 SECONDS!!"*
* **$t = 0\text{s}$:** Tire screech + `RideCar` wheelie peel-out with smoke puffs $\rightarrow$ Game Over trigger.

### 4.6 Chaos Hazards (`SlipperyHazard` & `PetAI`)
* **Implemented — Slippery Hazards (`SlipperyHazard`):**
  * `Hazard_Puddle` in the Bathroom and `Hazard_Legos` in the Hallway trip players sprinting at $\ge 5.0\text{ m/s}$, knocking them down (`Knockdown`), sliding them forward on momentum, briefly slowing recovery, shaking the camera, and playing a slide-whistle slip SFX.
* **Implemented — The Sock-Stealing Pet (`PetAI`):**
  * `SpawnManager` spawns a `ChaosCat` at runtime (`enableSockStealingPet`). After a `12s` grace period it roams the house, grabs an uncollected sock (`22s` cooldown) and bolts to another room until chased down and interacted with.

---

## 5. Data Schemas (`ItemData` ScriptableObjects)
Implemented in `Assets/_Project/Scripts/Items/ItemData.cs` with 10 `.asset` definitions stored in `Assets/_Project/Data/Items/`:
* **Matching (5):** `Item_sock_left`, `Item_sock_right`, `Item_pants`, `Item_shirt`, `Item_keys` (`dignityPoints: 20`)
* **Decoys / Traps (5):** `Item_sock_wet` (*"Squishy Foot"*, Bathroom only), `Item_sock_holey` (*"Toe Out"*), `Item_pants_clown` (*"Circus Commuter"*), `Item_shirt_insideout` (*"Seams Legit"*), `Item_keys_toy` (*"Toddler Chauffeur"*) (`dignityPoints: 10`)

---

## 6. House Level Layout (Compact 5-Room Loop — `HouseBuilder` 6×8 Tile Grid)

```text
+--------------------+--------------------+
|                    |                    |
|      BEDROOM       |      BATHROOM      |
|   (Player Spawn)   |  (Tub, Hamper,     |
|  Bed, Desk, Closet |   Puddle Hazard)   |
|         |          |         |          |
+---------+----------+---------+----------+
|               HALLWAY                   |
|     (Legos Hazard, Coat/Shoe Racks)     |
|         |                    |          |
+---------+--------------------+----------+
|                    |                    |
|    LIVING ROOM     |      KITCHEN       |
|  (Sofa, Coffee     |  (Cabinets, Table, |
|   Table, TV, Boxes)|   Fridge/Appliances|
|                    |                    |
+=====[FRONT DOOR]===+====================+
            (Idling RideCar Outside)
```

---

## 7. Project Directory Structure

```text
Assets/_Project
├── /Art
│   ├── /Materials      # Player/house materials + /Palette (5 schemes + checker textures)
│   ├── /Models         # Custom models (if needed)
│   ├── /PostFX         # SeptemberMorning.asset (URP VolumeProfile)
│   └── /UI             # Custom UI sprites (optional; HUD uses runtime uGUI/TMP)
├── /Audio              # Reserved if external clips are added (currently synthesized via Sfx.cs)
├── /Data
│   └── /Items          # 10 ItemData ScriptableObjects (5 matching + 5 decoys)
├── /Editor             # HouseBuilder, PlayerBuilder, GameSystemsBuilder, PaletteBuilder, PostFXBuilder
├── /Prefabs
│   ├── /Environment    # Shared environment prefabs
│   ├── /Furniture      # /Clutter (41 physics-safe grabbable/throwable clutter prefabs)
│   ├── /Items          # Optional custom wearable prefabs
│   └── /Player         # Player.prefab (Rigidbody + GoofyBody + ThirdPersonCamera + InteractionManager)
├── /Scenes
│   └── HouseLevel      # Main playable level (first in Build Settings)
└── /Scripts
    ├── /Core           # GameManager, TimerEscalationManager, RunIntro, Sfx, AudioHub, GameAudio
    ├── /Items          # ItemData, ItemVisuals, WearableItem, SpawnManager, HidingSpot, OutfitManager
    ├── /Player         # PlayerController, PlayerInputHandler, ThirdPersonCamera, GoofyBody, OutfitVisuals, InteractionManager
    ├── /UI             # Phase1DebugHUD, GameUI, PauseMenuUI
    └── /World          # IInteractable, PhysicsClutter, OpenableFurniture, HoverHighlighter, SlipperyHazard, RideCar, ExitDoor, ImpactSound
```

---

## 8. 5-Day Agent Implementation Plan (Current Status)

### Phase 1 (Day 1): Greybox & Core Physics Loop
- [x] Scaffold folder structure and `GameManager` state machine (`Menu`, `Playing`, `Win`, `Lose`, plus `IsPaused`).
- [x] Implement `PlayerController` (WASD, Sprint, Crouch, Jump, third-person physics momentum/skids/bonks) + `ThirdPersonCamera` + `GoofyBody`.
- [x] Implement `InteractionManager` with physics Grab, Hold (chest-height carry), and Throw/Yeet impulse.
- [x] Build house layout + `ExitDoor` win trigger.
- [x] Implement countdown timer and debug HUD (`Phase1DebugHUD`).

### Phase 2 (Day 2): Hidden Items, Spawning & Escalation
- [x] Create `ItemData` definitions (5 matching items + 5 decoy items in `Assets/_Project/Data/Items/`).
- [x] Place 44 `HidingSpot` anchors across all 5 rooms.
- [x] Implement `SpawnManager` (randomized placement across $\ge 3$ rooms + clutter spawning on `UnderClutter` spots).
- [x] Implement `OutfitManager`, `OutfitVisuals`, & HUD Checklist (`GameUI`).
- [x] Implement `TimerEscalationManager` + phone toast pop-ups (`GameUI`).

### Phase 3 (Day 3): Articulated Furniture & Visual Pass
- [x] Implement `OpenableFurniture` (32 sliding drawers & hinged doors).
- [x] Dress house with Kenney & Synty low-poly furniture, props, and switchable color palettes (`PaletteBuilder`).
- [x] Add `HoverHighlighter` (`MaterialPropertyBlock` tint pulse on aimed interactables).
- [x] Set up September morning lighting, `PostFX_SeptemberMorning` volume, and idling `RideCar` outside.

### Phase 4 (Day 4): Comedy, Polish & Hazards
- [x] Upgrade end screen (`GameUI` + `Phase1DebugHUD`) with Dignity Score, Time Bonus, Rank (`S`–`F`), funny tags, and Restart button.
- [x] Add camera juice (sprint FOV warp, wall-bonk/slip shake, panic vignette & chromatic aberration under `12s`).
- [x] Implement `SlipperyHazard` (Bathroom puddle & Hallway Legos).
- [x] Implement wake-up title overlay (`RunIntro` + `GameUI`) and runtime `PauseMenuUI` (`Esc` pause, Master Volume & Mouse Sensitivity sliders, Controls Card & hint strip, Quit).
- [x] *(Stretch)* Implement `PetAI` sock-stealing household pet (runtime-spawned `ChaosCat`). Dedicated `MainMenu.unity` scene dropped — the `RunIntro` title card covers it.

### Phase 5 (Day 5): Audio, Balance & Packaging
- [x] Integrate procedural SFX & music suite (`Sfx`, `AudioHub`, `GameAudio`, `ImpactSound`) and standalone single-`MonoBehaviour`-per-file compliance.
- [ ] Playtest and balance timer duration (`60s` vs `90s`), physics throw forces, and hiding spot fairness.
- [ ] Export and test standalone Windows build (`File > Build Profiles`) and prepare Game Jam submission page.

---

## 9. Directives for Antigravity Agent
When implementing tasks from this spec, follow these engineering rules:
1. **Physics Safety First:** Ensure small collectible items (`WearableItem`) remain kinematic inside drawers/spots until picked up, and dynamic clutter (`PhysicsClutter`) uses `BoxCollider` or convex `MeshCollider` with `ContinuousDynamic` collision detection.
2. **Standalone Build Safety:** Keep **at most one `MonoBehaviour`** per `.cs` file, named identically to the `.cs` filename.
3. **Expose Tuning Parameters:** Make movement speeds, throw force, grab distance, and timer thresholds serialized Inspector variables for rapid game-feel iteration.
4. **Decoupled Events:** Use C# events (`GameManager`, `OutfitManager`, `TimerEscalationManager`, `InteractionManager`, `PlayerController`) to avoid tight coupling.
5. **Scope Discipline:** Prioritize playtest balance, zero-error builds, and polish over high-risk scope additions.
