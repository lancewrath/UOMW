using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BSASharp;

namespace ESMSharp.TES3
{
    /// <summary>
    /// Global texture library for caching and reusing textures loaded during terrain generation and static loading.
    /// Stores texture information including name, ID, filename, and the Texture2D object itself.
    /// </summary>
    public static class TESLTextureLibrary
    {
        /// <summary>
        /// Texture entry containing all relevant texture information
        /// </summary>
        public class TextureEntry
        {
            public string Name { get; set; }              // Texture name from LTEX record
            public int LTEXIndex { get; set; }             // LTEX index (texture record index)
            public ushort VTEXIndex { get; set; }          // VTEX index (VTEX = LTEX + 1, used for terrain)
            public string Filename { get; set; }          // Full file path or filename
            public string ESMFilename { get; set; }        // ESM filename this texture came from (for BSA lookup)
            public Texture2D Texture { get; set; }        // The actual Texture2D object
            public Texture2D NormalMap { get; set; }      // Normal map texture (generated or loaded)
            
            public TextureEntry(string name, int ltexIndex, ushort vtexIndex, string filename, Texture2D texture)
            {
                Name = name;
                LTEXIndex = ltexIndex;
                VTEXIndex = vtexIndex;
                Filename = filename;
                ESMFilename = null;
                Texture = texture;
                NormalMap = null;
            }
        }
        
        // Cache dictionaries for fast lookups
        private static Dictionary<string, TextureEntry> _cacheByPath = new Dictionary<string, TextureEntry>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<int, TextureEntry> _cacheByLTEXIndex = new Dictionary<int, TextureEntry>();
        private static Dictionary<ushort, TextureEntry> _cacheByVTEXIndex = new Dictionary<ushort, TextureEntry>();
        private static Dictionary<string, TextureEntry> _cacheByName = new Dictionary<string, TextureEntry>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>
        /// Adds a texture to the library. If it already exists (by path), returns the existing entry.
        /// </summary>
        /// <param name="name">Texture name from LTEX record</param>
        /// <param name="ltexIndex">LTEX index (texture record index)</param>
        /// <param name="vtexIndex">VTEX index (VTEX = LTEX + 1, used for terrain)</param>
        /// <param name="filename">Full file path or filename</param>
        /// <param name="texture">The Texture2D object</param>
        /// <returns>The texture entry (existing if found, or newly created)</returns>
        public static TextureEntry AddTexture(string name, int ltexIndex, ushort vtexIndex, string filename, Texture2D texture)
        {
            if (texture == null)
            {
                Debug.LogWarning($"TESLTextureLibrary: Attempted to add null texture for {filename}");
                return null;
            }
            
            // Normalize path for consistent lookups
            string normalizedPath = System.IO.Path.GetFullPath(filename).ToLowerInvariant();
            
            // Check if already exists by path
            if (_cacheByPath.TryGetValue(normalizedPath, out TextureEntry existing))
            {
                // Update the entry if needed (in case texture object changed)
                if (existing.Texture != texture)
                {
                    existing.Texture = texture;
                }
                return existing;
            }
            
            // Create new entry
            TextureEntry entry = new TextureEntry(name, ltexIndex, vtexIndex, filename, texture);
            
            // Add to all cache dictionaries
            _cacheByPath[normalizedPath] = entry;
            
            if (ltexIndex >= 0)
            {
                _cacheByLTEXIndex[ltexIndex] = entry;
            }
            
            if (vtexIndex > 0) // VTEX 0 is default, skip it
            {
                _cacheByVTEXIndex[vtexIndex] = entry;
            }
            
            if (!string.IsNullOrEmpty(name))
            {
                _cacheByName[name] = entry;
            }
            
            return entry;
        }
        
        /// <summary>
        /// Gets a texture by file path. Returns null if not found.
        /// </summary>
        public static TextureEntry GetTextureByPath(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return null;
                
            string normalizedPath = System.IO.Path.GetFullPath(filename).ToLowerInvariant();
            _cacheByPath.TryGetValue(normalizedPath, out TextureEntry entry);
            return entry;
        }
        
        /// <summary>
        /// Gets a texture by LTEX index. Returns null if not found.
        /// </summary>
        public static TextureEntry GetTextureByLTEXIndex(int ltexIndex)
        {
            _cacheByLTEXIndex.TryGetValue(ltexIndex, out TextureEntry entry);
            return entry;
        }
        
        /// <summary>
        /// Gets a texture by VTEX index. Returns null if not found.
        /// </summary>
        public static TextureEntry GetTextureByVTEXIndex(ushort vtexIndex)
        {
            if (vtexIndex == 0)
                return null; // VTEX 0 is default texture
                
            _cacheByVTEXIndex.TryGetValue(vtexIndex, out TextureEntry entry);
            return entry;
        }
        
        /// <summary>
        /// Gets a texture by name. Returns null if not found.
        /// </summary>
        public static TextureEntry GetTextureByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
                
            _cacheByName.TryGetValue(name, out TextureEntry entry);
            return entry;
        }
        
