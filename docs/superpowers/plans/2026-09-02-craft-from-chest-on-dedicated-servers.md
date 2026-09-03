# Craft From Chest On Dedicated Servers — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make ValheimPlus crafting, building, repair and station auto-pull see and consume bottomless chest contents on a dedicated server, where a client today holds only a page.

**Architecture:** The server sends each nearby client a per-chest *index* — one entry per (prefab name, quality) carrying the true total — which is bounded by item variety rather than stack count, so paging is preserved. Three ValheimPlus `InventoryAssistant` methods are patched by reflection: one read method that every requirement check funnels through, and two removal overloads that every consumption path funnels through. Removals answer V+ synchronously from the index and are then sent to the server, which is free to remove less than asked but never more.

**Tech Stack:** C# against .NET Framework 4.6.2 / Unity 2022.3, BepInEx 5.4.2333, HarmonyX, Jotunn 2.29.2 (`CustomRPC`), xUnit for the pure-logic assembly. Build with `"/mnt/c/Program Files/dotnet/dotnet.exe"`.

**Spec:** `docs/superpowers/specs/2026-09-02-craft-from-chest-on-dedicated-servers-design.md`

## Global Constraints

These are project invariants from `docs/` and the project plan. Every task inherits them.

- **Never patch `Inventory.GetAllItems`, `Inventory.GetHeight`, or `Inventory.Changed`.** Mono inlines them; the patch applies and never runs. Confirmed twice.
- **Contents must never enter the ZDO.** The index is transient client state, never persisted, never written to a ZDO.
- **A client never receives whole chest contents.** The index is per (prefab name, quality), never per stack.
- **Never save an entry that was never read, or read only partially.**
- **Patch failure must degrade, never throw.** Every patch attachment and every patch body is individually guarded; a failure logs once and leaves that path at its current behaviour.
- **No hard assembly reference to ValheimPlus.** Bind by reflection; absent V+ means no patches and no cost.
- **The server may remove less than asked, never more.** This is the one-directional invariant that makes the optimistic write acceptable.
- **State argument types explicitly on every `HarmonyPatch` of an overloaded method.** An `AmbiguousMatchException` during patching once destroyed a live chest.
- Build: `"/mnt/c/Program Files/dotnet/dotnet.exe" build src/BottomlessChest/BottomlessChest.csproj -c Release`
- Test: `"/mnt/c/Program Files/dotnet/dotnet.exe" test tests/BottomlessChest.Tests/BottomlessChest.Tests.csproj`
- Commit trailers on every commit:
  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01UsnnVf949UHMBchnCv6Vfy
  ```

## File Structure

| File | Responsibility |
|---|---|
| `src/BottomlessChest.Logic/ChestIndex.cs` (create) | Pure index: build from items, count, take. No Unity types. |
| `tests/BottomlessChest.Tests/ChestIndexTests.cs` (create) | Tests for the above. |
| `src/BottomlessChest/Net/ChestProtocol.cs` (modify) | Two new message kinds. |
| `src/BottomlessChest/Net/ChestRpc.cs` (modify) | Serialize/send index; handle authoritative removal. |
| `src/BottomlessChest/Core/ChestIndexCache.cs` (create) | Client-side indexes by store id, with version and staleness. |
| `src/BottomlessChest/Compat/ValheimPlusBridge.cs` (create) | Reflection binding to V+ `InventoryAssistant`; attaches patches or degrades. |
| `src/BottomlessChest/Compat/ChestQueryPatches.cs` (create) | The read postfix and the two removal prefixes. |
| `src/BottomlessChest/Core/ClientDepositGuard.cs` (modify) | Forward deposits instead of refusing them, once the index exists. |

Tasks 1–2 are pure logic and fully unit-tested. Tasks 3–8 are Unity- and network-coupled; their verification is the dedicated-server checklist in Task 9, and the plan says so rather than pretending otherwise.

---

### Task 1: The index — construction and counting

**Files:**
- Create: `src/BottomlessChest.Logic/ChestIndex.cs`
- Test: `tests/BottomlessChest.Tests/ChestIndexTests.cs`

**Interfaces:**
- Consumes: `IStorableItem` (existing, `src/BottomlessChest.Logic/IStorableItem.cs`) — provides `ItemId`, `Quality`, `Stack`.
- Produces: `ChestIndex.From(long version, IEnumerable<IStorableItem> items) -> ChestIndex`; `ChestIndex.Version -> long`; `ChestIndex.Entries -> IReadOnlyList<IndexEntry>`; `ChestIndex.CountOf(string itemId, int quality) -> int`; `readonly struct IndexEntry { string ItemId; int Quality; int Count; }`.

- [ ] **Step 1: Write the failing test**

Create `tests/BottomlessChest.Tests/ChestIndexTests.cs`:

```csharp
using System.Linq;
using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    public class ChestIndexTests
    {
        private static TestItem Item(string id, int stack, int quality = 1) =>
            new TestItem(id, ItemKind.Material, stack, quality: quality, itemId: id, maxStackSize: 50);

        [Fact]
        public void StacksOfOneItemBecomeOneEntryCarryingTheTotal()
        {
            var index = ChestIndex.From(7, new[] { Item("Wood", 55), Item("Wood", 50), Item("Wood", 48) });

            var entry = Assert.Single(index.Entries);
            Assert.Equal("Wood", entry.ItemId);
            Assert.Equal(153, entry.Count);
        }

        [Fact]
        public void TheIndexIsBoundedByVarietyNotByStackCount()
        {
            // The whole reason this can exist without breaking paging.
            var manyStacks = Enumerable.Range(0, 5000).Select(_ => Item("Wood", 50));

            Assert.Single(ChestIndex.From(1, manyStacks).Entries);
        }

        [Fact]
        public void DifferentQualitiesStaySeparate()
        {
            // Merging them would let a repair claim materials it cannot use.
            var index = ChestIndex.From(1, new[] { Item("SwordIron", 1, quality: 1), Item("SwordIron", 1, quality: 3) });

            Assert.Equal(2, index.Entries.Count);
            Assert.Equal(1, index.CountOf("SwordIron", 1));
            Assert.Equal(1, index.CountOf("SwordIron", 3));
        }

        [Fact]
        public void NegativeQualityMeansAnyQuality()
        {
            // ValheimPlus passes quality -1 when it does not care, and expects the sum.
            var index = ChestIndex.From(1, new[] { Item("SwordIron", 1, quality: 1), Item("SwordIron", 1, quality: 3) });

            Assert.Equal(2, index.CountOf("SwordIron", -1));
        }

        [Fact]
        public void AnItemNotHeldCountsZero()
        {
            Assert.Equal(0, ChestIndex.From(1, new[] { Item("Wood", 10) }).CountOf("Stone", -1));
        }

        [Fact]
        public void TheVersionIsCarried()
        {
            Assert.Equal(42, ChestIndex.From(42, new[] { Item("Wood", 1) }).Version);
        }

        [Fact]
        public void AnEmptyChestIndexesToNothing()
        {
            Assert.Empty(ChestIndex.From(1, new IStorableItem[0]).Entries);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `"/mnt/c/Program Files/dotnet/dotnet.exe" test tests/BottomlessChest.Tests/BottomlessChest.Tests.csproj`
Expected: FAIL — `error CS0103: The name 'ChestIndex' does not exist in the current context`.

- [ ] **Step 3: Write minimal implementation**

Create `src/BottomlessChest.Logic/ChestIndex.cs`:

```csharp
using System.Collections.Generic;

namespace BottomlessChest.Logic
{
    /// <summary>One item type and quality, and how much of it a chest holds.</summary>
    public readonly struct IndexEntry
    {
        public IndexEntry(string itemId, int quality, int count)
        {
            ItemId = itemId;
            Quality = quality;
            Count = count;
        }

        /// <summary>Stable prefab name, so the server and client agree on what was asked for.</summary>
        public string ItemId { get; }

        public int Quality { get; }

        public int Count { get; }
    }

    /// <summary>
    /// What a chest holds, summarised so a client can answer questions about it without
    /// holding it.
    /// </summary>
    /// <remarks>
    /// One entry per item type and quality, not per stack. A chest of a million stacks
    /// spanning forty item types indexes to forty entries, which is what lets another mod
    /// query a bottomless chest on a dedicated server without breaking paging.
    ///
    /// Quality is kept distinct because it is not interchangeable: a repair cannot use
    /// materials of the wrong quality, and merging the two would let it claim it can.
    /// </remarks>
    public sealed class ChestIndex
    {
        private readonly List<IndexEntry> _entries;

        private ChestIndex(long version, List<IndexEntry> entries)
        {
            Version = version;
            _entries = entries;
        }

        /// <summary>The chest version this summary was taken at, for staleness checks.</summary>
        public long Version { get; }

        public IReadOnlyList<IndexEntry> Entries => _entries;

        public static ChestIndex From(long version, IEnumerable<IStorableItem> items)
        {
            var entries = new List<IndexEntry>();
            var seen = new Dictionary<string, int>(System.StringComparer.Ordinal);

            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item?.ItemId == null)
                    {
                        continue;
                    }

                    var key = item.ItemId + "/" + item.Quality;

                    if (seen.TryGetValue(key, out var at))
                    {
                        entries[at] = new IndexEntry(
                            entries[at].ItemId, entries[at].Quality, entries[at].Count + item.Stack);
                    }
                    else
                    {
                        seen[key] = entries.Count;
                        entries.Add(new IndexEntry(item.ItemId, item.Quality, item.Stack));
                    }
                }
            }

            return new ChestIndex(version, entries);
        }

        /// <summary>How much of an item is held. A negative quality means any quality.</summary>
        public int CountOf(string itemId, int quality)
        {
            if (itemId == null)
            {
                return 0;
            }

            var total = 0;
            foreach (var entry in _entries)
            {
                if (entry.ItemId == itemId && (quality < 0 || entry.Quality == quality))
                {
                    total += entry.Count;
                }
            }

            return total;
        }

    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `"/mnt/c/Program Files/dotnet/dotnet.exe" test tests/BottomlessChest.Tests/BottomlessChest.Tests.csproj`
Expected: PASS, total count 107.

- [ ] **Step 5: Commit**

```bash
git add src/BottomlessChest.Logic/ChestIndex.cs tests/BottomlessChest.Tests/ChestIndexTests.cs
git commit -m "$(cat <<'EOF'
Summarise a chest as totals per item type

One entry per item type and quality rather than per stack, so a client can answer
questions about a chest it does not hold. A million stacks across forty item types
is forty entries, which is what keeps paging intact.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01UsnnVf949UHMBchnCv6Vfy
EOF
)"
```

---

### Task 2: Taking from the index

**Files:**
- Modify: `src/BottomlessChest.Logic/ChestIndex.cs`
- Test: `tests/BottomlessChest.Tests/ChestIndexTests.cs`

**Interfaces:**
- Consumes: `ChestIndex`, `IndexEntry` from Task 1.
- Produces: `ChestIndex.Take(string itemId, int quality, int amount) -> int` — decrements in place and returns how much was actually taken, never more than held and never more than asked.

- [ ] **Step 1: Write the failing test**

Append inside the `ChestIndexTests` class in `tests/BottomlessChest.Tests/ChestIndexTests.cs`:

```csharp
        [Fact]
        public void TakingLessThanHeldReturnsWhatWasAsked()
        {
            var index = ChestIndex.From(1, new[] { Item("Wood", 100) });

            Assert.Equal(30, index.Take("Wood", -1, 30));
            Assert.Equal(70, index.CountOf("Wood", -1));
        }

        [Fact]
        public void TakingMoreThanHeldReturnsOnlyWhatWasThere()
        {
            // The invariant the optimistic write rests on: never claim more than exists.
            var index = ChestIndex.From(1, new[] { Item("Wood", 10) });

            Assert.Equal(10, index.Take("Wood", -1, 999));
            Assert.Equal(0, index.CountOf("Wood", -1));
        }

        [Fact]
        public void TakingWhatIsNotHeldTakesNothing()
        {
            var index = ChestIndex.From(1, new[] { Item("Wood", 10) });

            Assert.Equal(0, index.Take("Stone", -1, 5));
            Assert.Equal(10, index.CountOf("Wood", -1));
        }

        [Fact]
        public void TakingSpreadsAcrossQualitiesWhenQualityIsNotSpecified()
        {
            var index = ChestIndex.From(1, new[] { Item("SwordIron", 2, quality: 1), Item("SwordIron", 2, quality: 3) });

            Assert.Equal(3, index.Take("SwordIron", -1, 3));
            Assert.Equal(1, index.CountOf("SwordIron", -1));
        }

        [Fact]
        public void TakingARequestedQualityLeavesTheOthersAlone()
        {
            var index = ChestIndex.From(1, new[] { Item("SwordIron", 2, quality: 1), Item("SwordIron", 2, quality: 3) });

            Assert.Equal(2, index.Take("SwordIron", 3, 5));
            Assert.Equal(2, index.CountOf("SwordIron", 1));
            Assert.Equal(0, index.CountOf("SwordIron", 3));
        }

        [Fact]
        public void TakingZeroOrLessTakesNothing()
        {
            var index = ChestIndex.From(1, new[] { Item("Wood", 10) });

            Assert.Equal(0, index.Take("Wood", -1, 0));
            Assert.Equal(0, index.Take("Wood", -1, -5));
            Assert.Equal(10, index.CountOf("Wood", -1));
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `"/mnt/c/Program Files/dotnet/dotnet.exe" test tests/BottomlessChest.Tests/BottomlessChest.Tests.csproj`
Expected: FAIL — `'ChestIndex' does not contain a definition for 'Take'`.

- [ ] **Step 3: Write minimal implementation**

In `src/BottomlessChest.Logic/ChestIndex.cs`, add to the `ChestIndex` class after `CountOf`:

```csharp
        /// <summary>
        /// Deducts up to <paramref name="amount"/> and reports how much was actually taken.
        /// </summary>
        /// <remarks>
        /// Never returns more than the index held. The caller answers another mod with this
        /// number synchronously and only afterwards asks the server to make it true, so a
        /// figure larger than the chest can honour would overdraw it. Erring low costs the
        /// player a craft; erring high costs the chest.
        /// </remarks>
        public int Take(string itemId, int quality, int amount)
        {
            if (itemId == null || amount <= 0)
            {
                return 0;
            }

            var taken = 0;
            for (var i = 0; i < _entries.Count && taken < amount; i++)
            {
                var entry = _entries[i];
                if (entry.ItemId != itemId || (quality >= 0 && entry.Quality != quality))
                {
                    continue;
                }

                var from = entry.Count < amount - taken ? entry.Count : amount - taken;
                _entries[i] = new IndexEntry(entry.ItemId, entry.Quality, entry.Count - from);
                taken += from;
            }

            return taken;
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `"/mnt/c/Program Files/dotnet/dotnet.exe" test tests/BottomlessChest.Tests/BottomlessChest.Tests.csproj`
Expected: PASS, total count 113.

- [ ] **Step 5: Commit**

```bash
git add src/BottomlessChest.Logic/ChestIndex.cs tests/BottomlessChest.Tests/ChestIndexTests.cs
git commit -m "$(cat <<'EOF'
Deduct from the index, never more than it holds

Take reports what was actually available, not what was asked for. The caller
answers another mod with this number synchronously and only then asks the server
to make it true, so a figure larger than the chest can honour would overdraw it.
Erring low costs a craft; erring high costs the chest.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01UsnnVf949UHMBchnCv6Vfy
EOF
)"
```

---

### Task 3: Client-side cache of indexes

**Files:**
- Create: `src/BottomlessChest/Core/ChestIndexCache.cs`
- Test: `tests/BottomlessChest.Tests/ChestIndexCacheTests.cs`

**Interfaces:**
- Consumes: `ChestIndex` from Tasks 1–2.
- Produces: `ChestIndexCache.Put(string storeId, ChestIndex index)`; `ChestIndexCache.TryGet(string storeId, out ChestIndex index) -> bool`; `ChestIndexCache.Forget(string storeId)`; `ChestIndexCache.Clear()`.

The cache is a plain dictionary with no Unity types, so it lives in the mod assembly but is unit-testable via the test project's existing reference. Keep it free of `UnityEngine` imports.

- [ ] **Step 1: Write the failing test**

Create `tests/BottomlessChest.Tests/ChestIndexCacheTests.cs`:

```csharp
using BottomlessChest.Logic;
using Xunit;

namespace BottomlessChest.Tests
{
    public class ChestIndexCacheTests
    {
        private static ChestIndex Index(long version, int wood) =>
            ChestIndex.From(version, new[]
            {
                new TestItem("Wood", ItemKind.Material, wood, itemId: "Wood", maxStackSize: 50)
            });

        [Fact]
        public void AnUnknownChestHasNoIndex()
        {
            var cache = new ChestIndexCache();

            Assert.False(cache.TryGet("nope", out _));
        }

        [Fact]
        public void AStoredIndexComesBack()
        {
            var cache = new ChestIndexCache();
            cache.Put("chest-a", Index(1, 50));

            Assert.True(cache.TryGet("chest-a", out var index));
            Assert.Equal(50, index.CountOf("Wood", -1));
        }

        [Fact]
        public void ANewerIndexReplacesAnOlderOne()
        {
            var cache = new ChestIndexCache();
            cache.Put("chest-a", Index(1, 50));
            cache.Put("chest-a", Index(2, 20));

            cache.TryGet("chest-a", out var index);
            Assert.Equal(20, index.CountOf("Wood", -1));
        }

        [Fact]
        public void AnOlderIndexIsIgnored()
        {
            // Replies can arrive out of order; a late old snapshot must not undo a new one.
            var cache = new ChestIndexCache();
            cache.Put("chest-a", Index(5, 20));
            cache.Put("chest-a", Index(2, 999));

            cache.TryGet("chest-a", out var index);
            Assert.Equal(20, index.CountOf("Wood", -1));
        }

        [Fact]
        public void ChestsAreKeptApart()
        {
            var cache = new ChestIndexCache();
            cache.Put("chest-a", Index(1, 50));
            cache.Put("chest-b", Index(1, 7));

            cache.TryGet("chest-b", out var b);
            Assert.Equal(7, b.CountOf("Wood", -1));
        }

        [Fact]
        public void ForgettingRemovesOnlyThatChest()
        {
            var cache = new ChestIndexCache();
            cache.Put("chest-a", Index(1, 50));
            cache.Put("chest-b", Index(1, 7));

            cache.Forget("chest-a");

            Assert.False(cache.TryGet("chest-a", out _));
            Assert.True(cache.TryGet("chest-b", out _));
        }

        [Fact]
        public void ClearingEmptiesEverything()
        {
            var cache = new ChestIndexCache();
            cache.Put("chest-a", Index(1, 50));

            cache.Clear();

            Assert.False(cache.TryGet("chest-a", out _));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `"/mnt/c/Program Files/dotnet/dotnet.exe" test tests/BottomlessChest.Tests/BottomlessChest.Tests.csproj`
Expected: FAIL — `The name 'ChestIndexCache' does not exist in the current context`.

- [ ] **Step 3: Write minimal implementation**

Create `src/BottomlessChest/Core/ChestIndexCache.cs`:

```csharp
using System.Collections.Generic;
using BottomlessChest.Logic;

namespace BottomlessChest.Core
{
    /// <summary>
    /// The most recent summary this client has of each chest it can see.
    /// </summary>
    /// <remarks>
    /// Transient by design. Nothing here is persisted, written to a ZDO, or drawn - it
    /// exists only so another mod's questions about a server-held chest can be answered
    /// without shipping the chest.
    ///
    /// Deliberately free of UnityEngine types so it can be tested without the game.
    /// </remarks>
    public sealed class ChestIndexCache
    {
        private readonly Dictionary<string, ChestIndex> _byStore =
            new Dictionary<string, ChestIndex>(System.StringComparer.Ordinal);

        /// <summary>
        /// Records a summary, unless an equally fresh or fresher one is already held.
        /// </summary>
        /// <remarks>
        /// Replies can arrive out of order. A late snapshot from an older version would
        /// otherwise resurrect quantities the chest no longer has, which is the direction
        /// that overdraws it.
        /// </remarks>
        public void Put(string storeId, ChestIndex index)
        {
            if (string.IsNullOrEmpty(storeId) || index == null)
            {
                return;
            }

            if (_byStore.TryGetValue(storeId, out var held) && held.Version > index.Version)
            {
                return;
            }

            _byStore[storeId] = index;
        }

        public bool TryGet(string storeId, out ChestIndex index)
        {
            if (string.IsNullOrEmpty(storeId))
            {
                index = null;
                return false;
            }

            return _byStore.TryGetValue(storeId, out index);
        }

        public void Forget(string storeId)
        {
            if (!string.IsNullOrEmpty(storeId))
            {
                _byStore.Remove(storeId);
            }
        }

        public void Clear() => _byStore.Clear();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `"/mnt/c/Program Files/dotnet/dotnet.exe" test tests/BottomlessChest.Tests/BottomlessChest.Tests.csproj`
Expected: PASS, total count 120.

- [ ] **Step 5: Commit**

```bash
git add src/BottomlessChest/Core/ChestIndexCache.cs tests/BottomlessChest.Tests/ChestIndexCacheTests.cs
git commit -m "$(cat <<'EOF'
Hold the latest summary of each chest a client can see

Replies arrive out of order, so an older snapshot is dropped rather than applied:
resurrecting quantities the chest no longer has is the direction that overdraws it.

Transient by design - never persisted, never in a ZDO, never drawn.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01UsnnVf949UHMBchnCv6Vfy
EOF
)"
```

---

### Task 4: Carry the index over the wire

**Files:**
- Modify: `src/BottomlessChest/Net/ChestProtocol.cs`
- Modify: `src/BottomlessChest/Net/ChestRpc.cs`
- Modify: `src/BottomlessChest/Core/ChestSession.cs`

**Interfaces:**
- Consumes: `ChestIndex.From` (Task 1), `ChestIndexCache.Put` (Task 3), existing `ChestSessions.Acquire(storeId)`, existing `Filter.ItemAdapter`.
- Produces: `ChestMessage.IndexRequest = 13`, `ChestMessage.IndexResult = 14`; `ChestRpc.RequestIndex(string storeId)`; `ChestRpc.Indexes -> ChestIndexCache` (the client's cache instance).

Message numbering appends only. Existing values must not shift — an older client and a newer server otherwise disagree about what a number means.

- [ ] **Step 1: Add the message kinds**

In `src/BottomlessChest/Net/ChestProtocol.cs`, add after `Counts = 12,`:

```csharp
        /// <summary>Client asks for a summary of a chest it is near but has not opened.</summary>
        IndexRequest = 13,

        /// <summary>Server returns totals per item type, so another mod can query the chest.</summary>
        IndexResult = 14,
```

- [ ] **Step 2: Add the client request and cache**

In `src/BottomlessChest/Net/ChestRpc.cs`, add alongside the other client senders (near `internal static void Open`):

```csharp
        /// <summary>The latest summary of each chest this client can see. Empty on a host.</summary>
        internal static readonly Core.ChestIndexCache Indexes = new Core.ChestIndexCache();

        /// <summary>Asks the server to summarise a chest we are near but have not opened.</summary>
        internal static void RequestIndex(string storeId)
        {
            if (string.IsNullOrEmpty(storeId))
            {
                return;
            }

            var package = new ZPackage();
            package.Write((int)ChestMessage.IndexRequest);
            package.Write(storeId);
            ToServer(package);
        }
```

- [ ] **Step 3: Handle the request on the server**

In `OnServerReceive`, add a case alongside `ChestMessage.Open`:

```csharp
                case ChestMessage.IndexRequest:
                {
                    var session = ChestSessions.Acquire(storeId);
                    if (session == null)
                    {
                        break;
                    }

                    var index = Logic.ChestIndex.From(
                        session.Version,
                        session.Inventory.m_inventory.ConvertAll(
                            item => (Logic.IStorableItem)new Filter.ItemAdapter(item)));

                    var reply = new ZPackage();
                    reply.Write((int)ChestMessage.IndexResult);
                    reply.Write(storeId);
                    reply.Write(index.Version);
                    reply.Write(index.Entries.Count);
                    foreach (var entry in index.Entries)
                    {
                        reply.Write(entry.ItemId);
                        reply.Write(entry.Quality);
                        reply.Write(entry.Count);
                    }

                    _rpc.SendPackage(sender, reply);
                    break;
                }
```

- [ ] **Step 4: Handle the reply on the client**

In `OnClientReceive`, add a case alongside `ChestMessage.Counts`:

```csharp
                case ChestMessage.IndexResult:
                {
                    var version = package.ReadLong();
                    var count = package.ReadInt();
                    var items = new List<Logic.IStorableItem>(count);
                    for (var i = 0; i < count; i++)
                    {
                        var itemId = package.ReadString();
                        var quality = package.ReadInt();
                        var held = package.ReadInt();
                        items.Add(new Logic.IndexedItem(itemId, quality, held));
                    }

                    Indexes.Put(storeId, Logic.ChestIndex.From(version, items));
                    break;
                }
```

- [ ] **Step 5: Add the tiny carrier type the reply needs**

Create `src/BottomlessChest.Logic/IndexedItem.cs`:

```csharp
namespace BottomlessChest.Logic
{
    /// <summary>
    /// An index entry wearing the item interface, so a received summary can be rebuilt
    /// through the same <see cref="ChestIndex.From"/> path the server used to build it.
    /// </summary>
    /// <remarks>
    /// Only the three fields an index needs are real. The rest satisfy the interface and
    /// are never read: an index answers "how much", never "which one".
    /// </remarks>
    public sealed class IndexedItem : IStorableItem
    {
        public IndexedItem(string itemId, int quality, int stack)
        {
            ItemId = itemId;
            Quality = quality;
            Stack = stack;
        }

        public string ItemId { get; }

        public int Quality { get; }

        public int Stack { get; }

        public string DisplayName => ItemId;

        public string SearchKey => TextKey.Of(ItemId);

        public ItemKind Kind => ItemKind.Unknown;

        public int MaxStackSize => int.MaxValue;

        public int Variant => 0;

        public string CustomData => null;
    }
}
```

- [ ] **Step 6: Build and run the suite**

Run: `"/mnt/c/Program Files/dotnet/dotnet.exe" build src/BottomlessChest/BottomlessChest.csproj -c Release`
Expected: `Build succeeded`, 0 errors. Then run the test suite; expected PASS at 120 — this task adds no tests, and none should break.

- [ ] **Step 7: Commit**

```bash
git add src/BottomlessChest/Net/ChestProtocol.cs src/BottomlessChest/Net/ChestRpc.cs src/BottomlessChest.Logic/IndexedItem.cs
git commit -m "$(cat <<'EOF'
Send a chest summary to clients near it

Two appended message kinds: a client asks for a summary of a chest it has not
opened, and the server answers with totals per item type and the version they were
taken at. Numbering appends only - shifting an existing value would make an older
client and a newer server disagree about what a number means.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01UsnnVf949UHMBchnCv6Vfy
EOF
)"
```

---

### Task 5: Bind to ValheimPlus without depending on it

**Files:**
- Create: `src/BottomlessChest/Compat/ValheimPlusBridge.cs`
- Modify: `src/BottomlessChest/Plugin.cs` (call `ValheimPlusBridge.Attach(harmony)` from `Awake`, **after** prefab registration)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `ValheimPlusBridge.Attach(Harmony harmony)`; `ValheimPlusBridge.Available -> bool`.

- [ ] **Step 1: Write the bridge**

Create `src/BottomlessChest/Compat/ValheimPlusBridge.cs`:

```csharp
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace BottomlessChest.Compat
{
    /// <summary>
    /// Attaches our ValheimPlus patches, if ValheimPlus is here at all.
    /// </summary>
    /// <remarks>
    /// Bound by reflection rather than an assembly reference: a hard reference would make
    /// ValheimPlus a requirement for everyone, and this is a compatibility fix, not a
    /// dependency.
    ///
    /// Every resolution is separate and every failure is survivable. If ValheimPlus
    /// refactors one of these methods away, that path returns to its previous behaviour -
    /// a bottomless chest that ValheimPlus cannot see - which is exactly where the mod was
    /// before this feature existed. A compatibility shim must never be able to take the
    /// chest down with it.
    /// </remarks>
    internal static class ValheimPlusBridge
    {
        private const string AssemblyName = "ValheimPlus";
        private const string AssistantTypeName = "ValheimPlus.GameClasses.InventoryAssistant";

        internal static bool Available { get; private set; }

        /// <summary>The V+ method that lists every item in a set of chests.</summary>
        internal static MethodInfo GetNearbyChestItems { get; private set; }

        /// <summary>The two removal overloads every consumption path funnels through.</summary>
        internal static MethodInfo RemoveByItem { get; private set; }

        internal static MethodInfo RemoveByName { get; private set; }

        internal static void Attach(Harmony harmony)
        {
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == AssemblyName);

                if (assembly == null)
                {
                    Plugin.Log.LogInfo("ValheimPlus not present; its chest integration is not needed.");
                    return;
                }

                var assistant = assembly.GetType(AssistantTypeName, throwOnError: false);
                if (assistant == null)
                {
                    Plugin.Log.LogWarning(
                        $"ValheimPlus is present but '{AssistantTypeName}' was not found. " +
                        "Crafting from bottomless chests will not work on a dedicated server.");
                    return;
                }

                GetNearbyChestItems = assistant.GetMethod(
                    "GetNearbyChestItemsByContainerList",
                    BindingFlags.Public | BindingFlags.Static);

                RemoveByItem = assistant.GetMethod(
                    "RemoveItemFromChest",
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(Container), typeof(ItemDrop.ItemData), typeof(int) },
                    modifiers: null);

                RemoveByName = assistant.GetMethod(
                    "RemoveItemFromChest",
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(Container), typeof(string), typeof(int) },
                    modifiers: null);

                Available = GetNearbyChestItems != null && RemoveByItem != null && RemoveByName != null;

                if (!Available)
                {
                    Plugin.Log.LogWarning(
                        "ValheimPlus is present but its chest helpers have changed shape " +
                        $"(read: {GetNearbyChestItems != null}, remove-by-item: {RemoveByItem != null}, " +
                        $"remove-by-name: {RemoveByName != null}). Leaving those paths alone.");
                    return;
                }

                harmony.PatchAll(typeof(ChestQueryPatches));
                Plugin.Log.LogInfo("ValheimPlus chest integration attached.");
            }
            catch (Exception ex)
            {
                Available = false;
                Plugin.Log.LogWarning($"Could not attach ValheimPlus chest integration: {ex.Message}");
            }
        }
    }
}
```

- [ ] **Step 2: Call it from the plugin**

In `src/BottomlessChest/Plugin.cs`, inside `Awake`, **after** the prefab registration block and inside its own `try`/`catch`, add:

```csharp
            try
            {
                Compat.ValheimPlusBridge.Attach(harmony);
            }
            catch (Exception ex)
            {
                Log.LogWarning($"ValheimPlus integration could not start: {ex.Message}");
            }
