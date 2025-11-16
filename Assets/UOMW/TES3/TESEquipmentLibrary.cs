using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ESMSharp.TES3
{
    /// <summary>
    /// Global equipment library for managing armor and clothing records parsed from ESM files.
    /// Stores equipment information including models, equipment slots, and metadata.
    /// Equipment can be accessed by ID for easy runtime lookups.
    /// </summary>
    public static class TESEquipmentLibrary
    {
        /// <summary>
        /// Equipment slot types in Morrowind (bitmask values)
        /// </summary>
        public enum EquipmentSlot
        {
            Head = 0x0001,
            Hair = 0x0002,
            Neck = 0x0004,
            Chest = 0x0008,
            Groin = 0x0010,
            Skirt = 0x0020,
            RightHand = 0x0040,
            LeftHand = 0x0080,
            RightWrist = 0x0100,
            LeftWrist = 0x0200,
            Shield = 0x0400,
            RightForearm = 0x0800,
            LeftForearm = 0x1000,
            RightUpperArm = 0x2000,
            LeftUpperArm = 0x4000,
            RightFoot = 0x8000,
            LeftFoot = 0x10000,
            RightAnkle = 0x20000,
            LeftAnkle = 0x40000
        }

        /// <summary>
        /// Equipment entry containing all relevant equipment information
        /// </summary>
        public class EquipmentEntry
        {
            public string EquipmentId { get; set; }              // Equipment ID (from NAME subrecord)
            public string DisplayName { get; set; }              // Display name (from FNAM)
            public string ModelFilename { get; set; }            // Model filename (from MODL)
            public uint EquipmentSlotType { get; set; }         // Equipment slot bitmask (from AODT.type or CTDT.type)
            public bool IsArmor { get; set; }                   // True if ARMO, false if CLOT
            public string SourceESM { get; set; }                // ESM file this equipment came from
            
            // Reference to loaded model (via TESNifLibrary)
            public TESNifLibrary.NifEntry ModelEntry { get; set; }
            
            public EquipmentEntry(string equipmentId, bool isArmor, string sourceESM = null)
            {
                EquipmentId = equipmentId;
                IsArmor = isArmor;
                SourceESM = sourceESM;
            }
        }
        
        // Dictionary for fast lookups by equipment ID (case-insensitive)
        private static Dictionary<string, EquipmentEntry> _equipmentById = new Dictionary<string, EquipmentEntry>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>
        /// Adds an armor record to the library. If equipment with the same ID already exists, it will be overwritten.
        /// This is called automatically when armor is parsed from ESM files.
        /// </summary>
        /// <param name="armorRecord">The RecordArmor to extract data from</param>
        /// <param name="sourceESM">Optional ESM file this armor came from</param>
        /// <returns>The equipment entry (newly created or updated)</returns>
        public static EquipmentEntry AddArmor(RecordArmor armorRecord, string sourceESM = null)
        {
            if (armorRecord == null)
            {
                Debug.LogWarning("TESEquipmentLibrary: Attempted to add null armor record");
                return null;
            }
            
            // Extract armor ID from NAME subrecord
            string armorId = null;
            foreach (var subrec in armorRecord.subRecords)
            {
                if (subrec is SubRecordArmorNAME nameRec)
                {
                    armorId = nameRec.name;
                    break;
                }
            }
            
            if (string.IsNullOrEmpty(armorId))
            {
                Debug.LogWarning("TESEquipmentLibrary: Attempted to add armor without ID");
                return null;
            }
            
            // Clean armor ID - remove all null characters and whitespace
            armorId = armorId.TrimEnd('\0', ' ', '\t', '\r', '\n');
            armorId = armorId.Replace("\0", "");
            armorId = new string(armorId.Where(c => c != '\0').ToArray()).Trim();
            
            if (string.IsNullOrEmpty(armorId))
            {
                Debug.LogWarning("TESEquipmentLibrary: Armor ID was empty after cleaning");
                return null;
            }
            
            // Check if equipment already exists (using cleaned ID)
            bool isUpdate = _equipmentById.ContainsKey(armorId);
            
            // Create or get existing entry
            EquipmentEntry entry = isUpdate ? _equipmentById[armorId] : new EquipmentEntry(armorId, true, sourceESM);
            
            // Extract all subrecord data
            foreach (var subrec in armorRecord.subRecords)
            {
                if (subrec is SubRecordArmorFNAM fnam)
                    entry.DisplayName = fnam.name;
                else if (subrec is SubRecordArmorMODL modl)
                {
                    // Clean model filename to remove null characters
                    string cleanedModel = modl.model?.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    cleanedModel = cleanedModel?.Replace("\0", "");
                    cleanedModel = string.IsNullOrEmpty(cleanedModel) ? null : new string(cleanedModel.Where(c => c != '\0').ToArray()).Trim();
                    
                    // Normalize path separators
                    if (!string.IsNullOrEmpty(cleanedModel))
                    {
                        cleanedModel = cleanedModel.Replace('\\', '/');
                    }
                    
                    entry.ModelFilename = cleanedModel;
                }
                else if (subrec is SubRecordArmorAODT aodt)
                    entry.EquipmentSlotType = aodt.data.type;
            }
            
            // Try to get model reference from TESNifLibrary
            if (!string.IsNullOrEmpty(entry.ModelFilename))
            {
                string sanitizedFilename = SanitizePath(entry.ModelFilename);
                string baseFilename = System.IO.Path.GetFileNameWithoutExtension(sanitizedFilename);
                entry.ModelEntry = TESNifLibrary.GetModelByBaseFilename(baseFilename);
            }
            
            // Add/update in dictionary
            _equipmentById[armorId] = entry;
            
            return entry;
        }
        
        /// <summary>
        /// Adds a clothing record to the library. If equipment with the same ID already exists, it will be overwritten.
        /// This is called automatically when clothing is parsed from ESM files.
        /// </summary>
        /// <param name="clothingRecord">The RecordClothing to extract data from</param>
        /// <param name="sourceESM">Optional ESM file this clothing came from</param>
        /// <returns>The equipment entry (newly created or updated)</returns>
        public static EquipmentEntry AddClothing(RecordClothing clothingRecord, string sourceESM = null)
        {
            if (clothingRecord == null)
            {
                Debug.LogWarning("TESEquipmentLibrary: Attempted to add null clothing record");
                return null;
            }
            
            // Extract clothing ID from NAME subrecord
            string clothingId = null;
            foreach (var subrec in clothingRecord.subRecords)
            {
                if (subrec is SubRecordClothNAME nameRec)
                {
                    clothingId = nameRec.name;
                    break;
                }
            }
            
            if (string.IsNullOrEmpty(clothingId))
            {
                Debug.LogWarning("TESEquipmentLibrary: Attempted to add clothing without ID");
                return null;
            }
            
            // Clean clothing ID - remove all null characters and whitespace
            clothingId = clothingId.TrimEnd('\0', ' ', '\t', '\r', '\n');
            clothingId = clothingId.Replace("\0", "");
            clothingId = new string(clothingId.Where(c => c != '\0').ToArray()).Trim();
            
            if (string.IsNullOrEmpty(clothingId))
            {
                Debug.LogWarning("TESEquipmentLibrary: Clothing ID was empty after cleaning");
                return null;
            }
            
            // Check if equipment already exists (using cleaned ID)
            bool isUpdate = _equipmentById.ContainsKey(clothingId);
            
            // Create or get existing entry
            EquipmentEntry entry = isUpdate ? _equipmentById[clothingId] : new EquipmentEntry(clothingId, false, sourceESM);
            
            // Extract all subrecord data
            foreach (var subrec in clothingRecord.subRecords)
            {
                if (subrec is SubRecordClothFNAM fnam)
                    entry.DisplayName = fnam.name;
                else if (subrec is SubRecordClothtMODL modl)
                {
                    // Clean model filename to remove null characters
                    string cleanedModel = modl.model?.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    cleanedModel = cleanedModel?.Replace("\0", "");
                    cleanedModel = string.IsNullOrEmpty(cleanedModel) ? null : new string(cleanedModel.Where(c => c != '\0').ToArray()).Trim();
                    
                    // Normalize path separators
                    if (!string.IsNullOrEmpty(cleanedModel))
                    {
                        cleanedModel = cleanedModel.Replace('\\', '/');
                    }
                    
                    entry.ModelFilename = cleanedModel;
                }
                else if (subrec is SubRecordClothCTDT ctdt)
                    entry.EquipmentSlotType = ctdt.data.type;
            }
            
            // Try to get model reference from TESNifLibrary
            if (!string.IsNullOrEmpty(entry.ModelFilename))
            {
                string sanitizedFilename = SanitizePath(entry.ModelFilename);
                string baseFilename = System.IO.Path.GetFileNameWithoutExtension(sanitizedFilename);
                entry.ModelEntry = TESNifLibrary.GetModelByBaseFilename(baseFilename);
            }
            
            // Add/update in dictionary
            _equipmentById[clothingId] = entry;
            
            return entry;
        }
        
        /// <summary>
        /// Gets equipment by ID. Returns null if not found.
        /// </summary>
        /// <param name="equipmentId">Equipment ID (case-insensitive)</param>
        /// <returns>The equipment entry if found, null otherwise</returns>
        public static EquipmentEntry GetEquipment(string equipmentId)
        {
            if (string.IsNullOrEmpty(equipmentId))
                return null;
                
            // Clean ID before lookup
            equipmentId = equipmentId.TrimEnd('\0', ' ', '\t', '\r', '\n');
            equipmentId = equipmentId.Replace("\0", "");
            equipmentId = new string(equipmentId.Where(c => c != '\0').ToArray()).Trim();
            
            _equipmentById.TryGetValue(equipmentId, out EquipmentEntry entry);
            return entry;
        }
        
        /// <summary>
        /// Determines which items from an NPC's inventory are equipped.
        /// Returns one item per equipment slot type (the first matching item for each slot).
        /// </summary>
        /// <param name="inventory">List of NPCObject items in the NPC's inventory</param>
        /// <returns>Dictionary mapping equipment slot types to equipped equipment entries</returns>
        public static Dictionary<uint, EquipmentEntry> GetEquippedItems(List<TESCharacterManager.NPCObject> inventory)
        {
            Dictionary<uint, EquipmentEntry> equippedItems = new Dictionary<uint, EquipmentEntry>();
            
            if (inventory == null || inventory.Count == 0)
                return equippedItems;
            
            // Iterate through inventory items
            foreach (var item in inventory)
            {
                if (string.IsNullOrEmpty(item.ObjectName))
                    continue;
                
                // Look up equipment record
                EquipmentEntry equipment = GetEquipment(item.ObjectName);
                if (equipment == null)
                    continue;
                
                // Get the equipment slot type
                uint slotType = equipment.EquipmentSlotType;
                
                // For each bit set in the slot type, check if we already have an item for that slot
                // Equipment can occupy multiple slots (bitmask), so we need to check each slot
                // We iterate through all possible slots and add this equipment to any slot it occupies
                // that doesn't already have an item
                for (uint slot = 1; slot <= 0x40000; slot <<= 1)
                {
                    if ((slotType & slot) != 0)
                    {
                        // This equipment occupies this slot
                        // Only add if we don't already have an item for this slot
                        if (!equippedItems.ContainsKey(slot))
                        {
                            equippedItems[slot] = equipment;
                            // Don't break - continue checking other slots this item might occupy
                        }
                    }
                }
            }
            
            return equippedItems;
        }
        
        /// <summary>
        /// Checks if equipment exists in the library.
        /// </summary>
        /// <param name="equipmentId">Equipment ID (case-insensitive)</param>
        /// <returns>True if the equipment exists, false otherwise</returns>
        public static bool HasEquipment(string equipmentId)
        {
            if (string.IsNullOrEmpty(equipmentId))
                return false;
                
            // Clean ID before lookup
            equipmentId = equipmentId.TrimEnd('\0', ' ', '\t', '\r', '\n');
            equipmentId = equipmentId.Replace("\0", "");
            equipmentId = new string(equipmentId.Where(c => c != '\0').ToArray()).Trim();
                
            return _equipmentById.ContainsKey(equipmentId);
        }
        
        /// <summary>
        /// Gets all equipment entries in the library.
        /// </summary>
        /// <returns>Collection of all equipment entries</returns>
        public static IEnumerable<EquipmentEntry> GetAllEquipment()
        {
            return _equipmentById.Values;
        }
        
        /// <summary>
        /// Gets all equipment IDs in the library.
        /// </summary>
        /// <returns>Array of equipment IDs</returns>
        public static string[] GetAllEquipmentIds()
        {
            return _equipmentById.Keys.ToArray();
        }
        
        /// <summary>
        /// Clears all equipment from the library.
        /// </summary>
        public static void Clear()
        {
            _equipmentById.Clear();
            Debug.Log("TESEquipmentLibrary: Cleared all equipment");
        }
        
        /// <summary>
        /// Gets the total number of equipment items in the library.
        /// </summary>
        public static int EquipmentCount => _equipmentById.Count;
        
        /// <summary>
        /// Sanitizes a file path by removing invalid path characters.
        /// </summary>
        /// <param name="path">The path string to sanitize</param>
        /// <returns>The sanitized path with invalid characters replaced with underscores</returns>
        private static string SanitizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            
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
                char[] invalidChars = System.IO.Path.GetInvalidPathChars();
                foreach (char c in invalidChars)
                {
                    sanitized = sanitized.Replace(c, '_');
                }
                
                char[] invalidFileNameChars = System.IO.Path.GetInvalidFileNameChars();
                foreach (char c in invalidFileNameChars)
                {
                    sanitized = sanitized.Replace(c, '_');
                }
            }
            catch (System.Exception)
            {
                // If Path.GetInvalidPathChars() fails, basic sanitization should be sufficient
            }
            
            return sanitized;
        }
    }
}

