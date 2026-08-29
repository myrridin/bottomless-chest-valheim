using System;
using System.Collections.Generic;
using System.IO;

namespace BottomlessChest.Storage
{
    /// <summary>
    /// Keeps every bottomless chest's contents in a single file beside the world save.
    /// </summary>
    /// <remarks>
    /// Contents are kept out of the ZDO deliberately. A ZDO is replicated to every peer
    /// that has it in range and is rewritten wholesale on each change, so an unbounded
    /// inventory in a ZDO would be re-sent to the whole server every time anyone moved
    /// an item.
    ///
    /// Writes follow Valheim's own save pattern - write to ".new", then
    /// <see cref="FileHelpers.ReplaceOldFile"/> rotates the live file to ".old" and the
    /// new one into place. That keeps one generation of backup and works for cloud saves
    /// as well as local ones.
    /// </remarks>
    internal sealed class SidecarStore : IChestStore
    {
        private const int FormatVersion = 1;
        private const uint Magic = 0x424C4331; // "BLC1"
        private const string Extension = ".bottomless.dat";

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private string _loadedWorld;
        private bool _dirty;
        private bool _warnedNotAuthority;
        private bool _warnedAboutCloudFallback;

        internal static SidecarStore Instance { get; } = new SidecarStore();

        public int Count => _entries.Count;

        /// <summary>
        /// True once this world's file has been read, so an absent entry means "no such
        /// chest" rather than "not looked yet".
        /// </summary>
        /// <summary>
        /// Whether this process owns the store file.
        /// </summary>
        /// <remarks>
        /// Only the server may touch it. Valheim hands ZDO ownership of a container to
        /// whoever opens it, so on a dedicated server a client would otherwise run
        /// Container.Save and write chest contents into a file beside its own local world -
        /// inventing a store the server never sees. Until the RPC layer exists, clients stay
        /// inert: IsReady is false, so nothing loads and the save guard refuses to write.
        /// </remarks>
        internal static bool IsServerAuthority
        {
            get
            {
                var net = ZNet.instance;
                return net == null || net.IsServer();
            }
        }

        public bool IsReady
        {
            get
            {
                if (!IsServerAuthority)
                {
                    return false;
                }

                EnsureLoaded();
                var world = ZNet.m_world;
                return world != null && _loadedWorld == world.m_fileName;
            }
        }

        private sealed class Entry
        {
            internal byte[] Contents;
            internal long LastWrittenUtcTicks;
        }

        /// <summary>Store ids with a rough stack count, for the console commands.</summary>
        internal IEnumerable<(string StoreId, int StackCount)> Describe()
        {
            EnsureLoaded();

            foreach (var pair in _entries)
            {
                var stacks = 0;
                try
                {
                    var pkg = new ZPackage(pair.Value.Contents);
                    pkg.ReadInt();
                    stacks = pkg.ReadInt();
                }
                catch
                {
                    stacks = -1;
                }

                yield return (pair.Key, stacks);
            }
        }

        public bool TryGet(string storeId, out byte[] contents)
        {
            EnsureLoaded();

            if (_entries.TryGetValue(storeId, out var entry))
            {
                contents = entry.Contents;
                return true;
            }

            contents = null;
            return false;
        }

        public void Put(string storeId, byte[] contents)
        {
            EnsureLoaded();

            _entries[storeId] = new Entry
            {
                Contents = contents,
                LastWrittenUtcTicks = DateTime.UtcNow.Ticks
            };

            _dirty = true;
        }

        public void Unload()
        {
            _entries.Clear();
            _loadedWorld = null;
            _dirty = false;
        }

        /// <summary>
        /// Loads the current world's file on first use.
        /// </summary>
        /// <remarks>
        /// Lazy rather than hooked to world load, so that a world switch is picked up by
        /// comparing names instead of relying on shutdown callbacks always firing.
        /// </remarks>
        private void EnsureLoaded()
        {
            if (!IsServerAuthority)
            {
                if (!_warnedNotAuthority)
                {
                    _warnedNotAuthority = true;
                    Plugin.Log.LogWarning(
                        "Connected to a remote server. Chest contents live on the server and " +
                        "client-side sync is not implemented yet, so chests will read as empty " +
                        "here. Nothing is written locally and nothing stored is at risk.");
                }

                return;
            }

            var world = ZNet.m_world;
            if (world == null)
            {
                return;
            }

            if (_loadedWorld == world.m_fileName)
            {
                return;
            }

            _entries.Clear();
            _dirty = false;
            _loadedWorld = world.m_fileName;

            var save = SavePath(world);

            var local = SavePath(world, FileHelpers.FileSource.Local);

            if (!TryRead(save, world.m_fileSource)
                && !TryRead(save + ".old", world.m_fileSource)
                && !TryRead(save + ".old2", world.m_fileSource)
                && !TryRead(local, FileHelpers.FileSource.Local)
                && !TryRead(local + ".old", FileHelpers.FileSource.Local))
            {
                Plugin.Log.LogInfo($"No existing chest store for world '{world.m_fileName}'. Starting empty.");
            }
        }

        private static string SavePath(World world) => SavePath(world, world.m_fileSource);

        private static string SavePath(World world, FileHelpers.FileSource source) =>
            World.GetWorldSavePath(source) + "/" + world.m_fileName + Extension;

