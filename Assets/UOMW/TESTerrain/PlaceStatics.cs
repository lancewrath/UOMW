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
        
        /// <summary>
        /// Cache of model IDs that have failed to load (to avoid repeated lookups)
        /// Static so it's shared across all PlaceStatics instances
        /// </summary>
        private static HashSet<string> _failedModelLookups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
        public void PlaceLargeStructures(Record[] records, Transform parent = null)
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
            PlaceObjects(records, parent, ObjectType.LargeStructures);
        }

        /// <summary>
        /// Places trees from CELL records as Unity terrain trees
        /// </summary>
        public void PlaceTrees(Record[] records, Terrain terrain = null)
        {
            // Place trees as static meshes (not terrain tree instances)
            // Trees will be placed using the same logic as statics, but with SpeedTree shader
            PlaceObjects(records, null, ObjectType.Trees, terrain);
        }

        /// <summary>
        /// Places grass from CELL records as Unity terrain details
        /// </summary>
        public void PlaceGrass(Record[] records, Terrain terrain = null)
        {
            PlaceObjects(records, null, ObjectType.Grass, terrain);
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
        public void PlaceCellStatics(RecordCell cellRecord, Record[] allRecords, Transform parent, Terrain terrain)
        {
            // For synchronous calls, we need a MonoBehaviour to run the coroutine
            // Create a temporary helper object
            GameObject helperObj = new GameObject("PlaceStaticsHelper");
            PlaceStaticsCoroutineHelper helper = helperObj.AddComponent<PlaceStaticsCoroutineHelper>();
            helper.StartCoroutine(PlaceCellStaticsCoroutine(cellRecord, allRecords, parent, terrain, () =>
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
        public IEnumerator PlaceCellStaticsCoroutine(RecordCell cellRecord, Record[] allRecords, Transform parent, Terrain terrain, System.Action onComplete = null)
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

            // Get cell GameObject from static CellManager
            GameObject cellParent = CellManager.GetCell(cellGridX, cellGridY);

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
                                        // This is an NPC but ANAM wasn't detected - place as NPC anyway
                                        // UnityEngine.Debug.LogWarning($"PlaceCellStatics: NPC '{objectIdClean}' detected by Object ID but ANAM was missing. Placing as NPC anyway."); // Commented out for performance
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
                        // UnityEngine.Debug.Log($"PlaceCellStatics: Found ANAM record with NPC ID: '{currentANAM.name}' (cleaned: '{cleanedANAM}') (Object ID: '{currentObjectID?.objectId ?? "null"}')"); // Commented out for performance
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
        private void PlaceObjects(Record[] records, Transform parent = null, ObjectType objectType = ObjectType.LargeStructures, Terrain terrain = null)
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

                        // Get the cell GameObject from static CellManager
                        cellParent = CellManager.GetCell(cellGridX, cellGridY);
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
                if (cachedEntry != null && cachedEntry.Model != null)
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
                    
                    // XSCL scale multiplies the base GameObject scale (MORROWIND_TO_STATIC_SCALE = 0.0078125), same as regular statics
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
                    
                    // DISABLED: Coordinate system conversion should now handle this at NIF loading level
                    // If models appear mirrored or wrong orientation, re-enable these fixes:
                    // Apply reflection fix: negate Z scale and negate Yaw (same as statics)
                    // Vector3 instanceCurrentScale = instanceObj.transform.localScale;
                    // instanceObj.transform.localScale = new Vector3(instanceCurrentScale.x, instanceCurrentScale.y, -instanceCurrentScale.z);
                    // 
                    // Negate Yaw (Y rotation)
                    // Vector3 instanceEuler = instanceObj.transform.rotation.eulerAngles;
                    // float instanceYaw = instanceEuler.y;
                    // if (instanceYaw > 180f) instanceYaw -= 360f;
                    // instanceObj.transform.rotation = Quaternion.Euler(instanceEuler.x, -instanceYaw, instanceEuler.z);
                    
                    // Add LOD component
                    AddLODToObject(instanceObj);
                    
                    // Parent to cell or specified parent (same as statics)
                    if (parent != null)
                    {
                        instanceObj.transform.SetParent(parent, worldPositionStays: true);
                    }
                    
                    return true;
                }
                
                // Load the NIF model (keep meshes separate like regular statics)
                GameObject treeModel = LoadNIFModel(baseFilename, combineMeshes: false, isTreeOrGrass: true);
                if (treeModel == null)
                {
                    //UnityEngine.Debug.LogWarning($"Failed to load tree model: {baseFilename}");
                    return false;
                }
                
                // Store the loaded model in global library for future instancing
                string staticId = objectId?.objectId?.TrimEnd('\0');
                string staticName = staticId; // Use ID as name if no separate name available
                TESNifLibrary.AddModel(staticId, staticName, baseFilename, baseFilenameNoExt, treeModel, combineMeshes: false);
                
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
                
                // XSCL scale multiplies the base GameObject scale (MORROWIND_TO_STATIC_SCALE = 0.0078125), same as regular statics
                Vector3 treeBaseScale = new Vector3(TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE);
                // Apply XSCL scale (handle negative scales like statics)
                if (scale < 0)
                {
                    // Negative scale = mirror object (use absolute value)
                    treeModel.transform.localScale = treeBaseScale * Mathf.Abs(scale);
                }
                else
                {
                    // Positive scale = normal scaling
                    treeModel.transform.localScale = treeBaseScale * scale;
                }
                
                // Set name
                if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                {
                    treeModel.name = objectId.objectId.TrimEnd('\0');
                }
                
                // DISABLED: Coordinate system conversion should now handle this at NIF loading level
                // If models appear mirrored or wrong orientation, re-enable these fixes:
                // Apply reflection fix: negate Z scale and negate Yaw (same as statics)
                // Vector3 currentScale = treeModel.transform.localScale;
                // treeModel.transform.localScale = new Vector3(currentScale.x, currentScale.y, -currentScale.z);
                // 
                // Negate Yaw (Y rotation)
                // Vector3 euler = treeModel.transform.rotation.eulerAngles;
                // float yaw = euler.y;
                // if (yaw > 180f) yaw -= 360f;
                // treeModel.transform.rotation = Quaternion.Euler(euler.x, -yaw, euler.z);
                
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
                if (cachedEntry != null && cachedEntry.Model != null)
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
                    
                    // XSCL scale multiplies the base GameObject scale (MORROWIND_TO_STATIC_SCALE = 0.0078125), same as regular statics
                    Vector3 instanceBaseScale = new Vector3(TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE);
                    instanceObj.transform.localScale = instanceBaseScale * (scale < 0 ? Mathf.Abs(scale) : scale);
                    
                    if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                        instanceObj.name = objectId.objectId.TrimEnd('\0');
                    
                    // DISABLED: Coordinate system conversion should now handle this at NIF loading level
                    // Vector3 currentScaleLocal = instanceObj.transform.localScale;
                    // instanceObj.transform.localScale = new Vector3(currentScaleLocal.x, currentScaleLocal.y, -currentScaleLocal.z);
                    // 
                    // Vector3 euler = instanceObj.transform.rotation.eulerAngles;
                    // float yaw = euler.y;
                    // if (yaw > 180f) yaw -= 360f;
                    // instanceObj.transform.rotation = Quaternion.Euler(euler.x, -yaw, euler.z);
                    
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
            yield return LoadNIFModelCoroutine(baseFilename, false, (loadedModel) => treeModel = loadedModel, isTreeOrGrass: true);
            
            if (treeModel == null)
            {
                onComplete?.Invoke(false);
                yield break;
            }
            
            try
            {
                string staticId = objectId?.objectId?.TrimEnd('\0');
                string staticName = staticId;
                TESNifLibrary.AddModel(staticId, staticName, baseFilename, baseFilenameNoExt, treeModel, combineMeshes: false);
                
                float scaledX = refp.x * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                float scaledZ = refp.y * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                float scaledY = refp.z * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                
                Vector3 unityPosition = new Vector3(scaledX, scaledY, scaledZ);
                Quaternion unityRotation = Quaternion.Euler(-refp.yaw * Mathf.Rad2Deg, -refp.pitch * Mathf.Rad2Deg, -refp.roll * Mathf.Rad2Deg);
                
                treeModel.transform.position = unityPosition;
                treeModel.transform.rotation = unityRotation;
                
                // XSCL scale multiplies the base GameObject scale (MORROWIND_TO_STATIC_SCALE = 0.0078125), same as regular statics
                Vector3 instanceBaseScale = new Vector3(TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE);
                treeModel.transform.localScale = instanceBaseScale * (scale < 0 ? Mathf.Abs(scale) : scale);
                
                if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                    treeModel.name = objectId.objectId.TrimEnd('\0');
                
                // DISABLED: Coordinate system conversion should now handle this at NIF loading level
                // Vector3 currentScaleLocal2 = treeModel.transform.localScale;
                // treeModel.transform.localScale = new Vector3(currentScaleLocal2.x, currentScaleLocal2.y, -currentScaleLocal2.z);
                // 
                // Vector3 euler2 = treeModel.transform.rotation.eulerAngles;
                // float yaw2 = euler2.y;
                // if (yaw2 > 180f) yaw2 -= 360f;
                // treeModel.transform.rotation = Quaternion.Euler(euler2.x, -yaw2, euler2.z);
                
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
                if (cachedEntry != null && cachedEntry.Model != null)
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
                    
                    // XSCL scale multiplies the base GameObject scale (MORROWIND_TO_STATIC_SCALE = 0.0078125), same as regular statics
                    Vector3 instanceBaseScale = new Vector3(TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE);
                    instanceObj.transform.localScale = instanceBaseScale * (scale < 0 ? Mathf.Abs(scale) : scale);
                    
                    if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                        instanceObj.name = objectId.objectId.TrimEnd('\0');
                    
                    // DISABLED: Coordinate system conversion should now handle this at NIF loading level
                    // Vector3 currentScaleLocal = instanceObj.transform.localScale;
                    // instanceObj.transform.localScale = new Vector3(currentScaleLocal.x, currentScaleLocal.y, -currentScaleLocal.z);
                    // 
                    // Vector3 euler = instanceObj.transform.rotation.eulerAngles;
                    // float yaw = euler.y;
                    // if (yaw > 180f) yaw -= 360f;
                    // instanceObj.transform.rotation = Quaternion.Euler(euler.x, -yaw, euler.z);
                    
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
            yield return LoadNIFModelCoroutine(baseFilename, false, (loadedModel) => grassModel = loadedModel, isTreeOrGrass: true);
            
            if (grassModel == null)
            {
                onComplete?.Invoke(false);
                yield break;
            }
            
            try
            {
                string staticId = objectId?.objectId?.TrimEnd('\0');
                string staticName = staticId;
                TESNifLibrary.AddModel(staticId, staticName, baseFilename, baseFilenameNoExt, grassModel, combineMeshes: false);
                
                float scaledX = refp.x * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                float scaledZ = refp.y * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                float scaledY = refp.z * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                
                Vector3 unityPosition = new Vector3(scaledX, scaledY, scaledZ);
                Quaternion unityRotation = Quaternion.Euler(-refp.yaw * Mathf.Rad2Deg, -refp.pitch * Mathf.Rad2Deg, -refp.roll * Mathf.Rad2Deg);
                
                grassModel.transform.position = unityPosition;
                grassModel.transform.rotation = unityRotation;
                
                // XSCL scale multiplies the base GameObject scale (MORROWIND_TO_STATIC_SCALE = 0.0078125), same as regular statics
                Vector3 grassBaseScale = new Vector3(TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE);
                grassModel.transform.localScale = grassBaseScale * (scale < 0 ? Mathf.Abs(scale) : scale);
                
                if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                    grassModel.name = objectId.objectId.TrimEnd('\0');
                
                // DISABLED: Coordinate system conversion should now handle this at NIF loading level
                // Vector3 currentScaleLocal2 = grassModel.transform.localScale;
                // grassModel.transform.localScale = new Vector3(currentScaleLocal2.x, currentScaleLocal2.y, -currentScaleLocal2.z);
                
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
        private IEnumerator PlaceStaticObjectCoroutine(string modelFilename, SubRecordCellREFP refp, float scale, SubRecordCellObjectID objectId, Transform parent, int cellGridX, int cellGridY, Record[] allRecords, TESLightManager.LightEntry lightEntry, System.Action<bool> onComplete)
        {
            bool result = false;
            string baseFilename = Path.GetFileName(modelFilename);
            string baseFilenameNoExt = Path.GetFileNameWithoutExtension(baseFilename);
            
            TESNifLibrary.NifEntry cachedEntry = TESNifLibrary.GetModelByBaseFilename(baseFilenameNoExt);
            if (cachedEntry != null && cachedEntry.Model != null)
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
                    
                    // Check if this is a light and attach TESLight component
                    if (lightEntry != null)
                    {
                        AttachLightComponent(instanceObj, lightEntry);
                    }
                    else if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                    {
                        // Fallback: try to find light by ID if not passed in
                        string lightId = objectId.objectId.TrimEnd('\0', ' ', '\t', '\r', '\n');
                        lightId = lightId.Replace("\0", "");
                        lightId = new string(lightId.Where(c => c != '\0').ToArray()).Trim();
                        AttachLightComponentIfNeeded(instanceObj, lightId);
                    }
                    
                    if (parent != null)
                        instanceObj.transform.SetParent(parent, worldPositionStays: true);
                    
                    // DISABLED: Coordinate system conversion should now handle this at NIF loading level
                    // Vector3 currentScale = instanceObj.transform.localScale;
                    // instanceObj.transform.localScale = new Vector3(currentScale.x, currentScale.y, -currentScale.z);
                    
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
            // Cannot use try-catch with yield return, so we handle errors after the yield
            yield return LoadNIFModelCoroutine(baseFilename, false, (loadedModel) => modelObj = loadedModel);
            
            if (modelObj == null)
            {
                // Model failed to load - log warning but continue with other statics
                UnityEngine.Debug.LogWarning($"PlaceStaticObjectCoroutine: Failed to load model '{baseFilename}' for object '{objectId?.objectId}' - skipping this static");
                result = false;
                onComplete?.Invoke(result);
                yield break;
            }
            
            try
            {
                string staticId = objectId?.objectId?.TrimEnd('\0');
                string staticName = staticId;
                
                // IMPORTANT: Store the model in the library BEFORE applying any instance-specific transforms
                // The model from NIFLoader should be in a clean state (no transforms applied)
                // We check if it's already in the library first to avoid storing duplicates
                TESNifLibrary.NifEntry existingEntry = TESNifLibrary.GetModelByBaseFilename(baseFilenameNoExt);
                if (existingEntry == null)
                {
                    // Store the model in library (this is the first time loading this model)
                    // The model should be in a clean state from NIFLoader
                    TESNifLibrary.AddModel(staticId, staticName, modelFilename, baseFilenameNoExt, modelObj, combineMeshes: false);
                }
                
                float scaledX2 = refp.x * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                float scaledZ2 = refp.y * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                float scaledY2 = refp.z * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                
                Vector3 unityPosition2 = new Vector3(scaledX2, scaledY2, scaledZ2);
                Quaternion unityRotation2 = Quaternion.Euler(-refp.yaw * Mathf.Rad2Deg, -refp.pitch * Mathf.Rad2Deg, -refp.roll * Mathf.Rad2Deg);
                
                modelObj.transform.position = unityPosition2;
                modelObj.transform.rotation = unityRotation2;
                
                // XSCL scale multiplies the base GameObject scale (MORROWIND_TO_STATIC_SCALE = 0.0078125), not replaces it
                // Use MORROWIND_TO_STATIC_SCALE directly for consistency with cached models
                Vector3 instanceBaseScale = new Vector3(TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE);
                modelObj.transform.localScale = instanceBaseScale * (scale < 0 ? Mathf.Abs(scale) : scale);
                
                if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                    modelObj.name = objectId.objectId.TrimEnd('\0');
                else
                    modelObj.name = Path.GetFileNameWithoutExtension(modelFilename);
                
                AddLODToObject(modelObj);
                
                // Check if this is a light and attach TESLight component
                if (lightEntry != null)
                {
                    AttachLightComponent(modelObj, lightEntry);
                }
                else if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                {
                    // Fallback: try to find light by ID if not passed in
                    string lightId = objectId.objectId.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    lightId = lightId.Replace("\0", "");
                    lightId = new string(lightId.Where(c => c != '\0').ToArray()).Trim();
                    AttachLightComponentIfNeeded(modelObj, lightId);
                }
                
                if (parent != null)
                    modelObj.transform.SetParent(parent, worldPositionStays: true);
                
                // DISABLED: Coordinate system conversion should now handle this at NIF loading level
                // Vector3 modelCurrentScale = modelObj.transform.localScale;
                // modelObj.transform.localScale = new Vector3(modelCurrentScale.x, modelCurrentScale.y, -modelCurrentScale.z);
                
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
                if (cachedEntry != null && cachedEntry.Model != null)
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
                    
                    // XSCL scale multiplies the base GameObject scale (MORROWIND_TO_STATIC_SCALE = 0.0078125), same as regular statics
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
                    
                    // DISABLED: Coordinate system conversion should now handle this at NIF loading level
                    // Apply reflection fix: negate Z scale and negate Yaw (same as statics)
                    // Vector3 instanceCurrentScale = instanceObj.transform.localScale;
                    // instanceObj.transform.localScale = new Vector3(instanceCurrentScale.x, instanceCurrentScale.y, -instanceCurrentScale.z);
                    // 
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
                
                // Load the NIF model (keep meshes separate like regular statics)
                GameObject grassModel = LoadNIFModel(baseFilename, combineMeshes: false, isTreeOrGrass: true);
                if (grassModel == null)
                {
                    //UnityEngine.Debug.LogWarning($"Failed to load grass model: {baseFilename}");
                    return false;
                }
                
                // Store the loaded model in global library for future instancing
                string staticId = objectId?.objectId?.TrimEnd('\0');
                string staticName = staticId; // Use ID as name if no separate name available
                TESNifLibrary.AddModel(staticId, staticName, baseFilename, baseFilenameNoExt, grassModel, combineMeshes: false);
                
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
                
                // XSCL scale multiplies the base GameObject scale (MORROWIND_TO_STATIC_SCALE = 0.0078125), same as regular statics
                Vector3 grassBaseScale = new Vector3(TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE);
                // Apply XSCL scale (handle negative scales like statics)
                if (scale < 0)
                {
                    // Negative scale = mirror object (use absolute value)
                    grassModel.transform.localScale = grassBaseScale * Mathf.Abs(scale);
                }
                else
                {
                    // Positive scale = normal scaling
                    grassModel.transform.localScale = grassBaseScale * scale;
                }
                
                // Set name
                if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                {
                    grassModel.name = objectId.objectId.TrimEnd('\0');
                }
                
                // DISABLED: Coordinate system conversion should now handle this at NIF loading level
                // If models appear mirrored or wrong orientation, re-enable these fixes:
                // Apply reflection fix: negate Z scale and negate Yaw (same as statics)
                // Vector3 currentScale = grassModel.transform.localScale;
                // grassModel.transform.localScale = new Vector3(currentScale.x, currentScale.y, -currentScale.z);
                // 
                // Negate Yaw (Y rotation)
                // Vector3 euler = grassModel.transform.rotation.eulerAngles;
                // float yaw = euler.y;
                // if (yaw > 180f) yaw -= 360f;
                // grassModel.transform.rotation = Quaternion.Euler(euler.x, -yaw, euler.z);
                
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

            // Clean the model ID name (must match cleaning done in library managers)
            // Use the exact same pattern as TESContainerLibrary and TESCharacterManager
            string modelId = objectId.objectId;
            if (modelId != null)
            {
                modelId = modelId.TrimEnd('\0', ' ', '\t', '\r', '\n');
                modelId = modelId.Replace("\0", "");
                modelId = new string(modelId.Where(c => c != '\0').ToArray()).Trim();
            }
            
            if (string.IsNullOrEmpty(modelId))
            {
                UnityEngine.Debug.LogWarning("PlaceReference: Model ID was empty after cleaning");
                switch (objectType)
                {
                    case ObjectType.Trees: counts.FailedTrees++; break;
                    case ObjectType.Grass: counts.FailedGrass++; break;
                    case ObjectType.LargeStructures: counts.FailedStructures++; break;
                }
                yield break;
            }
            
            // Check if this model ID has already failed to load (skip repeated lookups)
            if (_failedModelLookups.Contains(modelId))
            {
                switch (objectType)
                {
                    case ObjectType.Trees: counts.FailedTrees++; break;
                    case ObjectType.Grass: counts.FailedGrass++; break;
                    case ObjectType.LargeStructures: counts.FailedStructures++; break;
                }
                yield break;
            }

            // Check if this might be an NPC (quick check - if it's not in STAT records)
            string cleanedModelId = modelId; // Already cleaned above - this is the normalized ID
            bool mightBeNPC = !TESStaticLibrary.HasStatic(modelId) && !TESStaticLibrary.HasStatic(cleanedModelId) && 
                              !statRecordsByName.ContainsKey(modelId) && !statRecordsByName.ContainsKey(cleanedModelId);
            
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

            // First, try to look up STAT record using TESStaticLibrary (pre-registered during ESM parsing)
            TESStaticLibrary.StaticEntry staticEntry = TESStaticLibrary.GetStatic(modelId);
            if (staticEntry == null && !string.Equals(modelId, cleanedModelId, StringComparison.OrdinalIgnoreCase))
            {
                staticEntry = TESStaticLibrary.GetStatic(cleanedModelId);
            }
            
            if (staticEntry != null && !string.IsNullOrEmpty(staticEntry.ModelFilename))
            {
                // Get model filename from pre-registered static entry
                modelFilename = staticEntry.ModelFilename;
                
                // Clean the model filename
                modelFilename = modelFilename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                modelFilename = modelFilename.Replace("\0", "");
                modelFilename = modelFilename.Trim();
                
                // Normalize path separators (replace backslashes with forward slashes)
                modelFilename = modelFilename.Replace('\\', '/');
                
                // Extract just the filename (strip any subdirectory paths)
                modelFilename = Path.GetFileName(modelFilename);
            }
            
            // Fallback: If TESStaticLibrary lookup failed, try the old method (for backward compatibility)
            if (string.IsNullOrEmpty(modelFilename) && statRecordsByName.ContainsKey(modelId))
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
                    // Clean the model filename
                    modelFilename = modelFilename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    modelFilename = modelFilename.Replace("\0", "");
                    modelFilename = modelFilename.Trim();
                    
                    // Normalize path separators (replace backslashes with forward slashes)
                    modelFilename = modelFilename.Replace('\\', '/');
                    
                    // Extract just the filename (strip any subdirectory paths)
                    modelFilename = Path.GetFileName(modelFilename);
                }
            }
            
            // If STAT record lookup failed, try to look up CONT (container) record
            if (string.IsNullOrEmpty(modelFilename))
            {
                TESContainerLibrary.ContainerEntry containerEntry = TESContainerLibrary.GetContainer(modelId);
                if (containerEntry == null && !string.Equals(modelId, cleanedModelId, StringComparison.OrdinalIgnoreCase))
                {
                    containerEntry = TESContainerLibrary.GetContainer(cleanedModelId);
                }
                
                if (containerEntry != null && !string.IsNullOrEmpty(containerEntry.ModelFilename))
                {
                    // Clean the model filename from container
                    modelFilename = containerEntry.ModelFilename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    modelFilename = modelFilename.Replace("\0", "");
                    modelFilename = modelFilename.Trim();
                    
                    // Normalize path separators (replace backslashes with forward slashes)
                    modelFilename = modelFilename.Replace('\\', '/');
                    
                    // Extract just the filename (strip any subdirectory paths)
                    modelFilename = Path.GetFileName(modelFilename);
                    
                    // Normalize entire filename to lowercase for consistency with cache files
                    // This ensures "Contain_crate_02.NIF" becomes "contain_crate_02.nif" to match cache
                    // Cache files are typically stored in lowercase
                    modelFilename = modelFilename.ToLowerInvariant();
                }
            }
            
            // If still not found, try to look up DOOR record
            if (string.IsNullOrEmpty(modelFilename))
            {
                TESDoorManager.DoorEntry doorEntry = TESDoorManager.GetDoor(modelId);
                if (doorEntry == null && !string.Equals(modelId, cleanedModelId, StringComparison.OrdinalIgnoreCase))
                {
                    doorEntry = TESDoorManager.GetDoor(cleanedModelId);
                }
                
                if (doorEntry != null && !string.IsNullOrEmpty(doorEntry.ModelFilename))
                {
                    // Clean the model filename from door
                    modelFilename = doorEntry.ModelFilename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    modelFilename = modelFilename.Replace("\0", "");
                    modelFilename = modelFilename.Trim();
                    
                    // Normalize path separators (replace backslashes with forward slashes)
                    modelFilename = modelFilename.Replace('\\', '/');
                    
                    // Extract just the filename (strip any subdirectory paths)
                    modelFilename = Path.GetFileName(modelFilename);
                    
                    // Normalize entire filename to lowercase for consistency with cache files
                    modelFilename = modelFilename.ToLowerInvariant();
                }
            }
            
            // If still not found, try to look up ACTI (activator) record
            if (string.IsNullOrEmpty(modelFilename))
            {
                TESActivatorLibrary.ActivatorEntry activatorEntry = TESActivatorLibrary.GetActivator(modelId);
                if (activatorEntry == null && !string.Equals(modelId, cleanedModelId, StringComparison.OrdinalIgnoreCase))
                {
                    activatorEntry = TESActivatorLibrary.GetActivator(cleanedModelId);
                }
                
                if (activatorEntry != null && !string.IsNullOrEmpty(activatorEntry.ModelFilename))
                {
                    // Clean the model filename from activator
                    modelFilename = activatorEntry.ModelFilename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    modelFilename = modelFilename.Replace("\0", "");
                    modelFilename = modelFilename.Trim();
                    
                    // Normalize path separators (replace backslashes with forward slashes)
                    modelFilename = modelFilename.Replace('\\', '/');
                    
                    // Extract just the filename (strip any subdirectory paths)
                    modelFilename = Path.GetFileName(modelFilename);
                    
                    // Normalize entire filename to lowercase for consistency with cache files
                    modelFilename = modelFilename.ToLowerInvariant();
                }
            }
            
            // Always check for LIGH (light) record - even if STAT/CONT/DOOR/ACTI was found, the object ID might still be a light
            // This ensures lights get the TESLight component attached even if they have a STAT record
            TESLightManager.LightEntry foundLightEntry = null; // Store for later component attachment
            // Use the cleaned modelId (which should match how it's stored in TESLightManager)
            TESLightManager.LightEntry lightEntry = TESLightManager.GetLight(modelId);
            
            // If not found, try with the original objectId.objectId (before cleaning)
            // GetLight will normalize it, so this should work if there was a cleaning mismatch
            if (lightEntry == null && objectId != null && !string.IsNullOrEmpty(objectId.objectId))
            {
                string originalId = objectId.objectId;
                lightEntry = TESLightManager.GetLight(originalId);
                if (lightEntry != null)
                {
                    UnityEngine.Debug.LogWarning($"[PlaceStatics] Found light using original ID '{originalId}' (cleaned was '{modelId}')");
                }
            }
            
            if (lightEntry != null)
            {
                foundLightEntry = lightEntry; // Store for component attachment (even if we use STAT model)
                
                // Only use light's modelFilename if we don't already have one from STAT/CONT
                if (string.IsNullOrEmpty(modelFilename) && !string.IsNullOrEmpty(lightEntry.ModelFilename))
                {
                    // UnityEngine.Debug.LogWarning($"[PlaceStatics] Found light record '{modelId}', model: '{lightEntry.ModelFilename}'"); // Commented out for performance
                    // Clean the model filename from light (same pattern as containers)
                    modelFilename = lightEntry.ModelFilename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    modelFilename = modelFilename.Replace("\0", "");
                    modelFilename = modelFilename.Trim();
                    
                    // Normalize path separators (replace backslashes with forward slashes)
                    modelFilename = modelFilename.Replace('\\', '/');
                    
                    // Extract just the filename (strip any subdirectory paths)
                    modelFilename = Path.GetFileName(modelFilename);
                    
                    // Normalize entire filename to lowercase for consistency with cache files
                    // This ensures "Light_de_streetlight_01.NIF" becomes "light_de_streetlight_01.nif" to match cache
                    // Cache files are typically stored in lowercase
                    modelFilename = modelFilename.ToLowerInvariant();
                    
                    // Note: The NIFLoader will check TESNifLibrary cache first, then file system/BSA
                    // and cache the result in TESNifLibrary for reuse, just like containers and NPCs
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
                // Mark this model ID as failed to avoid repeated lookups
                _failedModelLookups.Add(modelId);
                
                // Only log the first time we encounter this missing model (to reduce spam)
                if (_failedModelLookups.Count <= 100 || _failedModelLookups.Count % 50 == 0)
                {
                    UnityEngine.Debug.LogWarning($"Could not find model for ID '{modelId}' (checked STAT records, CONT records, DOOR records, ACTI records, LIGH records, and cache directory). This will be cached to avoid repeated lookups.");
                }
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
            // IMPORTANT: If this is a light, always place it as LargeStructures to ensure light component is attached
            bool success = false;
            if (foundLightEntry != null)
            {
                // This is a light - always place as LargeStructures regardless of objectType
                Transform refParent = cellParent != null ? cellParent.transform : parent;
                yield return PlaceStaticObjectCoroutine(modelFilename, refp, scale, objectId, refParent, cellGridX, cellGridY, allRecords, foundLightEntry, (result) => success = result);
            }
            else if (objectType == ObjectType.Trees)
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
                yield return PlaceStaticObjectCoroutine(modelFilename, refp, scale, objectId, refParent, cellGridX, cellGridY, allRecords, foundLightEntry, (result) => success = result);
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
            
            if (string.IsNullOrEmpty(modelId))
            {
                failedCount++;
                return;
            }
            
            // Check if this model ID has already failed to load (skip repeated lookups)
            if (_failedModelLookups.Contains(modelId))
            {
                failedCount++;
                return;
            }

            // Check if this might be an NPC (quick check - if it's not in STAT records)
            // This is a performance optimization to avoid checking NPCs for every static
            // Also clean the modelId to remove null characters before checking
            string cleanedModelId = new string(modelId.Where(c => c != '\0').ToArray()).Trim();
            bool mightBeNPC = !TESStaticLibrary.HasStatic(modelId) && !TESStaticLibrary.HasStatic(cleanedModelId) && 
                              !statRecordsByName.ContainsKey(modelId) && !statRecordsByName.ContainsKey(cleanedModelId);
            
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

            // First, try to look up STAT record using TESStaticLibrary (pre-registered during ESM parsing)
            TESStaticLibrary.StaticEntry staticEntry = TESStaticLibrary.GetStatic(modelId);
            if (staticEntry == null && !string.Equals(modelId, cleanedModelId, StringComparison.OrdinalIgnoreCase))
            {
                staticEntry = TESStaticLibrary.GetStatic(cleanedModelId);
            }
            
            if (staticEntry != null && !string.IsNullOrEmpty(staticEntry.ModelFilename))
            {
                // Get model filename from pre-registered static entry
                modelFilename = staticEntry.ModelFilename;
                
                // Clean the model filename
                modelFilename = modelFilename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                modelFilename = modelFilename.Replace("\0", "");
                modelFilename = modelFilename.Trim();
                
                // Normalize path separators (replace backslashes with forward slashes)
                modelFilename = modelFilename.Replace('\\', '/');
                
                // Extract just the filename (strip any subdirectory paths)
                modelFilename = Path.GetFileName(modelFilename);
            }
            
            // Fallback: If TESStaticLibrary lookup failed, try the old method (for backward compatibility)
            if (string.IsNullOrEmpty(modelFilename) && statRecordsByName.ContainsKey(modelId))
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
                    
                    // Normalize path separators (replace backslashes with forward slashes)
                    modelFilename = modelFilename.Replace('\\', '/');
                    
                    // Extract just the filename (strip any subdirectory paths)
                    modelFilename = Path.GetFileName(modelFilename);
                }
            }
            
            // If STAT record lookup failed, try to look up CONT (container) record
            if (string.IsNullOrEmpty(modelFilename))
            {
                TESContainerLibrary.ContainerEntry containerEntry = TESContainerLibrary.GetContainer(modelId);
                if (containerEntry == null && !string.Equals(modelId, cleanedModelId, StringComparison.OrdinalIgnoreCase))
                {
                    containerEntry = TESContainerLibrary.GetContainer(cleanedModelId);
                }
                
                if (containerEntry != null && !string.IsNullOrEmpty(containerEntry.ModelFilename))
                {
                    // Clean the model filename from container
                    modelFilename = containerEntry.ModelFilename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    modelFilename = modelFilename.Replace("\0", "");
                    modelFilename = modelFilename.Trim();
                    
                    // Normalize path separators (replace backslashes with forward slashes)
                    modelFilename = modelFilename.Replace('\\', '/');
                    
                    // Extract just the filename (strip any subdirectory paths)
                    modelFilename = Path.GetFileName(modelFilename);
                    
                    // Normalize entire filename to lowercase for consistency with cache files
                    // This ensures "Contain_crate_02.NIF" becomes "contain_crate_02.nif" to match cache
                    // Cache files are typically stored in lowercase
                    modelFilename = modelFilename.ToLowerInvariant();
                }
            }
            
            // If still not found, try to look up DOOR record
            if (string.IsNullOrEmpty(modelFilename))
            {
                TESDoorManager.DoorEntry doorEntry = TESDoorManager.GetDoor(modelId);
                if (doorEntry == null && !string.Equals(modelId, cleanedModelId, StringComparison.OrdinalIgnoreCase))
                {
                    doorEntry = TESDoorManager.GetDoor(cleanedModelId);
                }
                
                if (doorEntry != null && !string.IsNullOrEmpty(doorEntry.ModelFilename))
                {
                    // Clean the model filename from door
                    modelFilename = doorEntry.ModelFilename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    modelFilename = modelFilename.Replace("\0", "");
                    modelFilename = modelFilename.Trim();
                    
                    // Normalize path separators (replace backslashes with forward slashes)
                    modelFilename = modelFilename.Replace('\\', '/');
                    
                    // Extract just the filename (strip any subdirectory paths)
                    modelFilename = Path.GetFileName(modelFilename);
                    
                    // Normalize entire filename to lowercase for consistency with cache files
                    modelFilename = modelFilename.ToLowerInvariant();
                }
            }
            
            // If still not found, try to look up ACTI (activator) record
            if (string.IsNullOrEmpty(modelFilename))
            {
                TESActivatorLibrary.ActivatorEntry activatorEntry = TESActivatorLibrary.GetActivator(modelId);
                if (activatorEntry == null && !string.Equals(modelId, cleanedModelId, StringComparison.OrdinalIgnoreCase))
                {
                    activatorEntry = TESActivatorLibrary.GetActivator(cleanedModelId);
                }
                
                if (activatorEntry != null && !string.IsNullOrEmpty(activatorEntry.ModelFilename))
                {
                    // Clean the model filename from activator
                    modelFilename = activatorEntry.ModelFilename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    modelFilename = modelFilename.Replace("\0", "");
                    modelFilename = modelFilename.Trim();
                    
                    // Normalize path separators (replace backslashes with forward slashes)
                    modelFilename = modelFilename.Replace('\\', '/');
                    
                    // Extract just the filename (strip any subdirectory paths)
                    modelFilename = Path.GetFileName(modelFilename);
                    
                    // Normalize entire filename to lowercase for consistency with cache files
                    modelFilename = modelFilename.ToLowerInvariant();
                }
            }
            
            // Always check for LIGH (light) record - even if STAT/CONT/DOOR/ACTI was found, the object ID might still be a light
            // This ensures lights get the TESLight component attached even if they have a STAT record
            TESLightManager.LightEntry foundLightEntry = null; // Store for later component attachment
            // Use the cleaned modelId (which should match how it's stored in TESLightManager)
            TESLightManager.LightEntry lightEntry = TESLightManager.GetLight(modelId);
            
            // If not found, try with the original objectId.objectId (before cleaning)
            // GetLight will normalize it, so this should work if there was a cleaning mismatch
            if (lightEntry == null && objectId != null && !string.IsNullOrEmpty(objectId.objectId))
            {
                string originalId = objectId.objectId;
                lightEntry = TESLightManager.GetLight(originalId);
                if (lightEntry != null)
                {
                    UnityEngine.Debug.LogWarning($"[PlaceStatics] Found light using original ID '{originalId}' (cleaned was '{modelId}')");
                }
            }
            
            if (lightEntry != null)
            {
                foundLightEntry = lightEntry; // Store for component attachment (even if we use STAT model)
                
                // Only use light's modelFilename if we don't already have one from STAT/CONT
                if (string.IsNullOrEmpty(modelFilename) && !string.IsNullOrEmpty(lightEntry.ModelFilename))
                {
                    // Clean the model filename from light (same pattern as containers)
                    modelFilename = lightEntry.ModelFilename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    modelFilename = modelFilename.Replace("\0", "");
                    modelFilename = modelFilename.Trim();
                    
                    // Normalize path separators (replace backslashes with forward slashes)
                    modelFilename = modelFilename.Replace('\\', '/');
                    
                    // Extract just the filename (strip any subdirectory paths)
                    modelFilename = Path.GetFileName(modelFilename);
                    
                    // Normalize entire filename to lowercase for consistency with cache files
                    // This ensures "Light_de_streetlight_01.NIF" becomes "light_de_streetlight_01.nif" to match cache
                    // Cache files are typically stored in lowercase
                    modelFilename = modelFilename.ToLowerInvariant();
                    
                    // Note: The NIFLoader will check TESNifLibrary cache first, then file system/BSA
                    // and cache the result in TESNifLibrary for reuse, just like containers and NPCs
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
                // Mark this model ID as failed to avoid repeated lookups
                _failedModelLookups.Add(modelId);
                
                // Only log the first time we encounter this missing model (to reduce spam)
                if (_failedModelLookups.Count <= 100 || _failedModelLookups.Count % 50 == 0)
                {
                    UnityEngine.Debug.LogWarning($"Could not find model for ID '{modelId}' (checked STAT records, CONT records, DOOR records, ACTI records, LIGH records, and cache directory). This will be cached to avoid repeated lookups.");
                }
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
                success = PlaceStaticObject(modelFilename, refp, scale, objectId, refParent, cellGridX, cellGridY, allRecords, foundLightEntry);
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
        /// Tries to extract a NIF file from BSA archives using a full path (e.g., "meshes/base_anim.nif")
        /// This is used for skeleton files that need the "meshes/" prefix
        /// Also tries variations with/without underscores (base_anim vs baseanim)
        /// </summary>
        private string TryExtractFromBSAWithPath(string fullPath)
        {
            try
            {
                // Normalize path separators
                string normalizedPath = fullPath.Replace('\\', '/');
                
                // Build path variations to search for (with different case, separators, and underscore variations)
                List<string> pathVariations = new List<string>
                {
                    normalizedPath,
                    normalizedPath.Replace('/', '\\'),
                    normalizedPath.ToLowerInvariant(),
                    normalizedPath.ToUpperInvariant()
                };
                
                // For skeleton files, try both with and without underscores
                // Morrowind uses base_anim.nif but some mods might use baseanim.nif
                if (normalizedPath.Contains("base_anim") || normalizedPath.Contains("baseanim"))
                {
                    string underscoreVariation = normalizedPath.Replace("base_anim", "baseanim");
                    string noUnderscoreVariation = normalizedPath.Replace("baseanim", "base_anim");
                    
                    pathVariations.Add(underscoreVariation);
                    pathVariations.Add(underscoreVariation.Replace('/', '\\'));
                    pathVariations.Add(underscoreVariation.ToLowerInvariant());
                    
                    pathVariations.Add(noUnderscoreVariation);
                    pathVariations.Add(noUnderscoreVariation.Replace('/', '\\'));
                    pathVariations.Add(noUnderscoreVariation.ToLowerInvariant());
                }
                
                // Get all file names from all BSAs
                HashSet<string> allBSAFiles = TESBSALibrary.GetAllFileNames();
                
                // Try to find the model with various path variations
                string foundPath = null;
                foreach (string pathVar in pathVariations)
                {
                    if (allBSAFiles.Contains(pathVar))
                    {
                        foundPath = pathVar;
                        break;
                    }
                }
                
                // Case-insensitive fallback
                if (foundPath == null)
                {
                    string normalizedPathLower = normalizedPath.ToLowerInvariant();
                    foreach (string bsaFileName in allBSAFiles)
                    {
                        if (bsaFileName.ToLowerInvariant() == normalizedPathLower ||
                            bsaFileName.ToLowerInvariant().Replace('\\', '/') == normalizedPathLower)
                        {
                            foundPath = bsaFileName;
                            break;
                        }
                    }
                }
                
                if (foundPath != null)
                {
                    // Find which ESM's BSA contains this file (search in load order)
                    var esmEntries = TESESMLibrary.GetLoadedESMEntries();
                    string sourceESM = null;
                    
                    foreach (var esmEntry in esmEntries)
                    {
                        var bsaEntry = TESBSALibrary.GetBSAEntryForESM(esmEntry.ESMFilename);
                        if (bsaEntry != null && bsaEntry.IsLoaded && bsaEntry.FileNames.Contains(foundPath))
                        {
                            sourceESM = Path.GetFileNameWithoutExtension(esmEntry.ESMFilename);
                            break;
                        }
                    }
                    
                    // If we couldn't determine source ESM, use preferred ESM or first loaded
                    if (string.IsNullOrEmpty(sourceESM))
                    {
                        sourceESM = Path.GetFileNameWithoutExtension(_esm);
                        if (string.IsNullOrEmpty(sourceESM) && esmEntries.Length > 0)
                        {
                            sourceESM = Path.GetFileNameWithoutExtension(esmEntries[0].ESMFilename);
                        }
                    }
                    
                    // Extract to cache using TESBSALibrary
                    string outputFilename = Path.GetFileName(foundPath);
                    string cacheDir = Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", sourceESM);
                    Directory.CreateDirectory(cacheDir);
                    string outputPath = Path.Combine(cacheDir, outputFilename);
                    
                    // Use TESBSALibrary to extract (it will find the correct BSA)
                    if (TESBSALibrary.ExtractFile(foundPath, outputPath))
                    {
                        UnityEngine.Debug.Log($"Extracted skeleton from BSA: {foundPath} -> {outputFilename} (ESM: {sourceESM})");
                        return outputFilename;
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error extracting skeleton from BSA for path '{fullPath}': {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// Tries to extract a NIF file from BSA archives using TESBSALibrary (supports multiple BSAs)
        /// Searches all loaded BSAs in load order and extracts to the appropriate ESM cache directory
        /// </summary>
        private string TryExtractFromBSA(string modelId)
        {
            try
            {
                // Build path variations to search for
                List<string> pathVariations = new List<string>
                {
                    modelId + ".nif",
                    modelId + ".NIF",
                    "meshes\\" + modelId + ".nif",
                    "meshes/" + modelId + ".nif",
                    "Meshes\\" + modelId + ".nif",
                    "Meshes/" + modelId + ".nif"
                };
                
                // Try first letter subdirectory (common BSA organization) if modelId has at least 1 character
                if (!string.IsNullOrEmpty(modelId) && modelId.Length > 0)
                {
                    string firstLetter = modelId.Substring(0, 1);
                    pathVariations.AddRange(new string[]
                    {
                        "meshes\\" + firstLetter.ToLower() + "\\" + modelId + ".nif",
                        "meshes/" + firstLetter.ToLower() + "/" + modelId + ".nif",
                        "Meshes\\" + firstLetter.ToLower() + "\\" + modelId + ".nif",
                        "Meshes/" + firstLetter.ToLower() + "/" + modelId + ".nif",
                        "meshes\\" + firstLetter.ToUpper() + "\\" + modelId + ".nif",
                        "meshes/" + firstLetter.ToUpper() + "/" + modelId + ".nif"
                    });
                }

                // Get all file names from all BSAs
                HashSet<string> allBSAFiles = TESBSALibrary.GetAllFileNames();
                
                // Try to find the model with various path variations
                string foundPath = null;
                foreach (string pathVar in pathVariations)
                {
                    if (allBSAFiles.Contains(pathVar))
                    {
                        foundPath = pathVar;
                        break;
                    }
                }

                // Case-insensitive fallback - try multiple case variations and path patterns
                if (foundPath == null)
                {
                    string modelLower = modelId.ToLower();
                    string modelUpper = modelId.ToUpper();
                    string modelOriginal = modelId; // Keep original case
                    
                    // Try various case combinations
                    string[] caseVariations = new string[]
                    {
                        modelLower + ".nif",
                        modelUpper + ".NIF",
                        modelOriginal + ".nif",
                        modelOriginal + ".NIF"
                    };
                    
                    foreach (string bsaFileName in allBSAFiles)
                    {
                        string bsaFileNameLower = bsaFileName.ToLower();
                        string bsaFileNameNoExt = Path.GetFileNameWithoutExtension(bsaFileName);
                        
                        // Check if filename (without extension) matches any case variation
                        foreach (string caseVar in caseVariations)
                        {
                            string caseVarNoExt = Path.GetFileNameWithoutExtension(caseVar);
                            if (string.Equals(bsaFileNameNoExt, caseVarNoExt, StringComparison.OrdinalIgnoreCase))
                            {
                                // Check various path patterns (including subdirectories)
                                string caseVarLower = caseVar.ToLower();
                                if (bsaFileNameLower.EndsWith("\\" + caseVarLower) ||
                                    bsaFileNameLower.EndsWith("/" + caseVarLower) ||
                                    bsaFileNameLower == caseVarLower ||
                                    bsaFileNameLower.EndsWith("\\meshes\\" + caseVarLower) ||
                                    bsaFileNameLower.EndsWith("/meshes/" + caseVarLower) ||
                                    bsaFileNameLower.Contains("\\meshes\\" + caseVarLower) ||
                                    bsaFileNameLower.Contains("/meshes/" + caseVarLower))
                                {
                                    foundPath = bsaFileName;
                                    break;
                                }
                            }
                        }
                        
                        if (foundPath != null)
                            break;
                    }
                }

                if (foundPath != null)
                {
                    // Find which ESM's BSA contains this file (search in load order)
                    var esmEntries = TESESMLibrary.GetLoadedESMEntries();
                    string sourceESM = null;
                    
                    foreach (var esmEntry in esmEntries)
                    {
                        var bsaEntry = TESBSALibrary.GetBSAEntryForESM(esmEntry.ESMFilename);
                        if (bsaEntry != null && bsaEntry.IsLoaded && bsaEntry.FileNames.Contains(foundPath))
                        {
                            sourceESM = Path.GetFileNameWithoutExtension(esmEntry.ESMFilename);
                            break;
                        }
                    }
                    
                    // If we couldn't determine source ESM, use preferred ESM or first loaded
                    if (string.IsNullOrEmpty(sourceESM))
                    {
                        sourceESM = Path.GetFileNameWithoutExtension(_esm);
                        if (string.IsNullOrEmpty(sourceESM) && esmEntries.Length > 0)
                        {
                            sourceESM = Path.GetFileNameWithoutExtension(esmEntries[0].ESMFilename);
                        }
                    }
                    
                    // Extract to cache using TESBSALibrary
                    string outputFilename = Path.GetFileName(foundPath);
                    string cacheDir = Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", sourceESM);
                    Directory.CreateDirectory(cacheDir);
                    string outputPath = Path.Combine(cacheDir, outputFilename);
                    
                    // Use TESBSALibrary to extract (it will find the correct BSA)
                    if (TESBSALibrary.ExtractFile(foundPath, outputPath))
                    {
                        // UnityEngine.Debug.Log($"Extracted NIF from BSA: {foundPath} -> {outputFilename} (ESM: {sourceESM})"); // Commented out for performance
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
        private bool PlaceStaticObject(string modelFilename, SubRecordCellREFP refp, float scale, SubRecordCellObjectID objectId, Transform parent, int cellGridX = 0, int cellGridY = 0, Record[] allRecords = null, TESLightManager.LightEntry lightEntry = null)
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
                if (cachedEntry != null && cachedEntry.Model != null)
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
                    
                    // Check if this is a light and attach TESLight component
                    if (lightEntry != null)
                    {
                        AttachLightComponent(instanceObj, lightEntry);
                    }
                    else if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                    {
                        // Fallback: try to find light by ID if not passed in
                        string lightId = objectId.objectId.TrimEnd('\0', ' ', '\t', '\r', '\n');
                        lightId = lightId.Replace("\0", "");
                        lightId = new string(lightId.Where(c => c != '\0').ToArray()).Trim();
                        AttachLightComponentIfNeeded(instanceObj, lightId);
                    }
                    
                    // Parent to terrain or specified parent
                    // Use worldPositionStays: true to preserve world position when parenting
                    // This prevents Unity from converting world position to local coordinates
                    if (parent != null)
                    {
                        instanceObj.transform.SetParent(parent, worldPositionStays: true);
                    }
                    
                    // Fix reflection issues: negate Z scale and negate Yaw
                    // DISABLED: Coordinate system conversion should now handle this at NIF loading level
                    // This must be done AFTER parenting to ensure it's applied to all objects
                    // Vector3 currentScale = instanceObj.transform.localScale;
                    // instanceObj.transform.localScale = new Vector3(currentScale.x, currentScale.y, -currentScale.z);
                    // 
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
                // The model from NIFLoader should be in a clean state (no transforms applied)
                // We check if it's already in the library first to avoid storing duplicates
                string staticId = objectId?.objectId?.TrimEnd('\0');
                string staticName = staticId; // Use ID as name if no separate name available
                
                TESNifLibrary.NifEntry existingEntry = TESNifLibrary.GetModelByBaseFilename(baseFilenameNoExt);
                if (existingEntry == null)
                {
                    // Store the model in library (this is the first time loading this model)
                    // The model should be in a clean state from NIFLoader
                    TESNifLibrary.AddModel(staticId, staticName, modelFilename, baseFilenameNoExt, modelObj, combineMeshes: false);
                }

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
                
                // Check if this is a light and attach TESLight component
                if (lightEntry != null)
                {
                    AttachLightComponent(modelObj, lightEntry);
                }
                else if (objectId != null && !string.IsNullOrEmpty(objectId.objectId))
                {
                    // Fallback: try to find light by ID if not passed in
                    string lightId = objectId.objectId.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    lightId = lightId.Replace("\0", "");
                    lightId = new string(lightId.Where(c => c != '\0').ToArray()).Trim();
                    AttachLightComponentIfNeeded(modelObj, lightId);
                }
                
                // Parent to terrain or specified parent
                // Use worldPositionStays: true to preserve world position when parenting
                // This prevents Unity from converting world position to local coordinates
                if (parent != null)
                {
                    modelObj.transform.SetParent(parent, worldPositionStays: true);
                }
                
                // Fix reflection issues: negate Z scale and negate Yaw
                // DISABLED: Coordinate system conversion should now handle this at NIF loading level
                // This must be done AFTER parenting to ensure it's applied to all objects
                // Apply to this instance (the first one, which is also stored as template)
                // Vector3 modelCurrentScale = modelObj.transform.localScale;
                // modelObj.transform.localScale = new Vector3(modelCurrentScale.x, modelCurrentScale.y, -modelCurrentScale.z);
                // 
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
        /// Attaches TESLight component to a GameObject with the provided LightEntry
        /// </summary>
        private void AttachLightComponent(GameObject obj, TESLightManager.LightEntry lightEntry)
        {
            if (obj == null || lightEntry == null)
                return;
            
            // Add TESLight component and initialize it
            TESLight tesLight = obj.GetComponent<TESLight>();
            if (tesLight == null)
            {
                tesLight = obj.AddComponent<TESLight>();
            }
            tesLight.Initialize(lightEntry);
        }
        
        /// <summary>
        /// Attaches TESLight component to a GameObject if it corresponds to a light record (lookup by ID)
        /// </summary>
        private void AttachLightComponentIfNeeded(GameObject obj, string modelId)
        {
            if (obj == null || string.IsNullOrEmpty(modelId))
                return;
            
            // Check if this model ID corresponds to a light
            TESLightManager.LightEntry lightEntry = TESLightManager.GetLight(modelId);
            if (lightEntry == null)
            {
                // Try cleaned version
                string cleanedId = modelId.TrimEnd('\0', ' ', '\t', '\r', '\n');
                cleanedId = cleanedId.Replace("\0", "");
                cleanedId = new string(cleanedId.Where(c => c != '\0').ToArray()).Trim();
                if (!string.Equals(modelId, cleanedId, StringComparison.OrdinalIgnoreCase))
                {
                    lightEntry = TESLightManager.GetLight(cleanedId);
                }
            }
            
            if (lightEntry != null)
            {
                AttachLightComponent(obj, lightEntry);
            }
        }
        
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
            TESCharacterManager.NPCEntry npcEntry = null;
            
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

                npcEntry = TESCharacterManager.GetNPC(npcId);
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
            
            // Determine gender and race for skeleton selection
            bool isFemale = false;
            if (bodyPart != null && (bodyPart.Flags & 0x01) != 0)
            {
                isFemale = true;
            }
            else if (bodyPart != null && !string.IsNullOrEmpty(bodyPart.ModelFilename) && 
                     bodyPart.ModelFilename.IndexOf("female", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                isFemale = true;
            }
            
            bool isBeast = false;
            if (npcEntry != null && !string.IsNullOrEmpty(npcEntry.RaceName))
            {
                string raceLower = npcEntry.RaceName.ToLowerInvariant();
                isBeast = raceLower.Contains("khajiit") || raceLower.Contains("argonian");
            }
            
            // Get skeleton model path
            string skeletonPath = GetNPCSkeletonPath(isFemale, isBeast);
            
            // Load skeleton model first (this is the base that body parts attach to)
            GameObject skeletonModel = null;
            if (!string.IsNullOrEmpty(skeletonPath))
            {
                yield return LoadNIFModelCoroutine(skeletonPath, false, (loadedModel) => skeletonModel = loadedModel);
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
            
            // Load equipment models (armor/clothing) - must be done before try-catch since yield return can't be in try-catch
            List<GameObject> equipmentModels = new List<GameObject>();
            List<string> equipmentNames = new List<string>();
            if (npcEntry != null && npcEntry.Inventory != null && npcEntry.Inventory.Count > 0)
            {
                Dictionary<uint, TESEquipmentLibrary.EquipmentEntry> equippedItems = TESEquipmentLibrary.GetEquippedItems(npcEntry.Inventory);
                
                foreach (var kvp in equippedItems)
                {
                    uint slotType = kvp.Key;
                    TESEquipmentLibrary.EquipmentEntry equipment = kvp.Value;
                    
                    if (equipment == null || string.IsNullOrEmpty(equipment.ModelFilename))
                        continue;
                    
                    // Load equipment model
                    GameObject equipmentModel = null;
                    yield return LoadNIFModelCoroutine(equipment.ModelFilename, false, (loadedModel) => equipmentModel = loadedModel);
                    
                    if (equipmentModel != null)
                    {
                        equipmentModels.Add(equipmentModel);
                        string slotName = GetEquipmentSlotName(slotType);
                        equipmentNames.Add($"{slotName} ({equipment.DisplayName ?? equipment.EquipmentId})");
                    }
                }
            }
            
            try
            {
                // Process skeleton if loaded
                Transform headBoneTransform = null;
                if (skeletonModel != null)
                {
                    // Scale skeleton to match NPC scale
                    // Skeleton NIF files are in Morrowind units, same as static objects, so use MORROWIND_TO_STATIC_SCALE
                    // Then apply the NPC's individual scale multiplier
                    Vector3 skeletonBaseScale = new Vector3(TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE);
                    float scaleMultiplier = scale < 0 ? Mathf.Abs(scale) : scale;
                    skeletonModel.transform.SetParent(npcObj.transform, false);
                    skeletonModel.name = "Skeleton";
                    skeletonModel.transform.localScale = skeletonBaseScale * scaleMultiplier;
                    
                    // Coordinate system conversion is now handled at the NIF loading level in CreateBoneHierarchy
                    // No need for post-processing hacks here
                    
                    // Process skeleton to build bone hierarchy and find head bone
                    // Bones in skeleton files are NiNode objects that may be detected as collision meshes
                    // We need to identify them as bones and build proper hierarchy
                    headBoneTransform = ProcessSkeletonBones(skeletonModel);
                    
                    // Set up Mecanim/Animator for the skeleton using Biped naming convention
                    SetupMecanimRig(npcObj, skeletonModel);
                }
                
                // Attach body parts to skeleton if available, otherwise to NPC root
                Transform parentTransform = skeletonModel != null ? skeletonModel.transform : npcObj.transform;
                
                if (bodyModel != null)
                {
                    bodyModel.transform.SetParent(parentTransform, false);
                    bodyModel.name = "Body";
                    // Set scale to 1 so body parts inherit skeleton's scale
                    if (skeletonModel != null)
                    {
                        bodyModel.transform.localScale = Vector3.one;
                    }
                    loadedPartsCount++;
                }
                
                // Attach head to head bone if found, otherwise to skeleton root
                if (headModel != null)
                {
                    Transform headParent = headBoneTransform != null ? headBoneTransform : parentTransform;
                    headModel.transform.SetParent(headParent, false);
                    headModel.name = "Head";
                    // Set scale to 1 so body parts inherit skeleton's scale
                    if (skeletonModel != null)
                    {
                        headModel.transform.localScale = Vector3.one;
                    }
                    loadedPartsCount++;
                }
                
                // Attach hair to head bone if found, otherwise to skeleton root
                if (hairModel != null)
                {
                    Transform hairParent = headBoneTransform != null ? headBoneTransform : parentTransform;
                    hairModel.transform.SetParent(hairParent, false);
                    hairModel.name = "Hair";
                    // Set scale to 1 so body parts inherit skeleton's scale
                    if (skeletonModel != null)
                    {
                        hairModel.transform.localScale = Vector3.one;
                    }
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

                // Attach equipment models that were loaded before the try block
                for (int i = 0; i < equipmentModels.Count; i++)
                {
                    if (equipmentModels[i] != null)
                    {
                        equipmentModels[i].transform.SetParent(parentTransform, false);
                        equipmentModels[i].name = equipmentNames[i];
                        // Set scale to 1 so equipment inherits skeleton's scale
                        if (skeletonModel != null)
                        {
                            equipmentModels[i].transform.localScale = Vector3.one;
                        }
                    }
                }

                // DISABLED: Coordinate system conversion should now handle this at NIF loading level
                // Fix reflection issues
                // Vector3 currentScale = npcObj.transform.localScale;
                // npcObj.transform.localScale = new Vector3(currentScale.x, currentScale.y, -currentScale.z);

                Vector3 euler = npcObj.transform.rotation.eulerAngles;
                float yaw = euler.y;
                if (yaw > 180f) yaw -= 360f;
                npcObj.transform.rotation = Quaternion.Euler(euler.x, -yaw, euler.z);

                Transform cellParentTransform = cellParent != null ? cellParent.transform : null;
                if (cellParentTransform != null)
                {
                    npcObj.transform.SetParent(cellParentTransform, worldPositionStays: true);
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
            
            if (cachedEntry != null && cachedEntry.Model != null)
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
                
                // Determine gender early (needed for both MODL lookup and default body part search)
                bool isFemale = false;
                
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
                else
                {
                    // MODL is empty - try to find a default body part based on race and gender (following OpenMW logic)
                    // First, get head part to determine gender
                    if (!string.IsNullOrEmpty(npcEntry.HeadModel))
                    {
                        string cleanedHeadPartId = npcEntry.HeadModel.TrimEnd('\0', ' ', '\t', '\r', '\n');
                        cleanedHeadPartId = cleanedHeadPartId.Replace("\0", "");
                        cleanedHeadPartId = new string(cleanedHeadPartId.Where(c => c != '\0').ToArray()).Trim();
                        if (!string.IsNullOrEmpty(cleanedHeadPartId))
                        {
                            var tempHeadPart = TESCharacterManager.GetBodyPart(cleanedHeadPartId);
                            if (tempHeadPart != null)
                            {
                                isFemale = (tempHeadPart.Flags & 0x01) != 0;
                            }
                        }
                    }
                    
                    // Search for body parts matching race, gender, and part type (Part = 3 = MP_Chest, PartType = 0 = MT_Skin)
                    // Following OpenMW: search for body parts with MT_Skin type, matching race, matching gender
                    if (!string.IsNullOrEmpty(npcEntry.RaceName))
                    {
                        var matchingBodyParts = TESCharacterManager.GetBodyPartsByPart(3) // MP_Chest
                            .Where(bp => 
                                bp.PartType == 0 && // MT_Skin (0 = Skin type)
                                !string.IsNullOrEmpty(bp.RaceName) &&
                                string.Equals(bp.RaceName, npcEntry.RaceName, StringComparison.OrdinalIgnoreCase) &&
                                (bp.Flags & 0x01) == (isFemale ? 0x01 : 0x00)) // Match gender
                            .ToList();
                        
                        if (matchingBodyParts.Count > 0)
                        {
                            bodyPart = matchingBodyParts[0];
                            UnityEngine.Debug.Log($"PlaceNPC: NPC '{npcId}' has empty MODL, using default body part '{bodyPart.BodyPartId}' (race: '{npcEntry.RaceName}', gender: {(isFemale ? "female" : "male")})");
                        }
                    }
                    
                    // If still no body part found, try to find any chest body part matching gender
                    if (bodyPart == null)
                    {
                        var anyChestPart = TESCharacterManager.GetBodyPartsByPart(3)
                            .Where(bp => bp.PartType == 0 && (bp.Flags & 0x01) == (isFemale ? 0x01 : 0x00))
                            .FirstOrDefault();
                        if (anyChestPart != null)
                        {
                            bodyPart = anyChestPart;
                            UnityEngine.Debug.Log($"PlaceNPC: NPC '{npcId}' has empty MODL, using fallback body part '{bodyPart.BodyPartId}' (gender: {(isFemale ? "female" : "male")})");
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
                
                // Determine gender and race for skeleton selection (if not already determined)
                // Check body part flags for female flag (0x01)
                if (bodyPart != null && (bodyPart.Flags & 0x01) != 0)
                {
                    isFemale = true;
                }
                // Also check if model filename contains "female" as fallback
                else if (bodyPart != null && !string.IsNullOrEmpty(bodyPart.ModelFilename) && 
                         bodyPart.ModelFilename.IndexOf("female", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isFemale = true;
                }
                
                // Check if race is a beast race (Khajiit or Argonian)
                bool isBeast = false;
                if (!string.IsNullOrEmpty(npcEntry.RaceName))
                {
                    string raceLower = npcEntry.RaceName.ToLowerInvariant();
                    isBeast = raceLower.Contains("khajiit") || raceLower.Contains("argonian");
                }
                
                // Get skeleton model path based on race and gender (following OpenMW logic)
                string skeletonPath = GetNPCSkeletonPath(isFemale, isBeast);
                
                // Load skeleton model first (this is the base that body parts attach to)
                GameObject skeletonModel = null;
                Transform headBoneTransform = null;
                if (!string.IsNullOrEmpty(skeletonPath))
                {
                    skeletonModel = LoadNIFModel(skeletonPath, false);
                    if (skeletonModel != null)
                    {
                        // Scale skeleton to match NPC scale
                        // Skeleton NIF files are in Morrowind units, same as static objects, so use MORROWIND_TO_STATIC_SCALE
                        // Then apply the NPC's individual scale multiplier
                        Vector3 skeletonBaseScale = new Vector3(TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE, TESGlobals.MORROWIND_TO_STATIC_SCALE);
                        float scaleMultiplier = scale < 0 ? Mathf.Abs(scale) : scale;
                        skeletonModel.transform.SetParent(npcObj.transform, false);
                        skeletonModel.name = "Skeleton";
                        skeletonModel.transform.localScale = skeletonBaseScale * scaleMultiplier;
                        
                        // Coordinate system conversion is now handled at the NIF loading level in CreateBoneHierarchy
                        // No need for post-processing hacks here
                        
                        // Process skeleton to build bone hierarchy and find head bone
                        headBoneTransform = ProcessSkeletonBones(skeletonModel);
                        
                        // Set up Mecanim/Animator for the skeleton using Biped naming convention
                        SetupMecanimRig(npcObj, skeletonModel);
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"PlaceNPC: Failed to load skeleton model '{skeletonPath}' for NPC '{npcId}'. NPC will be placed without skeleton.");
                    }
                }
                
                // Load and attach body parts
                // Note: Some body parts may fail to load due to niflib.net parsing limitations
                // (e.g., "Invalid object type string length!" errors with certain NIF formats)
                // We continue loading other parts even if some fail
                int loadedPartsCount = 0;
                
                // Determine parent transform for body parts (skeleton if available, otherwise NPC root)
                Transform parentTransform = skeletonModel != null ? skeletonModel.transform : npcObj.transform;
                
                // Load body (main model) - this is the skin/torso that goes on the skeleton
                if (bodyPart != null && !string.IsNullOrEmpty(bodyPart.ModelFilename))
                {
                    UnityEngine.Debug.Log($"PlaceNPC: Loading body model for NPC '{npcId}': body part ID='{bodyPart.BodyPartId}', model filename='{bodyPart.ModelFilename}'");
                    GameObject bodyModel = LoadBodyPartModel(bodyPart.ModelFilename, bodyPart.BodyPartId);
                    if (bodyModel != null)
                    {
                        // Attach to skeleton if available, otherwise to NPC root
                        bodyModel.transform.SetParent(parentTransform, false);
                        bodyModel.name = "Body";
                        // Set scale to 1 so body parts inherit skeleton's scale
                        if (skeletonModel != null)
                        {
                            bodyModel.transform.localScale = Vector3.one;
                        }
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
                        // Attach head to head bone if found, otherwise to skeleton root
                        Transform headParent = headBoneTransform != null ? headBoneTransform : parentTransform;
                        headModel.transform.SetParent(headParent, false);
                        headModel.name = "Head";
                        // Set scale to 1 so body parts inherit skeleton's scale
                        if (skeletonModel != null)
                        {
                            headModel.transform.localScale = Vector3.one;
                        }
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
                        // Attach hair to head bone if found, otherwise to skeleton root
                        Transform hairParent = headBoneTransform != null ? headBoneTransform : parentTransform;
                        hairModel.transform.SetParent(hairParent, false);
                        hairModel.name = "Hair";
                        // Set scale to 1 so body parts inherit skeleton's scale
                        if (skeletonModel != null)
                        {
                            hairModel.transform.localScale = Vector3.one;
                        }
                        loadedPartsCount++;
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"PlaceNPC: Failed to load hair model for NPC '{npcId}' (body part: '{hairPart.BodyPartId}'). This may be due to niflib.net parsing limitations.");
                    }
                }
                
                // Load and attach equipment (armor/clothing) - synchronous version
                if (npcEntry != null && npcEntry.Inventory != null && npcEntry.Inventory.Count > 0)
                {
                    Dictionary<uint, TESEquipmentLibrary.EquipmentEntry> equippedItems = TESEquipmentLibrary.GetEquippedItems(npcEntry.Inventory);
                    
                    foreach (var kvp in equippedItems)
                    {
                        uint slotType = kvp.Key;
                        TESEquipmentLibrary.EquipmentEntry equipment = kvp.Value;
                        
                        if (equipment == null || string.IsNullOrEmpty(equipment.ModelFilename))
                            continue;
                        
                        // Load equipment model
                        GameObject equipmentModel = LoadNIFModel(equipment.ModelFilename, false);
                        
                        if (equipmentModel != null)
                        {
                            // Attach to skeleton if available, otherwise to NPC root
                            equipmentModel.transform.SetParent(parentTransform, false);
                            
                            // Set scale to 1 so equipment inherits skeleton's scale
                            if (skeletonModel != null)
                            {
                                equipmentModel.transform.localScale = Vector3.one;
                            }
                            
                            // Name based on equipment slot
                            string slotName = GetEquipmentSlotName(slotType);
                            equipmentModel.name = $"{slotName} ({equipment.DisplayName ?? equipment.EquipmentId})";
                        }
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

                // DISABLED: Coordinate system conversion should now handle this at NIF loading level
                // Fix reflection issues: negate Z scale and negate Yaw (same as static objects)
                // Vector3 currentScale = npcObj.transform.localScale;
                // npcObj.transform.localScale = new Vector3(currentScale.x, currentScale.y, -currentScale.z);

                Vector3 euler = npcObj.transform.rotation.eulerAngles;
                float yaw = euler.y;
                if (yaw > 180f) yaw -= 360f;
                npcObj.transform.rotation = Quaternion.Euler(euler.x, -yaw, euler.z);

                // Parent to cell or specified parent
                Transform cellParentTransform = cellParent != null ? cellParent.transform : null;
                if (cellParentTransform != null)
                {
                    npcObj.transform.SetParent(cellParentTransform, worldPositionStays: true);
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
        /// Gets the skeleton model path for an NPC based on gender and race.
        /// Following OpenMW logic: meshes/base_anim.nif (male), meshes/base_anim_female.nif (female), meshes/base_animkna.nif (beast races).
        /// OpenMW uses correctMeshPath which prepends "meshes/" to the path.
        /// Note: Morrowind uses underscores in skeleton filenames (base_anim.nif, not baseanim.nif)
        /// </summary>
        /// <param name="isFemale">Whether the NPC is female</param>
        /// <param name="isBeast">Whether the NPC is a beast race (Khajiit/Argonian)</param>
        /// <returns>The skeleton model path with "meshes/" prefix, or null if unable to determine</returns>
        private string GetNPCSkeletonPath(bool isFemale, bool isBeast)
        {
            // Based on OpenMW's getActorSkeleton function and correctMeshPath
            // OpenMW prepends "meshes/" to all mesh paths (see correctMeshPath in resourcehelpers.cpp)
            // Morrowind actually uses underscores: base_anim.nif, base_anim_female.nif, base_animkna.nif
            string skeletonName;
            
            // Beast races (Khajiit/Argonian) use base_animkna.nif
            if (isBeast)
            {
                skeletonName = "base_animkna.nif";
            }
            // Female NPCs use base_anim_female.nif
            else if (isFemale)
            {
                skeletonName = "base_anim_female.nif";
            }
            // Male NPCs use base_anim.nif
            else
            {
                skeletonName = "base_anim.nif";
            }
            
            // Prepend "meshes/" like OpenMW does (correctMeshPath)
            return "meshes/" + skeletonName;
        }
        
        /// <summary>
        /// Processes skeleton GameObject to build proper bone hierarchy and find bone transforms
        /// Removes collision meshes that are actually bones, and finds bones by name for body part attachment
        /// </summary>
        /// <param name="skeletonRoot">The skeleton root GameObject</param>
        /// <returns>Transform of the head bone (Bip01 Head) if found, null otherwise</returns>
        private Transform ProcessSkeletonBones(GameObject skeletonRoot)
        {
            if (skeletonRoot == null)
                return null;
            
            Transform headBone = null;
            Dictionary<string, Transform> boneMap = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            
            // Recursively process all children to find bones and remove collision meshes that are actually bones
            ProcessSkeletonBonesRecursive(skeletonRoot.transform, boneMap);
            
            // Find head bone by common names (Morrowind uses Bip01 Head or similar)
            string[] headBoneNames = { "Bip01 Head", "Bip01 Head1", "Head", "head", "bip01 head", "bip01 head1" };
            foreach (string boneName in headBoneNames)
            {
                if (boneMap.TryGetValue(boneName, out Transform foundBone))
                {
                    headBone = foundBone;
                    break;
                }
            }
            
            // Also search case-insensitively in all transforms
            if (headBone == null)
            {
                foreach (Transform child in skeletonRoot.GetComponentsInChildren<Transform>())
                {
                    if (child == skeletonRoot.transform)
                        continue;
                    
                    string childName = child.name;
                    foreach (string headName in headBoneNames)
                    {
                        if (string.Equals(childName, headName, StringComparison.OrdinalIgnoreCase))
                        {
                            headBone = child;
                            break;
                        }
                    }
                    if (headBone != null)
                        break;
                }
            }
            
            return headBone;
        }
        
        /// <summary>
        /// Recursively processes skeleton transforms to identify bones and remove collision meshes
        /// </summary>
        private void ProcessSkeletonBonesRecursive(Transform parent, Dictionary<string, Transform> boneMap)
        {
            if (parent == null)
                return;
            
            // Process all children
            List<Transform> children = new List<Transform>();
            foreach (Transform child in parent)
            {
                children.Add(child);
            }
            
            foreach (Transform child in children)
            {
                string childName = child.name;
                
                // Check if this is a bone (NiNode) that was incorrectly marked as a collision mesh
                // Bones typically:
                // 1. Have no MeshRenderer (collision meshes have MeshCollider but no MeshRenderer)
                // 2. Have children (bones form a hierarchy)
                // 3. Have names like "Bip01", "Bip01 Head", etc.
                bool hasMeshRenderer = child.GetComponent<MeshRenderer>() != null;
                bool hasMeshCollider = child.GetComponent<MeshCollider>() != null;
                bool hasChildren = child.childCount > 0;
                bool looksLikeBone = childName.Contains("Bip01", StringComparison.OrdinalIgnoreCase) ||
                                     childName.Contains("Bone", StringComparison.OrdinalIgnoreCase) ||
                                     childName.Contains("Head", StringComparison.OrdinalIgnoreCase) ||
                                     childName.Contains("Spine", StringComparison.OrdinalIgnoreCase) ||
                                     childName.Contains("Neck", StringComparison.OrdinalIgnoreCase);
                
                // If it looks like a bone but has a MeshCollider and no MeshRenderer, it's probably a bone, not a collision mesh
                if (looksLikeBone && hasMeshCollider && !hasMeshRenderer && hasChildren)
                {
                    // Remove the MeshCollider - this is a bone, not a collision mesh
                    MeshCollider collider = child.GetComponent<MeshCollider>();
                    if (collider != null)
                    {
                        UnityEngine.Object.DestroyImmediate(collider);
                    }
                    
                    // Also remove MeshFilter if present (bones don't need meshes)
                    MeshFilter meshFilter = child.GetComponent<MeshFilter>();
                    if (meshFilter != null)
                    {
                        UnityEngine.Object.DestroyImmediate(meshFilter);
                    }
                }
                
                // Add to bone map for lookup
                if (!string.IsNullOrEmpty(childName))
                {
                    boneMap[childName] = child;
                }
                
                // Recursively process children
                ProcessSkeletonBonesRecursive(child, boneMap);
            }
        }
        
        /// <summary>
        /// Sets up Unity Mecanim/Animator rig for NPC skeleton using Biped naming convention
        /// Maps Bip01 bones to Unity's HumanBodyBones for animation support
        /// </summary>
        private void SetupMecanimRig(GameObject npcRoot, GameObject skeletonRoot)
        {
            if (npcRoot == null || skeletonRoot == null)
                return;
            
            try
            {
                // Get or add Animator component to NPC root
                Animator animator = npcRoot.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = npcRoot.AddComponent<Animator>();
                }
                
                // Create HumanDescription for avatar mapping
                HumanDescription humanDescription = new HumanDescription();
                
                // Build bone mapping from Biped naming to Unity HumanBodyBones
                // Unity recognizes standard Biped naming conventions automatically
                List<HumanBone> humanBones = new List<HumanBone>();
                
                // Map Biped bones to Unity HumanBodyBones
                // Unity's Avatar system can auto-detect Biped naming, but we'll explicitly map key bones
                // Order matters: more specific names first to avoid duplicates
                Dictionary<string, HumanBodyBones> bipedToHumanBoneMap = new Dictionary<string, HumanBodyBones>(StringComparer.OrdinalIgnoreCase)
                {
                    // Core skeleton - prioritize "Bip01 Pelvis" over "Bip01" to avoid duplicates
                    { "Bip01 Pelvis", HumanBodyBones.Hips },
                    { "Bip01 Spine", HumanBodyBones.Spine },
                    { "Bip01 Spine1", HumanBodyBones.Chest },
                    { "Bip01 Spine2", HumanBodyBones.UpperChest },
                    { "Bip01 Neck", HumanBodyBones.Neck },
                    { "Bip01 Head", HumanBodyBones.Head },
                    
                    // Left arm
                    { "Bip01 L Clavicle", HumanBodyBones.LeftShoulder },
                    { "Bip01 L UpperArm", HumanBodyBones.LeftUpperArm },
                    { "Bip01 L Forearm", HumanBodyBones.LeftLowerArm },
                    { "Bip01 L Hand", HumanBodyBones.LeftHand },
                    
                    // Right arm
                    { "Bip01 R Clavicle", HumanBodyBones.RightShoulder },
                    { "Bip01 R UpperArm", HumanBodyBones.RightUpperArm },
                    { "Bip01 R Forearm", HumanBodyBones.RightLowerArm },
                    { "Bip01 R Hand", HumanBodyBones.RightHand },
                    
                    // Left leg
                    { "Bip01 L Thigh", HumanBodyBones.LeftUpperLeg },
                    { "Bip01 L Calf", HumanBodyBones.LeftLowerLeg },
                    { "Bip01 L Foot", HumanBodyBones.LeftFoot },
                    { "Bip01 L Toe0", HumanBodyBones.LeftToes },
                    
                    // Right leg
                    { "Bip01 R Thigh", HumanBodyBones.RightUpperLeg },
                    { "Bip01 R Calf", HumanBodyBones.RightLowerLeg },
                    { "Bip01 R Foot", HumanBodyBones.RightFoot },
                    { "Bip01 R Toe0", HumanBodyBones.RightToes },
                };
                
                // Track which HumanBodyBones we've already mapped to avoid duplicates
                HashSet<HumanBodyBones> mappedBones = new HashSet<HumanBodyBones>();
                
                // Find all bones in skeleton and map them
                Transform[] allBones = skeletonRoot.GetComponentsInChildren<Transform>();
                foreach (Transform bone in allBones)
                {
                    if (bone == skeletonRoot.transform)
                        continue;
                    
                    string boneName = bone.name;
                    HumanBodyBones? humanBone = null;
                    
                    // Try exact match first (prioritize specific names)
                    if (bipedToHumanBoneMap.TryGetValue(boneName, out HumanBodyBones exactMatch))
                    {
                        humanBone = exactMatch;
                    }
                    // Try partial matches for variations (e.g., "Bip01 Head1" -> "Bip01 Head")
                    else
                    {
                        // Check partial matches, but only if we haven't already mapped this HumanBodyBones
                        foreach (var kvp in bipedToHumanBoneMap)
                        {
                            if (boneName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase) && !mappedBones.Contains(kvp.Value))
                            {
                                humanBone = kvp.Value;
                                break;
                            }
                        }
                    }
                    
                    // Only add if we found a mapping and haven't mapped this HumanBodyBones yet
                    if (humanBone.HasValue && !mappedBones.Contains(humanBone.Value))
                    {
                        HumanBone humanBoneMapping = new HumanBone
                        {
                            humanName = humanBone.Value.ToString(),
                            boneName = boneName,
                            limit = new HumanLimit
                            {
                                useDefaultValues = true
                            }
                        };
                        humanBones.Add(humanBoneMapping);
                        mappedBones.Add(humanBone.Value);
                    }
                }
                
                // Fallback: if "Bip01 Pelvis" wasn't found, try "Bip01" for Hips
                if (!mappedBones.Contains(HumanBodyBones.Hips))
                {
                    Transform bip01Root = skeletonRoot.transform.Find("Bip01");
                    if (bip01Root != null)
                    {
                        HumanBone humanBoneMapping = new HumanBone
                        {
                            humanName = HumanBodyBones.Hips.ToString(),
                            boneName = "Bip01",
                            limit = new HumanLimit
                            {
                                useDefaultValues = true
                            }
                        };
                        humanBones.Add(humanBoneMapping);
                        mappedBones.Add(HumanBodyBones.Hips);
                    }
                }
                
                // Set up HumanDescription
                humanDescription.human = humanBones.ToArray();
                humanDescription.skeleton = new SkeletonBone[0]; // Unity will auto-generate from bone hierarchy
                humanDescription.upperArmTwist = 0.5f;
                humanDescription.lowerArmTwist = 0.5f;
                humanDescription.upperLegTwist = 0.5f;
                humanDescription.lowerLegTwist = 0.5f;
                humanDescription.armStretch = 0.05f;
                humanDescription.legStretch = 0.05f;
                humanDescription.feetSpacing = 0.0f;
                humanDescription.hasTranslationDoF = false;
                
                // Create Avatar from HumanDescription
                // Note: Avatar creation requires the skeleton root to be the root of the bone hierarchy
                Avatar avatar = AvatarBuilder.BuildHumanAvatar(npcRoot, humanDescription);
                
                if (avatar != null && avatar.isValid)
                {
                    animator.avatar = avatar;
                    animator.applyRootMotion = false; // NPCs use root motion from animations, not transform
                    //UnityEngine.Debug.Log($"SetupMecanimRig: Successfully created Avatar for NPC '{npcRoot.name}' with {humanBones.Count} bone mappings");
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"SetupMecanimRig: Failed to create valid Avatar for NPC '{npcRoot.name}'. Avatar may be null or invalid.");
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"SetupMecanimRig: Error setting up Mecanim rig for NPC '{npcRoot.name}': {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// Gets a human-readable name for an equipment slot type
        /// </summary>
        private string GetEquipmentSlotName(uint slotType)
        {
            switch (slotType)
            {
                case 0x0001: return "Head";
                case 0x0002: return "Hair";
                case 0x0004: return "Neck";
                case 0x0008: return "Chest";
                case 0x0010: return "Groin";
                case 0x0020: return "Skirt";
                case 0x0040: return "RightHand";
                case 0x0080: return "LeftHand";
                case 0x0100: return "RightWrist";
                case 0x0200: return "LeftWrist";
                case 0x0400: return "Shield";
                case 0x0800: return "RightForearm";
                case 0x1000: return "LeftForearm";
                case 0x2000: return "RightUpperArm";
                case 0x4000: return "LeftUpperArm";
                case 0x8000: return "RightFoot";
                case 0x10000: return "LeftFoot";
                case 0x20000: return "RightAnkle";
                case 0x40000: return "LeftAnkle";
                default: return "Equipment";
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
            
            if (cachedEntry != null && cachedEntry.Model != null)
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
        private IEnumerator LoadNIFModelCoroutine(string modelFilename, bool combineMeshes, System.Action<GameObject> onComplete, bool isTreeOrGrass = false)
        {
            GameObject result = null;
            
            if (!TESGlobals.EnableMultithreadedModelLoading)
            {
                // Synchronous loading (for debugging)
                result = _nifLoader.LoadNIFFromCache(modelFilename, combineMeshes, isTreeOrGrass);
                onComplete?.Invoke(result);
                yield break;
            }

            // Check cache first (thread-safe read)
            string baseFilenameNoExt = Path.GetFileNameWithoutExtension(modelFilename);
            TESNifLibrary.NifEntry cachedEntry = TESNifLibrary.GetModelByBaseFilename(baseFilenameNoExt);
            if (cachedEntry != null && cachedEntry.Model != null)
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
                // File not in cache, try to extract from BSA
                // For skeleton files, preserve the full path (e.g., "meshes/baseanim.nif")
                // For other files, use just the base filename
                string bsaSearchPath = modelFilename;
                bool isSkeletonFile = modelFilename.Contains("baseanim") || modelFilename.Contains("skeleton");
                
                // If it's a skeleton file with "meshes/" prefix, try extracting with full path first
                if (isSkeletonFile && modelFilename.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase))
                {
                    // Try with full path first (e.g., "meshes/baseanim.nif")
                    string skeletonExtractedFilename = TryExtractFromBSAWithPath(modelFilename);
                    if (!string.IsNullOrEmpty(skeletonExtractedFilename))
                    {
                        // File was extracted, try loading again
                        string normalizedExtractedFilename = Path.GetFileName(skeletonExtractedFilename);
                        normalizedExtractedFilename = normalizedExtractedFilename.Replace('\\', '/');
                        yield return LoadNIFModelCoroutine(normalizedExtractedFilename, combineMeshes, onComplete, isTreeOrGrass);
                        yield break;
                    }
                }
                
                // Fallback: try with just the base filename (for non-skeleton files or if full path failed)
                string baseFilenameNoExtForBSA = Path.GetFileNameWithoutExtension(modelFilename);
                string extractedFilename = TryExtractFromBSA(baseFilenameNoExtForBSA);
                
                if (!string.IsNullOrEmpty(extractedFilename))
                {
                    // File was extracted, try loading again
                    string normalizedExtractedFilename = Path.GetFileName(extractedFilename);
                    normalizedExtractedFilename = normalizedExtractedFilename.Replace('\\', '/');
                    
                    // Try loading the extracted file
                    yield return LoadNIFModelCoroutine(normalizedExtractedFilename, combineMeshes, onComplete, isTreeOrGrass);
                    yield break;
                }
                
                // Log more details for skeleton files to help debug
                if (isSkeletonFile)
                {
                    UnityEngine.Debug.LogWarning($"LoadNIFModel: Skeleton file not found: {modelFilename}. Checked cache and BSA archive '{_bsa}'. Tried paths: '{modelFilename}' and '{baseFilenameNoExtForBSA}.nif'. Make sure the skeleton files are in the BSA or cache directory.");
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"LoadNIFModel: File not found: {modelFilename}");
                }
                onComplete?.Invoke(null);
                yield break;
            }

            // Parse and create GameObject on main thread (must be on main thread)
            try
            {
                string actualFilename = Path.GetFileName(modelFilename);
                result = _nifLoader.LoadNIFFromBytes(nifData, actualFilename, combineMeshes, isTreeOrGrass);
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
        private GameObject LoadNIFModel(string modelFilename, bool combineMeshes = false, bool isTreeOrGrass = false)
        {
            if (!TESGlobals.EnableMultithreadedModelLoading)
            {
                // Synchronous loading (for debugging)
                return _nifLoader.LoadNIFFromCache(modelFilename, combineMeshes, isTreeOrGrass);
            }

            // Check cache first (thread-safe read)
            string baseFilenameNoExt = Path.GetFileNameWithoutExtension(modelFilename);
            TESNifLibrary.NifEntry cachedEntry = TESNifLibrary.GetModelByBaseFilename(baseFilenameNoExt);
            if (cachedEntry != null && cachedEntry.Model != null)
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
                    // File not in cache, try to extract from BSA
                    string baseFilenameNoExtForBSA = Path.GetFileNameWithoutExtension(modelFilename);
                    string extractedFilename = TryExtractFromBSA(baseFilenameNoExtForBSA);
                    
                    if (!string.IsNullOrEmpty(extractedFilename))
                    {
                        // File was extracted, try loading again
                        string normalizedExtractedFilename = Path.GetFileName(extractedFilename);
                        normalizedExtractedFilename = normalizedExtractedFilename.Replace('\\', '/');
                        
                        // Try loading the extracted file (recursive call, but should be in cache now)
                        return LoadNIFModel(normalizedExtractedFilename, combineMeshes, isTreeOrGrass);
                    }
                    
                    UnityEngine.Debug.LogWarning($"LoadNIFModel: File not found: {modelFilename}");
                    return null;
                }

                // Parse and create GameObject on main thread (must be on main thread)
                string actualFilename = Path.GetFileName(modelFilename);
                return _nifLoader.LoadNIFFromBytes(nifData, actualFilename, combineMeshes, isTreeOrGrass);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"LoadNIFModel: Error loading {modelFilename}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Finds NIF file in cache directories across all loaded ESMs with case-insensitive search
        /// Checks all ESM cache directories in load order
        /// </summary>
        private string FindNIFInCache(string preferredESM, string modelFilename)
        {
            if (string.IsNullOrEmpty(modelFilename))
                return null;
            
            // Normalize filename - extract just the filename part
            string normalizedFilename = modelFilename?.Replace('\\', '/');
            normalizedFilename = Path.GetFileName(normalizedFilename);
            string searchFilename = Path.GetFileName(normalizedFilename);
            string searchFilenameLower = searchFilename.ToLowerInvariant();
            
            // Get all loaded ESMs in load order
            string[] loadedESMs = TESESMLibrary.GetLoadedESMFilenames();
            
            // Try preferred ESM first if specified
            if (!string.IsNullOrEmpty(preferredESM))
            {
                string preferredESMName = Path.GetFileNameWithoutExtension(preferredESM);
                string cachePath = CheckCacheDirectory(preferredESMName, normalizedFilename, modelFilename, searchFilename, searchFilenameLower);
                if (!string.IsNullOrEmpty(cachePath))
                    return cachePath;
            }
            
            // Check all ESM cache directories in load order
            foreach (string esmFilename in loadedESMs)
            {
                string esmName = Path.GetFileNameWithoutExtension(esmFilename);
                // Skip if we already checked this one
                if (!string.IsNullOrEmpty(preferredESM) && string.Equals(esmName, Path.GetFileNameWithoutExtension(preferredESM), StringComparison.OrdinalIgnoreCase))
                    continue;
                    
                string cachePath = CheckCacheDirectory(esmName, normalizedFilename, modelFilename, searchFilename, searchFilenameLower);
                if (!string.IsNullOrEmpty(cachePath))
                    return cachePath;
            }

            return null;
        }
        
        /// <summary>
        /// Helper method to check a single cache directory for a NIF file
        /// </summary>
        private string CheckCacheDirectory(string esmName, string normalizedFilename, string originalFilename, string searchFilename, string searchFilenameLower)
        {
            // Try exact match first (with normalized path separators)
            string cachePath = Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", esmName, normalizedFilename);
            cachePath = cachePath.Replace('\\', '/');
            if (System.IO.File.Exists(cachePath))
                return cachePath;

            // Try original filename as-is
            string originalPath = Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", esmName, originalFilename);
            originalPath = originalPath.Replace('\\', '/');
            if (System.IO.File.Exists(originalPath))
                return originalPath;

            // Case-insensitive search - this is the most reliable method
            string cacheDir = Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", esmName);
            cacheDir = cacheDir.Replace('\\', '/');
            if (System.IO.Directory.Exists(cacheDir))
            {
                // Get all NIF files (case-insensitive pattern matching)
                string[] files = System.IO.Directory.GetFiles(cacheDir, "*.nif", System.IO.SearchOption.TopDirectoryOnly);
                string[] filesUpper = System.IO.Directory.GetFiles(cacheDir, "*.NIF", System.IO.SearchOption.TopDirectoryOnly);
                
                // Combine both patterns (some systems might return different results)
                HashSet<string> allFiles = new HashSet<string>(files);
                foreach (string file in filesUpper)
                {
                    allFiles.Add(file);
                }
                
                foreach (string file in allFiles)
                {
                    string fileName = Path.GetFileName(file);
                    string fileNameLower = fileName.ToLowerInvariant();
                    
                    // Case-insensitive comparison
                    if (string.Equals(fileName, searchFilename, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(fileNameLower, searchFilenameLower, StringComparison.Ordinal))
                    {
                        return file;
                    }
                }
            }
            
            return null;
        }
    }
}

