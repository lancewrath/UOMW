using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ESMSharp.TES3
{
    /// <summary>
    /// Global static library for managing static records parsed from ESM files.
    /// Stores static information including model filenames and metadata.
    /// Statics can be accessed by ID like a dictionary for easy runtime lookups.
    /// </summary>
    public static class TESStaticLibrary
    {
        /// <summary>
        /// Static entry containing all relevant static information
        /// </summary>
        public class StaticEntry
        {
            public string StaticId { get; set; }                  // Static ID (from NAME subrecord)
            public string ModelFilename { get; set; }              // Model filename (from MODL)
            public string SourceESM { get; set; }                  // ESM file this static came from
            
            // Reference to loaded model (via TESNifLibrary) - populated when model is loaded
            public TESNifLibrary.NifEntry ModelEntry { get; set; }
            
            public StaticEntry(string staticId, string sourceESM = null)
            {
                StaticId = staticId;
                SourceESM = sourceESM;
            }
        }
        
        // Dictionary for fast lookups by static ID (case-insensitive)
        private static Dictionary<string, StaticEntry> _staticsById = new Dictionary<string, StaticEntry>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>
        /// Adds a static to the library. If a static with the same ID already exists, it will be overwritten.
        /// This is called automatically when statics are parsed from ESM files.
        /// </summary>
        /// <param name="statRecord">The RecordStat to extract data from</param>
        /// <param name="sourceESM">Optional ESM file this static came from</param>
        /// <returns>The static entry (newly created or updated)</returns>
        public static StaticEntry AddStatic(RecordStat statRecord, string sourceESM = null)
        {
            if (statRecord == null)
            {
                Debug.LogWarning("TESStaticLibrary: Attempted to add null static record");
                return null;
            }
            
            // Extract static ID from NAME subrecord
            string staticId = null;
            string modelFilename = null;
            
            foreach (var subrec in statRecord.subRecords)
            {
                if (subrec is SubRecordStatNAME nameRec)
                {
                    staticId = nameRec.name;
                }
                else if (subrec is SubRecordStatMODL modlRec)
                {
                    modelFilename = modlRec.model;
                }
            }
            
            if (string.IsNullOrEmpty(staticId))
            {
                Debug.LogWarning("TESStaticLibrary: Attempted to add static without ID");
                return null;
            }
            
            // Clean static ID - remove all null characters and whitespace
            staticId = staticId.TrimEnd('\0', ' ', '\t', '\r', '\n');
            staticId = staticId.Replace("\0", "");
            staticId = new string(staticId.Where(c => c != '\0').ToArray()).Trim();
            
            if (string.IsNullOrEmpty(staticId))
            {
                Debug.LogWarning("TESStaticLibrary: Static ID was empty after cleaning");
                return null;
            }
            
            // Clean model filename if present
            if (!string.IsNullOrEmpty(modelFilename))
            {
                modelFilename = modelFilename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                modelFilename = modelFilename.Replace("\0", "");
                modelFilename = new string(modelFilename.Where(c => c != '\0').ToArray()).Trim();
                
                // Normalize path separators (replace backslashes with forward slashes)
                if (!string.IsNullOrEmpty(modelFilename))
                {
                    modelFilename = modelFilename.Replace('\\', '/');
                }
            }
            
            // Check if static already exists (using cleaned ID)
            bool isUpdate = _staticsById.ContainsKey(staticId);
            
            // Create or get existing entry
            StaticEntry entry = isUpdate ? _staticsById[staticId] : new StaticEntry(staticId, sourceESM);
            
            // Update model filename (later ESM files can override)
            if (!string.IsNullOrEmpty(modelFilename))
            {
                entry.ModelFilename = modelFilename;
            }
            
            // Try to get model reference from TESNifLibrary if model filename is available
            if (!string.IsNullOrEmpty(entry.ModelFilename))
            {
                string sanitizedFilename = SanitizePath(entry.ModelFilename);
                string baseFilename = System.IO.Path.GetFileNameWithoutExtension(sanitizedFilename);
                entry.ModelEntry = TESNifLibrary.GetModelByBaseFilename(baseFilename);
            }
            
            // Add/update in dictionary
            _staticsById[staticId] = entry;
            
            return entry;
        }
        
        /// <summary>
        /// Gets a static by ID. Returns null if not found.
        /// This is the primary method for accessing statics at runtime.
        /// </summary>
        /// <param name="staticId">Static ID (case-insensitive)</param>
        /// <returns>The static entry if found, null otherwise</returns>
        public static StaticEntry GetStatic(string staticId)
        {
            if (string.IsNullOrEmpty(staticId))
                return null;
                
            // Clean ID before lookup
            staticId = staticId.TrimEnd('\0', ' ', '\t', '\r', '\n');
            staticId = staticId.Replace("\0", "");
            staticId = new string(staticId.Where(c => c != '\0').ToArray()).Trim();
                
            _staticsById.TryGetValue(staticId, out StaticEntry entry);
            return entry;
        }
        
        /// <summary>
        /// Gets the model filename for a static ID. Returns null if not found.
        /// This is a convenience method for quick model filename lookups.
        /// </summary>
        /// <param name="staticId">Static ID (case-insensitive)</param>
        /// <returns>The model filename if found, null otherwise</returns>
        public static string GetModelFilename(string staticId)
        {
            StaticEntry entry = GetStatic(staticId);
            return entry?.ModelFilename;
        }
        
        /// <summary>
        /// Checks if a static exists in the library.
        /// </summary>
        /// <param name="staticId">Static ID (case-insensitive)</param>
        /// <returns>True if the static exists, false otherwise</returns>
        public static bool HasStatic(string staticId)
        {
            if (string.IsNullOrEmpty(staticId))
                return false;
                
            // Clean ID before lookup
            staticId = staticId.TrimEnd('\0', ' ', '\t', '\r', '\n');
            staticId = staticId.Replace("\0", "");
            staticId = new string(staticId.Where(c => c != '\0').ToArray()).Trim();
                
            return _staticsById.ContainsKey(staticId);
        }
        
        /// <summary>
        /// Gets all static entries in the library.
        /// </summary>
        /// <returns>Collection of all static entries</returns>
        public static IEnumerable<StaticEntry> GetAllStatics()
        {
            return _staticsById.Values;
        }
        
        /// <summary>
        /// Gets all static IDs in the library.
        /// </summary>
        /// <returns>Array of static IDs</returns>
        public static string[] GetAllStaticIds()
        {
            return _staticsById.Keys.ToArray();
        }
        
        /// <summary>
        /// Clears all statics from the library.
        /// </summary>
        public static void Clear()
        {
            _staticsById.Clear();
            Debug.Log("TESStaticLibrary: Cleared all statics");
        }
        
        /// <summary>
        /// Gets the total number of statics in the library.
        /// </summary>
        public static int StaticCount => _staticsById.Count;
        
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

