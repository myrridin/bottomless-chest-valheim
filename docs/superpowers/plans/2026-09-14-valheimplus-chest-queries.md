# ValheimPlus Chest Queries — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close every place ValheimPlus 10.1.2 reads or changes a bottomless chest's local page on a client, and remove the Place Stacks duplication and re-send races.

**Architecture:** Index entries gain world level and the cheated flag. Four vanilla `Inventory` queries answer from the index for client bottomless inventories. One `Inventory.StackAll` prefix replaces two Place Stacks intercepts. A client offer queue reserves in-flight items so nothing is ever offered twice.

**Tech Stack:** C# on .NET Framework (Unity/Mono), BepInEx 5.4.2350, HarmonyX, Jotunn 2.30.0, xUnit on the Logic assembly.

**Spec:** `docs/superpowers/specs/2026-09-02-craft-from-chest-on-dedicated-servers-design.md`, section *Revision — 2026-09-14 (2)*.

## Global Constraints

- **No hard assembly reference to ValheimPlus.** Bind by reflection; absent V+ means no patches run their bodies.
- **Everything done for V+ checks `ValheimPlusBridge.Attached`.** Without V+ a client behaves as before this plan, except the Place Stacks fixes (Tasks 3 and 4), which are V+-independent correctness fixes.
- **The new sweep-targeting hook is optional** and is not part of the all-or-nothing hook set.
- **The server may remove less than asked, never more.**
- **Never patch `Inventory.GetAllItems`, `Inventory.GetHeight` or `Inventory.Changed`** (Mono inlining).
- **State argument types explicitly on every `HarmonyPatch` of an overloaded method.**
- **Server-authority paths are untouched.** Every new client patch returns early on `SidecarStore.IsServerAuthority`.
- **Messages 13–18 are unreleased;** their formats may change until 0.3.0 ships. The four never-change identifiers (`bottomless_chest`, `BottomlessChest_id`, `com.myrridin.bottomlesschest`, `0x424C4331`) and the store format are not touched.
- Build: `dotnet.exe build src/BottomlessChest/BottomlessChest.csproj -c Release` (deploys to client and server; stop both first).
- Test: `dotnet.exe test tests/BottomlessChest.Tests/BottomlessChest.Tests.csproj`
- Every commit ends with:
  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01UsnnVf949UHMBchnCv6Vfy
  ```
- Do not push.

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/BottomlessChest.Logic/IStorableItem.cs` | Modify | Adds `WorldLevel`, `Cheated` |
| `src/BottomlessChest.Logic/IndexedItem.cs` | Modify | Carries them |
| `src/BottomlessChest.Logic/ChestIndex.cs` | Modify | Entries keyed by world level and cheated; `Count` by predicate; `Take` with minimum world level |
| `src/BottomlessChest.Logic/OfferQueue.cs` | Create | One offer at a time, reservations, timeouts |
| `src/BottomlessChest/Filter/ItemAdapter.cs` | Modify | Maps the two fields |
| `src/BottomlessChest/Net/ChestProtocol.cs` | Modify | `PutRefused = 18` |
| `src/BottomlessChest/Net/ChestRpc.cs` | Modify | Wire formats; server always answers puts and stack-alls |
| `src/BottomlessChest/Core/ChestSession.cs` | Modify | `RemoveByName` minimum world level |
| `src/BottomlessChest/Core/BottomlessContainer.cs` | Modify | `TryResolveInventory`; drives the offer queue |
| `src/BottomlessChest/Compat/ChestContents.cs` | Create | Client-side answers about a bottomless chest from its index |
| `src/BottomlessChest/Compat/ChestQueryPatches.cs` | Modify | Uses `ChestContents`; sweep-targeting prefix |
| `src/BottomlessChest/Compat/InventoryQueryPatches.cs` | Create | `HaveItem`, `CountItems`, `GetItem`, `RemoveItem(string)` |
| `src/BottomlessChest/Compat/ValheimPlusBridge.cs` | Modify | Optional sweep-targeting hook |
| `src/BottomlessChest/Core/ClientDepositGuard.cs` | Modify | Uses `TryResolveInventory` |
| `src/BottomlessChest/Core/ClientStackAllPatch.cs` | Create | The one client Place Stacks intercept |
| `src/BottomlessChest/Core/StackAllPatch.cs`, `StackAllButtonPatch.cs` | Delete | Replaced |
| `src/BottomlessChest/Filter/ChestView.cs` | Modify | Offer queue, put reservation, `PutRefused`, messages |
| `tests/BottomlessChest.Tests/TestItem.cs` | Modify | New fields |
| `tests/BottomlessChest.Tests/ChestIndexTests.cs` | Modify | New index tests |
| `tests/BottomlessChest.Tests/OfferQueueTests.cs` | Create | Queue tests |

---

### Task 1: Index entries carry world level and the cheated flag

**Files:**
- Modify: `src/BottomlessChest.Logic/IStorableItem.cs`, `src/BottomlessChest.Logic/IndexedItem.cs`, `src/BottomlessChest.Logic/ChestIndex.cs`
- Modify: `src/BottomlessChest/Filter/ItemAdapter.cs`, `src/BottomlessChest/Net/ChestRpc.cs` (`TakeByName` sender, `SendIndex`, `IndexResult` and `TakeByName` handlers), `src/BottomlessChest/Core/ChestSession.cs` (`RemoveByName`), `src/BottomlessChest/Compat/ChestQueryPatches.cs` (`StandInsFor`)
- Test: `tests/BottomlessChest.Tests/TestItem.cs`, `tests/BottomlessChest.Tests/ChestIndexTests.cs`

**Interfaces:**
- Produces: `IStorableItem.WorldLevel` (int), `IStorableItem.Cheated` (bool); `IndexEntry(string itemId, int quality, int count, int worldLevel, bool cheated)` with `WorldLevel`, `Cheated`; `ChestIndex.Count(Func<string, bool> itemIdMatches, int quality, int minWorldLevel)` (negative quality or world level means any); `ChestIndex.Take(string itemId, int quality, int amount, int minWorldLevel = -1)`; `IndexedItem(string itemId, int quality, int stack, int worldLevel = 0, bool cheated = false)`; `ChestRpc.TakeByName(string storeId, long version, string itemId, int quality, int amount, int minWorldLevel = -1)`; `ChestSession.RemoveByName(string itemId, int quality, int amount, int minWorldLevel = -1)`.

