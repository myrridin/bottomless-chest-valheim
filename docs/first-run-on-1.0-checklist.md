# First run on Valheim 1.0 — what to check, in order

Nothing on the `valheim-1.0-compat` branch has ever run. This is the whole test plan, and
the order matters: each step assumes the ones above it passed.

**Before launching anything**, confirm the backups are in place —
`/mnt/c/valheim_mods/backup-2026-09-09-pre-0.2.0-stores/` with its `README.txt`. The first
successful save writes a store format **0.1.0 cannot read**, so this is the last easy way
back.

Stop and investigate immediately if any of these appear. They all mean the same thing:

- a chest opens **empty** that should not be
- `Loaded N of M stacks` in the log
- a store file that **shrinks**
- `CRITICAL: the chest prefab could not be registered`

## 1. Does it load at all

Launch the client with the dev profile. Before touching anything, read the log.

- [ ] No `Patching failed - running in degraded mode`
- [ ] No `CRITICAL: the chest prefab could not be registered`
- [ ] Jotunn loads and the mod appears

If patching failed, the message names the method. `RPC_TakeAllResponse` is the one that
changed, and a wrong name there fails loudly at patch time rather than silently at runtime.

## 2. The upgrade test — the one that matters most

Use **CasualSolo**, which has a 9,959-stack chest and a 114-stack chest, both written by
0.1.0 and both sitting in the *legacy* location while the world has already been converted
to 1.0's per-world directory. This is precisely the case the path work exists for.

- [ ] Open each chest. Contents are there, and the counts look right.
- [ ] The log says `Read chest store from '...worlds/CasualSolo.bottomless.dat', which is
      not where this build writes`. **Its absence is a failure**, not a pass — it would
      mean the store was found somewhere unexpected.
- [ ] Search still filters, and `@`-tokens still work.

Do not save yet if anything above looks wrong. Quitting without saving leaves the store as
0.1.0 wrote it.

## 3. The move

Deposit one item, close the chest, and let it save.

- [ ] `worlds/CasualSolo/CasualSolo.bottomless.dat` now exists
- [ ] `worlds/CasualSolo.bottomless.dat` **still exists**, unchanged — it is deliberately
      left as a backup, and its mtime should not have moved
- [ ] `dump-store.py` on the new file reports `bottomless framing`
- [ ] Stack counts in the new file match the old one, plus the item you added

```
python3 scripts/dump-store.py "<worlds>/CasualSolo/CasualSolo.bottomless.dat"
python3 scripts/dump-store.py "<worlds>/CasualSolo.bottomless.dat"
```

The tool cannot decode 1.0's per-item encoding yet, so compare **counts and framing**, not
item lists. Counts are what a truncation would change.

## 4. The ceiling

This is the reason the format changed. `testagain` holds **101,004 stacks** in one chest and
`bottomlessdev` has six chests near 100,000 — all far past the 65,535 a 1.0 save can count.

- [ ] Open the big chest. The stack count is right, not ~35,000 and not ~16,960.
- [ ] Save, quit, reload, open again. The count is **unchanged**.
- [ ] `dump-store.py` agrees.

A wrapped count would show as a chest that lost roughly two thirds of itself. If step 2
passed and this fails, the framing is wrong rather than the paths.

## 5. The renamed patch

Take All is the one patch whose target Valheim renamed, and a Harmony patch on a name that
no longer resolves does nothing at all, quietly.

- [ ] With a search active, Take All takes **only what matches** — not the whole chest
- [ ] Stack All still works
- [ ] Both from the button and from the keyboard shortcut

## 6. Dedicated server

The server is on 1.0 already. This exercises `ChestSessions.Persist`, which was writing a
different format from the other writer until today.

- [ ] Connect, open a bottomless chest, confirm contents page in
- [ ] Deposit and withdraw; both survive a reconnect
- [ ] `dump-store.py` on the server's store shows `bottomless framing` and the right counts
- [ ] Hold-E deposit onto a closed chest
- [ ] Destroying a chest holding items is still refused

## 7. Costs nothing when not debugging

- [ ] Restore `BepInEx.cfg` from `BepInEx.cfg.backup` so `Debug` is off
- [ ] Play normally for a few minutes: `ContentsWatch` should log nothing at all

This has never been observed. The watch is meant to disarm itself when no debug listener
exists, and that gating has only ever run with `Debug` on.

## 8. ValheimPlus, if and when it has a 1.0 build

Not expected to work yet, and not a blocker for 0.2.0.

- [ ] Stations near a bottomless chest drop output on the ground rather than destroying it
- [ ] Craft-from-chest still reads empty on a dedicated server (known, documented)

## Then

- [ ] Bump the Jotunn dependency in `package/manifest.json` to whatever shipped
- [ ] `scripts/package.sh`
- [ ] **Tag the release commit** — 0.1.0 shipped untagged and had to be reconstructed
- [ ] Re-run the upgrade diff:
      `git diff v0.1.0..HEAD -- src/BottomlessChest/Storage/ src/BottomlessChest/Piece/ src/BottomlessChest/Plugin.cs`
