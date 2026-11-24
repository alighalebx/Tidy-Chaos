# Project Specification: *Where's My Left Sock?!* (Working Title)

> **Purpose:** This document serves as the single source of truth for **Antigravity Agent** and the developer(s) to scaffold, implement, and polish a 3D first-person comedy-chaos game within a **5-day Game Jam** window.

---

## 1. Project Overview
* **Game Jam Theme:** **HIDDEN**
* **Genre:** 3D First-Person Physics-Comedy / Time-Attack Scavenger Hunt
* **Core Pitch:** You overslept. Your ride (car/bus) is idling outside, honking, and leaving in 60–90 seconds. You must tear through your messy house—tossing pillows, yanking open drawers, and wrestling with physics clutter—to uncover your **hidden** clothes and essentials before sprinting out the front door.
* **Target Session Length:** 60–90 seconds per run (highly replayable via randomized hiding spots).
* **Timeline:** 5 Days.
* **Timeline:** 5 Days.

### 1.1 Technical Stack & Imported Assets (Unity URP)
* **Engine:** Unity 6 (3D Universal Render Pipeline / URP)
* **Input System:** Unity New Input System (`UnityEngine.InputSystem`) — do NOT use legacy `Input.GetAxis` or `Input.GetKeyDown`.
* **UI Framework:** uGUI (Canvas + TextMeshPro) for rapid HUD, timer, checklist, and end-screen prototyping. *(Switch to "UI Toolkit" if you prefer USS/UXML.)*
* **Workspace Rules:**
  * Put all new scripts, prefabs, scenes, and ScriptableObjects inside `Assets/_Project/`.
  * **Do NOT modify, rename, or move** files inside third-party asset folders.
* **Available Art Packs:**
  * **Synty POLYGON Sampler Pack** (`Assets/SyntyStudios/`):
    * `Assets/SyntyStudios/PolygonPrototype/Prefabs/Buildings/Polygon/` (modular walls/floors/doors) & `Assets/SyntyStudios/PolygonPrototype/Prefabs/Props/` (greybox/prototype clutter)
    * `Assets/SyntyStudios/PolygonCity/Prefabs/Vehicles/` & `Props/` (waiting car/taxi/van outside + urban props)
    * `Assets/SyntyStudios/PolygonKnights/Prefabs/`, `Assets/SyntyStudios/PolygonAdventure/Prefabs/`, `Assets/SyntyStudios/PolygonStarter/Prefabs/`, and `Assets/SyntyStudios/PolygonSamples/` (absurd decoy items & extra props)
  * **Kenney Furniture Kit** (`Assets/kenney-furniture-kit-interior-asset-collection/`): Use `Assets/kenney-furniture-kit-interior-asset-collection/Models/FBX format/` (140 `.fbx` interior furniture & clutter props for bedroom, bathroom, hallway, living room, and kitchen hiding spots).
* **Physics Rule for Imported Props:** Any furniture prop or clutter made grabbable/throwable must use a `BoxCollider` or a **Convex** `MeshCollider` paired with a `Rigidbody` (never a non-convex `MeshCollider` on a dynamic `Rigidbody`).

---

## 2. Theme Integration: "HIDDEN"


---

## 2. Theme Integration: "HIDDEN"
Every core mechanic must reinforce the theme:
1. **Physically Hidden:** Required items are never sitting out in plain sight. They are tucked *under* physics-enabled clutter (pillows, clothes piles, boxes), *inside* articulated furniture (drawers, cabinets, fridge, hampers), or *behind* environmental props.
2. **Hidden in Plain Sight (Decoys):** Mismatched, dirty, or ridiculous clothing items (e.g., a wet sock, clown pants, inside-out shirt) are placed in obvious locations to bait panicked players.
3. **Dynamically Hidden:** Optional mid-run chaos (e.g., a household pet grabbing an exposed sock and hiding it in another room).
4. **Hidden Until Interacted:** Some hiding spots require multi-step rummaging (e.g., open closet door $\rightarrow$ toss laundry basket $\rightarrow$ flip shoebox to find keys).

---

## 3. Core Gameplay Loop

