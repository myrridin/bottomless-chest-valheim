# Craft From Chest For Any Mod — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let any mod that asks a chest what it holds the vanilla way - NearbyCrafting and the like - craft, build and deposit from a bottomless chest on a dedicated server, not only ValheimPlus.

**Architecture:** 0.3.0 already answers `HaveItem`, `CountItems`, `GetItem`, `RemoveItem(string, …)` and `MoveItemToThis` for a client's bottomless chest, but only while ValheimPlus is attached, and the client only asks the server for a chest's totals under the same condition. A chest instead starts asking when something asks about it, and stops when nothing has for a while.

**Tech Stack:** C# on .NET Framework (Unity/Mono), BepInEx 5.4.2350, HarmonyX, Jotunn 2.30.2, xUnit on the Logic assembly.

**Spec:** `docs/superpowers/specs/2026-09-02-craft-from-chest-on-dedicated-servers-design.md`, section *Revision — 2026-09-25 (3)*.

## Global Constraints

- **Ships as 0.4.0.** `VersionStrictness.Minor` means clients and server must update together.
- **No wire change.** A 0.3.0 server already answers `IndexRequest`, `TakeByName` and the deposit messages regardless of ValheimPlus.
- **ValheimPlus keeps its own switch.** `ValheimPlusBridge.Attached` still governs the three `InventoryAssistant` hooks and the optional sweep-targeting hook. Only the vanilla-method answers and the polling stop depending on it.
- **A chest nobody asks about must cost nothing:** no polling, no session held on the server.
- **Never patch `Inventory.GetAllItems`, `GetHeight` or `Changed`.**
- Build: `dotnet.exe build src/BottomlessChest/BottomlessChest.csproj -c Release` (deploys to client and server; stop both first).
- Test: `dotnet.exe test tests/BottomlessChest.Tests/BottomlessChest.Tests.csproj`
- Commit trailers:
  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01UsnnVf949UHMBchnCv6Vfy
  ```
- Do not push without being asked.

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/BottomlessChest/Core/BottomlessContainer.cs` | Modify | Records that something asked about this chest; polls only while that is recent |
| `src/BottomlessChest/Compat/ChestContents.cs` | Modify | Drops the ValheimPlus condition; marks the chest asked-about |
| `src/BottomlessChest/Compat/ChestQueryPatches.cs` | Modify | The V+ hooks mark it too, so a station's first question starts the polling |
| `src/BottomlessChest/Core/ClientDepositGuard.cs` | Modify | Forwards deposits whenever the chest is ours and the network is up |
| `package/manifest.json`, `Plugin.cs`, `BottomlessChest.csproj` | Modify | 0.4.0 |
| `package/CHANGELOG.md`, `package/README.md` | Modify | Say it works with craft-from-chest mods generally |

---

### Task 1: A chest asks only while something is asking about it

**Files:**
- Modify: `src/BottomlessChest/Core/BottomlessContainer.cs`, `src/BottomlessChest/Compat/ChestContents.cs`, `src/BottomlessChest/Compat/ChestQueryPatches.cs`

**Interfaces:**
- Produces: `BottomlessContainer.MarkContentsWanted()`; `BottomlessContainer.TryResolveInventory` and `TryResolve` unchanged.

No unit test: this is a Unity component reading `Time.unscaledTime`. Task 3 verifies it in game.

- [ ] **Step 1: `BottomlessContainer` records the demand.** Next to `_nextIndexAt`:

```csharp
        /// <summary>Until when something has asked what this chest holds.</summary>
        /// <remarks>
        /// A chest asks the server for its totals only while another mod is asking about it.
        /// Before 0.4.0 the trigger was "ValheimPlus is installed", which asked for every chest
        /// in sight whether anything wanted it and answered nothing at all for anyone else.
        /// The window outlives a burst of questions - a crafting panel asks every frame, a
        /// station every second - so it is not re-armed constantly, and lapses soon after the
        /// questions stop.
        /// </remarks>
        private float _wantedUntil = -1f;

        private const float WantedWindowSeconds = 30f;

        /// <summary>Something asked what this chest holds, so keep its totals current.</summary>
        internal void MarkContentsWanted() =>
            _wantedUntil = UnityEngine.Time.unscaledTime + WantedWindowSeconds;
```

- [ ] **Step 2: poll on demand.** In `RefreshIndex`, replace the ValheimPlus condition:

```csharp
            // Only while something is asking. Each request keeps the chest's session alive on
            // the server, so polling for nobody would hold every nearby chest in its memory.
            if (SidecarStore.IsServerAuthority || !Net.ChestRpc.Ready
                || UnityEngine.Time.unscaledTime > _wantedUntil)
            {
                return;
            }
```

