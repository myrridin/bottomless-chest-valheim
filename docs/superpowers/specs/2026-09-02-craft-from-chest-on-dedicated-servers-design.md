# Craft from chest on dedicated servers

**Status:** design; being implemented on `release-0.3.0` (index logic done)
**Date:** 2026-09-02, re-derived 2026-09-14
**Verified against:** first ValheimPlus 0.9.17.1 on Valheim 0.221.12; re-derived against
ValheimPlus 10.1.2 on Valheim 1.0 (see below). Both decompiled with `ilspycmd`. Line numbers
in the body are from the 0.9.17.1 decompile unless marked.

## What changed with Valheim 1.0 and ValheimPlus 10.1.2

The design holds. The seam is intact, and what moved is listed here. The implementation
rulings that follow from it are in the plan's *Revision — 2026-09-14* section.

- **The three hook points are unchanged** in body and parameter names. In 10.1.2:
  - `GetNearbyChestItemsByContainerList` is at 1355;
  - `RemoveItemFromChest(Container, ItemData, int)` at 1423;
  - `RemoveItemFromChest(Container, string, int)` at 1455;
  - `ConveyContainerToNetwork` at 1487.

  The type is `ValheimPlus.InventoryAssistant`, internal. A new
  `RemoveItemInAmountFromChests` (1249), used by stations, loops over `RemoveItemFromChest`,
  so the write patches cover it.
- **Removal ignores quality.** Both overloads match on `m_shared.m_name` alone, so the write
  patches take any quality.
- **Stations no longer run only on a client.** The *Non-goals* section below says every V+
  station runs on a client. In 10.1.2 they run on whichever peer owns the station:
  `Smelter_UpdateSmelter_Patch` (8674) returns unless `m_nview.IsOwner()`, and chests are found
  by `GetNearbyChestsForMachine` (1167), which needs no local player. On a client that is this
  design's problem, unchanged. Where the server owns the station, it also holds the chest's
  real inventory, shared with any open session since 0.2.1, and the patches stay out of the way.
- **The fourth seam is decided: forward the deposit.** 0.2.0 refuses these deposits on a
  client (`ClientDepositGuard`), and V+ falls back to the ground. 0.3.0 forwards station output
  to the server over dedicated messages, and refused deposits land on the ground at the chest.
  Items from the player's own inventory are still refused: Place Stacks and V+'s new auto-stack
  sweep (6060) deposit those through their own path.
- **Index versions carry a session generation.** A session's version restarts at 0.
- **A new risk, world level.** V+ counts only items at or above `Game.m_worldLevel`. The index
  does not carry world level, so crafting from a bottomless chest is inert in a world whose world
  level is above 0.

## Revision — 2026-09-14 (2): the places ValheimPlus looks past the seam

Found in B9 by the user's station tests and a sweep of every V+ call that touches a chest's
inventory (10.1.2 line numbers). Approved by the user the same day; the plan's matching section
is *Revision — 2026-09-14 (2)*.

### What is wrong

The three `InventoryAssistant` hooks cover requirement counts, building, normal crafting and
every station pull through `RemoveItemFromChest`. On a client, V+ also asks a bottomless chest's
*local* inventory - the last page the player saw, or nothing - in these places:

| Site | Call on the chest's inventory | Effect |
|---|---|---|
| `Smelter_Spawn_Patch` 8657, `Beehive_RPC_Extract_Patch` 2787, `Beehive_UpdateBees_Transpiler` 2852, `SapCollector_UpdateTick_Patch` 3654, `Fermenter_DelayedTap_Transpiler` 5910 | `HaveItem(name)` then `AddItem` | Output goes to the wrong chest (observed: a smelter splitting bars) |
| `CookingStation_FindCookableItem_Transpiler` 4164 | `HaveItem` gate before removal | Never pulls |
| `Fermenter_SlowUpdate_Transpiler` 5835 | vanilla `Fermenter.FindCookableItem(inventory)` → `GetItem(name)` | Never pulls |
| `Recipe_GetAmount_Transpiler` 3523 | `CountItems(name, q)`, `GetItem(name, q)` | Choose-one-ingredient recipes miss chest materials |
| `InventoryGui_DoCrafting_Transpiler` 9482 | `CountItems(name, q) > 0` gate | Those recipes skip the chest when consuming |
| `Fireplace_Interact_Transpiler` 4530 | `HaveItem`, then vanilla `Inventory.RemoveItem(name, 1)` on the chest | **Free fuel**: removed from the page, never from the server |
| `AutoStackSweep.IsCandidate` 6370 | V+'s static `Inventory_StackAll_Patch.ContainsItemByName(inventory, name)` | Sweep targets judged from stale pages |

