# Valheim 1.0 compatibility

**Date:** 2026-09-09, the day 1.0 (Deep North) shipped.
**Method:** `ilspycmd` decompile of `assembly_valheim.dll` and `assembly_utils.dll` from
both builds, diffed; plus a real compile of this mod against the 1.0 reference assemblies.
Everything under "Verified" was read out of the decompile or the compiler. Nothing here has
been observed running — the mod cannot load on 1.0 yet (see Blocked).

| | Build | Assembly |
|---|---|---|
| Old | 0.221.12 (buildid 21981590) | 2,119,680 bytes |
| New | 1.0 (buildid 25185596) | 2,566,144 bytes |

The dedicated server held the last copy of the old build. It has since updated to 1.0
(buildid 25185644), so the diff baseline now exists only at
`/mnt/c/valheim_mods/valheim-0.221-assemblies/`, which holds both assemblies and both
decompiles.

## Blocked: we cannot test on 1.0 yet

- **Jotunn** is a hard dependency and its newest release is 2.29.2 (July). No 1.0 build.
  Until one exists the plugin does not load at all, so nothing below can be confirmed in-game.
- **ValheimPlus** newest is 0.9.17.1 (February), targeting 0.221.10. Community reports say
  1.0 broke it badly — UI, camera and inventory. No 1.0 build.
- **The dedicated server is on 1.0** (buildid 25185644, updated 2026-09-09), so it is ready
  as soon as the client side can load.

This is launch-day state and should be re-checked before planning around it.

## Verified: what actually breaks

### 1. The build fails — all of it in `SidecarStore`

Five API changes, thirteen errors, every one in the file that decides where chest contents
live:

| Was | Is now |
|---|---|
| `World.m_fileName` | `World.m_worldName` (rename only) |
| `World.GetWorldSavePath(source)` | `World.GetSaveDirectory(source, worldName)` — **different meaning**, see below |
| `FileHelpers.CloudStorageEnabled` | `FileHelpers.CloudStorageSupportedAndEnabled` |
| `FileHelpers.LocalStorageSupported` | `LocalStorageFallbackSupported` / `LocalStorageSupportedAndAllowed` |
| `FileHelpers.ReplaceOldFile(save, new, old, source)` | gained a `CloudStorageFileGrouping grouping` parameter, a type in the **`Splatform`** assembly we do not reference |

`FileHelpers.OperationExceedsCloudCapacity` and `GetRemainingCloudCapacity` both survive —
the compiler accepted those call sites.

One behaviour change worth noting: in 0.221 `FileHelpers.CloudStorageSupported` was
hardcoded `false`. In 1.0 it is real (`PlatformManager.DistributionPlatform.SaveDataProvider
!= null`). The cloud-quota fallback logic has therefore never actually executed, and on 1.0
it will.

### 2. World saves moved into a per-world directory, and existing worlds are converted

```
1.0 chunked world:   worlds_local/<WorldName>/          (_main.*, *.chunk)
1.0 legacy world:    worlds_local/<WorldName>.db + .fwl
0.221 world:         worlds_local/<WorldName>.db + .fwl
```

`World.GetSavePaths()` branches on a private `m_chunkedSave`. `GetDBPath` and `GetMetaPath`
in 1.0 return **exactly** the old flat paths, so for a world carried over from 0.221 the
sidecar's current location is still correct.

**This is the one that can lose everyone's chests.** Porting `GetWorldSavePath` to
`GetSaveDirectory` looks like a rename and is not: it appends `/<worldName>/`. Do that
naively and every existing store becomes unreachable, the chest opens empty, and the next
save overwrites it. The read path must **grow** — try the world's actual layout, then fall
back to the flat path — never move.

**Resolved 2026-09-09, by accident — and it is the worse answer.** `CasualSolo` was opened
in vanilla 1.0 and converted immediately, on load, without a prompt:

```
worlds/CasualSolo/_main.1.fwl2  _main.1.db2  _main.1.chunks  _main.1.ok  *.chunk
worlds/CasualSolo_backup_20260909-124711.db + .fwl    <- pre-conversion, made by the game
worlds/CasualSolo.db, .fwl                            <- gone, consumed by the conversion
worlds/CasualSolo.bottomless.dat                      <- untouched, still in the parent
```