- [ ] **Step 3: mark on every question.** In `ChestContents.Resolve`, drop `!ValheimPlusBridge.Attached` from the guard and mark the chest before returning true:

```csharp
            if (!InventoryCapacity.IsUnbounded(inventory) || Plugin.Degraded
                || SidecarStore.IsServerAuthority || !BottomlessContainer.TryResolveInventory(inventory, out var chest))
            {
                return false;
            }

            // Asking is what starts this chest keeping its totals current, and keeps it doing so.
            chest.MarkContentsWanted();
```

- [ ] **Step 4: the ValheimPlus hooks mark it too.** In `ChestQueryPatches.ListItemsPostfix`, after `TryResolve` succeeds, and in `TryTake` after `TryResolve` succeeds, call `bottomless.MarkContentsWanted();` so a station that only ever asks through ValheimPlus still starts the polling.

- [ ] **Step 5: build and run the suite.** Expected: build succeeds, 207 tests pass.

- [ ] **Step 6: Commit** — "Ask the server for a chest's totals only while something is asking".

---

### Task 2: Forward deposits for any mod

**Files:**
- Modify: `src/BottomlessChest/Core/ClientDepositGuard.cs`

- [ ] **Step 1: drop the ValheimPlus condition** in `TryForward`:

```csharp
            // Any mod, not only ValheimPlus: a client holds a page, so an item added here is
            // discarded at the next save unless the server is told. Refusing is the fallback
            // when it cannot be, which is what 0.2.1 did for everyone.
            if (item == null || !Net.ChestRpc.Ready)
            {
                return false;
            }
```

Update the class remarks: the paragraph naming `ValheimPlusBridge.Attached` becomes "forwarded for any mod; refusal is the fallback".

- [ ] **Step 2: build and run the suite.** Expected: build succeeds, 207 tests pass.

- [ ] **Step 3: Commit** — "Forward another mod's deposit whatever mod it is".

---

### Task 3: Ship it

**Files:**
- Modify: `src/BottomlessChest/Plugin.cs`, `src/BottomlessChest/BottomlessChest.csproj`, `package/manifest.json`, `package/CHANGELOG.md`, `package/README.md`, `docs/RESUME.md`

- [ ] **Step 1: version 0.4.0** in the three places, exactly as 0.3.0 was bumped.

- [ ] **Step 2: changelog**, a short entry:

```markdown
## 0.4.0

**Craft-from-chest mods can use a bottomless chest**, not only ValheimPlus. Tested with
NearbyCrafting.

**Everyone on a server needs 0.4.0**, including the server. Chests are stored exactly as before.

- Any mod that asks a chest what it holds the way the game does can now count a bottomless
  chest's contents, pay from it, and deposit into it.
- A chest asks the server for its totals only while something is asking about it, so a chest
  nobody queries costs nothing.
- Known limit: a mass deposit moves one stack per press, because one deposit is in flight at a
  time.
```

- [ ] **Step 3: README**, widen the ValheimPlus bullet to name craft-from-chest mods generally, keeping ValheimPlus as the tested one.

- [ ] **Step 4: build, test, package, verify the zip** as 0.3.0 was verified.

- [ ] **Step 5: in game, on the dedicated server, with NearbyCrafting installed in the client profile** (`IPA38-NearbyCrafting` 1.2.1, decompiled at `/mnt/c/valheim_mods/nearbycrafting/`):
  1. **Without ValheimPlus enabled**, stand near a bottomless chest holding wood and stone: the
     build panel should count them, and building should pay from the chest.
  2. Craft something at a workbench paid from the chest.
  3. Mass quick-deposit: one stack moves per press.
  4. **With ValheimPlus back on**, re-run two station checks from 0.3.0's list, to prove the
     demand-driven polling did not break them: a kiln pulling wood, and a smelter depositing bars.
  5. `save`, quit; check the store and both logs.

- [ ] **Step 6: `/code-review`, then commit the results in `docs/RESUME.md`.**

---

## Self-review

- **Spec coverage:** ask-on-demand → Task 1; deposits → Task 2; version, docs and the known limit → Task 3.
- **Placeholders:** none.
- **Types:** `MarkContentsWanted()` is defined in Task 1 Step 1 and used in Steps 3 and 4 only.
- **Risk:** the first question about a chest answers empty. Stations retry; requirement panels re-query. Task 3 Step 5 is what proves it in practice.