- [ ] **Step 1: Extend `TestItem`** with trailing optional parameters `int worldLevel = 0, bool cheated = false` stored in `public int WorldLevel { get; }` and `public bool Cheated { get; }`.

- [ ] **Step 2: Write the failing tests** — append to `ChestIndexTests`:

```csharp
        private static TestItem Leveled(string id, int stack, int worldLevel, bool cheated = false) =>
            new TestItem(id, ItemKind.Material, stack, itemId: id, maxStackSize: 50, worldLevel: worldLevel, cheated: cheated);

        [Fact]
        public void DifferentWorldLevelsStaySeparate()
        {
            // Vanilla only counts items at or above the world level; merging would hide which.
            var index = ChestIndex.From(1, 1, new[] { Leveled("Wood", 10, 0), Leveled("Wood", 5, 2) });

            Assert.Equal(2, index.Entries.Count);
            Assert.Contains(index.Entries, e => e.WorldLevel == 2 && e.Count == 5);
        }

        [Fact]
        public void CheatedAndHonestItemsStaySeparate()
        {
            var index = ChestIndex.From(1, 1, new[] { Leveled("CopperOre", 10, 0), Leveled("CopperOre", 3, 0, cheated: true) });

            Assert.Equal(2, index.Entries.Count);
            Assert.Contains(index.Entries, e => e.Cheated && e.Count == 3);
        }

        [Fact]
        public void CountFiltersByNameQualityAndMinimumWorldLevel()
        {
            var index = ChestIndex.From(1, 1, new[]
            {
                Leveled("Wood", 10, 0), Leveled("Wood", 5, 2), Leveled("Stone", 7, 2),
            });

            Assert.Equal(15, index.Count(id => id == "Wood", -1, -1));
            Assert.Equal(5, index.Count(id => id == "Wood", -1, 1));
            Assert.Equal(0, index.Count(id => id == "Wood", 3, -1));
            Assert.Equal(22, index.Count(_ => true, -1, -1));
        }

        [Fact]
        public void TakeLeavesItemsBelowTheMinimumWorldLevel()
        {
            var index = ChestIndex.From(1, 1, new[] { Leveled("Wood", 10, 0), Leveled("Wood", 5, 2) });

            Assert.Equal(5, index.Take("Wood", -1, 8, minWorldLevel: 2));
            Assert.Equal(10, index.Count(id => id == "Wood", -1, -1));
        }

        [Fact]
        public void TakeWithoutAMinimumWorldLevelTakesAnyLevel()
        {
            var index = ChestIndex.From(1, 1, new[] { Leveled("Wood", 10, 0), Leveled("Wood", 5, 2) });

            Assert.Equal(12, index.Take("Wood", -1, 12));
        }
```

- [ ] **Step 3: Run the tests; expect compile errors** (`WorldLevel`, `Cheated`, `Count` missing).

- [ ] **Step 4: Implement the Logic changes.**

`IStorableItem` — add after `Quality`:

```csharp
        /// <summary>The world level the item was made at. Vanilla counts only items at or above the world's.</summary>
        int WorldLevel { get; }

        /// <summary>Whether the item came from a cheat. Carried so stations can mark what they make from it.</summary>
        bool Cheated { get; }
```

`IndexedItem` — constructor `IndexedItem(string itemId, int quality, int stack, int worldLevel = 0, bool cheated = false)` assigning `WorldLevel` and `Cheated` properties.

`IndexEntry` — constructor `IndexEntry(string itemId, int quality, int count, int worldLevel, bool cheated)`, properties `WorldLevel` and `Cheated`. Update its summary to "One item type, quality, world level and cheated state".

`ChestIndex.From` — key and entry construction:

```csharp
                    var key = item.ItemId + "/" + item.Quality + "/" + item.WorldLevel + "/" + (item.Cheated ? "1" : "0");

                    if (seen.TryGetValue(key, out var at))
                    {
                        var e = entries[at];
                        entries[at] = new IndexEntry(e.ItemId, e.Quality, e.Count + item.Stack, e.WorldLevel, e.Cheated);
                    }
                    else
                    {
                        seen[key] = entries.Count;
                        entries.Add(new IndexEntry(item.ItemId, item.Quality, item.Stack, item.WorldLevel, item.Cheated));
                    }
```

`ChestIndex.Count`:

```csharp
        /// <summary>
        /// How much is held of every entry whose item id matches, at a quality and minimum world
        /// level. A negative quality or world level means any.
        /// </summary>
        /// <remarks>
        /// By predicate because the questions arrive as shared-name tokens, which only the mod
        /// can resolve to prefab names.
        /// </remarks>
        public int Count(Func<string, bool> itemIdMatches, int quality, int minWorldLevel)
        {
            if (itemIdMatches == null)
            {
                return 0;
            }

            var total = 0;
            foreach (var entry in _entries)
            {
                if ((quality < 0 || entry.Quality == quality)
                    && (minWorldLevel < 0 || entry.WorldLevel >= minWorldLevel)
                    && entry.Count > 0
                    && itemIdMatches(entry.ItemId))
                {
                    total += entry.Count;
                }
            }

            return total;
        }
```

`ChestIndex.Take` — add `int minWorldLevel = -1`, skip entries with `minWorldLevel >= 0 && entry.WorldLevel < minWorldLevel`, and rebuild entries with `new IndexEntry(entry.ItemId, entry.Quality, entry.Count - from, entry.WorldLevel, entry.Cheated)`. Add `using System;` for `Func`.

- [ ] **Step 5: Run the tests; expect all to pass.**

- [ ] **Step 6: Mod side.**

`ItemAdapter`: `public int WorldLevel => _item.m_worldLevel;` and `public bool Cheated => _item.m_cheated;`

`ChestRpc.SendIndex` — per entry, write `ItemId`, `Quality`, `WorldLevel`, `Cheated`, `Count` in that order. `IndexResult` handler — read in the same order and build `new Logic.IndexedItem(itemId, quality, held, worldLevel, cheated)`.

