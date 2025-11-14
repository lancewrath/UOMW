using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ESMSharp.TES3
{
    /// <summary>
    /// Global script library for managing Morrowind scripts parsed from ESM files.
    /// Stores script information including name, source code, variables, and metadata.
    /// Scripts can be accessed by name like a dictionary for easy runtime script execution.
    /// </summary>
    public static class TESMWScriptManager
    {
        /// <summary>
        /// Script entry containing all relevant script information
        /// </summary>
        public class ScriptEntry
        {
            public string ScriptName { get; set; }              // Script name (from SCHD header)
            public string SourceCode { get; set; }             // Full script source code (from SCTX)
            public string[] Variables { get; set; }             // Variable names (from SCVR)
            public MWScriptHeader Header { get; set; }            // Script header metadata
            public string CacheFilename { get; set; }           // Cached filename (e.g., "scriptname.mws")
            public string CachePath { get; set; }               // Full path to cached file
            public string SourceESM { get; set; }               // ESM file this script came from
            
            public ScriptEntry(string scriptName, string sourceCode, string[] variables, MWScriptHeader header, string cacheFilename = null, string sourceESM = null)
            {
                ScriptName = scriptName;
                SourceCode = sourceCode;
                Variables = variables ?? new string[0];
                Header = header;
                CacheFilename = cacheFilename;
                SourceESM = sourceESM;
                
                // Set cache path if filename provided
                if (!string.IsNullOrEmpty(cacheFilename))
                {
                    CachePath = Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Scripts", cacheFilename);
                }
            }
        }
        
        // Dictionary for fast lookups by script name (case-insensitive)
        private static Dictionary<string, ScriptEntry> _scriptsByName = new Dictionary<string, ScriptEntry>(StringComparer.OrdinalIgnoreCase);
        
        // Dictionary for lookups by cache filename
        private static Dictionary<string, ScriptEntry> _scriptsByFilename = new Dictionary<string, ScriptEntry>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>
        /// Adds a script to the library. If a script with the same name already exists, it will be overwritten.
        /// This is called automatically when scripts are parsed from ESM files.
        /// </summary>
        /// <param name="scriptName">Script name (from SCHD header)</param>
        /// <param name="sourceCode">Full script source code (from SCTX)</param>
        /// <param name="variables">Variable names array (from SCVR, can be null)</param>
        /// <param name="header">Script header metadata (from SCHD)</param>
        /// <param name="cacheFilename">Optional cached filename (e.g., "scriptname.mws")</param>
        /// <param name="sourceESM">Optional ESM file this script came from</param>
        /// <returns>The script entry (newly created or updated)</returns>
        public static ScriptEntry AddScript(string scriptName, string sourceCode, string[] variables, MWScriptHeader header, string cacheFilename = null, string sourceESM = null)
        {
            if (string.IsNullOrEmpty(scriptName))
            {
                Debug.LogWarning("TESMWScriptManager: Attempted to add script with empty name");
                return null;
            }
            
            if (string.IsNullOrEmpty(sourceCode))
            {
                Debug.LogWarning($"TESMWScriptManager: Attempted to add script '{scriptName}' with empty source code");
                return null;
            }
            
            // Generate cache filename if not provided
            if (string.IsNullOrEmpty(cacheFilename))
            {
                cacheFilename = SubRecordSctpSCTX.SanitizeFileName(scriptName) + ".mws";
            }
            
            // Check if script already exists
            bool isUpdate = _scriptsByName.ContainsKey(scriptName);
            
            // Create or update entry
            ScriptEntry entry = new ScriptEntry(scriptName, sourceCode, variables, header, cacheFilename, sourceESM);
            
            // Add/update in dictionaries
            _scriptsByName[scriptName] = entry;
            _scriptsByFilename[cacheFilename] = entry;
            
            if (isUpdate)
            {
                Debug.Log($"TESMWScriptManager: Updated script '{scriptName}' (from {sourceESM ?? "unknown"})");
            }
            else
            {
                // Debug.Log($"TESMWScriptManager: Added script '{scriptName}' (from {sourceESM ?? "unknown"})"); // Commented out for performance
            }
            
            return entry;
        }
        
        /// <summary>
        /// Gets a script by name. Returns null if not found.
        /// This is the primary method for accessing scripts at runtime.
        /// </summary>
        /// <param name="scriptName">Script name (case-insensitive)</param>
        /// <returns>The script entry if found, null otherwise</returns>
        public static ScriptEntry GetScript(string scriptName)
        {
            if (string.IsNullOrEmpty(scriptName))
                return null;
                
            _scriptsByName.TryGetValue(scriptName, out ScriptEntry entry);
            return entry;
        }
        
        /// <summary>
        /// Gets a script by cache filename. Returns null if not found.
        /// </summary>
        /// <param name="cacheFilename">Cache filename (e.g., "scriptname.mws")</param>
        /// <returns>The script entry if found, null otherwise</returns>
        public static ScriptEntry GetScriptByFilename(string cacheFilename)
        {
            if (string.IsNullOrEmpty(cacheFilename))
                return null;
                
            _scriptsByFilename.TryGetValue(cacheFilename, out ScriptEntry entry);
            return entry;
        }
        
        /// <summary>
        /// Gets the source code of a script by name. Returns null if not found.
        /// This is a convenience method for quick script source lookups.
        /// </summary>
        /// <param name="scriptName">Script name (case-insensitive)</param>
        /// <returns>The script source code if found, null otherwise</returns>
        public static string GetScriptSource(string scriptName)
        {
            ScriptEntry entry = GetScript(scriptName);
            return entry?.SourceCode;
        }
        
        /// <summary>
        /// Checks if a script exists in the library.
        /// </summary>
        /// <param name="scriptName">Script name (case-insensitive)</param>
        /// <returns>True if the script exists, false otherwise</returns>
        public static bool HasScript(string scriptName)
        {
            if (string.IsNullOrEmpty(scriptName))
                return false;
                
            return _scriptsByName.ContainsKey(scriptName);
        }
        
        /// <summary>
        /// Loads all scripts from the cache directory.
        /// This can be called on startup to preload scripts that were previously cached.
        /// </summary>
        /// <param name="cacheDir">Optional custom cache directory (defaults to StreamingAssets/Data/UOMW/Cache/Scripts)</param>
        /// <returns>Number of scripts loaded</returns>
        public static int LoadScriptsFromCache(string cacheDir = null)
        {
            if (cacheDir == null)
            {
                cacheDir = Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Scripts");
            }
            
            if (!Directory.Exists(cacheDir))
            {
                Debug.LogWarning($"TESMWScriptManager: Script cache directory not found: {cacheDir}");
                return 0;
            }
            
            // Find all .mws files
            string[] scriptFiles = Directory.GetFiles(cacheDir, "*.mws", SearchOption.TopDirectoryOnly);
            
            if (scriptFiles.Length == 0)
            {
                Debug.Log($"TESMWScriptManager: No script files found in cache directory: {cacheDir}");
                return 0;
            }
            
            int loadedCount = 0;
            foreach (string scriptPath in scriptFiles)
            {
                try
                {
                    string filename = Path.GetFileName(scriptPath);
                    string sourceCode = File.ReadAllText(scriptPath);
                    
                    // Extract script name from filename (remove .mws extension and sanitize)
                    string scriptName = Path.GetFileNameWithoutExtension(filename);
                    
                    // Try to find the original script name (may have underscores from sanitization)
                    // For now, we'll use the filename as the script name
                    // In a full implementation, you might want to parse the script to find the actual name
                    
                    // Create a minimal header (we don't have full header info from cache)
                    MWScriptHeader header = new MWScriptHeader(
                        scriptName,
                        0, // numshorts - unknown from cache
                        0, // numlongs - unknown from cache
                        0, // numfloats - unknown from cache
                        (uint)sourceCode.Length, // datasize - approximate
                        0  // varsize - unknown from cache
                    );
                    
                    ScriptEntry entry = AddScript(scriptName, sourceCode, null, header, filename);
                    if (entry != null)
                    {
                        loadedCount++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"TESMWScriptManager: Failed to load script from {scriptPath}: {ex.Message}");
                }
            }
            
            Debug.Log($"TESMWScriptManager: Loaded {loadedCount} script(s) from cache directory");
            return loadedCount;
        }
        
        /// <summary>
        /// Gets all script entries in the library.
        /// </summary>
        /// <returns>Collection of all script entries</returns>
        public static IEnumerable<ScriptEntry> GetAllScripts()
        {
            return _scriptsByName.Values;
        }
        
        /// <summary>
        /// Gets all script names in the library.
        /// </summary>
        /// <returns>Array of script names</returns>
        public static string[] GetAllScriptNames()
        {
            return _scriptsByName.Keys.ToArray();
        }
        
        /// <summary>
        /// Clears all scripts from the library.
        /// </summary>
        public static void Clear()
        {
            _scriptsByName.Clear();
            _scriptsByFilename.Clear();
            Debug.Log("TESMWScriptManager: Cleared all scripts");
        }
        
        /// <summary>
        /// Gets the total number of scripts in the library.
        /// </summary>
        public static int Count => _scriptsByName.Count;
    }
}