```

Order matters: registering the prefab first is constraint #1 in the project plan. An exception before registration once made Valheim delete every ZDO referencing an unregistered prefab.

- [ ] **Step 3: Build**

Run: `"/mnt/c/Program Files/dotnet/dotnet.exe" build src/BottomlessChest/BottomlessChest.csproj -c Release`
Expected: FAIL — `The name 'ChestQueryPatches' does not exist`. That type arrives in Task 6; this step confirms the bridge compiles apart from its one forward reference.

- [ ] **Step 4: Commit after Task 6**

This task and Task 6 land together, because the bridge names a type Task 6 creates. Do not commit yet.

---

### Task 6: The read patch — let V+ see the chest

**Files:**
- Create: `src/BottomlessChest/Compat/ChestQueryPatches.cs`

**Interfaces:**
- Consumes: `ValheimPlusBridge.GetNearbyChestItems` (Task 5), `ChestRpc.Indexes` (Task 4), `BottomlessContainer.TryResolve` (existing), `Storage.ItemTemplates.For` (existing), `SidecarStore.IsServerAuthority` (existing).
- Produces: `ChestQueryPatches` — a Harmony patch class attached by the bridge.

- [ ] **Step 1: Write the read patch**

Create `src/BottomlessChest/Compat/ChestQueryPatches.cs`:

```csharp
using System.Collections.Generic;
using BottomlessChest.Core;
using BottomlessChest.Storage;
using HarmonyLib;

