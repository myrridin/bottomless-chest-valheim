# Where this was left — 2026-09-14

Read this first. It is written to be picked up cold, after a context compaction or in a new
session, with nothing else in hand.

## The one-line answer

**0.2.0 is shipped and past 1,000 downloads. 0.3.0 is planned, not started.** It is
ValheimPlus 10.1.2 support, four changes the user asked for, and two items carried forward
from 0.2.0. Work happens on branch `release-0.3.0`. Nothing below is implemented yet —
**start at A1.**

A popular mod raises the cost of the standing rule: anyone on any earlier version must be
able to upgrade without losing data. Every item that touches storage or config defaults
has to be read with that in mind.

## Refs

| Ref | Commit | What |
|---|---|---|
| `v0.1.0` | `7a715cb` | First Thunderstore release |
| `v0.2.0` | `ee7afe7` | Valheim 1.0 release, on Thunderstore |
| `main` | `317ec90` | PR #1 merged; identical content to `v0.2.0` |
| `release-0.3.0` | — | This plan; branched from `main` |
| `craft-from-chest-dedicated` | `022a328` | V+ work parked at Task 1 of 9, still on **pre-1.0** `main` (`283c3b1`) |

---

## The 0.3.0 plan

### A. Investigate first — these decide the shape of everything in B

- [ ] **A1. Two uncoordinated copies of a chest on the server — reproduce or rule out.**
      Possible data loss; details in *The two-copies question* below. If it is real it
      already affects 0.2.0 users, so it is fixed and released before anything else.
- [ ] **A2. Who runs a V+ station.** 10.1.2 gates stations on `m_nview.IsOwner()` and finds
      chests without a local player. Establish which peer owns a smelter on (a) a dedicated
      server with players nearby and (b) a player-hosted server. Log it rather than infer it.
- [ ] **A3. Launch V+ 10.1.2 once** so it writes its config. The Development profile's
      `valheim_plus.cfg` is still the 9.17.1 one (dated 2026-09-02), so the new chest-related
      keys — auto-stack among them — have not been seen.

### B. ValheimPlus 10.1.2 support

