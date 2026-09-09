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

## Shipped as 0.2.0

Tagged `v0.2.0`. The release gate was run against `v0.1.0` first and passed:

- The four never-change identifiers are byte-identical to 0.1.0 - `bottomless_chest`,
  `BottomlessChest_id`, `com.myrridin.bottomlesschest`, `0x424C4331`.
- The store read path only grew. `FastInventoryReader` was replaced by
  `InventorySerializer`, and `Still_looks_everywhere_0_1_0_looked` pins all five paths
  0.1.0 read as a test, so a future reordering cannot quietly drop one.
- One client run at default log levels produced exactly four lines from this mod - the
  RPC, the version, the prefab, the piece - and no warnings or errors.

### What the code review caught

A round of `/code-review` on the finished branch found eight defects, and the serious one
was a family rather than a case: a session goes read-only when a chest loads short, and
`Persist` then refuses to write it, but **nothing asked before acting**. A deposit was taken
and silently not saved, so the client deleted an item that no longer existed anywhere; a
withdrawal handed out items the chest still had, which is repeatable duplication. Five paths
had the hole. Two more were mine from the same day: emptying a chest reached past the
session and left the open-stack index pointing at stacks that were gone, and every chest
open rewrote the whole store whether or not consolidation had changed anything.

Self-review found none of these. That is now four rounds in a row where a fresh reviewer
found real defects in the storage path that the author did not. **Run it again on any
further storage change.**

## Still to do

- [ ] Upload `dist/BottomlessChest-0.2.0.zip` to Thunderstore. **Not done.** Versions are
      immutable, so this waits for an explicit go-ahead.
- [ ] Merge `valheim-1.0-compat` into `main` once the upload is confirmed good.
- [ ] `StackRules.SplitForExit` is dead code - a correctness guard against oversized stacks
      escaping into a vanilla save that nothing calls. Only reachable with `UnlimitedStacks`,
      which is off by default, so it is not in 0.2.0's path. Worth wiring up or deleting.
- [ ] An occasional take no-ops: the client fires a second one before the count update
      lands, the server rejects it as stale and resends the page, and the item stays in the
      chest. Not data loss, and the guard is doing its job, but the player sees a click do
      nothing.

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
