# ValheimPlus compatibility

Findings from reading ValheimPlus 0.9.17.1 (`Grantapher-ValheimPlus_Grantapher_Temporary`)
rather than from waiting for symptoms. Re-derive this against a new V+ version before
trusting it — the conclusions rest on specific implementation details.

## Overlapping patches

V+ patches six methods that BottomlessChest also touches.

| Method | V+ does | Verdict |
|---|---|---|
| `Inventory.TopFirst` | Postfix, sets `__result = true` when fill-top-to-bottom is on | **Safe.** We prefix and force true for our inventories. Postfixes run even when a prefix cancels, and both want the same value. |
| `Container.RPC_StackResponse` | Postfix, resolves a `TaskCompletionSource` | **Safe.** We prefix and may cancel; its postfix still runs, so V+ does not hang waiting. |
| `Container.Awake` | Resizes chests by prefab name (wood, personal, iron) | **Safe.** Ours is `bottomless_chest` and matches none. `InventoryCapacity.Apply` overwrites dimensions regardless. |
| `Inventory` constructor | Resizes inventories named `Grave`, `Inventory`, `$piece_tombstone_container` | **Safe.** Ours are named `bottomless`, `page`, `offer`, `put`. A match would have corrupted the capacity maths. |
| `Inventory.MoveAll` | Part of its take-all handling | **Safe.** Our paged paths intercept the callers before this is reached. |
| `InventoryGrid.UpdateGui` | Prefix that forces an element rebuild when counts disagree | **Watch.** Both prefix it. Ordering matters where we swap the grid's inventory in single-player. Would show as grid rendering oddities, not item loss. |

## Stack size multiplier

V+ multiplies `m_shared.m_maxStackSize` once at load, on the shared data. Every stack limit
we read comes from the same place, so we simply see the larger value. Nothing here
hardcodes vanilla stack sizes.

Caveat on dedicated servers: client and server must agree on `itemStackMultiplier`, or a
stack legal on one side is not on the other. That is V+'s own config-sync concern, but note
our contents live in the sidecar file rather than the world save, so oversized stacks
persist there if V+ is later removed.

## Stack to nearby chests

V+ loops over containers calling `StackAll` on each. Our guard against overlapping offers
was originally global with a five second timeout, so that loop reached only the first
bottomless chest. Offers are now keyed by store id — the guard exists because the server
answers with positions into an offer, which only conflicts for the same chest.

## Craft from chest

**Works in single-player, unchanged.** V+ reads `chest.GetInventory().GetAllItems()`, which
in single-player is the full contents, and consumes with:

```csharp
public static void ConveyContainerToNetwork(Container c)
{
    c.Save();                      // our patch intercepts this
    c.GetInventory().Changed();
}
```

Because it persists through `Container.Save`, consumption lands in the sidecar file. Had it
written the ZDO directly, materials would have been consumed in memory and returned on
reload.

**Does not work on a dedicated server.** A paged client holds only the items on screen, and
nothing at all while the chest is shut, so V+ finds an almost empty inventory.

Making it work would mean one of:

- Giving clients the whole chest, which is exactly what paging exists to avoid.
- Syncing a lightweight index per chest (item name to total count) and intercepting V+'s
  own helpers so they consult it. Workable, but it means patching another mod's internals,
  which breaks whenever they refactor.
- Asking V+ upstream for an extension point that a storage mod can answer.

The third is the only one that stays working without maintenance, and worth raising before
building either of the others.
