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
        // Model cache removed - now using global TESNifLibrary
        private Dictionary<string, TreePrototype> _treePrototypes = new Dictionary<string, TreePrototype>(StringComparer.OrdinalIgnoreCase);
        private List<TreeInstance> _treeInstances = new List<TreeInstance>();
        private BSA _bsaArchive = null;
        private Record[] _allRecords = null; // Store all records for VHGT lookup

        // Distance-based LOD configuration (based on MWGE distant lands algorithm)
        // These values determine which quadtree/distance category objects are placed in
        public float FarStaticMinSize { get; set; } = 50f;      // Objects with radius <= this go to Near
        public float VeryFarStaticMinSize { get; set; } = 200f; // Objects with radius <= this go to Far, else VeryFar
        public float BuildingRadiusMultiplier { get; set; } = 2.0f; // Buildings get 2x radius multiplier

        public PlaceStatics(string esm = "Morrowind", string bsa = "Morrowind.bsa")
        {
            _esm = Path.GetFileNameWithoutExtension(esm);
            _bsa = bsa;
            _nifLoader = new NIFLoader(_esm, _bsa);
        }

        /// <summary>
        /// Static object distance categories (based on MWGE distant lands)
        /// </summary>
        public enum StaticDistanceCategory
        {
            Near,       // Close objects, always rendered
            Far,        // Medium distance objects
            VeryFar     // Distant objects, lowest detail
        }

        /// <summary>
        /// Places large static structures (buildings, ruins, etc.) from CELL records
        /// Enhanced with distance-based categorization similar to MWGE distant lands
        /// </summary>
        public void PlaceLargeStructures(Record[] records, CellManager cellManager = null, Transform parent = null)
        {
            // Track statistics
            int nearCount = 0, farCount = 0, veryFarCount = 0;
            int buildingCount = 0;

            // First pass: categorize objects by distance
            Dictionary<StaticDistanceCategory, List<(SubRecordCellREFP refp, float scale, SubRecordCellObjectID objectId, string modelFilename, int cellGridX, int cellGridY)>> categorizedObjects = 
                new Dictionary<StaticDistanceCategory, List<(SubRecordCellREFP, float, SubRecordCellObjectID, string, int, int)>>();
            categorizedObjects[StaticDistanceCategory.Near] = new List<(SubRecordCellREFP, float, SubRecordCellObjectID, string, int, int)>();
            categorizedObjects[StaticDistanceCategory.Far] = new List<(SubRecordCellREFP, float, SubRecordCellObjectID, string, int, int)>();
            categorizedObjects[StaticDistanceCategory.VeryFar] = new List<(SubRecordCellREFP, float, SubRecordCellObjectID, string, int, int)>();

            // Iterate through cells and categorize objects
            foreach (Record rec in records)
            {
                RecordCell cell = rec as RecordCell;
                if (cell == null || cell.subRecords == null)
                    continue;

                // Skip interior cells
                bool isInterior = false;
                int cellGridX = 0, cellGridY = 0;
                string cellName = "";

                foreach (SubRecords subrec in cell.subRecords)
                {
                    if (subrec is SubRecordCellDATA data)
                    {
                        cellGridX = data.gridX;
                        cellGridY = data.gridY;
                        // Check if interior (flag 0x01)
                        isInterior = (data.flags & 0x01) != 0;
                    }
                    else if (subrec is SubRecordCellNAME name && string.IsNullOrEmpty(cellName))
                    {
                        cellName = name.name;
                        if (cellName.ToLower().Contains("interior"))
                            isInterior = true;
                    }
                }

                if (isInterior)
                    continue;

                // Process references in this cell
                float currentScale = 1.0f;
                SubRecordCellObjectID currentObjectId = null;

                foreach (SubRecords subrec in cell.subRecords)
                {
                    if (subrec is SubRecordCellXSCL xscal)
                    {
                        currentScale = xscal.scale;
                    }
                    else if (subrec is SubRecordCellObjectID objectId)
                    {
                        currentObjectId = objectId;
                    }
                    else if (subrec is SubRecordCellREFP refp && currentObjectId != null)
                    {
                        // Find the model filename
                        string modelFilename = FindModelFilename(currentObjectId.objectId, records);
                        if (string.IsNullOrEmpty(modelFilename))
                            continue;

                        // Check if it's a large object
                        string baseFilename = Path.GetFileNameWithoutExtension(modelFilename).ToLower();
                        if (IsTreeModel(baseFilename) || IsGrassModel(baseFilename))
                            continue;

                        // Calculate bounding sphere radius
                        float radius = CalculateBoundingSphereRadius(modelFilename, currentScale);
                        
                        // Determine if it's a building
                        bool isBuilding = IsBuilding(baseFilename);
                        if (isBuilding)
                        {
                            radius *= BuildingRadiusMultiplier;
                            buildingCount++;
                        }

                        // Categorize by distance (based on MWGE algorithm)
                        StaticDistanceCategory category;
                        if (radius <= FarStaticMinSize)
                        {
                            category = StaticDistanceCategory.Near;
                            nearCount++;
                        }
                        else if (radius <= VeryFarStaticMinSize)
                        {
                            category = StaticDistanceCategory.Far;
                            farCount++;
                        }
                        else
                        {
                            category = StaticDistanceCategory.VeryFar;
                            veryFarCount++;
                        }

                        categorizedObjects[category].Add((refp, currentScale, currentObjectId, modelFilename, cellGridX, cellGridY));
                    }
                }
            }

            // Log statistics
            UnityEngine.Debug.Log($"PlaceLargeStructures: Categorized {nearCount} Near, {farCount} Far, {veryFarCount} VeryFar objects ({buildingCount} buildings)");

            // Second pass: place objects by category
            // For now, place all objects (we can add distance-based culling later)
            PlaceObjects(records, cellManager, parent, ObjectType.LargeStructures);
        }

        /// <summary>
        /// Places trees from CELL records as Unity terrain trees
        /// </summary>
        public void PlaceTrees(Record[] records, CellManager cellManager = null, Terrain terrain = null)
        {
            // Place trees as static meshes (not terrain tree instances)
            // Trees will be placed using the same logic as statics, but with SpeedTree shader
            PlaceObjects(records, cellManager, null, ObjectType.Trees, terrain);
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
                        return vhgt.offset * TESGlobals.HEIGHT_MAP_SCALE_FACTOR * TESGlobals.MORROWIND_TO_TERRAIN_SCALE;
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
                //UnityEngine.Debug.LogWarning("TESCell: Cannot generate statics - cell record is null or has no subrecords");
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
                //UnityEngine.Debug.Log($"TESCell: Skipping interior cell at ({cellGridX}, {cellGridY})");
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
            int placedGrassCount = 0;
            int failedGrassCount = 0;
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
                        
                        // Place grass
                        PlaceReference(currentObjectID, currentREFP, currentScale, statRecordsByName, cellParent, cellGridX, cellGridY, ObjectType.Grass, terrain, parent, ref placedGrassCount, ref failedGrassCount, allRecords);
                        
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
                    // Debug: Log negative scales to see which objects need mirroring
                    if (currentScale < 0)
                    {
                        UnityEngine.Debug.LogWarning($"XSCL: Negative scale {currentScale} in cell ({cellGridX}, {cellGridY})");
                    }
                }
            }

            if (currentObjectID != null && currentREFP != null)
            {
                // Place trees
                PlaceReference(currentObjectID, currentREFP, currentScale, statRecordsByName, cellParent, cellGridX, cellGridY, ObjectType.Trees, terrain, parent, ref placedTreesCount, ref failedTreesCount, allRecords);
                
                // Place grass
                PlaceReference(currentObjectID, currentREFP, currentScale, statRecordsByName, cellParent, cellGridX, cellGridY, ObjectType.Grass, terrain, parent, ref placedGrassCount, ref failedGrassCount, allRecords);
                
                // Place large structures
                PlaceReference(currentObjectID, currentREFP, currentScale, statRecordsByName, cellParent, cellGridX, cellGridY, ObjectType.LargeStructures, terrain, parent, ref placedStructuresCount, ref failedStructuresCount, allRecords);
            }

            //UnityEngine.Debug.Log($"TESCell ({cellGridX}, {cellGridY}): Placed {placedTreesCount} trees ({failedTreesCount} failed), {placedGrassCount} grass ({failedGrassCount} failed), {placedStructuresCount} structures ({failedStructuresCount} failed)");
            
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
                //UnityEngine.Debug.Log($"TESCell: Added {treeCount} tree instances to terrain (total now: {allTrees.Length}, verified: {verifyTrees.Length}). " +
                //    $"Terrain settings: treeDistance={terrain.treeDistance}, drawTreesAndFoliage={terrain.drawTreesAndFoliage}, " +
                //    $"treePrototypes={terrainData.treePrototypes.Length}");
                
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
                        
                        //UnityEngine.Debug.Log($"  Tree {i} (from terrain): prototypeIndex={ti.prototypeIndex}, " +
                        //    $"normalizedPos=({ti.position.x:F4},{ti.position.y:F4},{ti.position.z:F4}), " +
                        //    $"worldPos=({worldPos.x:F2},{worldPos.y:F2},{worldPos.z:F2}), " +
                        //    $"scale=({ti.widthScale:F4},{ti.heightScale:F4}), rotation={ti.rotation:F4}");
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

            //UnityEngine.Debug.Log($"Found {statRecordsByName.Count} STAT records by name");

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
                    //UnityEngine.Debug.LogWarning("Cell has no subrecords, skipping");
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
            //UnityEngine.Debug.Log($"Placed {placedCount} {typeName} from {cellCount} cells ({failedCount} failed)");
            
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
        private bool PlaceTree(string modelFilename, SubRecordCellREFP refp, float scale, SubRecordCellObjectID objectId, Terrain terrain, int cellGridX, int cellGridY, Transform parent = null)
        {
            try
            {
                // Use global constants from TESGlobals
                
                // Strip any subdirectory paths from filename (use just the base filename)
                string baseFilename = Path.GetFileName(modelFilename);
                string baseFilenameNoExt = Path.GetFileNameWithoutExtension(baseFilename);
                
                // Check if we've already loaded this model (mesh instancing) - use global library
                TESNifLibrary.NifEntry cachedEntry = TESNifLibrary.GetModelByBaseFilename(baseFilenameNoExt);
                if (cachedEntry != null)
                {
                    // Instantiate the existing model instead of loading again
                    GameObject instanceObj = GameObject.Instantiate(cachedEntry.Model);
                    instanceObj.name = cachedEntry.Model.name; // Unity adds "(Clone)" automatically
                    
                    // Use the same coordinate conversion as PlaceStaticObject
                    // Use 64/8192 for scaling so statics snap together correctly, then add offset to align with terrain
                    
                    // REFP coordinates in Morrowind are stored as (X, Z, Y) not (X, Y, Z)
                    float instanceScaledX = refp.x * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                    float instanceScaledZ = refp.y * TESGlobals.MORROWIND_TO_STATIC_SCALE; // Use refp.y for Z (North coordinate)
                    float instanceScaledY = refp.z * TESGlobals.MORROWIND_TO_STATIC_SCALE; // Use refp.z for Y (Height coordinate)
                    
                    Vector3 instancePosition = new Vector3(
                        instanceScaledX,      // X position in world space
                        instanceScaledY,      // Y position (height) from REFP coordinates
                        instanceScaledZ       // Z position in world space
                    );
                    
                    // Convert Morrowind rotation to Unity rotation (same as statics)
                    Quaternion instanceRotation = Quaternion.Euler(
                        -refp.yaw * Mathf.Rad2Deg,    // Yaw -> X
                        -refp.pitch * Mathf.Rad2Deg,  // Pitch -> Y
                        -refp.roll * Mathf.Rad2Deg    // Roll -> Z
                    );
                    
                    // Set position, rotation, scale BEFORE parenting
                    instanceObj.transform.position = instancePosition;
                    instanceObj.transform.rotation = instanceRotation;
                    
                    // XSCL scale multiplies the base GameObject scale (same as statics)
                    Vector3 instanceBaseScale = new Vector3(TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE);
                    // Apply XSCL scale (handle negative scales like statics)
                    if (scale < 0)
                    {
                        // Negative scale = mirror object (use absolute value)
                        instanceObj.transform.localScale = instanceBaseScale * Mathf.Abs(scale);
                    }
                    else
                    {
                        // Positive scale = normal scaling
                        instanceObj.transform.localScale = instanceBaseScale * scale;
                    }
                    
                    // Set name
                    if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                    {
                        instanceObj.name = objectId.objectId.TrimEnd('\0');
                    }
                    
                    // Apply reflection fix: negate Z scale and negate Yaw (same as statics)
                    Vector3 instanceCurrentScale = instanceObj.transform.localScale;
                    instanceObj.transform.localScale = new Vector3(instanceCurrentScale.x, instanceCurrentScale.y, -instanceCurrentScale.z);
                    
                    // Negate Yaw (Y rotation)
                    Vector3 instanceEuler = instanceObj.transform.rotation.eulerAngles;
                    float instanceYaw = instanceEuler.y;
                    if (instanceYaw > 180f) instanceYaw -= 360f;
                    instanceObj.transform.rotation = Quaternion.Euler(instanceEuler.x, -instanceYaw, instanceEuler.z);
                    
                    // Add LOD component
                    AddLODToObject(instanceObj);
                    
                    // Parent to cell or specified parent (same as statics)
                    if (parent != null)
                    {
                        instanceObj.transform.SetParent(parent, worldPositionStays: true);
                    }
                    
                    return true;
                }
                
                // Load the NIF model with meshes combined (for trees)
                GameObject treeModel = _nifLoader.LoadNIFFromCache(baseFilename, combineMeshes: true);
                if (treeModel == null)
                {
                    //UnityEngine.Debug.LogWarning($"Failed to load tree model: {baseFilename}");
                    return false;
                }
                
                // Store the loaded model in global library for future instancing
                string staticId = objectId?.objectId?.TrimEnd('\0');
                string staticName = staticId; // Use ID as name if no separate name available
                TESNifLibrary.AddModel(staticId, staticName, baseFilename, baseFilenameNoExt, treeModel, combineMeshes: true);
                
                // Use the same coordinate conversion as PlaceStaticObject
                
                // REFP coordinates in Morrowind are stored as (X, Z, Y) not (X, Y, Z)
                float scaledX = refp.x * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                float scaledZ = refp.y * TESGlobals.MORROWIND_TO_STATIC_SCALE; // Use refp.y for Z (North coordinate)
                float scaledY = refp.z * TESGlobals.MORROWIND_TO_STATIC_SCALE; // Use refp.z for Y (Height coordinate)
                
                Vector3 unityPosition = new Vector3(
                    scaledX,      // X position in world space
                    scaledY,      // Y position (height) from REFP coordinates
                    scaledZ       // Z position in world space
                );
                
                // Convert Morrowind rotation to Unity rotation (same as statics)
                Quaternion unityRotation = Quaternion.Euler(
                    -refp.yaw * Mathf.Rad2Deg,    // Yaw -> X
                    -refp.pitch * Mathf.Rad2Deg,  // Pitch -> Y
                    -refp.roll * Mathf.Rad2Deg    // Roll -> Z
                );
                
                // Set position and rotation BEFORE parenting
                treeModel.transform.position = unityPosition;
                treeModel.transform.rotation = unityRotation;
                
                // XSCL scale multiplies the base GameObject scale (same as statics)
                Vector3 modelBaseScale = treeModel.transform.localScale;
                // Apply XSCL scale (handle negative scales like statics)
                if (scale < 0)
                {
                    // Negative scale = mirror object (use absolute value)
                    treeModel.transform.localScale = modelBaseScale * Mathf.Abs(scale);
                }
                else
                {
                    // Positive scale = normal scaling
                    treeModel.transform.localScale = modelBaseScale * scale;
                }
                
                // Set name
                if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                {
                    treeModel.name = objectId.objectId.TrimEnd('\0');
                }
                
                // Apply reflection fix: negate Z scale and negate Yaw (same as statics)
                Vector3 currentScale = treeModel.transform.localScale;
                treeModel.transform.localScale = new Vector3(currentScale.x, currentScale.y, -currentScale.z);
                
                // Negate Yaw (Y rotation)
                Vector3 euler = treeModel.transform.rotation.eulerAngles;
                float yaw = euler.y;
                if (yaw > 180f) yaw -= 360f;
                treeModel.transform.rotation = Quaternion.Euler(euler.x, -yaw, euler.z);
                
                // Add LOD component
                AddLODToObject(treeModel);
                
                // Parent to cell or specified parent (same as statics)
                if (parent != null)
                {
                    treeModel.transform.SetParent(parent, worldPositionStays: true);
                }
                
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
                    //UnityEngine.Debug.LogWarning($"Tree model {modelName} has mesh on child object. Moving to root for Unity terrain compatibility.");
                    
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
            
            // Keep the prototype GameObject active (we're now placing trees as static meshes, not terrain trees)
            // The model will be used for instancing
            
            //UnityEngine.Debug.Log($"Created tree prototype for {modelName}: {meshFilter.sharedMesh.vertexCount} vertices, {meshFilter.sharedMesh.triangles.Length / 3} triangles");
            
            return prototype;
        }

        /// <summary>
        /// Places grass as a Unity terrain detail
        /// </summary>
        private bool PlaceGrassDetail(string modelFilename, SubRecordCellREFP refp, float scale, SubRecordCellObjectID objectId, Terrain terrain, int cellGridX, int cellGridY, Transform parent = null)
        {
            try
            {
                // Use global constants from TESGlobals
                
                // Strip any subdirectory paths from filename (use just the base filename)
                string baseFilename = Path.GetFileName(modelFilename);
                string baseFilenameNoExt = Path.GetFileNameWithoutExtension(baseFilename);
                
                // Check if we've already loaded this model (mesh instancing) - use global library
                TESNifLibrary.NifEntry cachedEntry = TESNifLibrary.GetModelByBaseFilename(baseFilenameNoExt);
                if (cachedEntry != null)
                {
                    // Instantiate the existing model instead of loading again
                    GameObject instanceObj = GameObject.Instantiate(cachedEntry.Model);
                    instanceObj.name = cachedEntry.Model.name; // Unity adds "(Clone)" automatically
                    
                    // Use the same coordinate conversion as PlaceStaticObject and PlaceTree
                    // Use 64/8192 for scaling so statics snap together correctly, then add offset to align with terrain
                    
                    // REFP coordinates in Morrowind are stored as (X, Z, Y) not (X, Y, Z)
                    float instanceScaledX = refp.x * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                    float instanceScaledZ = refp.y * TESGlobals.MORROWIND_TO_STATIC_SCALE; // Use refp.y for Z (North coordinate)
                    float instanceScaledY = refp.z * TESGlobals.MORROWIND_TO_STATIC_SCALE; // Use refp.z for Y (Height coordinate)
                    
                    Vector3 instancePosition = new Vector3(
                        instanceScaledX,      // X position in world space
                        instanceScaledY,      // Y position (height) from REFP coordinates
                        instanceScaledZ       // Z position in world space
                    );
                    
                    // Convert Morrowind rotation to Unity rotation (same as statics)
                    Quaternion instanceRotation = Quaternion.Euler(
                        -refp.yaw * Mathf.Rad2Deg,    // Yaw -> X
                        -refp.pitch * Mathf.Rad2Deg,  // Pitch -> Y
                        -refp.roll * Mathf.Rad2Deg    // Roll -> Z
                    );
                    
                    // Set position, rotation, scale BEFORE parenting
                    instanceObj.transform.position = instancePosition;
                    instanceObj.transform.rotation = instanceRotation;
                    
                    // XSCL scale multiplies the base GameObject scale (same as statics)
                    Vector3 instanceBaseScale = new Vector3(TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE);
                    // Apply XSCL scale (handle negative scales like statics)
                    if (scale < 0)
                    {
                        // Negative scale = mirror object (use absolute value)
                        instanceObj.transform.localScale = instanceBaseScale * Mathf.Abs(scale);
                    }
                    else
                    {
                        // Positive scale = normal scaling
                        instanceObj.transform.localScale = instanceBaseScale * scale;
                    }
                    
                    // Set name
                    if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                    {
                        instanceObj.name = objectId.objectId.TrimEnd('\0');
                    }
                    
                    // Apply reflection fix: negate Z scale and negate Yaw (same as statics)
                    Vector3 instanceCurrentScale = instanceObj.transform.localScale;
                    instanceObj.transform.localScale = new Vector3(instanceCurrentScale.x, instanceCurrentScale.y, -instanceCurrentScale.z);
                    
                    // Negate Yaw (Y rotation)
                    Vector3 instanceEuler = instanceObj.transform.rotation.eulerAngles;
                    float instanceYaw = instanceEuler.y;
                    if (instanceYaw > 180f) instanceYaw -= 360f;
                    instanceObj.transform.rotation = Quaternion.Euler(instanceEuler.x, -instanceYaw, instanceEuler.z);
                    
                    // Ensure MeshCollider is a trigger (for interaction, doesn't block movement)
                    MeshCollider instanceMeshCollider = instanceObj.GetComponent<MeshCollider>();
                    if (instanceMeshCollider != null)
                    {
                        instanceMeshCollider.isTrigger = true;
                    }
                    else
                    {
                        // If no collider exists, add one as trigger
                        MeshFilter meshFilter = instanceObj.GetComponent<MeshFilter>();
                        if (meshFilter != null && meshFilter.sharedMesh != null)
                        {
                            instanceMeshCollider = instanceObj.AddComponent<MeshCollider>();
                            instanceMeshCollider.sharedMesh = meshFilter.sharedMesh;
                            instanceMeshCollider.convex = false;
                            instanceMeshCollider.isTrigger = true;
                        }
                    }
                    
                    // Add LOD component
                    AddLODToObject(instanceObj);
                    
                    // Parent to cell or specified parent (same as statics)
                    if (parent != null)
                    {
                        instanceObj.transform.SetParent(parent, worldPositionStays: true);
                    }
                    
                    return true;
                }
                
                // Load the NIF model with meshes combined (for grass, same as trees)
                GameObject grassModel = _nifLoader.LoadNIFFromCache(baseFilename, combineMeshes: true);
                if (grassModel == null)
                {
                    //UnityEngine.Debug.LogWarning($"Failed to load grass model: {baseFilename}");
                    return false;
                }
                
                // Store the loaded model in global library for future instancing
                string staticId = objectId?.objectId?.TrimEnd('\0');
                string staticName = staticId; // Use ID as name if no separate name available
                TESNifLibrary.AddModel(staticId, staticName, baseFilename, baseFilenameNoExt, grassModel, combineMeshes: true);
                
                // Use the same coordinate conversion as PlaceStaticObject and PlaceTree
                
                // REFP coordinates in Morrowind are stored as (X, Z, Y) not (X, Y, Z)
                float scaledX = refp.x * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                float scaledZ = refp.y * TESGlobals.MORROWIND_TO_STATIC_SCALE; // Use refp.y for Z (North coordinate)
                float scaledY = refp.z * TESGlobals.MORROWIND_TO_STATIC_SCALE; // Use refp.z for Y (Height coordinate)
                
                Vector3 unityPosition = new Vector3(
                    scaledX,      // X position in world space
                    scaledY,      // Y position (height) from REFP coordinates
                    scaledZ       // Z position in world space
                );
                
                // Convert Morrowind rotation to Unity rotation (same as statics)
                Quaternion unityRotation = Quaternion.Euler(
                    -refp.yaw * Mathf.Rad2Deg,    // Yaw -> X
                    -refp.pitch * Mathf.Rad2Deg,  // Pitch -> Y
                    -refp.roll * Mathf.Rad2Deg    // Roll -> Z
                );
                
                // Set position and rotation BEFORE parenting
                grassModel.transform.position = unityPosition;
                grassModel.transform.rotation = unityRotation;
                
                // XSCL scale multiplies the base GameObject scale (same as statics)
                Vector3 modelBaseScale = grassModel.transform.localScale;
                // Apply XSCL scale (handle negative scales like statics)
                if (scale < 0)
                {
                    // Negative scale = mirror object (use absolute value)
                    grassModel.transform.localScale = modelBaseScale * Mathf.Abs(scale);
                }
                else
                {
                    // Positive scale = normal scaling
                    grassModel.transform.localScale = modelBaseScale * scale;
                }
                
                // Set name
                if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                {
                    grassModel.name = objectId.objectId.TrimEnd('\0');
                }
                
                // Apply reflection fix: negate Z scale and negate Yaw (same as statics)
                Vector3 currentScale = grassModel.transform.localScale;
                grassModel.transform.localScale = new Vector3(currentScale.x, currentScale.y, -currentScale.z);
                
                // Negate Yaw (Y rotation)
                Vector3 euler = grassModel.transform.rotation.eulerAngles;
                float yaw = euler.y;
                if (yaw > 180f) yaw -= 360f;
                grassModel.transform.rotation = Quaternion.Euler(euler.x, -yaw, euler.z);
                
                // Ensure MeshCollider is a trigger (for interaction, doesn't block movement)
                MeshCollider meshCollider = grassModel.GetComponent<MeshCollider>();
                if (meshCollider != null)
                {
                    meshCollider.isTrigger = true;
                }
                else
                {
                    // If no collider exists, add one as trigger
                    MeshFilter meshFilter = grassModel.GetComponent<MeshFilter>();
                    if (meshFilter != null && meshFilter.sharedMesh != null)
                    {
                        meshCollider = grassModel.AddComponent<MeshCollider>();
                        meshCollider.sharedMesh = meshFilter.sharedMesh;
                        meshCollider.convex = false;
                        meshCollider.isTrigger = true;
                    }
                }
                
                // Add LOD component
                AddLODToObject(grassModel);
                
                // Parent to cell or specified parent (same as statics)
                if (parent != null)
                {
                    grassModel.transform.SetParent(parent, worldPositionStays: true);
                }
                
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error placing grass {modelFilename}: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
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
            if (objectType == ObjectType.Trees)
            {
                // Place trees as static meshes (same as statics, but with SpeedTree shader)
                Transform refParent = cellParent != null ? cellParent.transform : parent;
                success = PlaceTree(modelFilename, refp, scale, objectId, terrain, cellGridX, cellGridY, refParent);
            }
            else if (objectType == ObjectType.Grass)
            {
                // Place grass as static meshes (same as trees, but with trigger colliders for interaction)
                Transform refParent = cellParent != null ? cellParent.transform : parent;
                success = PlaceGrassDetail(modelFilename, refp, scale, objectId, terrain, cellGridX, cellGridY, refParent);
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
            // Includes "parasol" for giant mushrooms (e.g., flora_emp_parasol)
            return baseFilename.Contains("tree") || 
                   baseFilename.Contains("Tree") ||
                   baseFilename.Contains("TREE") ||
                   baseFilename.Contains("parasol") ||
                   baseFilename.Contains("Parasol") ||
                   baseFilename.Contains("PARASOL") ||
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
        /// Calculates the bounding sphere radius for a model (post-transform, including scale)
        /// Based on MWGE's distant lands algorithm
        /// </summary>
        private float CalculateBoundingSphereRadius(string modelFilename, float scale)
        {
            try
            {
                // Try to get the mesh bounds from a loaded model
                string baseFilename = Path.GetFileName(modelFilename);
                GameObject model = null;

                // Check if already loaded in global library
                string baseFilenameNoExt = Path.GetFileNameWithoutExtension(baseFilename);
                TESNifLibrary.NifEntry cachedEntry = TESNifLibrary.GetModelByBaseFilename(baseFilenameNoExt);
                if (cachedEntry != null)
                {
                    return GetBoundingSphereRadiusFromGameObject(cachedEntry.Model, scale);
                }

                // Try to load the model temporarily
                GameObject tempModel = _nifLoader.LoadNIFFromCache(baseFilename);
                if (tempModel != null)
                {
                    float radius = GetBoundingSphereRadiusFromGameObject(tempModel, scale);
                    // Don't store it if it wasn't already stored (to avoid polluting cache)
                    if (!TESNifLibrary.HasModel(baseFilenameNoExt))
                    {
                        UnityEngine.Object.DestroyImmediate(tempModel);
                    }
                    return radius;
                }

                // Fallback: estimate based on filename or return default
                // Large objects (buildings) are typically larger
                string baseName = Path.GetFileNameWithoutExtension(modelFilename).ToLower();
                if (IsBuilding(baseName))
                {
                    return 100f * scale; // Default building size
                }
                else if (baseName.StartsWith("ex_"))
                {
                    return 50f * scale; // Default exterior object size
                }

                return 25f * scale; // Default small object size
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"Error calculating bounding sphere radius for {modelFilename}: {ex.Message}");
                return 25f * scale; // Default fallback
            }
        }

        /// <summary>
        /// Gets bounding sphere radius from a GameObject's mesh bounds
        /// </summary>
        private float GetBoundingSphereRadiusFromGameObject(GameObject obj, float scale)
        {
            if (obj == null)
                return 0f;

            Bounds combinedBounds = new Bounds();
            bool boundsInitialized = false;

            // Get bounds from all MeshRenderers
            MeshRenderer[] renderers = obj.GetComponentsInChildren<MeshRenderer>();
            foreach (MeshRenderer renderer in renderers)
            {
                if (renderer.bounds.size.magnitude > 0)
                {
                    if (!boundsInitialized)
                    {
                        combinedBounds = renderer.bounds;
                        boundsInitialized = true;
                    }
                    else
                    {
                        combinedBounds.Encapsulate(renderer.bounds);
                    }
                }
            }

            // If no renderers, try MeshFilters
            if (!boundsInitialized)
            {
                MeshFilter[] filters = obj.GetComponentsInChildren<MeshFilter>();
                foreach (MeshFilter filter in filters)
                {
                    if (filter.sharedMesh != null)
                    {
                        Bounds meshBounds = filter.sharedMesh.bounds;
                        // Transform bounds to world space
                        Bounds worldBounds = new Bounds(
                            obj.transform.TransformPoint(meshBounds.center),
                            Vector3.Scale(meshBounds.size, obj.transform.lossyScale)
                        );

                        if (!boundsInitialized)
                        {
                            combinedBounds = worldBounds;
                            boundsInitialized = true;
                        }
                        else
                        {
                            combinedBounds.Encapsulate(worldBounds);
                        }
                    }
                }
            }

            if (!boundsInitialized)
                return 25f * scale; // Default fallback

            // Calculate radius from bounds (half the diagonal)
            float radius = combinedBounds.size.magnitude * 0.5f;
            
            // Apply scale
            radius *= scale;

            return radius;
        }

        /// <summary>
        /// Determines if an object is a building (for radius multiplier)
        /// </summary>
        private bool IsBuilding(string baseFilename)
        {
            string lower = baseFilename.ToLower();
            
            // Buildings typically have these keywords
            string[] buildingKeywords = new string[]
            {
                "building", "house", "mansion", "ruin", "ruins",
                "lighthouse", "stronghold", "fort", "fortress",
                "castle", "keep", "tower", "temple", "shrine"
            };

            foreach (string keyword in buildingKeywords)
            {
                if (lower.Contains(keyword))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Finds the model filename for a given object ID
        /// </summary>
        private string FindModelFilename(string objectId, Record[] allRecords)
        {
            if (string.IsNullOrEmpty(objectId) || allRecords == null)
                return null;

            // Search STAT records
            foreach (Record rec in allRecords)
            {
                RecordStat stat = rec as RecordStat;
                if (stat != null && stat.subRecords != null)
                {
                    string statName = null;
                    string statModl = null;

                    foreach (SubRecords subrec in stat.subRecords)
                    {
                        if (subrec is SubRecordStatNAME name)
                        {
                            statName = name.name?.TrimEnd('\0');
                        }
                        else if (subrec is SubRecordStatMODL modl)
                        {
                            statModl = modl.model?.TrimEnd('\0');
                        }
                    }

                    if (statName != null && statName.Equals(objectId.TrimEnd('\0'), StringComparison.OrdinalIgnoreCase))
                    {
                        return statModl;
                    }
                }
            }

            return null;
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
                
                // Debug: Log scale value for troubleshooting
                if (Mathf.Abs(scale - 1.0f) > 0.001f)
                {
                    UnityEngine.Debug.LogWarning($"XSCL: Scale={scale} for {baseFilename} in cell ({cellGridX}, {cellGridY})");
                }
                
                // Convert Morrowind world coordinates to Unity world coordinates
                // Morrowind uses 8192 units per cell for static placement (64 effective units)
                // Unity terrain uses 65 units per cell (for RAW +1 requirement)
                // Use 64/8192 for scaling so statics snap together correctly
                // Use global constant from TESGlobals
                
                // REFP coordinates in Morrowind are stored as (X, Z, Y) not (X, Y, Z)
                // refp.x = Morrowind X (East) - world coordinate
                // refp.y = Morrowind Z (North) - world coordinate
                // refp.z = Morrowind Y (Up/Height) - world coordinate
                //
                // Important: Static objects in Morrowind use absolute coordinates relative to the cell's origin,
                // not dynamically calculated from terrain height. The Z coordinate (Y in Unity) is an absolute
                // value that determines the object's height relative to the cell's reference point.
                // VHGT is used for terrain generation, but static objects are placed independently at fixed coordinates.
                float scaledX = refp.x * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                float scaledZ = refp.y * TESGlobals.MORROWIND_TO_STATIC_SCALE; // Use refp.y for Z (North coordinate)
                float scaledY = refp.z * TESGlobals.MORROWIND_TO_STATIC_SCALE; // Use refp.z for Y (Height coordinate) - absolute, not terrain-relative
                
                // REFP coordinates are already in world space, so we use the scaled values directly
                Vector3 unityPosition = new Vector3(
                    scaledX,      // X position in world space
                    scaledY,      // Y position (height) from REFP coordinates - absolute position relative to cell origin
                    scaledZ       // Z position in world space
                );
                
                // Convert Morrowind rotation to Unity rotation
                // OpenMW's makeOsgQuat uses: Quat(rot[2], (0,0,-1)) * Quat(rot[1], (0,-1,0)) * Quat(rot[0], (-1,0,0))
                // Where rot[0]=pitch, rot[1]=yaw, rot[2]=roll (in OpenMW's Position struct)
                // Our REFP stores: roll, yaw, pitch (in that order when reading from file)
                // So: refp.roll = rot[2], refp.yaw = rot[1], refp.pitch = rot[0]
                // OpenMW applies: roll around -Z, then yaw around -Y, then pitch around -X
                // Unity Euler applies rotations in Z, X, Y order (intrinsic rotations)
                // User reports things rotated on X when they should be on Y - this suggests pitch/yaw might be swapped
                // Or the axis mapping needs adjustment. Let's try swapping pitch and yaw:
                Quaternion unityRotation = Quaternion.Euler(
                    -refp.yaw * Mathf.Rad2Deg,    // Yaw -> X (swapped with pitch, negated to match OpenMW's -Y axis)
                    -refp.pitch * Mathf.Rad2Deg,  // Pitch -> Y (swapped with yaw, negated to match OpenMW's -X axis)
                    -refp.roll * Mathf.Rad2Deg    // Roll -> Z (negated to match OpenMW's -Z axis)
                );
                
                // Check if we've already loaded this model (mesh instancing) - use global library
                string baseFilenameNoExt = Path.GetFileNameWithoutExtension(baseFilename);
                TESNifLibrary.NifEntry cachedEntry = TESNifLibrary.GetModelByBaseFilename(baseFilenameNoExt);
                if (cachedEntry != null)
                {
                    // Instantiate the existing model instead of loading again
                    GameObject instanceObj = GameObject.Instantiate(cachedEntry.Model);
                    instanceObj.name = cachedEntry.Model.name; // Unity adds "(Clone)" automatically
                    
                    // Set position, rotation, scale BEFORE parenting
                    instanceObj.transform.position = unityPosition;
                    instanceObj.transform.rotation = unityRotation;
                    
                    // XSCL scale multiplies the base GameObject scale (MORROWIND_TO_STATIC_SCALE = 0.0078125), not replaces it
                    // Base scale is already applied in NIFLoader, so we multiply by XSCL here
                    // IMPORTANT: The template may have the reflection fix already applied (Z scale negated)
                    // We need to use the ORIGINAL base scale, not read it from the instance
                    // Use the existing MORROWIND_TO_STATIC_SCALE constant from the enclosing scope
                    Vector3 instanceBaseScale = new Vector3(TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE);
                    
                    // Apply XSCL scale
                    if (scale < 0)
                    {
                        // Negative scale = mirror object (use absolute value)
                        instanceObj.transform.localScale = instanceBaseScale * Mathf.Abs(scale);
                    }
                    else
                    {
                        // Positive scale = normal scaling
                        instanceObj.transform.localScale = instanceBaseScale * scale;
                    }
                    
                    // Set name
                    if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                    {
                        instanceObj.name = objectId.objectId.TrimEnd('\0');
                    }

                    // Add LOD component
                    AddLODToObject(instanceObj);
                    
                    // Parent to terrain or specified parent
                    // Use worldPositionStays: true to preserve world position when parenting
                    // This prevents Unity from converting world position to local coordinates
                    if (parent != null)
                    {
                        instanceObj.transform.SetParent(parent, worldPositionStays: true);
                    }
                    
                    // Fix reflection issues: negate Z scale and negate Yaw
                    // This must be done AFTER parenting to ensure it's applied to all objects
                    Vector3 currentScale = instanceObj.transform.localScale;
                    instanceObj.transform.localScale = new Vector3(currentScale.x, currentScale.y, -currentScale.z);
                    
                    // Negate Yaw (Y rotation): extract Euler angles, negate Y, rebuild quaternion
                    Vector3 euler = instanceObj.transform.rotation.eulerAngles;
                    // Convert to -180 to 180 range for proper negation
                    float yaw = euler.y;
                    if (yaw > 180f) yaw -= 360f;
                    instanceObj.transform.rotation = Quaternion.Euler(euler.x, -yaw, euler.z);

                    return true;
                }

                // Load the NIF model for the first time
                GameObject modelObj = _nifLoader.LoadNIFFromCache(baseFilename);
                if (modelObj == null)
                {
                    UnityEngine.Debug.LogWarning($"Failed to load NIF model: {baseFilename}");
                    return false;
                }

                // Store the loaded model in global library for future instancing
                // IMPORTANT: Store the model BEFORE applying any instance-specific transforms
                // The template should remain in its base state (no position, rotation, scale, or reflection fix)
                // Each instance (including this first one) will get its own transforms applied
                string staticId = objectId?.objectId?.TrimEnd('\0');
                string staticName = staticId; // Use ID as name if no separate name available
                TESNifLibrary.AddModel(staticId, staticName, modelFilename, baseFilenameNoExt, modelObj, combineMeshes: false);

                // Set position and rotation BEFORE parenting
                modelObj.transform.position = unityPosition;
                modelObj.transform.rotation = unityRotation;
                
                // XSCL scale multiplies the base GameObject scale (MORROWIND_TO_TERRAIN_SCALE = 0.0078125), not replaces it
                // Base scale is already applied in NIFLoader, so we multiply by XSCL here
                Vector3 modelBaseScale = modelObj.transform.localScale;
                
                // Apply XSCL scale
                if (scale < 0)
                {
                    // Negative scale = mirror object (use absolute value)
                    modelObj.transform.localScale = modelBaseScale * Mathf.Abs(scale);
                }
                else
                {
                    // Positive scale = normal scaling
                    modelObj.transform.localScale = modelBaseScale * scale;
                }
                
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
                
                // Parent to terrain or specified parent
                // Use worldPositionStays: true to preserve world position when parenting
                // This prevents Unity from converting world position to local coordinates
                if (parent != null)
                {
                    modelObj.transform.SetParent(parent, worldPositionStays: true);
                }
                
                // Fix reflection issues: negate Z scale and negate Yaw
                // This must be done AFTER parenting to ensure it's applied to all objects
                // Apply to this instance (the first one, which is also stored as template)
                Vector3 modelCurrentScale = modelObj.transform.localScale;
                modelObj.transform.localScale = new Vector3(modelCurrentScale.x, modelCurrentScale.y, -modelCurrentScale.z);
                
                // Negate Yaw (Y rotation): extract Euler angles, negate Y, rebuild quaternion
                Vector3 modelEuler = modelObj.transform.rotation.eulerAngles;
                // Convert to -180 to 180 range for proper negation
                float modelYaw = modelEuler.y;
                if (modelYaw > 180f) modelYaw -= 360f;
                modelObj.transform.rotation = Quaternion.Euler(modelEuler.x, -modelYaw, modelEuler.z);

                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error placing static object {modelFilename}: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Adds LOD support to a static object, including Unity LOD Group with decimated meshes
        /// </summary>
        private void AddLODToObject(GameObject obj)
        {
            // Check if we should create Unity LOD Groups (requires meshes to be decimated)
            // For now, we'll use the simple distance-based culling
            // Unity LOD Groups can be added later when decimated meshes are generated
            
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

            // TODO: Create Unity LOD Group with decimated meshes
            // This would require:
            // 1. Generating LOD meshes using MeshDecimation.CreateLODLevels()
            // 2. Creating LOD Group component
            // 3. Assigning renderers to each LOD level
            // Example:
            // LODGroup lodGroup = obj.GetComponent<LODGroup>();
            // if (lodGroup == null)
            // {
            //     MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
            //     if (meshFilter != null && meshFilter.sharedMesh != null)
            //     {
            //         int[] lodTriangles = new int[] { 
            //             meshFilter.sharedMesh.triangles.Length / 3,  // LOD0: full detail
            //             (meshFilter.sharedMesh.triangles.Length / 3) / 2,  // LOD1: 50%
            //             (meshFilter.sharedMesh.triangles.Length / 3) / 4   // LOD2: 25%
            //         };
            //         Mesh[] lodMeshes = MeshDecimation.CreateLODLevels(meshFilter.sharedMesh, lodTriangles);
            //         // Create LOD Group and assign meshes...
            //     }
            // }
        }
    }
}

