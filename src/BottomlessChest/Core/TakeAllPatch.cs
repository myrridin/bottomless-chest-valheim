using BottomlessChest.Filter;
using HarmonyLib;

namespace BottomlessChest.Core
{
    /// <summary>
    /// Makes "Take All" honour the active search.
    /// </summary>
    /// <remarks>
    /// Vanilla empties the whole container into the player. On a chest holding a hundred
    /// thousand stacks that is never what anyone means - it fills your inventory with
    /// whatever happened to be first and leaves the rest behind. Taking what the search
    /// currently matches is both the useful reading of the button and the safe one.
    ///
    /// With no search active the match set is everything, so the behaviour is unchanged.
    /// </remarks>
    internal static class TakeAllPatch
    {
        // Valheim 1.0 fixed the spelling: this was "RPC_TakeAllRespons" through 0.221.
        // A name that no longer resolves is a patch that never runs, silently - so if Take
        // All ever stops honouring the search after a game update, check this string first.
        [HarmonyPatch(typeof(Container), "RPC_TakeAllResponse")]
        private static class Patch
        {
            private static bool Prefix(Container __instance, long uid, bool granted)
            {
                if (Plugin.Degraded || !granted || Player.m_localPlayer == null)
                {
                    return true;
                }

                if (!BottomlessContainer.TryResolve(__instance, out _))
                {
                    return true;
                }

                // Remote: the page is exactly what is on screen, so ask for all of it.
                if (ChestView.IsRemote)
                {
                    var slots = new System.Collections.Generic.List<int>();
                    for (var i = 0; i < __instance.m_inventory.m_inventory.Count; i++)
                    {
                        slots.Add(i);
                    }

                    ChestView.RequestTake(ChestView.TrimToCapacity(slots));
                    return false;
                }

                if (!ChestView.IsFiltering)
                {
                    return true;
                }

                __instance.m_nview.ClaimOwnership();
                ZDOMan.instance.ForceSendZDO(uid, __instance.m_nview.GetZDO().m_uid);

                var player = Player.m_localPlayer.GetInventory();
                var taken = 0;

                // Mirrors Inventory.MoveAll: try each item, keep going past ones that do not
                // fit, since a smaller stack later may still have room.
                foreach (var item in ChestView.MatchingItems())
                {
                    if (player.AddItem(item, item.m_stack, item.m_gridPos.x, item.m_gridPos.y))
                    {
                        __instance.m_inventory.RemoveItem(item);
                        taken++;
                    }
                    else if (player.AddItem(item))
                    {
                        __instance.m_inventory.RemoveItem(item);
                        taken++;
                    }
                }

                Player.m_localPlayer.Message(
                    MessageHud.MessageType.Center,
                    taken > 0 ? $"$msg_added {taken}" : "$inventory_full");

                __instance.m_onTakeAllSuccess?.Invoke();

                return false;
            }
        }
    }
}
