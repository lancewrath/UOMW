using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ESMSharp.TES3
{
    /// <summary>
    /// Global creature library for managing creature records parsed from ESM files.
    /// Stores creature information including stats, models, inventory, and other properties.
    /// Creatures can be accessed by ID like a dictionary for easy runtime creature creation.
    /// </summary>
    public static class TESCreatureManager
    {
        /// <summary>
        /// Creature entry containing all relevant creature information
        /// </summary>
        public class CreatureEntry
        {
            public string CreatureId { get; set; }                    // Creature ID (from NAME subrecord)
            public string DisplayName { get; set; }                     // Display name (from FNAM)
            public string ModelFilename { get; set; }                   // Model filename (from MODL)
            public string ClassName { get; set; }                       // Class name (from CNAM)
            public string RaceName { get; set; }                        // Race name (from RNAM)
            public string ScriptName { get; set; }                       // Script name (from SCRI)
            public string DeathName { get; set; }                       // Death name (from DNAM)
            public CREATUREDATA CreatureData { get; set; }              // Creature stats data (from NPDT)
            public uint Flags { get; set; }                             // Creature flags (from FLAG)
            public float Scale { get; set; }                             // Scale (from XSCL)
            public List<CreatureObject> Inventory { get; set; }          // Inventory items (from NPCO)
            public string Spells { get; set; }                          // Spells list (from NPCS)
            public string SourceESM { get; set; }                       // ESM file this creature came from
            
            // Death data (from DODT)
            public float DeathX { get; set; }
            public float DeathY { get; set; }
            public float DeathZ { get; set; }
            public float DeathRoll { get; set; }
            public float DeathYaw { get; set; }
            public float DeathPitch { get; set; }
            
            // Reference to loaded model (via TESNifLibrary)
            public TESNifLibrary.NifEntry ModelEntry { get; set; }
            
            public CreatureEntry(string creatureId, string sourceESM = null)
            {
                CreatureId = creatureId;
                SourceESM = sourceESM;
                Inventory = new List<CreatureObject>();
                Scale = 1.0f;
            }
        }
        
        /// <summary>
        /// Creature inventory object (item with count)
        /// </summary>
        public class CreatureObject
        {
            public string ObjectName { get; set; }    // Item/object ID
            public int Count { get; set; }             // Quantity
            
            public CreatureObject(string objectName, int count)
            {
                ObjectName = objectName;
                Count = count;
            }
        }
        
        // Dictionary for fast lookups by creature ID (case-insensitive)
        private static Dictionary<string, CreatureEntry> _creaturesById = new Dictionary<string, CreatureEntry>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>
        /// Adds a creature to the library. If a creature with the same ID already exists, it will be overwritten.
        /// This is called automatically when creatures are parsed from ESM files.
        /// </summary>
        /// <param name="creatureRecord">The RecordCreature to extract data from</param>
        /// <param name="sourceESM">Optional ESM file this creature came from</param>
        /// <returns>The creature entry (newly created or updated)</returns>
        public static CreatureEntry AddCreature(RecordCreature creatureRecord, string sourceESM = null)
        {
            if (creatureRecord == null)
            {
                Debug.LogWarning("TESCreatureManager: Attempted to add null creature record");
                return null;
            }
            
            // Extract creature ID from NAME subrecord
            string creatureId = null;
            foreach (var subrec in creatureRecord.subRecords)
            {
                if (subrec is SubRecordCREANAME nameRec)
                {
                    creatureId = nameRec.name;
                    break;
                }
            }
            
            if (string.IsNullOrEmpty(creatureId))
            {
                Debug.LogWarning("TESCreatureManager: Attempted to add creature without ID");
                return null;
            }
            
            // Clean creature ID - remove all null characters and whitespace
            // This ensures lookups work even if the NAME field has trailing nulls
            creatureId = creatureId.TrimEnd('\0', ' ', '\t', '\r', '\n');
            creatureId = creatureId.Replace("\0", "");
            creatureId = new string(creatureId.Where(c => c != '\0').ToArray()).Trim();
            
            if (string.IsNullOrEmpty(creatureId))
            {
                Debug.LogWarning("TESCreatureManager: Creature ID was empty after cleaning");
                return null;
            }
            
            // Check if creature already exists (using cleaned ID)
            bool isUpdate = _creaturesById.ContainsKey(creatureId);
            
            // Create or get existing entry
            CreatureEntry entry = isUpdate ? _creaturesById[creatureId] : new CreatureEntry(creatureId, sourceESM);
            
            // Extract all subrecord data
            foreach (var subrec in creatureRecord.subRecords)
            {
                if (subrec is SubRecordCREAFNAM fnam)
                    entry.DisplayName = fnam.name;
                else if (subrec is SubRecordCREAMODL modl)
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
                else if (subrec is SubRecordCREACNAM cnam)
                    entry.ClassName = cnam.name;
                else if (subrec is SubRecordCREARNAM rnam)
                    entry.RaceName = rnam.name;
                else if (subrec is SubRecordCREASCRI scri)
                    entry.ScriptName = scri.name;
                else if (subrec is SubRecordCREADNAM dnam)
                    entry.DeathName = dnam.name;
                else if (subrec is SubRecordCREANPDT npdt)
                    entry.CreatureData = npdt.data;
                else if (subrec is SubRecordCREAFLAG flag)
                    entry.Flags = flag.flag;
                else if (subrec is SubRecordCREAXSCL xscl)
                    entry.Scale = xscl.scale;
                else if (subrec is SubRecordCREANPCO npco)
                    entry.Inventory.Add(new CreatureObject(npco.objectName, npco.count));
                else if (subrec is SubRecordCREANPCS npcs)
                    entry.Spells = npcs.spells;
                else if (subrec is SubRecordCREADODT dodt)
                {
                    entry.DeathX = dodt.x;
                    entry.DeathY = dodt.y;
                    entry.DeathZ = dodt.z;
                    entry.DeathRoll = dodt.roll;
                    entry.DeathYaw = dodt.yaw;
                    entry.DeathPitch = dodt.pitch;
                }
            }
            
            // Try to get model reference from TESNifLibrary
            if (!string.IsNullOrEmpty(entry.ModelFilename))
            {
                // Sanitize filename before using Path methods to avoid "Illegal characters in path" errors
                string sanitizedFilename = SanitizePath(entry.ModelFilename);
                string baseFilename = System.IO.Path.GetFileNameWithoutExtension(sanitizedFilename);
                entry.ModelEntry = TESNifLibrary.GetModelByBaseFilename(baseFilename);
            }
            
            // Add/update in dictionary
            _creaturesById[creatureId] = entry;
            
            //Debug.Log($"TESCreatureManager: {(isUpdate ? "Updated" : "Added")} creature '{creatureId}' (from {sourceESM ?? "unknown"})");
            
            return entry;
        }
        
        /// <summary>
        /// Gets a creature by ID. Returns null if not found.
        /// This is the primary method for accessing creatures at runtime.
        /// </summary>
        /// <param name="creatureId">Creature ID (case-insensitive)</param>
        /// <returns>The creature entry if found, null otherwise</returns>
        public static CreatureEntry GetCreature(string creatureId)
        {
            if (string.IsNullOrEmpty(creatureId))
                return null;
                
            _creaturesById.TryGetValue(creatureId, out CreatureEntry entry);
            return entry;
        }
        
        /// <summary>
        /// Checks if a creature exists in the library.
        /// </summary>
        /// <param name="creatureId">Creature ID (case-insensitive)</param>
        /// <returns>True if the creature exists, false otherwise</returns>
        public static bool HasCreature(string creatureId)
        {
            if (string.IsNullOrEmpty(creatureId))
                return false;
                
            return _creaturesById.ContainsKey(creatureId);
        }
        
        /// <summary>
        /// Gets all creature entries in the library.
        /// </summary>
        /// <returns>Collection of all creature entries</returns>
        public static IEnumerable<CreatureEntry> GetAllCreatures()
        {
            return _creaturesById.Values;
        }
        
        /// <summary>
        /// Gets all creature IDs in the library.
        /// </summary>
        /// <returns>Array of creature IDs</returns>
        public static string[] GetAllCreatureIds()
        {
            return _creaturesById.Keys.ToArray();
        }
        
        /// <summary>
        /// Clears all creatures from the library.
        /// </summary>
        public static void Clear()
        {
            _creaturesById.Clear();
            Debug.Log("TESCreatureManager: Cleared all creatures");
        }
        
        /// <summary>
        /// Gets the total number of creatures in the library.
        /// </summary>
        public static int Count => _creaturesById.Count;
        
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
            // This prevents exceptions when calling Path.GetInvalidPathChars() on an already-invalid path
            string sanitized = path.Replace('<', '_')
                                  .Replace('>', '_')
                                  .Replace(':', '_')
                                  .Replace('"', '_')
                                  .Replace('|', '_')
                                  .Replace('?', '_')
                                  .Replace('*', '_')
                                  .Replace('\0', '_'); // Null characters
            
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