`ChestRpc.TakeByName` sender — add `int minWorldLevel = -1` and write it after `quality`. Server `TakeByName` handler — read it after `quality` and call `session.RemoveByName(itemId, quality, amount, minWorldLevel)`.

`ChestSession.RemoveByName` — add `int minWorldLevel = -1`; in the skip condition add `|| (minWorldLevel >= 0 && item.m_worldLevel < minWorldLevel)`.

`ChestQueryPatches.StandInsFor` — after `standIn.m_quality = ...`:

```csharp
                    // At the level and cheat state the chest really holds. Templates carry the
                    // prefab's world level, which vanilla's "at or above the world's" filter
                    // rejects in any world above level 0.
                    standIn.m_worldLevel = index.Entries[i].WorldLevel;
                    standIn.m_cheated = index.Entries[i].Cheated;
```

- [ ] **Step 7: Build and run the full suite.** Expected: build succeeds; all tests pass.

- [ ] **Step 8: Commit** — "Carry world level and the cheated flag in chest indexes".

---

### Task 2: Answer V+'s questions about a chest from its index

**Files:**
- Create: `src/BottomlessChest/Compat/ChestContents.cs`, `src/BottomlessChest/Compat/InventoryQueryPatches.cs`
- Modify: `src/BottomlessChest/Core/BottomlessContainer.cs`, `src/BottomlessChest/Core/ClientDepositGuard.cs`, `src/BottomlessChest/Compat/ChestQueryPatches.cs`