        /// <summary>
        /// Registers an LTEX record in the library (without loading the texture yet).
        /// This should be called when processing ESM files to pre-register all texture records.
        /// </summary>
        /// <param name="name">Texture name from LTEX NAME subrecord (will be sanitized)</param>
        /// <param name="ltexIndex">LTEX index from INTV subrecord</param>
        /// <param name="filename">Texture filename from DATA subrecord (will be sanitized)</param>
        /// <param name="esmFilename">ESM filename this record came from</param>
        /// <returns>The registered texture entry (or existing entry if already registered)</returns>
        public static TextureEntry RegisterLTEXRecord(string name, int ltexIndex, string filename, string esmFilename = null)
        {
            // Sanitize name (remove null characters, trim whitespace)
            if (!string.IsNullOrEmpty(name))
            {
                name = name.TrimEnd('\0', ' ', '\t', '\r', '\n');
                name = name.Replace("\0", "");
                name = name.Trim();
            }
            
            // Sanitize filename (remove null characters, trim whitespace)
            if (!string.IsNullOrEmpty(filename))
            {
                filename = filename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                filename = filename.Replace("\0", "");
                filename = filename.Trim();
            }
            
            // Calculate VTEX index (VTEX = LTEX + 1)
            ushort vtexIndex = (ushort)(ltexIndex + 1);
            
            // Check if already registered by LTEX index
            if (_cacheByLTEXIndex.TryGetValue(ltexIndex, out TextureEntry existing))
            {
                // Update name and filename if they're missing or different
                if (string.IsNullOrEmpty(existing.Name) && !string.IsNullOrEmpty(name))
                {
                    existing.Name = name;
                    if (!string.IsNullOrEmpty(name))
                    {
                        _cacheByName[name] = existing;
                    }
                }
                if (string.IsNullOrEmpty(existing.Filename) && !string.IsNullOrEmpty(filename))
                {
                    existing.Filename = filename;
                }
                return existing;
            }
            
            // Create new entry (without Texture2D - will be loaded later)
            TextureEntry entry = new TextureEntry(name ?? "", ltexIndex, vtexIndex, filename ?? "", null);
            entry.ESMFilename = esmFilename; // Store which ESM this texture came from
            
            // Add to cache dictionaries
            if (ltexIndex >= 0)
            {
                _cacheByLTEXIndex[ltexIndex] = entry;
            }
            
            if (vtexIndex > 0) // VTEX 0 is default, skip it
            {
                _cacheByVTEXIndex[vtexIndex] = entry;
            }
            
            if (!string.IsNullOrEmpty(name))
            {
                _cacheByName[name] = entry;
            }
            
            return entry;
        }
        
        /// <summary>
        /// Gets the Texture2D object by file path. Returns null if not found.
        /// This is a convenience method for quick texture lookups.
        /// </summary>
        public static Texture2D GetTexture(string filename)
        {
            TextureEntry entry = GetTextureByPath(filename);
            return entry?.Texture;
        }
        
        /// <summary>
        /// Gets the normal map texture by file path. Returns null if not found or not generated.
        /// </summary>
        public static Texture2D GetNormalMap(string filename)
        {
            TextureEntry entry = GetTextureByPath(filename);
            return entry?.NormalMap;
        }
        
        /// <summary>
        /// Gets the normal map texture by VTEX index. Returns null if not found or not generated.
        /// </summary>
        public static Texture2D GetNormalMapByVTEXIndex(ushort vtexIndex)
        {
            TextureEntry entry = GetTextureByVTEXIndex(vtexIndex);
            return entry?.NormalMap;
        }
        
        /// <summary>
        /// Gets the normal map texture by LTEX index. Returns null if not found or not generated.
        /// </summary>
        public static Texture2D GetNormalMapByLTEXIndex(int ltexIndex)
        {
            TextureEntry entry = GetTextureByLTEXIndex(ltexIndex);
            return entry?.NormalMap;
        }
        
        /// <summary>
        /// Checks if a texture exists in the library by file path.
        /// </summary>
        public static bool HasTexture(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return false;
                
            string normalizedPath = System.IO.Path.GetFullPath(filename).ToLowerInvariant();
            return _cacheByPath.ContainsKey(normalizedPath);
        }
        
        /// <summary>
        /// Checks if a texture exists in the library by LTEX index.
        /// </summary>
        public static bool HasTextureByLTEXIndex(int ltexIndex)
        {
            return _cacheByLTEXIndex.ContainsKey(ltexIndex);
        }
        
        /// <summary>
        /// Checks if a texture exists in the library by VTEX index.
        /// </summary>
        public static bool HasTextureByVTEXIndex(ushort vtexIndex)
        {
            if (vtexIndex == 0)
                return false;
            return _cacheByVTEXIndex.ContainsKey(vtexIndex);
        }
        
