# Bottomless Chest

A buildable chest that holds as much as you want, with a search box you type into to find
things again.

## What it does

- **Unlimited storage.** One chest replaces a wall of them. Tested with a million stacks.
- **Type to find.** A search box sits at the top of the chest window and filters as you
  type. Matching items are grouped by name so like things sit together.
- **Category search.** `@food`, `@weapon`, `@armor`, `@material`, `@ammo`, `@tool`,
  `@trophy`, `@misc`. Combine them with text: `@food ber` finds Blueberries. Several
  categories widen the search; several words narrow it.
- **Take All and Stack respect the search.** With a filter active they act on what matches,
  not on the whole chest.
- **Works on dedicated servers.** The server holds the contents and sends only what is on
  screen, so a huge chest costs no more to browse than a small one.

## Building one

Hammer, under Furniture, at a workbench. The cost is configurable.

## Using it

- Type to filter. **Escape** clears the search; pressing it again closes the chest.
- The **mouse wheel** or the **scrollbar** moves through a large chest.
- The green slot at the end of the page is always free, so there is somewhere to drop into
  a full chest.
- Holding the use key deposits matching stacks without opening the chest.

## Notes

- **Everyone on a server needs the mod**, including the server itself. Joining without it
  is refused with a version mismatch rather than allowed - a client that cannot resolve the
  chest would otherwise destroy it.
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
| `Crafting.Requirements` | `FineWood:20,BlackMetal:10,SurtlingCore:5` | Build cost, as prefab names |
| `Appearance.ModelScale` | `1.0` | Size of the chest model |
| `Appearance.BodyTint` | `#C4C2BC` | Colour over the chest body |
| `Appearance.LidTint` | `#2E2E33` | Colour over the lid |
| `Appearance.GlowColour` | `#8FA86B` | Colour of the lid glow |
| `Appearance.GlowStrength` | `0.35` | Glow intensity; above 1 blooms |
| `Multiplayer.SnapshotDebounceSeconds` | `1.5` | Delay before sending changes to the server |

## Commands

`bottomless list` - every chest store in this world, and whether it is in use or orphaned.
`bottomless here` - which store the nearest chest uses.
`bottomless rebind <id>` - point the nearest chest at a different store, to recover one.
