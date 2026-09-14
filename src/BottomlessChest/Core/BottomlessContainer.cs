using System;
using System.Collections.Generic;
using BottomlessChest.Storage;
using UnityEngine;

namespace BottomlessChest.Core
{
    /// <summary>
    /// Marks a <see cref="Container"/> as bottomless and binds it to a store entry.
    /// </summary>
    /// <remarks>
    /// The ZDO carries only a store id, never the items. The id is a GUID rather than the
    /// ZDOID so the binding survives world transfer, where ZDOs are renumbered.
    /// </remarks>
    [DisallowMultipleComponent]
    public class BottomlessContainer : MonoBehaviour
    {
        internal const string StoreIdKey = "BottomlessChest_id";

        /// <summary>
        /// Lets the Container patches resolve us with a dictionary lookup.
        /// </summary>
        /// <remarks>
        /// Container.Load runs once a second for every container in the world, so the
        /// hot path must not call GetComponent.
        /// </remarks>
        private static readonly Dictionary<Container, BottomlessContainer> Registry =
            new Dictionary<Container, BottomlessContainer>();

        private Container _container;
        private ZNetView _nview;
        private bool _contentsLoaded;
        private bool _warnedAboutUnloadedSave;
        private bool _loadWasPartial;
        private bool _warnedAboutSplitCopies;
        private readonly ContentsWatch _watch = new ContentsWatch();


        internal static bool TryResolve(Container container, out BottomlessContainer bottomless) =>
            Registry.TryGetValue(container, out bottomless);

        internal static IEnumerable<BottomlessContainer> Loaded => Registry.Values;

        /// <summary>Finds a loaded chest by its store id, for routing RPC replies.</summary>
        internal static bool TryResolveByStoreId(string storeId, out BottomlessContainer found)
        {
            foreach (var candidate in Registry.Values)
            {
                if (candidate.CurrentStoreId == storeId)
                {
                    found = candidate;
                    return true;
                }
            }

            found = null;
            return false;
        }

        /// <summary>
        /// Finds a chest whose contents this machine has actually loaded, by store id.
        /// </summary>
        /// <remarks>
        /// A session uses this chest's inventory rather than loading its own. A chest that is
        /// registered but not yet loaded does not qualify: its inventory is empty, and it will
        /// take over the session's contents itself when it loads.
        /// </remarks>
        internal static bool TryResolveLoaded(string storeId, out BottomlessContainer found)
        {
            foreach (var candidate in Registry.Values)
            {
                if (candidate._contentsLoaded && candidate._container != null && candidate.CurrentStoreId == storeId)
                {
                    found = candidate;
                    return true;
                }
            }

            found = null;
            return false;
        }

        internal bool LoadWasPartial => _loadWasPartial;

        /// <summary>Sends any debounced snapshot immediately, e.g. when the chest is closed.</summary>
        internal static void FlushAllPending()
        {
        }

        internal Vector3 Position => transform.position;

        internal Inventory Inventory => _container?.m_inventory;

        /// <summary>True while we are waiting on the server for this chest's contents.</summary>
        internal bool AwaitingContents => !_contentsLoaded && SidecarStore.IsServerAuthority;

        /// <summary>
        /// Re-asks the server for contents, ignoring the request throttle.
        /// </summary>
        /// <remarks>
        /// Called when the chest is opened. Contents were previously fetched once per
        /// session, so a chest changed by another player - or by a console command - would
        /// keep showing this client's stale copy indefinitely.
        /// </remarks>
        internal void RefreshFromServer()
        {
            // Remote refresh happens through ChestView's paging; nothing to do here.
        }

        /// <summary>
        /// Re-lays-out and persists after a bulk edit made outside the normal item paths.
        /// </summary>
        internal void NotifyFilled()
        {
            if (_container == null)
            {
                return;
            }

            InventoryCapacity.Repack(_container.m_inventory);
            InventoryCapacity.Apply(_container.m_inventory);
            SaveToStore();
        }

        internal string CurrentStoreId
        {
            get
            {
                var zdo = _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;
                return zdo?.GetString(StoreIdKey, string.Empty);
            }
        }