**Interfaces:**
- Consumes: Task 1's `ChestIndex.Count`, `ChestIndex.Take(..., minWorldLevel)`, `IndexEntry.WorldLevel/Cheated`, `ChestRpc.TakeByName(..., minWorldLevel)`.
- Produces: `BottomlessContainer.TryResolveInventory(Inventory, out BottomlessContainer)`; `ChestContents.Resolve(Inventory, out string storeId, out ChestIndex index)` (true when the inventory is a client bottomless chest's, index may be null); `ChestContents.IsNamed(string itemId, string sharedName)`; `ChestContents.StandIn(IndexEntry)`; `ChestContents.Take(string storeId, ChestIndex index, string sharedName, int quality, int amount, int minWorldLevel)`.

No unit tests: everything here needs the game. The counting and taking it relies on is tested in Task 1.

- [ ] **Step 1: `BottomlessContainer.TryResolveInventory`**, after `TryResolve`:

```csharp
        /// <summary>The loaded chest whose inventory this is, if any.</summary>
        internal static bool TryResolveInventory(Inventory inventory, out BottomlessContainer found)
        {
            found = null;
            if (inventory == null)
            {
                return false;
            }

            foreach (var candidate in Registry.Values)
            {
                if (candidate != null && ReferenceEquals(candidate.Inventory, inventory))
                {
                    found = candidate;
                    return true;
                }
            }

            return false;
        }
```

Replace the loop in `ClientDepositGuard.TryForward` with `BottomlessContainer.TryResolveInventory(inventory, out var owner);`.

- [ ] **Step 2: Create `ChestContents`.**

```csharp
using System;
using BottomlessChest.Core;
using BottomlessChest.Logic;
using BottomlessChest.Storage;

namespace BottomlessChest.Compat
{
    /// <summary>
    /// What a bottomless chest holds, answered on a client from its synced index.
    /// </summary>
    /// <remarks>
    /// A client holds only the page on screen, or nothing. Everything that asks a bottomless
    /// chest what it holds on ValheimPlus's behalf comes through here, so every answer agrees.
    /// </remarks>
    internal static class ChestContents
    {
        /// <summary>
        /// Whether this inventory is a bottomless chest's on a client with the V+ integration on.
        /// </summary>
        /// <returns>
        /// True when the caller must answer for the chest. <paramref name="index"/> is null until
        /// the first summary arrives, and a chest with no summary holds nothing as far as
        /// another mod can tell - never whatever its page shows.
        /// </returns>
        internal static bool Resolve(Inventory inventory, out string storeId, out ChestIndex index)
        {
            storeId = null;
            index = null;

            if (!InventoryCapacity.IsUnbounded(inventory) || Plugin.Degraded || !ValheimPlusBridge.Attached
                || SidecarStore.IsServerAuthority || !BottomlessContainer.TryResolveInventory(inventory, out var chest))
            {
                return false;
            }

            storeId = chest.CurrentStoreId;
            if (!string.IsNullOrEmpty(storeId))
            {
                Net.ChestRpc.Indexes.TryGet(storeId, out index);
            }

            return true;
        }

        /// <summary>Whether an indexed prefab has the given shared name token.</summary>
        internal static bool IsNamed(string itemId, string sharedName)
        {
            var template = ItemTemplates.For(itemId);
            return template != null && template.m_shared.m_name == sharedName;
        }

        /// <summary>A throwaway item standing for an index entry. Never one the chest holds.</summary>
        internal static ItemDrop.ItemData StandIn(IndexEntry entry)
        {
            var template = ItemTemplates.For(entry.ItemId);
            if (template == null)
            {
                return null;
            }

            var standIn = template.Clone();
            standIn.m_quality = entry.Quality;
            standIn.m_worldLevel = entry.WorldLevel;
            standIn.m_cheated = entry.Cheated;
            standIn.m_stack = entry.Count;

            // Fermenter.RPC_AddItem names the item by its drop prefab.
            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(entry.ItemId) : null;
            if (prefab != null)
            {
                standIn.m_dropPrefab = prefab;
            }

            return standIn;
        }

        /// <summary>
        /// Deducts from the index at once and tells the server, which removes what is really there.
        /// </summary>
        /// <remarks>
        /// The caller has to answer synchronously; <see cref="ChestIndex.Take"/> never reports more
        /// than the index held, so the error only ever falls the player's way.
        /// </remarks>
        internal static int Take(string storeId, ChestIndex index, string sharedName, int quality, int amount, int minWorldLevel)
        {
            if (index == null || string.IsNullOrEmpty(storeId) || string.IsNullOrEmpty(sharedName) || amount <= 0)
            {
                return 0;
            }

            var taken = 0;
            var asked = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in index.Entries)
            {
                if (taken >= amount)
                {
                    break;
                }

                if (entry.Count <= 0 || !asked.Add(entry.ItemId) || !IsNamed(entry.ItemId, sharedName))
                {
                    continue;
                }

                var got = index.Take(entry.ItemId, quality, amount - taken, minWorldLevel);
                if (got > 0)
                {
                    Net.ChestRpc.TakeByName(storeId, index.Version, entry.ItemId, quality, got, minWorldLevel);
                    taken += got;
                }
            }

            return taken;
        }
    }
}
```

- [ ] **Step 3: Refactor `ChestQueryPatches`.** `TryTake` keeps its guard, then answers with `taken = ChestContents.Take(storeId, index, sharedName, -1, amount, -1);` and `return true;` (V+'s own removals ignore quality and world level). `StandInsFor` builds each stand-in with `ChestContents.StandIn(index.Entries[i])` (dropping its own clone and field assignments) and keeps refreshing `m_stack` in place.

- [ ] **Step 4: Create `InventoryQueryPatches`.**

```csharp
using System;
using HarmonyLib;

namespace BottomlessChest.Compat
{
    /// <summary>
    /// Answers vanilla's questions about a bottomless chest's contents from its index, on a client.
    /// </summary>
    /// <remarks>
    /// ValheimPlus asks a chest's inventory directly in about ten places the InventoryAssistant
    /// hooks do not cover: which chest a station's output goes to, whether a cooking station or
    /// fermenter can pull, choose-one-ingredient recipes, and fireplace fuel. On a client that
    /// inventory is a page or nothing. Patched once here rather than at each V+ site, so
    /// anything V+ adds later is covered too. Every other inventory pays one hash lookup.
    ///
    /// <c>RemoveItem(string, ...)</c> is included because V+'s fireplace swaps the chest's
    /// inventory in and vanilla removes the fuel from it: from a page, that fuel was free.
    /// </remarks>
    internal static class InventoryQueryPatches
    {
        private static bool _warned;

        private static void Warn(Exception ex)
        {
            if (!_warned)
            {
                _warned = true;
                Plugin.Log.LogWarning($"Could not answer for a bottomless chest's contents: {ex}");
            }
        }

        private static int MinWorldLevel(bool match) => match ? Game.m_worldLevel : -1;

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.HaveItem), new[] { typeof(string), typeof(bool) })]
        private static class HaveItemPatch
        {
            private static bool Prefix(Inventory __instance, string name, bool matchWorldLevel, ref bool __result)
            {
                try
                {
                    if (!ChestContents.Resolve(__instance, out _, out var index))
                    {
                        return true;
                    }

                    __result = index != null
                        && index.Count(id => ChestContents.IsNamed(id, name), -1, MinWorldLevel(matchWorldLevel)) > 0;
                    return false;
                }
                catch (Exception ex)
                {
                    Warn(ex);
                    __result = false;
                    return false;
                }
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.CountItems), new[] { typeof(string), typeof(int), typeof(bool) })]
        private static class CountItemsPatch
        {
            private static bool Prefix(Inventory __instance, string name, int quality, bool matchWorldLevel, ref int __result)
            {
                try
                {
                    if (!ChestContents.Resolve(__instance, out _, out var index))
                    {
                        return true;
                    }

                    __result = index == null
                        ? 0
                        : index.Count(
                            id => name == null || ChestContents.IsNamed(id, name), quality, MinWorldLevel(matchWorldLevel));
                    return false;
                }
                catch (Exception ex)
                {
                    Warn(ex);
                    __result = 0;
                    return false;
                }
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetItem), new[] { typeof(string), typeof(int), typeof(bool) })]
        private static class GetItemPatch
        {
            private static bool Prefix(Inventory __instance, string name, int quality, bool isPrefabName, ref ItemDrop.ItemData __result)
            {
                try
                {
                    if (!ChestContents.Resolve(__instance, out _, out var index))
                    {
                        return true;
                    }

                    __result = null;
                    if (index == null)
                    {
                        return false;
                    }

                    foreach (var entry in index.Entries)
                    {
                        if (entry.Count > 0
                            && (quality < 0 || entry.Quality == quality)
                            && (isPrefabName ? entry.ItemId == name : ChestContents.IsNamed(entry.ItemId, name)))
                        {
                            __result = ChestContents.StandIn(entry);
                            break;
                        }
                    }

                    return false;
                }
                catch (Exception ex)
                {
                    Warn(ex);
                    __result = null;
                    return false;
                }
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), new[] { typeof(string), typeof(int), typeof(int), typeof(bool) })]
        private static class RemoveByNamePatch
        {
            private static bool Prefix(Inventory __instance, string name, int amount, int itemQuality, bool worldLevelBased)
            {
                try
                {
                    if (!ChestContents.Resolve(__instance, out var storeId, out var index))
                    {
                        return true;
                    }

                    ChestContents.Take(storeId, index, name, itemQuality, amount, MinWorldLevel(worldLevelBased));
                    return false;
                }
                catch (Exception ex)
                {
                    // Never let the original remove from a page: that is the free item.
                    Warn(ex);
                    return false;
                }
            }
        }
    }
}
```

- [ ] **Step 5: Build and run the full suite.** Expected: build succeeds; all tests pass.

- [ ] **Step 6: Commit** — "Answer V+'s contents questions about bottomless chests from their index".

---

### Task 3: One offer at a time, with reservations

**Files:**
- Create: `src/BottomlessChest.Logic/OfferQueue.cs`, `tests/BottomlessChest.Tests/OfferQueueTests.cs`
- Modify: `src/BottomlessChest/Net/ChestProtocol.cs`, `src/BottomlessChest/Net/ChestRpc.cs`, `src/BottomlessChest/Filter/ChestView.cs`, `src/BottomlessChest/Core/BottomlessContainer.cs`

**Interfaces:**
- Produces: `OfferQueue<TItem>(float timeoutSeconds)` with `Enqueue(string storeId, bool message)`, `TryNext(float now, out string storeId, out bool message)`, `Sent(string storeId, IReadOnlyList<TItem> items, bool message, float now)`, `Complete(string storeId, out IReadOnlyList<TItem> items, out bool message)`, `IsReserved(TItem)`, `Reserve(TItem)`, `Release(TItem)`, `Clear()`; `ChestView.RequestStackAll(string storeId, bool message)`; `ChestView.TickOffers()`; `ChestView.ApplyPutRefused()`; `ChestMessage.PutRefused = 18`.

- [ ] **Step 1: Write the failing tests** — `OfferQueueTests.cs`:

```csharp
using System.Collections.Generic;
using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    /// <summary>
    /// Place Stacks offers to several chests at once used to let two chests keep the same
    /// items. The queue sends one offer at a time and never sends an item twice.
    /// </summary>
    public class OfferQueueTests
    {
        private sealed class Item
        {
        }

        private static OfferQueue<Item> Queue() => new OfferQueue<Item>(5f);

        [Fact]
        public void TheFirstOfferGoesStraightOut()
        {
            var queue = Queue();
            queue.Enqueue("a", message: true);

            Assert.True(queue.TryNext(0f, out var store, out var message));
            Assert.Equal("a", store);
            Assert.True(message);
        }

        [Fact]
        public void ASecondChestWaitsForTheFirstReply()
        {
            var queue = Queue();
            queue.Enqueue("a", false);
            queue.Enqueue("b", false);
            queue.TryNext(0f, out _, out _);
            queue.Sent("a", new[] { new Item() }, false, 0f);

            Assert.False(queue.TryNext(1f, out _, out _));
        }

        [Fact]
        public void AReplyLetsTheNextChestGo()
        {
            var queue = Queue();
            queue.Enqueue("a", false);
            queue.Enqueue("b", false);
            queue.TryNext(0f, out _, out _);
            queue.Sent("a", new[] { new Item() }, false, 0f);

            Assert.True(queue.Complete("a", out _, out _));
            Assert.True(queue.TryNext(1f, out var store, out _));
            Assert.Equal("b", store);
        }

        [Fact]
        public void TheSameChestQueuedTwiceIsOneOfferAndKeepsAnyMessage()
        {
            var queue = Queue();
            queue.Enqueue("a", false);
            queue.Enqueue("a", true);

            Assert.True(queue.TryNext(0f, out _, out var message));
            Assert.True(message);
            Assert.False(queue.TryNext(0f, out _, out _));
        }

        [Fact]
        public void ItemsInFlightAreReservedUntilTheirReply()
        {
            var queue = Queue();
            var item = new Item();
            queue.Enqueue("a", false);
            queue.TryNext(0f, out _, out _);
            queue.Sent("a", new[] { item }, false, 0f);

            Assert.True(queue.IsReserved(item));
            Assert.True(queue.Complete("a", out var items, out _));
            Assert.Same(item, Assert.Single(items));
            Assert.False(queue.IsReserved(item));
        }

        [Fact]
        public void ATimeoutLetsTheNextChestGoButKeepsTheReservation()
        {
            var queue = Queue();
            var item = new Item();
            queue.Enqueue("a", false);
            queue.Enqueue("b", false);
            queue.TryNext(0f, out _, out _);
            queue.Sent("a", new[] { item }, false, 0f);

            Assert.True(queue.TryNext(6f, out var store, out _));
            Assert.Equal("b", store);
            Assert.True(queue.IsReserved(item));
        }

        [Fact]
        public void ALateReplyAfterATimeoutStillReleases()
        {
            var queue = Queue();
            var item = new Item();
            queue.Enqueue("a", false);
            queue.TryNext(0f, out _, out _);
            queue.Sent("a", new[] { item }, true, 0f);

            Assert.True(queue.Complete("a", out _, out var message));
            Assert.True(message);
            Assert.False(queue.IsReserved(item));
        }

        [Fact]
        public void AChestWithAnOfferInFlightIsNotOfferedAgainUntilItAnswers()
        {
            // The server names kept items by position in the offer, so two offers to one chest
            // would make the first answer name the wrong items.
            var queue = Queue();
            queue.Enqueue("a", false);
            queue.TryNext(0f, out _, out _);
            queue.Sent("a", new[] { new Item() }, false, 0f);
            queue.Enqueue("a", false);

            Assert.False(queue.TryNext(60f, out _, out _));
            queue.Complete("a", out _, out _);
            Assert.True(queue.TryNext(60f, out var store, out _));
            Assert.Equal("a", store);
        }

        [Fact]
        public void AReplyForAChestNotInFlightIsIgnored()
        {
            Assert.False(Queue().Complete("a", out var items, out _));
            Assert.Empty(items);
        }

        [Fact]
        public void APutReservationIsReportedUntilReleased()
        {
            var queue = Queue();
            var item = new Item();

            queue.Reserve(item);
            Assert.True(queue.IsReserved(item));
            queue.Release(item);
            Assert.False(queue.IsReserved(item));
        }

        [Fact]
        public void ClearingDropsQueuedOffersAndReservations()
        {
            var queue = Queue();
            var item = new Item();
            queue.Enqueue("a", false);
            queue.TryNext(0f, out _, out _);
            queue.Sent("a", new[] { item }, false, 0f);
            queue.Enqueue("b", false);

            queue.Clear();

            Assert.False(queue.IsReserved(item));
            Assert.False(queue.TryNext(0f, out _, out _));
        }
    }
}
```

- [ ] **Step 2: Run the tests; expect compile errors** (`OfferQueue` missing).

- [ ] **Step 3: Implement `OfferQueue`.**

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace BottomlessChest.Logic
{
    /// <summary>
    /// Sends a client's Place Stacks offers one at a time, and never offers an item twice.
    /// </summary>
    /// <remarks>
    /// A server keeps every offered item its chest already holds. Offers to two chests at once
    /// could both keep the same items, and a re-send after a slow reply could be kept twice.
    /// So an offer waits for the previous chest's answer; every item in flight stays reserved
    /// until its own answer; and a timeout only lets other chests and other items go ahead.
    /// </remarks>
    public sealed class OfferQueue<TItem> where TItem : class
    {
        private sealed class Waiting
        {
            internal string StoreId;
            internal bool Message;
        }

        private sealed class InFlight
        {
            internal IReadOnlyList<TItem> Items;
            internal bool Message;
            internal float SentAt;
        }

        private sealed class ByReference : IEqualityComparer<TItem>
        {
            public bool Equals(TItem x, TItem y) => ReferenceEquals(x, y);

            public int GetHashCode(TItem obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private readonly float _timeout;
        private readonly List<Waiting> _waiting = new List<Waiting>();
        private readonly Dictionary<string, InFlight> _inFlight = new Dictionary<string, InFlight>(StringComparer.Ordinal);
        private readonly Dictionary<TItem, int> _reserved = new Dictionary<TItem, int>(new ByReference());

        public OfferQueue(float timeoutSeconds)
        {
            _timeout = timeoutSeconds;
        }

        public void Enqueue(string storeId, bool message)
        {
            if (string.IsNullOrEmpty(storeId))
            {
                return;
            }

            foreach (var waiting in _waiting)
            {
                if (waiting.StoreId == storeId)
                {
                    waiting.Message |= message;
                    return;
                }
            }

            _waiting.Add(new Waiting { StoreId = storeId, Message = message });
        }

        /// <summary>The next chest to offer to, if nothing still in flight is within its timeout.</summary>
        public bool TryNext(float now, out string storeId, out bool message)
        {
            storeId = null;
            message = false;

            foreach (var flight in _inFlight.Values)
            {
                if (now - flight.SentAt < _timeout)
                {
                    return false;
                }
            }

            for (var i = 0; i < _waiting.Count; i++)
            {
                if (_inFlight.ContainsKey(_waiting[i].StoreId))
                {
                    continue;
                }

                storeId = _waiting[i].StoreId;
                message = _waiting[i].Message;
                _waiting.RemoveAt(i);
                return true;
            }

            return false;
        }

        public void Sent(string storeId, IReadOnlyList<TItem> items, bool message, float now)
        {
            var list = items ?? Array.Empty<TItem>();
            _inFlight[storeId] = new InFlight { Items = list, Message = message, SentAt = now };
            foreach (var item in list)
            {
                Reserve(item);
            }
        }

        /// <summary>The chest answered: hands back what was offered and releases it.</summary>
        public bool Complete(string storeId, out IReadOnlyList<TItem> items, out bool message)
        {
            items = Array.Empty<TItem>();
            message = false;

            if (storeId == null || !_inFlight.TryGetValue(storeId, out var flight))
            {
                return false;
            }

            _inFlight.Remove(storeId);
            foreach (var item in flight.Items)
            {
                Release(item);
            }

            items = flight.Items;
            message = flight.Message;
            return true;
        }

        public bool IsReserved(TItem item) => item != null && _reserved.ContainsKey(item);

        public void Reserve(TItem item)
        {
            if (item != null)
            {
                _reserved[item] = _reserved.TryGetValue(item, out var n) ? n + 1 : 1;
            }
        }

        public void Release(TItem item)
        {
            if (item == null || !_reserved.TryGetValue(item, out var n))
            {
                return;
            }

            if (n <= 1)
            {
                _reserved.Remove(item);
            }
            else
            {
                _reserved[item] = n - 1;
            }
        }

        public void Clear()
        {
            _waiting.Clear();
            _inFlight.Clear();
            _reserved.Clear();
        }
    }
}
```

- [ ] **Step 4: Run the tests; expect all to pass.**

- [ ] **Step 5: Protocol and server.**

`ChestProtocol`: after `DepositRefused = 17`:

```csharp
        /// <summary>Server declined an offered item. The client keeps it and may offer it again.</summary>
        PutRefused = 18,
```

Server `Put` handler: send `PutRefused` (storeId only) in all three declining branches - no session (currently a silent `break`), `RefusesToChange`, and an unreadable payload - before any page it already sends. Add a helper:

```csharp
        private static void SendPutRefused(long peer, string storeId)
        {
            var refused = new ZPackage();
            refused.Write((int)ChestMessage.PutRefused);
            refused.Write(storeId);
            _rpc.SendPackage(peer, refused);
        }
```

Server `StackAll` handler: when `ChestSessions.Acquire` returns null, send `Stacked` with a count of 0 (as the refusal branch does) before `break`.

Client handler: `case ChestMessage.PutRefused: Filter.ChestView.ApplyPutRefused(); break;`

- [ ] **Step 6: `ChestView` offers.** Replace `Offers` and `PendingOffer` with:

```csharp
        /// <summary>
        /// Place Stacks offers, one chest at a time, with every item in flight reserved.
        /// </summary>
        /// <remarks>
        /// Not cleared when the window closes: holding the use key stacks and closes the window a
        /// moment later, and a reply with nothing to act on duplicated items once. Cleared when
        /// the local player changes, which is what leaving a world looks like from here.
        /// </remarks>
        private static readonly Logic.OfferQueue<ItemDrop.ItemData> OfferQueue =
            new Logic.OfferQueue<ItemDrop.ItemData>(OfferTimeoutSeconds);

        private static Player _offerPlayer;
        private static int _offersPumpedFrame = -1;
```

Replace `RequestStackAll`:

```csharp
        /// <summary>
        /// Queues the player's stackable items to be offered to a chest, open or not.
        /// </summary>
        /// <param name="message">
        /// Vanilla's <c>StackAll</c> flag: true for the use-key hold, false for the button, and
        /// turned off by ValheimPlus when its sweep reports instead.
        /// </param>
        internal static bool RequestStackAll(string storeId, bool message)
        {
            if (string.IsNullOrEmpty(storeId) || Player.m_localPlayer == null)
            {
                return false;
            }

            OfferQueue.Enqueue(storeId, message);
            PumpOffers();
            return true;
        }

        /// <summary>Sends whatever offers are ready. Driven by every loaded chest's Update.</summary>
        internal static void TickOffers()
        {
            if (_offersPumpedFrame == Time.frameCount)
            {
                return;
            }

            _offersPumpedFrame = Time.frameCount;
            PumpOffers();
        }

        private static void PumpOffers()
        {
            var local = Player.m_localPlayer;
            if (!ReferenceEquals(local, _offerPlayer))
            {
                _offerPlayer = local;
                OfferQueue.Clear();
                _pendingPut = null;
                _pendingPutAmount = 0;
            }

            var player = local?.GetInventory();
            if (player == null)
            {
                return;
            }

            while (OfferQueue.TryNext(Time.realtimeSinceStartup, out var storeId, out var message))
            {
                var offered = new List<ItemDrop.ItemData>();
                foreach (var item in player.m_inventory)
                {
                    if (item.m_shared.m_maxStackSize > 1 && !item.m_equipped && !OfferQueue.IsReserved(item))
                    {
                        offered.Add(item);
                    }
                }

                Plugin.Log.LogDebug($"Offering {offered.Count} stackable item(s) to chest {storeId}.");

                if (offered.Count == 0)
                {
                    if (message)
                    {
                        local.Message(MessageHud.MessageType.Center, "$msg_stackall_none");
                    }

                    continue;
                }

                var scratch = new Inventory("offer", null, Width, 64);
                scratch.m_inventory.AddRange(offered);

                // Wrapped so the server reads it back with a direct add - see
                // ChestRpc.Serialize for why Inventory.AddItem is not safe for these.
                var payload = Storage.InventorySerializer.Save(scratch, "stack-all offer");
                if (payload == null)
                {
                    continue;
                }

                OfferQueue.Sent(storeId, offered, message, Time.realtimeSinceStartup);
                Net.ChestRpc.StackAll(storeId, payload);
            }
        }
```

Replace `ApplyStacked`:

```csharp
        /// <summary>Drops the items the given chest confirmed it kept, then sends the next offer.</summary>
        internal static void ApplyStacked(string storeId, List<int> keptIndices)
        {
            if (!OfferQueue.Complete(storeId, out var offered, out var message))
            {
                return;
            }

            var player = Player.m_localPlayer?.GetInventory();
            var moved = 0;
            if (player != null)
            {
                foreach (var index in keptIndices)
                {
                    if (index >= 0 && index < offered.Count)
                    {
                        moved += offered[index].m_stack;
                        player.RemoveItem(offered[index]);
                    }
                }
            }

            // Vanilla's message, and only where vanilla would show one.
            if (message && Player.m_localPlayer != null)
            {
                Player.m_localPlayer.Message(
                    MessageHud.MessageType.Center, moved > 0 ? $"$msg_stackall {moved}" : "$msg_stackall_none");
            }

            PumpOffers();
        }
```

- [ ] **Step 7: `ChestView` puts.** In `RequestPut`, replace the timed guard with:

```csharp
            // One put at a time, and no timeout. Accepted carries no identity, so a second put
            // sent before the first is answered would let the first answer remove the wrong item;
            // and re-sending an item whose answer was only slow is how both copies got kept. The
            // server now always answers, so only a disconnect leaves this set, and the player
            // keeps the item.
            if (_pendingPut != null || OfferQueue.IsReserved(item))
            {
                return false;
            }
```

After `_pendingPut = item;` add `OfferQueue.Reserve(item);`. In `ApplyAccepted`, call `OfferQueue.Release(_pendingPut);` before clearing it. Add:

```csharp
        /// <summary>The server declined the put, so the item stays and may be offered again.</summary>
        internal static void ApplyPutRefused()
        {
            if (_pendingPut != null)
            {
                OfferQueue.Release(_pendingPut);
            }

            _pendingPut = null;
            _pendingPutAmount = 0;
        }
```

Remove `_pendingPutSentAt`. Keep `OfferTimeoutSeconds` for the queue.

- [ ] **Step 8: Drive the queue.** In `BottomlessContainer.Update`, after the degraded check and before `RefreshIndex()`: `if (!SidecarStore.IsServerAuthority) { Filter.ChestView.TickOffers(); }`

- [ ] **Step 9: Build and run the full suite.** `StackAllPatch` and `StackAllButtonPatch` still call `RequestStackAll(storeId)`; make them pass `message: true` and `message: false` respectively so the build succeeds until Task 4 removes them.

- [ ] **Step 10: Commit** — "Send Place Stacks offers one at a time and never offer an item twice".

---

### Task 4: One client Place Stacks intercept, and sweep targeting

**Files:**
- Create: `src/BottomlessChest/Core/ClientStackAllPatch.cs`
- Delete: `src/BottomlessChest/Core/StackAllPatch.cs`, `src/BottomlessChest/Core/StackAllButtonPatch.cs`
- Modify: `src/BottomlessChest/Compat/ValheimPlusBridge.cs`, `src/BottomlessChest/Compat/ChestQueryPatches.cs`

**Interfaces:**
- Consumes: `ChestView.RequestStackAll(string, bool)` (Task 3); `BottomlessContainer.TryResolveInventory`, `ChestContents.Resolve`, `ChestContents.StandIn` (Task 2).
- Produces: `ChestQueryPatches.ContainsItemByNamePrefix(Inventory inventory, string name, ref bool __result)`; `ChestQueryPatches.ContainsItemByNameOriginal` (MethodInfo, set by the bridge).

- [ ] **Step 1: Create `ClientStackAllPatch`.**

```csharp
using BottomlessChest.Filter;
using BottomlessChest.Storage;
using HarmonyLib;

namespace BottomlessChest.Core
{
    /// <summary>
    /// Every Place Stacks into a bottomless chest on a client, from wherever it came.
    /// </summary>
    /// <remarks>
    /// Replaces two intercepts - the window's Stack button and the use-key hold's reply - that
    /// shared no code and between them hid the call ValheimPlus hooks to start its sweep. The
    /// button, the use-key hold and V+'s sweep all end in <c>Inventory.StackAll</c> on the
    /// chest's inventory, which on a client is a page or nothing; the offer goes to the server
    /// instead. V+'s own prefix and postfix still run, so its sweep starts from a bottomless
    /// chest too.
    ///
    /// After V+'s prefix, which turns <c>message</c> off when its sweep will report instead.
    /// When V+ skips the original because a sweep is already running, this still queues an offer;
    /// the queue makes that harmless.
    /// </remarks>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.StackAll))]
    [HarmonyAfter(Compat.ValheimPlusBridge.Guid)]
    internal static class ClientStackAllPatch
    {
        private static bool Prefix(Inventory __instance, Inventory fromInventory, bool message, ref int __result)
        {
            if (Plugin.Degraded || SidecarStore.IsServerAuthority || !InventoryCapacity.IsUnbounded(__instance))
            {
                return true;
            }

            var player = Player.m_localPlayer;
            if (player == null || !ReferenceEquals(fromInventory, player.GetInventory())
                || !BottomlessContainer.TryResolveInventory(__instance, out var chest))
            {
                return true;
            }

            __result = 0;
            ChestView.RequestStackAll(chest.CurrentStoreId, message);
            return false;
        }
    }
}
```

- [ ] **Step 2: Delete `StackAllPatch.cs` and `StackAllButtonPatch.cs`.** Build; expect success.

- [ ] **Step 3: Sweep-targeting prefix** — add to `ChestQueryPatches`:

```csharp
        /// <summary>V+'s own <c>Inventory_StackAll_Patch.ContainsItemByName</c>, set by the bridge.</summary>
        internal static System.Reflection.MethodInfo ContainsItemByNameOriginal;

        /// <summary>
        /// Prefix on V+'s <c>ContainsItemByName(Inventory, string)</c>: which chests its sweep asks.
        /// </summary>
        /// <remarks>
        /// V+ decides from the chest's inventory, which on a client is a page left over from the
        /// last time the chest was open, or nothing. Answered by running V+'s own method - its
        /// ignore-food, ammo, mead and equipment filters included - over stand-ins for the
        /// matching entries, in a scratch inventory this prefix does not intercept.
        /// </remarks>
        internal static bool ContainsItemByNamePrefix(Inventory inventory, string name, ref bool __result)
        {
            try
            {
                if (ContainsItemByNameOriginal == null || !ChestContents.Resolve(inventory, out _, out var index))
                {
                    return true;
                }

                __result = false;
                if (index == null)
                {
                    return false;
                }

                var scratch = new Inventory("sweep", null, 8, 64);
                foreach (var entry in index.Entries)
                {
                    if (entry.Count > 0 && ChestContents.IsNamed(entry.ItemId, name))
                    {
                        var standIn = ChestContents.StandIn(entry);
                        if (standIn != null)
                        {
                            scratch.m_inventory.Add(standIn);
                        }
                    }
                }

                __result = scratch.m_inventory.Count > 0
                    && (bool)ContainsItemByNameOriginal.Invoke(null, new object[] { scratch, name });
                return false;
            }
            catch (Exception ex)
            {
                if (!_warnedRead)
                {
                    _warnedRead = true;
                    Plugin.Log.LogWarning($"Could not answer ValheimPlus's sweep for a bottomless chest: {ex}");
                }

                return true;
            }
        }
```

- [ ] **Step 4: Bridge** — at the end of `Attach`, after the success log:

```csharp
            AttachSweepTargeting(harmony, assembly);
```

and:

```csharp
        /// <summary>
        /// Lets V+'s auto-stack sweep find bottomless chests by what they hold. Optional.
        /// </summary>
        /// <remarks>
        /// Outside the all-or-nothing set: without it the sweep only misjudges which chests to
        /// ask, which is what it did before, and nothing is counted or taken wrongly.
        /// </remarks>
        private static void AttachSweepTargeting(Harmony harmony, Assembly assembly)
        {
            var type = assembly.GetType("ValheimPlus.GameClasses.Inventory_StackAll_Patch", throwOnError: false);
            var method = type?.GetMethod(
                "ContainsItemByName", Statics, null, new[] { typeof(Inventory), typeof(string) }, null);

            if (method == null)
            {
                Plugin.Log.LogInfo("ValheimPlus auto-stack targeting hook not found; its sweep will judge bottomless chests from their pages.");
                return;
            }

            ChestQueryPatches.ContainsItemByNameOriginal = method;
            if (!TryPatch(harmony, new Hook(method, nameof(ChestQueryPatches.ContainsItemByNamePrefix), null, "auto-stack targeting")))
            {
                ChestQueryPatches.ContainsItemByNameOriginal = null;
            }
        }
```

- [ ] **Step 5: Build and run the full suite.** Expected: build succeeds; all tests pass; `grep -rn "StackAllButtonPatch\|StackAllPatch\b" src` finds nothing outside comments.

- [ ] **Step 6: Commit** — "Catch every client Place Stacks in one place, and target V+'s sweep by contents".

---

### Task 5: Verify

- [ ] **Step 1: Back up** the server store and CasualSolo stores; stop client and server gracefully; build (deploys to both); start the server; confirm "all 3 hooks attached" and no auto-stack targeting warning.
- [ ] **Step 2: In game (dedicated server, V+):**
  1. Two bottomless chests both holding wood; carry wood; press Place Stacks on a vanilla chest in range. Then on one of the bottomless chests. **Store check:** wood total across chests + player equals before.
  2. Hold the use key on a closed bottomless chest: one "$msg_stackall N" message, N in items.
  3. Smelter output goes to the chest already holding bars, consistently.
  4. Cooking station pulls raw meat from a bottomless chest; fermenter pulls a mead base and taps mead back.
  5. With no wood on you, refuel a campfire near a bottomless chest holding wood; the chest's wood drops by one per refuel after the next index refresh.
  6. Drag an item into a bottomless chest repeatedly while the server is busy: no duplicate.
  7. A server-run station at spawn pulling while you take (the B10 round 2 fix).
- [ ] **Step 3: Record results in `docs/RESUME.md`; the user runs `/code-review`.**

---

## Self-review

- **Spec coverage:** A → Task 2; B → Task 4; C → Task 3; world level and cheated → Task 1 (and stand-ins in Task 2); messages → Task 3 Step 6; always-answer server → Task 3 Step 5; accepted race → no task; verification → Task 5.
- **Placeholders:** none.
- **Types:** `RequestStackAll(string, bool)` defined in Task 3, used in Task 4; `ChestContents` defined in Task 2, used in Task 4; `IndexEntry` five-argument constructor defined in Task 1, used by `ChestContents.StandIn` via properties only.