And Place Stacks itself:

- **No sweep from a bottomless chest.** `StackAllButtonPatch` skips `InventoryGui.OnStackAll`, so
  `Inventory.StackAll` never runs and V+'s postfix never starts a sweep.
- **Duplication.** `AutoStackSweep.Start` asks every candidate at once; each grant reaches
  `StackAllPatch`, which offers the player's items to that chest. Offers are limited per chest
  only, so two bottomless chests holding the same item both keep the same items. Live in 0.2.1.
  User's ruling: fold the fix into 0.3.0, no hotfix.
- **Timeout re-send.** Offers and single-item puts allow a re-send after 5 s without a reply; a
  reply that was only slow means the server keeps both.
- **World level.** V+ and vanilla count only items with `m_worldLevel >= Game.m_worldLevel` (the
  World Level world modifier, 0-10). The index does not carry it and stand-ins are cloned from
  templates carrying the prefab's value (almost certainly 0), so at world level 1+ crafting from
  bottomless chests counts nothing.
- **Cheated flag.** V+ marks smelter output cheated from `GetAllItems().Any(m_cheated)` (8801) and
  the fermenter copies the pulled item's flag; pages and stand-ins are never cheated.

### The design

**A. Four vanilla `Inventory` queries answer from the index.** On a client, with
`ValheimPlusBridge.Attached`, for an inventory belonging to a bottomless chest:

- `HaveItem(string name, bool matchWorldLevel)` → any entry with that shared name, count > 0, and
  world level at or above `Game.m_worldLevel` when matching.
- `CountItems(string name, int quality, bool matchWorldLevel)` → the index total, same filters.
- `GetItem(string name, int quality, bool isPrefabName)` → a stand-in item: template clone carrying
  the entry's quality, world level, cheated flag and count, with `m_dropPrefab` set. Never a page
  item.
- `RemoveItem(string name, int amount, int itemQuality, bool worldLevelBased)` → a server take,
  exactly as the existing removal hooks do.

Patched once at the vanilla methods rather than at each V+ site: it covers every site above and
any V+ adds later, and costs other inventories one hash lookup. The server side is untouched.

**B. One Place Stacks intercept.** A prefix on `Inventory.StackAll(Inventory, bool)` for a client
bottomless inventory replaces `StackAllButtonPatch` and `StackAllPatch`. The button, the use-key
hold and V+'s sweep all reach it, and V+'s prefix and postfix still run, so the sweep starts from
a bottomless chest too. An **optional** hook on V+'s `Inventory_StackAll_Patch.ContainsItemByName`
answers sweep targeting by running V+'s own filter over stand-ins. It is outside the
all-or-nothing set: if it cannot attach, only sweep targeting keeps today's behaviour.

**C. One offer at a time, with reservations.** Client offers to different chests queue; the next
goes out when the previous chest answers. Every item in flight - an offer's or a put's - is
reserved until its reply and is never sent again. A timeout lets other items and chests proceed,
never the reserved ones; a reply that never comes (a disconnect) leaves the item with the player.
The server always answers: `Stacked` with none kept when no session opens, and a new `PutRefused`
(message 18) wherever it declines a put.

**Index entries** carry world level and the cheated flag: one entry per (prefab, quality, world
level, cheated). `TakeByName` carries a minimum world level, `-1` for any: V+'s own removals ignore
world level, so its hooks send `-1`; vanilla `RemoveItem(..., worldLevelBased: true)` sends
`Game.m_worldLevel`. Both messages are unreleased, so their formats are free to change.

**Messages follow vanilla's flag.** A reply shows vanilla's `$msg_stackall N` (items, not stacks)
only when the offer was made with `StackAll(..., message: true)` - the use-key hold. The button
shows nothing, as in vanilla, and V+ turns the flag off and shows its own sweep summary, which
undercounts items that went into bottomless chests. The user chose one message over a correct
count needing another V+ hook.