        /// <summary>
        /// Points this chest at a different store entry and reloads it.
        /// </summary>
        /// <remarks>
        /// Recovery path for when a chest is destroyed but its contents survive in the
        /// file - which is the normal outcome, since contents never lived in the ZDO.
        /// </remarks>
        internal bool Rebind(string storeId)
        {
            var zdo = _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;
            if (zdo == null || !_nview.IsOwner())
            {
                return false;
            }

            zdo.Set(StoreIdKey, storeId);

            _container.m_loading = true;
            _container.m_inventory.RemoveAll();
            _container.m_loading = false;

            _contentsLoaded = false;
            _warnedAboutUnloadedSave = false;

            return LoadFromStore();
        }

        /// <summary>
        /// Marks our inventory as unbounded. Idempotent, and called from the Container
        /// patches rather than Awake because Container.Awake - which creates the inventory -
        /// may not have run yet when ours does.
        /// </summary>
        internal void EnsureRegistered()
        {
            if (_container != null)
            {
                InventoryCapacity.Register(_container.m_inventory);
            }
        }

        private void Awake()
        {
            _container = GetComponent<Container>();
            _nview = GetComponent<ZNetView>();

            if (_container == null)
            {
                Plugin.Log.LogError($"{nameof(BottomlessContainer)} on '{name}' has no Container. Disabling.");
                enabled = false;
                return;
            }

            Registry[_container] = this;
            StoreTrace.Container("awake", null, GetInstanceID(), _container.m_inventory, _contentsLoaded, _loadWasPartial);
        }

        /// <summary>
        /// Watches for contents changing underneath us. Debug logging only; see <see cref="ContentsWatch"/>.
        /// </summary>
        private void Update()
        {
            if (Plugin.Degraded || _container == null || !ContentsWatch.Enabled)
            {
                return;
            }

            var storeId = CurrentStoreId;
            var label = string.IsNullOrEmpty(storeId)
                ? $"unbound chest at {transform.position}"
                : $"chest {storeId.Substring(0, System.Math.Min(8, storeId.Length))} at {transform.position}";

            var open = InventoryGui.instance != null
                && ReferenceEquals(InventoryGui.instance.m_currentContainer, _container);

            _watch.Poll(_container.m_inventory, label, open);
        }

        private void OnDestroy()
        {

            if (_container != null)
            {
                StoreTrace.Container("destroyed", CurrentStoreId, GetInstanceID(), _container.m_inventory, _contentsLoaded, _loadWasPartial);
                InventoryCapacity.Forget(_container.m_inventory);
                Registry.Remove(_container);
            }
        }

        /// <summary>Store id for this chest, minting one on first use. Null if unavailable.</summary>
        internal string GetOrCreateStoreId()
        {
            var zdo = _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;
            if (zdo == null)
            {
                return null;
            }

            var existing = zdo.GetString(StoreIdKey, string.Empty);
            if (!string.IsNullOrEmpty(existing))
            {
                return existing;
            }

            // Only the owner may mint an id; otherwise two peers could invent different
            // ids for the same chest and split its contents across two store entries.
            if (!_nview.IsOwner())
            {
                return null;
            }

            var minted = Guid.NewGuid().ToString("N");
            zdo.Set(StoreIdKey, minted);
            Plugin.Log.LogInfo($"Minted store id {minted} for a new bottomless chest.");

            return minted;
        }

