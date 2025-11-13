using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ESMSharp.TES3
{
    /// <summary>
    /// Global character library for managing NPCs and body parts parsed from ESM files.
    /// Stores NPC information including stats, models, inventory, and body part references.
    /// NPCs and body parts can be accessed by ID like a dictionary for easy runtime character creation.
    /// </summary>
    public static class TESCharacterManager
    {
        /// <summary>
        /// NPC entry containing all relevant NPC information
        /// </summary>
        public class NPCEntry
        {
            public string NPCId { get; set; }                    // NPC ID (from NAME subrecord)
            public string DisplayName { get; set; }               // Display name (from FNAM)
            public string ModelFilename { get; set; }            // Model filename (from MODL)
            public string RaceName { get; set; }                  // Race name (from RNAM)
            public string ClassName { get; set; }                 // Class name (from CNAM)
            public string FactionName { get; set; }               // Faction name (from ANAM)
            public string HeadModel { get; set; }                 // Head model (from BNAM)
            public string HairModel { get; set; }                 // Hair model (from KNAM)
            public string ScriptName { get; set; }                // Script name (from SCRI)
            public NPCDATA NPCData { get; set; }                  // NPC stats data (from NPDT)
            public uint Flags { get; set; }                       // NPC flags (from FLAG)
            public List<NPCObject> Inventory { get; set; }       // Inventory items (from NPCO)
            public string Spells { get; set; }                    // Spells list (from NPCS)
            public string SourceESM { get; set; }                  // ESM file this NPC came from
            
            // References to loaded models (via TESNifLibrary)
            public TESNifLibrary.NifEntry ModelEntry { get; set; }    // Main model reference
            public TESNifLibrary.NifEntry HeadModelEntry { get; set; } // Head model reference
            public TESNifLibrary.NifEntry HairModelEntry { get; set; } // Hair model reference
            
            public NPCEntry(string npcId, string sourceESM = null)
            {
                NPCId = npcId;
                SourceESM = sourceESM;
                Inventory = new List<NPCObject>();
            }
        }
        
        /// <summary>
        /// NPC inventory object (item with count)
        /// </summary>
        public class NPCObject
        {
            public string ObjectName { get; set; }    // Item/object ID
            public int Count { get; set; }            // Quantity
            
            public NPCObject(string objectName, int count)
            {
                ObjectName = objectName;
                Count = count;
            }
        }
        
        /// <summary>
        /// Body part entry containing body part model information
        /// </summary>
        public class BodyPartEntry
        {
            public string BodyPartId { get; set; }         // Body part ID (from NAME)
            public string ModelFilename { get; set; }      // Model filename (from MODL)
            public string PartName { get; set; }           // Part name (from FNAM)
            public byte Part { get; set; }                 // Part index (from BYDT)
            public byte Vampire { get; set; }              // Vampire flag (from BYDT)
            public byte Flags { get; set; }                // Flags (from BYDT)
            public byte PartType { get; set; }             // Part type (from BYDT)
            public string SourceESM { get; set; }          // ESM file this body part came from
            
            // Reference to loaded model (via TESNifLibrary)
            public TESNifLibrary.NifEntry ModelEntry { get; set; }
            
            public BodyPartEntry(string bodyPartId, string sourceESM = null)
            {
                BodyPartId = bodyPartId;
                SourceESM = sourceESM;
            }
        }
        
        // Dictionary for fast lookups by NPC ID (case-insensitive)
        private static Dictionary<string, NPCEntry> _npcsById = new Dictionary<string, NPCEntry>(StringComparer.OrdinalIgnoreCase);
        
        // Dictionary for fast lookups by body part ID (case-insensitive)
        private static Dictionary<string, BodyPartEntry> _bodyPartsById = new Dictionary<string, BodyPartEntry>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>
        /// Adds an NPC to the library. If an NPC with the same ID already exists, it will be overwritten.
        /// This is called automatically when NPCs are parsed from ESM files.
        /// </summary>
        /// <param name="npcRecord">The RecordNPC to extract data from</param>
        /// <param name="sourceESM">Optional ESM file this NPC came from</param>
        /// <returns>The NPC entry (newly created or updated)</returns>
        public static NPCEntry AddNPC(RecordNPC npcRecord, string sourceESM = null)
        {
            if (npcRecord == null)
            {
                Debug.LogWarning("TESCharacterManager: Attempted to add null NPC record");
                return null;
            }
            
            // Extract NPC ID from NAME subrecord
            string npcId = null;
            foreach (var subrec in npcRecord.subRecords)
            {
                if (subrec is SubRecordNPCNAME nameRec)
                {
                    npcId = nameRec.name;
                    break;
                }
            }
            
            if (string.IsNullOrEmpty(npcId))
            {
                Debug.LogWarning("TESCharacterManager: Attempted to add NPC without ID");
                return null;
            }
            
            // Clean NPC ID - remove all null characters and whitespace
            // This ensures lookups work even if the NAME field has trailing nulls
            npcId = npcId.TrimEnd('\0', ' ', '\t', '\r', '\n');
            npcId = npcId.Replace("\0", "");
            npcId = new string(npcId.Where(c => c != '\0').ToArray()).Trim();
            
            if (string.IsNullOrEmpty(npcId))
            {
                Debug.LogWarning("TESCharacterManager: NPC ID was empty after cleaning");
                return null;
            }
            
            // Check if NPC already exists (using cleaned ID)
            bool isUpdate = _npcsById.ContainsKey(npcId);
            
            // Create or get existing entry
            NPCEntry entry = isUpdate ? _npcsById[npcId] : new NPCEntry(npcId, sourceESM);
            
            // Extract all subrecord data
            foreach (var subrec in npcRecord.subRecords)
            {
                if (subrec is SubRecordNPCFNAM fnam)
                    entry.DisplayName = fnam.name;
                else if (subrec is SubRecordNPCMODL modl)
                {
                    // Clean body part ID (MODL) to remove null characters
                    string cleanedModelId = modl.model?.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    cleanedModelId = cleanedModelId?.Replace("\0", "");
                    cleanedModelId = string.IsNullOrEmpty(cleanedModelId) ? null : new string(cleanedModelId.Where(c => c != '\0').ToArray()).Trim();
                    entry.ModelFilename = cleanedModelId;
                    
                    // Debug: Log MODL values to help diagnose missing body parts
                    if (!string.IsNullOrEmpty(cleanedModelId))
                    {
                        //Debug.Log($"TESCharacterManager: NPC '{npcId}' has MODL='{cleanedModelId}' (original: '{modl.model}')");
                    }
                    else
                    {
                        Debug.LogWarning($"TESCharacterManager: NPC '{npcId}' has empty or null MODL (original: '{modl.model}')");
                    }
                }
                else if (subrec is SubRecordNPCRNAM rnam)
                    entry.RaceName = rnam.name;
                else if (subrec is SubRecordNPCCNAM cnam)
                    entry.ClassName = cnam.name;
                else if (subrec is SubRecordNPCANAM anam)
                    entry.FactionName = anam.name;
                else if (subrec is SubRecordNPCBNAM bnam)
                {
                    // Clean head body part ID (BNAM) to remove null characters
                    string cleanedHeadId = bnam.name?.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    cleanedHeadId = cleanedHeadId?.Replace("\0", "");
                    cleanedHeadId = string.IsNullOrEmpty(cleanedHeadId) ? null : new string(cleanedHeadId.Where(c => c != '\0').ToArray()).Trim();
                    entry.HeadModel = cleanedHeadId;
                }
                else if (subrec is SubRecordNPCKNAM knam)
                {
                    // Clean hair body part ID (KNAM) to remove null characters
                    string cleanedHairId = knam.name?.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    cleanedHairId = cleanedHairId?.Replace("\0", "");
                    cleanedHairId = string.IsNullOrEmpty(cleanedHairId) ? null : new string(cleanedHairId.Where(c => c != '\0').ToArray()).Trim();
                    entry.HairModel = cleanedHairId;
                }
                else if (subrec is SubRecordNPCSCRI scri)
                    entry.ScriptName = scri.name;
                else if (subrec is SubRecordNPCNPDT npdt)
                    entry.NPCData = npdt.data;
                else if (subrec is SubRecordNPCFLAG flag)
                    entry.Flags = flag.flag;
                else if (subrec is SubRecordNPCNPCO npco)
                    entry.Inventory.Add(new NPCObject(npco.objectName, npco.count));
                else if (subrec is SubRecordNPCNPCS npcs)
                    entry.Spells = npcs.spells;
            }
            
            // Try to get model references from TESNifLibrary
            if (!string.IsNullOrEmpty(entry.ModelFilename))
            {
                // Sanitize filename before using Path methods to avoid "Illegal characters in path" errors
                string sanitizedFilename = SanitizePath(entry.ModelFilename);
                string baseFilename = System.IO.Path.GetFileNameWithoutExtension(sanitizedFilename);
                entry.ModelEntry = TESNifLibrary.GetModelByBaseFilename(baseFilename);
            }
            
            if (!string.IsNullOrEmpty(entry.HeadModel))
            {
                // Sanitize filename before using Path methods to avoid "Illegal characters in path" errors
                string sanitizedFilename = SanitizePath(entry.HeadModel);
                string baseFilename = System.IO.Path.GetFileNameWithoutExtension(sanitizedFilename);
                entry.HeadModelEntry = TESNifLibrary.GetModelByBaseFilename(baseFilename);
            }
            
            if (!string.IsNullOrEmpty(entry.HairModel))
            {
                // Sanitize filename before using Path methods to avoid "Illegal characters in path" errors
                string sanitizedFilename = SanitizePath(entry.HairModel);
                string baseFilename = System.IO.Path.GetFileNameWithoutExtension(sanitizedFilename);
                entry.HairModelEntry = TESNifLibrary.GetModelByBaseFilename(baseFilename);
            }
            
            // Add/update in dictionary
            _npcsById[npcId] = entry;
            
            //Debug.Log($"TESCharacterManager: {(isUpdate ? "Updated" : "Added")} NPC '{npcId}' (from {sourceESM ?? "unknown"})");
            
            return entry;
        }
        
        /// <summary>
        /// Adds a body part to the library. If a body part with the same ID already exists, it will be overwritten.
        /// This is called automatically when body parts are parsed from ESM files.
        /// </summary>
        /// <param name="bodyRecord">The RecordBody to extract data from</param>
        /// <param name="sourceESM">Optional ESM file this body part came from</param>
        /// <returns>The body part entry (newly created or updated)</returns>
        public static BodyPartEntry AddBodyPart(RecordBody bodyRecord, string sourceESM = null)
        {
            if (bodyRecord == null)
            {
                Debug.LogWarning("TESCharacterManager: Attempted to add null body part record");
                return null;
            }
            
            // Extract body part ID from NAME subrecord
            string bodyPartId = null;
            foreach (var subrec in bodyRecord.subRecords)
            {
                if (subrec is SubRecordBodyNAME nameRec)
                {
                    bodyPartId = nameRec.name;
                    break;
                }
            }
            
            if (string.IsNullOrEmpty(bodyPartId))
            {
                Debug.LogWarning("TESCharacterManager: Attempted to add body part without ID");
                return null;
            }
            
            // Clean body part ID - remove all null characters and whitespace
            // This ensures lookups work even if the NAME field has trailing nulls
            bodyPartId = bodyPartId.TrimEnd('\0', ' ', '\t', '\r', '\n');
            bodyPartId = bodyPartId.Replace("\0", "");
            bodyPartId = new string(bodyPartId.Where(c => c != '\0').ToArray()).Trim();
            
            if (string.IsNullOrEmpty(bodyPartId))
            {
                Debug.LogWarning("TESCharacterManager: Body part ID was empty after cleaning");
                return null;
            }
            
            // Check if body part already exists (using cleaned ID)
            bool isUpdate = _bodyPartsById.ContainsKey(bodyPartId);
            
            // Create or get existing entry
            BodyPartEntry entry = isUpdate ? _bodyPartsById[bodyPartId] : new BodyPartEntry(bodyPartId, sourceESM);
            
            // Extract all subrecord data
            foreach (var subrec in bodyRecord.subRecords)
            {
                if (subrec is SubRecordBodyMODL modl)
                {
                    // Clean model filename to remove null characters
                    string cleanedModel = modl.model?.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    cleanedModel = cleanedModel?.Replace("\0", "");
                    cleanedModel = string.IsNullOrEmpty(cleanedModel) ? null : new string(cleanedModel.Where(c => c != '\0').ToArray()).Trim();
                    entry.ModelFilename = cleanedModel;
                }
                else if (subrec is SubRecordBodyFNAM fnam)
                    entry.PartName = fnam.name;
                else if (subrec is SubRecordBodyBYDT bydt)
                {
                    entry.Part = bydt.part;
                    entry.Vampire = bydt.vampire;
                    entry.Flags = bydt.flags;
                    entry.PartType = bydt.partType;
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
            _bodyPartsById[bodyPartId] = entry;
            
            //Debug.Log($"TESCharacterManager: {(isUpdate ? "Updated" : "Added")} body part '{bodyPartId}' (from {sourceESM ?? "unknown"})");
            
            return entry;
        }
        
        /// <summary>
        /// Gets an NPC by ID. Returns null if not found.
        /// This is the primary method for accessing NPCs at runtime.
        /// </summary>
        /// <param name="npcId">NPC ID (case-insensitive)</param>
        /// <returns>The NPC entry if found, null otherwise</returns>
        public static NPCEntry GetNPC(string npcId)
        {
            if (string.IsNullOrEmpty(npcId))
                return null;
                
            _npcsById.TryGetValue(npcId, out NPCEntry entry);
            return entry;
        }
        
        /// <summary>
        /// Gets a body part by ID. Returns null if not found.
        /// </summary>
        /// <param name="bodyPartId">Body part ID (case-insensitive)</param>
        /// <returns>The body part entry if found, null otherwise</returns>
        public static BodyPartEntry GetBodyPart(string bodyPartId)
        {
            if (string.IsNullOrEmpty(bodyPartId))
                return null;
                
            _bodyPartsById.TryGetValue(bodyPartId, out BodyPartEntry entry);
            return entry;
        }
        
        /// <summary>
        /// Gets all body parts of a specific part type.
        /// </summary>
        /// <param name="partType">Part type to filter by</param>
        /// <returns>Collection of body part entries matching the part type</returns>
        public static IEnumerable<BodyPartEntry> GetBodyPartsByType(byte partType)
        {
            return _bodyPartsById.Values.Where(bp => bp.PartType == partType);
        }
        
        /// <summary>
        /// Gets all body parts for a specific part index.
        /// </summary>
        /// <param name="part">Part index to filter by</param>
        /// <returns>Collection of body part entries matching the part index</returns>
        public static IEnumerable<BodyPartEntry> GetBodyPartsByPart(byte part)
        {
            return _bodyPartsById.Values.Where(bp => bp.Part == part);
        }
        
        /// <summary>
        /// Checks if an NPC exists in the library.
        /// </summary>
        /// <param name="npcId">NPC ID (case-insensitive)</param>
        /// <returns>True if the NPC exists, false otherwise</returns>
        public static bool HasNPC(string npcId)
        {
            if (string.IsNullOrEmpty(npcId))
                return false;
                
            return _npcsById.ContainsKey(npcId);
        }
        
        /// <summary>
        /// Checks if a body part exists in the library.
        /// </summary>
        /// <param name="bodyPartId">Body part ID (case-insensitive)</param>
        /// <returns>True if the body part exists, false otherwise</returns>
        public static bool HasBodyPart(string bodyPartId)
        {
            if (string.IsNullOrEmpty(bodyPartId))
                return false;
                
            return _bodyPartsById.ContainsKey(bodyPartId);
        }
        
        /// <summary>
        /// Gets all NPC entries in the library.
        /// </summary>
        /// <returns>Collection of all NPC entries</returns>
        public static IEnumerable<NPCEntry> GetAllNPCs()
        {
            return _npcsById.Values;
        }
        
        /// <summary>
        /// Gets all body part entries in the library.
        /// </summary>
        /// <returns>Collection of all body part entries</returns>
        public static IEnumerable<BodyPartEntry> GetAllBodyParts()
        {
            return _bodyPartsById.Values;
        }
        
        /// <summary>
        /// Gets all NPC IDs in the library.
        /// </summary>
        /// <returns>Array of NPC IDs</returns>
        public static string[] GetAllNPCIds()
        {
            return _npcsById.Keys.ToArray();
        }
        
        /// <summary>
        /// Gets all body part IDs in the library.
        /// </summary>
        /// <returns>Array of body part IDs</returns>
        public static string[] GetAllBodyPartIds()
        {
            return _bodyPartsById.Keys.ToArray();
        }
        
        /// <summary>
        /// Clears all NPCs and body parts from the library.
        /// </summary>
        public static void Clear()
        {
            _npcsById.Clear();
            _bodyPartsById.Clear();
            Debug.Log("TESCharacterManager: Cleared all NPCs and body parts");
        }
        
        /// <summary>
        /// Gets the total number of NPCs in the library.
        /// </summary>
        public static int NPCCount => _npcsById.Count;
        
        /// <summary>
        /// Gets the total number of body parts in the library.
        /// </summary>
        public static int BodyPartCount => _bodyPartsById.Count;
        
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
