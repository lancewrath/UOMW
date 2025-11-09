using ESMSharp.TES3;
using ESMSharp.TES3.Records;
using ESMSharp.NIF;
using BSASharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ESMSharp.TES3Terrain
{
    /// <summary>
    /// Places static objects from CELL records onto the terrain
    /// </summary>
    public class PlaceStatics
    {
        private string _esm = "";
        private string _bsa = "";
        private NIFLoader _nifLoader;
        private Dictionary<string, GameObject> _loadedModels = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, TreePrototype> _treePrototypes = new Dictionary<string, TreePrototype>(StringComparer.OrdinalIgnoreCase);
        private List<TreeInstance> _treeInstances = new List<TreeInstance>();
        private BSA _bsaArchive = null;
        private Record[] _allRecords = null; // Store all records for VHGT lookup

        public PlaceStatics(string esm = "Morrowind", string bsa = "Morrowind.bsa")
        {
            _esm = Path.GetFileNameWithoutExtension(esm);
            _bsa = bsa;
            _nifLoader = new NIFLoader(_esm, _bsa);
        }

        /// <summary>
        /// Places large static structures (buildings, ruins, etc.) from CELL records
        /// </summary>
        public void PlaceLargeStructures(Record[] records, CellManager cellManager = null, Transform parent = null)
        {
            PlaceObjects(records, cellManager, parent, ObjectType.LargeStructures);
        }

        /// <summary>
        /// Places trees from CELL records as Unity terrain trees
        /// </summary>
        public void PlaceTrees(Record[] records, CellManager cellManager = null, Terrain terrain = null)
        {
            // Clear tree instances list for this placement session
            _treeInstances.Clear();
            
            PlaceObjects(records, cellManager, null, ObjectType.Trees, terrain);
            
            // Apply all tree instances to terrain at once (more efficient)
            if (terrain != null && terrain.terrainData != null && _treeInstances.Count > 0)
            {
                TerrainData terrainData = terrain.terrainData;
                TreeInstance[] existingTrees = terrainData.treeInstances;
                TreeInstance[] allTrees = new TreeInstance[existingTrees.Length + _treeInstances.Count];
                System.Array.Copy(existingTrees, allTrees, existingTrees.Length);
                System.Array.Copy(_treeInstances.ToArray(), 0, allTrees, existingTrees.Length, _treeInstances.Count);
                terrainData.treeInstances = allTrees;
                
                UnityEngine.Debug.Log($"Added {_treeInstances.Count} tree instances to terrain (total: {allTrees.Length}, unique prototypes: {_treePrototypes.Count})");
            }
        }

        /// <summary>
        /// Places grass from CELL records as Unity terrain details
        /// </summary>
        public void PlaceGrass(Record[] records, CellManager cellManager = null, Terrain terrain = null)
        {
            PlaceObjects(records, cellManager, null, ObjectType.Grass, terrain);
        }

        /// <summary>
        /// Gets the VHGT height offset for a given cell from the Land records
        /// </summary>
        private float GetCellVHGTHeight(int cellGridX, int cellGridY, Record[] allRecords)
        {
            // Look for RecordLand with matching cell coordinates
            foreach (Record rec in allRecords)
            {
                RecordLand landRecord = rec as RecordLand;
                if (landRecord != null && landRecord.subRecords != null)
                {
                    int landCellX = 0;
                    int landCellY = 0;
                    SubRecordLandVHGT vhgt = null;
                    
                    // Extract cell coordinates and VHGT from this land record
                    foreach (SubRecords subrec in landRecord.subRecords)
                    {
                        if (subrec is SubRecordLandINTV intv)
                        {
                            landCellX = (int)intv.CellX;
                            landCellY = (int)intv.CellY;
                        }
                        else if (subrec is SubRecordLandVHGT vhgtSubrec)
                        {
                            vhgt = vhgtSubrec;
                        }
                    }
                    
                    // If this land record matches our cell, return its VHGT offset
                    if (landCellX == cellGridX && landCellY == cellGridY && vhgt != null)
                    {
                        // VHGT offset is in Morrowind units, needs to be scaled
                        // Height scale factor is 8.0f (from terrain generation)
                        // Then convert to Unity units using MORROWIND_TO_TERRAIN_SCALE
                        const float HEIGHT_SCALE_FACTOR = 8.0f;
                        const float MORROWIND_TO_TERRAIN_SCALE = 64f / 8192f; // 0.0078125
                        return vhgt.offset * HEIGHT_SCALE_FACTOR * MORROWIND_TO_TERRAIN_SCALE;
                    }
                }
            }
            
            return 0f; // No land record found for this cell
        }

        /// <summary>
        /// Places statics for a single cell only (for manual testing)
        /// </summary>
        public void PlaceCellStatics(RecordCell cellRecord, Record[] allRecords, CellManager cellManager, Transform parent, Terrain terrain)
        {
            if (cellRecord == null || cellRecord.subRecords == null)
            {
                UnityEngine.Debug.LogWarning("TESCell: Cannot generate statics - cell record is null or has no subrecords");
                return;
            }

            // Skip interior cells
            bool isInterior = false;
            string cellName = null;
            int cellGridX = 0;
            int cellGridY = 0;
            foreach (SubRecords subrec in cellRecord.subRecords)
            {
                if (subrec is SubRecordCellNAME nameSubrec)
                {
                    cellName = nameSubrec.name;
                }
                else if (subrec is SubRecordCellDATA dataSubrec)
                {
                    cellGridX = dataSubrec.gridX;
                    cellGridY = dataSubrec.gridY;
                    isInterior = (dataSubrec.flags & 0x01) != 0;
                }
            }

            if (isInterior || (cellName != null && cellName.IndexOf("interior", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                UnityEngine.Debug.Log($"TESCell: Skipping interior cell at ({cellGridX}, {cellGridY})");
                return;
            }

            // Build STAT records dictionary from all records (needed for model lookup)
            Dictionary<string, RecordStat> statRecordsByName = new Dictionary<string, RecordStat>(StringComparer.OrdinalIgnoreCase);
            foreach (Record rec in allRecords)
            {
                RecordStat stat = rec as RecordStat;
                if (stat != null && stat.subRecords != null)
                {
                    string statName = null;
                    foreach (SubRecords subrec in stat.subRecords)
                    {
                        SubRecordStatNAME nameSubrec = subrec as SubRecordStatNAME;
                        if (nameSubrec != null)
                        {
                            statName = nameSubrec.name;
                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(statName))
                    {
                        statName = statName.TrimEnd('\0', ' ', '\t', '\r', '\n');
                        statName = statName.Replace("\0", "");
                        statName = statName.Trim();

                        if (!string.IsNullOrEmpty(statName) && !statRecordsByName.ContainsKey(statName))
                        {
                            statRecordsByName[statName] = stat;
                        }
                    }
                }
            }

            // Get cell GameObject
            GameObject cellParent = null;
            if (cellManager != null)
            {
                cellParent = cellManager.GetCell(cellGridX, cellGridY);
            }

            // Process references in this cell only
            SubRecordCellFRMR currentFRMR = null;
            SubRecordCellREFP currentREFP = null;
            SubRecordCellObjectID currentObjectID = null;
            float currentScale = 1.0f;
            bool seenFirstDATA = false;

            int placedTreesCount = 0;
            int failedTreesCount = 0;
            int placedStructuresCount = 0;
            int failedStructuresCount = 0;

            foreach (SubRecords subrec in cellRecord.subRecords)
            {
                if (subrec == null) continue;

                if (subrec is SubRecordCellDATA && !seenFirstDATA)
                {
                    seenFirstDATA = true;
                    continue;
                }

                if (subrec is SubRecordCellFRMR)
                {
                    if (currentObjectID != null && currentREFP != null)
                    {
                        // Place trees
                        PlaceReference(currentObjectID, currentREFP, currentScale, statRecordsByName, cellParent, cellGridX, cellGridY, ObjectType.Trees, terrain, parent, ref placedTreesCount, ref failedTreesCount, allRecords);
                        
                        // Place large structures
                        PlaceReference(currentObjectID, currentREFP, currentScale, statRecordsByName, cellParent, cellGridX, cellGridY, ObjectType.LargeStructures, terrain, parent, ref placedStructuresCount, ref failedStructuresCount, allRecords);
                    }
                    currentFRMR = subrec as SubRecordCellFRMR;
                    currentREFP = null;
                    currentObjectID = null;
                    currentScale = 1.0f;
                }
                else if (subrec is SubRecordCellObjectID)
                {
                    currentObjectID = subrec as SubRecordCellObjectID;
                }
                else if (subrec is SubRecordCellREFP)
                {
                    currentREFP = subrec as SubRecordCellREFP;
                }
                else if (subrec is SubRecordCellXSCL xscal)
                {
                    // XSCL contains the object scale
                    currentScale = xscal.scale;
                }
            }

            if (currentObjectID != null && currentREFP != null)
            {
                // Place trees
                PlaceReference(currentObjectID, currentREFP, currentScale, statRecordsByName, cellParent, cellGridX, cellGridY, ObjectType.Trees, terrain, parent, ref placedTreesCount, ref failedTreesCount, allRecords);
                
                // Place large structures
                PlaceReference(currentObjectID, currentREFP, currentScale, statRecordsByName, cellParent, cellGridX, cellGridY, ObjectType.LargeStructures, terrain, parent, ref placedStructuresCount, ref failedStructuresCount, allRecords);
            }

            UnityEngine.Debug.Log($"TESCell ({cellGridX}, {cellGridY}): Placed {placedTreesCount} trees ({failedTreesCount} failed), {placedStructuresCount} structures ({failedStructuresCount} failed)");
            
            // Apply tree instances to terrain if any were placed
            if (terrain != null && terrain.terrainData != null && _treeInstances.Count > 0)
            {
                int treeCount = _treeInstances.Count;
                TerrainData terrainData = terrain.terrainData;
                TreeInstance[] existingTrees = terrainData.treeInstances;
                TreeInstance[] allTrees = new TreeInstance[existingTrees.Length + treeCount];
                System.Array.Copy(existingTrees, allTrees, existingTrees.Length);
                System.Array.Copy(_treeInstances.ToArray(), 0, allTrees, existingTrees.Length, treeCount);
                terrainData.treeInstances = allTrees;
                
                // Force Unity to refresh the terrain (editor-only)
                #if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(terrainData);
                UnityEditor.EditorUtility.SetDirty(terrain);
                // Force scene view to repaint so trees are visible
                UnityEditor.SceneView.RepaintAll();
                // Force terrain to update its internal rendering data
                terrain.Flush();
                #endif
                
                // Verify trees were actually added to terrain data
                TreeInstance[] verifyTrees = terrainData.treeInstances;
                UnityEngine.Debug.Log($"TESCell: Added {treeCount} tree instances to terrain (total now: {allTrees.Length}, verified: {verifyTrees.Length}). " +
                    $"Terrain settings: treeDistance={terrain.treeDistance}, drawTreesAndFoliage={terrain.drawTreesAndFoliage}, " +
                    $"treePrototypes={terrainData.treePrototypes.Length}");
                
                // Log first few tree instances for verification (from the actual terrain data, not our list)
                // Also calculate world positions to help debug visibility
                if (verifyTrees.Length > 0)
                {
                    int startIdx = Mathf.Max(0, verifyTrees.Length - treeCount);
                    Vector3 terrainPos = terrain.transform.position;
                    Vector3 terrainSize = terrain.terrainData.size;
                    
                    for (int i = startIdx; i < Mathf.Min(startIdx + 3, verifyTrees.Length); i++)
                    {
                        TreeInstance ti = verifyTrees[i];
                        // Calculate world position for debugging
                        Vector3 worldPos = new Vector3(
                            terrainPos.x + ti.position.x * terrainSize.x,
                            terrainPos.y + ti.position.y * terrainSize.y,
                            terrainPos.z + ti.position.z * terrainSize.z
                        );
                        
                        UnityEngine.Debug.Log($"  Tree {i} (from terrain): prototypeIndex={ti.prototypeIndex}, " +
                            $"normalizedPos=({ti.position.x:F4},{ti.position.y:F4},{ti.position.z:F4}), " +
                            $"worldPos=({worldPos.x:F2},{worldPos.y:F2},{worldPos.z:F2}), " +
                            $"scale=({ti.widthScale:F4},{ti.heightScale:F4}), rotation={ti.rotation:F4}");
                    }
                }
                
                _treeInstances.Clear(); // Clear after applying
            }
        }

        /// <summary>
        /// Object type filter for placement
        /// </summary>
        private enum ObjectType
        {
            LargeStructures,
            Trees,
            Grass
        }

        /// <summary>
        /// Internal method to place objects with filtering
        /// </summary>
        private void PlaceObjects(Record[] records, CellManager cellManager = null, Transform parent = null, ObjectType objectType = ObjectType.LargeStructures, Terrain terrain = null)
        {
            // Build a dictionary of STAT records by their NAME (model ID)
            Dictionary<string, RecordStat> statRecordsByName = new Dictionary<string, RecordStat>(StringComparer.OrdinalIgnoreCase);
            foreach (Record rec in records)
            {
                RecordStat stat = rec as RecordStat;
                if (stat != null && stat.subRecords != null)
                {
                    // Get the NAME from STAT record
                    string statName = null;
                    foreach (SubRecords subrec in stat.subRecords)
                    {
                        SubRecordStatNAME nameSubrec = subrec as SubRecordStatNAME;
                        if (nameSubrec != null)
                        {
                            statName = nameSubrec.name;
                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(statName))
                    {
                        // Clean the name
                        statName = statName.TrimEnd('\0', ' ', '\t', '\r', '\n');
                        statName = statName.Replace("\0", "");
                        statName = statName.Trim();

                        if (!string.IsNullOrEmpty(statName) && !statRecordsByName.ContainsKey(statName))
                        {
                            statRecordsByName[statName] = stat;
                        }
                    }
                }
            }

            UnityEngine.Debug.Log($"Found {statRecordsByName.Count} STAT records by name");

            // Iterate through CELL records
            int cellCount = 0;
            int placedCount = 0;
            int failedCount = 0;

            foreach (Record rec in records)
            {
                RecordCell cell = rec as RecordCell;
                if (cell == null)
                    continue;

                // Check if cell has subrecords
                if (cell.subRecords == null)
                {
                    UnityEngine.Debug.LogWarning("Cell has no subrecords, skipping");
                    continue;
                }

                // Skip interior cells
                bool isInterior = false;
                string cellName = null;
                foreach (SubRecords subrec in cell.subRecords)
                {
                    if (subrec is SubRecordCellNAME nameSubrec)
                    {
                        cellName = nameSubrec.name;
                    }
                    else if (subrec is SubRecordCellDATA dataSubrec)
                    {
                        // Check if interior (flag 0x01)
                        isInterior = (dataSubrec.flags & 0x01) != 0;
                    }
                }
                
                // Skip if interior or if cell name contains "interior" (case-insensitive)
                if (isInterior || (cellName != null && cellName.IndexOf("interior", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    continue;
                }

                // Get cell grid coordinates to find the cell GameObject
                int cellGridX = 0;
                int cellGridY = 0;
                GameObject cellParent = null;

                // Process references in this cell
                // CELL reference structure: FRMR -> NAME (cell name) -> NAME (Object ID/model ID) -> DATA (REFP) -> XSCL (optional)
                // We need to track these in sequence
                SubRecordCellFRMR currentFRMR = null;
                SubRecordCellREFP currentREFP = null;
                SubRecordCellObjectID currentObjectID = null; // This is the model ID we need!
                float currentScale = 1.0f;

                // Track if we've seen the first DATA (cell data, not REFP)
                bool seenFirstDATA = false;

                foreach (SubRecords subrec in cell.subRecords)
                {
                    // Skip null subrecords
                    if (subrec == null)
                        continue;

                    // Get cell grid coordinates from first DATA
                    if (subrec is SubRecordCellDATA && !seenFirstDATA)
                    {
                        SubRecordCellDATA data = subrec as SubRecordCellDATA;
                        cellGridX = data.gridX;
                        cellGridY = data.gridY;
                        seenFirstDATA = true;

                        // Get the cell GameObject from CellManager
                        if (cellManager != null)
                        {
                            cellParent = cellManager.GetCell(cellGridX, cellGridY);
                        }
                        continue;
                    }

                    // Process subrecords in order
                    if (subrec is SubRecordCellFRMR)
                    {
                        // FRMR indicates start of a new reference
                        // If we have a complete previous reference, place it first
                        if (currentObjectID != null && currentREFP != null)
                        {
                            PlaceReference(currentObjectID, currentREFP, currentScale, statRecordsByName, cellParent, cellGridX, cellGridY, objectType, terrain, parent, ref placedCount, ref failedCount);
                        }
                        
                        // Start new reference
                        currentFRMR = subrec as SubRecordCellFRMR;
                        currentREFP = null;
                        currentObjectID = null;
                        currentScale = 1.0f;
                    }
                    else if (subrec is SubRecordCellObjectID)
                    {
                        // Second NAME record is the Object ID (model ID for statics)
                        currentObjectID = subrec as SubRecordCellObjectID;
                    }
                    else if (subrec is SubRecordCellREFP)
                    {
                        currentREFP = subrec as SubRecordCellREFP;
                    }
                    else if (subrec is SubRecordCellXSCL xscal)
                    {
                        // XSCL contains the object scale
                        currentScale = xscal.scale;
                    }
                }

                // Place the last reference if we have one
                if (currentObjectID != null && currentREFP != null)
                {
                    PlaceReference(currentObjectID, currentREFP, currentScale, statRecordsByName, cellParent, cellGridX, cellGridY, objectType, terrain, parent, ref placedCount, ref failedCount);
                }

                if (cellParent != null)
                {
                    cellCount++;
                }
            }

            string typeName = objectType.ToString();
            UnityEngine.Debug.Log($"Placed {placedCount} {typeName} from {cellCount} cells ({failedCount} failed)");
            
            // Clean up BSA archive if opened
            if (_bsaArchive != null)
            {
                _bsaArchive.Close();
                _bsaArchive = null;
            }
        }

        /// <summary>
        /// Places a tree as a Unity terrain tree
        /// </summary>
        private bool PlaceTree(string modelFilename, SubRecordCellREFP refp, float scale, SubRecordCellObjectID objectId, Terrain terrain, int cellGridX, int cellGridY)
        {
            if (terrain == null || terrain.terrainData == null)
            {
                return false;
            }

            try
            {
                // Strip any subdirectory paths from filename (use just the base filename)
                string baseFilename = Path.GetFileName(modelFilename);
                
                // Get or create tree prototype (reuse if already loaded)
                TreePrototype treePrototype = null;
                int prototypeIndex = -1;
                
                if (!_treePrototypes.TryGetValue(baseFilename, out treePrototype))
                {
                    // Load the NIF model with meshes combined (required for Unity terrain trees)
                    GameObject treeModel = _nifLoader.LoadNIFFromCache(baseFilename, combineMeshes: true);
                    if (treeModel == null)
                    {
                        UnityEngine.Debug.LogWarning($"Failed to load tree model: {baseFilename}");
                        return false;
                    }

                    // Create tree prototype from the model
                    treePrototype = CreateTreePrototypeFromModel(treeModel, baseFilename);
                    if (treePrototype == null)
                    {
                        UnityEngine.Debug.LogWarning($"Failed to create tree prototype from: {baseFilename}");
                        return false;
                    }

                    // Add prototype to terrain data
                    TerrainData terrainData = terrain.terrainData;
                    TreePrototype[] existingPrototypes = terrainData.treePrototypes;
                    TreePrototype[] newPrototypes = new TreePrototype[existingPrototypes.Length + 1];
                    System.Array.Copy(existingPrototypes, newPrototypes, existingPrototypes.Length);
                    newPrototypes[existingPrototypes.Length] = treePrototype;
                    terrainData.treePrototypes = newPrototypes;
                    
                    prototypeIndex = existingPrototypes.Length;
                    _treePrototypes[baseFilename] = treePrototype;
                    
                    UnityEngine.Debug.Log($"Created tree prototype for: {baseFilename} (index {prototypeIndex})");
                }
                else
                {
                    // Find the prototype index in terrain data
                    TreePrototype[] prototypes = terrain.terrainData.treePrototypes;
                    for (int i = 0; i < prototypes.Length; i++)
                    {
                        if (prototypes[i] == treePrototype)
                        {
                            prototypeIndex = i;
                            break;
                        }
                    }
                    
                    if (prototypeIndex == -1)
                    {
                        UnityEngine.Debug.LogWarning($"Tree prototype not found in terrain data: {baseFilename}");
                        return false;
                    }
                }

                // Convert Morrowind position to terrain-relative position (0-1 range)
                // According to MWSE docs: https://mwse.github.io/MWSE/references/general/game-units/
                // - Exterior cells are 8192 x 8192 units in Morrowind's world coordinate system
                // - REFP coordinates are WORLD coordinates in Morrowind's coordinate system
                // - Our terrain uses 64 units per cell (STEP = 64)
                // - Scale factor: 64 / 8192 = 1/128 to convert from Morrowind world to terrain world
                // Morrowind coordinates: X=East, Y=Up, Z=North
                // Unity terrain: X=East, Y=Up, Z=South (flipped)
                // Terrain is positioned at (-cell0X, 0, -cell0Z) where cell0X/cell0Z are calculated to align cell (0,0) at world (0,0,0)
                
                // Scale factor to convert from Morrowind world coordinates (8192 units/cell) to terrain coordinates (64 units/cell)
                const float MORROWIND_TO_TERRAIN_SCALE = 64f / 8192f; // = 1/128 = 0.0078125
                
                // REFP coordinates in Morrowind are stored as (X, Z, Y) not (X, Y, Z)
                // refp.x = Morrowind X (East)
                // refp.y = Morrowind Z (North) - this is the horizontal coordinate we need for Unity Z
                // refp.z = Morrowind Y (Up/Height) - this is the vertical coordinate
                // Scale them to match our terrain coordinate system (64 units per cell)
                Vector3 morrowindWorldPos = new Vector3(
                    refp.x * MORROWIND_TO_TERRAIN_SCALE,  // East (scaled to terrain units)
                    refp.z,                                // Up (height, doesn't need scaling)
                    refp.y * MORROWIND_TO_TERRAIN_SCALE    // North (scaled to terrain units) - using refp.y for Z
                );
                
                // Validate coordinates - skip if Y (height) is way too high (likely invalid or interior cell)
                // Morrowind terrain height is typically 0-1000, so values > 10000 are likely invalid
                // Note: refp.z is the Y/Height coordinate in REFP format
                if (Mathf.Abs(refp.z) > 10000f)
                {
                    if (_treeInstances.Count < 5)
                    {
                        UnityEngine.Debug.LogWarning($"Tree has invalid Y coordinate (likely interior cell or corrupted data): " +
                            $"cell=({cellGridX},{cellGridY}), refp.z={refp.z} (Y/Height)");
                    }
                    return false;
                }
                
                // Get terrain bounds and position
                // NOTE: terrain.transform.position is calculated dynamically in GenerateUnityTerrain based on MinCellX/MinCellY
                // It's positioned at (-cell0X, 0, -cell0Z) where:
                //   cell0X = Math.Abs(MinCellX) * STEP + HALF
                //   cell0Z = Math.Abs(MinCellY) * STEP + HALF
                // This ensures cell (0,0) is at world origin (0,0,0) regardless of the map's cell bounds
                Vector3 terrainPosition = terrain.transform.position;
                Vector3 terrainSize = terrain.terrainData.size;
                
                // Convert to Unity world coordinates (flip Z: Morrowind Z=North becomes Unity Z=-South)
                // Account for terrain position offset
                Vector3 unityWorldPosition = new Vector3(
                    morrowindWorldPos.x + terrainPosition.x,  // Account for terrain offset
                    morrowindWorldPos.y,                      // Y will be set to 0 for terrain surface
                    -morrowindWorldPos.z + terrainPosition.z // Flip Z and account for terrain offset
                );
                
                // Convert Unity world position to terrain-relative position (0-1)
                // Account for terrain offset by subtracting the terrain position to get terrain-local coordinates
                // This works for any map because we're using the actual terrain position, not hardcoded values
                float normalizedX = (unityWorldPosition.x - terrainPosition.x) / terrainSize.x;
                float normalizedZ = (unityWorldPosition.z - terrainPosition.z) / terrainSize.z;
                
                // Validate that the tree is within terrain bounds
                // Skip trees that are way outside the terrain (likely invalid coordinates or interior cells that slipped through)
                if (normalizedX < -0.1f || normalizedX > 1.1f || normalizedZ < -0.1f || normalizedZ > 1.1f)
                {
                    if (_treeInstances.Count < 5)
                    {
                        UnityEngine.Debug.LogWarning($"Tree outside terrain bounds, skipping: normalized=({normalizedX},{normalizedZ}), " +
                            $"world=({unityWorldPosition.x},{unityWorldPosition.z}), terrain bounds=({terrainPosition.x} to {terrainPosition.x + terrainSize.x}, " +
                            $"{terrainPosition.z} to {terrainPosition.z + terrainSize.z})");
                    }
                    return false;
                }
                
                // Clamp to terrain bounds (allow slight overflow for edge cases)
                normalizedX = Mathf.Clamp01(normalizedX);
                normalizedZ = Mathf.Clamp01(normalizedZ);
                
                // Get terrain height at this position (in world space)
                // Unity's SampleHeight returns the height in world space
                float terrainHeight = terrain.SampleHeight(unityWorldPosition);
                
                // For TreeInstance.position, Y should be the normalized height offset from terrain base
                // Trees should always be placed on the terrain surface
                // terrainHeight is in world space, so we need to convert it to normalized (0-1) relative to terrain base
                float heightOffset = (terrainHeight - terrainPosition.y) / terrainSize.y; // Normalized height (0 = base, 1 = top)
                
                // Debug log for first few trees to verify placement
                if (_treeInstances.Count < 5)
                {
                    // Calculate expected cell position in Unity world coordinates (accounting for terrain offset)
                    float expectedCellX = cellGridX * 64f + terrainPosition.x; // Each cell is 64 units, plus terrain offset
                    float expectedCellZ = cellGridY * 64f + terrainPosition.z;
                    
                    UnityEngine.Debug.Log($"Tree placement: cell=({cellGridX},{cellGridY}), " +
                        $"refp(REFP format: X={refp.x}, Z={refp.y}, Y={refp.z}), " +
                        $"scaleFactor={MORROWIND_TO_TERRAIN_SCALE}, " +
                        $"morrowindWorld(scaled)=({morrowindWorldPos.x},{morrowindWorldPos.y},{morrowindWorldPos.z}), " +
                        $"unityWorld=({unityWorldPosition.x},{unityWorldPosition.y},{unityWorldPosition.z}), " +
                        $"terrainPos=({terrainPosition.x},{terrainPosition.y},{terrainPosition.z}), " +
                        $"terrainSize=({terrainSize.x},{terrainSize.y},{terrainSize.z}), " +
                        $"normalized=({normalizedX},{normalizedZ}), " +
                        $"terrainHeight={terrainHeight}, " +
                        $"heightOffset(normalized)={heightOffset}, " +
                        $"treeInstancePos=({normalizedX},{heightOffset},{normalizedZ}), " +
                        $"expectedCellCenter=({expectedCellX},{expectedCellZ})");
                }
                
                // Create tree instance
                TreeInstance treeInstance = new TreeInstance();
                treeInstance.prototypeIndex = prototypeIndex;
                treeInstance.position = new Vector3(normalizedX, heightOffset, normalizedZ); // Y is normalized height on terrain
                treeInstance.widthScale = scale;
                treeInstance.heightScale = scale;
                treeInstance.color = Color.white;
                treeInstance.lightmapColor = Color.white;
                
                // Convert Morrowind rotation to tree rotation
                // Trees in Unity terrain use rotation around Y axis (in radians)
                float rotationY = refp.yaw * Mathf.Deg2Rad;
                treeInstance.rotation = rotationY;
                
                // Debug: Log tree instance details for first few trees
                if (_treeInstances.Count < 5)
                {
                    UnityEngine.Debug.Log($"Tree instance created: prototypeIndex={prototypeIndex}, " +
                        $"position=({treeInstance.position.x},{treeInstance.position.y},{treeInstance.position.z}), " +
                        $"widthScale={treeInstance.widthScale}, heightScale={treeInstance.heightScale}, " +
                        $"rotation={treeInstance.rotation} (yaw={refp.yaw}°), " +
                        $"prototype valid={treePrototype != null}, prefab={treePrototype?.prefab?.name ?? "null"}");
                }
                
                _treeInstances.Add(treeInstance);
                
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error placing tree {modelFilename}: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Creates a TreePrototype from a loaded NIF model GameObject
        /// Unity terrain requires the prefab to have a MeshRenderer on the root GameObject
        /// </summary>
        private TreePrototype CreateTreePrototypeFromModel(GameObject treeModel, string modelName)
        {
            if (treeModel == null)
            {
                return null;
            }

            // Unity terrain trees REQUIRE the MeshRenderer to be on the root GameObject
            // Check root first
            MeshFilter meshFilter = treeModel.GetComponent<MeshFilter>();
            MeshRenderer meshRenderer = treeModel.GetComponent<MeshRenderer>();
            
            // If not on root, check children (but this shouldn't happen for combined meshes)
            if (meshFilter == null || meshRenderer == null)
            {
                meshFilter = treeModel.GetComponentInChildren<MeshFilter>();
                meshRenderer = treeModel.GetComponentInChildren<MeshRenderer>();
                
                // If mesh is on a child, we need to move it to root for Unity terrain
                if (meshFilter != null && meshRenderer != null && meshFilter.sharedMesh != null)
                {
                    UnityEngine.Debug.LogWarning($"Tree model {modelName} has mesh on child object. Moving to root for Unity terrain compatibility.");
                    
                    // Move mesh components to root
                    Mesh mesh = meshFilter.sharedMesh;
                    Material material = meshRenderer.sharedMaterial;
                    
                    // Remove from child
                    UnityEngine.Object.DestroyImmediate(meshFilter);
                    UnityEngine.Object.DestroyImmediate(meshRenderer);
                    
                    // Add to root
                    meshFilter = treeModel.AddComponent<MeshFilter>();
                    meshFilter.sharedMesh = mesh;
                    meshRenderer = treeModel.AddComponent<MeshRenderer>();
                    meshRenderer.sharedMaterial = material;
                }
            }
            
            if (meshRenderer == null || meshFilter == null || meshFilter.sharedMesh == null)
            {
                UnityEngine.Debug.LogError($"Tree model {modelName} has no valid mesh renderer/filter on root. " +
                    $"MeshFilter: {meshFilter != null}, MeshRenderer: {meshRenderer != null}, " +
                    $"Mesh: {(meshFilter != null ? meshFilter.sharedMesh != null : false)}. " +
                    $"Unity terrain requires mesh renderer on root GameObject.");
                return null;
            }

            // Create tree prototype
            TreePrototype prototype = new TreePrototype();
            prototype.prefab = treeModel; // Unity will use the prefab for rendering
            prototype.bendFactor = 0.0f; // Trees don't bend in Morrowind
            
            UnityEngine.Debug.Log($"Created tree prototype for {modelName}: {meshFilter.sharedMesh.vertexCount} vertices, {meshFilter.sharedMesh.triangles.Length / 3} triangles");
            
            return prototype;
        }

        /// <summary>
        /// Places grass as a Unity terrain detail
        /// </summary>
        private bool PlaceGrassDetail(string modelFilename, SubRecordCellREFP refp, float scale, SubRecordCellObjectID objectId, Terrain terrain, int cellGridX, int cellGridY)
        {
            // TODO: Implement Unity terrain detail placement
            // For now, just log that we found grass
            UnityEngine.Debug.Log($"Grass found: {modelFilename} at cell ({cellGridX}, {cellGridY}), position ({refp.x}, {refp.z}, {-refp.y})");
            return false; // Not implemented yet
        }

        /// <summary>
        /// Places a reference from a CELL record using the Object ID (model ID) to look up the STAT record
        /// </summary>
        private void PlaceReference(SubRecordCellObjectID objectId, SubRecordCellREFP refp, float scale, Dictionary<string, RecordStat> statRecordsByName, GameObject cellParent, int cellGridX, int cellGridY, ObjectType objectType, Terrain terrain, Transform parent, ref int placedCount, ref int failedCount, Record[] allRecords = null)
        {
            if (objectId == null || string.IsNullOrEmpty(objectId.objectId))
            {
                UnityEngine.Debug.LogWarning("Reference has no Object ID (model ID)");
                failedCount++;
                return;
            }

            // Clean the model ID name
            string modelId = objectId.objectId.TrimEnd('\0', ' ', '\t', '\r', '\n');
            modelId = modelId.Replace("\0", "");
            modelId = modelId.Trim();

            string modelFilename = null;

            // First, try to look up STAT record by name
            if (statRecordsByName.ContainsKey(modelId))
            {
                RecordStat statRecord = statRecordsByName[modelId];

                // Get model filename from STAT record
                foreach (SubRecords statSubrec in statRecord.subRecords)
                {
                    SubRecordStatMODL modl = statSubrec as SubRecordStatMODL;
                    if (modl != null)
                    {
                        modelFilename = modl.model;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(modelFilename))
                {
                    // Clean the model filename
                    modelFilename = modelFilename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    modelFilename = modelFilename.Replace("\0", "");
                    modelFilename = modelFilename.Trim();
                }
            }

            // If STAT record lookup failed, try to find NIF file directly in cache
            // The model ID might match a filename in the cache
            if (string.IsNullOrEmpty(modelFilename))
            {
                string cacheDir = Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", _esm);
                if (Directory.Exists(cacheDir))
                {
                    // Try model ID as filename with various extensions
                    string[] extensions = { ".nif", ".NIF" };
                    foreach (string ext in extensions)
                    {
                        string testPath = Path.Combine(cacheDir, modelId + ext);
                        if (File.Exists(testPath))
                        {
                            modelFilename = Path.GetFileName(testPath);
                            UnityEngine.Debug.Log($"Found NIF file directly in cache for model ID '{modelId}': {modelFilename}");
                            break;
                        }
                    }

                    // If still not found, try case-insensitive search (including subdirectories)
                    if (string.IsNullOrEmpty(modelFilename))
                    {
                        string[] files = Directory.GetFiles(cacheDir, "*.nif", SearchOption.AllDirectories);
                        foreach (string file in files)
                        {
                            string fileName = Path.GetFileNameWithoutExtension(file);
                            if (string.Equals(fileName, modelId, StringComparison.OrdinalIgnoreCase))
                            {
                                // Use just the filename, not the full path (strip subdirectories)
                                modelFilename = Path.GetFileName(file);
                                UnityEngine.Debug.Log($"Found NIF file in cache (case-insensitive) for model ID '{modelId}': {modelFilename}");
                                break;
                            }
                        }
                    }
                }
            }

            // If still not found, try to extract from BSA
            if (string.IsNullOrEmpty(modelFilename))
            {
                modelFilename = TryExtractFromBSA(modelId);
            }

            if (string.IsNullOrEmpty(modelFilename))
            {
                UnityEngine.Debug.LogWarning($"Could not find model for ID '{modelId}' (checked STAT records and cache directory)");
                failedCount++;
                return;
            }

            // Filter based on object type
            string baseFilename = Path.GetFileNameWithoutExtension(modelFilename).ToLower();
            
            // Check if this object matches the requested type
            bool shouldPlace = false;
            switch (objectType)
            {
                case ObjectType.LargeStructures:
                    // Place anything that isn't a tree or grass
                    shouldPlace = !IsTreeModel(baseFilename) && !IsGrassModel(baseFilename);
                    break;
                case ObjectType.Trees:
                    shouldPlace = IsTreeModel(baseFilename);
                    break;
                case ObjectType.Grass:
                    shouldPlace = IsGrassModel(baseFilename);
                    break;
            }
            
            if (!shouldPlace)
            {
                // Skip objects that don't match the requested type
                return;
            }

            // Place the object based on type
            bool success = false;
            if (objectType == ObjectType.Trees && terrain != null)
            {
                success = PlaceTree(modelFilename, refp, scale, objectId, terrain, cellGridX, cellGridY);
            }
            else if (objectType == ObjectType.Grass && terrain != null)
            {
                success = PlaceGrassDetail(modelFilename, refp, scale, objectId, terrain, cellGridX, cellGridY);
            }
            else if (objectType == ObjectType.LargeStructures)
            {
                Transform refParent = cellParent != null ? cellParent.transform : parent;
                success = PlaceStaticObject(modelFilename, refp, scale, objectId, refParent, cellGridX, cellGridY, allRecords);
            }
            
            if (success)
            {
                placedCount++;
            }
            else
            {
                failedCount++;
            }
        }

        /// <summary>
        /// Checks if a model is a tree (should be placed as terrain tree)
        /// </summary>
        private bool IsTreeModel(string baseFilename)
        {
            // Common tree-related keywords in Morrowind
            return baseFilename.Contains("tree") || 
                   baseFilename.Contains("Tree") ||
                   baseFilename.Contains("TREE") ||
                   baseFilename.Contains("flora_tree") ||
                   baseFilename.Contains("flora_bush") ||
                   baseFilename.Contains("flora_plant");
        }

        /// <summary>
        /// Checks if a model is grass (should be placed as terrain detail)
        /// </summary>
        private bool IsGrassModel(string baseFilename)
        {
            return baseFilename.Contains("grass") || 
                   baseFilename.Contains("Grass") ||
                   baseFilename.Contains("GRASS");
        }

        /// <summary>
        /// Checks if a model is a large object that should be placed as a static
        /// </summary>
        private bool IsLargeObject(string baseFilename)
        {
            // Exterior meshes (buildings, structures)
            if (baseFilename.StartsWith("ex_") || baseFilename.StartsWith("Ex_") || baseFilename.StartsWith("EX_"))
            {
                return true;
            }

            // Large structure keywords
            string[] largeObjectKeywords = new string[]
            {
                "building", "house", "mansion", "ruin", "ruins",
                "lighthouse", "stronghold", "fort", "fortress",
                "shipwreck", "bridge", "tower", "statue", "statues",
                "gg_fence", "wall", "castle", "keep", "temple",
                "shrine", "tomb", "crypt", "dungeon", "cave"
            };

            foreach (string keyword in largeObjectKeywords)
            {
                if (baseFilename.Contains(keyword))
                {
                    return true;
                }
            }

            // If none of the above, it's likely a small detail mesh - skip it
            return false;
        }

        /// <summary>
        /// Tries to extract a NIF file from BSA archive
        /// </summary>
        private string TryExtractFromBSA(string modelId)
        {
            try
            {
                // Open BSA if not already open
                if (_bsaArchive == null)
                {
                    string bsaPath = Path.Combine(Application.dataPath, "StreamingAssets", "Data", _bsa);
                    if (!File.Exists(bsaPath))
                    {
                        UnityEngine.Debug.LogWarning($"BSA file not found: {bsaPath}");
                        return null;
                    }

                    _bsaArchive = new BSA();
                    _bsaArchive.Open(bsaPath);
                }

                // Get all file names from BSA
                HashSet<string> bsaFileNames = new HashSet<string>(_bsaArchive.GetFileNames(), StringComparer.OrdinalIgnoreCase);

                // Try to find the model with various path variations
                string[] pathVariations = new string[]
                {
                    modelId + ".nif",
                    modelId + ".NIF",
                    "meshes\\" + modelId + ".nif",
                    "meshes/" + modelId + ".nif",
                    "Meshes\\" + modelId + ".nif",
                    "Meshes/" + modelId + ".nif"
                };

                string foundPath = null;
                foreach (string pathVar in pathVariations)
                {
                    if (bsaFileNames.Contains(pathVar))
                    {
                        foundPath = pathVar;
                        break;
                    }
                }

                // Case-insensitive fallback
                if (foundPath == null)
                {
                    string modelLower = modelId.ToLower() + ".nif";
                    foreach (string bsaFileName in bsaFileNames)
                    {
                        if (bsaFileName.ToLower().EndsWith("\\" + modelLower) ||
                            bsaFileName.ToLower().EndsWith("/" + modelLower) ||
                            bsaFileName.ToLower() == modelLower)
                        {
                            foundPath = bsaFileName;
                            break;
                        }
                    }
                }

                if (foundPath != null)
                {
                    // Extract to cache
                    BSAFileEntry entry = _bsaArchive.GetFileEntry(foundPath);
                    if (entry != null)
                    {
                        byte[] modelData = _bsaArchive.ExtractFile(entry);
                        string cacheDir = Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", _esm);
                        Directory.CreateDirectory(cacheDir);
                        
                        // Use just the filename (strip subdirectories)
                        string outputFilename = Path.GetFileName(foundPath);
                        string outputPath = Path.Combine(cacheDir, outputFilename);
                        File.WriteAllBytes(outputPath, modelData);
                        
                        UnityEngine.Debug.Log($"Extracted NIF from BSA: {foundPath} -> {outputFilename}");
                        return outputFilename;
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error extracting NIF from BSA for model ID '{modelId}': {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Places a single static object at the specified position
        /// </summary>
        private bool PlaceStaticObject(string modelFilename, SubRecordCellREFP refp, float scale, SubRecordCellObjectID objectId, Transform parent, int cellGridX = 0, int cellGridY = 0, Record[] allRecords = null)
        {
            try
            {
                // Strip any subdirectory paths from filename (use just the base filename)
                string baseFilename = Path.GetFileName(modelFilename);
                
                // Convert Morrowind world coordinates to Unity world coordinates
                // Morrowind uses 8192 units per cell, Unity terrain uses 64 units per cell
                // Same conversion as trees: scale by 64/8192 = 1/128
                const float MORROWIND_TO_TERRAIN_SCALE = 64f / 8192f; // 0.0078125
                
                // REFP coordinates in Morrowind are stored as (X, Z, Y) not (X, Y, Z)
                // refp.x = Morrowind X (East) - world coordinate
                // refp.y = Morrowind Z (North) - world coordinate
                // refp.z = Morrowind Y (Up/Height) - world coordinate
                // Scale to match terrain coordinate system (64 units per cell instead of 8192)
                //
                // Important: Static objects in Morrowind use absolute coordinates relative to the cell's origin,
                // not dynamically calculated from terrain height. The Z coordinate (Y in Unity) is an absolute
                // value that determines the object's height relative to the cell's reference point.
                // VHGT is used for terrain generation, but static objects are placed independently at fixed coordinates.
                float scaledX = refp.x * MORROWIND_TO_TERRAIN_SCALE;
                float scaledZ = refp.y * MORROWIND_TO_TERRAIN_SCALE; // Use refp.y for Z (North coordinate)
                float scaledY = refp.z * MORROWIND_TO_TERRAIN_SCALE; // Use refp.z for Y (Height coordinate) - absolute, not terrain-relative
                
                // REFP coordinates are already in world space, so we use the scaled values directly
                // No need to add terrain offset - the scaled coordinates are already correct world positions
                // Don't negate Z - the user confirmed the normal value is correct
                // Use the actual scaled Y coordinate from REFP directly (absolute position, not terrain-relative)
                Vector3 unityPosition = new Vector3(
                    scaledX,      // X position in world space
                    scaledY,      // Y position (height) from REFP coordinates - absolute position relative to cell origin
                    scaledZ       // Z position in world space (no negation)
                );
                
                // Convert Morrowind rotation to Unity rotation
                // Morrowind uses Euler angles in radians: roll, yaw, pitch
                // Unity uses degrees: X, Y, Z
                // Pitch and yaw are swapped: Morrowind pitch -> Unity Y, Morrowind yaw -> Unity X
                // Convert radians to degrees using Mathf.Rad2Deg
                Quaternion unityRotation = Quaternion.Euler(
                    refp.yaw * Mathf.Rad2Deg,      // Yaw -> X (converted to degrees)
                    -refp.pitch * Mathf.Rad2Deg,   // Pitch -> Y (negated, converted to degrees)
                    -refp.roll * Mathf.Rad2Deg     // Roll -> Z (negated, converted to degrees)
                );
                
                // Check if we've already loaded this model (mesh instancing)
                GameObject templateModel = null;
                if (_loadedModels.TryGetValue(baseFilename, out templateModel))
                {
                    // Instantiate the existing model instead of loading again
                    GameObject instanceObj = GameObject.Instantiate(templateModel);
                    instanceObj.name = templateModel.name; // Unity adds "(Clone)" automatically
                    
                    // Set position, rotation, scale BEFORE parenting
                    instanceObj.transform.position = unityPosition;
                    instanceObj.transform.rotation = unityRotation;
                    // XSCL scale multiplies the base GameObject scale (MORROWIND_TO_TERRAIN_SCALE = 0.0078125), not replaces it
                    // Base scale is already applied in NIFLoader, so we multiply by XSCL here
                    instanceObj.transform.localScale = instanceObj.transform.localScale * scale;

                    // Set name
                    if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                    {
                        instanceObj.name = objectId.objectId.TrimEnd('\0');
                    }

                    // Add LOD component
                    AddLODToObject(instanceObj);

                    // Debug: Log position before parenting
                    Vector3 instancePosBeforeParent = instanceObj.transform.position;
                    
                    // Parent to terrain or specified parent
                    // Use worldPositionStays: true to preserve world position when parenting
                    // This prevents Unity from converting world position to local coordinates
                    if (parent != null)
                    {
                        UnityEngine.Debug.Log($"Before parenting: worldPos=({instancePosBeforeParent.x:F2}, {instancePosBeforeParent.y:F2}, {instancePosBeforeParent.z:F2}), " +
                            $"parent worldPos=({parent.position.x:F2}, {parent.position.y:F2}, {parent.position.z:F2}), " +
                            $"parent localPos=({parent.localPosition.x:F2}, {parent.localPosition.y:F2}, {parent.localPosition.z:F2})");
                        instanceObj.transform.SetParent(parent, worldPositionStays: true);
                    }
                    
                    // Debug: Log position after parenting
                    Vector3 instancePosAfterParent = instanceObj.transform.position;
                    Vector3 instanceLocalPosAfterParent = instanceObj.transform.localPosition;
                    
                    // Calculate expected cell position in Unity coordinates
                    float expectedCellX = cellGridX * 64f; // Each cell is 64 units
                    float expectedCellZ = cellGridY * 64f;
                    
                    UnityEngine.Debug.Log($"Placed static object instance: {instanceObj.name} " +
                        $"worldPos=({instancePosAfterParent.x:F2}, {instancePosAfterParent.y:F2}, {instancePosAfterParent.z:F2}), " +
                        $"localPos=({instanceLocalPosAfterParent.x:F2}, {instanceLocalPosAfterParent.y:F2}, {instanceLocalPosAfterParent.z:F2}), " +
                        $"(Morrowind REFP: x={refp.x:F2} (X), y={refp.y:F2} (Z/North), z={refp.z:F2} (Y/Height), scaled: ({scaledX:F2}, {scaledY:F2}, {scaledZ:F2}), " +
                        $"cell=({cellGridX},{cellGridY}), expectedCellCenter=({expectedCellX:F2},0,{expectedCellZ:F2}))");

                    return true;
                }

                // Load the NIF model for the first time
                GameObject modelObj = _nifLoader.LoadNIFFromCache(baseFilename);
                if (modelObj == null)
                {
                    UnityEngine.Debug.LogWarning($"Failed to load NIF model: {baseFilename}");
                    return false;
                }

                // Store the loaded model for future instancing
                _loadedModels[baseFilename] = modelObj;

                // Set position and rotation BEFORE parenting
                modelObj.transform.position = unityPosition;
                modelObj.transform.rotation = unityRotation;
                // XSCL scale multiplies the base GameObject scale (MORROWIND_TO_TERRAIN_SCALE = 0.0078125), not replaces it
                // Base scale is already applied in NIFLoader, so we multiply by XSCL here
                modelObj.transform.localScale = modelObj.transform.localScale * scale;

                // Set name
                if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                {
                    modelObj.name = objectId.objectId.TrimEnd('\0');
                }
                else
                {
                    modelObj.name = Path.GetFileNameWithoutExtension(modelFilename);
                }

                // Add LOD component for distance-based culling
                // Add to root and all children that have renderers
                AddLODToObject(modelObj);

                // Debug: Log position before parenting
                Vector3 modelPosBeforeParent = modelObj.transform.position;
                
                // Parent to terrain or specified parent
                // Use worldPositionStays: true to preserve world position when parenting
                // This prevents Unity from converting world position to local coordinates
                if (parent != null)
                {
                    UnityEngine.Debug.Log($"Before parenting (new model): worldPos=({modelPosBeforeParent.x:F2}, {modelPosBeforeParent.y:F2}, {modelPosBeforeParent.z:F2}), " +
                        $"parent worldPos=({parent.position.x:F2}, {parent.position.y:F2}, {parent.position.z:F2})");
                    modelObj.transform.SetParent(parent, worldPositionStays: true);
                }
                
                // Debug: Log position after parenting
                Vector3 modelPosAfterParent = modelObj.transform.position;
                UnityEngine.Debug.Log($"After parenting (new model): worldPos=({modelPosAfterParent.x:F2}, {modelPosAfterParent.y:F2}, {modelPosAfterParent.z:F2})");

                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error placing static object {modelFilename}: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Adds LOD component to an object and all its children with renderers
        /// </summary>
        private void AddLODToObject(GameObject obj)
        {
            // Add LOD to root if it has a renderer
            if (obj.GetComponent<Renderer>() != null)
            {
                StaticObjectLOD lod = obj.GetComponent<StaticObjectLOD>();
                if (lod == null)
                {
                    lod = obj.AddComponent<StaticObjectLOD>();
                    lod.cullDistance = 100f; // Default cull distance
                    lod.checkInterval = 0.1f; // Check every 0.1 seconds
                }
            }

            // Add LOD to all children with renderers
            Renderer[] childRenderers = obj.GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in childRenderers)
            {
                if (renderer.gameObject != obj) // Skip root (already handled)
                {
                    StaticObjectLOD lod = renderer.GetComponent<StaticObjectLOD>();
                    if (lod == null)
                    {
                        lod = renderer.gameObject.AddComponent<StaticObjectLOD>();
                        lod.cullDistance = 100f;
                        lod.checkInterval = 0.1f;
                    }
                }
            }
        }
    }
}

