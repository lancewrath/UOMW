using ESMSharp.TES3;
using ESMSharp.TES3.Records;
using ESMSharp.NIF;
using BSASharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        
        /// <summary>
        /// Queue of pending async model loads for this PlaceStatics instance
        /// Used when multithreading is enabled
        /// </summary>
        private Queue<System.Action> _pendingAsyncLoads = new Queue<System.Action>();

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
        /// Helper MonoBehaviour for running PlaceStatics coroutines
        /// </summary>
        private class PlaceStaticsCoroutineHelper : MonoBehaviour 
        {
            public static PlaceStaticsCoroutineHelper Instance { get; private set; }
            
            void Awake()
            {
                Instance = this;
            }
            
            void OnDestroy()
            {
                if (Instance == this)
                    Instance = null;
            }
        }

        /// <summary>
        /// Places statics for a single cell only (for manual testing)
        /// Synchronous version - calls coroutine version internally
        /// </summary>
        public void PlaceCellStatics(RecordCell cellRecord, Record[] allRecords, CellManager cellManager, Transform parent, Terrain terrain)
        {
            // For synchronous calls, we need a MonoBehaviour to run the coroutine
            // Create a temporary helper object
            GameObject helperObj = new GameObject("PlaceStaticsHelper");
            PlaceStaticsCoroutineHelper helper = helperObj.AddComponent<PlaceStaticsCoroutineHelper>();
            helper.StartCoroutine(PlaceCellStaticsCoroutine(cellRecord, allRecords, cellManager, parent, terrain, () =>
            {
                GameObject.Destroy(helperObj);
            }));
        }

        /// <summary>
        /// Wrapper class for placement counts (needed because coroutines can't have ref parameters)
        /// </summary>
        private class PlacementCounts
        {
            public int PlacedTrees { get; set; }
            public int FailedTrees { get; set; }
            public int PlacedGrass { get; set; }
            public int FailedGrass { get; set; }
            public int PlacedStructures { get; set; }
            public int FailedStructures { get; set; }
            public int PlacedNPCs { get; set; }
            public int FailedNPCs { get; set; }
        }

        /// <summary>
        /// Coroutine version of PlaceCellStatics for async model loading
        /// </summary>
        public IEnumerator PlaceCellStaticsCoroutine(RecordCell cellRecord, Record[] allRecords, CellManager cellManager, Transform parent, Terrain terrain, System.Action onComplete = null)
        {
            if (cellRecord == null || cellRecord.subRecords == null)
            {
                //UnityEngine.Debug.LogWarning("TESCell: Cannot generate statics - cell record is null or has no subrecords");
                yield break;
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
                yield break;
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
            SubRecordCellANAM currentANAM = null; // NPC ID (if this reference is an NPC)
            float currentScale = 1.0f;
            bool seenFirstDATA = false;

            PlacementCounts counts = new PlacementCounts();

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
                        bool placedAsNPC = false;
                        
                        // Check if this is an NPC reference (has ANAM)
                        if (currentANAM != null && !string.IsNullOrEmpty(currentANAM.name))
                        {
                            // Clean ANAM name before using it
                            string cleanedANAM = currentANAM.name.TrimEnd('\0', ' ', '\t', '\r', '\n');
                            cleanedANAM = cleanedANAM.Replace("\0", "");
                            cleanedANAM = new string(cleanedANAM.Where(c => c != '\0').ToArray()).Trim();
                            if (!string.IsNullOrEmpty(cleanedANAM))
                            {
                                // Place NPC (yield for async loading)
                                yield return PlaceNPCCoroutine(cleanedANAM, currentREFP, currentScale, cellParent, cellGridX, cellGridY, counts);
                                placedAsNPC = true;
                            }
                        }
                        
                        // If not placed as NPC yet, check if Object ID might be an NPC name (fallback detection)
                        // This handles cases where ANAM might be missing or in wrong order
                        if (!placedAsNPC)
                        {
                            string objectIdClean = currentObjectID.objectId?.TrimEnd('\0', ' ', '\t', '\r', '\n')?.Replace("\0", "");
                            if (!string.IsNullOrEmpty(objectIdClean))
                            {
                                objectIdClean = new string(objectIdClean.Where(c => c != '\0').ToArray()).Trim();
                                if (!string.IsNullOrEmpty(objectIdClean))
                                {
                                    TESCharacterManager.NPCEntry npcEntry = TESCharacterManager.GetNPC(objectIdClean);
                                    if (npcEntry != null)
                                    {
                                        // This is an NPC but ANAM wasn't detected - log and place as NPC anyway
                                        UnityEngine.Debug.LogWarning($"PlaceCellStatics: NPC '{objectIdClean}' detected by Object ID but ANAM was missing. Placing as NPC anyway.");
                                        yield return PlaceNPCCoroutine(objectIdClean, currentREFP, currentScale, cellParent, cellGridX, cellGridY, counts);
                                        placedAsNPC = true;
                                    }
                                }
                            }
                        }
                        
                        // If still not placed as NPC, place as static objects
                        if (!placedAsNPC)
                        {
                            // Place trees (yield for async loading)
                            yield return PlaceReferenceCoroutine(currentObjectID, currentREFP, currentScale, statRecordsByName, cellParent, cellGridX, cellGridY, ObjectType.Trees, terrain, parent, counts, allRecords);
                            
                            // Place grass (yield for async loading)
                            yield return PlaceReferenceCoroutine(currentObjectID, currentREFP, currentScale, statRecordsByName, cellParent, cellGridX, cellGridY, ObjectType.Grass, terrain, parent, counts, allRecords);
                            
                            // Place large structures (yield for async loading)
                            yield return PlaceReferenceCoroutine(currentObjectID, currentREFP, currentScale, statRecordsByName, cellParent, cellGridX, cellGridY, ObjectType.LargeStructures, terrain, parent, counts, allRecords);
                        }
                    }
                    currentFRMR = subrec as SubRecordCellFRMR;
                    currentREFP = null;
                    currentObjectID = null;
                    currentANAM = null;
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
                else if (subrec is SubRecordCellANAM)
                {
                    // ANAM indicates this reference is an NPC
                    currentANAM = subrec as SubRecordCellANAM;
                    // Clean ANAM name to remove null characters
                    if (currentANAM != null && !string.IsNullOrEmpty(currentANAM.name))
                    {
                        // Remove null characters from ANAM name
                        string cleanedANAM = currentANAM.name.TrimEnd('\0', ' ', '\t', '\r', '\n');
                        cleanedANAM = cleanedANAM.Replace("\0", "");
                        cleanedANAM = new string(cleanedANAM.Where(c => c != '\0').ToArray()).Trim();
                        // Create a new SubRecordCellANAM with cleaned name (or update the existing one if possible)
                        // For now, we'll clean it when we use it in PlaceNPC
                        // Debug: Log ANAM content to help diagnose NPC detection issues
                        UnityEngine.Debug.Log($"PlaceCellStatics: Found ANAM record with NPC ID: '{currentANAM.name}' (cleaned: '{cleanedANAM}') (Object ID: '{currentObjectID?.objectId ?? "null"}')");
                    }
                    else if (currentANAM != null)
                    {
                        UnityEngine.Debug.LogWarning($"PlaceCellStatics: Found ANAM record but it is null or empty (Object ID: '{currentObjectID?.objectId ?? "null"}')");
                    }
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
                bool placedAsNPC = false;
                
                // Check if this is an NPC reference (has ANAM)
                if (currentANAM != null && !string.IsNullOrEmpty(currentANAM.name))
                {
                    // Clean ANAM name before using it
                    string cleanedANAM = currentANAM.name.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    cleanedANAM = cleanedANAM.Replace("\0", "");
                    cleanedANAM = new string(cleanedANAM.Where(c => c != '\0').ToArray()).Trim();
                    if (!string.IsNullOrEmpty(cleanedANAM))
                    {
                        // Place NPC (yield for async loading)
                        yield return PlaceNPCCoroutine(cleanedANAM, currentREFP, currentScale, cellParent, cellGridX, cellGridY, counts);
                        placedAsNPC = true;
                    }
                }
                
                // If not placed as NPC yet, check if Object ID might be an NPC name (fallback detection)
                // This handles cases where ANAM might be missing or in wrong order
                if (!placedAsNPC)
                {
                    string objectIdClean = currentObjectID.objectId?.TrimEnd('\0', ' ', '\t', '\r', '\n')?.Replace("\0", "");
                    if (!string.IsNullOrEmpty(objectIdClean))
                    {
                        objectIdClean = new string(objectIdClean.Where(c => c != '\0').ToArray()).Trim();
                        if (!string.IsNullOrEmpty(objectIdClean))
                        {
                            TESCharacterManager.NPCEntry npcEntry = TESCharacterManager.GetNPC(objectIdClean);
                            if (npcEntry != null)
                            {
                                // This is an NPC but ANAM wasn't detected - log and place as NPC anyway
                                UnityEngine.Debug.LogWarning($"PlaceCellStatics: NPC '{objectIdClean}' detected by Object ID but ANAM was missing. Placing as NPC anyway.");
                                yield return PlaceNPCCoroutine(objectIdClean, currentREFP, currentScale, cellParent, cellGridX, cellGridY, counts);
                                placedAsNPC = true;
                            }
                        }
                    }
                }
                
                // If still not placed as NPC, place as static objects
                if (!placedAsNPC)
                {
                    // Place trees (yield for async loading)
                    yield return PlaceReferenceCoroutine(currentObjectID, currentREFP, currentScale, statRecordsByName, cellParent, cellGridX, cellGridY, ObjectType.Trees, terrain, parent, counts, allRecords);
                    
                    // Place grass (yield for async loading)
                    yield return PlaceReferenceCoroutine(currentObjectID, currentREFP, currentScale, statRecordsByName, cellParent, cellGridX, cellGridY, ObjectType.Grass, terrain, parent, counts, allRecords);
                    
                    // Place large structures (yield for async loading)
                    yield return PlaceReferenceCoroutine(currentObjectID, currentREFP, currentScale, statRecordsByName, cellParent, cellGridX, cellGridY, ObjectType.LargeStructures, terrain, parent, counts, allRecords);
                }
            }
            
            onComplete?.Invoke();

            //UnityEngine.Debug.Log($"TESCell ({cellGridX}, {cellGridY}): Placed {counts.PlacedTrees} trees ({counts.FailedTrees} failed), {counts.PlacedGrass} grass ({counts.FailedGrass} failed), {counts.PlacedStructures} structures ({counts.FailedStructures} failed)");
            
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
                // Note: ANAM (NPC ID) is ignored here - this method is for distant visual objects only
                // NPCs are handled in PlaceCellStatics() instead
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
                            // Place static object (NPCs are skipped in this method - for distant visuals only)
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
                    // Note: ANAM (NPC ID) is intentionally ignored here - this method is for distant visual objects only
                    else if (subrec is SubRecordCellXSCL xscal)
                    {
                        // XSCL contains the object scale
                        currentScale = xscal.scale;
                    }
                }

                // Place the last reference if we have one
                if (currentObjectID != null && currentREFP != null)
                {
                    // Place static object (NPCs are skipped in this method - for distant visuals only)
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
                GameObject treeModel = LoadNIFModel(baseFilename, combineMeshes: true);
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
        /// Coroutine version of PlaceTree that uses async model loading
        /// </summary>
        private IEnumerator PlaceTreeCoroutine(string modelFilename, SubRecordCellREFP refp, float scale, SubRecordCellObjectID objectId, Terrain terrain, int cellGridX, int cellGridY, Transform parent, System.Action<bool> onComplete)
        {
            bool result = false;
            string baseFilename = Path.GetFileName(modelFilename);
            string baseFilenameNoExt = Path.GetFileNameWithoutExtension(baseFilename);
            
            try
            {
                // Check cache first
                TESNifLibrary.NifEntry cachedEntry = TESNifLibrary.GetModelByBaseFilename(baseFilenameNoExt);
                if (cachedEntry != null)
                {
                    GameObject instanceObj = GameObject.Instantiate(cachedEntry.Model);
                    instanceObj.name = cachedEntry.Model.name;
                    
                    float instanceScaledX = refp.x * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                    float instanceScaledZ = refp.y * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                    float instanceScaledY = refp.z * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                    
                    Vector3 instancePosition = new Vector3(instanceScaledX, instanceScaledY, instanceScaledZ);
                    Quaternion instanceRotation = Quaternion.Euler(-refp.yaw * Mathf.Rad2Deg, -refp.pitch * Mathf.Rad2Deg, -refp.roll * Mathf.Rad2Deg);
                    
                    instanceObj.transform.position = instancePosition;
                    instanceObj.transform.rotation = instanceRotation;
                    
                    Vector3 instanceBaseScale = new Vector3(TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE);
                    instanceObj.transform.localScale = instanceBaseScale * (scale < 0 ? Mathf.Abs(scale) : scale);
                    
                    if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                        instanceObj.name = objectId.objectId.TrimEnd('\0');
                    
                    Vector3 currentScaleLocal = instanceObj.transform.localScale;
                    instanceObj.transform.localScale = new Vector3(currentScaleLocal.x, currentScaleLocal.y, -currentScaleLocal.z);
                    
                    Vector3 euler = instanceObj.transform.rotation.eulerAngles;
                    float yaw = euler.y;
                    if (yaw > 180f) yaw -= 360f;
                    instanceObj.transform.rotation = Quaternion.Euler(euler.x, -yaw, euler.z);
                    
                    AddLODToObject(instanceObj);
                    if (parent != null)
                        instanceObj.transform.SetParent(parent, worldPositionStays: true);
                    
                    result = true;
                    onComplete?.Invoke(result);
                    yield break;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error placing tree {modelFilename}: {ex.Message}\n{ex.StackTrace}");
                result = false;
                onComplete?.Invoke(result);
                yield break;
            }
            
            // Load model asynchronously
            GameObject treeModel = null;
            yield return LoadNIFModelCoroutine(baseFilename, true, (loadedModel) => treeModel = loadedModel);
            
            if (treeModel == null)
            {
                onComplete?.Invoke(false);
                yield break;
            }
            
            try
            {
                string staticId = objectId?.objectId?.TrimEnd('\0');
                string staticName = staticId;
                TESNifLibrary.AddModel(staticId, staticName, baseFilename, baseFilenameNoExt, treeModel, combineMeshes: true);
                
                float scaledX = refp.x * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                float scaledZ = refp.y * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                float scaledY = refp.z * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                
                Vector3 unityPosition = new Vector3(scaledX, scaledY, scaledZ);
                Quaternion unityRotation = Quaternion.Euler(-refp.yaw * Mathf.Rad2Deg, -refp.pitch * Mathf.Rad2Deg, -refp.roll * Mathf.Rad2Deg);
                
                treeModel.transform.position = unityPosition;
                treeModel.transform.rotation = unityRotation;
                
                Vector3 modelBaseScale = treeModel.transform.localScale;
                treeModel.transform.localScale = modelBaseScale * (scale < 0 ? Mathf.Abs(scale) : scale);
                
                if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                    treeModel.name = objectId.objectId.TrimEnd('\0');
                
                Vector3 currentScaleLocal2 = treeModel.transform.localScale;
                treeModel.transform.localScale = new Vector3(currentScaleLocal2.x, currentScaleLocal2.y, -currentScaleLocal2.z);
                
                Vector3 euler2 = treeModel.transform.rotation.eulerAngles;
                float yaw2 = euler2.y;
                if (yaw2 > 180f) yaw2 -= 360f;
                treeModel.transform.rotation = Quaternion.Euler(euler2.x, -yaw2, euler2.z);
                
                AddLODToObject(treeModel);
                if (parent != null)
                    treeModel.transform.SetParent(parent, worldPositionStays: true);
                
                result = true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error placing tree {modelFilename}: {ex.Message}\n{ex.StackTrace}");
                result = false;
            }
            
            onComplete?.Invoke(result);
        }

        /// <summary>
        /// Coroutine version of PlaceGrassDetail that uses async model loading
        /// </summary>
        private IEnumerator PlaceGrassDetailCoroutine(string modelFilename, SubRecordCellREFP refp, float scale, SubRecordCellObjectID objectId, Terrain terrain, int cellGridX, int cellGridY, Transform parent, System.Action<bool> onComplete)
        {
            bool result = false;
            string baseFilename = Path.GetFileName(modelFilename);
            string baseFilenameNoExt = Path.GetFileNameWithoutExtension(baseFilename);
            
            try
            {
                TESNifLibrary.NifEntry cachedEntry = TESNifLibrary.GetModelByBaseFilename(baseFilenameNoExt);
                if (cachedEntry != null)
                {
                    GameObject instanceObj = GameObject.Instantiate(cachedEntry.Model);
                    instanceObj.name = cachedEntry.Model.name;
                    
                    float instanceScaledX = refp.x * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                    float instanceScaledZ = refp.y * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                    float instanceScaledY = refp.z * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                    
                    Vector3 instancePosition = new Vector3(instanceScaledX, instanceScaledY, instanceScaledZ);
                    Quaternion instanceRotation = Quaternion.Euler(-refp.yaw * Mathf.Rad2Deg, -refp.pitch * Mathf.Rad2Deg, -refp.roll * Mathf.Rad2Deg);
                    
                    instanceObj.transform.position = instancePosition;
                    instanceObj.transform.rotation = instanceRotation;
                    
                    Vector3 instanceBaseScale = new Vector3(TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE);
                    instanceObj.transform.localScale = instanceBaseScale * (scale < 0 ? Mathf.Abs(scale) : scale);
                    
                    if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                        instanceObj.name = objectId.objectId.TrimEnd('\0');
                    
                    Vector3 currentScaleLocal = instanceObj.transform.localScale;
                    instanceObj.transform.localScale = new Vector3(currentScaleLocal.x, currentScaleLocal.y, -currentScaleLocal.z);
                    
                    Vector3 euler = instanceObj.transform.rotation.eulerAngles;
                    float yaw = euler.y;
                    if (yaw > 180f) yaw -= 360f;
                    instanceObj.transform.rotation = Quaternion.Euler(euler.x, -yaw, euler.z);
                    
                    MeshCollider collider = instanceObj.GetComponent<MeshCollider>();
                    if (collider != null)
                    {
                        collider.convex = true;
                        collider.isTrigger = true;
                    }
                    else
                    {
                        MeshFilter meshFilter = instanceObj.GetComponent<MeshFilter>();
                        if (meshFilter != null && meshFilter.sharedMesh != null)
                        {
                            collider = instanceObj.AddComponent<MeshCollider>();
                            collider.sharedMesh = meshFilter.sharedMesh;
                            collider.convex = true;
                            collider.isTrigger = true;
                        }
                    }
                    
                    AddLODToObject(instanceObj);
                    if (parent != null)
                        instanceObj.transform.SetParent(parent, worldPositionStays: true);
                    
                    result = true;
                    onComplete?.Invoke(result);
                    yield break;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error placing grass {modelFilename}: {ex.Message}\n{ex.StackTrace}");
                result = false;
                onComplete?.Invoke(result);
                yield break;
            }
            
            GameObject grassModel = null;
            yield return LoadNIFModelCoroutine(baseFilename, true, (loadedModel) => grassModel = loadedModel);
            
            if (grassModel == null)
            {
                onComplete?.Invoke(false);
                yield break;
            }
            
            try
            {
                string staticId = objectId?.objectId?.TrimEnd('\0');
                string staticName = staticId;
                TESNifLibrary.AddModel(staticId, staticName, baseFilename, baseFilenameNoExt, grassModel, combineMeshes: true);
                
                float scaledX = refp.x * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                float scaledZ = refp.y * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                float scaledY = refp.z * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                
                Vector3 unityPosition = new Vector3(scaledX, scaledY, scaledZ);
                Quaternion unityRotation = Quaternion.Euler(-refp.yaw * Mathf.Rad2Deg, -refp.pitch * Mathf.Rad2Deg, -refp.roll * Mathf.Rad2Deg);
                
                grassModel.transform.position = unityPosition;
                grassModel.transform.rotation = unityRotation;
                
                Vector3 modelBaseScale = grassModel.transform.localScale;
                grassModel.transform.localScale = modelBaseScale * (scale < 0 ? Mathf.Abs(scale) : scale);
                
                if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                    grassModel.name = objectId.objectId.TrimEnd('\0');
                
                Vector3 currentScaleLocal2 = grassModel.transform.localScale;
                grassModel.transform.localScale = new Vector3(currentScaleLocal2.x, currentScaleLocal2.y, -currentScaleLocal2.z);
                
                Vector3 euler2 = grassModel.transform.rotation.eulerAngles;
                float yaw2 = euler2.y;
                if (yaw2 > 180f) yaw2 -= 360f;
                grassModel.transform.rotation = Quaternion.Euler(euler2.x, -yaw2, euler2.z);
                
                MeshCollider meshCollider = grassModel.GetComponent<MeshCollider>();
                if (meshCollider != null)
                {
                    meshCollider.convex = true;
                    meshCollider.isTrigger = true;
                }
                else
                {
                    MeshFilter meshFilter = grassModel.GetComponent<MeshFilter>();
                    if (meshFilter != null && meshFilter.sharedMesh != null)
                    {
                        meshCollider = grassModel.AddComponent<MeshCollider>();
                        meshCollider.sharedMesh = meshFilter.sharedMesh;
                        meshCollider.convex = true;
                        meshCollider.isTrigger = true;
                    }
                }
                
                AddLODToObject(grassModel);
                if (parent != null)
                    grassModel.transform.SetParent(parent, worldPositionStays: true);
                
                result = true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error placing grass {modelFilename}: {ex.Message}\n{ex.StackTrace}");
                result = false;
            }
            
            onComplete?.Invoke(result);
        }

        /// <summary>
        /// Coroutine version of PlaceStaticObject that uses async model loading
        /// </summary>
        private IEnumerator PlaceStaticObjectCoroutine(string modelFilename, SubRecordCellREFP refp, float scale, SubRecordCellObjectID objectId, Transform parent, int cellGridX, int cellGridY, Record[] allRecords, System.Action<bool> onComplete)
        {
            bool result = false;
            string baseFilename = Path.GetFileName(modelFilename);
            string baseFilenameNoExt = Path.GetFileNameWithoutExtension(baseFilename);
            
            TESNifLibrary.NifEntry cachedEntry = TESNifLibrary.GetModelByBaseFilename(baseFilenameNoExt);
            if (cachedEntry != null)
            {
                try
                {
                    GameObject instanceObj = GameObject.Instantiate(cachedEntry.Model);
                    instanceObj.name = cachedEntry.Model.name;
                    
                    float scaledX = refp.x * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                    float scaledZ = refp.y * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                    float scaledY = refp.z * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                    
                    Vector3 unityPosition = new Vector3(scaledX, scaledY, scaledZ);
                    Quaternion unityRotation = Quaternion.Euler(-refp.yaw * Mathf.Rad2Deg, -refp.pitch * Mathf.Rad2Deg, -refp.roll * Mathf.Rad2Deg);
                    
                    instanceObj.transform.position = unityPosition;
                    instanceObj.transform.rotation = unityRotation;
                    
                    Vector3 instanceBaseScale = new Vector3(TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE);
                    instanceObj.transform.localScale = instanceBaseScale * (scale < 0 ? Mathf.Abs(scale) : scale);
                    
                    if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                        instanceObj.name = objectId.objectId.TrimEnd('\0');
                    
                    AddLODToObject(instanceObj);
                    if (parent != null)
                        instanceObj.transform.SetParent(parent, worldPositionStays: true);
                    
                    Vector3 currentScale = instanceObj.transform.localScale;
                    instanceObj.transform.localScale = new Vector3(currentScale.x, currentScale.y, -currentScale.z);
                    
                    Vector3 euler = instanceObj.transform.rotation.eulerAngles;
                    float yaw = euler.y;
                    if (yaw > 180f) yaw -= 360f;
                    instanceObj.transform.rotation = Quaternion.Euler(euler.x, -yaw, euler.z);
                    
                    result = true;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"Error placing static object {modelFilename}: {ex.Message}\n{ex.StackTrace}");
                    result = false;
                }
                
                onComplete?.Invoke(result);
                yield break;
            }
            
            GameObject modelObj = null;
            yield return LoadNIFModelCoroutine(baseFilename, false, (loadedModel) => modelObj = loadedModel);
            
            if (modelObj == null)
            {
                onComplete?.Invoke(false);
                yield break;
            }
            
            try
            {
                string staticId = objectId?.objectId?.TrimEnd('\0');
                string staticName = staticId;
                TESNifLibrary.AddModel(staticId, staticName, modelFilename, baseFilenameNoExt, modelObj, combineMeshes: false);
                
                float scaledX2 = refp.x * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                float scaledZ2 = refp.y * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                float scaledY2 = refp.z * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                
                Vector3 unityPosition2 = new Vector3(scaledX2, scaledY2, scaledZ2);
                Quaternion unityRotation2 = Quaternion.Euler(-refp.yaw * Mathf.Rad2Deg, -refp.pitch * Mathf.Rad2Deg, -refp.roll * Mathf.Rad2Deg);
                
                modelObj.transform.position = unityPosition2;
                modelObj.transform.rotation = unityRotation2;
                
                Vector3 modelBaseScale = modelObj.transform.localScale;
                modelObj.transform.localScale = modelBaseScale * (scale < 0 ? Mathf.Abs(scale) : scale);
                
                if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                    modelObj.name = objectId.objectId.TrimEnd('\0');
                else
                    modelObj.name = Path.GetFileNameWithoutExtension(modelFilename);
                
                AddLODToObject(modelObj);
                if (parent != null)
                    modelObj.transform.SetParent(parent, worldPositionStays: true);
                
                Vector3 modelCurrentScale = modelObj.transform.localScale;
                modelObj.transform.localScale = new Vector3(modelCurrentScale.x, modelCurrentScale.y, -modelCurrentScale.z);
                
                Vector3 modelEuler = modelObj.transform.rotation.eulerAngles;
                float modelYaw = modelEuler.y;
                if (modelYaw > 180f) modelYaw -= 360f;
                modelObj.transform.rotation = Quaternion.Euler(modelEuler.x, -modelYaw, modelEuler.z);
                
                result = true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error placing static object {modelFilename}: {ex.Message}\n{ex.StackTrace}");
                result = false;
            }
            
            onComplete?.Invoke(result);
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
                    // Note: Triggers require convex MeshColliders
                    MeshCollider instanceMeshCollider = instanceObj.GetComponent<MeshCollider>();
                    if (instanceMeshCollider != null)
                    {
                        instanceMeshCollider.convex = true; // Must be convex for triggers
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
                            instanceMeshCollider.convex = true; // Must be convex for triggers
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
                GameObject grassModel = LoadNIFModel(baseFilename, combineMeshes: true);
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
                // Note: Triggers require convex MeshColliders
                MeshCollider meshCollider = grassModel.GetComponent<MeshCollider>();
                if (meshCollider != null)
                {
                    meshCollider.convex = true; // Must be convex for triggers
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
                        meshCollider.convex = true; // Must be convex for triggers
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
        /// Coroutine version of PlaceReference that yields during model loading
        /// </summary>
        private IEnumerator PlaceReferenceCoroutine(SubRecordCellObjectID objectId, SubRecordCellREFP refp, float scale, Dictionary<string, RecordStat> statRecordsByName, GameObject cellParent, int cellGridX, int cellGridY, ObjectType objectType, Terrain terrain, Transform parent, PlacementCounts counts, Record[] allRecords = null)
        {
            if (objectId == null || string.IsNullOrEmpty(objectId.objectId))
            {
                UnityEngine.Debug.LogWarning("Reference has no Object ID (model ID)");
                switch (objectType)
                {
                    case ObjectType.Trees: counts.FailedTrees++; break;
                    case ObjectType.Grass: counts.FailedGrass++; break;
                    case ObjectType.LargeStructures: counts.FailedStructures++; break;
                }
                yield break;
            }

            // Clean the model ID name
            string modelId = objectId.objectId.TrimEnd('\0', ' ', '\t', '\r', '\n');
            modelId = modelId.Replace("\0", "");
            modelId = modelId.Trim();

            // Check if this might be an NPC (quick check - if it's not in STAT records)
            string cleanedModelId = new string(modelId.Where(c => c != '\0').ToArray()).Trim();
            bool mightBeNPC = !statRecordsByName.ContainsKey(modelId) && !statRecordsByName.ContainsKey(cleanedModelId);
            
            if (mightBeNPC)
            {
                TESCharacterManager.NPCEntry npcEntry = TESCharacterManager.GetNPC(modelId);
                if (npcEntry == null && !string.Equals(modelId, cleanedModelId, StringComparison.OrdinalIgnoreCase))
                {
                    npcEntry = TESCharacterManager.GetNPC(cleanedModelId);
                }
                
                if (npcEntry != null)
                {
                    UnityEngine.Debug.LogWarning($"PlaceReference: Found NPC '{modelId}' but ANAM was not detected. This reference should have been handled by PlaceNPC().");
                    switch (objectType)
                    {
                        case ObjectType.Trees: counts.FailedTrees++; break;
                        case ObjectType.Grass: counts.FailedGrass++; break;
                        case ObjectType.LargeStructures: counts.FailedStructures++; break;
                    }
                    yield break;
                }
            }

            string modelFilename = null;

            // First, try to look up STAT record by name
            if (statRecordsByName.ContainsKey(modelId))
            {
                RecordStat statRecord = statRecordsByName[modelId];
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
                    modelFilename = modelFilename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    modelFilename = modelFilename.Replace("\0", "");
                    modelFilename = modelFilename.Trim();
                }
            }

            // If STAT record lookup failed, try to find NIF file directly in cache
            if (string.IsNullOrEmpty(modelFilename))
            {
                string cacheDir = Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", _esm);
                if (Directory.Exists(cacheDir))
                {
                    string[] extensions = { ".nif", ".NIF" };
                    foreach (string ext in extensions)
                    {
                        string testPath = Path.Combine(cacheDir, modelId + ext);
                        if (File.Exists(testPath))
                        {
                            modelFilename = Path.GetFileName(testPath);
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(modelFilename))
                    {
                        string[] files = Directory.GetFiles(cacheDir, "*.nif", SearchOption.AllDirectories);
                        foreach (string file in files)
                        {
                            string fileName = Path.GetFileNameWithoutExtension(file);
                            if (string.Equals(fileName, modelId, StringComparison.OrdinalIgnoreCase))
                            {
                                modelFilename = Path.GetFileName(file);
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
                switch (objectType)
                {
                    case ObjectType.Trees: counts.FailedTrees++; break;
                    case ObjectType.Grass: counts.FailedGrass++; break;
                    case ObjectType.LargeStructures: counts.FailedStructures++; break;
                }
                yield break;
            }

            // Filter based on object type
            string baseFilename = Path.GetFileNameWithoutExtension(modelFilename).ToLower();
            bool shouldPlace = false;
            switch (objectType)
            {
                case ObjectType.LargeStructures:
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
                yield break;
            }

            // Place the object based on type (using async coroutine versions)
            bool success = false;
            if (objectType == ObjectType.Trees)
            {
                Transform refParent = cellParent != null ? cellParent.transform : parent;
                yield return PlaceTreeCoroutine(modelFilename, refp, scale, objectId, terrain, cellGridX, cellGridY, refParent, (result) => success = result);
            }
            else if (objectType == ObjectType.Grass)
            {
                Transform refParent = cellParent != null ? cellParent.transform : parent;
                yield return PlaceGrassDetailCoroutine(modelFilename, refp, scale, objectId, terrain, cellGridX, cellGridY, refParent, (result) => success = result);
            }
            else if (objectType == ObjectType.LargeStructures)
            {
                Transform refParent = cellParent != null ? cellParent.transform : parent;
                yield return PlaceStaticObjectCoroutine(modelFilename, refp, scale, objectId, refParent, cellGridX, cellGridY, allRecords, (result) => success = result);
            }
            
            // Update counts based on result
            if (success)
            {
                switch (objectType)
                {
                    case ObjectType.Trees: counts.PlacedTrees++; break;
                    case ObjectType.Grass: counts.PlacedGrass++; break;
                    case ObjectType.LargeStructures: counts.PlacedStructures++; break;
                }
            }
            else
            {
                switch (objectType)
                {
                    case ObjectType.Trees: counts.FailedTrees++; break;
                    case ObjectType.Grass: counts.FailedGrass++; break;
                    case ObjectType.LargeStructures: counts.FailedStructures++; break;
                }
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

            // Check if this might be an NPC (quick check - if it's not in STAT records)
            // This is a performance optimization to avoid checking NPCs for every static
            // Also clean the modelId to remove null characters before checking
            string cleanedModelId = new string(modelId.Where(c => c != '\0').ToArray()).Trim();
            bool mightBeNPC = !statRecordsByName.ContainsKey(modelId) && !statRecordsByName.ContainsKey(cleanedModelId);
            
            // If it might be an NPC, check TESCharacterManager before trying STAT lookup
            if (mightBeNPC)
            {
                // Try both the original and cleaned version
                TESCharacterManager.NPCEntry npcEntry = TESCharacterManager.GetNPC(modelId);
                if (npcEntry == null && !string.Equals(modelId, cleanedModelId, StringComparison.OrdinalIgnoreCase))
                {
                    npcEntry = TESCharacterManager.GetNPC(cleanedModelId);
                }
                
                if (npcEntry != null)
                {
                    // This is an NPC, but we don't have ANAM in this context
                    // Log debug info to help diagnose
                    UnityEngine.Debug.LogWarning($"PlaceReference: Found NPC '{modelId}' (cleaned: '{cleanedModelId}') but ANAM was not detected. This reference should have been handled by PlaceNPC(). Object ID: '{objectId.objectId}'");
                    failedCount++;
                    return;
                }
            }

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

                // Check if already loaded in global library
                string baseFilenameNoExt = Path.GetFileNameWithoutExtension(baseFilename);
                TESNifLibrary.NifEntry cachedEntry = TESNifLibrary.GetModelByBaseFilename(baseFilenameNoExt);
                if (cachedEntry != null)
                {
                    return GetBoundingSphereRadiusFromGameObject(cachedEntry.Model, scale);
                }

                // Try to load the model temporarily
                GameObject tempModel = LoadNIFModel(baseFilename, combineMeshes: false);
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
                // Use async loading if enabled, otherwise synchronous
                GameObject modelObj = LoadNIFModel(baseFilename, combineMeshes: false);
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

        /// <summary>
        /// Coroutine version of PlaceNPC that yields during model loading
        /// </summary>
        private IEnumerator PlaceNPCCoroutine(string npcId, SubRecordCellREFP refp, float scale, GameObject cellParent, int cellGridX, int cellGridY, PlacementCounts counts)
        {
            int placedCount = 0;
            int failedCount = 0;
            
            // Declare variables outside try block so they're accessible after
            TESCharacterManager.BodyPartEntry bodyPart = null;
            TESCharacterManager.BodyPartEntry headPart = null;
            TESCharacterManager.BodyPartEntry hairPart = null;
            GameObject npcObj = null;
            
            try
            {
                // Clean NPC ID
                npcId = npcId?.TrimEnd('\0', ' ', '\t', '\r', '\n');
                npcId = npcId?.Replace("\0", "");
                npcId = npcId?.Trim();
                
                if (!string.IsNullOrEmpty(npcId))
                {
                    npcId = new string(npcId.Where(c => c != '\0').ToArray()).Trim();
                }

                if (string.IsNullOrEmpty(npcId))
                {
                    UnityEngine.Debug.LogWarning("PlaceNPC: NPC ID is null or empty");
                    failedCount++;
                    counts.PlacedNPCs += placedCount;
                    counts.FailedNPCs += failedCount;
                    yield break;
                }

                TESCharacterManager.NPCEntry npcEntry = TESCharacterManager.GetNPC(npcId);
                if (npcEntry == null)
                {
                    UnityEngine.Debug.LogWarning($"PlaceNPC: NPC '{npcId}' not found in TESCharacterManager");
                    failedCount++;
                    counts.PlacedNPCs += placedCount;
                    counts.FailedNPCs += failedCount;
                    yield break;
                }

                // Get body part entries
                if (!string.IsNullOrEmpty(npcEntry.ModelFilename))
                {
                    string cleanedBodyPartId = npcEntry.ModelFilename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    cleanedBodyPartId = cleanedBodyPartId.Replace("\0", "");
                    cleanedBodyPartId = new string(cleanedBodyPartId.Where(c => c != '\0').ToArray()).Trim();
                    
                    if (!string.IsNullOrEmpty(cleanedBodyPartId))
                    {
                        bodyPart = TESCharacterManager.GetBodyPart(cleanedBodyPartId);
                    }
                }
                
                if (!string.IsNullOrEmpty(npcEntry.HeadModel))
                {
                    string cleanedHeadPartId = npcEntry.HeadModel.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    cleanedHeadPartId = cleanedHeadPartId.Replace("\0", "");
                    cleanedHeadPartId = new string(cleanedHeadPartId.Where(c => c != '\0').ToArray()).Trim();
                    
                    if (!string.IsNullOrEmpty(cleanedHeadPartId))
                    {
                        headPart = TESCharacterManager.GetBodyPart(cleanedHeadPartId);
                    }
                }
                
                if (!string.IsNullOrEmpty(npcEntry.HairModel))
                {
                    string cleanedHairPartId = npcEntry.HairModel.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    cleanedHairPartId = cleanedHairPartId.Replace("\0", "");
                    cleanedHairPartId = new string(cleanedHairPartId.Where(c => c != '\0').ToArray()).Trim();
                    
                    if (!string.IsNullOrEmpty(cleanedHairPartId))
                    {
                        hairPart = TESCharacterManager.GetBodyPart(cleanedHairPartId);
                    }
                }

                // Convert coordinates
                float scaledX = refp.x * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                float scaledZ = refp.y * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                float scaledY = refp.z * TESGlobals.MORROWIND_TO_STATIC_SCALE;

                Vector3 unityPosition = new Vector3(scaledX, scaledY, scaledZ);
                Quaternion unityRotation = Quaternion.Euler(-refp.yaw * Mathf.Rad2Deg, -refp.pitch * Mathf.Rad2Deg, -refp.roll * Mathf.Rad2Deg);

                npcObj = new GameObject($"{npcEntry.DisplayName ?? npcId} (NPC)");
                npcObj.transform.position = unityPosition;
                npcObj.transform.rotation = unityRotation;
                
                Vector3 characterBaseScale = new Vector3(TESGlobals.MORROWIND_TO_CHARACTER_SCALE, TESGlobals.MORROWIND_TO_CHARACTER_SCALE, TESGlobals.MORROWIND_TO_CHARACTER_SCALE);
                npcObj.transform.localScale = characterBaseScale * (scale < 0 ? Mathf.Abs(scale) : scale);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"PlaceNPC: Exception while setting up NPC '{npcId}' in cell ({cellGridX}, {cellGridY}): {ex.Message}");
                failedCount++;
                counts.PlacedNPCs += placedCount;
                counts.FailedNPCs += failedCount;
                yield break;
            }
            
            if (npcObj == null)
            {
                // Setup failed, can't continue
                counts.PlacedNPCs += placedCount;
                counts.FailedNPCs += failedCount;
                yield break;
            }
            
            // Load body parts asynchronously (outside try-catch to allow yield return)
            int loadedPartsCount = 0;
            GameObject bodyModel = null;
            GameObject headModel = null;
            GameObject hairModel = null;
            
            if (bodyPart != null && !string.IsNullOrEmpty(bodyPart.ModelFilename))
            {
                yield return LoadBodyPartModelCoroutine(bodyPart.ModelFilename, bodyPart.BodyPartId, (loadedModel) => bodyModel = loadedModel);
            }
            
            if (headPart != null && !string.IsNullOrEmpty(headPart.ModelFilename))
            {
                yield return LoadBodyPartModelCoroutine(headPart.ModelFilename, headPart.BodyPartId, (loadedModel) => headModel = loadedModel);
            }
            
            if (hairPart != null && !string.IsNullOrEmpty(hairPart.ModelFilename))
            {
                yield return LoadBodyPartModelCoroutine(hairPart.ModelFilename, hairPart.BodyPartId, (loadedModel) => hairModel = loadedModel);
            }
            
            try
            {
                if (bodyModel != null)
                {
                    bodyModel.transform.SetParent(npcObj.transform, false);
                    bodyModel.name = "Body";
                    loadedPartsCount++;
                }
                
                if (headModel != null)
                {
                    headModel.transform.SetParent(npcObj.transform, false);
                    headModel.name = "Head";
                    loadedPartsCount++;
                }
                
                if (hairModel != null)
                {
                    hairModel.transform.SetParent(npcObj.transform, false);
                    hairModel.name = "Hair";
                    loadedPartsCount++;
                }
                
                if (loadedPartsCount == 0)
                {
                    UnityEngine.Debug.LogWarning($"PlaceNPC: NPC '{npcId}' has no loadable body parts.");
                    GameObject.Destroy(npcObj);
                    failedCount++;
                    counts.PlacedNPCs += placedCount;
                    counts.FailedNPCs += failedCount;
                    yield break;
                }

                // Fix reflection issues
                Vector3 currentScale = npcObj.transform.localScale;
                npcObj.transform.localScale = new Vector3(currentScale.x, currentScale.y, -currentScale.z);

                Vector3 euler = npcObj.transform.rotation.eulerAngles;
                float yaw = euler.y;
                if (yaw > 180f) yaw -= 360f;
                npcObj.transform.rotation = Quaternion.Euler(euler.x, -yaw, euler.z);

                Transform parentTransform = cellParent != null ? cellParent.transform : null;
                if (parentTransform != null)
                {
                    npcObj.transform.SetParent(parentTransform, worldPositionStays: true);
                }

                placedCount++;
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"PlaceNPC: Exception while placing NPC '{npcId}' in cell ({cellGridX}, {cellGridY}): {ex.Message}");
                failedCount++;
            }
            
            counts.PlacedNPCs += placedCount;
            counts.FailedNPCs += failedCount;
        }
        
        /// <summary>
        /// Coroutine version of LoadBodyPartModel that uses async model loading
        /// </summary>
        private IEnumerator LoadBodyPartModelCoroutine(string modelFilename, string bodyPartId, System.Action<GameObject> onComplete)
        {
            if (string.IsNullOrEmpty(modelFilename))
            {
                onComplete?.Invoke(null);
                yield break;
            }
            
            modelFilename = modelFilename.TrimEnd('\0', ' ', '\t', '\r', '\n');
            modelFilename = modelFilename.Replace("\0", "");
            modelFilename = modelFilename.Trim();
            modelFilename = modelFilename.Replace('\\', '/');
            
            string baseFilename = Path.GetFileName(modelFilename);
            string baseFilenameNoExt = Path.GetFileNameWithoutExtension(baseFilename);
            
            TESNifLibrary.NifEntry cachedEntry = TESNifLibrary.GetModelByBaseFilename(baseFilenameNoExt);
            if (cachedEntry == null)
            {
                string baseFilenameLower = baseFilenameNoExt.ToLowerInvariant();
                cachedEntry = TESNifLibrary.GetModelByBaseFilename(baseFilenameLower);
            }
            
            if (cachedEntry != null)
            {
                GameObject instantiatedModel = GameObject.Instantiate(cachedEntry.Model);
                instantiatedModel.name = baseFilenameNoExt;
                onComplete?.Invoke(instantiatedModel);
                yield break;
            }
            
            GameObject modelObj = null;
            yield return LoadNIFModelCoroutine(baseFilename, false, (loadedModel) => modelObj = loadedModel);
            
            if (modelObj == null)
            {
                // Try case-insensitive search
                string cacheDir = Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", _esm);
                if (Directory.Exists(cacheDir))
                {
                    string[] files = Directory.GetFiles(cacheDir, "*.nif", SearchOption.TopDirectoryOnly);
                    string baseFilenameLower = baseFilenameNoExt.ToLowerInvariant();
                    foreach (string file in files)
                    {
                        string fileName = Path.GetFileName(file);
                        string fileNameNoExt = Path.GetFileNameWithoutExtension(fileName);
                        if (string.Equals(fileNameNoExt, baseFilenameLower, StringComparison.OrdinalIgnoreCase))
                        {
                            yield return LoadNIFModelCoroutine(fileName, false, (loadedModel) => modelObj = loadedModel);
                            if (modelObj != null)
                            {
                                baseFilename = fileName;
                                baseFilenameNoExt = fileNameNoExt;
                                break;
                            }
                        }
                    }
                }
            }
            
            if (modelObj != null)
            {
                TESNifLibrary.AddModel(bodyPartId, bodyPartId, modelFilename, baseFilenameNoExt, modelObj, combineMeshes: false);
                modelObj.name = baseFilenameNoExt;
                onComplete?.Invoke(modelObj);
                yield break;
            }
            
            // Try BSA extraction
            string extractedFilename = TryExtractFromBSA(baseFilenameNoExt);
            if (!string.IsNullOrEmpty(extractedFilename))
            {
                string normalizedExtractedFilename = Path.GetFileName(extractedFilename);
                normalizedExtractedFilename = normalizedExtractedFilename.Replace('\\', '/');
                
                yield return LoadNIFModelCoroutine(normalizedExtractedFilename, false, (loadedModel) => modelObj = loadedModel);
                
                if (modelObj != null)
                {
                    TESNifLibrary.AddModel(bodyPartId, bodyPartId, modelFilename, baseFilenameNoExt, modelObj, combineMeshes: false);
                    modelObj.name = baseFilenameNoExt;
                    onComplete?.Invoke(modelObj);
                    yield break;
                }
            }
            
            UnityEngine.Debug.LogWarning($"PlaceNPC: Failed to load body part model '{modelFilename}' (ID: {bodyPartId})");
            onComplete?.Invoke(null);
        }

        /// <summary>
        /// Places an NPC character in the world at the specified cell reference position
        /// TODO: Implement skinned mesh handling and humanoid rig mapping for Mechanim compatibility
        /// TODO: Account for handedness conversion (Morrowind right-handed to Unity left-handed coordinate system)
        /// Reference: OpenMW source for skinned mesh handling (components/nif/nifloader.cpp, components/scene/skinned.cpp)
        /// </summary>
        private bool PlaceNPC(string npcId, SubRecordCellREFP refp, float scale, GameObject cellParent, int cellGridX, int cellGridY, ref int placedCount, ref int failedCount)
        {
            try
            {
                // Clean NPC ID - remove all null characters and whitespace
                npcId = npcId?.TrimEnd('\0', ' ', '\t', '\r', '\n');
                npcId = npcId?.Replace("\0", "");
                npcId = npcId?.Trim();
                
                // Also remove any embedded null characters
                if (!string.IsNullOrEmpty(npcId))
                {
                    npcId = new string(npcId.Where(c => c != '\0').ToArray()).Trim();
                }

                if (string.IsNullOrEmpty(npcId))
                {
                    UnityEngine.Debug.LogWarning("PlaceNPC: NPC ID is null or empty");
                    failedCount++;
                    return false;
                }

                // Get NPC data from TESCharacterManager (lookup is case-insensitive)
                TESCharacterManager.NPCEntry npcEntry = TESCharacterManager.GetNPC(npcId);
                if (npcEntry == null)
                {
                    UnityEngine.Debug.LogWarning($"PlaceNPC: NPC '{npcId}' not found in TESCharacterManager");
                    failedCount++;
                    return false;
                }

                // NPC_ records reference BODY records for actual model filenames
                // MODL = body part ID (not direct filename)
                // BNAM = head body part ID
                // KNAM = hair body part ID
                // We need to look up each body part to get the actual model filename
                
                // Get body part entries for body, head, and hair
                TESCharacterManager.BodyPartEntry bodyPart = null;
                TESCharacterManager.BodyPartEntry headPart = null;
                TESCharacterManager.BodyPartEntry hairPart = null;
                
                if (!string.IsNullOrEmpty(npcEntry.ModelFilename))
                {
                    // MODL contains a body part ID, not a direct filename
                    // Clean the body part ID to remove null characters before lookup
                    string cleanedBodyPartId = npcEntry.ModelFilename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    cleanedBodyPartId = cleanedBodyPartId.Replace("\0", "");
                    cleanedBodyPartId = new string(cleanedBodyPartId.Where(c => c != '\0').ToArray()).Trim();
                    
                    if (!string.IsNullOrEmpty(cleanedBodyPartId))
                    {
                        bodyPart = TESCharacterManager.GetBodyPart(cleanedBodyPartId);
                        if (bodyPart == null)
                        {
                            UnityEngine.Debug.LogWarning($"PlaceNPC: NPC '{npcId}' references body part '{cleanedBodyPartId}' (original: '{npcEntry.ModelFilename}') which was not found");
                        }
                    }
                }
                
                if (!string.IsNullOrEmpty(npcEntry.HeadModel))
                {
                    // BNAM contains a body part ID for the head
                    // Clean the body part ID to remove null characters before lookup
                    string cleanedHeadPartId = npcEntry.HeadModel.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    cleanedHeadPartId = cleanedHeadPartId.Replace("\0", "");
                    cleanedHeadPartId = new string(cleanedHeadPartId.Where(c => c != '\0').ToArray()).Trim();
                    
                    if (!string.IsNullOrEmpty(cleanedHeadPartId))
                    {
                        headPart = TESCharacterManager.GetBodyPart(cleanedHeadPartId);
                        if (headPart == null)
                        {
                            UnityEngine.Debug.LogWarning($"PlaceNPC: NPC '{npcId}' references head body part '{cleanedHeadPartId}' (original: '{npcEntry.HeadModel}') which was not found");
                        }
                    }
                }
                
                if (!string.IsNullOrEmpty(npcEntry.HairModel))
                {
                    // KNAM contains a body part ID for the hair
                    // Clean the body part ID to remove null characters before lookup
                    string cleanedHairPartId = npcEntry.HairModel.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    cleanedHairPartId = cleanedHairPartId.Replace("\0", "");
                    cleanedHairPartId = new string(cleanedHairPartId.Where(c => c != '\0').ToArray()).Trim();
                    
                    if (!string.IsNullOrEmpty(cleanedHairPartId))
                    {
                        hairPart = TESCharacterManager.GetBodyPart(cleanedHairPartId);
                        if (hairPart == null)
                        {
                            UnityEngine.Debug.LogWarning($"PlaceNPC: NPC '{npcId}' references hair body part '{cleanedHairPartId}' (original: '{npcEntry.HairModel}') which was not found");
                        }
                    }
                }
                
                // Note: We don't require all body parts to exist
                // NPCs can be placed with just body, just head, just hair, or any combination
                // This allows NPCs to be placed even if some body parts fail to load
                // (e.g., due to niflib.net parsing limitations with certain NIF file formats)

                // Convert Morrowind world coordinates to Unity world coordinates
                // Same coordinate conversion as PlaceStaticObject
                float scaledX = refp.x * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                float scaledZ = refp.y * TESGlobals.MORROWIND_TO_STATIC_SCALE; // Use refp.y for Z (North coordinate)
                float scaledY = refp.z * TESGlobals.MORROWIND_TO_STATIC_SCALE; // Use refp.z for Y (Height coordinate)

                Vector3 unityPosition = new Vector3(
                    scaledX,      // X position in world space
                    scaledY,      // Y position (height)
                    scaledZ       // Z position in world space
                );

                // Convert Morrowind rotation to Unity rotation
                // Same rotation conversion as PlaceStaticObject
                Quaternion unityRotation = Quaternion.Euler(
                    -refp.yaw * Mathf.Rad2Deg,    // Yaw -> X
                    -refp.pitch * Mathf.Rad2Deg,  // Pitch -> Y
                    -refp.roll * Mathf.Rad2Deg    // Roll -> Z
                );

                // Create parent GameObject for the NPC (will hold all body parts)
                GameObject npcObj = new GameObject($"{npcEntry.DisplayName ?? npcId} (NPC)");
                npcObj.transform.position = unityPosition;
                npcObj.transform.rotation = unityRotation;
                
                // NPC models should be 128 Morrowind units tall = 1 Unity unit
                // Body part models are already in Morrowind units, so we need to scale them appropriately
                // The scale factor is 1 / 128 = MORROWIND_TO_STATIC_SCALE (0.0078125)
                // However, we also need to account for XSCL if present
                // Base scale: 1 Unity unit / 128 Morrowind units
                Vector3 characterBaseScale = new Vector3(
                    TESGlobals.MORROWIND_TO_CHARACTER_SCALE,
                    TESGlobals.MORROWIND_TO_CHARACTER_SCALE,
                    TESGlobals.MORROWIND_TO_CHARACTER_SCALE
                );
                
                // Apply XSCL scale if present (default is 1.0)
                if (scale < 0)
                {
                    npcObj.transform.localScale = characterBaseScale * Mathf.Abs(scale);
                }
                else
                {
                    npcObj.transform.localScale = characterBaseScale * scale;
                }
                
                // Load and attach body parts
                // Note: Some body parts may fail to load due to niflib.net parsing limitations
                // (e.g., "Invalid object type string length!" errors with certain NIF formats)
                // We continue loading other parts even if some fail
                int loadedPartsCount = 0;
                
                // Load body (main model)
                if (bodyPart != null && !string.IsNullOrEmpty(bodyPart.ModelFilename))
                {
                    UnityEngine.Debug.Log($"PlaceNPC: Loading body model for NPC '{npcId}': body part ID='{bodyPart.BodyPartId}', model filename='{bodyPart.ModelFilename}'");
                    GameObject bodyModel = LoadBodyPartModel(bodyPart.ModelFilename, bodyPart.BodyPartId);
                    if (bodyModel != null)
                    {
                        bodyModel.transform.SetParent(npcObj.transform, false);
                        bodyModel.name = "Body";
                        loadedPartsCount++;
                        UnityEngine.Debug.Log($"PlaceNPC: Successfully loaded body model for NPC '{npcId}'");
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"PlaceNPC: Failed to load body model for NPC '{npcId}' (body part: '{bodyPart.BodyPartId}', model: '{bodyPart.ModelFilename}'). This may be due to niflib.net parsing limitations.");
                    }
                }
                else
                {
                    if (bodyPart == null)
                    {
                        UnityEngine.Debug.LogWarning($"PlaceNPC: NPC '{npcId}' has no body part (MODL was null or lookup failed). MODL value was: '{npcEntry.ModelFilename}'");
                    }
                    else if (string.IsNullOrEmpty(bodyPart.ModelFilename))
                    {
                        UnityEngine.Debug.LogWarning($"PlaceNPC: NPC '{npcId}' body part '{bodyPart.BodyPartId}' has no model filename");
                    }
                }
                
                // Load head
                if (headPart != null && !string.IsNullOrEmpty(headPart.ModelFilename))
                {
                    GameObject headModel = LoadBodyPartModel(headPart.ModelFilename, headPart.BodyPartId);
                    if (headModel != null)
                    {
                        headModel.transform.SetParent(npcObj.transform, false);
                        headModel.name = "Head";
                        loadedPartsCount++;
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"PlaceNPC: Failed to load head model for NPC '{npcId}' (body part: '{headPart.BodyPartId}'). This may be due to niflib.net parsing limitations.");
                    }
                }
                
                // Load hair
                if (hairPart != null && !string.IsNullOrEmpty(hairPart.ModelFilename))
                {
                    GameObject hairModel = LoadBodyPartModel(hairPart.ModelFilename, hairPart.BodyPartId);
                    if (hairModel != null)
                    {
                        hairModel.transform.SetParent(npcObj.transform, false);
                        hairModel.name = "Hair";
                        loadedPartsCount++;
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"PlaceNPC: Failed to load hair model for NPC '{npcId}' (body part: '{hairPart.BodyPartId}'). This may be due to niflib.net parsing limitations.");
                    }
                }
                
                // Check if we loaded at least one body part
                if (loadedPartsCount == 0)
                {
                    UnityEngine.Debug.LogWarning($"PlaceNPC: NPC '{npcId}' has no loadable body parts. All body parts failed to load (likely due to niflib.net parsing limitations).");
                    GameObject.Destroy(npcObj);
                    failedCount++;
                    return false;
                }

                // TODO: Handle skinned meshes and bone mapping
                // Morrowind character models use Bip01 bone hierarchy (similar to 3ds Max Biped)
                // Need to:
                // 1. Detect if model has skinned meshes (NiSkinInstance, NiSkinData, NiSkinPartition)
                // 2. Map Morrowind bones to Unity humanoid rig (Bip01 -> Humanoid bones)
                // 3. Convert right-handed to left-handed coordinate system for bones
                // 4. Set up Animator component with humanoid avatar
                // 5. Configure Mechanim for 3rd person controller compatibility
                // 
                // Reference OpenMW:
                // - components/nif/nifloader.cpp: loadKf() for animation loading
                // - components/scene/skinned.cpp: SkinnedMesh for bone hierarchy
                // - components/scene/node.cpp: Node::update() for transform updates
                //
                // For now, NPCs are placed as static meshes. Skinned mesh support will be added later.

                // Fix reflection issues: negate Z scale and negate Yaw (same as static objects)
                Vector3 currentScale = npcObj.transform.localScale;
                npcObj.transform.localScale = new Vector3(currentScale.x, currentScale.y, -currentScale.z);

                Vector3 euler = npcObj.transform.rotation.eulerAngles;
                float yaw = euler.y;
                if (yaw > 180f) yaw -= 360f;
                npcObj.transform.rotation = Quaternion.Euler(euler.x, -yaw, euler.z);

                // Parent to cell or specified parent
                Transform parentTransform = cellParent != null ? cellParent.transform : null;
                if (parentTransform != null)
                {
                    npcObj.transform.SetParent(parentTransform, worldPositionStays: true);
                }

                // Add NPC component for future scripting/interaction
                // TODO: Create NPC component class to hold NPC data, AI, dialogue, etc.
                // Note: Not setting tag since "NPC" tag doesn't exist by default in Unity
                // Can be added via Tags & Layers settings if needed

                placedCount++;
                return true;
            }
            catch (System.Exception)
            {
                UnityEngine.Debug.LogError($"PlaceNPC: Exception while placing NPC '{npcId}' in cell ({cellGridX}, {cellGridY})");
                failedCount++;
                return false;
            }
        }
        
        /// <summary>
        /// Loads a body part model from cache or BSA, similar to how statics are loaded.
        /// Uses TESLTextureLibrary for textures and BSA extraction if needed.
        /// </summary>
        /// <param name="modelFilename">The model filename from the BODY record</param>
        /// <param name="bodyPartId">The body part ID for caching/lookup</param>
        /// <returns>The loaded GameObject, or null if loading failed</returns>
        private GameObject LoadBodyPartModel(string modelFilename, string bodyPartId)
        {
            if (string.IsNullOrEmpty(modelFilename))
                return null;
            
            // Clean the model filename
            modelFilename = modelFilename.TrimEnd('\0', ' ', '\t', '\r', '\n');
            modelFilename = modelFilename.Replace("\0", "");
            modelFilename = modelFilename.Trim();
            
            // Normalize path separators (replace backslashes with forward slashes for consistency)
            modelFilename = modelFilename.Replace('\\', '/');
            
            // Strip any subdirectory paths from filename (use just the base filename)
            string baseFilename = Path.GetFileName(modelFilename);
            string baseFilenameNoExt = Path.GetFileNameWithoutExtension(baseFilename);
            
            // Normalize to lowercase for case-insensitive lookup (Windows file system is case-insensitive but we want consistency)
            string baseFilenameLower = baseFilename.ToLowerInvariant();
            string baseFilenameNoExtLower = baseFilenameNoExt.ToLowerInvariant();
            
            // Check if we've already loaded this model (mesh instancing)
            // Try both original case and lowercase
            TESNifLibrary.NifEntry cachedEntry = TESNifLibrary.GetModelByBaseFilename(baseFilenameNoExt);
            if (cachedEntry == null && !string.Equals(baseFilenameNoExt, baseFilenameNoExtLower, StringComparison.Ordinal))
            {
                cachedEntry = TESNifLibrary.GetModelByBaseFilename(baseFilenameNoExtLower);
            }
            
            if (cachedEntry != null)
            {
                // Instantiate the existing model
                GameObject instantiatedModel = GameObject.Instantiate(cachedEntry.Model);
                instantiatedModel.name = baseFilenameNoExt;
                return instantiatedModel;
            }
            
            // Try to load from cache first (try original case, then lowercase)
            GameObject modelObj = LoadNIFModel(baseFilename, combineMeshes: false);
            if (modelObj == null && !string.Equals(baseFilename, baseFilenameLower, StringComparison.Ordinal))
            {
                // Try case-insensitive search in cache directory
                string cacheDir = Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", _esm);
                if (Directory.Exists(cacheDir))
                {
                    string[] files = Directory.GetFiles(cacheDir, "*.nif", SearchOption.TopDirectoryOnly);
                    foreach (string file in files)
                    {
                        string fileName = Path.GetFileName(file);
                        string fileNameNoExt = Path.GetFileNameWithoutExtension(fileName);
                        if (string.Equals(fileNameNoExt, baseFilenameNoExtLower, StringComparison.OrdinalIgnoreCase))
                        {
                            // Found case-insensitive match, try loading with the actual filename
                            modelObj = LoadNIFModel(fileName, combineMeshes: false);
                            if (modelObj != null)
                            {
                                baseFilename = fileName; // Update to use the actual filename
                                baseFilenameNoExt = fileNameNoExt;
                                break;
                            }
                        }
                    }
                }
            }
            
            if (modelObj != null)
            {
                // Store in library for future instancing
                TESNifLibrary.AddModel(bodyPartId, bodyPartId, modelFilename, baseFilenameNoExt, modelObj, combineMeshes: false);
                modelObj.name = baseFilenameNoExt;
                return modelObj;
            }
            
            // If not in cache, try to extract from BSA (same as statics)
            // Try both original case and lowercase for BSA lookup
            string extractedFilename = TryExtractFromBSA(baseFilenameNoExt);
            if (string.IsNullOrEmpty(extractedFilename) && !string.Equals(baseFilenameNoExt, baseFilenameNoExtLower, StringComparison.Ordinal))
            {
                extractedFilename = TryExtractFromBSA(baseFilenameNoExtLower);
            }
            
            if (!string.IsNullOrEmpty(extractedFilename))
            {
                // Normalize the extracted filename (remove any path separators, ensure correct case)
                string normalizedExtractedFilename = Path.GetFileName(extractedFilename);
                normalizedExtractedFilename = normalizedExtractedFilename.Replace('\\', '/');
                
                // Try loading again after extraction
                GameObject extractedModel = LoadNIFModel(normalizedExtractedFilename, combineMeshes: false);
                
                // If that failed, try case-insensitive search
                if (extractedModel == null)
                {
                    string cacheDir = Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", _esm);
                    if (Directory.Exists(cacheDir))
                    {
                        string[] files = Directory.GetFiles(cacheDir, "*.nif", SearchOption.TopDirectoryOnly);
                        string extractedLower = normalizedExtractedFilename.ToLowerInvariant();
                        foreach (string file in files)
                        {
                            string fileName = Path.GetFileName(file);
                            if (string.Equals(fileName, normalizedExtractedFilename, StringComparison.OrdinalIgnoreCase))
                            {
                                extractedModel = LoadNIFModel(fileName, combineMeshes: false);
                                if (extractedModel != null)
                                {
                                    normalizedExtractedFilename = fileName;
                                    baseFilename = fileName;
                                    baseFilenameNoExt = Path.GetFileNameWithoutExtension(fileName);
                                    break;
                                }
                            }
                        }
                    }
                }
                
                if (extractedModel != null)
                {
                    // Store in library for future instancing
                    TESNifLibrary.AddModel(bodyPartId, bodyPartId, modelFilename, baseFilenameNoExt, extractedModel, combineMeshes: false);
                    extractedModel.name = baseFilenameNoExt;
                    return extractedModel;
                }
            }
            
            UnityEngine.Debug.LogWarning($"PlaceNPC: Failed to load body part model '{modelFilename}' (ID: {bodyPartId}) from cache or BSA. File may exist but niflib.net cannot parse it.");
            return null;
        }

        /// <summary>
        /// Coroutine version of LoadNIFModel that yields during file I/O
        /// </summary>
        private IEnumerator LoadNIFModelCoroutine(string modelFilename, bool combineMeshes, System.Action<GameObject> onComplete)
        {
            GameObject result = null;
            
            if (!TESGlobals.EnableMultithreadedModelLoading)
            {
                // Synchronous loading (for debugging)
                result = _nifLoader.LoadNIFFromCache(modelFilename, combineMeshes);
                onComplete?.Invoke(result);
                yield break;
            }

            // Check cache first (thread-safe read)
            string baseFilenameNoExt = Path.GetFileNameWithoutExtension(modelFilename);
            TESNifLibrary.NifEntry cachedEntry = TESNifLibrary.GetModelByBaseFilename(baseFilenameNoExt);
            if (cachedEntry != null)
            {
                // Return cached model immediately
                result = GameObject.Instantiate(cachedEntry.Model);
                result.name = cachedEntry.Model.name;
                onComplete?.Invoke(result);
                yield break;
            }

            // Use Task-based async loading for file I/O on background thread
            // Parse and create GameObject on main thread
            byte[] nifData = null;
            System.Exception loadError = null;
            
            // Load file on background thread
            System.Threading.Tasks.Task fileLoadTask = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    string cachePath = FindNIFInCache(_esm, modelFilename);
                    if (string.IsNullOrEmpty(cachePath) || !System.IO.File.Exists(cachePath))
                    {
                        nifData = null;
                        return;
                    }
                    nifData = System.IO.File.ReadAllBytes(cachePath);
                }
                catch (System.Exception ex)
                {
                    loadError = ex;
                    nifData = null;
                }
            });

            // Yield while file I/O happens on background thread
            while (!fileLoadTask.IsCompleted)
            {
                yield return null;
            }

            if (loadError != null)
            {
                UnityEngine.Debug.LogError($"LoadNIFModel: Error loading {modelFilename}: {loadError.Message}");
                onComplete?.Invoke(null);
                yield break;
            }

            if (nifData == null)
            {
                UnityEngine.Debug.LogWarning($"LoadNIFModel: File not found: {modelFilename}");
                onComplete?.Invoke(null);
                yield break;
            }

            // Parse and create GameObject on main thread (must be on main thread)
            try
            {
                string actualFilename = Path.GetFileName(modelFilename);
                result = _nifLoader.LoadNIFFromBytes(nifData, actualFilename, combineMeshes);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"LoadNIFModel: Error parsing {modelFilename}: {ex.Message}");
                result = null;
            }

            onComplete?.Invoke(result);
        }

        /// <summary>
        /// Loads a NIF model, using async loading if multithreading is enabled
        /// When async is enabled, file I/O happens on background thread
        /// </summary>
        private GameObject LoadNIFModel(string modelFilename, bool combineMeshes = false)
        {
            if (!TESGlobals.EnableMultithreadedModelLoading)
            {
                // Synchronous loading (for debugging)
                return _nifLoader.LoadNIFFromCache(modelFilename, combineMeshes);
            }

            // Check cache first (thread-safe read)
            string baseFilenameNoExt = Path.GetFileNameWithoutExtension(modelFilename);
            TESNifLibrary.NifEntry cachedEntry = TESNifLibrary.GetModelByBaseFilename(baseFilenameNoExt);
            if (cachedEntry != null)
            {
                // Return cached model immediately
                GameObject instance = GameObject.Instantiate(cachedEntry.Model);
                instance.name = cachedEntry.Model.name;
                return instance;
            }

            // For coroutine-based loading, we need to use the coroutine version
            // But since this is called from synchronous code, we'll use Task.Result as fallback
            // The coroutine version should be used from coroutine contexts
            try
            {
                // Load file on background thread
                System.Threading.Tasks.Task<byte[]> fileLoadTask = System.Threading.Tasks.Task.Run(() =>
                {
                    string cachePath = FindNIFInCache(_esm, modelFilename);
                    if (string.IsNullOrEmpty(cachePath) || !System.IO.File.Exists(cachePath))
                    {
                        return null;
                    }
                    return System.IO.File.ReadAllBytes(cachePath);
                });

                // Wait for file load (this blocks but file I/O is on background thread)
                byte[] nifData = fileLoadTask.Result;
                if (nifData == null)
                {
                    UnityEngine.Debug.LogWarning($"LoadNIFModel: File not found: {modelFilename}");
                    return null;
                }

                // Parse and create GameObject on main thread (must be on main thread)
                string actualFilename = Path.GetFileName(modelFilename);
                return _nifLoader.LoadNIFFromBytes(nifData, actualFilename, combineMeshes);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"LoadNIFModel: Error loading {modelFilename}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Finds NIF file in cache directory with case-insensitive search
        /// </summary>
        private string FindNIFInCache(string esm, string modelFilename)
        {
            // Normalize filename
            string normalizedFilename = modelFilename?.Replace('\\', '/');
            normalizedFilename = Path.GetFileName(normalizedFilename);
            
            string cachePath = Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", esm, normalizedFilename);
            
            if (System.IO.File.Exists(cachePath))
                return cachePath;

            // Try original filename
            string originalPath = Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", esm, modelFilename);
            if (System.IO.File.Exists(originalPath))
                return originalPath;

            // Try case-insensitive search
            string cacheDir = Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", esm);
            if (System.IO.Directory.Exists(cacheDir))
            {
                string[] files = System.IO.Directory.GetFiles(cacheDir, "*.nif", System.IO.SearchOption.TopDirectoryOnly);
                string searchFilename = Path.GetFileName(modelFilename);
                foreach (string file in files)
                {
                    string fileName = Path.GetFileName(file);
                    if (string.Equals(fileName, searchFilename, StringComparison.OrdinalIgnoreCase))
                    {
                        return file;
                    }
                }
            }

            return null;
        }
    }
}