        /// <summary>
        /// Imports items still sitting in the ZDO from before this chest was store-backed.
        /// </summary>
        /// <remarks>
        /// Without this, a chest placed by an earlier build - or by any code path that wrote
        /// the vanilla way - would silently read as empty and then be overwritten by the
        /// empty store entry, losing its contents. Runs once per chest; the ZDO copy is left
        /// in place as a fallback rather than cleared.
        /// </remarks>
        private bool AdoptLegacyZdoContents(string storeId)
        {
            var zdo = _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;
            var legacy = zdo?.GetString(ZDOVars.s_items, string.Empty);

            if (string.IsNullOrEmpty(legacy))
            {
                return false;
            }

            try
            {
                _container.m_loading = true;
                InventoryCapacity.Suspended = true;
                LoadIntoInventory(System.Convert.FromBase64String(legacy));
                InventoryCapacity.Repack(_container.m_inventory);
                InventoryCapacity.Apply(_container.m_inventory);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Could not adopt legacy contents for chest {storeId}: {ex}");
                return false;
            }
            finally
            {
                InventoryCapacity.Suspended = false;
                _container.m_loading = false;
            }

            var adopted = _container.m_inventory.NrOfItems();
            Plugin.Log.LogInfo($"Adopted {adopted} item stack(s) from the ZDO into chest store {storeId}.");

            var payload = Storage.InventorySerializer.Save(_container.m_inventory, storeId);
            if (payload == null)
            {
                // The ZDO still holds the items, so nothing is lost by not adopting them.
                return false;
            }

            SidecarStore.Instance.Put(storeId, payload);

            return true;
        }

        /// <summary>Serializes the live inventory into the store. Called instead of Container.Save.</summary>
        internal void SaveToStore()
        {
            var storeId = GetOrCreateStoreId();
            if (storeId == null)
            {
                return;
            }

            StoreTrace.Container("save requested", storeId, GetInstanceID(), _container.m_inventory, _contentsLoaded, _loadWasPartial);

            // A partial load is more dangerous than a failed one: the chest looks populated,
            // just smaller, so nothing seems wrong until the truncated copy is written back
            // over the real contents.
            if (_loadWasPartial)
            {
                if (!_warnedAboutUnloadedSave)
                {
                    _warnedAboutUnloadedSave = true;
                    Plugin.Log.LogError(
                        $"Refusing to save chest {storeId}: it only loaded partially, so saving " +
                        "would overwrite the full contents with this truncated copy.");
                }

                return;
            }

            // The one rule that makes this safe: never write over an entry we have not
            // read. If loading failed for any reason the inventory is empty in memory but
            // full on disk, and saving would destroy it. Refusing costs a session's edits;
            // saving costs the chest.
            if (!_contentsLoaded)
            {
                if (!_warnedAboutUnloadedSave)
                {
                    _warnedAboutUnloadedSave = true;
                    Plugin.Log.LogWarning(
                        $"Refusing to save chest {storeId}: its contents were never loaded. " +
                        "The stored copy is intact and left untouched.");
                }

                return;
            }

            if (!SidecarStore.IsServerAuthority)
            {
                // Saving here would write the handful of items currently paged in over the
                // whole chest. Mutations travel as explicit operations instead.
                return;
            }

            // Restore the grid's free slots before writing.
            //
            // Other mods add to a chest with Inventory.AddItem, which needs a free slot and
            // silently drops what will not fit. ValheimPlus does it from beehives, sap
            // collectors, fermenters and every smelter, and each such write ends in
            // ConveyContainerToNetwork calling Container.Save - so this is the one event
            // that follows every one of them.
            //
            // Re-sizing used to happen on Inventory.Changed. That patch is dead: Changed is
            // a small private method and Mono inlines it, so the reserve only came back when
            // the chest was next opened or loaded, shrinking by a slot for every deposit in
            // between. Not free, but negligible beside the full serialize just below.
            if (!InventoryCapacity.Suspended)
            {
                InventoryCapacity.Apply(_container.m_inventory);
            }

            // Every change made on this machine outside a session reaches the store through
            // here - the host's own moves, ValheimPlus stations, console commands. A session
            // sharing this inventory has to hear about them, or its index and order go stale.
            if (ChestSessions.TryGet(storeId, out var session))
            {
                if (!ReferenceEquals(session.Inventory, _container.m_inventory))
                {
                    // Two separate copies again, which sharing exists to make impossible.
                    // Writing ours would overwrite the session's changes, so do not.
                    if (!_warnedAboutSplitCopies)
                    {
                        _warnedAboutSplitCopies = true;
                        Plugin.Log.LogError(
                            $"Chest {storeId} is held by a session with a separate copy of its " +
                            "contents. Refusing to save this copy over the session's; this is a " +
                            "bug in the mod.");
                    }

                    return;
                }

                session.OnExternalChange();
            }

            var payload = Storage.InventorySerializer.Save(_container.m_inventory, storeId);
            if (payload == null)
            {
                return;
            }

            StoreTrace.Container("save wrote", storeId, GetInstanceID(), _container.m_inventory, _contentsLoaded, _loadWasPartial);
            SidecarStore.Instance.Put(storeId, payload);
        }

