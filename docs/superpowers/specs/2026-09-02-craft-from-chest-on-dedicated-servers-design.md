# Craft from chest on dedicated servers

**Status:** design, not yet implemented
**Date:** 2026-09-02
**Verified against:** ValheimPlus 0.9.17.1 (`Grantapher-ValheimPlus_Grantapher_Temporary`),
Valheim 0.221.12, decompiled with `ilspycmd`. Line numbers below are from that decompile.

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
