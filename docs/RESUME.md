# Where this was left — 2026-09-09

Read this first.

## The one-line answer

**0.2.0 is written, reviewed three times, and proven in-game on Valheim 1.0 — including a
ten-million-stack chest on a dedicated server.** The blocker cleared the same evening:
**Jotunn 2.30.0 shipped**, with PR #483 merged into it.

What remains is to install the real Jotunn, rebuild against it, and re-run the checklist.

Branch `valheim-1.0-compat`, pushed. Nothing is floating any more.

## What is on the remote

| Ref | Commit | What |
|---|---|---|
| `v0.1.0` | `7a715cb` | The published Thunderstore build |
| `main` | `283c3b1` | Pre-1.0 work, reviewed, untouched today |
| `valheim-1.0-compat` | `072dea4` | Everything below |

## Jotunn — resolved, but everything tested so far used a PR build

**Jotunn 2.30.0 was released on 2026-09-09 at 21:45**, with
[PR #483](https://github.com/Valheim-Modding/Jotunn/pull/483) merged at 21:12. Its notes say
"Updated the majority of systems for Valheim 1.0.7", with one caveat worth watching:
**piece categories are not fully updated**, because Valheim overhauled that system - and
this mod registers a piece with a category.

Everything tested on 2026-09-09 ran against a **local build of that PR**, not the release:

- Cloned to `/mnt/c/valheim_mods/jotunn-build`, branch `fix/valheim-1.0-compatibility`.
- Two local-only edits, in that clone only: `SolutionDir` is undefined when building the
  project directly, and a `CopyToUnity` target assumes BepInEx lives in the game folder.
  Build with `-p:SolutionDir=C:\valheim_mods\jotunn-build\ -p:SkipUnityCopy=true`.
- The real 2.29.2 is preserved at `/mnt/c/valheim_mods/backup-2026-09-09-jotunn-2.29.2/`.

**Next step:** install Jotunn 2.30.0 through r2modman so the profile carries the real
package, delete `bin`/`obj`, rebuild, redeploy to client and server, and re-run the
checklist. `package/manifest.json` is already pinned to 2.30.0 and BepInEx 5.4.2350.
Do not ship against the PR build.

## Proven in-game on 1.0

All on a dedicated server unless noted.

- Loads clean, client and server. Prefab registers, no degraded mode, all 21 Harmony
  patches resolve.
- **Upgrade from 0.1.0 works.** A store written by 0.1.0 was read from the legacy location
  on a world 1.0 had already converted, and chests left untouched stayed in the old format
  in the same file.
- **The stack-count ceiling is gone.** 10,000,000 stacks round-tripped. On 1.0's own format
  that count wraps to 38,528.
- Take All honours the active search — that is the patch 1.0 renamed, and it fails silently
  when wrong.
- Placing stacks from inside and outside the dialog, take/put, paging, scrolling.
- Store is roughly 4x smaller: ~14 bytes a stack against ~57.
- `ContentsWatch` stays silent with Debug off, which had never been observed.

Timings: 100,000 stacks opens near-instantly and searches fast. Ten million takes 15-20s to
open and about 8s to search. Slow but usable, and the load is the place to attack if that
ever matters — the parked `ChestIndex` work is the shape of that answer.

## Verified on the shipping Jotunn 2.30.0

Re-tested after installing the real release, which is a different binary from the PR build
that everything before it was tested against: chest appears in 1.0's new build menu, opens,
searches, take and put all correct, drop marker in the right place.

Known limit, recorded in the changelog: **a million stacks is the pragmatic ceiling.** Up to
a few hundred thousand there is nothing to notice; a million is comfortable. Ten million is
correct but on the wrong side of usable - 15-20s to open, ~8s to search, and on a dedicated
server that blocks the main thread long enough that everyone else sees their network
indicator blink. The changelog says so plainly rather than implying the chest is unbounded
in practice as well as in principle.

## Still to do before shipping

- [ ] One run at default log levels on the **client** (the server has been checked). Both
      configs are now set for it: BepInEx logs at Info and above, and
      `EnableTestingCommands` is back to `false` on client and server, so the run exercises
      what a player actually installs. The dev values are saved beside each config as
      `.bak-dev-settings`.
- [ ] Package with `scripts/package.sh` and upload. **Not done, and not to be done without
      being asked** - Thunderstore versions are immutable.
- [x] **Tag the release commit.**
- [x] Re-run the upgrade diff. Clean: the four never-change identifiers are intact and the
      read path only gained candidates.

### What the default-log run is looking for

`ScrollbarProbe` used to dump the container scrollbar's whole UI hierarchy at **Info** the
first time anyone opened a bottomless chest - a wall of text in every player's log, from a
probe with no consumer, because the scrollbar work it was meant to inform is still parked.
It is now behind the same Debug-listener check `ContentsWatch` uses, hoisted into
`Core/DebugLogging.cs`, and the check comes before the walk rather than after it.

So the run should produce, from this mod, the load line and nothing else.

## Bugs found today that predate 1.0

All three ship in 0.1.0 today. Recorded in the changelog because someone deciding whether to
upgrade should be told.

- `InventoryCapacity.Suspended` was set on load and never cleared, disabling the headroom
  top-up for the rest of the session — the exact code that stops another mod's deposits
  vanishing into a full grid.
- `bottomless fill` / `empty` had no server-side gate, so any connected player could wipe a
  chest whatever their own config said.
- Those commands never worked on a dedicated server at all: `IsCheatsEnabled()` also
  requires being the server.
- And `VisibleRows` was 6 when the panel shows 4, hidden because everything derived from the
  same constant stayed self-consistent.

## Things that bit, worth not re-learning

- **`ListContainsId` honours only the display prefix.** `FilterPlatformUserID` rewrites
  "Steam" to "V" and then *overwrites* the earlier match rather than OR-ing it, so a
  dedicated server's admin list needs `V_<steamid>`. The client's own `PlayerIsAdmin` wants
  `Steam_<steamid>`. Both forms are in `server-save/adminlist.txt` with the reasoning.
- **BepInEx overwrites `LogOutput.log` on start.** Read the log before restarting; a session
  of evidence was lost that way.
- **r2modman preserves each package's original file timestamps**, so an updated dependency
  can look older than your build output and MSBuild will skip the recompile. Delete
  `bin`/`obj` when a dependency changes.
- **WSL tooling silently rewrites line endings.** It cost a full-file diff on `ChestView.cs`
  and a red herring on `adminlist.txt`. Valheim writes CRLF.
- Three rounds of `/code-review` found nine real defects that self-review missed, several of
  them introduced by the previous round's fixes. It is worth running again on any further
  storage changes.

## Backups, all verified byte-identical

- `/mnt/c/valheim_mods/backup-2026-09-09-pre-0.2.0-stores/` — every chest store as 0.1.0
  left it, with a README. **0.2.0 writes a format 0.1.0 cannot read**, so this is the way
  back.
- `/mnt/c/valheim_mods/backup-2026-09-09-server-world-pre1.0/` — the dedicated server world
  including `.db`/`.fwl`, taken before 1.0 converted it.
- `/mnt/c/valheim_mods/valheim-0.221-assemblies/` — the old game assemblies and both
  decompiles. The dedicated server was the last copy and has since updated.
- `/mnt/c/valheim_mods/backup-2026-09-09-jotunn-2.29.2/` — the real Jotunn.
