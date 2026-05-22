using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace psu_archive_explorer
{
    /// <summary>
    /// Loads and searches the PSU string index. Supports THREE on-disk
    /// formats, auto-detected from the folder/file the caller points at:
    ///
    ///   BINARY (preferred, written by Build_Index.py --format binary):
    ///     psu_string_index.idx/index.dat
    ///     psu_string_index.idx/strings.dat
    ///     Memory-mapped, multi-threaded byte scan.
    ///
    ///   CHUNKED (legacy, written by --format chunked):
    ///     psu_string_index.idx/meta.json.gz
    ///     psu_string_index.idx/tokens.NNN.json.gz
    ///     psu_string_index.idx/strings.NNN.json.gz
    ///     Inverted-index narrowing, lazy string-shard loads.
    ///
    ///   SINGLE-GZ (legacy, written by --format gz):
    ///     psu_string_index.gz  (single gzipped JSON file)
    ///     Whole thing decompressed and parsed at load time, in-RAM scan.
    ///
    /// All three paths produce the same FileIndex.SearchResult shape, so
    /// MainForm doesn't need to know which one is active. Use
    /// `ActiveFormat` for diagnostics / status display.
    /// </summary>
    public static class StringIndex
    {
        public enum IndexFormat { None, Binary, Chunked, SingleGz }
        public static IndexFormat ActiveFormat { get; private set; } = IndexFormat.None;

        public static bool IsLoaded { get; private set; }
        public static int TotalFileCount => fileEntries?.Length ?? 0;
        public static int TotalStringCount { get; private set; }

        // Used by MainForm's auto-detect of "index folder next to the EXE"
        public const string IndexFolderName = "psu_string_index.idx";

        // ================ shared state ================

        // One file-entry shape per format, but search/UI only ever sees
        // these fields. Format-specific extras (offsets, shard IDs, etc.)
        // live in parallel arrays so we don't pay struct bloat when not
        // needed.
        private struct FileEntry
        {
            public string HashName;
            public string RelPath;
            public string Source;
        }
        private static FileEntry[] fileEntries;

        // ================ binary-format state ================
        private const string BinIndexFile = "index.dat";
        private const string BinStringsFile = "strings.dat";
        private const int BinFormatVersion = 12;
        private const byte SepByte = 0x01;

        private static long[] binOffsets;   // per file_id
        private static long[] binLengths;
        private static MemoryMappedFile binMmFile;
        private static MemoryMappedViewAccessor binMmAccessor;
        private static long binStringsSize;
        private static long[] binOffsetsSorted;
        private static int[] binOffsetsFileIds;

        // ================ chunked-format state ================
        // Two naming conventions exist in the wild for chunked indexes,
        // depending on which version of convert_to_chunks.py produced
        // them:
        //   newer: meta.json.gz / strings.NNN.json.gz / tokens.NNN.json.gz
        //   older: meta.gz      / strings.NNN.gz      / tokens.NNN.gz
        // We accept both. chunkedUsesJsonExt is set at load time and
        // controls which naming pattern we use when looking up shards.
        private const string ChunkedMetaFileNew = "meta.json.gz";
        private const string ChunkedMetaFileOld = "meta.gz";
        private const int ChunkedStringsPerShard = 2000;

        private static Dictionary<string, List<int>> chunkedTokens;
        private static int chunkedStringShards;
        private static string chunkedFolder;
        private static bool chunkedUsesJsonExt;
        // shard_idx -> List<List<string>>  (one inner list per file in the shard)
        private static ConcurrentDictionary<int, List<List<string>>> chunkedShardCache;

        // ================ single-gz state ================
        // After load, all strings are resident in memory.
        // gzAllStrings[file_id] = List<string> of that file's strings.
        // Inverted index is also loaded into chunkedTokens-style structure
        // because they share the search path.
        private static List<List<string>> gzAllStrings;

        // ================ Detection ================

        public static bool IndexFolderExists(string folderPath)
        {
            try
            {
                if (string.IsNullOrEmpty(folderPath)) return false;

                // Single .gz file?
                if (File.Exists(folderPath) &&
                    folderPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (!Directory.Exists(folderPath)) return false;

                // Binary?
                if (File.Exists(Path.Combine(folderPath, BinIndexFile))
                    && File.Exists(Path.Combine(folderPath, BinStringsFile)))
                    return true;

                // Chunked? Accept either naming convention.
                if (File.Exists(Path.Combine(folderPath, ChunkedMetaFileNew))
                    || File.Exists(Path.Combine(folderPath, ChunkedMetaFileOld)))
                    return true;

                return false;
            }
            catch { return false; }
        }

        public static IndexFormat DetectFormat(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return IndexFormat.None;

                if (File.Exists(path) &&
                    path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) &&
                    !path.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase))
                    return IndexFormat.SingleGz;

                if (Directory.Exists(path))
                {
                    if (File.Exists(Path.Combine(path, BinIndexFile))
                        && File.Exists(Path.Combine(path, BinStringsFile)))
                        return IndexFormat.Binary;
                    if (File.Exists(Path.Combine(path, ChunkedMetaFileNew))
                        || File.Exists(Path.Combine(path, ChunkedMetaFileOld)))
                        return IndexFormat.Chunked;
                }
            }
            catch { }
            return IndexFormat.None;
        }

        // ================ Loading ================

        public static bool LoadFromFolder(string path, Action<double, string> progress = null)
        {
            var fmt = DetectFormat(path);
            if (fmt == IndexFormat.None)
                throw new DirectoryNotFoundException(
                    "No recognized PSU string index at: " + path +
                    "\nExpected one of: psu_string_index.idx/ (with index.dat or meta.json.gz), or a .gz file.");

            CloseLoaded();
            ActiveFormat = fmt;

            switch (fmt)
            {
                case IndexFormat.Binary:
                    LoadBinary(path, progress);
                    break;
                case IndexFormat.Chunked:
                    LoadChunked(path, progress);
                    break;
                case IndexFormat.SingleGz:
                    LoadSingleGz(path, progress);
                    break;
            }

            IsLoaded = true;
            progress?.Invoke(1.0, $"Ready ({ActiveFormat} format, {TotalFileCount:N0} files)");
            return true;
        }

        private static void CloseLoaded()
        {
            try { binMmAccessor?.Dispose(); } catch { }
            try { binMmFile?.Dispose(); } catch { }
            binMmAccessor = null;
            binMmFile = null;
            binOffsets = null;
            binLengths = null;
            binOffsetsSorted = null;
            binOffsetsFileIds = null;
            fileEntries = null;
            chunkedTokens = null;
            chunkedShardCache = null;
            chunkedFolder = null;
            gzAllStrings = null;
            ActiveFormat = IndexFormat.None;
            IsLoaded = false;
        }

        // ================ Binary loader ================

        private static void LoadBinary(string folderPath, Action<double, string> progress)
        {
            progress?.Invoke(0.05, "Reading index.dat...");
            byte[] idxBytes = File.ReadAllBytes(Path.Combine(folderPath, BinIndexFile));
            int p = 0;
            if (idxBytes.Length < 12 || idxBytes[0] != 'P' || idxBytes[1] != 'S'
                || idxBytes[2] != 'U' || idxBytes[3] != 'X')
                throw new InvalidDataException("index.dat: bad magic (expected 'PSUX')");
            p += 4;
            int version = ReadU32(idxBytes, ref p);
            if (version != BinFormatVersion)
                throw new InvalidDataException(
                    $"index.dat: version {version}, expected {BinFormatVersion}. " +
                    "Rebuild the index with a matching Build_Index.py.");
            int fileCount = ReadU32(idxBytes, ref p);

            progress?.Invoke(0.2, $"Parsing {fileCount:N0} file entries...");

            fileEntries = new FileEntry[fileCount];
            binOffsets = new long[fileCount];
            binLengths = new long[fileCount];
            var sortedOff = new long[fileCount];
            for (int i = 0; i < fileCount; i++)
            {
                string h = ReadStr(idxBytes, ref p);
                string r = ReadStr(idxBytes, ref p);
                string s = ReadStr(idxBytes, ref p);
                long off = ReadI64(idxBytes, ref p);
                long len = ReadI64(idxBytes, ref p);
                fileEntries[i] = new FileEntry { HashName = h, RelPath = r, Source = s };
                binOffsets[i] = off;
                binLengths[i] = len;
                sortedOff[i] = off;
            }
            int[] order = new int[fileCount];
            for (int i = 0; i < fileCount; i++) order[i] = i;
            Array.Sort(sortedOff, order);
            binOffsetsSorted = sortedOff;
            binOffsetsFileIds = order;

            progress?.Invoke(0.6, "Opening strings.dat...");
            string sPath = Path.Combine(folderPath, BinStringsFile);
            binStringsSize = new FileInfo(sPath).Length;
            binMmFile = MemoryMappedFile.CreateFromFile(
                sPath, FileMode.Open, mapName: null,
                capacity: 0, MemoryMappedFileAccess.Read);
            binMmAccessor = binMmFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

            TotalStringCount = 0;
        }

        // ================ Chunked loader ================

        private class ChunkedMetaDto
        {
            public int version { get; set; }
            public string root { get; set; }
            public List<List<string>> files { get; set; }
            public int string_shards { get; set; }
            public int token_shards { get; set; }
        }
        private class ChunkedStringShardDto
        {
            public int start { get; set; }
            public List<List<string>> data { get; set; }
        }

        private static void LoadChunked(string folderPath, Action<double, string> progress)
        {
            chunkedFolder = folderPath;
            chunkedShardCache = new ConcurrentDictionary<int, List<List<string>>>();

            // Detect which naming convention this index uses.
            string newMeta = Path.Combine(folderPath, ChunkedMetaFileNew);
            string oldMeta = Path.Combine(folderPath, ChunkedMetaFileOld);
            string metaPath;
            if (File.Exists(newMeta))
            {
                chunkedUsesJsonExt = true;
                metaPath = newMeta;
            }
            else if (File.Exists(oldMeta))
            {
                chunkedUsesJsonExt = false;
                metaPath = oldMeta;
            }
            else
            {
                throw new FileNotFoundException(
                    "No chunked meta file (meta.json.gz or meta.gz) found in: " + folderPath);
            }

            string ext = chunkedUsesJsonExt ? ".json.gz" : ".gz";

            progress?.Invoke(0.02, $"Loading {Path.GetFileName(metaPath)}...");
            var meta = ReadGzippedJson<ChunkedMetaDto>(metaPath);
            if (meta == null || meta.files == null)
                throw new InvalidDataException(Path.GetFileName(metaPath) + " invalid.");

            fileEntries = new FileEntry[meta.files.Count];
            for (int i = 0; i < meta.files.Count; i++)
            {
                var row = meta.files[i];
                fileEntries[i] = new FileEntry
                {
                    HashName = (row != null && row.Count > 0) ? row[0] : "",
                    RelPath = (row != null && row.Count > 1) ? row[1] : "",
                    Source = (row != null && row.Count > 2) ? row[2] : "",
                };
            }
            chunkedStringShards = meta.string_shards;

            progress?.Invoke(0.1, $"Loading {meta.token_shards} token shards...");
            chunkedTokens = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var perShard = new Dictionary<string, List<int>>[meta.token_shards];

            int loaded = 0;
            object loadedLock = new object();
            Parallel.For(0, meta.token_shards, shardIdx =>
            {
                string shardName = $"tokens.{shardIdx:D3}{ext}";
                string shardPath = Path.Combine(folderPath, shardName);
                if (!File.Exists(shardPath))
                    throw new FileNotFoundException("Token shard missing: " + shardName, shardPath);
                perShard[shardIdx] = ReadGzippedJson<Dictionary<string, List<int>>>(shardPath);
                lock (loadedLock)
                {
                    loaded++;
                    double frac = 0.10 + 0.85 * loaded / (double)meta.token_shards;
                    progress?.Invoke(frac, $"Loading token shard {loaded}/{meta.token_shards}...");
                }
            });
            foreach (var shard in perShard)
            {
                if (shard == null) continue;
                foreach (var kvp in shard)
                {
                    if (chunkedTokens.TryGetValue(kvp.Key, out var existing))
                        existing.AddRange(kvp.Value);
                    else
                        chunkedTokens[kvp.Key] = kvp.Value;
                }
            }
            TotalStringCount = 0;
        }

        private static List<List<string>> ChunkedLoadShard(int shardIdx)
        {
            if (chunkedShardCache.TryGetValue(shardIdx, out var existing))
                return existing;
            string ext = chunkedUsesJsonExt ? ".json.gz" : ".gz";
            string path = Path.Combine(chunkedFolder, $"strings.{shardIdx:D3}{ext}");
            if (!File.Exists(path)) return null;
            try
            {
                var dto = ReadGzippedJson<ChunkedStringShardDto>(path);
                var data = dto?.data ?? new List<List<string>>();
                chunkedShardCache.TryAdd(shardIdx, data);
                return data;
            }
            catch { return null; }
        }

        private static List<string> ChunkedStringsForFile(int fileId)
        {
            if (fileId < 0) return null;
            int shardIdx = fileId / ChunkedStringsPerShard;
            var shard = ChunkedLoadShard(shardIdx);
            if (shard == null) return null;
            int localIdx = fileId - shardIdx * ChunkedStringsPerShard;
            if (localIdx < 0 || localIdx >= shard.Count) return null;
            return shard[localIdx];
        }

        // ================ Single-gz loader ================

        private class GzIndexDto
        {
            public int version { get; set; }
            public string root { get; set; }
            public List<List<string>> files { get; set; }
            public Dictionary<string, List<int>> tokens { get; set; }
            public List<List<string>> strings { get; set; }
        }

        private static void LoadSingleGz(string filePath, Action<double, string> progress)
        {
            progress?.Invoke(0.05, "Decompressing .gz...");
            byte[] decompressed;
            using (var fs = File.OpenRead(filePath))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var ms = new MemoryStream())
            {
                gz.CopyTo(ms);
                decompressed = ms.ToArray();
            }

            progress?.Invoke(0.40, $"Parsing JSON ({decompressed.Length / (1024 * 1024):N0} MB)...");
            GzIndexDto idx = JsonSerializer.Deserialize<GzIndexDto>(decompressed);
            decompressed = null; // free
            if (idx == null) throw new InvalidDataException("psu_string_index.gz: parsed null");

            progress?.Invoke(0.85, "Indexing in memory...");
            fileEntries = new FileEntry[idx.files?.Count ?? 0];
            for (int i = 0; i < fileEntries.Length; i++)
            {
                var row = idx.files[i];
                fileEntries[i] = new FileEntry
                {
                    HashName = (row != null && row.Count > 0) ? row[0] : "",
                    RelPath = (row != null && row.Count > 1) ? row[1] : "",
                    Source = (row != null && row.Count > 2) ? row[2] : "",
                };
            }
            chunkedTokens = idx.tokens ?? new Dictionary<string, List<int>>();
            gzAllStrings = idx.strings ?? new List<List<string>>();
            TotalStringCount = gzAllStrings.Sum(l => l?.Count ?? 0);
        }

        // ================ Query normalization (shared) ================

        private static readonly Regex WordRe = new Regex(
            @"[A-Za-z0-9_]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex ControlCodeRe = new Regex(
            @"<[0-9A-Fa-f]{4,}>", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRunRe = new Regex(
            @"\s+", RegexOptions.Compiled);

        private static string StripControlCodes(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string r = ControlCodeRe.Replace(s, " ");
            r = WhitespaceRunRe.Replace(r, " ");
            return r.Trim();
        }

        private static byte[] NormalizeQueryBytes(string q)
        {
            if (string.IsNullOrEmpty(q)) return Array.Empty<byte>();
            string r = ControlCodeRe.Replace(q, " ");
            r = WhitespaceRunRe.Replace(r, " ").Trim().ToLowerInvariant();
            return Encoding.UTF8.GetBytes(r);
        }

        // ================ Public search dispatcher ================

        public static List<FileIndex.SearchResult> Search(string query, int maxResults = 500)
        {
            if (!IsLoaded || string.IsNullOrWhiteSpace(query))
                return new List<FileIndex.SearchResult>();

            switch (ActiveFormat)
            {
                case IndexFormat.Binary: return SearchBinary(query, maxResults);
                case IndexFormat.Chunked: return SearchTokenBased(query, maxResults, useGz: false);
                case IndexFormat.SingleGz: return SearchTokenBased(query, maxResults, useGz: true);
                default: return new List<FileIndex.SearchResult>();
            }
        }

        // ---------- Binary search (mmap byte scan) ----------

        private static List<FileIndex.SearchResult> SearchBinary(string query, int maxResults)
        {
            var results = new List<FileIndex.SearchResult>();
            byte[] needle = NormalizeQueryBytes(query);
            if (needle.Length < 2) return results;

            byte[] gluedNeedle;
            {
                var buf = new List<byte>(needle.Length);
                foreach (byte b in needle) if (b != (byte)' ') buf.Add(b);
                gluedNeedle = buf.ToArray();
            }

            int workerCount = Math.Min(Math.Max(2, Environment.ProcessorCount), 8);
            var ranges = BinSplitIntoRanges(workerCount);

            var matched = new Dictionary<int, string>();
            object matchedLock = new object();
            var stop = new CancellationTokenSource();

            Parallel.ForEach(ranges,
                new ParallelOptions { MaxDegreeOfParallelism = workerCount },
                (range, loopState) =>
                {
                    if (stop.IsCancellationRequested) return;
                    BinScanRange(range.startFileIdx, range.endFileIdx,
                                 needle, gluedNeedle,
                                 matched, matchedLock, maxResults, stop);
                });

            foreach (var kv in matched)
            {
                int fid = kv.Key;
                if (fid < 0 || fid >= fileEntries.Length) continue;
                var e = fileEntries[fid];
                string preview = kv.Value ?? "";
                if (preview.Length > 80) preview = preview.Substring(0, 77) + "...";
                results.Add(new FileIndex.SearchResult
                {
                    Archive = e.HashName ?? "",
                    InnerPath = e.RelPath ?? "",
                    FileName = GetFileName(e.RelPath ?? ""),
                    IsLoose = false,
                    FriendlyName = preview,
                });
                if (results.Count >= maxResults) break;
            }
            SortResults(results);
            return results;
        }

        private struct WorkRange { public int startFileIdx; public int endFileIdx; }

        private static WorkRange[] BinSplitIntoRanges(int workerCount)
        {
            int totalFiles = fileEntries.Length;
            if (totalFiles == 0) return Array.Empty<WorkRange>();

            long bytesPerWorker = Math.Max(1, binStringsSize / workerCount);
            var ranges = new List<WorkRange>();
            int cursor = 0;
            for (int w = 0; w < workerCount && cursor < totalFiles; w++)
            {
                int endIdx = cursor;
                long bytesInRange = 0;
                while (endIdx < totalFiles && bytesInRange < bytesPerWorker)
                {
                    bytesInRange += binLengths[endIdx];
                    endIdx++;
                }
                if (endIdx <= cursor) endIdx = cursor + 1;
                if (w == workerCount - 1) endIdx = totalFiles;
                ranges.Add(new WorkRange { startFileIdx = cursor, endFileIdx = endIdx });
                cursor = endIdx;
            }
            return ranges.ToArray();
        }

        private static void BinScanRange(int startFileIdx, int endFileIdx,
                                         byte[] needle, byte[] gluedNeedle,
                                         Dictionary<int, string> matched,
                                         object matchedLock,
                                         int maxResults,
                                         CancellationTokenSource stop)
        {
            byte[] fileBuf = new byte[64 * 1024];
            byte[] normBuf = new byte[64 * 1024];

            for (int fid = startFileIdx; fid < endFileIdx; fid++)
            {
                if (stop.IsCancellationRequested) return;
                long len = binLengths[fid];
                if (len <= 0) continue;

                if (fileBuf.Length < len)
                    fileBuf = new byte[Math.Max((long)fileBuf.Length * 2, len)];

                int read = binMmAccessor.ReadArray(binOffsets[fid], fileBuf, 0, (int)len);
                if (read <= 0) continue;

                int sStart = 0;
                while (sStart < read)
                {
                    int sEnd = sStart;
                    while (sEnd < read && fileBuf[sEnd] != SepByte) sEnd++;

                    if (sEnd > sStart)
                    {
                        int normLen = NormalizeStringInto(fileBuf, sStart, sEnd, ref normBuf);
                        bool hit = ContainsBytes(normBuf, normLen, needle);
                        if (!hit && gluedNeedle != null)
                        {
                            int glueLen = 0;
                            for (int i = 0; i < normLen; i++)
                                if (normBuf[i] != (byte)' ')
                                    normBuf[glueLen++] = normBuf[i];
                            hit = ContainsBytes(normBuf, glueLen, gluedNeedle);
                        }
                        if (hit)
                        {
                            string preview = Encoding.UTF8.GetString(fileBuf, sStart,
                                Math.Min(sEnd - sStart, 200));
                            lock (matchedLock)
                            {
                                if (!matched.ContainsKey(fid))
                                {
                                    matched[fid] = preview;
                                    if (matched.Count >= maxResults)
                                    {
                                        stop.Cancel();
                                        return;
                                    }
                                }
                            }
                            break;
                        }
                    }
                    sStart = sEnd + 1;
                }
            }
        }

        private static int NormalizeStringInto(byte[] src, int start, int end, ref byte[] dst)
        {
            int worstCase = end - start;
            if (dst.Length < worstCase) dst = new byte[worstCase * 2];
            int dp = 0;
            bool prevWs = false;
            int i = start;
            while (i < end)
            {
                byte b = src[i];
                if (b == (byte)'<' && i + 1 < end && IsHex(src[i + 1]))
                {
                    int j = i + 1;
                    while (j < end && src[j] != (byte)'>') j++;
                    if (!prevWs && dp > 0) { dst[dp++] = (byte)' '; prevWs = true; }
                    i = (j < end) ? j + 1 : end;
                    continue;
                }
                if (b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n')
                {
                    if (!prevWs && dp > 0) { dst[dp++] = (byte)' '; prevWs = true; }
                    i++;
                    continue;
                }
                if (b >= (byte)'A' && b <= (byte)'Z') b = (byte)(b + 32);
                dst[dp++] = b;
                prevWs = false;
                i++;
            }
            if (dp > 0 && dst[dp - 1] == (byte)' ') dp--;
            return dp;
        }

        private static bool IsHex(byte b)
        {
            return (b >= (byte)'0' && b <= (byte)'9')
                || (b >= (byte)'a' && b <= (byte)'f')
                || (b >= (byte)'A' && b <= (byte)'F');
        }

        private static bool ContainsBytes(byte[] hay, int hayLen, byte[] needle)
        {
            int n = needle.Length;
            if (n == 0 || hayLen < n) return false;
            byte b0 = needle[0];
            int limit = hayLen - n;
            for (int i = 0; i <= limit; i++)
            {
                if (hay[i] != b0) continue;
                bool ok = true;
                for (int k = 1; k < n; k++)
                    if (hay[i + k] != needle[k]) { ok = false; break; }
                if (ok) return true;
            }
            return false;
        }

        // ---------- Token-based search (chunked + single-gz) ----------
        //
        // Shared between the two legacy formats because they have the same
        // data model: an inverted token index + per-file string lists. The
        // only difference is HOW we get a file's strings:
        //   chunked: ChunkedStringsForFile(fid) — loads a shard on demand
        //   gz:      gzAllStrings[fid] — already in memory

        private static List<FileIndex.SearchResult> SearchTokenBased(
            string query, int maxResults, bool useGz)
        {
            var results = new List<FileIndex.SearchResult>();
            string q = query.Trim();

            // Build query tokens like the indexer would.
            var queryTokens = new List<string>();
            foreach (Match m in WordRe.Matches(q))
            {
                string t = m.Value.ToLowerInvariant();
                if (t.Length >= 2) queryTokens.Add(t);
            }

            HashSet<int> candidateIds;
            if (queryTokens.Count == 0)
            {
                candidateIds = new HashSet<int>(Enumerable.Range(0, fileEntries.Length));
            }
            else
            {
                var postings = new List<List<int>>(queryTokens.Count);
                foreach (var tok in queryTokens)
                {
                    if (!chunkedTokens.TryGetValue(tok, out var list) ||
                        list == null || list.Count == 0)
                        return results;
                    postings.Add(list);
                }
                postings.Sort((a, b) => a.Count.CompareTo(b.Count));
                candidateIds = new HashSet<int>(postings[0]);
                for (int i = 1; i < postings.Count; i++)
                {
                    candidateIds.IntersectWith(postings[i]);
                    if (candidateIds.Count == 0) return results;
                }
            }

            string qStripped = StripControlCodes(q);
            string qGlued = WhitespaceRunRe.Replace(qStripped, "");
            var sortedCandidates = candidateIds.ToList();
            sortedCandidates.Sort();

            foreach (int fileId in sortedCandidates)
            {
                if (fileId < 0 || fileId >= fileEntries.Length) continue;

                List<string> strings;
                if (useGz)
                {
                    strings = (fileId < gzAllStrings.Count) ? gzAllStrings[fileId] : null;
                }
                else
                {
                    strings = ChunkedStringsForFile(fileId);
                }
                if (strings == null) continue;

                string matched = null;
                foreach (var s in strings)
                {
                    if (s == null) continue;
                    if (s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    { matched = s; break; }
                    string sStripped = StripControlCodes(s);
                    if (sStripped.IndexOf(qStripped, StringComparison.OrdinalIgnoreCase) >= 0)
                    { matched = s; break; }
                    string sGlued = WhitespaceRunRe.Replace(sStripped, "");
                    if (sGlued.IndexOf(qGlued, StringComparison.OrdinalIgnoreCase) >= 0)
                    { matched = s; break; }
                }
                if (matched == null) continue;

                var e = fileEntries[fileId];
                string preview = matched;
                if (preview.Length > 80) preview = preview.Substring(0, 77) + "...";

                results.Add(new FileIndex.SearchResult
                {
                    Archive = e.HashName ?? "",
                    InnerPath = e.RelPath ?? "",
                    FileName = GetFileName(e.RelPath ?? ""),
                    IsLoose = false,
                    FriendlyName = preview,
                });
                if (results.Count >= maxResults) break;
            }

            SortResults(results);
            return results;
        }

        private static void SortResults(List<FileIndex.SearchResult> results)
        {
            results.Sort((a, b) =>
            {
                int c = string.Compare(a.Archive, b.Archive, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                return string.Compare(a.InnerPath, b.InnerPath, StringComparison.OrdinalIgnoreCase);
            });
        }

        // ================ Helpers ================

        private static T ReadGzippedJson<T>(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var ms = new MemoryStream())
            {
                gz.CopyTo(ms);
                ms.Position = 0;
                return JsonSerializer.Deserialize<T>(ms.ToArray());
            }
        }

        private static string GetFileName(string innerPath)
        {
            if (string.IsNullOrEmpty(innerPath)) return "";
            int slash = innerPath.LastIndexOfAny(new[] { '/', '\\' });
            return slash < 0 ? innerPath : innerPath.Substring(slash + 1);
        }

        private static int ReadU32(byte[] buf, ref int p)
        {
            int v = buf[p] | (buf[p + 1] << 8) | (buf[p + 2] << 16) | (buf[p + 3] << 24);
            p += 4;
            return v;
        }

        private static long ReadI64(byte[] buf, ref int p)
        {
            long lo = (uint)(buf[p] | (buf[p + 1] << 8) | (buf[p + 2] << 16) | (buf[p + 3] << 24));
            long hi = (uint)(buf[p + 4] | (buf[p + 5] << 8) | (buf[p + 6] << 16) | (buf[p + 7] << 24));
            p += 8;
            return (hi << 32) | lo;
        }

        private static string ReadStr(byte[] buf, ref int p)
        {
            int n = buf[p] | (buf[p + 1] << 8);
            p += 2;
            if (n == 0xFFFF)
            {
                n = ReadU32(buf, ref p);
            }
            string s = Encoding.UTF8.GetString(buf, p, n);
            p += n;
            return s;
        }
    }
}