        /// <summary>
        /// Reads just the item count from a serialized inventory, without parsing it.
        /// </summary>
        /// <remarks>
        /// The grid has to be big enough before Load runs, and Load is the only thing that
        /// knows how many items there are - so the header is peeked first.
        /// </remarks>
        private static int PeekItemCount(byte[] contents) =>
            Logic.InventoryPayload.TryReadHeader(contents, out var header) ? header.Count : 0;

        /// <summary>Loads a serialized inventory without losing items to grid capacity.</summary>
        private void LoadIntoInventory(byte[] contents)
        {
            var expected = PeekItemCount(contents);

            // Replacing every stack at once is not a leak; do not report it as one.
            _watch.Rebaseline();

            _container.m_inventory.RemoveAll();
            InventoryCapacity.ApplyFor(_container.m_inventory, expected);

            // A payload that could not be read at all reports no count, so comparing counts
            // alone would see nothing loaded, nothing expected, and call that a success -
            // then write an empty chest over bytes we simply failed to parse.
            var readable = Storage.InventorySerializer.Load(_container.m_inventory, contents, out expected);

            var actual = _container.m_inventory.m_inventory.Count;
            _loadWasPartial = !readable || actual != expected;

            if (_loadWasPartial)
            {
                // Loud, because the failure mode is silent: the chest simply looks emptier
                // than it is. Two things cause it - a grid too small, where AddItem drops
                // what will not fit and reports nothing, or a payload that could not be
                // read to the end. The serializer logs the second, so both are named here
                // rather than blaming the grid for something it did not do.
                Plugin.Log.LogError(
                    $"Loaded {actual} of {expected} stacks - {expected - actual} missing. " +
                    $"Either the grid was too small ({_container.m_inventory.m_width}x" +
                    $"{_container.m_inventory.m_height}) or the store could not be read to " +
                    "the end; any read error is logged above. This chest will refuse to save.");

                // Deliberately not consolidated. A partial load never gets written back, and
                // rewriting contents we already know are incomplete buys nothing while
                // giving a later change somewhere else the chance to save them.
                return;
            }

            if (!StackConsolidation.Collapse(_container.m_inventory, out var collapsed))
            {
                // Same door as a short load: refuse to save, so the store keeps what it had.
                _loadWasPartial = true;
                return;
            }

            if (collapsed > 0)
            {
                Plugin.Log.LogInfo(
                    $"Consolidated {collapsed} part-stack(s) on load; " +
                    $"{_container.m_inventory.m_inventory.Count} stack(s) left.");
            }
        }

        /// <summary>
        /// Takes on the contents of a session that opened this chest before it loaded here,
        /// and hands the session this chest's inventory in exchange.
        /// </summary>
        /// <remarks>
        /// The items are the session's own objects, copied across in the same order, so the
        /// pages its client holds still name the same items. Only grid positions are rewritten.
        /// </remarks>
        private void AdoptSession(string storeId, ChestSession session)
        {
            _contentsLoaded = true;

            if (ReferenceEquals(session.Inventory, _container.m_inventory))
            {
                _loadWasPartial = session.ReadOnly;
                return;
            }

            try
            {
                // As on load: populating the inventory must not fire a save back.
                _container.m_loading = true;
                InventoryCapacity.Suspended = true;
                _watch.Rebaseline();

                // Copied first: clearing the destination before reading a list that happened
                // to be the same object would empty the chest.
                var incoming = new List<ItemDrop.ItemData>(session.Inventory.m_inventory);
                var items = _container.m_inventory.m_inventory;
                items.Clear();
                items.AddRange(incoming);
                _loadWasPartial = session.ReadOnly;

                InventoryCapacity.Repack(_container.m_inventory);
                InventoryCapacity.Apply(_container.m_inventory);
                session.AdoptInventory(_container.m_inventory);

                StoreTrace.Container("adopted open session", storeId, GetInstanceID(), _container.m_inventory, _contentsLoaded, _loadWasPartial);
            }
            finally
            {
                _container.m_loading = false;
                InventoryCapacity.Suspended = false;
            }
        }

