using System;
using System.Collections.Generic;
using UnityEngine;

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
            public Texture2D Texture { get; set; }        // The actual Texture2D object
            
            public TextureEntry(string name, int ltexIndex, ushort vtexIndex, string filename, Texture2D texture)
            {
                Name = name;
                LTEXIndex = ltexIndex;
                VTEXIndex = vtexIndex;
                Filename = filename;
                Texture = texture;
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
        /// Gets the Texture2D object by file path. Returns null if not found.
        /// This is a convenience method for quick texture lookups.
        /// </summary>
        public static Texture2D GetTexture(string filename)
        {
            TextureEntry entry = GetTextureByPath(filename);
            return entry?.Texture;
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
            
            // Remove extension from texture name for searching
            string baseName = System.IO.Path.GetFileNameWithoutExtension(textureName);
            
            // Step 1: Check if already cached by VTEX index
            if (vtexIndex > 0)
            {
                TextureEntry cached = GetTextureByVTEXIndex(vtexIndex);
                if (cached != null)
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
            Debug.Log($"TESLTextureLibrary: Searching for texture '{baseName}' in directory: {textureDir}");
            
            foreach (string ext in extensions)
            {
                string searchPath = System.IO.Path.Combine(textureDir, baseName + ext);
                // Normalize the search path
                searchPath = System.IO.Path.GetFullPath(searchPath);
                if (System.IO.File.Exists(searchPath))
                {
                    foundPath = searchPath;
                    foundExtension = ext;
                    Debug.Log($"TESLTextureLibrary: Found texture file: {System.IO.Path.GetFileName(searchPath)} (full path: {searchPath})");
                    break;
                }
            }
            
            if (foundPath == null)
            {
                Debug.Log($"TESLTextureLibrary: Texture '{baseName}' not found in cache directory");
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
                Debug.Log($"TESLTextureLibrary: Texture not found in cache, trying BSA extraction for: {baseName}");
                
                // Try multiple BSA path variations (Morrowind uses different path formats)
                // Normalize to use backslashes (Morrowind BSA standard)
                string[] bsaPathVariations = new[]
                {
                    $"textures\\{baseName}",           // textures\filename
                    $"textures\\landscape\\{baseName}", // textures\landscape\filename
                    $"textures\\terrain\\{baseName}",    // textures\terrain\filename
                    $"textures\\tx\\{baseName}"         // textures\tx\filename
                };
                
                string[] bsaExtensions = new[] { ".dds", ".tga", ".png" };
                
                foreach (string bsaPathBase in bsaPathVariations)
                {
                    foreach (string ext in bsaExtensions)
                    {
                        // Ensure consistent backslashes for BSA paths
                        string bsaFileName = bsaPathBase.Replace('/', '\\') + ext;
                        
                        // Check if file exists in BSA
                        if (TESBSALibrary.FileExists(bsaFileName, esmName + ".esm"))
                        {
                            Debug.Log($"TESLTextureLibrary: Found texture in BSA: {bsaFileName}");
                            
                            string outputPath = System.IO.Path.Combine(textureDir, baseName + ext);
                            System.IO.Directory.CreateDirectory(textureDir);
                            
                            if (TESBSALibrary.ExtractFile(bsaFileName, outputPath, esmName + ".esm"))
                            {
                                Debug.Log($"TESLTextureLibrary: Extracted texture from BSA: {bsaFileName} -> {System.IO.Path.GetFileName(outputPath)}");
                                
                                // Convert to PNG if needed
                                if (ext == ".dds" || ext == ".tga")
                                {
                                    string pngPath = System.IO.Path.ChangeExtension(outputPath, ".png");
                                    
                                    if (ext == ".dds" && convertDDSToPNGFunc != null)
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
                                    else if (ext == ".tga")
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
                        }
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
                        return AddTexture(textureName, ltexIndex, vtexIndex, foundPath, texture);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"TESLTextureLibrary: Error loading texture {foundPath}: {ex.Message}");
                }
            }
            
            return null;
        }
    }
}