        /// <summary>
        /// Gets or loads a texture. If the texture exists in the library, returns it.
        /// Otherwise, loads it from the file system and adds it to the library.
        /// </summary>
        /// <param name="filename">Full file path to the texture</param>
        /// <param name="name">Optional texture name (from LTEX record)</param>
        /// <param name="ltexIndex">Optional LTEX index</param>
        /// <param name="vtexIndex">Optional VTEX index</param>
        /// <param name="loadTextureFunc">Function to load the texture if not cached (returns Texture2D or null)</param>
        /// <returns>The texture entry, or null if loading failed</returns>
        public static TextureEntry GetOrLoadTexture(string filename, string name = null, int ltexIndex = -1, ushort vtexIndex = 0, Func<Texture2D> loadTextureFunc = null)
        {
            // Check if already cached
            TextureEntry existing = GetTextureByPath(filename);
            if (existing != null)
            {
                return existing;
            }
            
            // Try to load if load function provided
            if (loadTextureFunc != null)
            {
                Texture2D texture = loadTextureFunc();
                if (texture != null)
                {
                    return AddTexture(name, ltexIndex, vtexIndex, filename, texture);
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Clears all cached textures from the library.
        /// </summary>
        public static void Clear()
        {
            _cacheByPath.Clear();
            _cacheByLTEXIndex.Clear();
            _cacheByVTEXIndex.Clear();
            _cacheByName.Clear();
        }
        
        /// <summary>
        /// Gets the total number of textures in the library.
        /// </summary>
        public static int Count => _cacheByPath.Count;
        
        /// <summary>
        /// Gets all texture entries in the library.
        /// </summary>
        public static IEnumerable<TextureEntry> GetAllTextures()
        {
            return _cacheByPath.Values;
        }
        
        /// <summary>
        /// Loads or gets a texture with automatic conversion and BSA extraction.
        /// Priority: 1) PNG in cache, 2) DDS/TGA in cache (convert to PNG), 3) Extract from BSA and convert.
        /// </summary>
        /// <param name="textureName">Base texture name (e.g., "Tx_scrubplain_01")</param>
        /// <param name="textureDir">Directory to search for textures (e.g., Cache/Textures/morrowind)</param>
        /// <param name="esmName">ESM name for BSA extraction (e.g., "morrowind")</param>
        /// <param name="ltexIndex">LTEX index</param>
        /// <param name="vtexIndex">VTEX index</param>
        /// <param name="convertDDSToPNGFunc">Function to convert DDS to PNG (from TESTerrain)</param>
        /// <param name="setTextureImportSettingsFunc">Optional function to set texture import settings</param>
        /// <returns>The texture entry if found/loaded, null otherwise</returns>
        public static TextureEntry LoadOrGetTexture(
            string textureName, 
            string textureDir, 
            string esmName,
            int ltexIndex = -1, 
            ushort vtexIndex = 0,
            Func<byte[], string, bool> convertDDSToPNGFunc = null,
            Action<string> setTextureImportSettingsFunc = null)
        {
            if (string.IsNullOrEmpty(textureName) || string.IsNullOrEmpty(textureDir))
                return null;
            
            // Step 0: Check for pre-registered LTEX record and use its filename if available
            TextureEntry registeredEntry = null;
            if (ltexIndex >= 0)
            {
                registeredEntry = GetTextureByLTEXIndex(ltexIndex);
            }
            else if (vtexIndex > 0)
            {
                registeredEntry = GetTextureByVTEXIndex(vtexIndex);
            }
            
            // If we have a registered entry with a filename, prefer that over the textureName parameter
            string originalTextureName = textureName;
            string originalTexturePath = textureName; // May include path like "textures\filename.dds"
            string sourceESMFilename = esmName; // Default to provided esmName
            
            if (registeredEntry != null && !string.IsNullOrEmpty(registeredEntry.Filename))
            {
                // Use the filename from the LTEX record (this is the actual texture filename)
                originalTextureName = registeredEntry.Filename;
                originalTexturePath = registeredEntry.Filename;
                
                // Use the ESM filename from the registered entry if available (more accurate than esmName parameter)
                if (!string.IsNullOrEmpty(registeredEntry.ESMFilename))
                {
                    sourceESMFilename = System.IO.Path.GetFileNameWithoutExtension(registeredEntry.ESMFilename);
                }
                
                // If the registered entry already has a loaded texture, return it
                if (registeredEntry.Texture != null)
                {
                    Debug.Log($"TESLTextureLibrary: Reusing loaded texture from LTEX record (LTEX {ltexIndex}, VTEX {vtexIndex}): {registeredEntry.Filename}");
                    return registeredEntry;
                }
            }
            
            // Remove extension from texture name for cache searching
            string baseName = System.IO.Path.GetFileNameWithoutExtension(originalTextureName);
            
            // Step 1: Check if already cached by VTEX index
            if (vtexIndex > 0)
            {
                TextureEntry cached = GetTextureByVTEXIndex(vtexIndex);
                if (cached != null && cached.Texture != null)
                {
                    Debug.Log($"TESLTextureLibrary: Reusing cached texture (VTEX {vtexIndex}): {cached.Filename}");
                    return cached;
                }
            }
            
            // Step 2: Try PNG first (converted files)
            string[] extensions = new[] { ".png", ".dds", ".tga" };
            string foundPath = null;
            string foundExtension = null;
            
            // Normalize texture directory path to ensure consistent separators
            textureDir = System.IO.Path.GetFullPath(textureDir);
            //Debug.Log($"TESLTextureLibrary: Searching for texture '{baseName}' in directory: {textureDir}");
            
            foreach (string ext in extensions)
            {
                string searchPath = System.IO.Path.Combine(textureDir, baseName + ext);
                // Normalize the search path
                searchPath = System.IO.Path.GetFullPath(searchPath);
                if (System.IO.File.Exists(searchPath))
                {
                    foundPath = searchPath;
                    foundExtension = ext;
                    //Debug.Log($"TESLTextureLibrary: Found texture file: {System.IO.Path.GetFileName(searchPath)} (full path: {searchPath})");
                    break;
                }
            }
            
            if (foundPath == null)
            {
                // Debug.Log($"TESLTextureLibrary: Texture '{baseName}' not found in cache directory"); // Commented out for performance
            }
            
            // Step 3: If DDS/TGA found, convert to PNG
            if (foundPath != null && (foundExtension == ".dds" || foundExtension == ".tga"))
            {
                string pngPath = System.IO.Path.ChangeExtension(foundPath, ".png");
                
                // Check if PNG already exists
                if (!System.IO.File.Exists(pngPath))
                {
                    // Convert DDS/TGA to PNG
                    if (foundExtension == ".dds" && convertDDSToPNGFunc != null)
                    {
                        try
                        {
                            byte[] ddsData = System.IO.File.ReadAllBytes(foundPath);
                            if (convertDDSToPNGFunc(ddsData, pngPath))
                            {
                                Debug.Log($"TESLTextureLibrary: Converted DDS to PNG: {System.IO.Path.GetFileName(foundPath)} -> {System.IO.Path.GetFileName(pngPath)}");
                                #if UNITY_EDITOR
                                setTextureImportSettingsFunc?.Invoke(pngPath);
                                #endif
                                foundPath = pngPath;
                                foundExtension = ".png";
                            }
                            else
                            {
                                Debug.LogWarning($"TESLTextureLibrary: Failed to convert DDS to PNG: {System.IO.Path.GetFileName(foundPath)}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"TESLTextureLibrary: Error converting DDS to PNG: {ex.Message}");
                        }
                    }
                    else if (foundExtension == ".tga")
                    {
                        try
                        {
                            // Load TGA and convert to PNG
                            byte[] tgaData = System.IO.File.ReadAllBytes(foundPath);
                            Texture2D tempTexture = new Texture2D(2, 2);
                            if (tempTexture.LoadImage(tgaData))
                            {
                                byte[] pngData = tempTexture.EncodeToPNG();
                                System.IO.File.WriteAllBytes(pngPath, pngData);
                                UnityEngine.Object.DestroyImmediate(tempTexture);
                                #if UNITY_EDITOR
                                setTextureImportSettingsFunc?.Invoke(pngPath);
                                #endif
                                Debug.Log($"TESLTextureLibrary: Converted TGA to PNG: {System.IO.Path.GetFileName(foundPath)} -> {System.IO.Path.GetFileName(pngPath)}");
                                foundPath = pngPath;
                                foundExtension = ".png";
                            }
                            else
                            {
                                UnityEngine.Object.DestroyImmediate(tempTexture);
                                Debug.LogWarning($"TESLTextureLibrary: Failed to load TGA: {System.IO.Path.GetFileName(foundPath)}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"TESLTextureLibrary: Error converting TGA to PNG: {ex.Message}");
                        }
                    }
                }
                else
                {
                    // PNG already exists, use it
                    foundPath = pngPath;
                    foundExtension = ".png";
                }
            }
            
            // Step 4: If still not found, try extracting from BSA
            if (foundPath == null || !System.IO.File.Exists(foundPath))
            {
                // Debug.Log($"TESLTextureLibrary: Texture not found in cache, trying BSA extraction for: {baseName} (original: '{originalTextureName}')"); // Commented out for performance
                
                // Get all file names from all BSAs for searching (matching old GatherLandTextures approach)
                HashSet<string> allBSAFiles = TESBSALibrary.GetAllFileNames();
                
                if (allBSAFiles.Count == 0)
                {
                    Debug.LogWarning($"TESLTextureLibrary: No BSA files loaded or no files found in BSAs");
                }
                
                // Preserve the original extension from the textureName (records may have .tga extension even if file is .dds)
                string originalExtension = System.IO.Path.GetExtension(originalTextureName);
                if (string.IsNullOrEmpty(originalExtension))
                {
                    originalExtension = ".dds"; // Default to .dds if no extension
                }
                originalExtension = originalExtension.ToLower();
                
                // Extract base filename from original texture name (matching old code approach)
                string originalBaseFilename = System.IO.Path.GetFileName(originalTextureName);
                string originalBaseNameNoExt = System.IO.Path.GetFileNameWithoutExtension(originalBaseFilename);
                
                // Build path variations - PRIORITIZE original extension from LTEX record
                // Strategy: Try exact filename with original extension first, then path variations with original extension,
                // then fall back to other extensions only if original extension fails
                List<string> pathsToTryOriginalExt = new List<string>(); // Paths with original extension (try first)
                List<string> pathsToTryOtherExts = new List<string>();  // Paths with other extensions (try second)
                
                // First, try the original texture name/path as-is with original extension
                if (!string.IsNullOrEmpty(originalTexturePath))
                {
                    // Original path with normalized separators (matching old code)
                    pathsToTryOriginalExt.Add(originalTexturePath.Replace('/', '\\'));
                    // Just the filename from the original path (should already have extension)
                    pathsToTryOriginalExt.Add(originalBaseFilename);
                }
                
                // Then try common path variations with ORIGINAL extension first
                pathsToTryOriginalExt.Add($"textures\\{originalBaseFilename}");  // textures\filename.ext (original)
                pathsToTryOriginalExt.Add($"textures\\landscape\\{originalBaseFilename}");  // textures\landscape\filename.ext (original)
                pathsToTryOriginalExt.Add($"textures\\terrain\\{originalBaseFilename}");  // textures\terrain\filename.ext (original)
                pathsToTryOriginalExt.Add($"textures\\tx\\{originalBaseFilename}");  // textures\tx\filename.ext (original)
                
                // Also add just the base name with original extension (in case originalBaseFilename didn't have extension)
                if (!originalBaseFilename.EndsWith(originalExtension, StringComparison.OrdinalIgnoreCase))
                {
                    pathsToTryOriginalExt.Add(originalBaseNameNoExt + originalExtension);  // Just filename with original extension
                    pathsToTryOriginalExt.Add($"textures\\{originalBaseNameNoExt + originalExtension}");  // textures\filename.ext (original)
                    pathsToTryOriginalExt.Add($"textures\\landscape\\{originalBaseNameNoExt + originalExtension}");  // textures\landscape\filename.ext (original)
                    pathsToTryOriginalExt.Add($"textures\\terrain\\{originalBaseNameNoExt + originalExtension}");  // textures\terrain\filename.ext (original)
                    pathsToTryOriginalExt.Add($"textures\\tx\\{originalBaseNameNoExt + originalExtension}");  // textures\tx\filename.ext (original)
                }
                
                // Now build fallback paths with other extensions (only if original extension doesn't work)
                List<string> otherExtensions = new List<string>();
                if (originalExtension != ".dds") otherExtensions.Add(".dds");
                if (originalExtension != ".tga") otherExtensions.Add(".tga");
                if (originalExtension != ".png") otherExtensions.Add(".png");
                
                foreach (string ext in otherExtensions)
                {
                    pathsToTryOtherExts.Add(originalBaseNameNoExt + ext);  // Just filename with extension
                    pathsToTryOtherExts.Add($"textures\\{originalBaseNameNoExt + ext}");  // textures\filename.ext
                    pathsToTryOtherExts.Add($"textures\\landscape\\{originalBaseNameNoExt + ext}");  // textures\landscape\filename.ext
                    pathsToTryOtherExts.Add($"textures\\terrain\\{originalBaseNameNoExt + ext}");  // textures\terrain\filename.ext
                    pathsToTryOtherExts.Add($"textures\\tx\\{originalBaseNameNoExt + ext}");  // textures\tx\filename.ext
                }
                
                string foundBSAPath = null;
                string foundExt = null;
                
                // FIRST: Try paths with original extension (from LTEX record)
                foreach (string bsaFileName in pathsToTryOriginalExt)
                {
                    // Check in our hashset first (faster, case-insensitive)
                    if (allBSAFiles.Contains(bsaFileName))
                    {
                        foundBSAPath = bsaFileName;
                        foundExt = System.IO.Path.GetExtension(bsaFileName).ToLower();
                        if (string.IsNullOrEmpty(foundExt))
                        {
                            foundExt = originalExtension; // Fallback to original extension
                        }
                        Debug.Log($"TESLTextureLibrary: Found texture in BSA with original extension: {foundBSAPath}");
                        break;
                    }
                }
                
                // SECOND: Only if original extension didn't work, try other extensions
                if (foundBSAPath == null)
                {
                    foreach (string bsaFileName in pathsToTryOtherExts)
                    {
                        // Check in our hashset first (faster, case-insensitive)
                        if (allBSAFiles.Contains(bsaFileName))
                        {
                            foundBSAPath = bsaFileName;
                            foundExt = System.IO.Path.GetExtension(bsaFileName).ToLower();
                            if (string.IsNullOrEmpty(foundExt))
                            {
                                foundExt = originalExtension; // Fallback to original extension
                            }
                            Debug.Log($"TESLTextureLibrary: Found texture in BSA with fallback extension: {foundBSAPath} (original was {originalExtension})");
                            break;
                        }
                    }
                }
                
                // If not found with path variations, try case-insensitive filename search (matching old code)
                if (foundBSAPath == null)
                {
                    // Use the already-extracted originalBaseFilename from above
                    if (!string.IsNullOrEmpty(originalBaseFilename))
                    {
                        foreach (string bsaFileName in allBSAFiles)
                        {
                            string bsaBaseName = System.IO.Path.GetFileName(bsaFileName);
                            if (bsaBaseName.Equals(originalBaseFilename, StringComparison.OrdinalIgnoreCase))
                            {
                                foundBSAPath = bsaFileName;
                                foundExt = System.IO.Path.GetExtension(bsaFileName).ToLower();
                                if (string.IsNullOrEmpty(foundExt))
                                {
                                    foundExt = originalExtension;
                                }
                                break;
                            }
                        }
                    }
                }
                
                // If still not found, try partial match (filename without extension) - matching old code
                if (foundBSAPath == null)
                {
                    string baseNameNoExt = System.IO.Path.GetFileNameWithoutExtension(originalTextureName);
                    if (!string.IsNullOrEmpty(baseNameNoExt))
                    {
                        foreach (string bsaFileName in allBSAFiles)
                        {
                            string bsaBaseNameNoExt = System.IO.Path.GetFileNameWithoutExtension(bsaFileName);
                            if (bsaBaseNameNoExt.Equals(baseNameNoExt, StringComparison.OrdinalIgnoreCase))
                            {
                                foundBSAPath = bsaFileName;
                                foundExt = System.IO.Path.GetExtension(bsaFileName).ToLower();
                                if (string.IsNullOrEmpty(foundExt))
                                {
                                    foundExt = originalExtension;
                                }
                                break;
                            }
                        }
                    }
                }
                
                // If we found the file in a BSA, extract it directly (matching old GatherLandTextures approach)
                if (foundBSAPath != null)
                {
                    //Debug.Log($"TESLTextureLibrary: Found texture in BSA: {foundBSAPath}");
                    
                    string outputPath = System.IO.Path.Combine(textureDir, baseName + foundExt);
                    System.IO.Directory.CreateDirectory(textureDir);
                    
                    // Find which BSA contains this file and extract directly (matching old code)
                    // Since we found it in the combined HashSet, we need to find which specific BSA has it
                    // The path format in individual BSAs might differ, so we need to search through each BSA's FileNames
                    bool extractSuccess = false;
                    TESBSALibrary.BSAEntry foundBSAEntry = null;
                    string exactBSAPath = null; // The exact path format as stored in the BSA
                    
                    // Use the source ESM from the registered entry if available (more accurate)
                    string bsaLookupESM = sourceESMFilename;
                    if (string.IsNullOrEmpty(bsaLookupESM))
                    {
                        bsaLookupESM = esmName; // Fallback to provided esmName
                    }
                    
                    // Helper function to find file in a BSA entry (handles path separator variations)
                    Func<TESBSALibrary.BSAEntry, string, (bool found, string exactPath)> findInBSA = (bsaEntry, searchPath) =>
                    {
                        if (bsaEntry == null)
                        {
                            return (false, null);
                        }
                        
                        if (!bsaEntry.IsLoaded)
                        {
                            Debug.LogWarning($"TESLTextureLibrary: BSA entry for '{bsaEntry.BSAFilename}' is not loaded");
                            return (false, null);
                        }
                        
                        if (bsaEntry.FileNames == null || bsaEntry.FileNames.Count == 0)
                        {
                            Debug.LogWarning($"TESLTextureLibrary: BSA entry for '{bsaEntry.BSAFilename}' has no file names");
                            return (false, null);
                        }
                        
                        // Try exact match first
                        if (bsaEntry.FileNames.Contains(searchPath))
                        {
                            return (true, searchPath);
                        }
                        
                        // Try with different path separator
                        string altPath1 = searchPath.Replace('\\', '/');
                        if (altPath1 != searchPath && bsaEntry.FileNames.Contains(altPath1))
                        {
                            return (true, altPath1);
                        }
                        
                        string altPath2 = searchPath.Replace('/', '\\');
                        if (altPath2 != searchPath && bsaEntry.FileNames.Contains(altPath2))
                        {
                            return (true, altPath2);
                        }
                        
                        // Try case-insensitive filename-only match
                        string searchFileName = System.IO.Path.GetFileName(searchPath);
                        foreach (string bsaFileName in bsaEntry.FileNames)
                        {
                            string bsaBaseName = System.IO.Path.GetFileName(bsaFileName);
                            if (bsaBaseName.Equals(searchFileName, StringComparison.OrdinalIgnoreCase))
                            {
                                return (true, bsaFileName); // Return the exact path format from BSA
                            }
                        }
                        
                        return (false, null);
                    };
                    
                    // Debug: Log what we're searching for
                    Debug.Log($"TESLTextureLibrary: Searching for '{foundBSAPath}' in BSAs (sourceESM: '{bsaLookupESM}')");
                    
                    // Try source ESM first (from LTEX record)
                    if (!string.IsNullOrEmpty(bsaLookupESM))
                    {
                        string esmKey = bsaLookupESM + ".esm";
                        Debug.Log($"TESLTextureLibrary: Trying source ESM: {esmKey}");
                        var sourceBSAEntry = TESBSALibrary.GetBSAEntryForESM(esmKey);
                        if (sourceBSAEntry != null)
                        {
                            Debug.Log($"TESLTextureLibrary: Found BSA entry for source ESM: {sourceBSAEntry.BSAFilename} (IsLoaded: {sourceBSAEntry.IsLoaded}, FileCount: {sourceBSAEntry.FileNames?.Count ?? 0})");
                        }
                        else
                        {
                            Debug.LogWarning($"TESLTextureLibrary: No BSA entry found for source ESM: {esmKey}");
                        }
                        
                        var (found, exactPath) = findInBSA(sourceBSAEntry, foundBSAPath);
                        if (found)
                        {
                            foundBSAEntry = sourceBSAEntry;
                            exactBSAPath = exactPath;
                            Debug.Log($"TESLTextureLibrary: Found '{exactBSAPath}' in source ESM BSA: {foundBSAEntry.BSAFilename}");
                        }
                    }
                    
                    // If not in source ESM, search all BSAs
                    if (foundBSAEntry == null)
                    {
                        Debug.Log($"TESLTextureLibrary: Searching all BSAs for '{foundBSAPath}'");
                        var esmEntries = TESESMLibrary.GetLoadedESMEntries();
                        Debug.Log($"TESLTextureLibrary: Checking {esmEntries?.Length ?? 0} loaded ESMs");
                        
                        foreach (var esmEntry in esmEntries)
                        {
                            var bsaEntry = TESBSALibrary.GetBSAEntryForESM(esmEntry.ESMFilename);
                            if (bsaEntry != null)
                            {
                                Debug.Log($"TESLTextureLibrary: Checking BSA: {bsaEntry.BSAFilename} (IsLoaded: {bsaEntry.IsLoaded}, FileCount: {bsaEntry.FileNames?.Count ?? 0})");
                            }
                            
                            var (found, exactPath) = findInBSA(bsaEntry, foundBSAPath);
                            if (found)
                            {
                                foundBSAEntry = bsaEntry;
                                exactBSAPath = exactPath;
                                Debug.Log($"TESLTextureLibrary: Found '{exactBSAPath}' in BSA: {bsaEntry.BSAFilename}");
                                break;
                            }
                        }
                    }
                    
                    // Extract directly from the BSA (matching old code)
                    if (foundBSAEntry != null && foundBSAEntry.Archive != null && !string.IsNullOrEmpty(exactBSAPath))
                    {
                        try
                        {
                            // Use the exact path format we found in the BSA's FileNames HashSet
                            BSAFileEntry fileEntry = foundBSAEntry.Archive.GetFileEntry(exactBSAPath);
                            
                            if (fileEntry != null)
                            {
                                byte[] fileData = foundBSAEntry.Archive.ExtractFile(fileEntry);
                                if (fileData != null && fileData.Length == fileEntry.FileSize)
                                {
                                    System.IO.File.WriteAllBytes(outputPath, fileData);
                                    extractSuccess = true;
                                    Debug.Log($"TESLTextureLibrary: Extracted '{exactBSAPath}' from '{foundBSAEntry.BSAFilename}' ({fileEntry.FileSize} bytes)");
                                }
                                else
                                {
                                    Debug.LogWarning($"TESLTextureLibrary: Extracted data size mismatch for '{exactBSAPath}': expected {fileEntry.FileSize}, got {fileData?.Length ?? 0}");
                                }
                            }
                            else
                            {
                                Debug.LogWarning($"TESLTextureLibrary: File entry not found for: {exactBSAPath} in BSA {foundBSAEntry.BSAFilename}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"TESLTextureLibrary: Failed to extract '{exactBSAPath}' from '{foundBSAEntry.BSAFilename}': {ex.Message}\n{ex.StackTrace}");
                        }
                    }
                    else if (foundBSAPath != null)
                    {
                        Debug.LogWarning($"TESLTextureLibrary: Found '{foundBSAPath}' in BSA HashSet but could not find BSA entry (foundBSAEntry={foundBSAEntry != null}, archive={foundBSAEntry?.Archive != null}, exactBSAPath={exactBSAPath})");
                    }
                    
                    if (extractSuccess)
                    {
                        Debug.Log($"TESLTextureLibrary: Extracted texture from BSA: {foundBSAPath} -> {System.IO.Path.GetFileName(outputPath)}");
                        
                        // Convert to PNG if needed
                        if (foundExt == ".dds" || foundExt == ".tga")
                        {
                            string pngPath = System.IO.Path.ChangeExtension(outputPath, ".png");
                            
                            if (foundExt == ".dds" && convertDDSToPNGFunc != null)
                            {
                                try
                                {
                                    byte[] ddsData = System.IO.File.ReadAllBytes(outputPath);
                                    if (convertDDSToPNGFunc(ddsData, pngPath))
                                    {
                                        #if UNITY_EDITOR
                                        setTextureImportSettingsFunc?.Invoke(pngPath);
                                        #endif
                                        foundPath = pngPath;
                                        foundExtension = ".png";
                                        goto FoundTexture;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.LogWarning($"TESLTextureLibrary: Error converting extracted DDS to PNG: {ex.Message}");
                                }
                            }
                            else if (foundExt == ".tga")
                            {
                                try
                                {
                                    byte[] tgaData = System.IO.File.ReadAllBytes(outputPath);
                                    Texture2D tempTexture = new Texture2D(2, 2);
                                    if (tempTexture.LoadImage(tgaData))
                                    {
                                        byte[] pngData = tempTexture.EncodeToPNG();
                                        System.IO.File.WriteAllBytes(pngPath, pngData);
                                        UnityEngine.Object.DestroyImmediate(tempTexture);
                                        #if UNITY_EDITOR
                                        setTextureImportSettingsFunc?.Invoke(pngPath);
                                        #endif
                                        foundPath = pngPath;
                                        foundExtension = ".png";
                                        goto FoundTexture;
                                    }
                                    UnityEngine.Object.DestroyImmediate(tempTexture);
                                }
                                catch (Exception ex)
                                {
                                    Debug.LogWarning($"TESLTextureLibrary: Error converting extracted TGA to PNG: {ex.Message}");
                                }
                            }
                        }
                        else
                        {
                            // Already PNG
                            foundPath = outputPath;
                            foundExtension = ".png";
                            goto FoundTexture;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"TESLTextureLibrary: Found texture '{foundBSAPath}' in BSA but failed to extract it");
                    }
                }
                
                FoundTexture:;
                
                if (foundPath == null)
                {
                    Debug.LogWarning($"TESLTextureLibrary: Could not find or extract texture '{baseName}' from BSA archives");
                }
            }
            
            // Step 5: Load the texture if we found a file
            if (foundPath != null && System.IO.File.Exists(foundPath))
            {
                // Check if already cached by path
                TextureEntry existing = GetTextureByPath(foundPath);
                if (existing != null)
                {
                    return existing;
                }
                
                // Load the texture
                try
                {
                    Texture2D texture = null;
                    string ext = System.IO.Path.GetExtension(foundPath).ToLower();
                    
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga")
                    {
                        byte[] textureData = System.IO.File.ReadAllBytes(foundPath);
                        texture = new Texture2D(2, 2);
                        if (texture.LoadImage(textureData))
                        {
                            texture.wrapMode = TextureWrapMode.Repeat;
                            texture.filterMode = FilterMode.Bilinear;
                            texture.anisoLevel = 9;
                            #if UNITY_EDITOR
                            texture.Apply(true, false);
                            #else
                            texture.Apply(false, false);
                            #endif
                        }
                        else
                        {
                            UnityEngine.Object.DestroyImmediate(texture);
                            texture = null;
                        }
                    }
                    #if UNITY_EDITOR
                    else if (ext == ".dds")
                    {
                        // Try AssetDatabase for DDS
                        string normalizedPath = foundPath.Replace('\\', '/');
                        string normalizedDataPath = Application.dataPath.Replace('\\', '/');
                        string relativePath = normalizedPath;
                        if (normalizedPath.StartsWith(normalizedDataPath, StringComparison.OrdinalIgnoreCase))
                        {
                            relativePath = normalizedPath.Substring(normalizedDataPath.Length);
                        }
                        if (!relativePath.StartsWith("/"))
                            relativePath = "/" + relativePath;
                        string assetPath = "Assets" + relativePath;
                        
                        UnityEditor.AssetDatabase.ImportAsset(assetPath, UnityEditor.ImportAssetOptions.ForceUpdate);
                        texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                        
                        if (texture != null)
                        {
                            texture.wrapMode = TextureWrapMode.Repeat;
                            texture.filterMode = FilterMode.Bilinear;
                            texture.anisoLevel = 9;
                        }
                    }
                    #endif
                    
                    if (texture != null)
                    {
                        // If we have a registered entry, update it with the loaded texture
                        if (registeredEntry != null)
                        {
                            registeredEntry.Texture = texture;
                            registeredEntry.Filename = foundPath; // Update with actual loaded path
                            
                            // Also add to path cache
                            string normalizedPath = System.IO.Path.GetFullPath(foundPath).ToLowerInvariant();
                            _cacheByPath[normalizedPath] = registeredEntry;
                            
                            return registeredEntry;
                        }
                        else
                        {
                            // No registered entry, create new one
                            return AddTexture(textureName, ltexIndex, vtexIndex, foundPath, texture);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"TESLTextureLibrary: Error loading texture {foundPath}: {ex.Message}");
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Generates a normal map from a diffuse texture using the Sobel operator.
        /// </summary>
        /// <param name="diffuseTexture">The source diffuse texture</param>
        /// <param name="strength">Strength of the normal map effect (default 1.0f)</param>
        /// <returns>Generated normal map texture, or null if generation failed</returns>
        public static Texture2D GenerateNormalMap(Texture2D diffuseTexture, float strength = 1.0f)
        {
            if (diffuseTexture == null)
                return null;
            
            try
            {
                int width = diffuseTexture.width;
                int height = diffuseTexture.height;
                
                // Make sure texture is readable
                #if UNITY_EDITOR
                if (!diffuseTexture.isReadable)
                {
                    Debug.LogWarning($"TESLTextureLibrary: Texture {diffuseTexture.name} is not readable, cannot generate normal map");
                    return null;
                }
                #endif
                
                Texture2D normalMap = new Texture2D(width, height, TextureFormat.RGB24, false);
                Color32[] normalPixels = new Color32[width * height];
                
                // Get all pixels from the diffuse texture
                Color32[] diffusePixels = diffuseTexture.GetPixels32();
                
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        // Sample the brightness of adjacent pixels (treating the texture as a height map)
                        float left = (x > 0) ? GetBrightness(diffusePixels[y * width + (x - 1)]) : GetBrightness(diffusePixels[y * width + x]);
                        float right = (x < width - 1) ? GetBrightness(diffusePixels[y * width + (x + 1)]) : GetBrightness(diffusePixels[y * width + x]);
                        float up = (y > 0) ? GetBrightness(diffusePixels[(y - 1) * width + x]) : GetBrightness(diffusePixels[y * width + x]);
                        float down = (y < height - 1) ? GetBrightness(diffusePixels[(y + 1) * width + x]) : GetBrightness(diffusePixels[y * width + x]);
                        
                        // Calculate the X and Y components of the normal vector (Sobel logic)
                        float x_vector = (left - right) * strength;
                        float y_vector = (up - down) * strength;
                        float z_vector = 1.0f; // Depth (always pointing "out" towards the viewer)
                        
                        // Transform from -1 to 1 space into 0 to 1 color space, then scale to 0-255 RGB values
                        int r = Mathf.Clamp((int)((x_vector + 1.0f) * 0.5f * 255.0f), 0, 255);
                        int g = Mathf.Clamp((int)((y_vector + 1.0f) * 0.5f * 255.0f), 0, 255);
                        int b = Mathf.Clamp((int)((z_vector + 1.0f) * 0.5f * 255.0f), 0, 255);
                        
                        normalPixels[y * width + x] = new Color32((byte)r, (byte)g, (byte)b, 255);
                    }
                }
                
                normalMap.SetPixels32(normalPixels);
                normalMap.Apply();
                normalMap.name = diffuseTexture.name + "_n";
                
                return normalMap;
            }
            catch (Exception ex)
            {
                Debug.LogError($"TESLTextureLibrary: Error generating normal map: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Helper function to get brightness from a Color32 (grayscale value)
        /// </summary>
        private static float GetBrightness(Color32 color)
        {
            // Standard grayscale conversion: 0.299*R + 0.587*G + 0.114*B
            return (0.299f * color.r + 0.587f * color.g + 0.114f * color.b) / 255.0f;
        }
        
        /// <summary>
        /// Gets or generates a normal map for a texture. First checks for existing _n suffix file,
        /// then generates one from the diffuse texture if not found.
        /// </summary>
        /// <param name="textureEntry">The texture entry to get/generate normal map for</param>
        /// <param name="strength">Strength of the normal map effect (default 1.0f)</param>
        /// <returns>The normal map texture, or null if generation failed</returns>
        public static Texture2D GetOrGenerateNormalMap(TextureEntry textureEntry, float strength = 1.0f)
        {
            if (textureEntry == null || textureEntry.Texture == null)
                return null;
            
            // If normal map already cached, return it
            if (textureEntry.NormalMap != null)
                return textureEntry.NormalMap;
            
            // Check for existing normal map file with _n suffix
            string basePath = textureEntry.Filename;
            string baseName = System.IO.Path.GetFileNameWithoutExtension(basePath);
            string directory = System.IO.Path.GetDirectoryName(basePath);
            string extension = System.IO.Path.GetExtension(basePath);
            
            string normalMapPath = System.IO.Path.Combine(directory, baseName + "_n" + extension);
            
            if (System.IO.File.Exists(normalMapPath))
            {
                // Load existing normal map
                try
                {
                    byte[] normalData = System.IO.File.ReadAllBytes(normalMapPath);
                    Texture2D normalMap = new Texture2D(2, 2);
                    if (normalMap.LoadImage(normalData))
                    {
                        normalMap.wrapMode = TextureWrapMode.Repeat;
                        normalMap.filterMode = FilterMode.Bilinear;
                        normalMap.anisoLevel = 9;
                        #if UNITY_EDITOR
                        normalMap.Apply(true, false);
                        #else
                        normalMap.Apply(false, false);
                        #endif
                        normalMap.name = baseName + "_n";
                        textureEntry.NormalMap = normalMap;
                        //Debug.Log($"TESLTextureLibrary: Loaded existing normal map: {System.IO.Path.GetFileName(normalMapPath)}");
                        return normalMap;
                    }
                    UnityEngine.Object.DestroyImmediate(normalMap);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"TESLTextureLibrary: Error loading normal map from {normalMapPath}: {ex.Message}");
                }
            }
            
            // Generate normal map from diffuse texture
            Texture2D generatedNormalMap = GenerateNormalMap(textureEntry.Texture, strength);
            if (generatedNormalMap != null)
            {
                // Save the generated normal map to disk
                try
                {
                    byte[] pngData = generatedNormalMap.EncodeToPNG();
                    System.IO.File.WriteAllBytes(normalMapPath, pngData);
                    // Debug.Log($"TESLTextureLibrary: Generated and saved normal map: {System.IO.Path.GetFileName(normalMapPath)}"); // Commented out for performance
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"TESLTextureLibrary: Error saving generated normal map: {ex.Message}");
                }
                
                textureEntry.NormalMap = generatedNormalMap;
                return generatedNormalMap;
            }
            
            return null;
        }
    }
}