1. **Wake-Up / Start State:** Player wakes up in bed to an alarm and a vibrating phone notification: *"Outside. Leaving in 60s."*
2. **Objective Reveal:** A diegetic or minimal HUD checklist displays 5 randomized essentials required to leave (e.g., *Left Sock, Right Sock, Pants, Shirt, Keys*).
3. **Search & Rummage:** Player sprints through the 4-room house, grabbing and throwing physics objects, opening drawers/doors, and searching hiding anchors.
4. **Equip / Compromise:**
   * Finding a **Matching Item** checks it off cleanly.
   * Finding a **Decoy/Trap Item** allows the player to equip it anyway to save time (at the cost of their final **Dignity Score**).
5. **Escalation:** The ride outside escalates audio/visual cues as the timer drops.
6. **Exit / Win Condition:** Player runs through the **Front Door** before time hits $t = 0$.
   * **Note:** The player *can* exit the house at any time—even missing pants or wearing one wet sock—resulting in a hilarious end-screen grade!
7. **Lose Condition:** Timer reaches $t = 0$ before exiting $\rightarrow$ The car/bus peels away outside the window, leaving the player stranded in pajamas.

---

## 4. Core Systems & Technical Specifications

### 4.1 First-Person Player Controller (`PlayerController`)
* **Movement:** Crisp, responsive WASD movement with high acceleration (minimal floatiness).
* **Sprint:** Hold `Shift` for speed boost + slight FOV increase.
* **Crouch:** Hold/Toggle `Ctrl` or `C` (essential for looking under beds, tables, and desks).
* **Jump:** `Space` (low hop to clear clutter piles).
* **Camera Juice:** Subtle head-bob while sprinting; subtle camera tilt when strafing; escalating heartbeat/shake vignette when timer $t < 15\text{s}$.

### 4.2 Physics Interaction & "Yeet" System (`InteractionManager`)
Use a center-screen raycast (range $\approx 2.5\text{m}$) with clear crosshair feedback (dot $\rightarrow$ open hand / grab icon / equip icon).

| Object Type | Interaction Input | Behavior |
| :--- | :--- | :--- |
| **Clutter Props** (Pillows, boxes, chairs, laundry) | `LMB` (Hold) / `RMB` (Click) | **Hold `LMB`** to pick up and carry using a spring-damped physics target in front of the camera. **Click `RMB`** (or release with flick) to **Yeet/Throw** forward with high impulse force. |
| **Wearable / Objective Items** (Socks, shirt, keys) | `E` or `LMB` (Tap) | Immediately equips/collects the item, plays a snappy "zip/pop" SFX, removes item from world, updates Checklist & Dignity Score. |
| **Articulated Furniture** (Drawers, cabinets, doors) | `E` (Tap) or `LMB` (Drag) | Smoothly slides/rotates open along constrained local axis (preferred: single-tap snappy open/close for fast jam pacing, or drag-to-pull physics hinge). |
| **Front Door (Exit)** | `E` (Tap) | Triggers immediate End-of-Run evaluation. |

### 4.3 Randomized Hiding & Spawn System (`SpawnManager`)
To guarantee replayability, items must never spawn in the exact same spot twice.

* **Spawn Anchors (`HidingSpot`):** Place 25–35 lightweight transform markers across the house. Each marker has metadata:
  * `spotID`: string
  * `room`: Enum (`Bedroom`, `Bathroom`, `LivingRoom`, `Hallway`, `Kitchen`)
  * `concealmentType`: Enum (`UnderClutter`, `InsideDrawer`, `HighShelf`, `FloorHidden`, `ObviousDecoySpot`)
  * `isOccupied`: bool
* **Spawning Algorithm (On Run Start):**
  1. Select the **5 Required Matching Items** for the run.
  2. Filter valid `HidingSpot` anchors for each item (avoiding `ObviousDecoySpot`).
  3. Randomly assign 1 unique anchor per required item (ensuring spatial distribution across at least 3 different rooms so the player must explore the whole house).
  4. Spawn **3–5 Decoy/Trap Items** in `ObviousDecoySpot` or remaining anchors.
  5. Spawn **Physics Clutter** (pillows, cardboard boxes, towels, laundry piles) directly on top of `UnderClutter` anchors so items are genuinely concealed.

### 4.4 Item & Dignity Scoring System (`OutfitManager` & `ScoreCalculator`)
Each wearable slot (`LeftFoot`, `RightFoot`, `Legs`, `Torso`, `Accessory`) tracks its equipped state:

* `Empty`: $0$ points (Major Dignity penalty)
* `Matching`: $+20$ points (Full Dignity)
* `Mismatched / Dirty / Wet`: $+10$ points (Funny status tag, e.g., *"Squishy Left Sock"*, *"Bathrobe Commuter"*)