So **every existing world converts on its first 1.0 load**, and the flat legacy layout is
not the case we can rely on — it is the case that disappears. The sidecar is left behind in
the parent directory while the world moves into a subdirectory, which means:

- Reading must try the new per-world directory **and** the old flat location beside it.
- Writing should follow the world into its directory, and the old file should be migrated
  rather than orphaned — carefully, since a half-finished migration is worse than none.
- The world was a **cloud** save (`Steam/userdata/<id>/892970/remote/worlds/`), so
  `FileSource.Cloud` is the path that matters here, not `worlds_local`.

Valheim writes its own pre-conversion backup, which is what makes this recoverable.

**Loading a bottomless world without the mod is now materially more dangerous than it was.**
The prefab is unregistered, so chest ZDOs in any zone that loads can be dropped, and the
conversion then rewrites the whole save. The store file itself is never at risk — vanilla
does not know it exists, and it was verified intact afterwards — but the chests that point
at it can be. Do not open a bottomless world in an unmodded 1.0 client.

### 3. `Inventory.Save` now writes a `ushort` count

```csharp
// 1.0
pkg.Write(109);                          // was 106
pkg.Write((ushort)m_inventory.Count);    // was int
```

`BottomlessContainer.SaveToStore` serialises the store with `m_inventory.Save(package)`, so
on 1.0 **any chest above 65,535 stacks wraps its count and is silently truncated on the next
load.** The mod's headline claim is a million stacks. This is the most severe correctness
issue on the list and it is caused by vanilla, not by us — writing the count ourselves is
the likely fix.

Two consequences follow from the same change:

- `BottomlessContainer.PeekItemCount` reads version-as-int then count-as-int. Against a
  1.0 package it reads two count bytes plus two bytes of the first item and returns garbage,
  so the grid is sized wrong before load — and per constraint #3 `AddItem` silently drops
  whatever does not fit.
- `FastInventoryReader` hard-checks `SupportedVersion == 106` and falls back otherwise. That
  is correct and safe: 1.0 stores simply take the slow vanilla path. It does mean the fast
  loader stops working the moment a store is rewritten on 1.0 — a performance cliff on
  exactly the chests that need it, not a data risk.

### 4. Old stores still load — upgrade safety holds

1.0's `Inventory.Load` reads the version and sends anything below `Version.Item.Smaller`
(108) to `LoadOld`, which handles 106 correctly. **Existing 0.1.0 sidecar stores remain
readable.** This is the single most important finding for the standing rule, and it means
the upgrade path is achievable rather than merely hoped for.

### 5. One Harmony patch target renamed

Of the 21 methods we patch, 20 still exist unchanged. One does not:

- `Container.RPC_TakeAllRespons` → `Container.RPC_TakeAllResponse` (typo fixed)

`Container.RPC_OpenRespons` → `RPC_OpenResponse` the same way; we do not patch that one.

### 6. `ItemDrop.ItemData` gained `m_cheated`

Item format 109 is `ChunksNCheats`. 1.0 tracks whether an item was spawned with cheats
(`Inventory.AnyCheatedItem`, `ItemCheated`, `CheatedDamagingItemEquipped` are all new). Two
things follow: our own item construction should carry the flag honestly rather than clear
it, and the test world is full of spawned items that will now be flagged.

### 7. Smaller renames that touch our UI code

`InventoryGrid.Element` → `InventoryElement` (now a top-level class), and
`InventoryGrid.SetSelection` split into `SetGamepadSelection` / `SetTouchSelection`. Whether
we reference either has not been checked — the build stopped at `SidecarStore`, so **there
may be more breakage behind these thirteen errors.** Expect a second wave once storage
compiles.

## The work

Ordered by what unblocks what. Nothing here is started.

1. ~~**Preserve the baseline.**~~ **Done 2026-09-09.** The 0.221 assemblies and both
   decompiles are at `/mnt/c/valheim_mods/valheim-0.221-assemblies/`.
2. ~~**Answer the layout question.**~~ **Answered 2026-09-09** — see section 2. Every world
   converts on first load, and the sidecar is left behind in the parent directory.
   `CasualSolo`'s pre-conversion backup and its intact store are preserved at
   `/mnt/c/valheim_mods/backup-2026-09-09-pre1.0-recovery/`.
