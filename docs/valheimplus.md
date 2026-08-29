# ValheimPlus compatibility

Findings from reading ValheimPlus 0.9.17.1 (`Grantapher-ValheimPlus_Grantapher_Temporary`)
rather than from waiting for symptoms. Re-derive this against a new V+ version before
trusting it — the conclusions rest on specific implementation details.

## Overlapping patches

V+ patches six methods that BottomlessChest also touches. Every row below was checked
against the decompiled assembly, and the two that could bite are gated behind V+ config
settings rather than being unconditionally safe — so read the verdicts as "safe with this
configuration", not "safe forever".

| Method | V+ does | Verdict |
|---|---|---|
| `Inventory.TopFirst` | Postfix, sets `__result = true` when `inventoryFillTopToBottom` is on | **Safe either way.** We prefix and force true for our inventories. Postfixes run even when a prefix cancels, and both want the same value. |
| `Container.RPC_StackResponse` | Postfix, resolves a `TaskCompletionSource` | **Safe.** We prefix and may cancel; its postfix still runs, so V+ does not hang waiting. |
| `Container.Awake` | Resizes chests by inventory name | **Safe, but fragile — see below.** |
| `Inventory` constructor | Resizes inventories named `Grave`, `Inventory`, `$piece_tombstone_container` | **Safe.** Ours are named `bottomless`, `page`, `offer`, `put`. A match would have corrupted the capacity maths. |
| `Inventory.MoveAll` | Merges stacks before vanilla runs, when `mergeWithExistingStacks` is on | **Hazard when enabled — see below.** |
| `InventoryGrid.UpdateGui` | Prefix that forces an element rebuild when the element list is stale | **Safe.** Its condition is unreachable for our grid — see below. |

### `Container.Awake` — safe only because of one string

V+ switches on the *inventory's* name, not the prefab name:

```csharp
switch (___m_inventory.m_name)
{
    case "$piece_chestprivate":
        height = Helper.Clamp(Configuration.Current.Inventory.personalChestRows, 2, 20);
        width  = Helper.Clamp(Configuration.Current.Inventory.personalChestColumns, 3, 8);
        break;
    ...
}
```

`Container.Awake` builds its inventory as `new Inventory(m_name, ...)`, so the inventory
inherits whatever `Container.m_name` held at that moment. We set
`container.m_name = "$piece_bottomlesschest"` on the *prefab*, before any instance awakes,
so the switch finds no case.

That is the whole reason the chest is not clamped to the personal chest's 3x2. Our piece is
cloned from `piece_chest_private`, and the default V+ config sets `personalChestRows = 2`
and `personalChestColumns = 3` — so anything that lets the vanilla name reach the inventory
constructor caps the chest at six slots. **Do not set `container.m_name` to a vanilla token
for display purposes**, and do not move that assignment to instance time.

### `Inventory.MoveAll` — a real hazard if `mergeWithExistingStacks` is enabled

The prefix mutates both inventories directly:

```csharp
item2.m_stack += num;
if (item.m_stack == num) { fromInventory.RemoveItem(item); break; }
item.m_stack -= num;
```

It never calls `Changed()`. Our whole redraw and re-layout chain hangs off the
`Inventory.Changed` postfix, so items moved this way arrive without the grid ever hearing
about it. Expect stale counts and items that appear only after a reopen. It is inert while
`mergeWithExistingStacks = false`; if that is switched on, this is the first place to look.

### `InventoryGrid.UpdateGui` — condition unreachable for our grid

```csharp
int width  = __instance.m_inventory.GetWidth();
int height = __instance.m_inventory.GetHeight();
if (__instance.m_width == width && __instance.m_height == height
    && __instance.m_elements.Count != width * height)
{
    __instance.m_width = ((__instance.m_width != 1) ? 1 : 2);
}
```

This is V+ forcing a rebuild that vanilla would skip, by perturbing `m_width` so the
dimensions no longer match. Both prefix orderings are benign for us:

- **V+ first.** It reads the real inventory, which is 8 by however many rows the contents
  need. That rarely equals the grid's 8x6, so the condition fails. We then swap our view in
  and vanilla draws it.
- **Us first.** It reads our view, which is always exactly 8x6. The condition then needs a
  stale element list, and the view's dimensions never change, so vanilla keeps the element
  count in step.

The ordering that would matter — V+ perturbing `m_width` while the *real* inventory is
still installed — would make vanilla rebuild an element list sized to the whole chest, one
GameObject per slot. Display windowing exists to prevent exactly that, which is why the
view's dimensions are pinned rather than derived from the contents.

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
