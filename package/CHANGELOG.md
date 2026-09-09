# Changelog

## 0.2.0

**Requires Valheim 1.0.** This release does not work on 0.221 or earlier — 1.0 renamed a
method the mod patches, and the storage APIs it uses moved. Stay on 0.1.0 if you are still
on an older game version.

Upgrading from 0.1.0 does not lose chest contents. Existing stores are read exactly where
they have always been, and 1.0's new save layout is handled without moving anything out
from under an existing chest.

### Valheim 1.0 support

- Chest stores are found whether or not 1.0 has converted your world. 1.0 gives each world
  its own directory the first time it is opened and leaves the store behind in the parent;
  both places are searched, and the old file is left where it is as a backup.
- Rebuilt against 1.0's storage and save APIs.

### Chests larger than 65,535 stacks

Valheim 1.0 records a container's stack count in a field that stops at 65,535. Left alone,
a chest holding more than that would have been saved with a wrapped count and lost the
remainder on the next load, silently and permanently.

Chest contents are now stored with the stack count written by this mod rather than by the
game, so there is no ceiling. Items themselves are still stored in the game's own format
and read back by the game's own code, so nothing about how an item is recorded has changed.

If a chest ever does serialise into something that does not read back as what went in, it
refuses to save and says so, leaving the stored contents alone.

**Stores written by 0.2.0 cannot be read by 0.1.0.** Upgrading is safe; going back is not.

### Other

- `bottomless fill` and `bottomless empty` are off by default. They are testing tools, and
  `empty` discards a chest's contents outright, so they now need `EnableTestingCommands` in
  the Testing section of the config **and** `devcommands`.
- Items added directly into a bottomless chest by another mod are refused on a dedicated
  server client rather than accepted and discarded. ValheimPlus smelters, kilns and
  furnaces drop their output on the ground instead of destroying it.

### Dedicated servers

Storage, paging, search, Take All and Stack All work as they did in 0.1.0.

ValheimPlus crafting and station auto-pull still do **not** see bottomless chest contents on
a dedicated server. They read an empty inventory and take nothing, so no materials are lost;
they simply behave as though the chest were not there. Fixing that is designed and started,
and waits on ValheimPlus having a 1.0 build of its own.

## 0.1.0

First release.

- Buildable chest with unlimited storage and a typed search filter.
- Category tokens (`@food`, `@weapon`, ...) and name-sorted results.
- Take All and Stack respect the active search.
- Dedicated server support: contents are held server-side and paged to clients, so chest
  size does not affect network cost.
- Contents stored beside the world save rather than in the world file, and survive a
  destroyed chest, a crashed client or a failed mod load.
- Chests holding items cannot be dismantled, and never spill their contents on destruction.