**Accepted:** a free item when another consumer drains the last units of an item within the
second before this client's index refreshes. It never overdraws the store and never loses items.

## Problem

ValheimPlus lets a player build, craft and repair using materials held in nearby chests.
Against a bottomless chest this works in single-player and does nothing on a dedicated
server.

The cause is our paging. Contents live on the server; a client is sent only the window it
is looking at, and a **closed** chest leaves `Container.m_inventory` empty. V+ passes its
`GetInventory() != null` check, finds no items, and removes nothing.

Today's behaviour is therefore inert rather than destructive - V+ under-counts and takes
nothing, so no material is lost. That is the floor this design must not fall below.

## Goals

- V+ crafting, building and repair see and consume bottomless chest contents on a
  dedicated server, matching what they already do in single-player.
- No change to the paging principle: a client never receives whole chest contents.
- No item is ever lost from a chest. Divergence must err toward the player, never the store.
- Absent or refactored V+ degrades to today's behaviour rather than breaking the chest.

## Non-goals

- Crafting from chests **without** V+ installed. Decided 2026-09-02: this is a compatibility
  fix, not a feature of this mod. If that changes, the index below is the foundation for it.
- Making a chest reachable that V+ would not otherwise have found. We answer its questions;
  we do not widen the set of chests it may ask about.

**Station auto-pull is in scope, contrary to a first draft of this document.** It was
listed as already working on the grounds that stations run on the machine owning the chest.
That is wrong. `GetNearbyChests` (1349) dereferences `Player.m_localPlayer.GetPlayerID()`,
and `Smelter_UpdateSmelter_Patch` returns early without a local player - so every V+ station
behaviour runs on a **client**, not on the dedicated server, and hits exactly the same empty
paged inventory. Kilns, smelters, furnaces, cooking stations and fermenters are therefore
broken on dedicated servers today for the same reason, and are fixed by the same seam:
their fuel and ore pulls go through `RemoveItemInAmountFromAllNearbyChests` and
`RemoveItemFromChest`.

## The interception seam

V+'s chest access is funnelled through `InventoryAssistant`, which is smaller than the
call-site count suggests. **Three patch points cover every path.**

| Method | Line | Direction | Why this one |
|---|---|---|---|
| `GetNearbyChestItemsByContainerList(List<Container>)` | 1444 | read | Builds the list that every requirement check, repair check and threshold then counts. All six read call sites funnel through it. |
| `RemoveItemFromChest(Container, ItemData, int)` | 1512 | write | Every consumption path reaches this or its overload. |
| `RemoveItemFromChest(Container, string, int)` | 1544 | write | Same, for the name-keyed callers. |

Both `RemoveItemInAmountFromAllNearbyChests` overloads (1468, 1490) check a total via the
read method and then loop calling `RemoveItemFromChest`, so patching the two removal
overloads covers building, crafting and repair without touching the loop itself.

`ChestContainsItem` (1403) needs no patch: our write prefix skips the original, which is
the only caller that consults it.

**We cannot take the obvious shortcut** of making `Inventory.GetAllItems` lie about a
bottomless chest's contents. Mono inlines it, so the patch would apply and never run -
constraint #4 in the plan, and the same failure mode confirmed on `Inventory.Changed`
earlier today.

### The fourth seam: deposits, which do not go through InventoryAssistant

Station output takes a different route. `Smelter_Spawn_Patch` (8979) deposits with a direct
`inventory.AddItem(comp.m_itemData)` (9045) and then calls `ConveyContainerToNetwork`.

On a dedicated client that is a silent loss. The client's paged inventory is empty, so
`AddItem` finds a free slot and **succeeds**; `ConveyContainerToNetwork` calls
`Container.Save()`; our save patch declines to write because the client is not the
authority. The produced item is added to a phantom inventory and never reaches the store.
Vanilla would have dropped it on the ground, so this is output the player loses.

This is a **defect discovered while writing this design**, not a consequence of it, and it
is arguably more urgent than the crafting gap: crafting under-counts and takes nothing,
whereas this destroys smelter output. It needs its own decision before implementation:

- Patch the `Inventory.AddItem(ItemData)` overload for chest inventories on a non-authority
  client, forwarding the deposit to the server. Targeted, but `AddItem` is on a hot path and
  its inlining behaviour must be confirmed by decompile first - `Inventory.Changed` and
  `GetAllItems` both proved unpatchable for that reason.
