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
    }
}
