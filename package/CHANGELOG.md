# Changelog

## 0.4.0

**Craft-from-chest mods can use a bottomless chest**, not only ValheimPlus. Tested with
NearbyCrafting.

**Everyone on a server needs 0.4.0**, including the server; a 0.3.x client cannot join a 0.4.0
server. Chests are stored exactly as before, so upgrading and going back are both safe.

- Any mod that asks a chest what it holds the way the game does can now count a bottomless
  chest's contents, pay from it for crafting and building, and deposit into it.
- A chest asks the server for its totals only while something is asking about it, so a chest
  nobody queries costs nothing. Before, that depended on having ValheimPlus installed.
- **Fixed: items could be destroyed by a mod depositing into a closed bottomless chest.** The
  item left your inventory and went into your own machine's copy of a chest whose contents live
  on the server, and that copy is thrown away. This could happen on 0.3.0 and earlier too.
- **Depositing into a chest you do not have open now works**, so quick-deposit hotkeys such as
  NearbyCrafting's put items into bottomless chests. Each item stays with you until the server
  confirms it arrived.
- A mod that counts what it moved by reading your inventory straight afterwards may report
  nothing deposited: the items leave a moment later, when the server answers. They do arrive.

## 0.3.0

**Crafting, building and stations can use a bottomless chest on a dedicated server**, with
ValheimPlus. Place Stacks on a large chest is no longer slow. Scrolling, the window's size and
gamepad support are fixed, and two ways items could be lost or duplicated are closed.

**Everyone on a server needs 0.3.0**, including the server. A 0.2.x client cannot join a 0.3.0
server. Chests are stored exactly as before: upgrading from 0.2.x or 0.1.0 keeps every item, and
going back to 0.2.1 does too.

### ValheimPlus

Tested against ValheimPlus 10.2.0 on Valheim 1.0.16. Without ValheimPlus, nothing in this section
runs and the mod behaves as 0.2.1 did.

- **Craft, build and repair** from a bottomless chest, and see its materials counted in the
  build and crafting panels.
- **Stations use it.** Kilns, smelters, furnaces, windmills, spinning wheels, eitr refineries,
  fermenters, cooking stations, ovens and fires pull from it, and station output goes back into
  it. Beehives and sap collectors deposit into it.
- **Output goes to the chest that already holds that item**, instead of whichever chest you
  last had open.
- **Place Stacks** works from the chest window, from holding the use key, and through
  ValheimPlus's sweep of nearby chests.
- **Fixed: a fire could be refuelled for free** from a bottomless chest, because the fuel came
  off the copy on your screen and not out of the chest.
- **Fixed: Place Stacks could duplicate items.** With two bottomless chests in range holding the
  same item, both could keep the same stack. Present since 0.2.1 for anyone using ValheimPlus
  10.1.2.
- **World levels are respected.** Materials count, and are taken, by the same rules vanilla uses.

### Speed

- **Place Stacks into a large chest: 9.5 seconds, now 21 milliseconds** on a chest of ten
  thousand stacks. Adding to a chest no longer re-reads it for every item, and the chest is
  saved once per Place Stacks instead of once per stack.

### The window

- The window no longer scrolls itself, and the wheel moves one row per notch, only while the
  pointer is over the chest.
- The window no longer grows past its size while a station fills the chest.
- The last row no longer shows an extra empty row.
- Dragging an item out onto the world drops it on the ground, as vanilla does. It used to leave
  the item in the chest as well.

### Gamepad

- The right stick scrolls the chest.
- The left stick and D-pad move through it, and page at the top and bottom edges.
- View/Back focuses the search box.
- B closes the window.
- The search box no longer shows a stray key hint.

### Also

- **Fixed: items that merely share a display name are no longer merged.** Raw fish of different
  kinds, Draugr trophies, Ectoplasm and Voidplasm, and a few others could be combined into one
  stack. Existing chests are corrected the first time they are opened.
- **The build cost is now 10 wood**, the same as a wooden chest. If you never changed the cost,
  your config is updated to match; if you set your own, it is left alone.
- **The chest looks warmer** - wood with a low amber rim, instead of grey with a green glow. The
  same rule applies: untouched colours are updated, colours you chose are left alone.
- `Multiplayer.SnapshotDebounceSeconds` is gone. Nothing had read it since 0.2.0.
- Dragging a quest item out of the chest window is refused, the way the game refuses it
  everywhere else.
- Tested with 100,000 stacks. A million still works, but makes a server close to unusable.

## 0.2.1

**Fixes a way chest contents could be lost on player-hosted servers.** Recommended for anyone
who plays on a server hosted from a player's game, and for anyone running ValheimPlus 10.1.2
on a dedicated server.

Nothing about how chests are stored has changed. Upgrading from 0.2.0 or 0.1.0 is safe, and
going back to 0.2.0 is too.

### What went wrong

When a chest was loaded on the machine running the server - always the case near the host of
a player-hosted game - that machine held the chest twice: once as the chest in the world, and
once for whichever remote player had it open. Both wrote the whole chest to storage, and
neither noticed the other's changes.

So a remote player's deposit could be erased the next time the host touched that chest. And a
change made on the host while a remote player had the chest open could be undone when they
closed it - taken items coming back.

This has been possible since 0.1.0. Single-player was never affected. Dedicated servers were
only exposed around the world's centre, where the server itself keeps the area loaded, and
only when something there changes a chest on the server's side - which ValheimPlus 10.1.2
stations do.

Items already lost this way cannot be brought back by the update.

### The fix

