using ESMSharp.TES3;
using ESMSharp.TES3.Records;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ESMSharp.TES3Terrain
{
    /// <summary>
    /// Manages CELL GameObjects with colliders and events
    /// Static class for global cell management
    /// </summary>
    public static class CellManager
    {
        private static Dictionary<string, GameObject> _cells = new Dictionary<string, GameObject>();
        private static Dictionary<string, RecordCell> _cellRecords = new Dictionary<string, RecordCell>();
        private static Transform _parent = null;
        private static Record[] _allRecords = null;
        private static string _esm = "";
        private static string _bsa = "";

        /// <summary>
        /// Creates cell GameObjects from CELL records (coroutine version)
        /// Only creates cells that have a matching LAND record
        /// Prioritizes cells near the player position
        /// Uses pre-registered cells from TESCelLibrary for faster lookups
        /// </summary>
        public static IEnumerator CreateCellsCoroutine(Record[] records, Transform parent = null, string esm = "", string bsa = "", Vector3? priorityPosition = null, int cellsPerFrame = 15)
        {
            _allRecords = records;
            _esm = esm;
            _bsa = bsa;

            // First, build a set of all cell coordinates that have LAND records
            // This ensures we only create cells where there's actual terrain data
            HashSet<(int x, int y)> landCellCoordinates = new HashSet<(int, int)>();
            
            foreach (Record rec in records)
            {
                RecordLand landRecord = rec as RecordLand;
                if (landRecord != null && landRecord.subRecords != null)
                {
                    foreach (SubRecords subrec in landRecord.subRecords)
                    {
                        if (subrec is SubRecordLandINTV intv)
                        {
                            int landCellX = (int)intv.CellX;
                            int landCellY = (int)intv.CellY;
                            landCellCoordinates.Add((landCellX, landCellY));
                        }
                    }
                }
            }
            
            UnityEngine.Debug.Log($"Found {landCellCoordinates.Count} cells with LAND records");

            // Create a parent GameObject for all cells
            GameObject cellsParent = new GameObject("Cells");
            if (parent != null)
            {
                cellsParent.transform.SetParent(parent);
            }
            _parent = cellsParent.transform;

            // Collect all cells to create with their metadata using TESCelLibrary (pre-registered during ESM parsing)
            List<(TESCelLibrary.CellEntry cellEntry, RecordCell cellRecord)> cellsToCreate = new List<(TESCelLibrary.CellEntry, RecordCell)>();

            // Use TESCelLibrary to get all cells (much faster than iterating through records)
            foreach (var cellEntry in TESCelLibrary.GetAllCells())
            {
                // Only create cells that have a matching LAND record (exterior cells only)
                // This ensures cells match exactly with the heightmap
                if (!cellEntry.IsInterior && !landCellCoordinates.Contains((cellEntry.GridX, cellEntry.GridY)))
                {
                    continue; // Skip cells without LAND records
                }

                // Get the cell record from the entry
                if (cellEntry.CellRecord != null)
                {
                    cellsToCreate.Add((cellEntry, cellEntry.CellRecord));
                }
            }

            // Get priority position (player position or default to origin)
            Vector3 priorityPos = priorityPosition ?? Vector3.zero;
            int priorityCellX = Mathf.FloorToInt(priorityPos.x / TESGlobals.CELL_SIZE);
            int priorityCellY = Mathf.FloorToInt(priorityPos.z / TESGlobals.CELL_SIZE);

            // Sort cells by distance from priority position (closest first)
            cellsToCreate = cellsToCreate.OrderBy(c =>
            {
                int dx = c.cellEntry.GridX - priorityCellX;
                int dy = c.cellEntry.GridY - priorityCellY;
                return dx * dx + dy * dy; // Distance squared (no need for sqrt)
            }).ToList();

            UnityEngine.Debug.Log($"Creating {cellsToCreate.Count} cells, prioritizing around cell ({priorityCellX}, {priorityCellY})");

            // Process cells in batches
            int cellCount = 0;
            for (int i = 0; i < cellsToCreate.Count; i++)
            {
                var (cellEntry, cellRecord) = cellsToCreate[i];

                // Create cell name
                string finalCellName;
                if (!string.IsNullOrEmpty(cellEntry.CellName))
                {
                    finalCellName = $"{cellEntry.CellName} ({cellEntry.GridX}, {cellEntry.GridY})";
                }
                else
                {
                    finalCellName = cellEntry.IsInterior ? $"Interior ({cellEntry.GridX}, {cellEntry.GridY})" : $"Cell ({cellEntry.GridX}, {cellEntry.GridY})";
                }

                // Create cell GameObject
                GameObject cellObj = CreateCell(finalCellName, cellEntry.GridX, cellEntry.GridY, cellEntry.IsInterior, cellRecord);
                if (cellObj != null)
                {
                    // Store cell by a unique key (grid coordinates)
                    string cellKey = cellEntry.GetKey();
                    if (!_cells.ContainsKey(cellKey))
                    {
                        _cells[cellKey] = cellObj;
                        _cellRecords[cellKey] = cellRecord;
                        cellCount++;
                    }
                }

                // Yield every N cells to prevent frame drops
                if ((i + 1) % cellsPerFrame == 0 || i == cellsToCreate.Count - 1)
                {
                    yield return null; // Yield control back to Unity
                }
            }

            UnityEngine.Debug.Log($"Created {cellCount} cell GameObjects (only cells with LAND records for exterior cells)");
        }

        /// <summary>
        /// Creates cell GameObjects from CELL records (legacy synchronous method)
        /// </summary>
        public static void CreateCells(Record[] records, Transform parent = null, string esm = "", string bsa = "")
        {
            _allRecords = records;
            _esm = esm;
            _bsa = bsa;
            // For backwards compatibility, use the old synchronous method
            // Note: This will block, but maintains compatibility
            CreateCellsSync(records, parent);
        }

        /// <summary>
        /// Synchronous cell creation (internal method for backwards compatibility)
        /// Uses pre-registered cells from TESCelLibrary for faster lookups
        /// </summary>
        private static void CreateCellsSync(Record[] records, Transform parent = null)
        {
            // First, build a set of all cell coordinates that have LAND records
            // This ensures we only create cells where there's actual terrain data
            HashSet<(int x, int y)> landCellCoordinates = new HashSet<(int, int)>();
            
            foreach (Record rec in records)
            {
                RecordLand landRecord = rec as RecordLand;
                if (landRecord != null && landRecord.subRecords != null)
                {
                    foreach (SubRecords subrec in landRecord.subRecords)
                    {
                        if (subrec is SubRecordLandINTV intv)
                        {
                            int landCellX = (int)intv.CellX;
                            int landCellY = (int)intv.CellY;
                            landCellCoordinates.Add((landCellX, landCellY));
                        }
                    }
                }
            }
            
            UnityEngine.Debug.Log($"Found {landCellCoordinates.Count} cells with LAND records");

            // Create a parent GameObject for all cells
            GameObject cellsParent = new GameObject("Cells");
            if (parent != null)
            {
                cellsParent.transform.SetParent(parent);
            }
            _parent = cellsParent.transform;
            
            int cellCount = 0;

            // Use TESCelLibrary to get all cells (much faster than iterating through records)
            foreach (var cellEntry in TESCelLibrary.GetAllCells())
            {
                // Only create cells that have a matching LAND record (exterior cells only)
                // This ensures cells match exactly with the heightmap
                if (!cellEntry.IsInterior && !landCellCoordinates.Contains((cellEntry.GridX, cellEntry.GridY)))
                {
                    continue; // Skip cells without LAND records
                }

                // Create cell name
                string finalCellName;
                if (!string.IsNullOrEmpty(cellEntry.CellName))
                {
                    finalCellName = $"{cellEntry.CellName} ({cellEntry.GridX}, {cellEntry.GridY})";
                }
                else
                {
                    finalCellName = cellEntry.IsInterior ? $"Interior ({cellEntry.GridX}, {cellEntry.GridY})" : $"Cell ({cellEntry.GridX}, {cellEntry.GridY})";
                }

                // Create cell GameObject
                GameObject cellObj = CreateCell(finalCellName, cellEntry.GridX, cellEntry.GridY, cellEntry.IsInterior, cellEntry.CellRecord);
                if (cellObj != null)
                {
                    // Store cell by a unique key (grid coordinates)
                    string cellKey = cellEntry.GetKey();
                    if (!_cells.ContainsKey(cellKey))
                    {
                        _cells[cellKey] = cellObj;
                        _cellRecords[cellKey] = cellEntry.CellRecord;
                        cellCount++;
                    }
                }
            }

            UnityEngine.Debug.Log($"Created {cellCount} cell GameObjects (only cells with LAND records for exterior cells)");
        }

        /// <summary>
        /// Gets the VHGT height offset for a given cell from the Land records
        /// </summary>
        private static float GetCellVHGTHeight(int cellGridX, int cellGridY, Record[] allRecords)
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
        /// Creates a single cell GameObject with collider
        /// </summary>
        private static GameObject CreateCell(string cellName, int gridX, int gridY, bool isInterior, RecordCell cellRecord)
        {
            GameObject cellObj = new GameObject(cellName);
            
            // Add Cell component for events
            Cell cellComponent = cellObj.AddComponent<Cell>();
            cellComponent.gridX = gridX;
            cellComponent.gridY = gridY;
            cellComponent.isInterior = isInterior;

            // Calculate VHGT height for this cell (cache it for efficiency)
            float vhgtHeight = 0f;
            if (!isInterior && _allRecords != null)
            {
                vhgtHeight = GetCellVHGTHeight(gridX, gridY, _allRecords);
            }

            // Add TESCell component for static generation
            TESCell tesCell = cellObj.AddComponent<TESCell>();
            tesCell.SetCell(cellRecord, _allRecords, _esm, _bsa, vhgtHeight);

            // Add box collider (64x64 units per cell in Morrowind game world)
            BoxCollider collider = cellObj.AddComponent<BoxCollider>();
            collider.size = new Vector3(TESGlobals.CELL_SIZE, 1000f, TESGlobals.CELL_SIZE); // Tall enough to catch player at any height
            collider.isTrigger = true; // Pass-through collider for events
            collider.center = new Vector3(TESGlobals.CELL_SIZE * 0.5f, 500f, TESGlobals.CELL_SIZE * 0.5f); // Center of cell (half of CELL_SIZE)

            // Position cell at grid coordinates
            // Morrowind cells are 64 units apart in game world (65 is only for heightmap generation)
            cellObj.transform.position = new Vector3(gridX * TESGlobals.CELL_SIZE, 0f, gridY * TESGlobals.CELL_SIZE);

            // Parent to parent transform if provided
            if (_parent != null)
            {
                cellObj.transform.SetParent(_parent);
            }

            return cellObj;
        }

        /// <summary>
        /// Gets a cell GameObject by grid coordinates
        /// </summary>
        public static GameObject GetCell(int gridX, int gridY)
        {
            string cellKey = $"{gridX}_{gridY}";
            if (_cells.ContainsKey(cellKey))
            {
                return _cells[cellKey];
            }
            return null;
        }

        /// <summary>
        /// Gets all cell GameObjects
        /// </summary>
        public static Dictionary<string, GameObject> GetAllCells()
        {
            return _cells;
        }

        /// <summary>
        /// Gets all records (for STAT lookup)
        /// </summary>
        public static Record[] GetAllRecords()
        {
            return _allRecords;
        }

        /// <summary>
        /// Gets ESM filename
        /// </summary>
        public static string GetESM()
        {
            return _esm;
        }

        /// <summary>
        /// Gets BSA filename
        /// </summary>
        public static string GetBSA()
        {
            return _bsa;
        }
        
        /// <summary>
        /// Clears all cells from the manager
        /// </summary>
        public static void Clear()
        {
            _cells.Clear();
            _cellRecords.Clear();
            _parent = null;
            _allRecords = null;
            _esm = "";
            _bsa = "";
        }
    }

    /// <summary>
    /// Component attached to cell GameObjects for events
    /// </summary>
    public class Cell : MonoBehaviour
    {
        public int gridX = 0;
        public int gridY = 0;
        public bool isInterior = false;

        // Events
        public event Action<GameObject> OnCellEnter;
        public event Action<GameObject> OnCellExit;

        private void OnTriggerEnter(Collider other)
        {
            // Only trigger for player or tagged objects
            if (other.CompareTag("Player") || other.gameObject.name.Contains("Player"))
            {
                OnCellEnter?.Invoke(other.gameObject);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            // Only trigger for player or tagged objects
            if (other.CompareTag("Player") || other.gameObject.name.Contains("Player"))
            {
                OnCellExit?.Invoke(other.gameObject);
            }
        }
    }
}

