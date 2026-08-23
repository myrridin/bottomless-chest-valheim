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

        internal static bool TryResolve(Container container, out BottomlessContainer bottomless) =>
            Registry.TryGetValue(container, out bottomless);

        internal static IEnumerable<BottomlessContainer> Loaded => Registry.Values;

        internal Vector3 Position => transform.position;

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
                _container.m_inventory.Load(new ZPackage(legacy));
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

            var package = new ZPackage();
            _container.m_inventory.Save(package);
            SidecarStore.Instance.Put(storeId, package.GetArray());
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
                _container.m_inventory.Load(new ZPackage(contents));
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