        /// <summary>
        /// Decides where this world's store should be written.
        /// </summary>
        /// <remarks>
        /// Follows the world by default, so a cloud save carries its chest contents between
        /// machines. But an unlimited chest can grow far larger than a world file - roughly
        /// 58 bytes a stack - and cloud quotas are small and shared with the world save
        /// itself. Valheim checks the quota before its own writes and falls back to local
        /// storage; ours did not, so a big chest could have exhausted the quota and taken
        /// the player's *world* saves down with it.
        ///
        /// The cost of falling back is that the store no longer travels with a cloud world.
        /// The contents are safe and listed by 'bottomless list' on the machine that holds
        /// them, which is a far better failure than a world that cannot save.
        /// </remarks>
        private FileHelpers.FileSource ChooseSource(World world)
        {
            var preferred = world.m_fileSource;

            if (preferred != FileHelpers.FileSource.Cloud || !FileHelpers.CloudStorageEnabled)
            {
                return preferred;
            }

            var required = 0UL;
            foreach (var entry in _entries.Values)
            {
                required += (ulong)entry.Contents.Length;
            }

            // Room for the write plus the backup rotation it triggers.
            required *= 2;

            var remaining = FileHelpers.GetRemainingCloudCapacity();
            Plugin.Log.LogDebug($"Cloud store needs {required / 1024}KB; {remaining / 1024}KB remaining.");

            if (!FileHelpers.OperationExceedsCloudCapacity(required))
            {
                return preferred;
            }

            if (!FileHelpers.LocalStorageSupported)
            {
                Plugin.Log.LogError(
                    $"Chest contents need {required / 1024}KB but only {remaining / 1024}KB of cloud " +
                    "storage remains, and local storage is unavailable. Not saving, to avoid " +
                    "exhausting the quota your world saves also use.");

                return preferred;
            }

            if (!_warnedAboutCloudFallback)
            {
                _warnedAboutCloudFallback = true;
                Plugin.Log.LogWarning(
                    $"Chest contents ({required / 1024}KB) would exceed the remaining cloud quota " +
                    $"({remaining / 1024}KB), which your world saves share. Writing them to local " +
                    "storage instead. They will not follow this world to another machine.");
            }

            return FileHelpers.FileSource.Local;
        }

        private bool TryRead(string path, FileHelpers.FileSource source)
        {
            if (!FileHelpers.Exists(path, source))
            {
                return false;
            }

            FileReader reader = null;

            try
            {
                reader = new FileReader(path, source, FileHelpers.FileHelperType.Binary);
                var binary = reader.m_binary;

                if (binary.ReadUInt32() != Magic)
                {
                    throw new InvalidDataException("bad magic - not a bottomless chest store");
                }

                var version = binary.ReadInt32();
                if (version > FormatVersion)
                {
                    throw new InvalidDataException($"file is format v{version}, this build understands up to v{FormatVersion}");
                }

                var count = binary.ReadInt32();
                for (var i = 0; i < count; i++)
                {
                    var id = binary.ReadString();
                    var written = binary.ReadInt64();
                    var length = binary.ReadInt32();
                    var contents = binary.ReadBytes(length);

                    if (contents.Length != length)
                    {
                        throw new EndOfStreamException($"entry '{id}' is truncated");
                    }

                    _entries[id] = new Entry { Contents = contents, LastWrittenUtcTicks = written };
                }

                Plugin.Log.LogInfo($"Loaded {_entries.Count} chest store(s) from {Path.GetFileName(path)}.");
                return true;
            }
            catch (Exception ex)
            {
                // Never let a damaged store take the world down with it; fall through to
                // the .old backup, and failing that start empty rather than throwing.
                Plugin.Log.LogError($"Could not read chest store '{path}': {ex.Message}");
                _entries.Clear();
                return false;
            }
            finally
            {
                reader?.Dispose();
            }
        }

        public void Flush()
        {
            if (!_dirty)
            {
                return;
            }

            var world = ZNet.m_world;
            if (world == null || _loadedWorld != world.m_fileName)
            {
                return;
            }

            var timer = System.Diagnostics.Stopwatch.StartNew();
            var source = ChooseSource(world);
            var save = SavePath(world, source);
            var pending = save + ".new";
            var previous = save + ".old";
            FileWriter writer = null;

            try
            {
                FileHelpers.EnsureDirectoryExists(World.GetWorldSavePath(source));

                writer = new FileWriter(pending, FileHelpers.FileHelperType.Binary, source);
                var binary = writer.m_binary;

                binary.Write(Magic);
                binary.Write(FormatVersion);
                binary.Write(_entries.Count);

                foreach (var pair in _entries)
                {
                    binary.Write(pair.Key);
                    binary.Write(pair.Value.LastWrittenUtcTicks);
                    binary.Write(pair.Value.Contents.Length);
                    binary.Write(pair.Value.Contents);
                }

                writer.Finish();
                writer = null;

                // Keep two generations. One was very nearly not enough: a single bad save
                // rotates the only good copy into .old, and the save after that destroys it.
                if (FileHelpers.Exists(previous, source))
                {
                    var older = save + ".old2";
                    if (FileHelpers.Exists(older, source))
                    {
                        FileHelpers.Delete(older, source);
                    }

                    FileHelpers.Copy(previous, source, older, source);
                }

                FileHelpers.ReplaceOldFile(save, pending, previous, source);

                _dirty = false;

                var bytes = 0L;
                foreach (var entry in _entries.Values)
                {
                    bytes += entry.Contents.Length;
                }

                // Every store is rewritten whenever any one of them changes, so the cost
                // scales with the total held across all chests, not with what was edited.
                Plugin.Log.LogInfo(
                    $"Saved {_entries.Count} chest store(s), {bytes / 1024}KB, in {timer.ElapsedMilliseconds}ms.");
            }
            catch (Exception ex)
            {
                // Leave _dirty set so the next save retries rather than silently dropping
                // the only copy of someone's chest.
                Plugin.Log.LogError($"Failed to save chest store: {ex}");
                writer?.Finish();
            }
        }
    }
}