        /// <summary>Applies contents received over the network.</summary>
        internal void ApplyRemoteContents(byte[] contents)
        {
            if (_container == null)
            {
                return;
            }

            var timer = System.Diagnostics.Stopwatch.StartNew();
            long loaded = 0, repacked = 0;

            try
            {
                _container.m_loading = true;
                InventoryCapacity.Suspended = true;

                if (contents != null && contents.Length > 0)
                {
                    LoadIntoInventory(contents);
                }
                else
                {
                    _container.m_inventory.RemoveAll();
                }

                loaded = timer.ElapsedMilliseconds;

                InventoryCapacity.Repack(_container.m_inventory);
                InventoryCapacity.Apply(_container.m_inventory);
                repacked = timer.ElapsedMilliseconds;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Could not apply contents received for a chest: {ex}");
                return;
            }
            finally
            {
                InventoryCapacity.Suspended = false;
                _container.m_loading = false;
            }

            var count = _container.m_inventory.m_inventory.Count;
            if (count >= 1000)
            {
                Plugin.Log.LogDebug(
                    $"Applied {count} stacks in {timer.ElapsedMilliseconds}ms " +
                    $"(deserialise {loaded}ms, layout {repacked - loaded}ms, rest {timer.ElapsedMilliseconds - repacked}ms).");
            }

            // Only now is it safe to save: we have something real to save over.
            _contentsLoaded = true;
            if (!_loadWasPartial)
            {
                _warnedAboutUnloadedSave = false;
            }
        }

        /// <summary>
        /// Fills the live inventory from the store, once.
        /// </summary>
        /// <returns>True the first time contents are applied, matching Container.Load's contract.</returns>
        internal bool LoadFromStore()
        {
            if (_contentsLoaded)
            {
                return false;
            }

            var storeId = GetOrCreateStoreId();
            if (storeId == null)
            {
                return false;
            }

            if (!SidecarStore.IsServerAuthority)
            {
                // Remote chests are paged by ChestView straight from the server; the
                // container never holds their full contents and must never persist them.
                return false;
            }

            // Until the file has been read, "no entry" means "not looked yet". Treating
            // that as an empty chest is what allowed contents to be overwritten.
            if (!SidecarStore.Instance.IsReady)
            {
                return false;
            }

            // A session already holds this chest - a remote player opened it before it loaded
            // here. Its contents are newer than the store's by however many changes it has
            // made, so they are what this chest takes on, not the file.
            if (ChestSessions.TryGet(storeId, out var session))
            {
                AdoptSession(storeId, session);
                return true;
            }

            _contentsLoaded = true;

            if (!SidecarStore.Instance.TryGet(storeId, out var contents) || contents == null || contents.Length == 0)
            {
                return AdoptLegacyZdoContents(storeId);
            }

            try
            {
                // Vanilla Container.Load does the same. Without it, populating the
                // inventory fires m_onChanged -> OnContainerChanged -> Save, which would
                // immediately write back whatever we just read - including a partial read.
                _container.m_loading = true;
                InventoryCapacity.Suspended = true;
                LoadIntoInventory(contents);
                InventoryCapacity.Repack(_container.m_inventory);
                InventoryCapacity.Apply(_container.m_inventory);
                StoreTrace.Container("loaded", storeId, GetInstanceID(), _container.m_inventory, _contentsLoaded, _loadWasPartial);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Could not load contents for chest {storeId}: {ex}");
                return false;
            }
            finally
            {
                _container.m_loading = false;

                // Leaking this leaves it true for the life of the process, which makes
                // SaveToStore's headroom top-up a no-op and disables the capacity hook. The
                // symptom is another mod's deposits vanishing into a full grid - the exact
                // thing that top-up exists to prevent. The other two load paths already
                // reset it; this one did not.
                InventoryCapacity.Suspended = false;
            }
        }
    }
}
