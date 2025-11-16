using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ESMSharp.TES3
{
    /// <summary>
    /// Global door library for managing door records parsed from ESM files.
    /// Stores door information including model filenames and metadata.
    /// Doors can be accessed by ID like a dictionary for easy runtime door creation.
    /// </summary>
    public static class TESDoorManager
    {
        /// <summary>
        /// Door entry containing all relevant door information
        /// </summary>
        public class DoorEntry
        {
            public string DoorId { get; set; }                    // Door ID (from NAME subrecord)
            public string DisplayName { get; set; }               // Display name (from FNAM)
            public string ModelFilename { get; set; }              // Model filename (from MODL)
            public string ScriptName { get; set; }                 // Script name (from SCRI)
            public string OpenSound { get; set; }                  // Open sound (from SNAM)
            public string CloseSound { get; set; }                 // Close sound (from ANAM)
            public string SourceESM { get; set; }                  // ESM file this door came from
            
            // Reference to loaded model (via TESNifLibrary)
            public TESNifLibrary.NifEntry ModelEntry { get; set; }
            
            public DoorEntry(string doorId, string sourceESM = null)
            {
                DoorId = doorId;
                SourceESM = sourceESM;
            }
        }
        
        // Dictionary for fast lookups by door ID (case-insensitive)
        private static Dictionary<string, DoorEntry> _doorsById = new Dictionary<string, DoorEntry>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>
        /// Adds a door to the library. If a door with the same ID already exists, it will be overwritten.
        /// This is called automatically when doors are parsed from ESM files.
        /// </summary>
        /// <param name="doorRecord">The RecordDoor to extract data from</param>
        /// <param name="sourceESM">Optional ESM file this door came from</param>
        /// <returns>The door entry (newly created or updated)</returns>
        public static DoorEntry AddDoor(RecordDoor doorRecord, string sourceESM = null)
        {
            if (doorRecord == null)
            {
                Debug.LogWarning("TESDoorManager: Attempted to add null door record");
                return null;
            }
            
            // Extract door ID from NAME subrecord
            string doorId = null;
            foreach (var subrec in doorRecord.subRecords)
            {
                if (subrec is SubRecordDoorNAME nameRec)
                {
                    doorId = nameRec.name;
                    break;
                }
            }
            
            if (string.IsNullOrEmpty(doorId))
            {
                Debug.LogWarning("TESDoorManager: Attempted to add door without ID");
                return null;
            }
            
            // Clean door ID - remove all null characters and whitespace
            // This ensures lookups work even if the NAME field has trailing nulls
            doorId = doorId.TrimEnd('\0', ' ', '\t', '\r', '\n');
            doorId = doorId.Replace("\0", "");
            doorId = new string(doorId.Where(c => c != '\0').ToArray()).Trim();
            
            if (string.IsNullOrEmpty(doorId))
            {
                Debug.LogWarning("TESDoorManager: Door ID was empty after cleaning");
                return null;
            }
            
            // Check if door already exists (using cleaned ID)
            bool isUpdate = _doorsById.ContainsKey(doorId);
            
            // Create or get existing entry
            DoorEntry entry = isUpdate ? _doorsById[doorId] : new DoorEntry(doorId, sourceESM);
            
            // Extract all subrecord data
            foreach (var subrec in doorRecord.subRecords)
            {
                if (subrec is SubRecordDoorFNAM fnam)
                    entry.DisplayName = fnam.name;
                else if (subrec is SubRecordDoorMODL modl)
                {
                    // Clean model filename to remove null characters
                    string cleanedModel = modl.model?.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    cleanedModel = cleanedModel?.Replace("\0", "");
                    cleanedModel = string.IsNullOrEmpty(cleanedModel) ? null : new string(cleanedModel.Where(c => c != '\0').ToArray()).Trim();
                    
                    // Normalize path separators (replace backslashes with forward slashes) for consistency
                    if (!string.IsNullOrEmpty(cleanedModel))
                    {
                        cleanedModel = cleanedModel.Replace('\\', '/');
                    }
                    
                    entry.ModelFilename = cleanedModel;
                }
                else if (subrec is SubRecordDoorSCRI scri)
                    entry.ScriptName = scri.name;
                else if (subrec is SubRecordDoorSNAM snam)
                    entry.OpenSound = snam.name;
                else if (subrec is SubRecordDoorANAM anam)
                    entry.CloseSound = anam.name;
            }
            
            // Try to get model reference from TESNifLibrary
            if (!string.IsNullOrEmpty(entry.ModelFilename))
            {
                string sanitizedFilename = SanitizePath(entry.ModelFilename);
                string baseFilename = System.IO.Path.GetFileNameWithoutExtension(sanitizedFilename);
                entry.ModelEntry = TESNifLibrary.GetModelByBaseFilename(baseFilename);
            }
            
            // Add/update in dictionary
            _doorsById[doorId] = entry;
            
            return entry;
        }
        
        /// <summary>
        /// Gets a door by ID. Returns null if not found.
        /// This is the primary method for accessing doors at runtime.
        /// </summary>
        /// <param name="doorId">Door ID (case-insensitive)</param>
        /// <returns>The door entry if found, null otherwise</returns>
        public static DoorEntry GetDoor(string doorId)
        {
            if (string.IsNullOrEmpty(doorId))
                return null;
                
            // Clean ID before lookup
            doorId = doorId.TrimEnd('\0', ' ', '\t', '\r', '\n');
            doorId = doorId.Replace("\0", "");
            doorId = new string(doorId.Where(c => c != '\0').ToArray()).Trim();
                
            _doorsById.TryGetValue(doorId, out DoorEntry entry);
            return entry;
        }
        
        /// <summary>
        /// Checks if a door exists in the library.
        /// </summary>
        /// <param name="doorId">Door ID (case-insensitive)</param>
        /// <returns>True if the door exists, false otherwise</returns>
        public static bool HasDoor(string doorId)
        {
            if (string.IsNullOrEmpty(doorId))
                return false;
                
            // Clean ID before lookup
            doorId = doorId.TrimEnd('\0', ' ', '\t', '\r', '\n');
            doorId = doorId.Replace("\0", "");
            doorId = new string(doorId.Where(c => c != '\0').ToArray()).Trim();
                
            return _doorsById.ContainsKey(doorId);
        }
        
        /// <summary>
        /// Gets all door entries in the library.
        /// </summary>
        /// <returns>Collection of all door entries</returns>
        public static IEnumerable<DoorEntry> GetAllDoors()
        {
            return _doorsById.Values;
        }
        
        /// <summary>
        /// Gets all door IDs in the library.
        /// </summary>
        /// <returns>Array of door IDs</returns>
        public static string[] GetAllDoorIds()
        {
            return _doorsById.Keys.ToArray();
        }
        
        /// <summary>
        /// Clears all doors from the library.
        /// </summary>
        public static void Clear()
        {
            _doorsById.Clear();
            Debug.Log("TESDoorManager: Cleared all doors");
        }
        
        /// <summary>
        /// Gets the total number of doors in the library.
        /// </summary>
        public static int DoorCount => _doorsById.Count;
        
        /// <summary>
        /// Sanitizes a file path by removing invalid path characters.
        /// This prevents "Illegal characters in path" errors when using System.IO.Path methods.
        /// </summary>
        /// <param name="path">The path string to sanitize</param>
        /// <returns>The sanitized path with invalid characters replaced with underscores</returns>
        private static string SanitizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            
            // First, do a basic sanitization to replace common problematic characters
            string sanitized = path.Replace('<', '_')
                                  .Replace('>', '_')
                                  .Replace(':', '_')
                                  .Replace('"', '_')
                                  .Replace('|', '_')
                                  .Replace('?', '_')
                                  .Replace('*', '_')
                                  .Replace('\0', '_');
            
            try
            {
                // Get invalid path characters (this should be safe now after basic sanitization)
                char[] invalidChars = System.IO.Path.GetInvalidPathChars();
                
                // Replace invalid characters with underscores
                foreach (char c in invalidChars)
                {
                    sanitized = sanitized.Replace(c, '_');
                }
                
                // Also replace invalid filename characters (which are a subset of path chars)
                char[] invalidFileNameChars = System.IO.Path.GetInvalidFileNameChars();
                foreach (char c in invalidFileNameChars)
                {
                    sanitized = sanitized.Replace(c, '_');
                }
            }
            catch (System.Exception)
            {
                // If Path.GetInvalidPathChars() still fails, the basic sanitization above should be sufficient
            }
            
            return sanitized;
        }
    }
}