**Final Score Formula:**
$$\text{Total Score} = \text{DignityScore} + \lfloor t_{\text{remaining}} \times 2 \rfloor$$

**End-Screen Ranks (Examples):**
* **S-Rank (100% Dignity + Fast):** *"Model Citizen"*
* **B-Rank (Full clothes, mismatched):** *"Fashionably Chaotic"*
* **C-Rank (Missing 1–2 items):** *"Forgot Pants, Caught the Bus"*
* **F-Rank (Missed Ride):** *"Left in Pajamas"*

### 4.5 Escalation & Ride Timer System (`TimerEscalationManager`)
Keep the player constantly aware of the ticking clock through diegetic audio and UI rather than a silent number:

* **$t = 60\text{s}$ (Start):** Low engine idle rumble outside the front window. Calm morning music starts.
* **$t = 40\text{s}$:** Single polite car honk. Phone toast notification: *"Yo I'm outside."*
* **$t = 25\text{s}$:** Double impatient honk. Music tempo speeds up ($1.15\times$ pitch/BPM). Phone toast: *"Seriously, I'm double-parked!"*
* **$t = 12\text{s}$:** Rapid honking + engine revving. Subtle red screen vignette + heartbeat SFX + camera FOV shift. Phone toast: *"LEAVING IN 10 SECONDS!!"*
* **$t = 0\text{s}$:** Tire screech SFX $\rightarrow$ Game Over trigger.

### 4.6 Chaos Hazards (Day 4 Stretch Features)
Implement at least 1–2 of these once the core loop is rock solid:
* **The Sock-Stealing Pet:** A simple NavMesh/waypoint cat or dog that roams the house. If it passes near an uncollected sock, it picks it up in its mouth and runs to hide in another room (player must chase and interact to reclaim it).
* **Slippery Hazard / Legos:** Stepping on spilled water in the bathroom or toys in the hallway applies a brief 1.5s camera stumble/slowdown + funny slip SFX.

---

## 5. Data Schemas

```json
// Example Item Definition (ScriptableObject / Resource / JSON)
{
  "itemId": "sock_left_clean",
  "displayName": "Clean Left Sock",
  "slot": "LeftFoot",
  "quality": "Matching",
  "dignityPoints": 20,
  "funnyTag": null,
  "allowedRooms": ["Bedroom", "LivingRoom", "Bathroom"],
  "meshAsset": "SM_Sock_Blue",
  "pickupSfx": "SFX_FabricPop"
}
```

```json
// Example Decoy Item Definition
{
  "itemId": "sock_left_wet",
  "displayName": "Soggy Bathroom Sock",
  "slot": "LeftFoot",
  "quality": "Trap",
  "dignityPoints": 10,
  "funnyTag": "Squishy Foot",
  "allowedRooms": ["Bathroom"],
  "meshAsset": "SM_Sock_WetGreen",
  "pickupSfx": "SFX_WetSquelch"
}
```

---

## 6. House Level Layout (Compact 4-Room Loop)
Design the floorplan as a tight, interconnected loop (not a long dead-end corridor) so players can sprint smoothly between rooms:

```text
+-------------------+-------------------+
|                   |                   |
|     BEDROOM       |     BATHROOM      |
|  (Player Spawn)   |  (Wet Decoys /    |
|  Bed, Closet,     |   Hamper, Tub,    |
|  Nightstand       |   Slippery Floor) |
|         |         |         |         |
+---------+---------+---------+---------+
|                                       |
|               HALLWAY                 |
|     (Shoe Rack, Coat Closet, Rug)     |
|                   |                   |
+-------------------+-------------------+
|                                       |
|             LIVING ROOM               |
|   (Couch Cushions, Coffee Table,      |
|    Cardboard Boxes, Front Window)     |
|                                       |
+==============[FRONT DOOR]=============+
            (Idling Car Outside)
```

---

## 7. Recommended Project Directory Structure

