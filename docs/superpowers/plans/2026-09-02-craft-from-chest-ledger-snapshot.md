# SDD ledger — plan: docs/superpowers/plans/2026-09-02-craft-from-chest-on-dedicated-servers.md

Branch: craft-from-chest-dedicated (created from main @ 283c3b1; main carries tag v0.1.0)

## Pre-flight scan

| Rows | What was checked | Finding |
|---|---|---|
| T1 ↔ T2 | Both own ChestIndex.cs + ChestIndexTests.cs; T2 consumes IndexEntry/_entries from T1 | clean — T2 only appends a method and tests |
| T1 → T4 | T4 consumes ChestIndex.From/Entries/Version | clean |
| T2 → T7 | T7 consumes ChestIndex.Take | clean |
| T3 → T4,T6,T7,T8 | All consume the cache via ChestRpc.Indexes | **DEFECT — see Ruling 1** |
| T4 ↔ T7 | Both modify ChestProtocol.cs and ChestRpc.cs; T4 adds 13/14, T7 adds 15 | clean — append-only, no renumbering |
| T4 ↔ T7 | T4's Files list claims ChestSession.cs; no T4 step touches it. T7 is what modifies it | **DEFECT — see Ruling 2** |
| T5 → T6,T7 | T6/T7 consume ValheimPlusBridge.GetNearbyChestItems/RemoveByItem/RemoveByName | clean |
| T5 ↔ T6 | T5's Attach names ChestQueryPatches, which T6 creates; T5's own build step expects FAILURE | **DEFECT — see Ruling 3** |
| T6 ↔ T7 | Both own ChestQueryPatches.cs; T7 appends TryTake + two prefix classes | clean |
| T4 → T8 | T8 consumes ChestRpc.RequestIndex and Indexes.Forget | clean |
| T8 self | Needs ChestRpc.Serialize, which is private; plan says widen to internal | clean — stated in the task |
| T1 self | Tests vs code: From/Entries/CountOf/Version all specified and asserted | clean |
| T2 self | Take specified, six behaviours asserted incl. the never-over-report invariant | clean |
| T3 self | Cache API vs tests: Put/TryGet/Forget/Clear all asserted | clean apart from Ruling 1 |
| T4 self | Wire format written and read in the same field order | clean |
| T6 self | Uses ItemTemplates.For, BottomlessContainer.TryResolve — both exist | clean |
| T7 self | TryTake mutates the cached index in place; cache holds the same object, so the decrement persists until the server's next index | clean — intended |
| T8 self | Uses BottomlessContainer.Loaded (exists, internal) | clean |

## Rulings

Ruling 1: ChestIndexCache moves to src/BottomlessChest.Logic/ChestIndexCache.cs, namespace BottomlessChest.Logic. Why: tests/BottomlessChest.Tests references ONLY BottomlessChest.Logic and targets net6.0; the class as planned sits in the net462 Unity-coupled assembly, so its seven tests could never compile. The class was written free of UnityEngine types precisely so it could be tested, so Logic is where it belongs. Task 4's `Core.ChestIndexCache` becomes `Logic.ChestIndexCache`. Cost if wrong: a namespace move, minutes.

Ruling 2: Task 4's Files entry for ChestSession.cs is stale and is ignored; ChestSession is modified in Task 7 only. Why: no Task 4 step touches it. Cost if wrong: none — Task 7 makes the change either way.

Ruling 3: Tasks 5 and 6 are dispatched as ONE unit. Why: Task 5's Attach references a type Task 6 creates, so Task 5 cannot build, test or be reviewed alone — the plan itself defers their commit to a single commit. Splitting them would send a reviewer a knowingly broken tree. Cost if wrong: one larger review surface instead of two small ones.

## Execution

Task 1: dispatched (haiku, transcription — brief carries complete code). BASE 07b1691.
Task 1: implementer DONE — commit e760a37, 107 tests passing. Review dispatched (sonnet).
Task 1: minor (deferred): stray blank line at ChestIndex.cs:118 (cosmetic; reviewer said not worth a commit).
Task 1: complete (commits 07b1691..e760a37, review clean — spec ✅, quality Approved).

## PAUSED 2026-09-02 by user request (usage limit). Resume at Task 2.
Task 2 brief already extracted: task-2-brief.md. Task 3 and 4 briefs extracted AND patched for Ruling 1.
BASE for Task 2 = e760a37.
