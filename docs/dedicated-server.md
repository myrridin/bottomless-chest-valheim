# Dedicated server setup and testing

## Current state

**Clients connected to a remote server are deliberately inert.** `SidecarStore` refuses to
load or save unless `ZNet.IsServer()`, so on a dedicated server a bottomless chest will
open and read as **empty** on the client. That is expected until the RPC layer exists.

This is not a limitation, it is a guard. Valheim transfers ZDO ownership of a container to
whoever opens it, so without it the *client* would run `Container.Save` and write chest
contents into a file beside its own local world — a store the server never sees, while the
real contents sit untouched on the server. The client logs a warning once and writes
nothing.

## Installing the server

The dedicated server is a **separate Steam app: 896660** ("Valheim Dedicated Server"). It
is not installed. r2modman manages the client profile only, so the server needs its mods
placed by hand:

1. Install app 896660 via Steam.
2. Extract the same **BepInExPack_Valheim 5.4.2333** into the server root, next to
   `valheim_server.exe`.
3. Copy **Jotunn 2.29.2** into `BepInEx/plugins/ValheimModding-Jotunn/` — the exact version
   the client uses. `NetworkCompatibility(VersionStrictness.Minor)` compares mod versions
   between client and server; a mismatch rejects the join.
4. Copy `BottomlessChest.dll` **and** `BottomlessChest.Logic.dll` into
   `BepInEx/plugins/BottomlessChest/`. Both are required — the logic assembly is separate.
5. Start with `start_headless_server.bat`. Confirm `BottomlessChest ... loaded.` appears in
   the server's `BepInEx/LogOutput.log` **without** `(DEGRADED)`.

## Test checklist

Prefix everything with: back up the world and its `.bottomless.dat` first.

1. **Server loads the mod.** Prefab registers; no `CRITICAL` line about registration.
2. **Client joins.** Build a chest, add items, relog. Contents persist server-side.
3. **The ownership hazard.** Client A opens a chest, adds items, closes. Client B opens it
   and sees the same contents. This is the case the RPC layer must fix.
4. **In-use lock.** Two clients open the same chest; the second is refused with `$msg_inuse`.
5. **Modless client is rejected** by `NetworkCompatibility` — and critically, **confirm no
   chest ZDOs were destroyed** by the attempt. Check `bottomless list` afterwards.
6. **Unclean shutdown.** Kill the server process without saving; confirm the last flushed
   store survives and `.old` / `.old2` are intact.
7. **Version mismatch.** Run a deliberately different mod version on the client and confirm
   a clean rejection rather than a partial join.

## What still has to be built

- `Net/ChestRpc.cs` on Jotunn `CustomRPC` (it fragments large payloads, which a big
  inventory snapshot needs). Client requests contents on open; server replies; client sends
  a debounced snapshot back. The vanilla in-use lock guarantees a single writer, so no
  conflict resolution is needed.
- `BottomlessContainer._contentsLoaded` loads once per session and must reload when
  ownership changes.
- `Container.Load` returning false every tick stops `UpdateUseVisual` being called, so the
  lid open/closed state may not animate for other players. Verify.