```text
Assets/_Project
├── /Art
│   ├── /Models         # Low-poly furniture, clothes, clutter props
│   ├── /Materials      # Simple flat/stylized shaders, outline highlight material
│   └── /UI             # Crosshair icons, checklist icons, phone toast UI
├── /Audio
│   ├── /SFX            # Footsteps, yeet whoosh, drawer slide, honks, equip pops
│   └── /Music          # Fast quirky morning loop
├── /Prefabs
│   ├── /Player         # PlayerController + Interaction raycaster + Physics hold point
│   ├── /Items          # Matching items & Decoy items
│   ├── /Furniture      # Openable drawers, closets, physics clutter piles
│   └── /Environment    # House walls, front door trigger, outside car prop
├── /Scenes
│   ├── MainMenu        # Camera inside bedroom looking at alarm/window
│   └── HouseLevel      # Main playable level
└── /Scripts
    ├── /Core           # GameManager, TimerEscalationManager, ScoreCalculator
    ├── /Player         # PlayerController, InteractionManager, CameraJuice
    ├── /Items          # ItemData, WearableItem, SpawnManager, HidingSpot, OutfitManager
    ├── /World          # OpenableFurniture, PhysicsClutter, ExitDoor, PetAI (stretch)
    └── /UI             # ChecklistUI, PhoneNotificationUI, EndScreenUI
```

---

## 8. 5-Day Agent Implementation Plan

### Phase 1 (Day 1): Greybox & Core Physics Loop
- [ ] Scaffold folder structure and `GameManager` state machine (`Menu`, `Playing`, `Win`, `Lose`).
- [ ] Implement `PlayerController` (WASD, Sprint, Crouch, Jump).
- [ ] Implement `InteractionManager` with physics Grab, Hold (spring/velocity tracking), and Throw/Yeet impulse.
- [ ] Greybox the 4-room house layout with basic colliders + `ExitDoor` win trigger.
- [ ] Implement basic countdown timer (`60s`) and debug HUD.
* **Milestone Check:** Can you walk around the greybox house, throw boxes across the room, and run out the front door before time runs out?

### Phase 2 (Day 2): Hidden Items, Spawning & Escalation
- [ ] Create `ItemData` definitions (5 matching items + 4 decoy items).
- [ ] Place 25+ `HidingSpot` anchors across all 4 rooms.
- [ ] Implement `SpawnManager` (randomized placement + clutter spawning on top of hidden spots).
- [ ] Implement `OutfitManager` & HUD `ChecklistUI` (shows slots filled, flags decoy items).
- [ ] Implement `TimerEscalationManager` + `PhoneNotificationUI` pop-ups.
* **Milestone Check:** Every restart hides items in new spots under clutter; collecting them updates the checklist; timer triggers text alerts.

### Phase 3 (Day 3): Articulated Furniture & Visual Pass
- [ ] Implement `OpenableFurniture` (sliding drawers, hinged closet doors, fridge door).
- [ ] Replace greybox primitives with stylized low-poly furniture & room props.
- [ ] Add subtle hover highlight/outline shader when looking at interactable objects.
- [ ] Set up bright morning sunlight streaming through windows + visible idling car/bus outside.

### Phase 4 (Day 4): Comedy, Polish & Hazards
- [ ] Build `EndScreenUI` showing final Dignity Score, Time Remaining, funny outfit tags, and instant "Play Again" button.
- [ ] Add camera juice (FOV sprint warp, panic vignette, subtle screen shake under $15\text{s}$).
- [ ] Implement 1–2 hazards (e.g., slippery bathroom puddle or sock-stealing pet AI).
- [ ] Build diegetic `MainMenu` (camera in bed looking at vibrating phone $\rightarrow$ smooth snap to first-person on Play).

### Phase 5 (Day 5): Audio, Balance & Packaging
- [ ] Integrate full SFX suite (honks, footsteps, fabric zip, clutter thuds, text pings, tire screech).
- [ ] Playtest and tune physics throw force, collider sizes (prevent small socks from clipping through floors!), and timer duration.
- [ ] Export, test standalone build, and prepare Game Jam submission page.

---

## 9. Directives for Antigravity Agent
When implementing tasks from this spec, follow these engineering rules:
1. **Physics Safety First:** Ensure small collectible items (like socks and keys) use continuous collision detection (CCD) or clean box/capsule colliders so they never clip through drawers or floors when clutter is tossed off them.
2. **Expose Tuning Parameters:** Make movement speed, throw force, grab distance, and timer thresholds public/serialized inspector variables for rapid game-feel iteration.
3. **Decoupled Events:** Use an Event Bus / C# Events / Signals pattern between `OutfitManager`, `TimerEscalationManager`, and UI scripts to avoid tight coupling.
4. **Scope Discipline:** Prioritize completing Phases 1 and 2 cleanly before adding stretch hazards or complex animations.