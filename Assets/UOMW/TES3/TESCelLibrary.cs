using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ESMSharp.TES3
{
    /// <summary>
    /// Global cell library for managing cell records parsed from ESM files.
    /// Stores cell information including grid coordinates, names, and metadata.
    /// Cells can be accessed by grid coordinates or name for easy runtime lookups.
    /// </summary>
    public static class TESCelLibrary
    {
        /// <summary>
        /// Cell entry containing all relevant cell information
        /// </summary>
        public class CellEntry
        {
            public string CellName { get; set; }                  // Cell name (from NAME or RGNN subrecord)
            public int GridX { get; set; }                        // Grid X coordinate (from DATA subrecord)
            public int GridY { get; set; }                        // Grid Y coordinate (from DATA subrecord)
            public bool IsInterior { get; set; }                  // Whether this is an interior cell (from DATA flags)
            public string SourceESM { get; set; }                // ESM file this cell came from
            public RecordCell CellRecord { get; set; }             // Reference to the original cell record
            
            public CellEntry(int gridX, int gridY, string sourceESM = null)
            {
                GridX = gridX;
                GridY = gridY;
                SourceESM = sourceESM;
            }
            
            /// <summary>
            /// Gets a unique key for this cell (grid coordinates)
            /// </summary>
            public string GetKey()
            {
                return $"{GridX}_{GridY}";
            }
        }
        
        // Dictionary for fast lookups by grid coordinates (key format: "X_Y")
        private static Dictionary<string, CellEntry> _cellsByKey = new Dictionary<string, CellEntry>(StringComparer.OrdinalIgnoreCase);
        
        // Dictionary for fast lookups by cell name (for interior cells)
        private static Dictionary<string, CellEntry> _cellsByName = new Dictionary<string, CellEntry>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>
        /// Adds a cell to the library. If a cell with the same coordinates already exists, it will be overwritten.
        /// This is called automatically when cells are parsed from ESM files.
        /// </summary>
        /// <param name="cellRecord">The RecordCell to extract data from</param>
        /// <param name="sourceESM">Optional ESM file this cell came from</param>
        /// <returns>The cell entry (newly created or updated)</returns>
        public static CellEntry AddCell(RecordCell cellRecord, string sourceESM = null)
        {
            if (cellRecord == null || cellRecord.subRecords == null)
            {
                Debug.LogWarning("TESCelLibrary: Attempted to add null cell record or record with no subrecords");
                return null;
            }
            
            // Extract cell data from subrecords
            string cellName = null;
            int gridX = 0;
            int gridY = 0;
            bool isInterior = false;
            
            foreach (var subrec in cellRecord.subRecords)
            {
                if (subrec == null)
                    continue;
                
                if (subrec is SubRecordCellNAME nameRec)
                {
                    // First NAME is cell name (for interior cells)
                    if (string.IsNullOrEmpty(cellName))
                    {
                        cellName = nameRec.name;
                    }
                }
                else if (subrec is SubRecordCellRGNN rgnnRec)
                {
                    // RGNN is region name (alternative cell name)
                    if (string.IsNullOrEmpty(cellName))
                    {
                        cellName = rgnnRec.name;
                    }
                }
                else if (subrec is SubRecordCellDATA dataRec)
                {
                    gridX = dataRec.gridX;
                    gridY = dataRec.gridY;
                    // Check if interior (flag 0x01)
                    isInterior = (dataRec.flags & 0x01) != 0;
                }
            }
            
            // Clean cell name if present
            if (!string.IsNullOrEmpty(cellName))
            {
                cellName = cellName.TrimEnd('\0', ' ', '\t', '\r', '\n');
                cellName = cellName.Replace("\0", "");
                cellName = new string(cellName.Where(c => c != '\0').ToArray()).Trim();
            }
            
            // Create cell key from grid coordinates
            string cellKey = $"{gridX}_{gridY}";
            
            // Check if cell already exists (using grid coordinates)
            bool isUpdate = _cellsByKey.ContainsKey(cellKey);
            
            // Create or get existing entry
            CellEntry entry = isUpdate ? _cellsByKey[cellKey] : new CellEntry(gridX, gridY, sourceESM);
            
            // Update cell data (later ESM files can override)
            if (!string.IsNullOrEmpty(cellName))
            {
                entry.CellName = cellName;
            }
            entry.IsInterior = isInterior;
            entry.CellRecord = cellRecord;
            
            // Add/update in dictionaries
            _cellsByKey[cellKey] = entry;
            
            // Also index by name if cell has a name (for interior cells)
            if (!string.IsNullOrEmpty(entry.CellName))
            {
                _cellsByName[entry.CellName] = entry;
            }
            
            return entry;
        }
        
        /// <summary>
        /// Gets a cell by grid coordinates. Returns null if not found.
        /// </summary>
        /// <param name="gridX">Grid X coordinate</param>
        /// <param name="gridY">Grid Y coordinate</param>
        /// <returns>The cell entry if found, null otherwise</returns>
        public static CellEntry GetCell(int gridX, int gridY)
        {
            string cellKey = $"{gridX}_{gridY}";
            _cellsByKey.TryGetValue(cellKey, out CellEntry entry);
            return entry;
        }
        
        /// <summary>
        /// Gets a cell by name. Returns null if not found.
        /// Useful for interior cells which are identified by name rather than coordinates.
        /// </summary>
        /// <param name="cellName">Cell name (case-insensitive)</param>
        /// <returns>The cell entry if found, null otherwise</returns>
        public static CellEntry GetCellByName(string cellName)
        {
            if (string.IsNullOrEmpty(cellName))
                return null;
                
            // Clean cell name before lookup
            cellName = cellName.TrimEnd('\0', ' ', '\t', '\r', '\n');
            cellName = cellName.Replace("\0", "");
            cellName = new string(cellName.Where(c => c != '\0').ToArray()).Trim();
                
            _cellsByName.TryGetValue(cellName, out CellEntry entry);
            return entry;
        }
        
        /// <summary>
        /// Gets all cell entries in the library.
        /// </summary>
        /// <returns>Collection of all cell entries</returns>
        public static IEnumerable<CellEntry> GetAllCells()
        {
            return _cellsByKey.Values;
        }
        
        /// <summary>
        /// Gets all exterior cells (non-interior cells).
        /// </summary>
        /// <returns>Collection of exterior cell entries</returns>
        public static IEnumerable<CellEntry> GetExteriorCells()
        {
            return _cellsByKey.Values.Where(c => !c.IsInterior);
        }
        
        /// <summary>
        /// Gets all interior cells.
        /// </summary>
        /// <returns>Collection of interior cell entries</returns>
        public static IEnumerable<CellEntry> GetInteriorCells()
        {
            return _cellsByKey.Values.Where(c => c.IsInterior);
        }
        
        /// <summary>
        /// Checks if a cell exists at the specified grid coordinates.
        /// </summary>
        /// <param name="gridX">Grid X coordinate</param>
        /// <param name="gridY">Grid Y coordinate</param>
        /// <returns>True if the cell exists, false otherwise</returns>
        public static bool HasCell(int gridX, int gridY)
        {
            string cellKey = $"{gridX}_{gridY}";
            return _cellsByKey.ContainsKey(cellKey);
        }
        
        /// <summary>
        /// Clears all cells from the library.
        /// </summary>
        public static void Clear()
        {
            _cellsByKey.Clear();
            _cellsByName.Clear();
            Debug.Log("TESCelLibrary: Cleared all cells");
        }
        
        /// <summary>
        /// Gets the total number of cells in the library.
        /// </summary>
        public static int CellCount => _cellsByKey.Count;
    }
}

