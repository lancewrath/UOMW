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
        /// 
        /// ESM Load Order and Record Stacking
        /// ====================================
        /// ESM files stack on top of each other - when generating terrain, we take all cells from all ESM files
        /// to create one unified heightmap and terrain. Load order is CRITICAL because:
        /// 
        /// 1. Duplicate records: When multiple ESM files contain records with the same ID (e.g., same CELL, NPC, STAT),
        ///    the record from the LATER ESM file (higher load order) REPLACES the one from earlier ESMs.
        /// 
        /// 2. Morrowind.ini load order: The [Game Files] section in Morrowind.ini (located in StreamingAssets/Data/)
        ///    specifies the exact load order. This is now parsed by ParseMorrowindIniLoadOrder() to determine the
        ///    correct order. The format is:
        ///    [Game Files]
        ///    GameFile0=Morrowind.esm
        ///    GameFile1=Tribunal.esm
        ///    GameFile2=Bloodmoon.esm
        ///    etc.
        /// 
        /// 3. Modding support: This is how expansions and mods work - they can override existing content by
        ///    providing replacement records with the same ID. For example, Bloodmoon.esm can modify cells from
        ///    Morrowind.esm by providing updated CELL records with the same coordinates.
        /// 
        /// 4. Implementation: 
        ///    - ParseMorrowindIniLoadOrder() parses Morrowind.ini [Game Files] section to get correct load order
        ///    - ScanAndLoadESMs() sorts ESMs according to Morrowind.ini order (falls back to alphabetical if INI not found)
        ///    - RebuildMergedRecords() processes ESMs in load order (later records overwrite earlier ones)
        ///    - Terrain generation (GenerateHeightMap_MergedLands) uses the merged records correctly
        /// 
        /// 5. Record types affected: All record types are affected, but especially important for:
        ///    - CELL records (terrain cells - later ESMs can modify terrain)
        ///    - LAND records (heightmap data - later ESMs can override terrain heights)
        ///    - STAT, CONT, NPC_, LIGH, etc. (game objects - later ESMs can replace or modify them)
        public static int ScanAndLoadESMs(string dataFolder = null)
        {
            if (string.IsNullOrEmpty(dataFolder))
            {
                dataFolder = Path.Combine(Application.dataPath, "StreamingAssets", "Data");
                Debug.Log("Path set to : " + dataFolder);
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
            
            // Parse Morrowind.ini to get load order from [Game Files] section
            List<string> loadOrder = ParseMorrowindIniLoadOrder(dataFolder);
            
            // Sort ESM files according to Morrowind.ini load order
            List<(string path, int order)> esmFilesWithOrder = new List<(string, int)>();
            foreach (string esmPath in esmFiles)
            {
                string esmFilename = Path.GetFileName(esmPath);
                int order = loadOrder.IndexOf(esmFilename);
                
                if (order >= 0)
                {
                    // Found in Morrowind.ini - use that order
                    esmFilesWithOrder.Add((esmPath, order));
                }
                else
                {
                    // Not in Morrowind.ini - add at end with high order number
                    esmFilesWithOrder.Add((esmPath, int.MaxValue));
                    Debug.LogWarning($"ESM file '{esmFilename}' not found in Morrowind.ini [Game Files] section. Loading after configured files.");
                }
            }
            
            // Sort by load order
            esmFilesWithOrder.Sort((a, b) => a.order.CompareTo(b.order));
            
            // Load each ESM file in the correct order
            int loadedCount = 0;
            for (int i = 0; i < esmFilesWithOrder.Count; i++)
            {
                string esmPath = esmFilesWithOrder[i].path;
                string esmFilename = Path.GetFileName(esmPath);
                
                if (LoadESM(esmFilename, esmPath, i))
                {
                    loadedCount++;
                }
            }
            
            // Rebuild merged records after all ESMs are loaded
            RebuildMergedRecords();
            
            Debug.Log($"TESESMLibrary: Loaded {loadedCount} ESM file(s) from {dataFolder}");
            Debug.Log($"TESESMLibrary: Summary - Total lights: {TESLightManager.Count}, Total containers: {TESContainerLibrary.Count}, Total NPCs: {TESCharacterManager.NPCCount}, Total creatures: {TESCreatureManager.Count}");
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
                                
                                // Register LTEX record with TESLTextureLibrary
                                // Extract NAME, INTV, and DATA subrecords
                                string ltexName = null;
                                int ltexIndex = -1;
                                string ltexFilename = null;
                                
                                foreach (SubRecords subrec in ltexrecord.subRecords)
                                {
                                    SubRecordLTexNAME nameSubrec = subrec as SubRecordLTexNAME;
                                    if (nameSubrec != null)
                                    {
                                        ltexName = nameSubrec.name;
                                    }
                                    
                                    SubRecordLTexINTV intvSubrec = subrec as SubRecordLTexINTV;
                                    if (intvSubrec != null)
                                    {
                                        ltexIndex = intvSubrec.index;
                                    }
                                    
                                    SubRecordLTexData dataSubrec = subrec as SubRecordLTexData;
                                    if (dataSubrec != null)
                                    {
                                        ltexFilename = dataSubrec.filename;
                                    }
                                }
                                
                                // Register if we have at least an index
                                if (ltexIndex >= 0)
                                {
                                    TESLTextureLibrary.RegisterLTEXRecord(ltexName, ltexIndex, ltexFilename, esmFilename);
                                }
                                break;
                                
                            case "CELL":
                                RecordCell cellrecord = new RecordCell();
                                cellrecord.Deserialize(reader, name);
                                mRecord = cellrecord;
                                
                                // Register cell with TESCelLibrary
                                TESCelLibrary.AddCell(cellrecord, esmFilename);
                                break;
                                
                            case "STAT":
                                RecordStat statrecord = new RecordStat();
                                statrecord.Deserialize(reader, name);
                                mRecord = statrecord;
                                
                                // Register static with TESStaticLibrary
                                TESStaticLibrary.AddStatic(statrecord, esmFilename);
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

                            case "SCPT":
                                RecordScpt scptrecord = new RecordScpt();
                                scptrecord.Deserialize(reader, name);
                                mRecord = scptrecord;
                                
                                // Register script with TESMWScriptManager if it has all required data
                                if (scptrecord.Header != null && !string.IsNullOrEmpty(scptrecord.SourceCode))
                                {
                                    string cacheFilename = SubRecordSctpSCTX.SanitizeFileName(scptrecord.Header.name) + ".mws";
                                    TESMWScriptManager.AddScript(
                                        scptrecord.Header.name, 
                                        scptrecord.SourceCode, 
                                        scptrecord.Variables, 
                                        scptrecord.Header, 
                                        cacheFilename, 
                                        esmFilename
                                    );
                                }
                                break;

                            case "BODY":
                                RecordBody recordBody = new RecordBody();
                                recordBody.Deserialize(reader, name);
                                mRecord = recordBody;
                                
                                // Register body part with TESCharacterManager
                                TESCharacterManager.AddBodyPart(recordBody, esmFilename);
                                break;

                            case "NPC_":
                                RecordNPC recordNPC = new RecordNPC();
                                recordNPC.Deserialize(reader, name);
                                mRecord = recordNPC;
                                
                                // Register NPC with TESCharacterManager
                                TESCharacterManager.AddNPC(recordNPC, esmFilename);
                                break;

                            case "CONT":
                                RecordCont contrecord = new RecordCont();
                                contrecord.Deserialize(reader, name);
                                mRecord = contrecord;
                                
                                // Register container with TESContainerLibrary
                                TESContainerLibrary.AddContainer(contrecord, esmFilename);
                                break;

                            case "LIGH":
                                RecordLight lightrecord = new RecordLight();
                                lightrecord.Deserialize(reader, name);
                                mRecord = lightrecord;
                                
                                // Register light with TESLightManager
                                var lightEntry = TESLightManager.AddLight(lightrecord, esmFilename);
                                break;


                            case "CLOT":
                                RecordClothing clothrecord = new RecordClothing();
                                clothrecord.Deserialize(reader, name);
                                mRecord = clothrecord;
                                
                                // Register clothing with TESEquipmentLibrary
                                TESEquipmentLibrary.AddClothing(clothrecord, esmFilename);
                                break;

                            case "ARMO":
                                RecordArmor armorrecord = new RecordArmor();
                                armorrecord.Deserialize(reader, name);
                                mRecord = armorrecord;
                                
                                // Register armor with TESEquipmentLibrary
                                TESEquipmentLibrary.AddArmor(armorrecord, esmFilename);
                                break;

                            case "DOOR":
                                RecordDoor doorrecord = new RecordDoor();
                                doorrecord.Deserialize(reader, name);
                                mRecord = doorrecord;
                                
                                // Register door with TESDoorManager
                                TESDoorManager.AddDoor(doorrecord, esmFilename);
                                break;


                            case "ACTI":
                                RecordActivator actirecord = new RecordActivator();
                                actirecord.Deserialize(reader, name);
                                mRecord = actirecord;

                                // Register activator with TESActivatorLibrary
                                TESActivatorLibrary.AddActivator(actirecord, esmFilename);

                                break;

                            case "CREA":
                                RecordCreature creaturerecord = new RecordCreature();
                                creaturerecord.Deserialize(reader, name);
                                mRecord = creaturerecord;
                                
                                // Register creature with TESCreatureManager
                                TESCreatureManager.AddCreature(creaturerecord, esmFilename);
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
                
                // Log summary of registered records
                int lightCount = entry.RecordsByType.ContainsKey("LIGH") ? entry.RecordsByType["LIGH"].Count : 0;
                if (lightCount > 0)
                {
                    Debug.Log($"TESESMLibrary: Registered {lightCount} LIGH record(s) from '{esmFilename}'. Total lights in library: {TESLightManager.Count}");
                }
                
                int creatureCount = entry.RecordsByType.ContainsKey("CREA") ? entry.RecordsByType["CREA"].Count : 0;
                if (creatureCount > 0)
                {
                    Debug.Log($"TESESMLibrary: Registered {creatureCount} CREA record(s) from '{esmFilename}'. Total creatures in library: {TESCreatureManager.Count}");
                }
                
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
        /// 
        /// NOTE: This function correctly implements ESM stacking - records from later ESMs (higher load order)
        /// overwrite records from earlier ESMs with the same ID. This is how Morrowind modding works.
        /// The load order is determined by the order in _loadedESMs, which should match Morrowind.ini [Game Files].
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
        /// Parses Morrowind.ini file to extract ESM load order from [Game Files] section
        /// </summary>
        /// <param name="dataFolder">Path to the data folder containing Morrowind.ini</param>
        /// <returns>List of ESM filenames in load order (GameFile0, GameFile1, etc.)</returns>
        private static List<string> ParseMorrowindIniLoadOrder(string dataFolder)
        {
            List<string> loadOrder = new List<string>();
            string iniPath = Path.Combine(dataFolder, "Morrowind.ini");
            
            if (!File.Exists(iniPath))
            {
                Debug.LogWarning($"Morrowind.ini not found at: {iniPath}. Using alphabetical load order.");
                return loadOrder;
            }
            
            try
            {
                bool inGameFilesSection = false;
                Dictionary<int, string> gameFiles = new Dictionary<int, string>();
                
                using (StreamReader reader = new StreamReader(iniPath))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        // Trim whitespace and handle comments
                        line = line.Trim();
                        if (string.IsNullOrEmpty(line) || line.StartsWith(";") || line.StartsWith("#"))
                            continue;
                        
                        // Check for section headers
                        if (line.StartsWith("[") && line.EndsWith("]"))
                        {
                            string section = line.Substring(1, line.Length - 2).Trim();
                            inGameFilesSection = string.Equals(section, "Game Files", StringComparison.OrdinalIgnoreCase);
                            continue;
                        }
                        
                        // Parse GameFile entries when in [Game Files] section
                        if (inGameFilesSection && line.StartsWith("GameFile", StringComparison.OrdinalIgnoreCase))
                        {
                            int equalsIndex = line.IndexOf('=');
                            if (equalsIndex > 0)
                            {
                                string key = line.Substring(0, equalsIndex).Trim();
                                string value = line.Substring(equalsIndex + 1).Trim();
                                
                                // Extract number from "GameFile0", "GameFile1", etc.
                                if (key.Length > 8) // "GameFile" is 8 characters
                                {
                                    string numberStr = key.Substring(8);
                                    if (int.TryParse(numberStr, out int fileNumber) && !string.IsNullOrEmpty(value))
                                    {
                                        gameFiles[fileNumber] = value;
                                    }
                                }
                            }
                        }
                    }
                }
                
                // Sort by file number and build ordered list
                var sortedFiles = gameFiles.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).ToList();
                loadOrder.AddRange(sortedFiles);
                
                if (loadOrder.Count > 0)
                {
                    Debug.Log($"TESESMLibrary: Parsed Morrowind.ini - found {loadOrder.Count} ESM file(s) in load order: {string.Join(", ", loadOrder)}");
                }
                else
                {
                    Debug.LogWarning($"TESESMLibrary: Morrowind.ini [Game Files] section is empty or invalid. Using alphabetical load order.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"TESESMLibrary: Failed to parse Morrowind.ini: {ex.Message}. Using alphabetical load order.");
            }
            
            return loadOrder;
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
