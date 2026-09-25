# Bottomless Chest

A buildable chest that holds as much as you want, with a search box you type into to find
things again.

## What it does

- **Unlimited storage.** One chest replaces a wall of them. Tested with 100,000 stacks. A
  million still works, but makes a server close to unusable.
- **Type to find.** A search box sits at the top of the chest window and filters as you
  type. Matching items are grouped by name so like things sit together.
- **Category search.** `@food`, `@weapon`, `@armor`, `@material`, `@ammo`, `@tool`,
  `@trophy`, `@misc`. Combine them with text: `@food ber` finds Blueberries. Several
  categories widen the search; several words narrow it.
- **Take All and Stack respect the search.** With a filter active they act on what matches,
  not on the whole chest.
- **Works on dedicated servers.** The server holds the contents and sends only what is on
  screen, so a huge chest costs no more to browse than a small one.
- **Works with craft-from-chest mods.** Count, craft, build and deposit from the chest with mods
  like NearbyCrafting, and with ValheimPlus - which also lets kilns, smelters, fermenters,
  cooking stations and fires pull from it and put their output back. On a dedicated server as
  well as in single-player.

## Building one

Hammer, under Furniture, at a workbench. The cost is configurable.

## Using it

- Type to filter. **Escape** clears the search; pressing it again closes the chest.
- The **mouse wheel** or the **scrollbar** moves through a large chest.
- The green slot at the end of the page is always free, so there is somewhere to drop into
  a full chest.

## Notes

- **A chest that still holds something cannot be dismantled.** Emptying an unlimited chest
  onto the ground would spawn an item for every stack, which is not something a world
  recovers from.
- Contents are stored beside the world save in `<world>.bottomless.dat`, not inside the
  world file. Back it up along with the world. If a chest is ever destroyed anyway, its
  contents survive there and `bottomless list` will show them as orphaned, ready to be
  reattached to a new chest with `bottomless rebind <id>`.

## Configuration

`com.myrridin.bottomlesschest.cfg`:

| Setting | Default | |
|---|---|---|
| `Crafting.Requirements` | `Wood:10` | Build cost, as prefab names |
| `Appearance.ModelScale` | `1.0` | Size of the chest model |
| `Appearance.BodyTint` | `#C9B79A` | Colour over the chest body |
| `Appearance.LidTint` | `#3A2E22` | Colour over the lid |
| `Appearance.GlowColour` | `#FFC489` | Colour of the lid glow |
| `Appearance.GlowStrength` | `0.25` | Glow intensity; above 1 blooms |

## Commands

`bottomless list` - every chest store in this world, and whether it is in use or orphaned.
`bottomless here` - which store the nearest chest uses.
`bottomless rebind <id>` - point the nearest chest at a different store, to recover one.