- [ ] **B4. Rebase `craft-from-chest-dedicated` onto `main`.** The branch only adds
      `ChestIndex.cs`, `ChestIndexTests.cs`, a `.gitignore` line for `.superpowers/` and docs,
      so code should rebase clean. Expect conflicts in `docs/RESUME.md` (keep this file; fold
      in the branch's plan-state section) and `docs/valheim-1.0-compatibility.md`.
- [ ] **B5. Update the spec and `docs/valheimplus.md` for 10.1.2.** Line numbers, station
      ownership, the new features and the re-derived overlaps — all listed under
      *ValheimPlus 10.1.2* below.
- [ ] **B6. Rework plan Tasks 3–8 against the 1.0 code.** They were written before the take
      message carried request ids and amounts, before deposits consolidated, and before the
      read-only refusals. Re-extract the Task 3 and 4 briefs afterwards and re-apply
      Ruling 1 — the extracted copies in the ledger directory are now stale.
- [ ] **B7. Decide what server-run stations need.** Depends on A1 and A2. The server holds
      real contents, so they may need nothing from the index — but they must not write around
      an open session.
- [ ] **B8. Execute Tasks 2–8.** Task 1 is done (`e760a37`, review clean).
- [ ] **B9. Verify on the dedicated server (Task 9), extended for 10.1.2:** crafting, repair,
      building, stations (smelter family, cooking, fermenter, beehive, sap collector, shield
      generator, fireplace), auto-stack sweep, and V+'s container-panel resize alongside our
      scrollbar and drop marker. `dump-store.py` is the witness for every consumption test.
- [ ] **B10. `/code-review` the branch.** Storage and network code; every round so far has
      found real defects that self-review did not.

### C. Requested by the user

- [ ] **C11. Default build cost → the wooden chest's.** Believed to be `Wood:10`; confirm
      against `piece_chest_wood`'s `Piece.m_resources` rather than memory. Config defaults only
      reach new installs — BepInEx keeps whatever value is already in a player's file, and
      most players never edit it. Proposed: replace the stored value only when it still equals
      the old default exactly (`FineWood:20,BlackMetal:10,SurtlingCore:5`), leave anything
      edited. (The dev profile's `Wood:1` is a local testing value; leave it.)
- [ ] **C12. Make the chest look good instead of drab.** Current defaults: body `#C4C2BC`,
      lid `#2E2E33`, glow `#8FA86B` at `0.35`. Needs in-game screenshots to judge. Same
      stored-default problem as C11, same proposed answer.
- [ ] **C13. Remove `SnapshotDebounceSeconds`.** Bound in `ModConfig.cs`, read by nothing
      since `9065391` ("Page chest contents from the server instead of sending them whole").
      The user asked for a static value matching current behaviour; there is no current
      behaviour — deleting it changes nothing. Existing config files keep an orphaned
      `[Multiplayer]` entry, as they already do for `UnlimitedStacks` and
      `MaxStackMultiplier`. Check how BepInEx 5.4 exposes orphaned entries before promising to
      clean them up.
- [ ] **C14. README.**
      - Line 8: say tested with 100,000 stacks; a million works but makes a server more or
        less unusable.
      - Line 29: remove "Holding the use key deposits matching stacks…" — standard Valheim.
      - Line 33: remove "Everyone on a server needs the mod" — standard for any mod.
      - The 0.2.0 changelog calls a million "the ceiling". Leave that entry alone; restate the
        limit in the 0.3.0 entry so README and changelog agree.

### D. Carried forward from 0.2.0

- [ ] **D15. The unwired stack-limit code.** `StackRules.SplitForExit`, `EffectiveStackLimit`,
      `StackPolicy` and `CanMerge` are reached only by tests. That is the user's earlier
      decision — unbounded stacks were deprioritised and the config keys removed in
      `828c002` — not an accident. But stacks above the limit can still occur: lowering V+'s
      `itemStackMultiplier`, or removing V+, leaves them in the store, and they currently leave
      the chest unsplit into a player's vanilla inventory. Decide between wiring
      `SplitForExit` into the exit paths and deleting the lot. Also correct
      `StackConsolidation.cs:48`, whose comment still names `UnlimitedStacks`.
- [ ] **D16. A take that does nothing.** The client blanks slots and sends a take quoting its
      version; a second take sent before the first's count update arrives quotes the old
      version, the server rejects it as stale (`ChestRpc`, Take handler), resends the page,
      and the item stays in the chest. No data is lost — the guard is doing its job — but the
      click appears to do nothing. Seen three times on chest `2e39a316…` in the 2026-09-09
      session. Likely fix: the client holds further takes until the in-flight one is
      answered, rather than loosening the server's check.

### E. Release 0.3.0

- [ ] **E17.** Release gate: the four never-change identifiers against `v0.2.0`, the store
      read-path diff, `Still_looks_everywhere_0_1_0_looked` green. Then changelog, version bump
      in `Plugin.cs` **and** `manifest.json`, one default-log-level client run, package,
      verify the zip contains the new code, tag, push the tag. Upload only when asked.

---

## The two-copies question (A1)

On the server, a bottomless chest can exist twice in memory, and the two are never
reconciled:

- **The `Container`'s own inventory.** `ContainerPersistencePatches.LoadPatch` →
  `BottomlessContainer.LoadFromStore` fills it from the store **once** — `_contentsLoaded`
  stops any reload. `Container.Save` → `SaveToStore` serialises it back over the store entry.
- **A `ChestSession`'s inventory.** `ChestSessions.Acquire` reads the same store entry into a
  separate `Inventory`; remote players' takes and deposits change that copy, and `Persist`
  writes it back.

`BottomlessContainer` never refers to `ChestSessions`, and the reverse is true too.
Whichever copy saves last wins.

The loss this would cause:

1. The server instantiates the chest; its `Container` copy loads at T0.
2. A remote player deposits 50 iron through a session; the store now has the iron.
3. Something on the server changes the T0 copy and saves it — a V+ station pulling fuel via
   `RemoveItemFromChest` → `ConveyContainerToNetwork` → `Container.Save`.
4. The store is overwritten with the T0 contents minus the fuel. The iron is gone.

What has to be true for this to happen, and is **not yet established**:

- **Does the server instantiate the chest at all?** A dedicated server may never create
  `GameObject`s for areas only clients are near. If it does not, step 1 never happens on a
  dedicated server.
- **The most plausible trigger is a player-hosted server**, not a dedicated one. There the
  host *is* the server authority, instantiates everything near them, and owns nearby
  stations.
- **It may not need V+ at all.** On a player-hosted server, the host opening a chest works on
  the `Container` copy directly, with no session. If a remote player changed that chest
  through a session earlier, the host sees stale contents, and any change the host makes
  saves the stale copy. Check this case first — it would be a 0.2.0 bug, not a V+ one.

---

## ValheimPlus 10.1.2 — what the inspection found

Package: `Grantapher-ValheimPlus_Grantapher_Temporary` 10.1.2, released 2026-09-14 01:25,
depends on `denikson-BepInExPack_Valheim-5.4.2350`. Installed in the Development profile.
Both DLLs and full decompiles are kept at `/mnt/c/valheim_mods/valheimplus-assemblies/`
(`9.17.1/` and `10.1.2/`, each `ValheimPlus.dll` + `ValheimPlus.cs`). Line numbers below are
from `10.1.2/ValheimPlus.cs`; vanilla ones from
`/mnt/c/valheim_mods/valheim-0.221-assemblies/v1.0.cs`.

### The three hook points the design rests on — intact

| Method | Line | Change |
|---|---|---|
| `GetNearbyChestItemsByContainerList(List<Container>)` | 1355 | None. Still `GetInventory().GetAllItems()` per chest. |
| `RemoveItemFromChest(Container, ItemData, int)` | 1423 | None. Mutates stacks, reassigns `m_inventory.m_inventory`, ends in `ConveyContainerToNetwork`. |
| `RemoveItemFromChest(Container, string, int)` | 1455 | None. |

`ConveyContainerToNetwork` (1487) still calls `Container.Save`. Both
`RemoveItemInAmountFromAllNearbyChests` overloads (1379, 1401) still loop over
`RemoveItemFromChest`.

### New in `InventoryAssistant`

| Member | Line | Why it matters |
|---|---|---|
| `RemoveItemInAmountFromChests(List<Container>, …)` | 1249 | New removal entry point for stations. Loops over `RemoveItemFromChest`, so the planned write patch covers it. |
| `GetNearbyChestsForMachine` | 1167 | Station chest search. **No local player needed**; requires `Container.m_privacy == Public` (we force Public on the prefab) and `SharesWards`. |
| `GetNearbyChests` | 1157 | Player chest search. Still returns nothing without `Player.m_localPlayer`. |
| `FindNearbyChests` | 1265 | Shared by both. Still `Physics.OverlapSphere`; skips containers with `m_lastRevision == uint.MaxValue`. |
| `NearbyChestsLoaded` / `SpawnsOnThisPeer` / `MachineCouldUseChest` | 1185 / 1209 / 1218 | Stations wait up to 10 s for nearby chest ZDOs to be instantiated on this peer (`GameObjectAssistant`, 1939–2005). `MachineCouldUseChest` needs a public piece container with `s_creator != 0`. |

### Stations now run on the station's owner

`Smelter_Spawn_Patch` (8594) and `Smelter_UpdateSmelter_Patch` (8674) return early unless
`m_nview.IsOwner()`. The design's claim that "every V+ station behaviour runs on a client" is
out of date: they run on whichever peer owns the station. That is usually a nearby client,
but not necessarily. This feeds A1, A2 and B7.

### Who reaches a chest, and how

Through the hook points, and so covered by the design:
`Player_ConsumeResources` 7995, `Player_HaveRequirementItems` 7882, `Player_HaveRequirements`
7935, `InventoryGui_SetupRequirement` 9407, `InventoryGui_DoCrafting` 9477/9484,
`Recipe_GetAmount` 3503, `CookingStation_FindCookableItem` 4156/4168,
`CookingStation_UpdateCooking` 4245, `Fireplace_UpdateFireplace` 4453, `Fireplace_Interact`
4539, `Fermenter_SlowUpdate` 5831/5835, `ShieldGenerator_Update` 3779, `Smelter_UpdateSmelter`
8769/8802.

**Direct `inventory.AddItem` deposits**, which bypass `InventoryAssistant`:
`Smelter_Spawn` 8657, `Beehive_RPC_Extract` 2787, `Beehive_UpdateBees` 2852, `SapCollector`
3654, `Fermenter_DelayedTap` 5910. On a client, `ClientDepositGuard` refuses these and V+
falls back to dropping the item on the ground — the safe fallback 0.2.0 ships with.

**Direct reads that bypass the hook points:** `Smelter_UpdateSmelter_Patch` 8801 checks
`GetInventory().GetAllItems()` for ore. Against a client's page it finds nothing, so it does
nothing rather than harm.

### Auto-stack sweep — new, and it reaches chests

`AutoStackSweep` (6060) collects nearby chests (`GetNearbyChests`, 6249), requests each with
`Container.StackAll()`, and once a chest is granted and owned, `StackInto` (6301) calls
`chest.Load()` then `chest.GetInventory().StackAll(playerInventory)`. It logs "moved N
item(s) into a chest that did not save them, so they are lost" when the ZDO data revision does
not change.

- Vanilla `Inventory.StackAll` (v1.0.cs 67968) moves with `AddItem(item)`, the one-argument
  overload — exactly what `ClientDepositGuard` refuses on a client. So V+'s own move is inert
  against a remote bottomless chest.
- Its `Container.RPC_StackResponse` prefix (4096) can skip the original; ours
  (`StackAllPatch`) is also a prefix, and Harmony runs both. Ours sends the deposit to the
  server, which is expected to do the real work.
- Expected result: auto-stack works through our path, with warnings in V+'s log.
  **Unverified** — B9.

### Overlapping patches, re-derived

- **`Container.Awake`** (3969). The name switch is unchanged, so `$piece_bottomlesschest`
  still escapes the personal-chest 3×2 clamp. New: its postfix copies the inventory's size
  into `Container.m_width`/`m_height`. Vanilla reads `m_height` only in `UpdateRows`
  (v1.0.cs 121973, called once from 122284), and only as a floor it grows past. Looks benign.
- **`InventoryGrid.UpdateGui`** (3173). Still has the stale-element nudge, which remains
  unreachable for our grid. New: `LayoutContainerScrollbar` widens the container panel and
  moves `ContainerScroll` when the grid is wider than the panel, and otherwise writes the base
  values back. Our 8-column grid fits, so it should only restore. This is the scrollbar
  `ChestScrollbarBridge` detaches and shows or hides, so check it in game.
- **`Inventory` constructor patch — gone**, replaced by a `Player.SetInventorySize`
  transpiler (5950). One fewer overlap.
- `Inventory.MoveAll`, `Inventory.TopFirst`: unchanged.

---

## The parked branch, for B4–B8

- Spec: `docs/superpowers/specs/2026-09-02-craft-from-chest-on-dedicated-servers-design.md`
- Plan: `docs/superpowers/plans/2026-09-02-craft-from-chest-on-dedicated-servers.md`
- Both, plus that branch's own resume notes, are readable without checking it out:
  `git show craft-from-chest-dedicated:docs/RESUME.md`.
- Its rulings (1: `ChestIndexCache` lives in `BottomlessChest.Logic` so its tests compile;
  2: Task 4's `ChestSession.cs` entry is stale; 3: Tasks 5 and 6 go as one unit) are written
  in that file. The ledger at `.superpowers/sdd/2026-09-02-craft-from-chest-on-dedicated-servers/`
  is on disk but **not in git on `main`** — don't treat it as durable.
- One deferred cosmetic from Task 1's review: a stray blank line at `ChestIndex.cs:118`.

---

## Standing rules for this work

- **Upgrade without data loss**, for anyone on any earlier version. The four never-change
  identifiers: `bottomless_chest`, `BottomlessChest_id`, `com.myrridin.bottomlesschest`,
  `0x424C4331`.
- **`/code-review` every storage or network change** before tagging.
- **Decompile before patching**, Valheim and V+ alike. Mono inlines small methods and the
  patch silently never runs.
- **Back up the live store** before running a build that rewrites it.
- **Stop the server gracefully** — `taskkill /PID <pid>` without `/F` — so it saves.
- Client and server DLLs must be byte-identical; the build deploys to both and refuses while
  either is running.

## Environment

- Dedicated server: `valheim_server.exe -nographics -batchmode -name "BottomlessDev" -port 2456
  -world "bottomlessdev" -password "devpassword" -savedir "C:\valheim_mods\server-save"
  -public 0`, started from the dedicated server's install folder.
- Local dev config differs from shipped defaults on purpose: `EnableTestingCommands = true`,
  `Requirements = Wood:1`. Shipped defaults live in code.
- Admin list needs `V_<steamid>` for server commands and `Steam_<steamid>` for the client UI;
  both are in `server-save/adminlist.txt`.

## Things that bit, worth not re-learning

- **`ListContainsId` honours only the display prefix.** `FilterPlatformUserID` rewrites
  "Steam" to "V" and then *overwrites* the earlier match rather than OR-ing it.
- **BepInEx overwrites `LogOutput.log` on start.** Read the log before restarting; a session
  of evidence was lost that way. Reading it *during* a restart gives you the old session.
- **r2modman preserves package file timestamps**, so an updated dependency can look older
  than your build output and MSBuild skips the recompile. Delete `bin`/`obj` when a
  dependency changes.
- **WSL tooling rewrites line endings.** Valheim and BepInEx write CRLF; edit config files
  with something that preserves it.
- **Detaching a UI element from what managed it means taking over everything it managed.**
  The scrollbar vanished because the bridge took the bar off the `ScrollRect` and never took
  over its visibility.
- **Self-review does not find storage defects.** Four `/code-review` rounds in a row found real
  ones: a read-only session accepting writes, a stale index after `empty`, a store rewrite on
  every open, and more.

## History: 0.2.0

Shipped 2026-09-09. Valheim 1.0 and Jotunn 2.30.0; stores kept beside (not inside) 1.0's
per-world directories, which 1.0 prunes; the 65,535-stack format ceiling removed; stacks
consolidate on open and on deposit; partial take and put on dedicated servers; read-only
chests refuse changes; four pre-1.0 bugs fixed. Verified in game up to ten million stacks.
The full account is in `package/CHANGELOG.md` and the `v0.2.0` tag message.

## Backups, all verified byte-identical

- `/mnt/c/valheim_mods/backup-2026-09-09-pre-0.2.0-stores/` — stores as 0.1.0 left them.
  0.2.0 writes a format 0.1.0 cannot read, so this is the way back.
- `/mnt/c/valheim_mods/backup-2026-09-09-pre-consolidation-stores/` — the dev server store
  before consolidation first ran.
- `/mnt/c/valheim_mods/backup-2026-09-09-server-world-pre1.0/` — dedicated server world before
  1.0 converted it.
- `/mnt/c/valheim_mods/valheim-0.221-assemblies/` — old game assemblies and decompiles of
  both 0.221 and 1.0.
- `/mnt/c/valheim_mods/valheimplus-assemblies/` — V+ 9.17.1 and 10.1.2, DLLs and decompiles.
- `/mnt/c/valheim_mods/backup-2026-09-09-jotunn-2.29.2/` — the last pre-1.0 Jotunn.
- `recovery/logs-pre-0.2.0-release/` — the client log holding the scrollbar probe's findings.