The server now holds exactly one copy of each chest in memory. A remote player's session uses
the loaded chest itself; a chest that loads while someone has it open takes over what they
were working with; and a change made on the server's side tells the open session, so it
refreshes instead of acting on what it last saw. Verified in both directions against the
stored file.

### Other

- Rebinding a chest with `bottomless rebind` from another player's game, while the host also
  had that chest loaded, left the host holding the old contents under the new store id. The
  host's next change to the chest then saved those old contents over the store being
  recovered. The host now notices the rebind and reloads.
- New console commands for looking into storage: `bottomless probe` shows each copy of the
  nearest chest side by side, and `bottomless trace on [prefab]` logs every load and save.
  `session-put`, `session-hold` and `session-release` reproduce a remote player's side of a
  chest on one machine; like `fill` and `empty`, they need `EnableTestingCommands` and
  `devcommands`.

## 0.2.0

**Requires Valheim 1.0.** This release does not work on 0.221 or earlier — 1.0 renamed a
method the mod patches, and the storage APIs it uses moved. Stay on 0.1.0 if you are still
on an older game version.

Upgrading from 0.1.0 does not lose chest contents. Existing stores are read exactly where
they have always been, and 1.0's new save layout is handled without moving anything out
from under an existing chest.

### Valheim 1.0 support

- Chest stores stay exactly where they have always been, beside the world directory rather
  than inside it. 1.0 gives each world its own directory and prunes anything it does not
  recognise from it, so a store kept there would be deleted the first time you backed up or
  restored that world. Several locations are searched on load, newest first.
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

### Stacks collapse on their own

A bottomless chest never runs out of slots, so nothing ever pushed its contents to be tidy:
every deposit landed as its own stack. Put wood in eight at a time and you got a chest full
of eight-wood piles, which is not a storage problem - the chest is unbounded - but it is a
finding problem, because the window shows stacks.

Stacks of the same item now merge whenever it is free to do so: once when a chest is opened,
and from then on as things are put in, including with ctrl-click and by dropping a stack
straight onto a matching one. An existing chest collapses the first time you open it after
upgrading.

Items only merge when the game itself would call them the same thing - same item, quality,
variant and world level - and stacks are never filled past the game's own limit. Anything
the game does not stack, such as gear carrying its own durability, is left alone.

Consolidation counts the items before and after and refuses to save if the two disagree, so
a mistake here costs a session rather than a chest.

### Fixes that predate 1.0

These have been in the mod since 0.1.0 and were only found while testing against a
dedicated server on 1.0. Anyone on 0.1.0 has all of them.

- **Taking part of a stack took the whole thing.** On a dedicated server, splitting a stack
  out of a bottomless chest - the shift-drag that asks for twenty of something - handed over
  the entire stack instead, and whatever would not fit in your inventory dropped on the
  ground. Nothing was destroyed, but asking for twenty wood out of five thousand was not
  something you wanted to do twice. Putting part of a stack *into* a chest had the same
  fault in the other direction. Amounts now travel with the request, and the server measures
  them against its own copy of the stack rather than trusting the page the client is looking
  at.

- **Another mod's deposits could vanish into a full chest.** Loading a chest left an
  internal flag set for the rest of the session, which quietly disabled the code that keeps
  spare slots available - the exact thing that code was added to prevent.
- **`bottomless fill` and `empty` could be run by any connected player**, whatever their own
  settings said. The server now decides for its own chests.
- **A chest that could not be fully read accepted changes anyway.** When a chest holds an
  item whose prefab this install cannot resolve - a content mod removed, or a server running
  one the client lacks - it opens read-only so the good copy on disk is never overwritten.
  Nothing checked that before acting: a deposit was taken and quietly not saved, destroying
  the item, and a withdrawal handed out items the chest still had, which is a duplication
  bug you could repeat at will. Every path that changes a chest now refuses first and says
  so, and whoever asked keeps their items.
- **Items that arrived unreadable were reported as delivered.** A deposit whose payload read
  as nothing at all counted as a successful read, so the sender was told to delete it. The
  count in the payload is now checked against what came out of it.
- **Those two commands never worked on a dedicated server at all**, because the check they
  used also required being the server.
- The panel shows four rows, not six. The mod had assumed six since before 1.0 redesigned
  the inventory; the extra rows were drawn off-screen and scrolled past.
- The chest's scrollbar is shown when there is somewhere to scroll to and hidden when there
  is not, the same as a vanilla container. It used to be permanently visible, and correcting
  the row count above turned that into permanently invisible - the mod takes the bar off the
  game's own scroll machinery in order to drive it, and had never taken over deciding
  whether to show it.

### Other

- `bottomless fill` and `bottomless empty` are off by default. They are testing tools, and
  `empty` discards a chest's contents outright, so they now need `EnableTestingCommands` in
  the Testing section of the config **and** `devcommands`.
- Items added directly into a bottomless chest by another mod are refused on a dedicated
  server client rather than accepted and discarded. ValheimPlus smelters, kilns and
  furnaces drop their output on the ground instead of destroying it.

### Known limits

**Treat a million stacks as the ceiling.** Up to a few hundred thousand a chest opens and
searches so fast there is nothing to notice. At a million it is still comfortable: opening
takes a moment and a search takes a second or two.

Beyond that it degrades badly. Ten million stacks is correct - nothing is lost, everything
is found - but it takes fifteen to twenty seconds to open and around eight seconds to
search, and on a dedicated server that work happens on the server's main thread. While it
runs the server is not answering anyone, and other players watch their network indicator
blink. It recovers on its own and costs nothing permanent, but it is past the point of being
usable and well past the point of being fair to whoever else is on the server.

The chest has no cap and will not stop you. This is the honest number instead of one.

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
