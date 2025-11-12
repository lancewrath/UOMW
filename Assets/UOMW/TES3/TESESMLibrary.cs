using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ESMSharp.TES3
{
    /// <summary>
    /// Global ESM library for managing multiple ESM files and their records.
    /// Handles load order and record overwriting (later ESM files overwrite earlier ones).
    /// </summary>
    public static class TESESMLibrary
    {
        /// <summary>
        /// ESM file entry containing loaded records and metadata
        /// </summary>
        public class ESMEntry
        {
            public string ESMFilename { get; set; }           // Full filename (e.g., "Morrowind.esm")
            public string ESMPath { get; set; }               // Full file path
            public int LoadOrder { get; set; }                 // Load order (0 = first, higher = later)
            public Record[] Records { get; set; }              // All records from this ESM
            public Dictionary<string, List<Record>> RecordsByType { get; set; }  // Records grouped by type
            public bool IsLoaded { get; set; }                 // Whether this ESM is loaded
            public long MinCellX { get; set; }
            public long MinCellY { get; set; }
            public long MaxCellX { get; set; }
            public long MaxCellY { get; set; }
            public float MaxHeight { get; set; }
            
            public ESMEntry(string esmFilename, string esmPath, int loadOrder)
            {
                ESMFilename = esmFilename;
                ESMPath = esmPath;
                LoadOrder = loadOrder;
                Records = new Record[0];
                RecordsByType = new Dictionary<string, List<Record>>();
                IsLoaded = false;
            }
        }
        
        // List of ESM files in load order
        private static List<ESMEntry> _loadedESMs = new List<ESMEntry>();
        
        // Merged records dictionary (keyed by record type and ID, later records overwrite earlier ones)
        // Format: Dictionary<RecordType, Dictionary<RecordID, Record>>
        private static Dictionary<string, Dictionary<string, Record>> _mergedRecords = 
            new Dictionary<string, Dictionary<string, Record>>(StringComparer.OrdinalIgnoreCase);
        
        // All records as a flat array (for backward compatibility)
        private static Record[] _allRecords = new Record[0];
        
        /// <summary>
        /// Scans the StreamingAssets/Data folder for ESM files and loads them in order
        /// </summary>
        /// <param name="dataFolder">Optional custom data folder path (defaults to StreamingAssets/Data)</param>
        /// <returns>Number of ESM files loaded</returns>
        public static int ScanAndLoadESMs(string dataFolder = null)
        {
            if (dataFolder == null)
            {
                dataFolder = Path.Combine(Application.dataPath, "StreamingAssets", "Data");
            }
            
            if (!Directory.Exists(dataFolder))
            {
                Debug.LogError($"Data folder not found: {dataFolder}");
                return 0;
            }
            
            // Find all .esm files
            string[] esmFiles = Directory.GetFiles(dataFolder, "*.esm", SearchOption.TopDirectoryOnly);
            
            if (esmFiles.Length == 0)
            {
                Debug.LogWarning($"No ESM files found in: {dataFolder}");
                return 0;
            }
            
            // Sort by filename (load order is typically alphabetical, but can be customized)
            Array.Sort(esmFiles, StringComparer.OrdinalIgnoreCase);
            
            // Load each ESM file
            int loadedCount = 0;
            for (int i = 0; i < esmFiles.Length; i++)
            {
                string esmPath = esmFiles[i];
                string esmFilename = Path.GetFileName(esmPath);
                
                if (LoadESM(esmFilename, esmPath, i))
                {
                    loadedCount++;
                }
            }
            
            // Rebuild merged records after all ESMs are loaded
            RebuildMergedRecords();
            
            Debug.Log($"TESESMLibrary: Loaded {loadedCount} ESM file(s) from {dataFolder}");
            return loadedCount;
        }
        
        /// <summary>
        /// Loads a single ESM file
        /// </summary>
        public static bool LoadESM(string esmFilename, string esmPath = null, int loadOrder = -1)
        {
            // Check if already loaded
            if (_loadedESMs.Any(e => e.ESMFilename.Equals(esmFilename, StringComparison.OrdinalIgnoreCase)))
            {
                Debug.LogWarning($"ESM file already loaded: {esmFilename}");
                return false;
            }
            
            // Determine path if not provided
            if (esmPath == null)
            {
                esmPath = Path.Combine(Application.dataPath, "StreamingAssets", "Data", esmFilename);
            }
            
            if (!File.Exists(esmPath))
            {
                Debug.LogError($"ESM file not found: {esmPath}");
                return false;
            }
            
            // Determine load order if not specified
            if (loadOrder < 0)
            {
                loadOrder = _loadedESMs.Count;
            }
            
            // Create entry
            ESMEntry entry = new ESMEntry(esmFilename, esmPath, loadOrder);
            
            try
            {
                // Load records from ESM file
                using (var reader = new BetterBinaryReader(File.OpenRead(esmPath)))
                {
                    var tes3 = new Record();
                    tes3.Deserialize(reader, reader.ReadString(4));
                    
                    if (tes3.Type != "TES3")
                    {
                        Debug.LogError($"Not a valid Morrowind master file: {esmFilename}");
                        return false;
                    }
                    
                    var mRecords = new List<Record>();
                    var mRecordsByType = new Dictionary<string, List<Record>>(StringComparer.OrdinalIgnoreCase);
                    
                    while (reader.Position < reader.Length)
                    {
                        string name = reader.ReadString(4);
                        Record mRecord = null;
                        
                        switch (name)
                        {
                            case "LAND":
                                RecordLand lndrecord = new RecordLand();
                                lndrecord.Deserialize(reader, name);
                                mRecord = lndrecord;
                                
                                // Track cell bounds
                                if (lndrecord.maxheight > entry.MaxHeight)
                                    entry.MaxHeight = lndrecord.maxheight;
                                if (lndrecord.MinCellX < entry.MinCellX)
                                    entry.MinCellX = lndrecord.MinCellX;
                                if (lndrecord.MaxCellX > entry.MaxCellX)
                                    entry.MaxCellX = lndrecord.MaxCellX;
                                if (lndrecord.MinCellY < entry.MinCellY)
                                    entry.MinCellY = lndrecord.MinCellY;
                                if (lndrecord.MaxCellY > entry.MaxCellY)
                                    entry.MaxCellY = lndrecord.MaxCellY;
                                break;
                                
                            case "LTEX":
                                RecordLTex ltexrecord = new RecordLTex();
                                ltexrecord.Deserialize(reader, name);
                                mRecord = ltexrecord;
                                break;
                                
                            case "CELL":
                                RecordCell cellrecord = new RecordCell();
                                cellrecord.Deserialize(reader, name);
                                mRecord = cellrecord;
                                break;
                                
                            case "STAT":
                                RecordStat statrecord = new RecordStat();
                                statrecord.Deserialize(reader, name);
                                mRecord = statrecord;
                                break;

                            case "GMST":
                                RecordGmst gmstrecord = new RecordGmst();
                                gmstrecord.Deserialize(reader, name);
                                mRecord = gmstrecord;
                                break;

                            case "GLOB":
                                RecordGlob globrecord = new RecordGlob();
                                globrecord.Deserialize(reader, name);
                                mRecord = globrecord;
                                break;

                            default:
                                mRecord = new Record();
                                mRecord.Deserialize(reader, name);
                                break;
                        }
                        
                        mRecords.Add(mRecord);
                        
                        // Group by type
                        if (!mRecordsByType.ContainsKey(mRecord.Type))
                        {
                            mRecordsByType[mRecord.Type] = new List<Record>();
                        }
                        mRecordsByType[mRecord.Type].Add(mRecord);
                    }
                    
                    entry.Records = mRecords.ToArray();
                    entry.RecordsByType = mRecordsByType;
                    entry.IsLoaded = true;
                }
                
                // Add to loaded list
                _loadedESMs.Add(entry);
                
                // Sort by load order
                _loadedESMs.Sort((a, b) => a.LoadOrder.CompareTo(b.LoadOrder));
                
                // Rebuild merged records after loading this ESM
                RebuildMergedRecords();
                
                Debug.Log($"TESESMLibrary: Loaded ESM '{esmFilename}' ({entry.Records.Length} records) at load order {loadOrder}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"TESESMLibrary: Failed to load ESM '{esmFilename}': {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Rebuilds the merged records dictionary, applying load order (later records overwrite earlier ones)
        /// </summary>
        private static void RebuildMergedRecords()
        {
            _mergedRecords.Clear();
            var allRecordsList = new List<Record>();
            
            // Process ESMs in load order
            foreach (var esmEntry in _loadedESMs)
            {
                if (!esmEntry.IsLoaded)
                    continue;
                
                foreach (var record in esmEntry.Records)
                {
                    // Get record ID (varies by record type)
                    string recordId = GetRecordId(record);
                    
                    if (string.IsNullOrEmpty(recordId))
                        continue;
                    
                    // Group by type
                    if (!_mergedRecords.ContainsKey(record.Type))
                    {
                        _mergedRecords[record.Type] = new Dictionary<string, Record>(StringComparer.OrdinalIgnoreCase);
                    }
                    
                    // Later records overwrite earlier ones (load order)
                    _mergedRecords[record.Type][recordId] = record;
                }
                
                // Add to flat list
                allRecordsList.AddRange(esmEntry.Records);
            }
            
            _allRecords = allRecordsList.ToArray();
            
            Debug.Log($"TESESMLibrary: Rebuilt merged records ({_allRecords.Length} total records, {_mergedRecords.Count} types)");
        }
        
        /// <summary>
        /// Gets the unique ID for a record (varies by record type)
        /// </summary>
        private static string GetRecordId(Record record)
        {
            // Different record types have different ID fields
            // Extract IDs from subrecords
            if (record is RecordCell cell)
            {
                // CELL records use grid coordinates from DATA subrecord
                foreach (var subrec in cell.subRecords)
                {
                    if (subrec is SubRecordCellDATA data)
                    {
                        return $"CELL_{data.gridX}_{data.gridY}";
                    }
                }
                // Fallback if no DATA subrecord
                return $"CELL_{cell.GetHashCode()}";
            }
            else if (record is RecordLand land)
            {
                // LAND records use coordinates (already stored in the record)
                return $"LAND_{land.MinCellX}_{land.MinCellY}";
            }
            else if (record is RecordStat stat)
            {
                // STAT records use NAME subrecord for ID
                foreach (var subrec in stat.subRecords)
                {
                    if (subrec is SubRecordStatNAME name)
                    {
                        return name.name ?? $"STAT_{stat.GetHashCode()}";
                    }
                }
                return $"STAT_{stat.GetHashCode()}";
            }
            else if (record is RecordLTex ltex)
            {
                // LTEX records use index from INTV subrecord
                foreach (var subrec in ltex.subRecords)
                {
                    if (subrec is SubRecordLTexINTV intv)
                    {
                        return $"LTEX_{intv.index}";
                    }
                }
                return $"LTEX_{ltex.GetHashCode()}";
            }
            
            // Fallback: use type + hash code
            return $"{record.Type}_{record.GetHashCode()}";
        }
        
        /// <summary>
        /// Gets all records (merged, with load order applied)
        /// </summary>
        public static Record[] GetAllRecords()
        {
            if (_allRecords == null || _allRecords.Length == 0)
            {
                Debug.LogWarning($"TESESMLibrary.GetAllRecords(): No records available. Library count: {_loadedESMs.Count}, " +
                    $"Loaded ESMs: {string.Join(", ", _loadedESMs.Where(e => e.IsLoaded).Select(e => e.ESMFilename))}");
            }
            return _allRecords;
        }
        
        /// <summary>
        /// Gets all records of a specific type (merged, with load order applied)
        /// </summary>
        public static Record[] GetRecordsByType(string recordType)
        {
            if (_mergedRecords.TryGetValue(recordType, out var recordsDict))
            {
                return recordsDict.Values.ToArray();
            }
            return new Record[0];
        }
        
        /// <summary>
        /// Gets a specific record by type and ID (returns the latest version based on load order)
        /// </summary>
        public static Record GetRecord(string recordType, string recordId)
        {
            if (_mergedRecords.TryGetValue(recordType, out var recordsDict))
            {
                recordsDict.TryGetValue(recordId, out var record);
                return record;
            }
            return null;
        }
        
        /// <summary>
        /// Gets all loaded ESM filenames
        /// </summary>
        public static string[] GetLoadedESMFilenames()
        {
            return _loadedESMs.Select(e => e.ESMFilename).ToArray();
        }
        
        /// <summary>
        /// Gets all loaded ESM entries
        /// </summary>
        public static ESMEntry[] GetLoadedESMEntries()
        {
            return _loadedESMs.ToArray();
        }
        
        /// <summary>
        /// Gets the global cell bounds from all loaded ESMs
        /// </summary>
        public static void GetGlobalCellBounds(out long minCellX, out long minCellY, out long maxCellX, out long maxCellY, out float maxHeight)
        {
            minCellX = long.MaxValue;
            minCellY = long.MaxValue;
            maxCellX = long.MinValue;
            maxCellY = long.MinValue;
            maxHeight = 0f;
            
            foreach (var esm in _loadedESMs)
            {
                if (esm.MinCellX < minCellX) minCellX = esm.MinCellX;
                if (esm.MinCellY < minCellY) minCellY = esm.MinCellY;
                if (esm.MaxCellX > maxCellX) maxCellX = esm.MaxCellX;
                if (esm.MaxCellY > maxCellY) maxCellY = esm.MaxCellY;
                if (esm.MaxHeight > maxHeight) maxHeight = esm.MaxHeight;
            }
            
            if (minCellX == long.MaxValue) minCellX = 0;
            if (minCellY == long.MaxValue) minCellY = 0;
        }
        
        /// <summary>
        /// Clears all loaded ESM files
        /// </summary>
        public static void Clear()
        {
            _loadedESMs.Clear();
            _mergedRecords.Clear();
            _allRecords = new Record[0];
        }
        
        /// <summary>
        /// Gets the number of loaded ESM files
        /// </summary>
        public static int Count => _loadedESMs.Count;
    }
}
