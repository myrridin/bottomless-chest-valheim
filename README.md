# BottomlessChest

A Valheim mod adding a buildable chest with unlimited storage and a typed search filter.
Works in single-player and on dedicated servers.

For what it does and how to use it, see [package/README.md](package/README.md) — that is
the text shown on the mod page.

## Building

Requires the .NET SDK, a Valheim install, and a BepInEx profile containing Jotunn.

```
dotnet build src/BottomlessChest/BottomlessChest.csproj
```

Paths live in `Directory.Build.props` and can be overridden with the `VALHEIM_INSTALL` and
`R2_PROFILE` environment variables. A build deploys to both the r2modman profile and the
dedicated server, and warns when either is running and holding the DLL — Windows will not
let a loaded assembly be replaced, so a build that appears to succeed can silently leave
you testing a stale one.

```
./scripts/package.sh          # Thunderstore-ready zip in dist/ (does not deploy)
dotnet test tests/BottomlessChest.Tests/BottomlessChest.Tests.csproj
```

## Layout

| | |
|---|---|
| `src/BottomlessChest.Logic` | Search, sorting, grid maths. **References neither Unity nor Valheim**, which is what makes it testable — and lets the server run the same search code as the client. |
| `src/BottomlessChest` | The mod: piece registration, storage, networking, GUI. |
| `tests/` | 73 tests, all against the logic assembly. Everything else needs a running game. |
| `scripts/` | Dev server setup and control, sidecar store dump/merge tools, packaging. |

## How it works

The chest stays a vanilla `Container`. Placement, interaction and the in-use lock are
untouched. Three things are redirected: where contents live, what the grid draws, and how
mutations travel.

**Contents live in `<world>.bottomless.dat`, never in the ZDO.** A ZDO replicates to every
peer in range and is rewritten whole on each change, so an unbounded inventory in one would
be re-sent constantly. Keeping contents out of it is also why they survive a chest being
destroyed.

**The grid is a fixed window.** `InventoryGrid` instantiates a GameObject per slot, so a
grid sized to the contents was the real ceiling on "unlimited".

**Clients are paged.** The server holds a live session per open chest and sends only the
items on screen. Sending whole inventories caps out near a megabyte — Jotunn slices packages
into 250KB fragments and waits for the peer's send queue to drain between each, giving up
after 30 seconds, so the true limit is Valheim's per-peer bandwidth.

**Takes and puts are authoritative round trips.** The client asks, the server decides, and
only then does an item move. Optimistic local edits duplicate items whenever the server
disagrees.

## Working on this

Two things are worth knowing before changing anything.

**Vanilla reaches the same behaviour by more than one path, routinely.** Stacking and taking
each have two entry points that share no code; `DropAllItems` has two overloads; scrolling
had two implementations. Nearly every bug in this mod's history was one of those handled on
only one path. Before intercepting anything, enumerate every call site that achieves the
outcome — not the callers of the method you happen to be patching.

**Mono inlines one-line accessors**, so Harmony patches on things like `Inventory.GetHeight`
apply cleanly and never run.

`scripts/dump-store.py` reads a sidecar file outside the game. When something looks wrong,
that file is the honest witness — it settled every data question here.

## Licence

MIT. See [LICENSE](LICENSE).
