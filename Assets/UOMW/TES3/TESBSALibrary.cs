using BSASharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ESMSharp.TES3
{
    /// <summary>
    /// Global BSA library for managing BSA archives associated with ESM files.
    /// Automatically associates BSA files with ESM files (same name, .bsa extension).
    /// </summary>
    public static class TESBSALibrary
    {
        /// <summary>
        /// BSA archive entry
        /// </summary>
        public class BSAEntry
        {
            public string BSAFilename { get; set; }           // Full filename (e.g., "Morrowind.bsa")
            public string BSAPath { get; set; }               // Full file path
            public string AssociatedESM { get; set; }         // Associated ESM filename (e.g., "Morrowind.esm")
            public BSA Archive { get; set; }                  // The BSA archive object
            public bool IsLoaded { get; set; }                // Whether this BSA is loaded
            public HashSet<string> FileNames { get; set; }    // Cached list of all file names in the archive
            
            public BSAEntry(string bsaFilename, string bsaPath, string associatedESM)
            {
                BSAFilename = bsaFilename;
                BSAPath = bsaPath;
                AssociatedESM = associatedESM;
                Archive = null;
                IsLoaded = false;
                FileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }
        
        // Dictionary of BSA files keyed by ESM filename
        private static Dictionary<string, BSAEntry> _bsaArchives = new Dictionary<string, BSAEntry>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>
        /// Scans for BSA files associated with loaded ESM files and loads them
        /// </summary>
        /// <param name="dataFolder">Optional custom data folder path (defaults to StreamingAssets/Data)</param>
        /// <returns>Number of BSA files loaded</returns>
        public static int ScanAndLoadBSAs(string dataFolder = null)
        {
            if (dataFolder == null)
            {
                dataFolder = Path.Combine(Application.dataPath, "StreamingAssets", "Data");
            }
            
            if (!Directory.Exists(dataFolder))
            {
                Debug.LogError($"Data folder not found: {dataFolder}");
                return 0;
            }
            
            // Get all loaded ESM files
            string[] esmFiles = TESESMLibrary.GetLoadedESMFilenames();
            
            if (esmFiles.Length == 0)
            {
                Debug.LogWarning("TESBSALibrary: No ESM files loaded. Load ESMs first using TESESMLibrary.");
                return 0;
            }
            
            int loadedCount = 0;
            
            // For each ESM, try to find and load its associated BSA
            foreach (string esmFilename in esmFiles)
            {
                string esmNameWithoutExt = Path.GetFileNameWithoutExtension(esmFilename);
                string bsaFilename = esmNameWithoutExt + ".bsa";
                string bsaPath = Path.Combine(dataFolder, bsaFilename);
                
                if (File.Exists(bsaPath))
                {
                    if (LoadBSA(bsaFilename, bsaPath, esmFilename))
                    {
                        loadedCount++;
                    }
                }
                else
                {
                    Debug.LogWarning($"TESBSALibrary: BSA file not found for ESM '{esmFilename}': {bsaPath}");
                }
            }
            
            Debug.Log($"TESBSALibrary: Loaded {loadedCount} BSA file(s)");
            return loadedCount;
        }
        
        /// <summary>
        /// Loads a BSA file and associates it with an ESM
        /// </summary>
        public static bool LoadBSA(string bsaFilename, string bsaPath = null, string associatedESM = null)
        {
            // Check if already loaded
            string esmKey = associatedESM ?? Path.GetFileNameWithoutExtension(bsaFilename);
            if (_bsaArchives.ContainsKey(esmKey))
            {
                Debug.LogWarning($"BSA file already loaded for ESM '{esmKey}': {bsaFilename}");
                return false;
            }
            
            // Determine path if not provided
            if (bsaPath == null)
            {
                bsaPath = Path.Combine(Application.dataPath, "StreamingAssets", "Data", bsaFilename);
            }
            
            if (!File.Exists(bsaPath))
            {
                Debug.LogError($"BSA file not found: {bsaPath}");
                return false;
            }
            
            // Determine associated ESM if not provided
            if (associatedESM == null)
            {
                string esmNameWithoutExt = Path.GetFileNameWithoutExtension(bsaFilename);
                associatedESM = esmNameWithoutExt + ".esm";
            }
            
            try
            {
                BSAEntry entry = new BSAEntry(bsaFilename, bsaPath, associatedESM);
                entry.Archive = new BSA();
                entry.Archive.Open(bsaPath);
                entry.IsLoaded = true;
                
                // Cache file names for faster lookups
                entry.FileNames = new HashSet<string>(entry.Archive.GetFileNames(), StringComparer.OrdinalIgnoreCase);
                
                _bsaArchives[esmKey] = entry;
                
                Debug.Log($"TESBSALibrary: Loaded BSA '{bsaFilename}' ({entry.FileNames.Count} files) for ESM '{associatedESM}'");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"TESBSALibrary: Failed to load BSA '{bsaFilename}': {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Gets the BSA archive for a specific ESM file
        /// </summary>
        public static BSA GetBSAForESM(string esmFilename)
        {
            string esmKey = Path.GetFileNameWithoutExtension(esmFilename);
            if (_bsaArchives.TryGetValue(esmKey, out BSAEntry entry))
            {
                return entry.Archive;
            }
            return null;
        }
        
        /// <summary>
        /// Gets the BSA entry for a specific ESM file
        /// </summary>
        public static BSAEntry GetBSAEntryForESM(string esmFilename)
        {
            string esmKey = Path.GetFileNameWithoutExtension(esmFilename);
            _bsaArchives.TryGetValue(esmKey, out BSAEntry entry);
            return entry;
        }
        
        /// <summary>
        /// Extracts a file from BSA archives (checks in load order)
        /// </summary>
        /// <param name="fileName">Name of the file to extract</param>
        /// <param name="outputPath">Output path for the extracted file</param>
        /// <param name="preferredESM">Optional: prefer this ESM's BSA first</param>
        /// <returns>True if file was found and extracted, false otherwise</returns>
        public static bool ExtractFile(string fileName, string outputPath, string preferredESM = null)
        {
            // Try preferred ESM first if specified
            if (!string.IsNullOrEmpty(preferredESM))
            {
                BSAEntry entry = GetBSAEntryForESM(preferredESM);
                if (entry != null && entry.IsLoaded && entry.FileNames.Contains(fileName))
                {
                    try
                    {
                        BSAFileEntry fileEntry = entry.Archive.GetFileEntry(fileName);
                        if (fileEntry != null)
                        {
                            byte[] data = entry.Archive.ExtractFile(fileEntry);
                            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                            File.WriteAllBytes(outputPath, data);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"TESBSALibrary: Failed to extract '{fileName}' from '{entry.BSAFilename}': {ex.Message}");
                    }
                }
            }
            
            // Try all BSAs in load order (same as ESM load order)
            var esmEntries = TESESMLibrary.GetLoadedESMEntries();
            foreach (var esmEntry in esmEntries)
            {
                BSAEntry bsaEntry = GetBSAEntryForESM(esmEntry.ESMFilename);
                if (bsaEntry != null && bsaEntry.IsLoaded && bsaEntry.FileNames.Contains(fileName))
                {
                    try
                    {
                        BSAFileEntry fileEntry = bsaEntry.Archive.GetFileEntry(fileName);
                        if (fileEntry != null)
                        {
                            byte[] data = bsaEntry.Archive.ExtractFile(fileEntry);
                            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                            File.WriteAllBytes(outputPath, data);
                            Debug.Log($"TESBSALibrary: Extracted '{fileName}' from '{bsaEntry.BSAFilename}'");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"TESBSALibrary: Failed to extract '{fileName}' from '{bsaEntry.BSAFilename}': {ex.Message}");
                    }
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Checks if a file exists in any loaded BSA archive
        /// </summary>
        public static bool FileExists(string fileName, string preferredESM = null)
        {
            // Try preferred ESM first if specified
            if (!string.IsNullOrEmpty(preferredESM))
            {
                BSAEntry entry = GetBSAEntryForESM(preferredESM);
                if (entry != null && entry.IsLoaded && entry.FileNames.Contains(fileName))
                {
                    return true;
                }
            }
            
            // Check all BSAs
            foreach (var entry in _bsaArchives.Values)
            {
                if (entry.IsLoaded && entry.FileNames.Contains(fileName))
                {
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Gets all file names from all loaded BSA archives
        /// </summary>
        public static HashSet<string> GetAllFileNames()
        {
            var allFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in _bsaArchives.Values)
            {
                if (entry.IsLoaded)
                {
                    foreach (string fileName in entry.FileNames)
                    {
                        allFiles.Add(fileName);
                    }
                }
            }
            return allFiles;
        }
        
        /// <summary>
        /// Gets all file names from a specific ESM's BSA
        /// </summary>
        public static HashSet<string> GetFileNamesForESM(string esmFilename)
        {
            BSAEntry entry = GetBSAEntryForESM(esmFilename);
            if (entry != null && entry.IsLoaded)
            {
                return new HashSet<string>(entry.FileNames, StringComparer.OrdinalIgnoreCase);
            }
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        
        /// <summary>
        /// Closes all BSA archives
        /// </summary>
        public static void CloseAll()
        {
            foreach (var entry in _bsaArchives.Values)
            {
                if (entry.IsLoaded && entry.Archive != null)
                {
                    try
                    {
                        entry.Archive.Close();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"TESBSALibrary: Error closing BSA '{entry.BSAFilename}': {ex.Message}");
                    }
                }
            }
        }
        
        /// <summary>
        /// Clears all loaded BSA files
        /// </summary>
        public static void Clear()
        {
            CloseAll();
            _bsaArchives.Clear();
        }
        
        /// <summary>
        /// Gets the number of loaded BSA files
        /// </summary>
        public static int Count => _bsaArchives.Count;
    }
}