3. ~~**Make it compile.**~~ **Done.** Six API changes in the end — `FileWriter`'s constructor
   also takes a grouping. Storage paths deliberately excluded; `SaveSystem.GetWorldsSaveRootPath`
   reproduces the old path exactly, so nothing moved.
4. ~~**Store path resolution, read-path-grows.**~~ **Done.** `StoreLocations` in
   `BottomlessChest.Logic`, 16 tests. Reads try the per-world directory then the flat
   location, in both the world's own storage and the local fallback; a candidate that will
   not read falls through, so a half-written file cannot hide a good one. Writes follow the
   layout `World.GetSavePaths()` reports, defaulting to Flat when unsure — the safe way to
   be wrong, since the read candidates cover it. The old file is left in place as a backup.
   The standing rule is pinned as a test: every path 0.1.0 read is still searched.
5. ~~**Fix the count.**~~ **Done.** The stack count is written by this mod; the items stay
   the game's, byte for byte. `InventoryPayload` handles all three header shapes and is
   covered by 18 tests. Every store write goes through `InventorySerializer.Save`, which
   reads its own output back before returning it and refuses if it disagrees.
6. ~~**Teach the reader format 109.**~~ **Done, by not hand-parsing it.** Items are read
   back through the game's own `ItemData.Load`, which keeps this correct across item
   formats we know nothing about - `m_cheated` included - and avoids the per-stack
   `Instantiate` that made the fast reader necessary in the first place. Format 106 is
   still read by hand for existing stores.
7. ~~**Rename the patch target.**~~ **Done** — `RPC_TakeAllResponse`.
8. ~~**Second-wave build breakage.**~~ **None.** `InventoryGrid.Element` and `SetSelection`
   were both renamed and we reference neither.
9. **Verify on 1.0** once Jotunn ships a build: the existing in-game checklist, plus an
   explicit upgrade test from a 0.1.0 store.

## What this does to the rest of the plan

The craft-from-chest branch is ValheimPlus compatibility work, and ValheimPlus does not run
on 1.0. Finishing it now means writing to an interface (`InventoryAssistant`) that its
author is about to rebuild, against a mod nobody can currently load. It should be parked at
Task 1 rather than abandoned — the design and the index logic stay valid — and picked up
when V+ ships a 1.0 build.

The 0.2.0 release plan changes shape too: the published 0.1.0 is broken for anyone who
updates to 1.0, which makes a compatibility release more urgent than a feature release, and
makes the game version an explicit part of the release notes for the first time.

## What stands between here and shipping 0.2.0

Everything below is blocked on Jotunn, which is the honest summary: the code is written and
none of it has run.

- [ ] **Jotunn needs a 1.0 build.** PR #483 was opened the day 1.0 shipped by a community
      contributor and is unreviewed. The manifest still pins `ValheimModding-Jotunn-2.29.2`
      and must be bumped before publishing — the mod cannot load without it, so shipping
      against the old pin would produce a release that does nothing.
- [ ] **Nothing has been verified in a game.** Not the path resolution, not the new store
      format, not the renamed RPC patch. The first run is the whole test plan, and the new
      format means the first run also writes a store no earlier build can read.
- [ ] **`dump-store.py` cannot decode 1.0 items yet.** It reads the framing and the count,
      which is what matters for spotting a truncated chest, but the per-item encoding is a
      bitfield keyed by prefab hash and needs the game's ObjectDB to resolve names. Worth
      writing against a real 109 store once one exists, rather than guessing at it.
- [ ] **Upgrade test, specifically:** open a world holding a 0.1.0 store, let 1.0 convert
      it, confirm the chest still opens full and that `dump-store.py` shows the same totals.
- [ ] **Tag the release commit.** 0.1.0 shipped untagged and had to be reconstructed
      afterwards by matching a zip's mtime against commit times.
- [ ] **One run at default log levels** to confirm `ContentsWatch` disarms and costs nothing.

Done and waiting: version bumped to 0.2.0, changelog written, testing commands gated behind
config plus devcommands, `.pdb` confirmed absent from both the package and the deploys.
