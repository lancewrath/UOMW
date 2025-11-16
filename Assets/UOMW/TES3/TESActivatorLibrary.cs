using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ESMSharp.TES3
{
    /// <summary>
    /// Global activator library for managing activator records parsed from ESM files.
    /// Stores activator information including model filenames and metadata.
    /// Activators can be accessed by ID like a dictionary for easy runtime lookups.
    /// </summary>
    public static class TESActivatorLibrary
    {
        /// <summary>
        /// Activator entry containing all relevant activator information
        /// </summary>
        public class ActivatorEntry
        {
            public string ActivatorId { get; set; }                 // Activator ID (from NAME subrecord)
            public string DisplayName { get; set; }                  // Display name (from FNAM)
            public string ModelFilename { get; set; }                // Model filename (from MODL)
            public string ScriptName { get; set; }                   // Script name (from SCRI)
            public string SourceESM { get; set; }                     // ESM file this activator came from
            
            // Reference to loaded model (via TESNifLibrary)
            public TESNifLibrary.NifEntry ModelEntry { get; set; }
            
            public ActivatorEntry(string activatorId, string sourceESM = null)
            {
                ActivatorId = activatorId;
                SourceESM = sourceESM;
            }
        }
        
        // Dictionary for fast lookups by activator ID (case-insensitive)
        private static Dictionary<string, ActivatorEntry> _activatorsById = new Dictionary<string, ActivatorEntry>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>
        /// Adds an activator to the library. If an activator with the same ID already exists, it will be overwritten.
        /// This is called automatically when activators are parsed from ESM files.
        /// </summary>
        /// <param name="activatorRecord">The RecordActivator to extract data from</param>
        /// <param name="sourceESM">Optional ESM file this activator came from</param>
        /// <returns>The activator entry (newly created or updated)</returns>
        public static ActivatorEntry AddActivator(RecordActivator activatorRecord, string sourceESM = null)
        {
            if (activatorRecord == null)
            {
                Debug.LogWarning("TESActivatorLibrary: Attempted to add null activator record");
                return null;
            }
            
            // Extract activator ID from NAME subrecord
            string activatorId = null;
            foreach (var subrec in activatorRecord.subRecords)
            {
                if (subrec is SubRecordActiNAME nameRec)
                {
                    activatorId = nameRec.name;
                    break;
                }
            }
            
            if (string.IsNullOrEmpty(activatorId))
            {
                Debug.LogWarning("TESActivatorLibrary: Attempted to add activator without ID");
                return null;
            }
            
            // Clean activator ID - remove all null characters and whitespace
            activatorId = activatorId.TrimEnd('\0', ' ', '\t', '\r', '\n');
            activatorId = activatorId.Replace("\0", "");
            activatorId = new string(activatorId.Where(c => c != '\0').ToArray()).Trim();
            
            if (string.IsNullOrEmpty(activatorId))
            {
                Debug.LogWarning("TESActivatorLibrary: Activator ID was empty after cleaning");
                return null;
            }
            
            // Check if activator already exists (using cleaned ID)
            bool isUpdate = _activatorsById.ContainsKey(activatorId);
            
            // Create or get existing entry
            ActivatorEntry entry = isUpdate ? _activatorsById[activatorId] : new ActivatorEntry(activatorId, sourceESM);
            
            // Extract all subrecord data
            foreach (var subrec in activatorRecord.subRecords)
            {
                if (subrec is SubRecordActiFNAM fnam)
                    entry.DisplayName = fnam.name;
                else if (subrec is SubRecordActiMODL modl)
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
                else if (subrec is SubRecordActiSCRI scri)
                    entry.ScriptName = scri.name;
            }
            
            // Try to get model reference from TESNifLibrary
            if (!string.IsNullOrEmpty(entry.ModelFilename))
            {
                string sanitizedFilename = SanitizePath(entry.ModelFilename);
                string baseFilename = System.IO.Path.GetFileNameWithoutExtension(sanitizedFilename);
                entry.ModelEntry = TESNifLibrary.GetModelByBaseFilename(baseFilename);
            }
            
            // Add/update in dictionary
            _activatorsById[activatorId] = entry;
            
            return entry;
        }
        
        /// <summary>
        /// Gets an activator by ID. Returns null if not found.
        /// </summary>
        /// <param name="activatorId">Activator ID (case-insensitive)</param>
        /// <returns>The activator entry if found, null otherwise</returns>
        public static ActivatorEntry GetActivator(string activatorId)
        {
            if (string.IsNullOrEmpty(activatorId))
                return null;
                
            // Clean ID before lookup
            activatorId = activatorId.TrimEnd('\0', ' ', '\t', '\r', '\n');
            activatorId = activatorId.Replace("\0", "");
            activatorId = new string(activatorId.Where(c => c != '\0').ToArray()).Trim();
                
            _activatorsById.TryGetValue(activatorId, out ActivatorEntry entry);
            return entry;
        }
        
        /// <summary>
        /// Checks if an activator exists in the library.
        /// </summary>
        /// <param name="activatorId">Activator ID (case-insensitive)</param>
        /// <returns>True if the activator exists, false otherwise</returns>
        public static bool HasActivator(string activatorId)
        {
            if (string.IsNullOrEmpty(activatorId))
                return false;
                
            // Clean ID before lookup
            activatorId = activatorId.TrimEnd('\0', ' ', '\t', '\r', '\n');
            activatorId = activatorId.Replace("\0", "");
            activatorId = new string(activatorId.Where(c => c != '\0').ToArray()).Trim();
                
            return _activatorsById.ContainsKey(activatorId);
        }
        
        /// <summary>
        /// Gets all activator entries in the library.
        /// </summary>
        /// <returns>Collection of all activator entries</returns>
        public static IEnumerable<ActivatorEntry> GetAllActivators()
        {
            return _activatorsById.Values;
        }
        
        /// <summary>
        /// Clears all activators from the library.
        /// </summary>
        public static void Clear()
        {
            _activatorsById.Clear();
            Debug.Log("TESActivatorLibrary: Cleared all activators");
        }
        
        /// <summary>
        /// Gets the total number of activators in the library.
        /// </summary>
        public static int ActivatorCount => _activatorsById.Count;
        
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
                // If Path.GetInvalidPathChars() still fails, the basic sanitization above should be sufficient
            }

            return sanitized;
        }
    }
}

