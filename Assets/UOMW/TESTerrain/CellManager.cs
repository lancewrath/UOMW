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
    /// </summary>
    public class CellManager
    {
        private Dictionary<string, GameObject> _cells = new Dictionary<string, GameObject>();
        private Dictionary<string, RecordCell> _cellRecords = new Dictionary<string, RecordCell>();
        private Transform _parent = null;
        private Record[] _allRecords = null;
        private string _esm = "";
        private string _bsa = "";

        /// <summary>
        /// Creates cell GameObjects from CELL records (coroutine version)
        /// Only creates cells that have a matching LAND record
        /// Prioritizes cells near the player position
        /// </summary>
        public IEnumerator CreateCellsCoroutine(Record[] records, Transform parent = null, string esm = "", string bsa = "", Vector3? priorityPosition = null, int cellsPerFrame = 15)
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

            // Collect all cells to create with their metadata
            List<(RecordCell cell, int gridX, int gridY, bool isInterior, string cellName)> cellsToCreate = new List<(RecordCell, int, int, bool, string)>();

            foreach (Record rec in records)
            {
                RecordCell cell = rec as RecordCell;
                if (cell == null)
                    continue;

                if (cell.subRecords == null)
                    continue;

                // Extract cell data
                string cellName = null;
                int gridX = 0;
                int gridY = 0;
                bool isInterior = false;

                foreach (SubRecords subrec in cell.subRecords)
                {
                    if (subrec == null)
                        continue;

                    if (subrec is SubRecordCellRGNN)
                    {
                        SubRecordCellRGNN rgnn = subrec as SubRecordCellRGNN;
                        cellName = rgnn.name;
                    }
                    else if (subrec is SubRecordCellDATA)
                    {
                        SubRecordCellDATA data = subrec as SubRecordCellDATA;
                        gridX = data.gridX;
                        gridY = data.gridY;
                        // Check if interior (flag 0x01)
                        isInterior = (data.flags & 0x01) != 0;
                    }
                }

                // Only create cells that have a matching LAND record (exterior cells only)
                // This ensures cells match exactly with the heightmap
                if (!isInterior && !landCellCoordinates.Contains((gridX, gridY)))
                {
                    continue; // Skip cells without LAND records
                }

                cellsToCreate.Add((cell, gridX, gridY, isInterior, cellName));
            }

            // Get priority position (player position or default to origin)
            Vector3 priorityPos = priorityPosition ?? Vector3.zero;
            int priorityCellX = Mathf.FloorToInt(priorityPos.x / TESGlobals.CELL_SIZE);
            int priorityCellY = Mathf.FloorToInt(priorityPos.z / TESGlobals.CELL_SIZE);

            // Sort cells by distance from priority position (closest first)
            cellsToCreate = cellsToCreate.OrderBy(c =>
            {
                int dx = c.gridX - priorityCellX;
                int dy = c.gridY - priorityCellY;
                return dx * dx + dy * dy; // Distance squared (no need for sqrt)
            }).ToList();

            UnityEngine.Debug.Log($"Creating {cellsToCreate.Count} cells, prioritizing around cell ({priorityCellX}, {priorityCellY})");

            // Process cells in batches
            int cellCount = 0;
            for (int i = 0; i < cellsToCreate.Count; i++)
            {
                var (cell, gridX, gridY, isInterior, cellName) = cellsToCreate[i];

                // Create cell name
                string finalCellName;
                if (!string.IsNullOrEmpty(cellName))
                {
                    finalCellName = $"{cellName} ({gridX}, {gridY})";
                }
                else
                {
                    finalCellName = isInterior ? $"Interior ({gridX}, {gridY})" : $"Cell ({gridX}, {gridY})";
                }

                // Create cell GameObject
                GameObject cellObj = CreateCell(finalCellName, gridX, gridY, isInterior, cell);
                if (cellObj != null)
                {
                    // Store cell by a unique key (grid coordinates)
                    string cellKey = $"{gridX}_{gridY}";
                    if (!_cells.ContainsKey(cellKey))
                    {
                        _cells[cellKey] = cellObj;
                        _cellRecords[cellKey] = cell;
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
        public void CreateCells(Record[] records, Transform parent = null, string esm = "", string bsa = "")
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
        /// </summary>
        private void CreateCellsSync(Record[] records, Transform parent = null)
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

            foreach (Record rec in records)
            {
                RecordCell cell = rec as RecordCell;
                if (cell == null)
                    continue;

                if (cell.subRecords == null)
                    continue;

                // Extract cell data
                string cellName = null;
                int gridX = 0;
                int gridY = 0;
                bool isInterior = false;

                foreach (SubRecords subrec in cell.subRecords)
                {
                    if (subrec == null)
                        continue;

                    if (subrec is SubRecordCellRGNN)
                    {
                        SubRecordCellRGNN rgnn = subrec as SubRecordCellRGNN;
                        cellName = rgnn.name;
                    }
                    else if (subrec is SubRecordCellDATA)
                    {
                        SubRecordCellDATA data = subrec as SubRecordCellDATA;
                        gridX = data.gridX;
                        gridY = data.gridY;
                        // Check if interior (flag 0x01)
                        isInterior = (data.flags & 0x01) != 0;
                    }
                }

                // Only create cells that have a matching LAND record (exterior cells only)
                // This ensures cells match exactly with the heightmap
                if (!isInterior && !landCellCoordinates.Contains((gridX, gridY)))
                {
                    continue; // Skip cells without LAND records
                }

                // Create cell name
                string finalCellName;
                if (!string.IsNullOrEmpty(cellName))
                {
                    finalCellName = $"{cellName} ({gridX}, {gridY})";
                }
                else
                {
                    finalCellName = isInterior ? $"Interior ({gridX}, {gridY})" : $"Cell ({gridX}, {gridY})";
                }

                // Create cell GameObject
                GameObject cellObj = CreateCell(finalCellName, gridX, gridY, isInterior, cell);
                if (cellObj != null)
                {
                    // Store cell by a unique key (grid coordinates)
                    string cellKey = $"{gridX}_{gridY}";
                    if (!_cells.ContainsKey(cellKey))
                    {
                        _cells[cellKey] = cellObj;
                        _cellRecords[cellKey] = cell;
                        cellCount++;
                    }
                }
            }

            UnityEngine.Debug.Log($"Created {cellCount} cell GameObjects (only cells with LAND records for exterior cells)");
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
        /// Creates a single cell GameObject with collider
        /// </summary>
        private GameObject CreateCell(string cellName, int gridX, int gridY, bool isInterior, RecordCell cellRecord)
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
            tesCell.SetCell(cellRecord, this, _allRecords, _esm, _bsa, vhgtHeight);

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
        public GameObject GetCell(int gridX, int gridY)
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
        public Dictionary<string, GameObject> GetAllCells()
        {
            return _cells;
        }

        /// <summary>
        /// Gets all records (for STAT lookup)
        /// </summary>
        public Record[] GetAllRecords()
        {
            return _allRecords;
        }

        /// <summary>
        /// Gets ESM filename
        /// </summary>
        public string GetESM()
        {
            return _esm;
        }

        /// <summary>
        /// Gets BSA filename
        /// </summary>
        public string GetBSA()
        {
            return _bsa;
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

