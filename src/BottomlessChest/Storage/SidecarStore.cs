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

        internal static SidecarStore Instance { get; } = new SidecarStore();

        public int Count => _entries.Count;

        /// <summary>
        /// True once this world's file has been read, so an absent entry means "no such
        /// chest" rather than "not looked yet".
        /// </summary>
        public bool IsReady
        {
            get
            {
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

            if (!TryRead(save, world.m_fileSource)
                && !TryRead(save + ".old", world.m_fileSource)
                && !TryRead(save + ".old2", world.m_fileSource))
            {
                Plugin.Log.LogInfo($"No existing chest store for world '{world.m_fileName}'. Starting empty.");
            }
        }

        private static string SavePath(World world) =>
            World.GetWorldSavePath(world.m_fileSource) + "/" + world.m_fileName + Extension;

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

            var save = SavePath(world);
            var pending = save + ".new";
            var previous = save + ".old";
            FileWriter writer = null;

            try
            {
                FileHelpers.EnsureDirectoryExists(World.GetWorldSavePath(world.m_fileSource));

                writer = new FileWriter(pending, FileHelpers.FileHelperType.Binary, world.m_fileSource);
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
                if (FileHelpers.Exists(previous, world.m_fileSource))
                {
                    var older = save + ".old2";
                    if (FileHelpers.Exists(older, world.m_fileSource))
                    {
                        FileHelpers.Delete(older, world.m_fileSource);
                    }

                    FileHelpers.Copy(previous, world.m_fileSource, older, world.m_fileSource);
                }

                FileHelpers.ReplaceOldFile(save, pending, previous, world.m_fileSource);

                _dirty = false;
                Plugin.Log.LogInfo($"Saved {_entries.Count} chest store(s) to {Path.GetFileName(save)}.");
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