- Or detect the addition on the save path, where we already intercept, by diffing the paged
  inventory against what we last sent it, and forward the difference.

Neither is designed here. Flagged so it is decided deliberately rather than discovered in
testing.

## The index

A per-chest summary: one entry per `(prefab name, quality)` carrying the chest's true
total in `m_stack`.

It is bounded by item **variety**, not stack count. A chest holding a million stacks of
forty item types syncs forty entries. This is what lets the feature exist without
violating paging.

- Held client-side for bottomless chests within range of the player.
- Pushed by the server on change, over the existing `ChestRpc` channel, carrying the
  session `Version` already used for staleness checks.
- Never written to the ZDO. Never becomes the inventory the grid draws. It exists only to
  answer V+'s questions.

Synthetic `ItemData` handed to V+ are clones built through `ItemTemplates`, so they carry
the shared data `ItemDrop.Awake` would have given them - stack size, weight,
teleportability - exactly as the fast loader does.

## Data flow

**Read.** V+ calls `GetNearbyChestItemsByContainerList` with the chests it found. Our
postfix walks that list, and for each bottomless chest appends its index entries to the
returned list. V+ counts them as ordinary items and gets the right answer.

**Write.** V+ calls `RemoveItemFromChest(chest, needle, amount)` and expects an `int` back
**synchronously**. Our prefix, for a bottomless chest:

1. Decrements the local index by what it can satisfy.
2. Returns that amount to V+ immediately, skipping the original.
3. Sends the removal to the server, quoting the index version it acted on.

The server removes what it actually has, and replies with the true amount and a fresh
index.

## The optimistic write, and why it is acceptable here

This is an optimistic local edit. The plan records that optimistic edits duplicated items
twice in this project, so it is named here rather than buried.

The difference is the direction of the error. The client answers from a version-stamped
server snapshot and the divergence window is one round trip. If the server holds less than
the index claimed, it removes what it has - **the chest is never over-drawn.** The player
may occasionally receive a craft they had not quite paid for.

Accepted 2026-09-02 on that basis: chest contents are the product, a duplicated wooden
beam is not. The invariant is one-directional and must stay so - **the server is always
free to remove less than asked, and never more.**

## Error handling and degradation

- **V+ absent.** All three patches are attached by reflection against a soft dependency. No
  assembly reference, no patch, no cost.
- **V+ refactored.** Patch attachment is per-method and individually guarded; a method that
  no longer resolves is logged once and skipped, leaving that path at today's inert
  behaviour. A missing seam must never take the chest with it.
- **Each patch body is wrapped.** An exception inside ours falls through to V+'s original
  rather than propagating - the same degradation rule as prefab registration.
- **Stale version.** The server refuses and re-syncs the index, per the existing mechanism.
- **Chest open while crafting.** The window's paged inventory and the index are separate;
  the index remains the only thing V+ is shown.

## Testing

Unit-testable, and therefore test-first:

- Index construction: totals per (name, quality); variety-bounded size; quality kept
  distinct; unstackables never merged.
- Satisfaction arithmetic: exact, partial and zero removals; a request larger than held.
- Version staleness: an operation quoting an old version is refused.

Needs the game, and belongs in the dedicated-server pass:

- Build a piece with materials only in a bottomless chest, client on a dedicated server.
- Repair with materials only in the chest.
- Materials split across two bottomless chests.
- Craft while the chest window is open.
- Confirm against `dump-store.py` that the store's totals fell by exactly the recipe cost -
  the file read outside the game stays the honest witness.
- Kill the client mid-craft and confirm the store is short by at most one operation.

## Risks

1. **We depend on another mod's internals.** Named in `docs/valheimplus.md` as the cost of
   this approach. Mitigated by degradation, not avoided.
2. **The index is a second representation of contents.** Two sources of truth for the same
   chest is how duplication bugs start. It is deliberately read-only to V+, never drawn,
   and never persisted.
3. **Range mismatch.** V+ finds chests by `Physics.OverlapSphere` at up to 50m; our index
   syncs by its own range rule. If ours is narrower, V+ sees a chest with no index and
   silently gets zero. The sync range must be at least V+'s configured `CraftFromChest.range`
   ceiling of 50m.
