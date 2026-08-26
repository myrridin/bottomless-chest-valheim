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
        }

        private void OnDestroy()
        {

            if (_container != null)
            {
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

            var package = new ZPackage();
            _container.m_inventory.Save(package);
            SidecarStore.Instance.Put(storeId, package.GetArray());

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

            var package = new ZPackage();
            _container.m_inventory.Save(package);
            SidecarStore.Instance.Put(storeId, package.GetArray());
        }

        /// <summary>
        /// Reads just the item count from a serialized inventory, without parsing it.
        /// </summary>
        /// <remarks>
        /// The grid has to be big enough before Load runs, and Load is the only thing that
        /// knows how many items there are - so the header is peeked first.
        /// </remarks>
        private static int PeekItemCount(byte[] contents)
        {
            if (contents == null || contents.Length < 8)
            {
                return 0;
            }

            try
            {
                var package = new ZPackage(contents);
                package.ReadInt();
                return package.ReadInt();
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Loads a serialized inventory without losing items to grid capacity.</summary>
        private void LoadIntoInventory(byte[] contents)
        {
            var expected = PeekItemCount(contents);

            _container.m_inventory.RemoveAll();
            InventoryCapacity.ApplyFor(_container.m_inventory, expected);

            if (!Storage.FastInventoryReader.TryLoad(_container.m_inventory, contents, out expected))
            {
                _container.m_inventory.Load(new ZPackage(contents));
            }

            var actual = _container.m_inventory.m_inventory.Count;
            _loadWasPartial = actual != expected;

            if (_loadWasPartial)
            {
                // Loud, because the failure mode is silent: AddItem drops what will not fit
                // and reports nothing, so the chest simply looks emptier than it is.
                Plugin.Log.LogError(
                    $"Loaded {actual} of {expected} stacks - {expected - actual} were dropped " +
                    $"because the grid was too small ({_container.m_inventory.m_width}x{_container.m_inventory.m_height}).");
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
            }
        }
    }
}