namespace BottomlessChest.Compat
{
    /// <summary>
    /// Answers ValheimPlus's questions about bottomless chests on a dedicated server.
    /// </summary>
    /// <remarks>
    /// On a dedicated server a client holds only the page it is looking at, so ValheimPlus
    /// finds an empty chest and silently does nothing - no crafting from it, no station
    /// pulling fuel or ore from it. It reaches chests through three InventoryAssistant
    /// methods, one read and two removal overloads, so answering those three from the synced
    /// index covers building, crafting, repair and every station.
    ///
    /// Only on a client. Where we are the authority the real inventory is already correct
    /// and ValheimPlus should see it untouched.
    /// </remarks>
    internal static class ChestQueryPatches
    {
        [HarmonyPatch]
        private static class ListItems
        {
            private static System.Reflection.MethodBase TargetMethod() =>
                ValheimPlusBridge.GetNearbyChestItems;

            private static void Postfix(List<Container> nearbyChests, List<ItemDrop.ItemData> __result)
            {
                if (Plugin.Degraded || SidecarStore.IsServerAuthority
                    || nearbyChests == null || __result == null)
                {
                    return;
                }

                try
                {
                    foreach (var container in nearbyChests)
                    {
                        if (container == null
                            || !BottomlessContainer.TryResolve(container, out var bottomless))
                        {
                            continue;
                        }

                        var storeId = bottomless.CurrentStoreId;
                        if (string.IsNullOrEmpty(storeId)
                            || !Net.ChestRpc.Indexes.TryGet(storeId, out var index))
                        {
                            continue;
                        }

                        foreach (var entry in index.Entries)
                        {
                            if (entry.Count <= 0)
                            {
                                continue;
                            }

                            // Through ItemTemplates so the stand-in carries the shared data
                            // ItemDrop.Awake would have given it - stack size, weight,
                            // teleportability - which other mods may have adjusted.
                            var template = ItemTemplates.For(entry.ItemId);
                            if (template == null)
                            {
                                continue;
                            }

                            var stub = template.Clone();
                            stub.m_stack = entry.Count;
                            stub.m_quality = entry.Quality;
                            __result.Add(stub);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    // A miscounted requirement is a wrong answer; a thrown one is a broken game.
                    Plugin.Log.LogWarning($"Could not describe a bottomless chest to ValheimPlus: {ex.Message}");
                }
            }
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `"/mnt/c/Program Files/dotnet/dotnet.exe" build src/BottomlessChest/BottomlessChest.csproj -c Release`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 3: Run the suite**

Run: `"/mnt/c/Program Files/dotnet/dotnet.exe" test tests/BottomlessChest.Tests/BottomlessChest.Tests.csproj`
Expected: PASS at 120. No new tests: this is Harmony and Unity wiring, verified in Task 9 on a real server. Do not add a test that only asserts the patch class exists.

- [ ] **Step 4: Commit Tasks 5 and 6 together**

```bash
git add src/BottomlessChest/Compat/ src/BottomlessChest/Plugin.cs
git commit -m "$(cat <<'EOF'
Let ValheimPlus see a bottomless chest from a client

ValheimPlus reads every chest through one InventoryAssistant method, so a postfix
on it - appending stand-in items built from the synced index - answers every
requirement check, repair check and station threshold at once.

Bound by reflection. A hard reference would make ValheimPlus a requirement for
everyone, and each helper resolves separately so a refactor on their side costs
that one path and nothing else. The chest must never be able to go down with a
compatibility shim.

Stand-ins are cloned through ItemTemplates so they carry the shared data
ItemDrop.Awake would have given them, which is what another mod's stack size or
weight changes live on.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01UsnnVf949UHMBchnCv6Vfy
EOF
)"
```

---

### Task 7: The write patches — let V+ consume from the chest

**Files:**
- Modify: `src/BottomlessChest/Compat/ChestQueryPatches.cs`
- Modify: `src/BottomlessChest/Net/ChestProtocol.cs`
- Modify: `src/BottomlessChest/Net/ChestRpc.cs`

**Interfaces:**
- Consumes: `ChestIndex.Take` (Task 2), `ValheimPlusBridge.RemoveByItem` / `RemoveByName` (Task 5).
- Produces: `ChestMessage.TakeByName = 15`; `ChestRpc.TakeByName(string storeId, long version, string itemId, int quality, int amount)`.

- [ ] **Step 1: Add the message kind**

In `src/BottomlessChest/Net/ChestProtocol.cs`, after `IndexResult = 14,`:

```csharp
        /// <summary>Client reports a removal another mod made against the index.</summary>
        TakeByName = 15,
```

- [ ] **Step 2: Add the client sender**

In `src/BottomlessChest/Net/ChestRpc.cs`, beside `RequestIndex`:

```csharp
        /// <summary>
        /// Tells the server another mod consumed from this chest, quoting the version acted on.
        /// </summary>
        internal static void TakeByName(string storeId, long version, string itemId, int quality, int amount)
        {
            if (string.IsNullOrEmpty(storeId) || string.IsNullOrEmpty(itemId) || amount <= 0)
            {
                return;
            }

            var package = new ZPackage();
            package.Write((int)ChestMessage.TakeByName);
            package.Write(storeId);
            package.Write(version);
            package.Write(itemId);
            package.Write(quality);
            package.Write(amount);
            ToServer(package);
        }
```

- [ ] **Step 3: Handle it on the server**

In `OnServerReceive`:

```csharp
                case ChestMessage.TakeByName:
                {
                    var version = package.ReadLong();
                    var itemId = package.ReadString();
                    var quality = package.ReadInt();
                    var amount = package.ReadInt();

                    var session = ChestSessions.Acquire(storeId);
                    if (session == null)
                    {
                        break;
                    }

                    // Remove what is actually there, never what was claimed. The client has
                    // already answered the other mod; the chest is what must not be overdrawn.
                    session.RemoveByName(itemId, quality, amount);

                    var refreshed = Logic.ChestIndex.From(
                        session.Version,
                        session.Inventory.m_inventory.ConvertAll(
                            item => (Logic.IStorableItem)new Filter.ItemAdapter(item)));

                    var reply = new ZPackage();
                    reply.Write((int)ChestMessage.IndexResult);
                    reply.Write(storeId);
                    reply.Write(refreshed.Version);
                    reply.Write(refreshed.Entries.Count);
                    foreach (var entry in refreshed.Entries)
                    {
                        reply.Write(entry.ItemId);
                        reply.Write(entry.Quality);
                        reply.Write(entry.Count);
                    }

                    _rpc.SendPackage(sender, reply);
                    break;
                }
```

The `version` read is deliberately not used to refuse the operation: the client has already answered ValheimPlus and the craft has happened. It is read so the wire format carries it and a future policy can act on it, and the fresh index that follows is what actually reconciles the two sides.

- [ ] **Step 4: Add the server-side removal to `ChestSession`**

In `src/BottomlessChest/Core/ChestSession.cs`, add a method that removes by prefab name and quality, bumping the version exactly as the existing mutators do. Follow the pattern of the existing take path in that file — read it first and match it, including how it marks the order dirty and bumps `Version`.

```csharp
        /// <summary>
        /// Removes up to <paramref name="amount"/> of an item, and reports what it took.
        /// </summary>
        /// <remarks>
        /// Takes from the smallest stacks first so the chest tends toward fewer, fuller
        /// stacks rather than a field of remainders.
        /// </remarks>
        internal int RemoveByName(string itemId, int quality, int amount)
        {
            if (string.IsNullOrEmpty(itemId) || amount <= 0)
            {
                return 0;
            }

            var items = Inventory.m_inventory;
            var taken = 0;

            for (var i = items.Count - 1; i >= 0 && taken < amount; i--)
            {
                var item = items[i];
                if (item?.m_dropPrefab == null
                    || item.m_dropPrefab.name != itemId
                    || (quality >= 0 && item.m_quality != quality))
                {
                    continue;
                }

                var from = item.m_stack < amount - taken ? item.m_stack : amount - taken;
                item.m_stack -= from;
                taken += from;

                if (item.m_stack <= 0)
                {
                    items.RemoveAt(i);
                }
            }

            if (taken > 0)
            {
                Touch();
            }

            return taken;
        }
```

`Touch()` is the existing mutator on `ChestSession` (line 75): it bumps `Version` and marks both the cached order and the cached weight dirty. Every other mutator in that file calls it, and so must this one — a removal that does not bump `Version` leaves clients acting on a snapshot the server has already moved past.

- [ ] **Step 5: Add the removal prefixes**

In `src/BottomlessChest/Compat/ChestQueryPatches.cs`, add inside `ChestQueryPatches`:

```csharp
        /// <summary>
        /// Shared body for both removal overloads.
        /// </summary>
        /// <remarks>
        /// Answers from the index immediately, because ValheimPlus expects an int back and
        /// cannot wait for a round trip, then tells the server. The index came from the
        /// server one round trip ago and Take never reports more than it holds, so the error
        /// can only fall the player's way - an occasional unpaid craft, never an overdrawn
        /// chest.
        /// </remarks>
        private static bool TryTake(Container chest, string sharedName, int quality, int amount, out int taken)
        {
            taken = 0;

            if (Plugin.Degraded || SidecarStore.IsServerAuthority || chest == null || amount <= 0)
            {
                return false;
            }

            if (!BottomlessContainer.TryResolve(chest, out var bottomless))
            {
                return false;
            }

            var storeId = bottomless.CurrentStoreId;
            if (string.IsNullOrEmpty(storeId) || !Net.ChestRpc.Indexes.TryGet(storeId, out var index))
            {
                return false;
            }

            // ValheimPlus matches on the localized shared name; the index is keyed by prefab
            // name because that is what the server stores. Resolve one to the other through
            // the same templates the stand-ins are built from.
            foreach (var entry in index.Entries)
            {
                if (entry.Count <= 0)
                {
                    continue;
                }

                var template = ItemTemplates.For(entry.ItemId);
                if (template == null || template.m_shared.m_name != sharedName)
                {
                    continue;
                }

                var got = index.Take(entry.ItemId, quality, amount - taken);
                if (got > 0)
                {
                    Net.ChestRpc.TakeByName(storeId, index.Version, entry.ItemId, quality, got);
                    taken += got;
                }

                if (taken >= amount)
                {
                    break;
                }
            }

            return true;
        }

        [HarmonyPatch]
        private static class RemoveByItem
        {
            private static System.Reflection.MethodBase TargetMethod() => ValheimPlusBridge.RemoveByItem;

            private static bool Prefix(Container chest, ItemDrop.ItemData needle, int amount, ref int __result)
            {
                try
                {
                    if (needle?.m_shared == null
                        || !TryTake(chest, needle.m_shared.m_name, needle.m_quality, amount, out var taken))
                    {
                        return true;
                    }

                    __result = taken;
                    return false;
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning($"Could not take from a bottomless chest for ValheimPlus: {ex.Message}");
                    return true;
                }
            }
        }

        [HarmonyPatch]
        private static class RemoveByName
        {
            private static System.Reflection.MethodBase TargetMethod() => ValheimPlusBridge.RemoveByName;

            private static bool Prefix(Container chest, string needle, int amount, ref int __result)
            {
                try
                {
                    if (!TryTake(chest, needle, -1, amount, out var taken))
                    {
                        return true;
                    }

                    __result = taken;
                    return false;
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning($"Could not take from a bottomless chest for ValheimPlus: {ex.Message}");
                    return true;
                }
            }
        }
```

Returning `true` from a prefix on failure runs the original, which against an empty client-side inventory removes nothing — the safe fallback.

- [ ] **Step 6: Build and run the suite**

Run the build, then the test suite.
Expected: `Build succeeded`; tests PASS at 120.

- [ ] **Step 7: Commit**

```bash
git add src/BottomlessChest/Compat/ChestQueryPatches.cs src/BottomlessChest/Net/ src/BottomlessChest/Core/ChestSession.cs
git commit -m "$(cat <<'EOF'
Let ValheimPlus consume from a chest held on the server

Both removal overloads answer from the synced index immediately, because
ValheimPlus expects an int back and cannot wait for a round trip, and then tell
the server what was taken. The server removes what it actually has.

Take never reports more than the index holds, so the error can only fall the
player's way: an occasional unpaid craft, never an overdrawn chest. That
one-directional invariant is what makes an optimistic write acceptable here, and
it must stay one-directional.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01UsnnVf949UHMBchnCv6Vfy
EOF
)"
```

---

### Task 8: Keep the index fresh, and forward deposits

**Files:**
- Modify: `src/BottomlessChest/Core/BottomlessContainer.cs`
- Modify: `src/BottomlessChest/Core/ClientDepositGuard.cs`

**Interfaces:**
- Consumes: `ChestRpc.RequestIndex` (Task 4), `ChestRpc.Indexes` (Task 4).
- Produces: no new public surface.

- [ ] **Step 1: Request an index while a client is near a chest**

In `BottomlessContainer.Update` — which already exists and already early-returns cheaply — add index refresh beside the contents watch. Use a per-instance `float _nextIndexAt` and the same one-second cadence the watch uses:

```csharp
            if (!Storage.SidecarStore.IsServerAuthority
                && Net.ChestRpc.Ready
                && UnityEngine.Time.unscaledTime >= _nextIndexAt)
            {
                // ValheimPlus reaches up to 50m, the ceiling it clamps every range to, so a
                // chest must be indexed before the player is close enough to craft from it.
                _nextIndexAt = UnityEngine.Time.unscaledTime + 1f;

                var player = Player.m_localPlayer;
                if (player != null
                    && UnityEngine.Vector3.Distance(player.transform.position, transform.position) <= 50f)
                {
                    Net.ChestRpc.RequestIndex(CurrentStoreId);
                }
            }
```

Declare `private float _nextIndexAt;` beside the other instance fields.

- [ ] **Step 2: Forget the index when the chest goes**

In `BottomlessContainer.OnDestroy`, before `Registry.Remove(_container)`:

```csharp
            var storeId = CurrentStoreId;
            if (!string.IsNullOrEmpty(storeId))
            {
                Net.ChestRpc.Indexes.Forget(storeId);
            }
```

- [ ] **Step 3: Forward deposits instead of refusing them**

`ClientDepositGuard` currently refuses an external `AddItem` so the caller keeps the item rather than losing it. With an authoritative path available, forward instead. Replace the body of `Prefix` after the guard clauses:

```csharp
                var container = __instance.m_onChanged != null ? FindOwner(__instance) : null;
                var storeId = container?.CurrentStoreId;

                if (string.IsNullOrEmpty(storeId) || !Net.ChestRpc.Ready)
                {
                    // No way to hand it over: refuse, so the caller keeps it and drops it on
                    // the ground rather than handing it to an inventory that discards it.
                    __result = false;
                    return false;
                }

                Net.ChestRpc.Put(storeId, Net.ChestRpc.Serialize(new List<ItemDrop.ItemData> { item }));
                __result = true;
                return false;
```

Add the parameter `ItemDrop.ItemData item` to the `Prefix` signature so Harmony supplies it, and add this helper to `ClientDepositGuard`:

```csharp
        /// <summary>The bottomless chest that owns an inventory, or null.</summary>
        private static BottomlessContainer FindOwner(Inventory inventory)
        {
            foreach (var candidate in BottomlessContainer.Loaded)
            {
                if (ReferenceEquals(candidate.Inventory, inventory))
                {
                    return candidate;
                }
            }

            return null;
        }
```

Drop the `__instance.m_onChanged != null` test from the snippet above and call `FindOwner(__instance)` directly — it was a guess at a cheap pre-filter and it is not one.

`ChestRpc.Put(string storeId, byte[] itemBytes)` is already `internal` (line 79). `ChestRpc.Serialize(List<ItemDrop.ItemData>)` is **private** (line 405): widen it to `internal` rather than duplicating it, since it wraps the items in a scratch `Inventory` and saves that, which is the exact format the server's `Put` handler expects.

**Keep the refusal as the fallback.** It is the behaviour that stopped items being destroyed, and it must remain reachable whenever forwarding is not possible.

- [ ] **Step 4: Build and run the suite**

Expected: `Build succeeded`; tests PASS at 120.

- [ ] **Step 5: Commit**

```bash
git add src/BottomlessChest/Core/BottomlessContainer.cs src/BottomlessChest/Core/ClientDepositGuard.cs
git commit -m "$(cat <<'EOF'
Keep the index fresh and hand deposits to the server

A client asks for a summary of any bottomless chest within 50m - the ceiling
ValheimPlus clamps every range to - once a second, and forgets it when the chest
goes.

With an authoritative path available, an external deposit is forwarded rather than
refused. The refusal stays as the fallback: it is what stopped station output
being destroyed, and it must remain reachable whenever forwarding is not possible.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01UsnnVf949UHMBchnCv6Vfy
EOF
)"
```

---

### Task 9: Verify on a dedicated server

Nothing above is proven until this runs. The unit tests cover the index arithmetic; everything else is Harmony, Unity and networking, and the file read outside the game is the only honest witness.

**Setup:**

```bash
scripts/server-setup.sh          # mirror BepInEx + Jotunn + the build into the server
scripts/server-start.bat         # 127.0.0.1:2456, password devpassword
scripts/server-status.sh         # confirms the mod loaded and dumps the stores
```

Restore realistic ValheimPlus settings first — `valheim_plus.cfg.pretest-backup` holds the pre-testing values. Testing against 60-second timers and 50m ranges proves nothing about a real install.

- [ ] **Step 1: Baseline.** Join the server, place a bottomless chest, put 200 wood and 50 stone in it. Quit. Run `scripts/dump-store.py` on the server save and record exact totals.

- [ ] **Step 2: Build from the chest.** Rejoin, empty your own inventory of wood, stand within 20m of the chest, place a wooden structure. Expect it to place. Quit and dump: wood must be down by exactly the piece cost, stone unchanged.

- [ ] **Step 3: Requirement display.** With no materials on you, open the build menu near the chest. Pieces the chest can pay for must show as buildable. This is the read patch, and it is the one that fails silently if the index is not arriving.

- [ ] **Step 4: Station pull.** Place a charcoal kiln within 10m. Expect `Added N ores($item_wood) in $piece_charcoalkiln` in the server or client log, and wood falling in the store. This path was broken on dedicated servers before this work and is fixed by the same patches.

- [ ] **Step 5: Station deposit.** Let the kiln finish a conversion. Coal must arrive in the chest and appear in the store file — not on the ground, which was the Task 8 fallback, and not nowhere, which was the bug before `ClientDepositGuard`.

- [ ] **Step 6: Two chests.** Place a second bottomless chest, wood in both, one kiln in range of both. Confirm the draw comes from both and totals across the two fall by exactly what the kiln consumed.

- [ ] **Step 7: Conservation under interruption.** During a long build run, kill the client process. Restart, rejoin, dump. The store may be short by at most one operation and must never be short by more, and must never be *over*.

- [ ] **Step 8: No ValheimPlus.** Remove ValheimPlus from the server and client profiles, rejoin, open and use a chest normally. Expect the log line "ValheimPlus not present" and no behaviour change. Then restore it.

- [ ] **Step 9: Upgrade safety.** Install the published 0.1.0 in a clean profile, make a chest with known contents, then upgrade in place to this build and confirm the contents are intact. This is the standing release rule and applies to every version.

- [ ] **Step 10: Record results in the plan and fix what failed.** Do not mark this plan complete on a partial pass.

---

## Notes for whoever executes this

- **The store file is the witness.** `scripts/dump-store.py` read outside the game has settled every incident in this project's history. A screenshot of a chest has settled none.
- **`scripts/merge-store.py` is safe again** as of `e24dd5e`, but read its docstring before using it for recovery.
- **Debug logging** must be on in the profile's `BepInEx.cfg` (`LogLevels` needs `Debug`) for `ContentsWatch` lines to appear, and off for release.
- **A `CLOSED - not by us` watch line is not a bug** — small exact deltas are ValheimPlus stations doing their job. See `docs/valheimplus.md`.
