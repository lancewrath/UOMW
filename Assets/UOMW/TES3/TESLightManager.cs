using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ESMSharp.TES3
{
    /// <summary>
    /// Global light library for managing light records parsed from ESM files.
    /// Stores light information including model filenames and light properties.
    /// Lights can be accessed by ID like a dictionary for easy runtime light creation.
    /// </summary>
    public static class TESLightManager
    {
        /// <summary>
        /// Light entry containing all relevant light information
        /// </summary>
        public class LightEntry
        {
            public string LightId { get; set; }              // Light ID (from NAME subrecord)
            public string DisplayName { get; set; }           // Display name (from FNAM)
            public string ModelFilename { get; set; }          // Model filename (from MODL)
            public string InventoryIcon { get; set; }          // Inventory icon (from ITEX)
            public string SoundName { get; set; }              // Sound name (from SNAM)
            public string ScriptName { get; set; }             // Script name (from SCRI)
            public LightData LightData { get; set; }           // Light data (from LHDT)
            public string SourceESM { get; set; }              // ESM file this light came from
            
            // Reference to loaded model (via TESNifLibrary)
            public TESNifLibrary.NifEntry ModelEntry { get; set; }
            
            public LightEntry(string lightId, string sourceESM = null)
            {
                LightId = lightId;
                SourceESM = sourceESM;
            }
        }
        
        // Dictionary for fast lookups by light ID (case-insensitive)
        private static Dictionary<string, LightEntry> _lightsById = new Dictionary<string, LightEntry>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>
        /// Normalizes a light ID by removing null characters and trimming whitespace.
        /// This must match the exact cleaning pattern used in TESContainerLibrary and TESCharacterManager.
        /// </summary>
        private static string NormalizeLightId(string lightId)
        {
            if (string.IsNullOrEmpty(lightId))
                return lightId;
            
            // Clean light ID - remove all null characters and whitespace
            // This ensures lookups work even if the NAME field has trailing nulls
            // Must match the exact pattern: TrimEnd, Replace, new string with Where, Trim
            lightId = lightId.TrimEnd('\0', ' ', '\t', '\r', '\n');
            lightId = lightId.Replace("\0", "");
            lightId = new string(lightId.Where(c => c != '\0').ToArray()).Trim();
            
            return lightId;
        }
        
        /// <summary>
        /// Adds a light to the library. If a light with the same ID already exists, it will be overwritten.
        /// This is called automatically when lights are parsed from ESM files.
        /// </summary>
        /// <param name="lightRecord">The RecordLight to extract data from</param>
        /// <param name="sourceESM">Optional ESM file this light came from</param>
        /// <returns>The light entry (newly created or updated)</returns>
        public static LightEntry AddLight(RecordLight lightRecord, string sourceESM = null)
        {
            if (lightRecord == null)
            {
                Debug.LogWarning("TESLightManager: Attempted to add null light record");
                return null;
            }
            
            // Extract light ID from NAME subrecord
            string lightId = null;
            string rawName = null;
            foreach (var subrec in lightRecord.subRecords)
            {
                if (subrec is SubRecordLightNAME nameRec)
                {
                    rawName = nameRec.name;
                    // Clean the name immediately when extracting to handle embedded null characters
                    if (nameRec.name != null)
                    {
                        lightId = NormalizeLightId(nameRec.name);
                        
                        #if UNITY_EDITOR
                        // Debug logging for lights with streetlight in name
                        if (rawName != null && (rawName.Contains("light_de_streetlight") || rawName.Contains("streetlight")))
                        {
                            Debug.Log($"TESLightManager: Extracted NAME subrecord. Raw: '{rawName}' (length: {rawName?.Length ?? 0}), Cleaned: '{lightId}' (length: {lightId?.Length ?? 0})");
                            if (rawName != null && rawName.Any(c => c == '\0'))
                            {
                                Debug.Log($"TESLightManager: Raw name contains null characters at positions: {string.Join(", ", Enumerable.Range(0, rawName.Length).Where(i => rawName[i] == '\0'))}");
                            }
                        }
                        #endif
                    }
                    break;
                }
            }
            
            if (string.IsNullOrEmpty(lightId))
            {
                Debug.LogWarning("TESLightManager: Attempted to add light without ID");
                return null;
            }
            
            // Ensure the lightId is normalized (should already be done, but double-check)
            lightId = NormalizeLightId(lightId);
            
            if (string.IsNullOrEmpty(lightId))
            {
                Debug.LogWarning("TESLightManager: Light ID was empty after cleaning");
                return null;
            }
            
            // Check if light already exists (using cleaned ID)
            bool isUpdate = _lightsById.ContainsKey(lightId);
            
            // Create or get existing entry
            LightEntry entry = isUpdate ? _lightsById[lightId] : new LightEntry(lightId, sourceESM);
            
            // Extract all subrecord data
            foreach (var subrec in lightRecord.subRecords)
            {
                if (subrec is SubRecordLightFNAM fnam)
                {
                    entry.DisplayName = fnam.name;
                }
                else if (subrec is SubRecordLightMODL modl)
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
                else if (subrec is SubRecordLightITEX itex)
                {
                    entry.InventoryIcon = itex.icon;
                }
                else if (subrec is SubRecordLightSNAM snam)
                {
                    entry.SoundName = snam.name;
                }
                else if (subrec is SubRecordLightSCRI scri)
                {
                    entry.ScriptName = scri.name;
                }
                else if (subrec is SubRecordLightLHDT lhdt)
                {
                    entry.LightData = lhdt.light;
                }
            }
            
            // Try to get model reference from TESNifLibrary
            if (!string.IsNullOrEmpty(entry.ModelFilename))
            {
                // Sanitize filename before using Path methods to avoid "Illegal characters in path" errors
                string sanitizedFilename = SanitizePath(entry.ModelFilename);
                
                // Normalize path separators (replace backslashes with forward slashes)
                sanitizedFilename = sanitizedFilename.Replace('\\', '/');
                
                // Extract just the filename (strip any subdirectory paths)
                string baseFilename = System.IO.Path.GetFileNameWithoutExtension(sanitizedFilename);
                entry.ModelEntry = TESNifLibrary.GetModelByBaseFilename(baseFilename);
            }
            
            // Store in dictionary (using cleaned ID)
            _lightsById[lightId] = entry;
            
            // Debug: Log when we add lights with streetlight in the name
            #if UNITY_EDITOR
            if (lightId.Contains("light_de_streetlight") || lightId.Contains("streetlight") || lightId.Contains("light_"))
            {
                Debug.Log($"TESLightManager: Added light '{lightId}' (length: {lightId.Length}, from {sourceESM ?? "unknown"}). Model: {entry.ModelFilename ?? "null"}");
                // Show character codes for first and last few characters to detect hidden characters
                if (lightId.Length > 0)
                {
                    string firstChars = string.Join(",", lightId.Take(5).Select(c => $"{(int)c}"));
                    string lastChars = string.Join(",", lightId.TakeLast(5).Select(c => $"{(int)c}"));
                    Debug.Log($"TESLightManager: Light ID character codes - First 5: [{firstChars}], Last 5: [{lastChars}]");
                }
            }
            #endif
            
            return entry;
        }
        
        /// <summary>
        /// Gets a light by ID (case-insensitive lookup)
        /// </summary>
        /// <param name="lightId">The light ID to look up</param>
        /// <returns>The light entry, or null if not found</returns>
        public static LightEntry GetLight(string lightId)
        {
            if (string.IsNullOrEmpty(lightId))
                return null;
            
            // Normalize the light ID before lookup (must match the normalization done in AddLight)
            string cleanedId = NormalizeLightId(lightId);
            
            if (string.IsNullOrEmpty(cleanedId))
                return null;
            
            // Try exact match first (using cleaned ID)
            if (_lightsById.TryGetValue(cleanedId, out var entry))
            {
                // Success - log for streetlight debugging
                if (cleanedId.Contains("light_de_streetlight") || cleanedId.Contains("streetlight"))
                {
                    UnityEngine.Debug.Log($"[TESLightManager] SUCCESS: Found light '{cleanedId}' in dictionary!");
                }
                return entry;
            }
            
            // Lookup failed - always log for streetlight (helps diagnose even with threading)
            if (cleanedId.Contains("light_de_streetlight") || cleanedId.Contains("streetlight"))
            {
                UnityEngine.Debug.LogError($"[TESLightManager] FAILED: Lookup for '{cleanedId}' (length: {cleanedId.Length}). Dictionary has {_lightsById.Count} lights total.");
                
                // Show what keys actually exist that are similar (limit to 5 for performance)
                var similarKeys = _lightsById.Keys.Where(k => k.Contains("streetlight", StringComparison.OrdinalIgnoreCase) || k.Contains("light_de_streetlight", StringComparison.OrdinalIgnoreCase)).Take(5).ToList();
                if (similarKeys.Any())
                {
                    UnityEngine.Debug.LogError($"[TESLightManager] Similar keys in dictionary: {string.Join(", ", similarKeys)}");
                    // Check if any match exactly (case-insensitive)
                    foreach (var key in similarKeys)
                    {
                        bool exactMatch = string.Equals(key, cleanedId, StringComparison.OrdinalIgnoreCase);
                        UnityEngine.Debug.LogError($"[TESLightManager]   Key '{key}' (len: {key.Length}) matches '{cleanedId}': {exactMatch}");
                    }
                }
                else
                {
                    UnityEngine.Debug.LogError($"[TESLightManager] No similar keys found. First 10 lights: {string.Join(", ", _lightsById.Keys.Take(10))}");
                }
            }
            
            // Try original ID if different from cleaned (in case it was stored differently)
            if (!string.Equals(lightId, cleanedId, StringComparison.OrdinalIgnoreCase))
            {
                if (_lightsById.TryGetValue(lightId, out entry))
                {
                    return entry;
                }
            }
            
            // Fallback: Try case-insensitive search through all keys
            // This handles cases where the cleaning might have produced slightly different results
            foreach (var key in _lightsById.Keys)
            {
                if (string.Equals(key, cleanedId, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogWarning($"[TESLightManager] Found case-insensitive match: '{cleanedId}' -> '{key}'");
                    return _lightsById[key];
                }
            }
            
            // Fallback: Try partial match (in case of extra characters or different formatting)
            // Only do this for lights that contain the search term
            if (cleanedId.Contains("light_") || cleanedId.Contains("streetlight"))
            {
                // First try: exact substring match (cleanedId is contained in key or vice versa)
                foreach (var kvp in _lightsById)
                {
                    // Check if the key contains the cleaned ID or vice versa
                    if (kvp.Key.Contains(cleanedId, StringComparison.OrdinalIgnoreCase) || 
                        cleanedId.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        // Make sure it's a reasonable match (not just "light" matching everything)
                        if (Math.Abs(kvp.Key.Length - cleanedId.Length) <= 5)
                        {
                            Debug.LogWarning($"[TESLightManager] Found partial match for '{cleanedId}': '{kvp.Key}'");
                            return kvp.Value;
                        }
                    }
                }
                
                // Second try: find keys that start with the same prefix (e.g., "light_de_streetlight_01")
                string prefix = cleanedId;
                // Remove trailing numbers to get base prefix
                while (prefix.Length > 0 && char.IsDigit(prefix[prefix.Length - 1]))
                {
                    prefix = prefix.Substring(0, prefix.Length - 1);
                }
                
                if (prefix.Length > 10) // Only if we have a meaningful prefix
                {
                    foreach (var kvp in _lightsById)
                    {
                        if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            Debug.LogWarning($"[TESLightManager] Found prefix match for '{cleanedId}' (prefix: '{prefix}'): '{kvp.Key}'");
                            return kvp.Value;
                        }
                    }
                }
            }
            
            // Debug: Log available keys if not found (only for debugging)
            #if UNITY_EDITOR
            if (cleanedId.Contains("light_de_streetlight") || cleanedId.Contains("streetlight"))
            {
                Debug.LogWarning($"TESLightManager: Could not find light '{cleanedId}' (original: '{lightId}'). Total lights: {_lightsById.Count}");
                
                // Show first 20 keys that contain "light" or "street"
                var relevantKeys = _lightsById.Keys.Where(k => k.Contains("light", StringComparison.OrdinalIgnoreCase) || k.Contains("street", StringComparison.OrdinalIgnoreCase)).Take(20);
                Debug.LogWarning($"TESLightManager: Relevant lights in dictionary: {string.Join(", ", relevantKeys)}");
            }
            #endif
            
            return null;
        }
        
        /// <summary>
        /// Checks if a light exists in the library
        /// </summary>
        /// <param name="lightId">The light ID to check</param>
        /// <returns>True if the light exists, false otherwise</returns>
        public static bool HasLight(string lightId)
        {
            return GetLight(lightId) != null;
        }
        
        /// <summary>
        /// Gets all lights in the library
        /// </summary>
        /// <returns>Array of all light entries</returns>
        public static LightEntry[] GetAllLights()
        {
            return _lightsById.Values.ToArray();
        }
        
        /// <summary>
        /// Clears all lights from the library
        /// </summary>
        public static void Clear()
        {
            _lightsById.Clear();
        }
        
        /// <summary>
        /// Gets the number of lights in the library
        /// </summary>
        public static int Count => _lightsById.Count;
        
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
