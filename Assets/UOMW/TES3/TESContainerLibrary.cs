using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ESMSharp.TES3
{
    /// <summary>
    /// Global container library for managing container records parsed from ESM files.
    /// Stores container information including model filenames and metadata.
    /// Containers can be accessed by ID like a dictionary for easy runtime container creation.
    /// </summary>
    public static class TESContainerLibrary
    {
        /// <summary>
        /// Container entry containing all relevant container information
        /// </summary>
        public class ContainerEntry
        {
            public string ContainerId { get; set; }              // Container ID (from NAME subrecord)
            public string DisplayName { get; set; }               // Display name (from FNAM)
            public string ModelFilename { get; set; }            // Model filename (from MODL)
            public string ScriptName { get; set; }                // Script name (from SCRI)
            public float Weight { get; set; }                     // Container weight (from CNDT)
            public uint Flags { get; set; }                       // Container flags (from FLAG)
            public List<ContainerObject> Items { get; set; }      // Container items (from NPCO)
            public string SourceESM { get; set; }                 // ESM file this container came from
            
            // Reference to loaded model (via TESNifLibrary)
            public TESNifLibrary.NifEntry ModelEntry { get; set; }
            
            public ContainerEntry(string containerId, string sourceESM = null)
            {
                ContainerId = containerId;
                SourceESM = sourceESM;
                Items = new List<ContainerObject>();
            }
        }
        
        /// <summary>
        /// Container item object (item with count)
        /// </summary>
        public class ContainerObject
        {
            public string ObjectName { get; set; }    // Item/object ID
            public int Count { get; set; }            // Quantity
            
            public ContainerObject(string objectName, int count)
            {
                ObjectName = objectName;
                Count = count;
            }
        }
        
        // Dictionary for fast lookups by container ID (case-insensitive)
        private static Dictionary<string, ContainerEntry> _containersById = new Dictionary<string, ContainerEntry>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>
        /// Adds a container to the library. If a container with the same ID already exists, it will be overwritten.
        /// This is called automatically when containers are parsed from ESM files.
        /// </summary>
        /// <param name="contRecord">The RecordCont to extract data from</param>
        /// <param name="sourceESM">Optional ESM file this container came from</param>
        /// <returns>The container entry (newly created or updated)</returns>
        public static ContainerEntry AddContainer(RecordCont contRecord, string sourceESM = null)
        {
            if (contRecord == null)
            {
                Debug.LogWarning("TESContainerLibrary: Attempted to add null container record");
                return null;
            }
            
            // Extract container ID from NAME subrecord
            string containerId = null;
            foreach (var subrec in contRecord.subRecords)
            {
                if (subrec is SubRecordContNAME nameRec)
                {
                    containerId = nameRec.name;
                    break;
                }
            }
            
            if (string.IsNullOrEmpty(containerId))
            {
                Debug.LogWarning("TESContainerLibrary: Attempted to add container without ID");
                return null;
            }
            
            // Clean container ID - remove all null characters and whitespace
            // This ensures lookups work even if the NAME field has trailing nulls
            containerId = containerId.TrimEnd('\0', ' ', '\t', '\r', '\n');
            containerId = containerId.Replace("\0", "");
            containerId = new string(containerId.Where(c => c != '\0').ToArray()).Trim();
            
            if (string.IsNullOrEmpty(containerId))
            {
                Debug.LogWarning("TESContainerLibrary: Container ID was empty after cleaning");
                return null;
            }
            
            // Check if container already exists (using cleaned ID)
            bool isUpdate = _containersById.ContainsKey(containerId);
            
            // Create or get existing entry
            ContainerEntry entry = isUpdate ? _containersById[containerId] : new ContainerEntry(containerId, sourceESM);
            
            // Extract all subrecord data
            foreach (var subrec in contRecord.subRecords)
            {
                if (subrec is SubRecordContFNAM fnam)
                {
                    entry.DisplayName = fnam.name;
                }
                else if (subrec is SubRecordContMODL modl)
                {
                    // Clean model filename to remove null characters
                    string cleanedModelFilename = modl.model?.TrimEnd('\0', ' ', '\t', '\r', '\n');
                    cleanedModelFilename = cleanedModelFilename?.Replace("\0", "");
                    cleanedModelFilename = string.IsNullOrEmpty(cleanedModelFilename) ? null : new string(cleanedModelFilename.Where(c => c != '\0').ToArray()).Trim();
                    
                    // Normalize path separators (replace backslashes with forward slashes) for consistency
                    if (!string.IsNullOrEmpty(cleanedModelFilename))
                    {
                        cleanedModelFilename = cleanedModelFilename.Replace('\\', '/');
                    }
                    
                    entry.ModelFilename = cleanedModelFilename;
                }
                else if (subrec is SubRecordContSCRI scri)
                {
                    entry.ScriptName = scri.name;
                }
                else if (subrec is SubRecordContCNDT cndt)
                {
                    entry.Weight = cndt.value;
                }
                else if (subrec is SubRecordContFLAG flag)
                {
                    entry.Flags = flag.flag;
                }
                else if (subrec is SubRecordContNPCO npco)
                {
                    // Add item to container inventory
                    string itemName = npco.objectName?.TrimEnd('\0');
                    int itemCount = npco.count;
                    if (!string.IsNullOrEmpty(itemName))
                    {
                        entry.Items.Add(new ContainerObject(itemName, itemCount));
                    }
                }
            }
            
            // Store in dictionary (using cleaned ID)
            _containersById[containerId] = entry;
            
            return entry;
        }
        
        /// <summary>
        /// Gets a container by ID (case-insensitive lookup)
        /// </summary>
        /// <param name="containerId">The container ID to look up</param>
        /// <returns>The container entry, or null if not found</returns>
        public static ContainerEntry GetContainer(string containerId)
        {
            if (string.IsNullOrEmpty(containerId))
                return null;
            
            // Clean the container ID before lookup
            string cleanedId = containerId.TrimEnd('\0', ' ', '\t', '\r', '\n');
            cleanedId = cleanedId.Replace("\0", "");
            cleanedId = new string(cleanedId.Where(c => c != '\0').ToArray()).Trim();
            
            if (string.IsNullOrEmpty(cleanedId))
                return null;
            
            // Try exact match first
            if (_containersById.TryGetValue(cleanedId, out var entry))
            {
                return entry;
            }
            
            // Try original ID if different from cleaned
            if (!string.Equals(containerId, cleanedId, StringComparison.OrdinalIgnoreCase))
            {
                _containersById.TryGetValue(containerId, out entry);
            }
            
            return entry;
        }
        
        /// <summary>
        /// Checks if a container exists in the library
        /// </summary>
        /// <param name="containerId">The container ID to check</param>
        /// <returns>True if the container exists, false otherwise</returns>
        public static bool HasContainer(string containerId)
        {
            return GetContainer(containerId) != null;
        }
        
        /// <summary>
        /// Gets all containers in the library
        /// </summary>
        /// <returns>Array of all container entries</returns>
        public static ContainerEntry[] GetAllContainers()
        {
            return _containersById.Values.ToArray();
        }
        
        /// <summary>
        /// Clears all containers from the library
        /// </summary>
        public static void Clear()
        {
            _containersById.Clear();
        }
        
        /// <summary>
        /// Gets the number of containers in the library
        /// </summary>
        public static int Count => _containersById.Count;
    }
}
