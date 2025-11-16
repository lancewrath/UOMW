using ESMSharp.TES3;
using ESMSharp.TES3.Records;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using Pfim;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ESMSharp.TES3Terrain
{
    public partial class TESTerrain
    {
        int MinCellX, MaxCellX, MinCellY, MaxCellY;
        private string _esm = "";
        private string _bsa = "";
        
        // Store global heightmap data for per-cell terrain generation
        private float[][] _globalHeights = null;
        private Dictionary<(int x, int y), ushort[][]> _cellVTEXData = null;
        private Dictionary<ushort, int> _textureIndexToLayerIndex = null;
        private List<TerrainLayer> _sharedTerrainLayers = null;
        
        // Texture cache removed - now using global TESLTextureLibrary
        public TESTerrain() { }

        public TESTerrain(int minX, int maxX, int minY, int maxY)
        {
            MinCellX = minX; MaxCellX = maxX; MinCellY = minY; MaxCellY = maxY;
        }

        // Helper function to convert DDS to PNG using Pfim
        /// <summary>
        /// Sets texture import settings to disable alpha source (prevents shininess in URP terrain)
        /// In builds, this is a no-op since import settings can't be changed at runtime
        /// </summary>
        private void SetTextureImportSettings(string texturePath)
        {
            #if UNITY_EDITOR
            try
            {
                // Convert to asset path (relative to Assets folder)
                string assetPath = texturePath.Replace('\\', '/');
                if (assetPath.StartsWith(Application.dataPath))
                {
                    assetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);
                }
                
                if (!assetPath.StartsWith("Assets/"))
                {
                    // Not in Assets folder, can't set import settings
                    return;
                }
                
                TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer != null)
                {
                    importer.alphaSource = TextureImporterAlphaSource.FromGrayScale;
                    // Set texture format to ETC RGB compressed to prevent shininess
                    importer.textureCompression = TextureImporterCompression.Compressed;
                    // Use PlatformTextureSettings API instead of obsolete textureFormat
                    TextureImporterPlatformSettings platformSettings = importer.GetPlatformTextureSettings("Android");
                    platformSettings.format = TextureImporterFormat.ETC_RGB4;
                    importer.SetPlatformTextureSettings(platformSettings);
                    importer.SaveAndReimport();
                    AssetDatabase.Refresh();
                }
            }
            catch (System.Exception)
            {
                //UnityEngine.Debug.LogWarning($"Failed to set texture import settings for {texturePath}: {ex.Message}");
            }
            #else
            // In builds, import settings are already baked in, so this is a no-op
            // The settings should have been applied during development/editor time
            #endif
        }

        public bool ConvertDDSToPNG(byte[] ddsData, string outputPngPath)
        {
            try
            {
                if (ddsData == null || ddsData.Length < 128)
                    return false;

                // Check DDS magic number
                if (System.Text.Encoding.ASCII.GetString(ddsData, 0, 4) != "DDS ")
                    return false;

                // Use Pfim to decode the DDS file
                using (var image = Pfimage.FromStream(new MemoryStream(ddsData)))
                {
                    // Determine the pixel format based on Pfim's format
                    Color[] pixels = null;
                    int width = image.Width;
                    int height = image.Height;

                    // Convert Pfim's image data to Unity Color array
                    // Note: Pfim uses stride which accounts for row alignment
                    pixels = new Color[width * height];
                    int stride = image.Stride;
                    
                    switch (image.Format)
                    {
                        case ImageFormat.Rgba32:
                            // RGBA32: 4 bytes per pixel (B, G, R, A) - DDS uses BGR order, swap R and B
                            for (int y = 0; y < height; y++)
                            {
                                for (int x = 0; x < width; x++)
                                {
                                    int rowOffset = y * stride;
                                    int pixelOffset = rowOffset + x * 4;
                                    if (pixelOffset + 3 < image.Data.Length)
                                    {
                                        pixels[y * width + x] = new Color(
                                            image.Data[pixelOffset + 2] / 255f, // R (was B)
                                            image.Data[pixelOffset + 1] / 255f, // G
                                            image.Data[pixelOffset] / 255f,     // B (was R)
                                            image.Data[pixelOffset + 3] / 255f // A
                                        );
                                    }
                                }
                            }
                            break;

                        case ImageFormat.Rgb24:
                            // RGB24: 3 bytes per pixel (B, G, R) - DDS uses BGR order, swap R and B
                            for (int y = 0; y < height; y++)
                            {
                                for (int x = 0; x < width; x++)
                                {
                                    int rowOffset = y * stride;
                                    int pixelOffset = rowOffset + x * 3;
                                    if (pixelOffset + 2 < image.Data.Length)
                                    {
                                        pixels[y * width + x] = new Color(
                                            image.Data[pixelOffset + 2] / 255f, // R (was B)
                                            image.Data[pixelOffset + 1] / 255f, // G
                                            image.Data[pixelOffset] / 255f,     // B (was R)
                                            1f
                                        );
                                    }
                                }
                            }
                            break;

                        case ImageFormat.R5g5b5:
                            // RGB565-like format: 2 bytes per pixel
                            for (int y = 0; y < height; y++)
                            {
                                for (int x = 0; x < width; x++)
                                {
                                    int rowOffset = y * stride;
                                    int pixelOffset = rowOffset + x * 2;
                                    if (pixelOffset + 1 < image.Data.Length)
                                    {
                                        ushort pixel = (ushort)(image.Data[pixelOffset] | (image.Data[pixelOffset + 1] << 8));
                                        int r = (pixel >> 10) & 0x1F;
                                        int g = (pixel >> 5) & 0x1F;
                                        int b = pixel & 0x1F;
                                        pixels[y * width + x] = new Color(r / 31f, g / 31f, b / 31f, 1f);
                                    }
                                }
                            }
                            break;

                        case ImageFormat.R5g5b5a1:
                            // RGBA5551: 2 bytes per pixel
                            for (int y = 0; y < height; y++)
                            {
                                for (int x = 0; x < width; x++)
                                {
                                    int rowOffset = y * stride;
                                    int pixelOffset = rowOffset + x * 2;
                                    if (pixelOffset + 1 < image.Data.Length)
                                    {
                                        ushort pixel = (ushort)(image.Data[pixelOffset] | (image.Data[pixelOffset + 1] << 8));
                                        int r = (pixel >> 11) & 0x1F;
                                        int g = (pixel >> 6) & 0x1F;
                                        int b = (pixel >> 1) & 0x1F;
                                        int a = pixel & 0x1;
                                        pixels[y * width + x] = new Color(r / 31f, g / 31f, b / 31f, a);
                                    }
                                }
                            }
                            break;

                        default:
                            //UnityEngine.Debug.LogWarning($"Unsupported DDS format: {image.Format}, attempting fallback (stride: {stride})");
                            // Fallback: try to read based on stride
                            int bytesPerPixel = stride / width;
                            if (bytesPerPixel == 4)
                            {
                                // Assume RGBA32 (B, G, R, A) - DDS uses BGR order, swap R and B
                                for (int y = 0; y < height; y++)
                                {
                                    for (int x = 0; x < width; x++)
                                    {
                                        int rowOffset = y * stride;
                                        int pixelOffset = rowOffset + x * 4;
                                        if (pixelOffset + 3 < image.Data.Length)
                                        {
                                            pixels[y * width + x] = new Color(
                                                image.Data[pixelOffset + 2] / 255f, // R (was B)
                                                image.Data[pixelOffset + 1] / 255f, // G
                                                image.Data[pixelOffset] / 255f,     // B (was R)
                                                image.Data[pixelOffset + 3] / 255f // A
                                            );
                                        }
                                    }
                                }
                            }
                            else if (bytesPerPixel == 3)
                            {
                                // Assume RGB24 (B, G, R) - DDS uses BGR order, swap R and B
                                for (int y = 0; y < height; y++)
                                {
                                    for (int x = 0; x < width; x++)
                                    {
                                        int rowOffset = y * stride;
                                        int pixelOffset = rowOffset + x * 3;
                                        if (pixelOffset + 2 < image.Data.Length)
                                        {
                                            pixels[y * width + x] = new Color(
                                                image.Data[pixelOffset + 2] / 255f, // R (was B)
                                                image.Data[pixelOffset + 1] / 255f, // G
                                                image.Data[pixelOffset] / 255f,     // B (was R)
                                                1f
                                            );
                                        }
                                    }
                                }
                            }
                            else
                            {
                                UnityEngine.Debug.LogError($"Cannot convert DDS format {image.Format} (stride: {stride}, width: {width}, bytesPerPixel: {bytesPerPixel})");
                                return false;
                            }
                            break;
                    }

                    if (pixels == null || pixels.Length == 0)
                    {
                        UnityEngine.Debug.LogError("Failed to convert DDS pixel data");
                        return false;
                    }

                    // Create Unity texture in RGBA32 format (32-bit with alpha) for terrain
                    // Alpha will be set to "From grayscale" in import settings to prevent shininess
                    Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    texture.SetPixels(pixels);
                    texture.Apply();

                    byte[] pngData = texture.EncodeToPNG();
                    System.IO.File.WriteAllBytes(outputPngPath, pngData);
                    UnityEngine.Object.DestroyImmediate(texture);
                    
                    #if UNITY_EDITOR
                    // Set import settings to disable alpha source (prevents shininess)
                    SetTextureImportSettings(outputPngPath);
                    #endif

                    return true;
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"Error converting DDS to PNG: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public void GatherLandTextures(Record[] _records, string bsa = "Morrowind.bsa", string esm = "Morrowind")
        {
            _bsa = bsa;
            _esm = System.IO.Path.GetFileNameWithoutExtension(esm);

            // Step 1: Build a dictionary mapping texture index -> filename from RecordLTex records
            // Store both NAME and DATA so we can try both when looking up in BSA
            Dictionary<int, (string primary, string fallback)> textureIndexToNames = new Dictionary<int, (string, string)>();

            foreach (Record rec in _records)
            {
                RecordLTex ltexRecord = rec as RecordLTex;
                if (ltexRecord != null)
                {
                    int textureIndex = -1;
                    string filename = null;
                    string name = null;

                    // Extract INTV (index), DATA (filename), and NAME (name) from subrecords
                    foreach (SubRecords subrec in ltexRecord.subRecords)
                    {
                        SubRecordLTexINTV intvSubrec = subrec as SubRecordLTexINTV;
                        if (intvSubrec != null)
                        {
                            textureIndex = intvSubrec.index;
                        }

                        SubRecordLTexData dataSubrec = subrec as SubRecordLTexData;
                        if (dataSubrec != null)
                        {
                            filename = dataSubrec.filename;
                        }

                        SubRecordLTexNAME nameSubrec = subrec as SubRecordLTexNAME;
                        if (nameSubrec != null)
                        {
                            // Clean null characters immediately when reading from subrecord
                            name = nameSubrec.name;
                            if (!string.IsNullOrEmpty(name))
                            {
                                name = name.TrimEnd('\0', ' ', '\t', '\r', '\n');
                                name = name.Replace("\0", ""); // Remove any remaining null characters
                                name = name.Trim();
                            }
                        }
                    }

                    // If we have index, store both NAME and DATA for BSA lookup
                    if (textureIndex >= 0)
                    {
                        string primaryName = null;
                        string fallbackName = null;
                        
                        // NAME is already cleaned above, just use it directly
                        if (!string.IsNullOrEmpty(name))
                        {
                            primaryName = name;
                        }
                        
                        // Clean and store DATA filename as fallback
                        if (!string.IsNullOrEmpty(filename))
                        {
                            fallbackName = filename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                            fallbackName = fallbackName.Replace("\0", "");
                            fallbackName = fallbackName.Trim();
                        }
                        
                        // Store both names (prefer NAME, fallback to DATA)
                        if (!string.IsNullOrEmpty(primaryName) || !string.IsNullOrEmpty(fallbackName))
                        {
                            textureIndexToNames[textureIndex] = (primaryName ?? fallbackName, fallbackName);
                            string source = !string.IsNullOrEmpty(primaryName) ? "NAME" : "DATA";
                            //UnityEngine.Debug.Log($"Texture index {textureIndex} -> primary: '{primaryName ?? fallbackName}', fallback: '{fallbackName}' (from {source})");
                        }
                    }
                }
            }

            //UnityEngine.Debug.Log($"Found {textureIndexToNames.Count} texture mappings");

            // Step 2: Use TESBSALibrary to search across all BSAs (no need to open a single BSA)
            // We'll use TESBSALibrary.ExtractFile() which searches all BSAs automatically

            // Step 4: Collect unique texture indices from all Land records
            HashSet<int> uniqueTextureIndices = new HashSet<int>();

            foreach (Record rec in _records)
            {
                RecordLand landRecord = rec as RecordLand;
                if (landRecord != null)
                {
                    // Find VTEX subrecord
                    foreach (SubRecords subrec in landRecord.subRecords)
                    {
                        SubRecordLandVTEX vtexSubrec = subrec as SubRecordLandVTEX;
                        if (vtexSubrec != null)
                        {
                            // Collect all texture indices from the 16x16 array
                            ushort[][] indices = vtexSubrec.indices;
                            for (int y = 0; y < 16; y++)
                            {
                                if (indices[y] != null)
                                {
                                    for (int x = 0; x < 16; x++)
                                    {
                                        uniqueTextureIndices.Add(indices[y][x]);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            //UnityEngine.Debug.Log($"Found {uniqueTextureIndices.Count} unique texture indices in land records");

            // Step 5: Load textures using TESLTextureLibrary (which handles BSA extraction, conversion, and caching)
            int loadedCount = 0;
            int failedCount = 0;

            // Get all loaded ESMs to determine cache directories
            string[] loadedESMs = TESESMLibrary.GetLoadedESMFilenames();
            
            // Determine the preferred ESM cache directory (use _esm if available, otherwise first loaded)
            string preferredESMName = _esm;
            if (string.IsNullOrEmpty(preferredESMName) && loadedESMs.Length > 0)
            {
                preferredESMName = System.IO.Path.GetFileNameWithoutExtension(loadedESMs[0]);
            }

            foreach (int textureIndex in uniqueTextureIndices)
            {
                if (textureIndexToNames.TryGetValue(textureIndex, out var names))
                {
                    // VTEX index is textureIndex + 1 (VTEX 0 is default texture)
                    ushort vtexIndex = (ushort)(textureIndex + 1);
                    
                    // Try primary name first (from NAME subrecord), then fallback (from DATA subrecord)
                    // Sanitize names to remove null characters and trim whitespace
                    string textureName = null;
                    if (!string.IsNullOrEmpty(names.primary))
                    {
                        textureName = names.primary.TrimEnd('\0', ' ', '\t', '\r', '\n');
                        textureName = textureName.Replace("\0", "");
                        textureName = textureName.Trim();
                    }
                    if (string.IsNullOrEmpty(textureName) && !string.IsNullOrEmpty(names.fallback))
                    {
                        textureName = names.fallback.TrimEnd('\0', ' ', '\t', '\r', '\n');
                        textureName = textureName.Replace("\0", "");
                        textureName = textureName.Trim();
                    }
                    
                    if (string.IsNullOrEmpty(textureName))
                    {
                                failedCount++;
                                continue;
                            }
                            
                    // Try to load texture using TESLTextureLibrary (handles BSA extraction, conversion, caching)
                    // Try each ESM cache directory until we find or successfully extract the texture
                    TESLTextureLibrary.TextureEntry textureEntry = null;
                    string targetESMName = preferredESMName;
                    
                    // First, try the preferred ESM
                    string textureDir = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Textures", targetESMName);
                    System.IO.Directory.CreateDirectory(textureDir);
                    
                    textureEntry = TESLTextureLibrary.LoadOrGetTexture(
                        textureName,
                        textureDir,
                        targetESMName,
                        textureIndex,
                        vtexIndex,
                        ConvertDDSToPNG,
                        SetTextureImportSettings
                    );
                    
                    // If not found in preferred ESM, try other ESMs
                    if (textureEntry == null)
                    {
                        foreach (string esmFilename in loadedESMs)
                        {
                            string esmName = System.IO.Path.GetFileNameWithoutExtension(esmFilename);
                            if (esmName.Equals(targetESMName, StringComparison.OrdinalIgnoreCase))
                                continue; // Already tried
                            
                            textureDir = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Textures", esmName);
                            System.IO.Directory.CreateDirectory(textureDir);
                            
                            textureEntry = TESLTextureLibrary.LoadOrGetTexture(
                                textureName,
                                textureDir,
                                esmName,
                                textureIndex,
                                vtexIndex,
                                ConvertDDSToPNG,
                                #if UNITY_EDITOR
                                SetTextureImportSettings
                                #else
                                null
                                #endif
                            );
                            
                            if (textureEntry != null)
                                                break;
                                            }
                    }
                    
                    if (textureEntry != null && textureEntry.Texture != null)
                    {
                        loadedCount++;
                                        }
                                        else
                                        {
                                    failedCount++;
                        //UnityEngine.Debug.LogWarning($"Failed to load texture '{textureName}' (LTEX index {textureIndex}, VTEX index {vtexIndex})");
                                }
                            }
                            else
                            {
                    // No filename mapping found for this texture index
                    failedCount++;
                }
            }

            // Clean up is handled by TESLTextureLibrary internally
            //UnityEngine.Debug.Log($"Texture loading complete: {loadedCount} loaded, {failedCount} failed");
        }

        /// <summary>
        /// Finds water and foam textures.
        /// Priority: 1) Textures folder, 2) BSA archives, 3) LTEX records
        /// </summary>
        private (Texture2D waterTexture, Texture2D waterNormalMap, Texture2D foamTexture) FindWaterTextures(Record[] _records, string esm = "Morrowind")
        {
            Texture2D waterTexture = null;
            Texture2D waterNormalMap = null;
            Texture2D foamTexture = null;
            
            string esmName = System.IO.Path.GetFileNameWithoutExtension(esm);
            string textureDir = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Textures", esmName);
            textureDir = System.IO.Path.GetFullPath(textureDir);
            
            // Common water texture names to search for
            string[] waterNames = { "water00", "water", "water_00", "tx_water" };
            string[] foamNames = { "foam", "foam00", "foam_00", "tx_foam" };
            
            // Priority 1: Search textures folder for water textures
            if (waterTexture == null)
            {
                foreach (string waterName in waterNames)
                {
                    string[] extensions = { ".png", ".dds", ".tga" };
                    foreach (string ext in extensions)
                    {
                        string testPath = System.IO.Path.Combine(textureDir, waterName + ext);
                        if (System.IO.File.Exists(testPath))
                        {
                            // Check library first
                            TESLTextureLibrary.TextureEntry cachedEntry = TESLTextureLibrary.GetTextureByPath(testPath);
                            if (cachedEntry == null)
                            {
                                // Load using TESLTextureLibrary
                                cachedEntry = TESLTextureLibrary.LoadOrGetTexture(
                                    waterName,
                                    textureDir,
                                    esmName,
                                    -1,
                                    0,
                                    ConvertDDSToPNG,
                                    #if UNITY_EDITOR
                                    SetTextureImportSettings
                                    #else
                                    null
                                    #endif
                                );
                            }
                            
                            if (cachedEntry != null && cachedEntry.Texture != null)
                            {
                                waterTexture = cachedEntry.Texture;
                                waterNormalMap = TESLTextureLibrary.GetOrGenerateNormalMap(cachedEntry, 1.0f);
                                UnityEngine.Debug.Log($"Found water texture in textures folder: {waterName + ext}");
                                break;
                            }
                        }
                    }
                    if (waterTexture != null) break;
                }
            }
            
            // Priority 1: Search textures folder for foam textures
            if (foamTexture == null)
            {
                foreach (string foamName in foamNames)
                {
                    string[] extensions = { ".png", ".dds", ".tga" };
                    foreach (string ext in extensions)
                    {
                        string testPath = System.IO.Path.Combine(textureDir, foamName + ext);
                        if (System.IO.File.Exists(testPath))
                        {
                            // Check library first
                            TESLTextureLibrary.TextureEntry cachedEntry = TESLTextureLibrary.GetTextureByPath(testPath);
                            if (cachedEntry == null)
                            {
                                // Load using TESLTextureLibrary
                                cachedEntry = TESLTextureLibrary.LoadOrGetTexture(
                                    foamName,
                                    textureDir,
                                    esmName,
                                    -1,
                                    0,
                                    ConvertDDSToPNG,
                                    #if UNITY_EDITOR
                                    SetTextureImportSettings
                                    #else
                                    null
                                    #endif
                                );
                            }
                            
                            if (cachedEntry != null && cachedEntry.Texture != null)
                            {
                                foamTexture = cachedEntry.Texture;
                                UnityEngine.Debug.Log($"Found foam texture in textures folder: {foamName + ext}");
                                break;
                            }
                        }
                    }
                    if (foamTexture != null) break;
                }
            }
            
            // Priority 2: Try BSA extraction if not found in textures folder
            if (waterTexture == null)
            {
                foreach (string waterName in waterNames)
                {
                    // Try to extract from BSA using TESLTextureLibrary
                    TESLTextureLibrary.TextureEntry cachedEntry = TESLTextureLibrary.LoadOrGetTexture(
                        waterName,
                        textureDir,
                        esmName,
                        -1,
                        0,
                        ConvertDDSToPNG,
                        SetTextureImportSettings
                    );
                    
                    if (cachedEntry != null && cachedEntry.Texture != null)
                    {
                        waterTexture = cachedEntry.Texture;
                        waterNormalMap = TESLTextureLibrary.GetOrGenerateNormalMap(cachedEntry, 1.0f);
                        UnityEngine.Debug.Log($"Found water texture in BSA: {waterName}");
                        break;
                    }
                }
            }
            
            if (foamTexture == null)
            {
                foreach (string foamName in foamNames)
                {
                    // Try to extract from BSA using TESLTextureLibrary
                    TESLTextureLibrary.TextureEntry cachedEntry = TESLTextureLibrary.LoadOrGetTexture(
                        foamName,
                        textureDir,
                        esmName,
                        -1,
                        0,
                        ConvertDDSToPNG,
                        SetTextureImportSettings
                    );
                    
                    if (cachedEntry != null && cachedEntry.Texture != null)
                    {
                        foamTexture = cachedEntry.Texture;
                        UnityEngine.Debug.Log($"Found foam texture in BSA: {foamName}");
                        break;
                    }
                }
            }
            
            // Priority 3: Search for water texture in LTEX records (last resort)
            foreach (Record rec in _records)
            {
                RecordLTex ltexRecord = rec as RecordLTex;
                if (ltexRecord != null)
                {
                    string filename = null;
                    string name = null;
                    
                    // Extract DATA (filename) and NAME (name) from subrecords
                    foreach (SubRecords subrec in ltexRecord.subRecords)
                    {
                        SubRecordLTexData dataSubrec = subrec as SubRecordLTexData;
                        if (dataSubrec != null)
                        {
                            filename = dataSubrec.filename;
                            // Clean filename of null characters and illegal path characters
                            if (!string.IsNullOrEmpty(filename))
                            {
                                filename = filename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                                filename = filename.Replace("\0", "");
                                filename = filename.Trim();
                            }
                        }
                        
                        SubRecordLTexNAME nameSubrec = subrec as SubRecordLTexNAME;
                        if (nameSubrec != null)
                        {
                            name = nameSubrec.name;
                            if (!string.IsNullOrEmpty(name))
                            {
                                name = name.TrimEnd('\0', ' ', '\t', '\r', '\n');
                                name = name.Replace("\0", "");
                                name = name.Trim();
                            }
                        }
                    }
                    
                    // Check if this is a water texture (case-insensitive search)
                    bool isWater = false;
                    bool isFoam = false;
                    
                    if (!string.IsNullOrEmpty(name))
                    {
                        string nameLower = name.ToLowerInvariant();
                        isWater = nameLower.Contains("water");
                        isFoam = nameLower.Contains("foam");
                    }
                    
                    if (!isWater && !isFoam && !string.IsNullOrEmpty(filename))
                    {
                        try
                        {
                            // Clean filename before using Path methods
                            string cleanFilename = filename;
                            // Remove any illegal path characters
                            char[] invalidChars = System.IO.Path.GetInvalidPathChars();
                            foreach (char c in invalidChars)
                            {
                                cleanFilename = cleanFilename.Replace(c.ToString(), "");
                            }
                            
                            if (!string.IsNullOrEmpty(cleanFilename))
                            {
                                string filenameLower = System.IO.Path.GetFileNameWithoutExtension(cleanFilename).ToLowerInvariant();
                                isWater = filenameLower.Contains("water");
                                isFoam = filenameLower.Contains("foam");
                            }
                        }
                        catch (System.ArgumentException)
                        {
                            // If filename still has issues, try simple string operations
                            string filenameLower = filename.ToLowerInvariant();
                            isWater = filenameLower.Contains("water");
                            isFoam = filenameLower.Contains("foam");
                        }
                    }
                    
                    // Load water texture
                    if (isWater && waterTexture == null && !string.IsNullOrEmpty(filename))
                    {
                        // Try to load using TESLTextureLibrary
                        // Safely get base name without extension, handling illegal characters
                        string baseNameNoExt = filename;
                        try
                        {
                            // Remove invalid path characters first
                            char[] invalidChars = System.IO.Path.GetInvalidPathChars();
                            foreach (char c in invalidChars)
                            {
                                baseNameNoExt = baseNameNoExt.Replace(c.ToString(), "");
                            }
                            baseNameNoExt = System.IO.Path.GetFileNameWithoutExtension(baseNameNoExt);
                        }
                        catch (System.ArgumentException)
                        {
                            // Fallback: manually extract filename without extension
                            baseNameNoExt = filename;
                            int lastSlash = baseNameNoExt.LastIndexOfAny(new char[] { '/', '\\' });
                            if (lastSlash >= 0)
                            {
                                baseNameNoExt = baseNameNoExt.Substring(lastSlash + 1);
                            }
                            int lastDot = baseNameNoExt.LastIndexOf('.');
                            if (lastDot >= 0)
                            {
                                baseNameNoExt = baseNameNoExt.Substring(0, lastDot);
                            }
                        }
                        
                        TESLTextureLibrary.TextureEntry cachedEntry = null;
                        
                        // First, check library by name
                        if (!string.IsNullOrEmpty(name))
                        {
                            cachedEntry = TESLTextureLibrary.GetTextureByName(name);
                        }
                        
                        // If not found by name, check by base filename
                        if (cachedEntry == null)
                        {
                            cachedEntry = TESLTextureLibrary.GetTextureByName(baseNameNoExt);
                        }
                        
                        // If still not found, try to find file and load it
                        if (cachedEntry == null)
                        {
                            // Try to find file in texture directory
                            string[] extensions = { ".png", ".dds", ".tga" };
                            string foundPath = null;
                            foreach (string ext in extensions)
                            {
                                string testPath = System.IO.Path.Combine(textureDir, baseNameNoExt + ext);
                                if (System.IO.File.Exists(testPath))
                                {
                                    foundPath = testPath;
                                    break;
                                }
                            }
                            
                            // Also check if file exists with original filename
                            if (foundPath == null)
                            {
                                string originalPath = System.IO.Path.Combine(textureDir, filename);
                                if (System.IO.File.Exists(originalPath))
                                {
                                    foundPath = originalPath;
                                }
                            }
                            
                            if (foundPath != null)
                            {
                                // Check library by path
                                cachedEntry = TESLTextureLibrary.GetTextureByPath(foundPath);
                                
                                // If not in library, load it using TESLTextureLibrary (which will add it)
                                if (cachedEntry == null)
                                {
                                    cachedEntry = TESLTextureLibrary.LoadOrGetTexture(
                                        name ?? baseNameNoExt,  // textureName
                                        textureDir,             // textureDir
                                        esmName,                // esmName (already declared at function level)
                                        -1,                     // ltexIndex
                                        0,                      // vtexIndex
                                        ConvertDDSToPNG,        // convertDDSToPNGFunc
                                        #if UNITY_EDITOR
                                        SetTextureImportSettings // setTextureImportSettingsFunc
                                        #else
                                        null
                                        #endif
                                    );
                                }
                            }
                        }
                        
                        if (cachedEntry != null && cachedEntry.Texture != null)
                        {
                            waterTexture = cachedEntry.Texture;
                            // Generate normal map for water (this will also cache it in the entry)
                            waterNormalMap = TESLTextureLibrary.GetOrGenerateNormalMap(cachedEntry, 1.0f);
                            UnityEngine.Debug.Log($"Found water texture: {name ?? filename} (cached in library)");
                        }
                    }
                    
                    // Load foam texture (optional)
                    if (isFoam && foamTexture == null && !string.IsNullOrEmpty(filename))
                    {
                        // Safely get base name without extension, handling illegal characters
                        string baseNameNoExt = filename;
                        try
                        {
                            // Remove invalid path characters first
                            char[] invalidChars = System.IO.Path.GetInvalidPathChars();
                            foreach (char c in invalidChars)
                            {
                                baseNameNoExt = baseNameNoExt.Replace(c.ToString(), "");
                            }
                            baseNameNoExt = System.IO.Path.GetFileNameWithoutExtension(baseNameNoExt);
                        }
                        catch (System.ArgumentException)
                        {
                            // Fallback: manually extract filename without extension
                            baseNameNoExt = filename;
                            int lastSlash = baseNameNoExt.LastIndexOfAny(new char[] { '/', '\\' });
                            if (lastSlash >= 0)
                            {
                                baseNameNoExt = baseNameNoExt.Substring(lastSlash + 1);
                            }
                            int lastDot = baseNameNoExt.LastIndexOf('.');
                            if (lastDot >= 0)
                            {
                                baseNameNoExt = baseNameNoExt.Substring(0, lastDot);
                            }
                        }
                        
                        TESLTextureLibrary.TextureEntry cachedEntry = null;
                        
                        // First, check library by name
                        if (!string.IsNullOrEmpty(name))
                        {
                            cachedEntry = TESLTextureLibrary.GetTextureByName(name);
                        }
                        
                        // If not found by name, check by base filename
                        if (cachedEntry == null)
                        {
                            cachedEntry = TESLTextureLibrary.GetTextureByName(baseNameNoExt);
                        }
                        
                        // If still not found, try to find file and load it
                        if (cachedEntry == null)
                        {
                            // Try to find file in texture directory
                            string[] extensions = { ".png", ".dds", ".tga" };
                            string foundPath = null;
                            foreach (string ext in extensions)
                            {
                                string testPath = System.IO.Path.Combine(textureDir, baseNameNoExt + ext);
                                if (System.IO.File.Exists(testPath))
                                {
                                    foundPath = testPath;
                                    break;
                                }
                            }
                            
                            // Also check if file exists with original filename
                            if (foundPath == null)
                            {
                                string originalPath = System.IO.Path.Combine(textureDir, filename);
                                if (System.IO.File.Exists(originalPath))
                                {
                                    foundPath = originalPath;
                                }
                            }
                            
                            if (foundPath != null)
                            {
                                // Check library by path
                                cachedEntry = TESLTextureLibrary.GetTextureByPath(foundPath);
                                
                                // If not in library, load it using TESLTextureLibrary (which will add it)
                                if (cachedEntry == null)
                                {
                                    cachedEntry = TESLTextureLibrary.LoadOrGetTexture(
                                        name ?? baseNameNoExt,  // textureName
                                        textureDir,             // textureDir
                                        esmName,                // esmName (already declared at function level)
                                        -1,                     // ltexIndex
                                        0,                      // vtexIndex
                                        ConvertDDSToPNG,        // convertDDSToPNGFunc
                                        #if UNITY_EDITOR
                                        SetTextureImportSettings // setTextureImportSettingsFunc
                                        #else
                                        null
                                        #endif
                                    );
                                }
                            }
                        }
                        
                        if (cachedEntry != null && cachedEntry.Texture != null)
                        {
                            foamTexture = cachedEntry.Texture;
                            UnityEngine.Debug.Log($"Found foam texture: {name ?? filename} (cached in library)");
                        }
                    }
                }
            }
            
            return (waterTexture, waterNormalMap, foamTexture);
        }
        
        public void GenerateUnityTerrain(Record[] _records, string esm = "Morrowind", float terrainHeight = 0f, float waterY = 0f)
        {
            _esm = System.IO.Path.GetFileNameWithoutExtension(esm);

            // Step 1: Load the heightmap RAW file
            string rawFilePath = Application.dataPath + "/StreamingAssets/Data/UOMW/Cache/" + _esm + "_MapHeight.raw";
            if (!File.Exists(rawFilePath))
            {
                UnityEngine.Debug.LogError($"Heightmap RAW file not found: {rawFilePath}. Please run GenerateHeightMap first.");
                return;
            }
            
            // Load height range from saved file (matches OpenMW's approach of using actual min/max)
            float actualMinHeight = 0f;
            float actualMaxHeight = 0f;
            float actualHeightRange = 0f;
            string heightRangePath = Application.dataPath + "/StreamingAssets/Data/UOMW/Cache/" + _esm + "_MapHeight_Range.json";
            if (File.Exists(heightRangePath))
            {
                try
                {
                    string heightRangeJson = File.ReadAllText(heightRangePath);
                    // Simple JSON parsing (minHeight, maxHeight, heightRange)
                    var minMatch = System.Text.RegularExpressions.Regex.Match(heightRangeJson, @"""minHeight"":([-\d.]+)");
                    var maxMatch = System.Text.RegularExpressions.Regex.Match(heightRangeJson, @"""maxHeight"":([-\d.]+)");
                    var rangeMatch = System.Text.RegularExpressions.Regex.Match(heightRangeJson, @"""heightRange"":([-\d.]+)");
                    
                    if (minMatch.Success && maxMatch.Success && rangeMatch.Success)
                    {
                        actualMinHeight = float.Parse(minMatch.Groups[1].Value);
                        actualMaxHeight = float.Parse(maxMatch.Groups[1].Value);
                        actualHeightRange = float.Parse(rangeMatch.Groups[1].Value);
                        UnityEngine.Debug.Log($"Loaded height range: min={actualMinHeight}, max={actualMaxHeight}, range={actualHeightRange}");
                    }
                }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogWarning($"Failed to load height range from {heightRangePath}: {e.Message}");
                }
            }
            
            // Calculate terrain height from actual height range (like OpenMW does)
            // Convert from Morrowind height units to Unity world units using MORROWIND_TO_STATIC_SCALE
            // This matches how statics are positioned: 64 units per cell, 8192 Morrowind units per cell
            
            // Calculate terrain height from actual height range
            // If terrainHeight is 0 or not provided, calculate from actual range
            // Otherwise use provided value (for manual override)
            if (terrainHeight <= 0f && actualHeightRange > 0f)
            {
                // Convert height range from Morrowind units to Unity units
                // terrainHeight = actualHeightRange * MORROWIND_TO_STATIC_SCALE
                terrainHeight = actualHeightRange * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                UnityEngine.Debug.Log($"Calculated terrain height from actual range: {terrainHeight} " +
                    $"(Morrowind range={actualHeightRange}, converted with scale={TESGlobals.MORROWIND_TO_STATIC_SCALE})");
            }
            else if (terrainHeight <= 0f)
            {
                // Fallback if range file doesn't exist
                terrainHeight = 160f;
                UnityEngine.Debug.LogWarning($"Using fallback terrain height: {terrainHeight} (height range file not found)");
            }
            else
            {
                UnityEngine.Debug.Log($"Using provided terrain height: {terrainHeight}");
            }

            // Read the RAW file dimensions from the heightmap generation
            // RAW file uses 65 samples per cell (for seamless edges)
            // Note: Uses (Abs(Min) + Max) formula which matches the RAW file generation
            int rawWidth = (Math.Abs(Convert.ToInt32(MinCellX)) + Convert.ToInt32(MaxCellX)) * (int)TESGlobals.HEIGHTMAP_CELL_SIZE;
            int rawHeight = (Math.Abs(Convert.ToInt32(MinCellY)) + Convert.ToInt32(MaxCellY)) * (int)TESGlobals.HEIGHTMAP_CELL_SIZE;

            // Calculate number of cells from the RAW dimensions (more accurate than recalculating)
            // RAW file has 65 samples per cell, so divide by 65 to get cell count
            int numCellsX = rawWidth / (int)TESGlobals.HEIGHTMAP_CELL_SIZE;
            int numCellsY = rawHeight / (int)TESGlobals.HEIGHTMAP_CELL_SIZE;
            
            // The RAW file has 65 samples per cell (for neighbor alignment)
            // But we extract only 64x64 per cell (the actual playable area)
            // Calculate extracted dimensions (64 samples per cell)
            int extractedWidth = numCellsX * (int)TESGlobals.CELL_SIZE;
            int extractedHeight = numCellsY * (int)TESGlobals.CELL_SIZE;
            
            // Store extracted dimensions for final terrain size (64 units per cell)
            float originalWorldWidth = extractedWidth * 1.0f;
            float originalWorldHeight = extractedHeight * 1.0f;
            
            // For Unity import, we need square dimensions, but we'll restore original at the end
            float worldWidth = originalWorldWidth;
            float worldHeight = originalWorldHeight;

            // Read RAW heightmap data (16-bit little-endian)
            byte[] rawData = File.ReadAllBytes(rawFilePath);
            
            // Extract 64x64 chunks from each cell (skip the 65th row/column which is for neighbor alignment)
            // extractedWidth and extractedHeight are already calculated above
            
            // Unity terrain heightmap must be (width+1) x (height+1)
            int heightmapWidth = extractedWidth + 1;
            int heightmapHeight = extractedHeight + 1;
            float[,] heights = new float[heightmapHeight, heightmapWidth];

            // Extract 64x64 chunks from the 65x65 cell data
            // For each cell, take rows 0-63 and columns 0-63 (skip row 64 and column 64)
            for (int cellY = 0; cellY < numCellsY; cellY++)
            {
                for (int cellX = 0; cellX < numCellsX; cellX++)
                {
                    // Source position in 65x65 grid
                    int srcCellStartX = cellX * (int)TESGlobals.HEIGHTMAP_CELL_SIZE;
                    int srcCellStartY = cellY * (int)TESGlobals.HEIGHTMAP_CELL_SIZE;
                    
                    // Destination position in 64x64 grid
                    int dstCellStartX = cellX * (int)TESGlobals.CELL_SIZE;
                    int dstCellStartY = cellY * (int)TESGlobals.CELL_SIZE;
                    
                    // Copy 64x64 chunk (excluding the 65th row/column)
                    for (int localY = 0; localY < 64; localY++)
                    {
                        for (int localX = 0; localX < 64; localX++)
                        {
                            int srcX = srcCellStartX + localX;
                            int srcY = srcCellStartY + localY;
                            int dstX = dstCellStartX + localX;
                            int dstY = dstCellStartY + localY;
                            
                            // Read from RAW file (65x65 per cell)
                            if (srcX < rawWidth && srcY < rawHeight)
                            {
                                int byteIndex = (srcY * rawWidth + srcX) * 2;
                        if (byteIndex + 1 < rawData.Length)
                        {
                            ushort heightValue = (ushort)(rawData[byteIndex] | (rawData[byteIndex + 1] << 8));
                                    heights[dstY, dstX] = heightValue / 65535.0f; // Normalize to 0-1
                        }
                        else
                        {
                                    heights[dstY, dstX] = 0f;
                        }
                    }
                    else
                    {
                                heights[dstY, dstX] = 0f;
                            }
                        }
                    }
                }
            }
            
            // Pad the right and bottom edges for Unity's (width+1) x (height+1) requirement
            for (int y = 0; y < heightmapHeight; y++)
            {
                for (int x = 0; x < heightmapWidth; x++)
                {
                    if (x >= extractedWidth || y >= extractedHeight)
                    {
                        // Pad by duplicating the last valid pixel
                        int srcX = Mathf.Clamp(x, 0, extractedWidth - 1);
                        int srcY = Mathf.Clamp(y, 0, extractedHeight - 1);
                        heights[y, x] = heights[srcY, srcX];
                    }
                }
            }

            // Step 2: Create TerrainData
            TerrainData terrainData = new TerrainData();
            
            // Unity's heightmap resolution must be a power of 2 + 1 (33, 65, 129, 257, 513, 1025, 2049, 4097)
            // Unity uses a single value for heightmapResolution (it's always square)
            // Find the nearest valid resolution based on our max dimension
            int maxDimension = Mathf.Max(heightmapWidth, heightmapHeight);
            
            // Valid Unity heightmap resolutions (power of 2 + 1)
            int[] validResolutions = { 33, 65, 129, 257, 513, 1025, 2049, 4097 };
            int actualHeightmapResolution = 4097; // Default to max
            
            // Find the nearest valid resolution that fits our data
            for (int i = validResolutions.Length - 1; i >= 0; i--)
            {
                if (validResolutions[i] >= maxDimension)
                {
                    actualHeightmapResolution = validResolutions[i];
                }
                else
                {
                    break;
                }
            }
            
            // If we need to scale, use the chosen resolution
            if (heightmapWidth != actualHeightmapResolution || heightmapHeight != actualHeightmapResolution)
            {
                //UnityEngine.Debug.Log($"Scaling heightmap from {heightmapWidth}x{heightmapHeight} to {actualHeightmapResolution}x{actualHeightmapResolution} (Unity valid resolution)");
                
                // Scale the heightmap to the target square resolution using bilinear interpolation
                float[,] scaledHeights = new float[actualHeightmapResolution, actualHeightmapResolution];
                float scaleX = (float)(heightmapWidth - 1) / (actualHeightmapResolution - 1);
                float scaleY = (float)(heightmapHeight - 1) / (actualHeightmapResolution - 1);
                
                for (int y = 0; y < actualHeightmapResolution; y++)
                {
                    for (int x = 0; x < actualHeightmapResolution; x++)
                    {
                        float srcX = x * scaleX;
                        float srcY = y * scaleY;
                        int x0 = Mathf.FloorToInt(srcX);
                        int y0 = Mathf.FloorToInt(srcY);
                        int x1 = Mathf.Min(x0 + 1, heightmapWidth - 1);
                        int y1 = Mathf.Min(y0 + 1, heightmapHeight - 1);
                        
                        // Bilinear interpolation
                        float fx = srcX - x0;
                        float fy = srcY - y0;
                        float h00 = heights[y0, x0];
                        float h10 = heights[y0, x1];
                        float h01 = heights[y1, x0];
                        float h11 = heights[y1, x1];
                        float h0 = Mathf.Lerp(h00, h10, fx);
                        float h1 = Mathf.Lerp(h01, h11, fx);
                        scaledHeights[y, x] = Mathf.Lerp(h0, h1, fy);
                    }
                }
                heights = scaledHeights;
                
                // For Unity import, we temporarily use square dimensions
                // But we'll restore original dimensions at the end after SetHeights and SetAlphamaps
                float maxWorldSize = Mathf.Max(originalWorldWidth, originalWorldHeight);
                worldWidth = maxWorldSize;
                worldHeight = maxWorldSize;
            }
            
            // Set Unity's heightmap resolution to the valid resolution we chose
            terrainData.heightmapResolution = actualHeightmapResolution;
            
            // Temporarily set terrain size to square for Unity import (required for heightmap and splatmaps)
            // We'll restore original dimensions at the end
            terrainData.size = new Vector3(worldWidth, terrainHeight, worldHeight);
            
            //UnityEngine.Debug.Log($"Terrain dimensions: RAW file={rawWidth}x{rawHeight} (samples), World size={worldWidth}x{worldHeight} (units), Requested heightmap={heightmapWidth}x{heightmapHeight}, Actual heightmap resolution={actualHeightmapResolution}x{actualHeightmapResolution}, Terrain size={terrainData.size}, Cells={numCellsX}x{numCellsY}");
            
            // Set heights (heights array is now square and matches actualHeightmapResolution)
            terrainData.SetHeights(0, 0, heights);
            
            // After setting heights, check what Unity actually reports
            //UnityEngine.Debug.Log($"After SetHeights: Heightmap resolution={terrainData.heightmapResolution}, Terrain size={terrainData.size}");

            // Step 3: Build texture index to filename mapping (same as GatherLandTextures)
            // Store both NAME and DATA so we can try both when searching for extracted files
            Dictionary<int, (string primary, string fallback)> textureIndexToNames = new Dictionary<int, (string, string)>();
            foreach (Record rec in _records)
            {
                RecordLTex ltexRecord = rec as RecordLTex;
                if (ltexRecord != null)
                {
                    int textureIndex = -1;
                    string filename = null;
                    string name = null;

                    foreach (SubRecords subrec in ltexRecord.subRecords)
                    {
                        SubRecordLTexINTV intvSubrec = subrec as SubRecordLTexINTV;
                        if (intvSubrec != null)
                        {
                            textureIndex = intvSubrec.index;
                        }

                        SubRecordLTexData dataSubrec = subrec as SubRecordLTexData;
                        if (dataSubrec != null)
                        {
                            filename = dataSubrec.filename;
                        }

                        SubRecordLTexNAME nameSubrec = subrec as SubRecordLTexNAME;
                        if (nameSubrec != null)
                        {
                            // Clean null characters immediately when reading from subrecord
                            name = nameSubrec.name;
                            if (!string.IsNullOrEmpty(name))
                            {
                                name = name.TrimEnd('\0', ' ', '\t', '\r', '\n');
                                name = name.Replace("\0", ""); // Remove any remaining null characters
                                name = name.Trim();
                            }
                        }
                    }

                    // Store both NAME and DATA for file searching
                    if (textureIndex >= 0)
                    {
                        string primaryName = null;
                        string fallbackName = null;
                        
                        // NAME is already cleaned above, just use it directly
                        if (!string.IsNullOrEmpty(name))
                        {
                            primaryName = name;
                        }
                        
                        // Clean and store DATA filename as fallback
                        if (!string.IsNullOrEmpty(filename))
                        {
                            fallbackName = filename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                            fallbackName = fallbackName.Replace("\0", "");
                            fallbackName = fallbackName.Trim();
                        }
                        
                        if (!string.IsNullOrEmpty(primaryName) || !string.IsNullOrEmpty(fallbackName))
                        {
                            textureIndexToNames[textureIndex] = (primaryName ?? fallbackName, fallbackName);
                        }
                    }
                }
            }

            // Step 4: Collect all VTEX data and build texture map
            Dictionary<(int x, int y), ushort[][]> cellVTEXData = new Dictionary<(int, int), ushort[][]>();
            HashSet<ushort> uniqueTextureIndices = new HashSet<ushort>();

            foreach (Record rec in _records)
            {
                RecordLand landRecord = rec as RecordLand;
                if (landRecord != null)
                {
                    int cellx = 0;
                    int celly = 0;
                    ushort[][] vtexData = null;

                    foreach (SubRecords subrec in landRecord.subRecords)
                    {
                        SubRecordLandINTV intvSubrec = subrec as SubRecordLandINTV;
                        if (intvSubrec != null)
                        {
                            cellx = Convert.ToInt32(intvSubrec.CellX);
                            celly = Convert.ToInt32(intvSubrec.CellY);
                        }

                        SubRecordLandVTEX vtexSubrec = subrec as SubRecordLandVTEX;
                        if (vtexSubrec != null)
                        {
                            vtexData = vtexSubrec.indices;
                        }
                    }

                    if (vtexData != null)
                    {
                        cellVTEXData[(cellx, celly)] = vtexData;
                        for (int y = 0; y < 16; y++)
                        {
                            if (vtexData[y] != null)
                            {
                                for (int x = 0; x < 16; x++)
                                {
                                    uniqueTextureIndices.Add(vtexData[y][x]);
                                }
                            }
                        }
                    }
                }
            }

            // Step 5: Create a placeholder texture for missing/failed textures
            Texture2D CreatePlaceholderTexture(string name, Color color)
            {
                Texture2D placeholder = new Texture2D(64, 64, TextureFormat.RGB24, false);
                Color[] pixels = new Color[64 * 64];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = color;
                }
                placeholder.SetPixels(pixels);
                placeholder.Apply();
                placeholder.name = name;
                return placeholder;
            }
            
            // Create mask map texture to prevent shininess
            // Mask map channels: R=Metallic, G=Ambient Occlusion, B=Height, A=Smoothness
            // To remove shininess: R=0 (non-metallic), A=0 (rough/non-shiny)
            Texture2D CreateBlackMaskTexture()
            {
                Texture2D maskTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                Color32[] pixels = new Color32[64 * 64];
                for (int i = 0; i < pixels.Length; i++)
                {
                    // R=0 (non-metallic), G=255 (full AO), B=0 (no height), A=0 (rough/non-shiny)
                    pixels[i] = new Color32(0, 255, 0, 0);
                }
                maskTexture.SetPixels32(pixels);
                maskTexture.Apply();
                maskTexture.name = "NonShinyMask";
                return maskTexture;
            }

            Texture2D placeholderTexture = CreatePlaceholderTexture("Placeholder", new Color(0.5f, 0.5f, 0.5f, 1f)); // Gray placeholder
            Texture2D blackMaskTexture = CreateBlackMaskTexture(); // Mask texture to prevent shininess

            // Step 6: Create TerrainLayers for each unique texture
            List<TerrainLayer> terrainLayers = new List<TerrainLayer>();
            Dictionary<ushort, int> textureIndexToLayerIndex = new Dictionary<ushort, int>();

            UnityEngine.Debug.Log($"GenerateUnityTerrain: Found {uniqueTextureIndices.Count} unique VTEX indices, {textureIndexToNames.Count} LTEX texture mappings");

            // Sort texture indices to ensure consistent ordering
            List<ushort> sortedIndices = uniqueTextureIndices.OrderBy(x => x).ToList();

            foreach (ushort vtexIndex in sortedIndices)
            {
                // VTEX index 0 is default texture, skip it or use a default
                if (vtexIndex == 0)
                    continue;

                // Convert VTEX index to LTEX index (VTEX = LTEX + 1)
                int ltexIndex = vtexIndex - 1;

                if (textureIndexToNames.TryGetValue(ltexIndex, out var names))
                {
                    // Check texture library FIRST by VTEX index - textures might already be loaded from GatherLandTextures
                    Texture2D texture = null;
                    string texturePath = null;
                    string foundBaseName = null;
                    TESLTextureLibrary.TextureEntry cachedEntry = TESLTextureLibrary.GetTextureByVTEXIndex(vtexIndex);
                    if (cachedEntry != null)
                    {
                        texture = cachedEntry.Texture;
                        texturePath = cachedEntry.Filename;
                        foundBaseName = System.IO.Path.GetFileName(texturePath);
                        UnityEngine.Debug.Log($"Reusing cached texture from library (by VTEX {vtexIndex}): {cachedEntry.Filename}");
                    }
                    
                    // Try both NAME and DATA values, and also try with common Morrowind texture prefixes
                    List<string> namesToTry = new List<string>();
                    
                    // Helper function to sanitize texture names
                    Func<string, string> sanitizeName = (name) =>
                    {
                        if (string.IsNullOrEmpty(name))
                            return null;
                        string sanitized = name.TrimEnd('\0', ' ', '\t', '\r', '\n');
                        sanitized = sanitized.Replace("\0", "");
                        sanitized = sanitized.Trim();
                        return string.IsNullOrEmpty(sanitized) ? null : sanitized;
                    };
                    
                    if (!string.IsNullOrEmpty(names.primary))
                    {
                        string sanitizedPrimary = sanitizeName(names.primary);
                        if (!string.IsNullOrEmpty(sanitizedPrimary))
                        {
                            namesToTry.Add(sanitizedPrimary);
                        // Try with common prefixes (only if not already present)
                            if (!sanitizedPrimary.StartsWith("Tx_", StringComparison.OrdinalIgnoreCase) && 
                                !sanitizedPrimary.StartsWith("tx_", StringComparison.OrdinalIgnoreCase))
                        {
                                namesToTry.Add("Tx_" + sanitizedPrimary);
                                namesToTry.Add("tx_" + sanitizedPrimary.ToLower());
                        }
                        // Try removing spaces (common in Morrowind texture names)
                            string primaryNoSpaces = sanitizedPrimary.Replace(" ", "");
                            if (primaryNoSpaces != sanitizedPrimary)
                        {
                            namesToTry.Add(primaryNoSpaces);
                            if (!primaryNoSpaces.StartsWith("Tx_", StringComparison.OrdinalIgnoreCase) && 
                                !primaryNoSpaces.StartsWith("tx_", StringComparison.OrdinalIgnoreCase))
                            {
                                namesToTry.Add("Tx_" + primaryNoSpaces);
                                namesToTry.Add("tx_" + primaryNoSpaces.ToLower());
                                }
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(names.fallback))
                    {
                        string sanitizedFallback = sanitizeName(names.fallback);
                        if (!string.IsNullOrEmpty(sanitizedFallback))
                        {
                            namesToTry.Add(sanitizedFallback);
                            string fallbackBase = System.IO.Path.GetFileNameWithoutExtension(sanitizedFallback);
                            // Sanitize the base name too
                            fallbackBase = sanitizeName(fallbackBase);
                            if (!string.IsNullOrEmpty(fallbackBase))
                            {
                        namesToTry.Add(fallbackBase);
                        // Also try with Tx_ prefix if not already there
                        if (!fallbackBase.StartsWith("Tx_", StringComparison.OrdinalIgnoreCase) && 
                            !fallbackBase.StartsWith("tx_", StringComparison.OrdinalIgnoreCase))
                        {
                            namesToTry.Add("Tx_" + fallbackBase);
                            namesToTry.Add("tx_" + fallbackBase.ToLower());
                                }
                            }
                        }
                    }
                    // Remove duplicates
                    namesToTry = namesToTry.Distinct().ToList();
                    
                    // Try to find the texture file (case-insensitive) - Check all ESM cache directories
                    // Only search if not already found in library
                    if (texture == null)
                    {
                        // Get all loaded ESMs to check their cache directories
                        string[] loadedESMs = TESESMLibrary.GetLoadedESMFilenames();
                        
                        // Try preferred ESM first if specified
                        string preferredESMName = System.IO.Path.GetFileNameWithoutExtension(_esm);
                        if (!string.IsNullOrEmpty(preferredESMName))
                        {
                            string preferredTextureDir = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Textures", preferredESMName);
                            texturePath = FindTextureInCacheDirectories(preferredTextureDir, namesToTry, vtexIndex);
                            if (texturePath != null)
                            {
                                foundBaseName = System.IO.Path.GetFileName(texturePath);
                            }
                        }
                        
                        // Check all ESM cache directories if not found in preferred
                        if (texturePath == null)
                        {
                            foreach (string esmFilename in loadedESMs)
                            {
                                string esmName = System.IO.Path.GetFileNameWithoutExtension(esmFilename);
                                // Skip if we already checked this one
                                if (string.Equals(esmName, preferredESMName, StringComparison.OrdinalIgnoreCase))
                                    continue;
                                    
                                string textureDir = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Textures", esmName);
                                texturePath = FindTextureInCacheDirectories(textureDir, namesToTry, vtexIndex);
                                if (texturePath != null)
                                {
                                    foundBaseName = System.IO.Path.GetFileName(texturePath);
                                        break;
                                }
                            }
                        }
                    }
                    
                    bool usePlaceholder = false;
                    
                    // Check global texture library by path if we found a file - MATCHING ORIGINAL WORKING CODE
                    if (texture == null && texturePath != null)
                    {
                        cachedEntry = TESLTextureLibrary.GetTextureByPath(texturePath);
                        if (cachedEntry != null)
                        {
                            texture = cachedEntry.Texture;
                            if (texture != null)
                            {
                                UnityEngine.Debug.Log($"Reusing cached texture from library: {System.IO.Path.GetFileName(texturePath)}");
                            }
                            else
                            {
                                UnityEngine.Debug.LogWarning($"[VTEX {vtexIndex}] Library entry found but Texture is null for: {System.IO.Path.GetFileName(texturePath)}");
                            }
                        }
                    }
                    
                    // Only try to load from file if texture is still null
                    if (texture == null && texturePath != null && File.Exists(texturePath))
                    {
                        try
                        {
                            string textureExtension = System.IO.Path.GetExtension(texturePath).ToLower();
                            
                            // Handle DDS files - convert to PNG if PNG doesn't exist
                            if (textureExtension == ".dds")
                            {
                                // Check if PNG version exists
                                string pngPath = System.IO.Path.ChangeExtension(texturePath, ".png");
                                if (!System.IO.File.Exists(pngPath))
                                {
                                    // Convert DDS to PNG
                                    //UnityEngine.Debug.Log($"DDS file found but no PNG exists, converting: {texturePath}");
                                    byte[] ddsData = System.IO.File.ReadAllBytes(texturePath);
                                    if (ConvertDDSToPNG(ddsData, pngPath))
                                    {
                                        //UnityEngine.Debug.Log($"Successfully converted DDS to PNG: {texturePath} -> {pngPath}");
                                        #if UNITY_EDITOR
                                        SetTextureImportSettings(pngPath);
                                        #endif
                                        texturePath = pngPath; // Use PNG instead
                                        textureExtension = ".png";
                                        foundBaseName = System.IO.Path.GetFileName(pngPath);
                                        
                                        // Check library again with PNG path after conversion
                                        if (texture == null)
                                        {
                                            TESLTextureLibrary.TextureEntry convertedEntry = TESLTextureLibrary.GetTextureByPath(pngPath);
                                            if (convertedEntry != null)
                                            {
                                                texture = convertedEntry.Texture;
                                                UnityEngine.Debug.Log($"Reusing cached texture from library (after DDS->PNG conversion): {System.IO.Path.GetFileName(pngPath)}");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        //UnityEngine.Debug.LogWarning($"Failed to convert DDS to PNG: {texturePath}, will try to load DDS directly");
                                    }
                                }
                                else
                                {
                                    // PNG exists, use it instead
                                    //UnityEngine.Debug.Log($"PNG version exists, using PNG instead of DDS: {pngPath}");
                                    texturePath = pngPath;
                                    textureExtension = ".png";
                                    foundBaseName = System.IO.Path.GetFileName(pngPath);
                                    
                                    // Check library again with PNG path
                                    if (texture == null)
                                    {
                                        cachedEntry = TESLTextureLibrary.GetTextureByPath(pngPath);
                                        if (cachedEntry != null)
                                        {
                                            texture = cachedEntry.Texture;
                                            UnityEngine.Debug.Log($"Reusing cached texture from library (PNG path): {System.IO.Path.GetFileName(pngPath)}");
                                        }
                                    }
                                }
                            }
                            
                            // Handle TGA files - convert to PNG if PNG doesn't exist
                            if (textureExtension == ".tga")
                            {
                                // Check if PNG version exists
                                string pngPath = System.IO.Path.ChangeExtension(texturePath, ".png");
                                if (!System.IO.File.Exists(pngPath))
                                {
                                    // Convert TGA to PNG (32-bit RGBA, alpha from grayscale)
                                    UnityEngine.Debug.Log($"TGA file found but no PNG exists, converting: {texturePath}");
                                    byte[] tgaData = System.IO.File.ReadAllBytes(texturePath);
                                    Texture2D tempTexture = new Texture2D(2, 2);
                                    if (tempTexture.LoadImage(tgaData))
                                    {
                                        // Keep as RGBA32 (32-bit with alpha) - alpha will be set to "From grayscale" in import settings
                                        Texture2D rgbaTexture = new Texture2D(tempTexture.width, tempTexture.height, TextureFormat.RGBA32, false);
                                        Color32[] pixels = tempTexture.GetPixels32();
                                        // Ensure all alpha values are 255 (opaque) - Unity will use grayscale for alpha via import settings
                                        for (int i = 0; i < pixels.Length; i++)
                                        {
                                            pixels[i].a = 255;
                                        }
                                        rgbaTexture.SetPixels32(pixels);
                                        rgbaTexture.Apply();
                                        
                                        byte[] pngData = rgbaTexture.EncodeToPNG();
                                        System.IO.File.WriteAllBytes(pngPath, pngData);
                                        UnityEngine.Debug.Log($"Successfully converted TGA to PNG (32-bit): {texturePath} -> {pngPath}");
                                        #if UNITY_EDITOR
                                        SetTextureImportSettings(pngPath);
                                        #endif
                                        
                                        UnityEngine.Object.DestroyImmediate(tempTexture);
                                        UnityEngine.Object.DestroyImmediate(rgbaTexture);
                                        
                                        texturePath = pngPath; // Use PNG instead
                                        textureExtension = ".png";
                                        foundBaseName = System.IO.Path.GetFileName(pngPath);
                                        
                                        // Check library again with PNG path after conversion
                                        if (texture == null)
                                        {
                                            TESLTextureLibrary.TextureEntry convertedEntry = TESLTextureLibrary.GetTextureByPath(pngPath);
                                            if (convertedEntry != null)
                                            {
                                                texture = convertedEntry.Texture;
                                                UnityEngine.Debug.Log($"Reusing cached texture from library (after TGA->PNG conversion): {System.IO.Path.GetFileName(pngPath)}");
                                            }
                                        }
                                        
                                        UnityEngine.Object.DestroyImmediate(tempTexture);
                                    }
                                    else
                                    {
                                        UnityEngine.Debug.LogWarning($"Failed to convert TGA to PNG: {texturePath}");
                                        UnityEngine.Object.DestroyImmediate(tempTexture);
                                    }
                                }
                                else
                                {
                                    // PNG exists, use it instead
                                    UnityEngine.Debug.Log($"PNG version exists, using PNG instead of TGA: {pngPath}");
                                    texturePath = pngPath;
                                    textureExtension = ".png";
                                    foundBaseName = System.IO.Path.GetFileName(pngPath);
                                    
                                    // Check library again with PNG path
                                    if (texture == null)
                                    {
                                        cachedEntry = TESLTextureLibrary.GetTextureByPath(pngPath);
                                        if (cachedEntry != null)
                                        {
                                            texture = cachedEntry.Texture;
                                            UnityEngine.Debug.Log($"Reusing cached texture from library (PNG path): {System.IO.Path.GetFileName(pngPath)}");
                                        }
                                    }
                                }
                            }
                            
                            // Now load the texture (should be PNG at this point, or original format if conversion failed)
                            if (textureExtension == ".dds")
                            {
                                #if UNITY_EDITOR
                                // In editor, use AssetDatabase to load DDS (fallback if conversion failed)
                                // Normalize path separators first
                                string normalizedPath = texturePath.Replace('\\', '/');
                                string normalizedDataPath = Application.dataPath.Replace('\\', '/');
                                
                                // Get relative path from Assets folder
                                string relativePath = normalizedPath;
                                if (normalizedPath.StartsWith(normalizedDataPath, StringComparison.OrdinalIgnoreCase))
                                {
                                    relativePath = normalizedPath.Substring(normalizedDataPath.Length);
                                }
                                
                                // Ensure it starts with /
                                if (!relativePath.StartsWith("/"))
                                    relativePath = "/" + relativePath;
                                
                                string assetPath = "Assets" + relativePath;
                                
                                UnityEngine.Debug.Log($"Loading DDS via AssetDatabase: {assetPath} (from {texturePath})");
                                
                                UnityEditor.AssetDatabase.ImportAsset(assetPath, UnityEditor.ImportAssetOptions.ForceUpdate);
                                texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                                
                                if (texture == null)
                                {
                                    UnityEngine.Debug.LogWarning($"DDS file found but could not load via AssetDatabase. Path: {texturePath}, AssetPath: {assetPath}, using placeholder");
                                    usePlaceholder = true;
                                }
                                else
                                {
                                    // Make texture readable
                                    texture.wrapMode = TextureWrapMode.Repeat;
                                    texture.filterMode = FilterMode.Bilinear;
                                    texture.anisoLevel = 9;
                                    UnityEngine.Debug.Log($"Loaded DDS texture layer {terrainLayers.Count}: {foundBaseName ?? System.IO.Path.GetFileName(texturePath)} ({texture.width}x{texture.height})");
                                    
                                    // Set import settings to disable alpha source (prevents shininess)
                                    #if UNITY_EDITOR
                                    SetTextureImportSettings(texturePath);
                                    #endif
                                    
                                    // Add to global texture library
                                    string textureName = names.primary ?? names.fallback ?? foundBaseName ?? System.IO.Path.GetFileNameWithoutExtension(texturePath);
                                    cachedEntry = TESLTextureLibrary.AddTexture(textureName, ltexIndex, vtexIndex, texturePath, texture);
                                }
                                #else
                                // At runtime, DDS loading is more complex - use placeholder for now
                                UnityEngine.Debug.LogWarning($"DDS files not supported at runtime: {texturePath}, using placeholder");
                                usePlaceholder = true;
                                #endif
                            }
                            else
                            {
                                // Load PNG, JPG, TGA, etc. using standard LoadImage
                                byte[] textureData = File.ReadAllBytes(texturePath);
                                texture = new Texture2D(2, 2);
                                
                                // LoadImage will auto-detect format (PNG, JPG, TGA, etc.)
                                if (texture.LoadImage(textureData))
                                {
                                    // Configure texture for terrain use
                                    texture.wrapMode = TextureWrapMode.Repeat;
                                    texture.filterMode = FilterMode.Bilinear;
                                    texture.anisoLevel = 9; // Good quality for terrain
                                    
                                    // Make texture readable (important for terrain)
                                    #if UNITY_EDITOR
                                    // In editor, we can directly modify the texture
                                    texture.Apply(true, false);
                                    #else
                                    // At runtime, we need to make it readable
                                    texture.Apply(false, false);
                                    #endif

                                    //UnityEngine.Debug.Log($"Loaded texture layer {terrainLayers.Count}: {foundBaseName ?? System.IO.Path.GetFileName(texturePath)} ({texture.width}x{texture.height})");
                                    
                                    // Set import settings to disable alpha source (prevents shininess)
                                    #if UNITY_EDITOR
                                    SetTextureImportSettings(texturePath);
                                    #endif
                                    
                                    // Add to global texture library
                                    string textureName = names.primary ?? names.fallback ?? foundBaseName ?? System.IO.Path.GetFileNameWithoutExtension(texturePath);
                                    cachedEntry = TESLTextureLibrary.AddTexture(textureName, ltexIndex, vtexIndex, texturePath, texture);
                                }
                                else
                                {
                                    UnityEngine.Debug.LogWarning($"Failed to load image data from: {texturePath}, using placeholder");
                                    usePlaceholder = true;
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            UnityEngine.Debug.LogWarning($"Error loading texture {texturePath}: {ex.Message}, using placeholder");
                            usePlaceholder = true;
                        }
                    }
                    else if (texture == null)
                    {
                        // Texture not found in cache - try extracting from BSAs
                        // Use the same approach as GatherLandTextures: get all BSA file names first
                        HashSet<string> allBSAFiles = TESBSALibrary.GetAllFileNames();
                        bool extractedFromBSA = false;
                        string[] loadedESMs = TESESMLibrary.GetLoadedESMFilenames();
                        
                        foreach (string textureName in namesToTry)
                        {
                            if (extractedFromBSA) break;
                            
                            // Sanitize texture name (remove null characters, trim whitespace)
                            string sanitizedTextureName = textureName;
                            if (!string.IsNullOrEmpty(sanitizedTextureName))
                            {
                                sanitizedTextureName = sanitizedTextureName.TrimEnd('\0', ' ', '\t', '\r', '\n');
                                sanitizedTextureName = sanitizedTextureName.Replace("\0", ""); // Remove any remaining null characters
                                sanitizedTextureName = sanitizedTextureName.Trim();
                            }
                            
                            if (string.IsNullOrEmpty(sanitizedTextureName))
                                continue;
                            
                            string baseName = System.IO.Path.GetFileNameWithoutExtension(sanitizedTextureName);
                            
                            // Sanitize baseName as well (in case Path.GetFileNameWithoutExtension didn't catch everything)
                            if (!string.IsNullOrEmpty(baseName))
                            {
                                baseName = baseName.TrimEnd('\0', ' ', '\t', '\r', '\n');
                                baseName = baseName.Replace("\0", "");
                                baseName = baseName.Trim();
                            }
                            
                            if (string.IsNullOrEmpty(baseName))
                                continue;
                            
                            // Try multiple BSA path variations (try both forward and backslash)
                            List<string> bsaPathVariations = new List<string>();
                            bsaPathVariations.Add(sanitizedTextureName.Replace('/', '\\'));  // Original path with backslash
                            bsaPathVariations.Add(sanitizedTextureName.Replace('\\', '/'));  // Original path with forward slash
                            bsaPathVariations.Add(baseName);  // Just the filename
                            bsaPathVariations.Add($"textures\\{baseName}");  // textures\filename
                            bsaPathVariations.Add($"textures/{baseName}");  // textures/filename
                            bsaPathVariations.Add($"textures\\landscape\\{baseName}");  // textures\landscape\filename
                            bsaPathVariations.Add($"textures/landscape/{baseName}");  // textures/landscape/filename
                            bsaPathVariations.Add($"textures\\terrain\\{baseName}");  // textures\terrain\filename
                            bsaPathVariations.Add($"textures/terrain/{baseName}");  // textures/terrain/filename
                            bsaPathVariations.Add($"textures\\tx\\{baseName}");  // textures\tx\filename
                            bsaPathVariations.Add($"textures/tx/{baseName}");  // textures/tx/filename
                            
                            string[] bsaExtensions = new[] { ".dds", ".tga", ".png" };
                            
                            string foundPath = null;
                            string usedName = null;
                            
                            // Search in the HashSet first (faster, case-insensitive)
                            foreach (string bsaPathBase in bsaPathVariations)
                            {
                                if (foundPath != null) break;
                                
                                foreach (string ext in bsaExtensions)
                                {
                                    string bsaFileName = bsaPathBase + ext;
                                    
                                    // Check in HashSet (case-insensitive)
                                    if (allBSAFiles.Contains(bsaFileName))
                                    {
                                        foundPath = bsaFileName;
                                        usedName = textureName;
                                        break;
                                    }
                                }
                            }
                            
                            // If not found in common paths, try case-insensitive search in HashSet
                            if (foundPath == null)
                            {
                                foreach (string bsaFileName in allBSAFiles)
                                {
                                    string bsaBaseName = System.IO.Path.GetFileName(bsaFileName);
                                    string bsaBaseNameNoExt = System.IO.Path.GetFileNameWithoutExtension(bsaBaseName);
                                    
                                    // Try matching by base filename
                                    if (bsaBaseNameNoExt.Equals(baseName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        foundPath = bsaFileName;
                                        usedName = textureName;
                                        break;
                                    }
                                }
                            }
                            
                            if (foundPath != null)
                            {
                                // Determine which ESM's BSA contains this file and get the exact path format
                                var esmEntries = TESESMLibrary.GetLoadedESMEntries();
                                string sourceESM = null;
                                string exactBSAPath = null;
                                
                                foreach (var esmEntry in esmEntries)
                                {
                                    var bsaEntry = TESBSALibrary.GetBSAEntryForESM(esmEntry.ESMFilename);
                                    if (bsaEntry != null && bsaEntry.IsLoaded)
                                    {
                                        // Check if the found path exists in this BSA (try path variations)
                                        if (bsaEntry.FileNames.Contains(foundPath))
                                        {
                                            exactBSAPath = foundPath;
                                            sourceESM = System.IO.Path.GetFileNameWithoutExtension(esmEntry.ESMFilename);
                                            break;
                                        }
                                        // Try with different path separators
                                        string foundPathAlt = foundPath.Replace('\\', '/');
                                        if (foundPathAlt != foundPath && bsaEntry.FileNames.Contains(foundPathAlt))
                                        {
                                            exactBSAPath = foundPathAlt;
                                            sourceESM = System.IO.Path.GetFileNameWithoutExtension(esmEntry.ESMFilename);
                                            break;
                                        }
                                        string foundPathAlt2 = foundPath.Replace('/', '\\');
                                        if (foundPathAlt2 != foundPath && bsaEntry.FileNames.Contains(foundPathAlt2))
                                        {
                                            exactBSAPath = foundPathAlt2;
                                            sourceESM = System.IO.Path.GetFileNameWithoutExtension(esmEntry.ESMFilename);
                                            break;
                                        }
                                        // Try filename-only match
                                        string baseFilename = System.IO.Path.GetFileName(foundPath);
                                        foreach (string bsaFileName in bsaEntry.FileNames)
                                        {
                                            if (System.IO.Path.GetFileName(bsaFileName).Equals(baseFilename, StringComparison.OrdinalIgnoreCase))
                                            {
                                                exactBSAPath = bsaFileName;
                                                sourceESM = System.IO.Path.GetFileNameWithoutExtension(esmEntry.ESMFilename);
                                                break;
                                            }
                                        }
                                        if (!string.IsNullOrEmpty(exactBSAPath)) break;
                                    }
                                }
                                
                                if (string.IsNullOrEmpty(sourceESM))
                                {
                                    // Fallback: use first loaded ESM
                                    if (esmEntries.Length > 0)
                                    {
                                        sourceESM = System.IO.Path.GetFileNameWithoutExtension(esmEntries[0].ESMFilename);
                                    }
                                }
                                
                                // Use the exact BSA path if we found it, otherwise use the found path
                                string pathToExtract = exactBSAPath ?? foundPath;
                                
                                // Extract to the correct ESM cache directory
                                string targetCacheDir = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Textures", sourceESM);
                                System.IO.Directory.CreateDirectory(targetCacheDir);
                                
                                // Get the actual extension from the BSA path
                                string bsaExtension = System.IO.Path.GetExtension(pathToExtract).ToLower();
                                string outputPath = System.IO.Path.Combine(targetCacheDir, baseName + bsaExtension);
                                
                                if (TESBSALibrary.ExtractFile(pathToExtract, outputPath))
                                {
                                    UnityEngine.Debug.Log($"[VTEX {vtexIndex}] Extracted texture from BSA: {foundPath} -> {System.IO.Path.GetFileName(outputPath)} (from {sourceESM})");
                                    
                                    // Convert to PNG if needed
                                    if (bsaExtension == ".dds" || bsaExtension == ".tga")
                                    {
                                        string pngPath = System.IO.Path.ChangeExtension(outputPath, ".png");
                                        
                                        if (bsaExtension == ".dds")
                                        {
                                            try
                                            {
                                                byte[] ddsData = System.IO.File.ReadAllBytes(outputPath);
                                                if (ConvertDDSToPNG(ddsData, pngPath))
                                                {
                                                    #if UNITY_EDITOR
                                                    SetTextureImportSettings(pngPath);
                                                    #endif
                                                    texturePath = pngPath;
                                                    foundBaseName = baseName + ".png";
                                                    extractedFromBSA = true;
                                                    break;
                                                }
                                            }
                                            catch (System.Exception ex)
                                            {
                                                UnityEngine.Debug.LogWarning($"[VTEX {vtexIndex}] Error converting extracted DDS to PNG: {ex.Message}");
                                            }
                                        }
                                        else if (bsaExtension == ".tga")
                                        {
                                            try
                                            {
                                                byte[] tgaData = System.IO.File.ReadAllBytes(outputPath);
                                                Texture2D tempTexture = new Texture2D(2, 2);
                                                if (tempTexture.LoadImage(tgaData))
                                                {
                                                    Texture2D rgbaTexture = new Texture2D(tempTexture.width, tempTexture.height, TextureFormat.RGBA32, false);
                                                    Color32[] pixels = tempTexture.GetPixels32();
                                                    for (int i = 0; i < pixels.Length; i++)
                                                    {
                                                        pixels[i].a = 255;
                                                    }
                                                    rgbaTexture.SetPixels32(pixels);
                                                    rgbaTexture.Apply();
                                                    
                                                    byte[] pngData = rgbaTexture.EncodeToPNG();
                                                    System.IO.File.WriteAllBytes(pngPath, pngData);
                                                    UnityEngine.Object.DestroyImmediate(tempTexture);
                                                    UnityEngine.Object.DestroyImmediate(rgbaTexture);
                                                    #if UNITY_EDITOR
                                                    SetTextureImportSettings(pngPath);
                                                    #endif
                                                    texturePath = pngPath;
                                                    foundBaseName = baseName + ".png";
                                                    extractedFromBSA = true;
                                                    break;
                                                }
                                                UnityEngine.Object.DestroyImmediate(tempTexture);
                                            }
                                            catch (System.Exception ex)
                                            {
                                                UnityEngine.Debug.LogWarning($"[VTEX {vtexIndex}] Error converting extracted TGA to PNG: {ex.Message}");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // Already PNG
                                        texturePath = outputPath;
                                        foundBaseName = baseName + ".png";
                                        extractedFromBSA = true;
                                        break;
                                    }
                                }
                            }
                        }
                        
                        // If we extracted from BSA, try loading the texture again
                        if (extractedFromBSA && texturePath != null && System.IO.File.Exists(texturePath))
                        {
                            try
                            {
                                byte[] textureData = System.IO.File.ReadAllBytes(texturePath);
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
                                    
                                    #if UNITY_EDITOR
                                    SetTextureImportSettings(texturePath);
                                    #endif
                                    
                                    string textureName = names.primary ?? names.fallback ?? foundBaseName ?? System.IO.Path.GetFileNameWithoutExtension(texturePath);
                                    cachedEntry = TESLTextureLibrary.AddTexture(textureName, ltexIndex, vtexIndex, texturePath, texture);
                                    
                                    UnityEngine.Debug.Log($"[VTEX {vtexIndex}] Successfully loaded extracted texture: {System.IO.Path.GetFileName(texturePath)}");
                                }
                                else
                                {
                                    UnityEngine.Debug.LogWarning($"[VTEX {vtexIndex}] Failed to load extracted texture image data");
                                    usePlaceholder = true;
                                }
                            }
                            catch (System.Exception ex)
                            {
                                UnityEngine.Debug.LogWarning($"[VTEX {vtexIndex}] Error loading extracted texture: {ex.Message}");
                                usePlaceholder = true;
                            }
                        }
                        else if (!extractedFromBSA)
                        {
                            // Only show "not found" warning if we didn't try BSA extraction or it failed
                        string triedNames = string.Join(", ", namesToTry);
                        string triedWithExts = string.Join(", ", namesToTry.SelectMany(n => 
                        {
                            string baseName = System.IO.Path.GetFileNameWithoutExtension(n);
                            return new[] { ".png", ".dds", ".tga" }.Select(ext => baseName + ext);
                        }).Distinct());
                            
                            // Build list of directories that were searched
                            List<string> searchedDirs = new List<string>();
                            foreach (string esmFilename in loadedESMs)
                            {
                                string esmName = System.IO.Path.GetFileNameWithoutExtension(esmFilename);
                                string dir = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Textures", esmName);
                                if (Directory.Exists(dir))
                                {
                                    searchedDirs.Add(esmName);
                                }
                            }
                            string searchedDirsStr = searchedDirs.Count > 0 ? string.Join(", ", searchedDirs) : "none (directories don't exist)";
                            
                            UnityEngine.Debug.LogWarning($"[VTEX {vtexIndex}] Texture file not found for VTEX {vtexIndex} (LTEX {ltexIndex}) (tried: {triedNames}) (with extensions: {triedWithExts}) (searched in ESM cache dirs: {searchedDirsStr}, checked all BSAs), using placeholder");
                        usePlaceholder = true;
                        }
                    }

                    // Use placeholder if texture failed to load or wasn't found
                    if (usePlaceholder || texture == null)
                    {
                        string placeholderName = foundBaseName ?? names.primary ?? names.fallback ?? $"Index_{ltexIndex}";
                        texture = CreatePlaceholderTexture($"Placeholder_{placeholderName}", new Color(0.7f, 0.5f, 0.3f, 1f)); // Brownish placeholder
                    }

                    // Create TerrainLayer (always create one, even if using placeholder)
                    TerrainLayer layer = new TerrainLayer();
                    layer.diffuseTexture = texture;
                    layer.maskMapTexture = blackMaskTexture; // Black mask map to prevent shininess
                    layer.tileSize = new Vector2(8, 8); // Tiling size for better texture appearance
                    layer.tileOffset = Vector2.zero;
                    
                    // Set metallic and smoothness to reduce shininess (URP terrain shader uses these)
                    layer.metallic = 0.0f; // No metallic
                    layer.smoothness = 0.1f; // Low smoothness to reduce shininess
                    
                    // Get or generate normal map for this texture
                    if (cachedEntry != null)
                    {
                        Texture2D normalMap = TESLTextureLibrary.GetOrGenerateNormalMap(cachedEntry);
                        if (normalMap != null)
                        {
                            layer.normalMapTexture = normalMap;
                        }
                    }
                    
                    terrainLayers.Add(layer);
                    int assignedLayerIndex = terrainLayers.Count - 1;
                    textureIndexToLayerIndex[vtexIndex] = assignedLayerIndex;
                }
                else
                {
                    // No LTEX record found for this VTEX index, skip layer creation
                    UnityEngine.Debug.LogWarning($"GenerateUnityTerrain: No LTEX record found for VTEX {vtexIndex} (LTEX {ltexIndex}), skipping layer creation");
                }
            }

            // Always create at least one layer (placeholder if needed)
            if (terrainLayers.Count == 0)
            {
                UnityEngine.Debug.LogWarning("No terrain layers found, creating default placeholder layer.");
                TerrainLayer defaultLayer = new TerrainLayer();
                defaultLayer.diffuseTexture = placeholderTexture;
                defaultLayer.maskMapTexture = blackMaskTexture; // Black mask map to prevent shininess
                defaultLayer.tileSize = new Vector2(8, 8);
                defaultLayer.tileOffset = Vector2.zero;
                defaultLayer.metallic = 0.0f;
                defaultLayer.smoothness = 0.1f;
                terrainLayers.Add(defaultLayer);
            }

            terrainData.terrainLayers = terrainLayers.ToArray();

            UnityEngine.Debug.Log($"Created {terrainLayers.Count} terrain layers (including placeholders for missing textures)");

            // Step 7: Create splatmap (alpha map) from VTEX data
            int alphamapWidth = terrainData.alphamapWidth;
            int alphamapHeight = terrainData.alphamapHeight;
            
            // Unity's alphamap resolution might be different from what we expect
            // It's typically calculated as: min(terrainSize / detailResolutionPerPatch, maxResolution)
            // If heightmap appears 1/3 smaller, we might need to account for this
            UnityEngine.Debug.Log($"Alphamap resolution from Unity: {alphamapWidth}x{alphamapHeight}, Terrain size: {terrainData.size}, Heightmap resolution: {terrainData.heightmapResolution}");
            
            float[,,] alphamaps = new float[alphamapHeight, alphamapWidth, terrainLayers.Count];

            // Initialize all to 0
            for (int y = 0; y < alphamapHeight; y++)
            {
                for (int x = 0; x < alphamapWidth; x++)
                {
                    for (int layer = 0; layer < terrainLayers.Count; layer++)
                    {
                        alphamaps[y, x, layer] = 0f;
                    }
                }
            }

            // Map VTEX data to alphamap
            // Use the same coordinate system as GenerateHeightMap
            const int CELL = 65;
            const int VTEX_SIZE = 16;
            const int STEP = 65; // Cell size in world units (matches heightmap samples per cell)
            const int HALF = CELL / 2;
            
            // Calculate cell positions in terrain coordinates
            // Use rawWidth/rawHeight for coordinate calculations (these are the actual heightmap dimensions)
            // The terrain in Unity starts at (0,0), but our coordinate system uses a center-based approach
            // We need to convert from center-based to origin-based coordinates
            int cx = rawWidth / 2;
            int cy = rawHeight / 2;
            
            // Calculate offset to account for terrain starting at (0,0) instead of center
            // If MinCellX is negative, we need to shift the coordinate system
            int terrainOffsetX = (Math.Abs(Convert.ToInt32(MinCellX)) * CELL);
            int terrainOffsetY = (Math.Abs(Convert.ToInt32(MinCellY)) * CELL);
            
            // Debug: log coordinate system info
            UnityEngine.Debug.Log($"VTEX to Alphamap mapping: rawWidth={rawWidth}, rawHeight={rawHeight}, alphamapWidth={alphamapWidth}, alphamapHeight={alphamapHeight}, MinCellX={MinCellX}, MinCellY={MinCellY}, terrainOffset=({terrainOffsetX},{terrainOffsetY})");

            foreach (var kvp in cellVTEXData)
            {
                int cellx = kvp.Key.x;
                int celly = kvp.Key.y;
                ushort[][] vtexIndices = kvp.Value;

                // Calculate cell position in terrain coordinates
                // The terrain in Unity starts at (0,0), so we need to adjust for the actual terrain bounds
                // Account for negative cell coordinates by offsetting based on MinCellX/MinCellY
                int cellOffsetX = (cellx - Convert.ToInt32(MinCellX)) * STEP;
                int cellOffsetY = (celly - Convert.ToInt32(MinCellY)) * STEP;
                
                // Start position: each cell is 65 pixels, spaced 65 apart
                // With correct 65x65 cell size, no offset needed - cellOffsetX/Y already accounts for cell positions
                int startX = HALF + cellOffsetX + (STEP/2);  // Add 1 cells to fix X offset
                int startY = HALF + cellOffsetY - STEP;         // Subtract 1 cell to fix Z offset

                // Calculate scale factors: map from terrain coordinates (rawWidth x rawHeight) to alphamap coordinates
                // Since the heightmap is now correctly scaled, we can use simple terrain-based scaling
                float vtexToAlphamapX = (float)alphamapWidth / rawWidth;
                float vtexToAlphamapY = (float)alphamapHeight / rawHeight;

                for (int vy = 0; vy < VTEX_SIZE; vy++)
                {
                    if (vtexIndices[vy] == null) continue;

                    for (int vx = 0; vx < VTEX_SIZE; vx++)
                    {
                        ushort vtexIndex = vtexIndices[vy][vx];
                        
                        if (vtexIndex == 0 || !textureIndexToLayerIndex.TryGetValue(vtexIndex, out int layerIndex))
                            continue;

                        // Map each VTEX entry to terrain coordinates
                        // Each cell is 65x65, VTEX is 16x16, so each VTEX entry covers 65/16 = 4.0625 terrain pixels (rounded to 4)
                        int terrainX = startX + (vx * STEP / VTEX_SIZE);
                        int terrainY = startY + (vy * STEP / VTEX_SIZE);
                        
                        // Use the center of the VTEX coverage area for more accurate mapping
                        int terrainCenterX = terrainX + (STEP / VTEX_SIZE) / 2;
                        int terrainCenterY = terrainY + (STEP / VTEX_SIZE) / 2;
                        
                        // Ensure terrain coordinates are within bounds before mapping
                        terrainCenterX = Mathf.Clamp(terrainCenterX, 0, rawWidth - 1);
                        terrainCenterY = Mathf.Clamp(terrainCenterY, 0, rawHeight - 1);
                        
                        // Map to alphamap coordinates
                        int alphamapX = Mathf.RoundToInt(terrainCenterX * vtexToAlphamapX);
                        int alphamapY = Mathf.RoundToInt(terrainCenterY * vtexToAlphamapY);

                        alphamapX = Mathf.Clamp(alphamapX, 0, alphamapWidth - 1);
                        alphamapY = Mathf.Clamp(alphamapY, 0, alphamapHeight - 1);

                        // Apply texture to single alphamap pixel (no X flip needed)
                        alphamaps[alphamapY, alphamapX, layerIndex] = 1.0f;
                    }
                }
                
            }

            // Normalize alphamaps
            for (int y = 0; y < alphamapHeight; y++)
            {
                for (int x = 0; x < alphamapWidth; x++)
                {
                    float sum = 0f;
                    for (int layer = 0; layer < terrainLayers.Count; layer++)
                    {
                        sum += alphamaps[y, x, layer];
                    }
                    if (sum > 0.01f)
                    {
                        for (int layer = 0; layer < terrainLayers.Count; layer++)
                        {
                            alphamaps[y, x, layer] /= sum;
                        }
                    }
                    else
                    {
                        if (terrainLayers.Count > 0)
                            alphamaps[y, x, 0] = 1.0f;
                    }
                }
            }

            // Step 8: Apply alphamaps to terrain
            terrainData.SetAlphamaps(0, 0, alphamaps);

            // Step 9: Set terrain dimensions (already extracted to 64-unit cells)
            // Heightmap was extracted from 65x65 per cell to 64x64 per cell (removing alignment rows/columns)
            // So originalWorldWidth/Height are already in 64-unit cells
            float actualWorldWidth = originalWorldWidth;
            float actualWorldHeight = originalWorldHeight;
            terrainData.size = new Vector3(actualWorldWidth, terrainHeight, actualWorldHeight);
            
            UnityEngine.Debug.Log($"Terrain size set to extracted 64-unit cell dimensions: {actualWorldWidth}x{actualWorldHeight} (from square {worldWidth}x{worldHeight})");

            // Step 10: Create Terrain GameObject
            GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            terrainObject.name = _esm + "_Terrain";
            Terrain terrain = terrainObject.GetComponent<Terrain>();
            terrain.heightmapPixelError = 5f;
            terrain.basemapDistance = 1000f;
            
            // Position terrain based on furthest negative cell coordinates
            // Terrain position is at its corner (bottom-left), so position at MinCellX * 64, MinCellY * 64
            // Game world uses 64 units per cell (not 65, which is only for heightmap generation)
            // Calculate Y offset from actual min height (converts Morrowind height units to Unity units)
            // This ensures the lowest terrain point maps correctly to the terrain
            float terrainYOffset = 0f;
            if (actualMinHeight != 0f)
            {
                // Convert min height from Morrowind units to Unity units
                // This offsets the terrain so the lowest point maps correctly
                terrainYOffset = actualMinHeight * TESGlobals.MORROWIND_TO_STATIC_SCALE;
                UnityEngine.Debug.Log($"Calculated terrain Y offset from min height: {terrainYOffset} " +
                    $"(Morrowind min={actualMinHeight}, converted with scale={TESGlobals.MORROWIND_TO_STATIC_SCALE})");
            }
            else
            {
                // Fallback if min height not available
                terrainYOffset = -16.4f;
                UnityEngine.Debug.LogWarning($"Using fallback terrain Y offset: {terrainYOffset} (min height not available)");
            }
            
            float terrainPositionX = Convert.ToInt32(MinCellX) * TESGlobals.CELL_SIZE;
            float terrainPositionZ = Convert.ToInt32(MinCellY) * TESGlobals.CELL_SIZE;
            
            terrainObject.transform.position = new Vector3(terrainPositionX, terrainYOffset, terrainPositionZ);
            
            UnityEngine.Debug.Log($"Terrain positioned: position=({terrainPositionX}, {terrainYOffset}, {terrainPositionZ}), " +
                $"terrain size: {actualWorldWidth}x{actualWorldHeight}, " +
                $"cell range: X[{MinCellX} to {MaxCellX}], Y[{MinCellY} to {MaxCellY}], " +
                $"terrain edges: X[{terrainPositionX} to {terrainPositionX + actualWorldWidth}], " +
                $"Z[{terrainPositionZ} to {terrainPositionZ + actualWorldHeight}], " +
                $"furthest negative cells: X={MinCellX} ({terrainPositionX}), Y={MinCellY} ({terrainPositionZ})");
            
            // Configure terrain material to reduce shininess/metallic (looks too wet/icy)
            // Try URP terrain shader first
            Shader terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (terrainShader == null)
            {
                terrainShader = Shader.Find("Nature/Terrain/Standard");
            }
            if (terrainShader == null)
            {
                terrainShader = Shader.Find("Terrain/Standard");
            }
            
            if (terrainShader != null)
            {
                Material terrainMaterial = new Material(terrainShader);
                
                // Set material properties to reduce shininess
                if (terrainMaterial.HasProperty("_Metallic"))
                {
                    terrainMaterial.SetFloat("_Metallic", 0.0f); // No metallic (terrain shouldn't be metallic)
                }
                if (terrainMaterial.HasProperty("_Smoothness"))
                {
                    terrainMaterial.SetFloat("_Smoothness", 0.1f); // Low smoothness (not shiny)
                }
                if (terrainMaterial.HasProperty("_Glossiness"))
                {
                    terrainMaterial.SetFloat("_Glossiness", 0.1f); // Low glossiness (alternative property name)
                }
                
                terrain.materialTemplate = terrainMaterial;
                UnityEngine.Debug.Log("Applied terrain material with reduced metallic/smoothness");
            }
            else
            {
                UnityEngine.Debug.LogWarning("Could not find suitable terrain shader, using default material");
            }
            
            // Force terrain to refresh and update
            #if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(terrainData);
            UnityEditor.EditorUtility.SetDirty(terrain);
            #endif

            UnityEngine.Debug.Log($"Created Unity terrain: {terrainObject.name} with {terrainLayers.Count} texture layers");
            UnityEngine.Debug.Log($"Terrain size: {terrainData.size}, Alphamap resolution: {terrainData.alphamapWidth}x{terrainData.alphamapHeight}");

            // Step 10: Create water plane
            GameObject waterPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            waterPlane.name = _esm + "_Water";
            
            // Position water plane at terrain center
            // Terrain position is at bottom-left corner, so center is position + half size
            float waterCenterX = terrainPositionX + (actualWorldWidth * 0.5f);
            float waterCenterZ = terrainPositionZ + (actualWorldHeight * 0.5f);
            waterPlane.transform.position = new Vector3(waterCenterX, waterY, waterCenterZ);
            waterPlane.transform.localScale = new Vector3(actualWorldWidth * 0.1f, 1f, actualWorldHeight * 0.1f);
            
            UnityEngine.Debug.Log($"Water plane positioned at ({waterCenterX}, {waterY}, {waterCenterZ}) to align with terrain center");
            
            Renderer waterRenderer = waterPlane.GetComponent<Renderer>();
            
            // Find water and foam textures from LTEX records
            var (waterTexture, waterNormalMap, foamTexture) = FindWaterTextures(_records, esm);
            
            // Try to load TESWater material asset first
            Material waterMaterial = null;
            Material tesWaterAsset = Resources.Load<Material>("TESWater");
            if (tesWaterAsset != null)
            {
                // Create instance of the material asset
                waterMaterial = new Material(tesWaterAsset);
                UnityEngine.Debug.Log("Loaded TESWater material from Resources");
            }
            else
            {
                // Try to find TESWater shader and create material
                Shader waterShader = Shader.Find("Shader Graphs/TESWater");
                if (waterShader == null)
                {
                    // Fallback to URP shaders if TESWater not found
                    waterShader = Shader.Find("Universal Render Pipeline/Lit");
                    if (waterShader == null)
                    {
                        waterShader = Shader.Find("Universal Render Pipeline/Simple Lit");
                    }
                    if (waterShader == null)
                    {
                        waterShader = Shader.Find("Universal Render Pipeline/Unlit");
                    }
                    if (waterShader == null)
                    {
                        waterShader = Shader.Find("Standard");
                    }
                }
                
                if (waterShader != null)
                {
                    waterMaterial = new Material(waterShader);
                    UnityEngine.Debug.Log($"Created water material using shader: {waterShader.name}");
                }
                else
                {
                    UnityEngine.Debug.LogError("Could not find TESWater shader or any fallback shader for water material");
                    return;
                }
            }
            
            // Set up TESWater material properties
            if (waterMaterial.shader.name.Contains("TESWater") || waterMaterial.shader.name.Contains("Shader Graphs/TESWater"))
            {
                // TESWater shader setup
                // Normal maps
                if (waterNormalMap != null && waterMaterial.HasProperty("_Normal"))
                {
                    waterMaterial.SetTexture("_Normal", waterNormalMap);
                }
                if (waterNormalMap != null && waterMaterial.HasProperty("_NormalB"))
                {
                    waterMaterial.SetTexture("_NormalB", waterNormalMap);
                }
                if (waterMaterial.HasProperty("_NormalStrength"))
                {
                    waterMaterial.SetFloat("_NormalStrength", 0.277f);
                }
                
                // Animation/Speed
                if (waterMaterial.HasProperty("_Speed"))
                {
                    waterMaterial.SetFloat("_Speed", 2.02f);
                }
                
                // Water colors
                if (waterMaterial.HasProperty("_ShallowWaterColor"))
                {
                    waterMaterial.SetColor("_ShallowWaterColor", new Color(0.14771953f, 0.45471424f, 0.48427665f, 0f));
                }
                if (waterMaterial.HasProperty("_DeepWaterColor"))
                {
                    waterMaterial.SetColor("_DeepWaterColor", new Color(0.070705205f, 0.2934088f, 0.408805f, 1f));
                }
                
                // Tiling
                if (waterMaterial.HasProperty("_Tiling"))
                {
                    waterMaterial.SetVector("_Tiling", new Vector4(3000f, 3000f, 0f, 0f));
                }
                if (waterMaterial.HasProperty("_TilingB"))
                {
                    waterMaterial.SetVector("_TilingB", new Vector4(1000f, 1000f, 0f, 0f));
                }
                
                // Depth and strength
                if (waterMaterial.HasProperty("_Depth"))
                {
                    waterMaterial.SetFloat("_Depth", 0.03f);
                }
                if (waterMaterial.HasProperty("_Strength"))
                {
                    waterMaterial.SetFloat("_Strength", 0.984f);
                }
                
                // Smoothness
                if (waterMaterial.HasProperty("_Smoothness"))
                {
                    waterMaterial.SetFloat("_Smoothness", 1.0f);
                }
                
                // Base color
                if (waterMaterial.HasProperty("_BaseColor"))
                {
                    waterMaterial.SetColor("_BaseColor", Color.white);
                }
                if (waterMaterial.HasProperty("_Color"))
                {
                    waterMaterial.SetColor("_Color", Color.white);
                }
                
                UnityEngine.Debug.Log($"Applied TESWater shader with water texture: {(waterTexture != null ? "Yes" : "No")}, normal map: {(waterNormalMap != null ? "Yes" : "No")}, foam: {(foamTexture != null ? "Yes" : "No")}");
            }
            else if (waterMaterial.shader.name.Contains("Universal Render Pipeline"))
            {
                // URP shader setup (fallback)
                waterMaterial.SetFloat("_Surface", 1); // Transparent surface
                waterMaterial.SetFloat("_Blend", 0); // Alpha blend
                waterMaterial.SetColor("_BaseColor", new Color(0.2f, 0.4f, 0.8f, 0.6f));
                waterMaterial.SetFloat("_Smoothness", 0.9f);
                waterMaterial.SetFloat("_Metallic", 0f);
                
                // Apply normal map if available
                if (waterNormalMap != null && waterMaterial.HasProperty("_BumpMap"))
                {
                    waterMaterial.SetTexture("_BumpMap", waterNormalMap);
                    waterMaterial.EnableKeyword("_NORMALMAP");
                }
                
                waterMaterial.renderQueue = 3000; // Transparent queue
            }
            else
            {
                // Standard shader setup (fallback)
                waterMaterial.SetFloat("_Mode", 3);
                waterMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                waterMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                waterMaterial.SetInt("_ZWrite", 0);
                waterMaterial.DisableKeyword("_ALPHATEST_ON");
                waterMaterial.EnableKeyword("_ALPHABLEND_ON");
                waterMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                waterMaterial.renderQueue = 3000;
                waterMaterial.color = new Color(0.2f, 0.4f, 0.8f, 0.6f);
                
                // Apply normal map if available
                if (waterNormalMap != null && waterMaterial.HasProperty("_BumpMap"))
                {
                    waterMaterial.SetTexture("_BumpMap", waterNormalMap);
                }
            }
            
            waterRenderer.material = waterMaterial;
        }

        public void GenerateUnityTerrainCells(Record[] _records, string esm = "Morrowind", float terrainHeight = 256f, float waterY = 0f)
        {
            if (_sharedTerrainLayers == null)
            {
                UnityEngine.Debug.LogError("Terrain layers not available. Call GenerateHeightMap_Cells first.");
                return;
            }
            
            _esm = System.IO.Path.GetFileNameWithoutExtension(esm);
            
            UnityEngine.Debug.Log("=== GenerateUnityTerrainCells: Creating per-cell terrain tiles ===");
            
            const int CELL = 65;
            
            // Get all cells with LAND records
            HashSet<(int x, int y)> landCellCoordinates = new HashSet<(int, int)>();
            foreach (Record rec in _records)
            {
                RecordLand landRecord = rec as RecordLand;
                if (landRecord != null && landRecord.subRecords != null)
                {
                    foreach (SubRecords subrec in landRecord.subRecords)
                    {
                        if (subrec is SubRecordLandINTV intv)
                        {
                            int landCellX = (int)intv.CellX;
                            int landCellY = (int)intv.CellY;
                            landCellCoordinates.Add((landCellX, landCellY));
                        }
                    }
                }
            }
            
            UnityEngine.Debug.Log($"Creating terrain tiles for {landCellCoordinates.Count} cells");
            
            int tilesCreated = 0;
            
            // Create terrain tile for each cell
            foreach (var (cellX, cellY) in landCellCoordinates)
            {
                // Load cell height data from RAW file (faster and less memory intensive)
                float minHeight, maxHeight;
                float[,] normalizedHeights = LoadCellRawFile(cellX, cellY, out minHeight, out maxHeight);
                
                // Heights are already normalized to 0-1 range from the RAW file
                
                // Create TerrainData for this cell (65x65 is a valid Unity resolution - power of 2 + 1)
                TerrainData terrainData = new TerrainData();
                terrainData.heightmapResolution = CELL; // 65 is valid (2^6 + 1)
                terrainData.size = new Vector3(TESGlobals.HEIGHTMAP_CELL_SIZE, terrainHeight, TESGlobals.HEIGHTMAP_CELL_SIZE); // 65 units per cell
                terrainData.SetHeights(0, 0, normalizedHeights);
                
                // COMMENTED OUT: Texturing and splat maps to reduce memory usage
                // Set terrain layers
                // terrainData.terrainLayers = _sharedTerrainLayers.ToArray();
                
                // Create terrain GameObject
                GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
                terrainObject.name = $"Cell_Terrain_{cellX}_{cellY}";
                
                // Position terrain at cell's world position
                float worldX = cellX * TESGlobals.HEIGHTMAP_CELL_SIZE;
                float worldZ = cellY * TESGlobals.HEIGHTMAP_CELL_SIZE;
                terrainObject.transform.position = new Vector3(worldX, 0f, worldZ);
                
                // Enable autoconnect to stitch with neighboring terrain tiles
                Terrain terrain = terrainObject.GetComponent<Terrain>();
                terrain.allowAutoConnect = true;
                terrain.heightmapPixelError = 5f;
                terrain.basemapDistance = 1000f;
                
                // COMMENTED OUT: Create alphamap for this cell from VTEX data (reduces memory usage)
                // if (_cellVTEXData != null && _textureIndexToLayerIndex != null)
                // {
                //     CreateCellAlphamap(terrainData, cellX, cellY);
                // }
                
                tilesCreated++;
            }
            
            UnityEngine.Debug.Log($"=== GenerateUnityTerrainCells: Complete - Created {tilesCreated} terrain tiles ===");
        }
        
        /// <summary>
        /// Creates alphamap (splat map) for a single cell from VTEX data
        /// </summary>
        private void CreateCellAlphamap(TerrainData terrainData, int cellGridX, int cellGridY)
        {
            if (!_cellVTEXData.TryGetValue((cellGridX, cellGridY), out ushort[][] vtexIndices))
            {
                // No VTEX data for this cell, use default layer
                return;
            }
            
            const int VTEX_SIZE = 16;
            const int CELL = 65;
            int alphamapWidth = terrainData.alphamapWidth;
            int alphamapHeight = terrainData.alphamapHeight;
            int layerCount = terrainData.terrainLayers.Length;
            
            float[,,] alphamaps = new float[alphamapHeight, alphamapWidth, layerCount];
            
            // Initialize all to 0
            for (int y = 0; y < alphamapHeight; y++)
            {
                for (int x = 0; x < alphamapWidth; x++)
                {
                    for (int layer = 0; layer < layerCount; layer++)
                    {
                        alphamaps[y, x, layer] = 0f;
                    }
                }
            }
            
            // Map VTEX data to alphamap
            // VTEX is 16x16, cell is 65x65, so each VTEX entry covers ~4 pixels
            float vtexToAlphamapX = (float)alphamapWidth / CELL;
            float vtexToAlphamapY = (float)alphamapHeight / CELL;
            
            for (int vy = 0; vy < VTEX_SIZE; vy++)
            {
                if (vtexIndices[vy] == null) continue;
                
                for (int vx = 0; vx < VTEX_SIZE; vx++)
                {
                    ushort vtexIndex = vtexIndices[vy][vx];
                    
                    if (vtexIndex == 0 || !_textureIndexToLayerIndex.TryGetValue(vtexIndex, out int layerIndex))
                        continue;
                    
                    // Map VTEX coordinate to alphamap coordinate
                    // Each VTEX entry covers CELL/VTEX_SIZE pixels
                    float terrainX = (vx + 0.5f) * (CELL / (float)VTEX_SIZE);
                    float terrainY = (vy + 0.5f) * (CELL / (float)VTEX_SIZE);
                    
                    int alphamapX = Mathf.RoundToInt(terrainX * vtexToAlphamapX);
                    int alphamapY = Mathf.RoundToInt(terrainY * vtexToAlphamapY);
                    
                    alphamapX = Mathf.Clamp(alphamapX, 0, alphamapWidth - 1);
                    alphamapY = Mathf.Clamp(alphamapY, 0, alphamapHeight - 1);
                    
                    // Apply texture to alphamap pixel
                    alphamaps[alphamapY, alphamapX, layerIndex] = 1.0f;
                }
            }
            
            // Normalize alphamaps
            for (int y = 0; y < alphamapHeight; y++)
            {
                for (int x = 0; x < alphamapWidth; x++)
                {
                    float sum = 0f;
                    for (int layer = 0; layer < layerCount; layer++)
                    {
                        sum += alphamaps[y, x, layer];
                    }
                    if (sum > 0.01f)
                    {
                        for (int layer = 0; layer < layerCount; layer++)
                        {
                            alphamaps[y, x, layer] /= sum;
                        }
                    }
                    else
                    {
                        // Default to first layer if no texture assigned
                        if (layerCount > 0)
                        {
                            alphamaps[y, x, 0] = 1.0f;
                        }
                    }
                }
            }
            
            terrainData.SetAlphamaps(0, 0, alphamaps);
        }

        public void GenerateHeightMap_Cells(Record[] _records, string esm = "Morrowind")
        {
            _esm = System.IO.Path.GetFileNameWithoutExtension(esm);
            
            UnityEngine.Debug.Log("=== GenerateHeightMap_Cells: Generating heightmap for per-cell terrain ===");
            
            // Use the same algorithm as GenerateHeightMap_MergedLands
            // Calculate dimensions
            int width = (Math.Abs(Convert.ToInt32(MinCellX)) + Convert.ToInt32(MaxCellX)) * 65;
            int height = (Math.Abs(Convert.ToInt32(MinCellY)) + Convert.ToInt32(MaxCellY)) * 65;
            
            UnityEngine.Debug.Log($"Heightmap dimensions: {width}x{height}");
            
            // Create global height array
            _globalHeights = new float[height][];
            for (int i = 0; i < height; i++)
            {
                _globalHeights[i] = new float[width];
            }
            
            const int CELL = 65;
            // Use global constant from TESGlobals
            
            // Collect cell data (offset and deltas)
            Dictionary<(int x, int y), (float offset, sbyte[][] deltas)> cellData = 
                new Dictionary<(int, int), (float, sbyte[][])>();
            
            foreach (Record recs in _records)
            {
                RecordLand landRecord = recs as RecordLand;
                if (landRecord != null)
                {
                    SubRecordLandINTV intv = null;
                    float offset = 0f;
                    sbyte[][] deltas = null;
                    
                    if (landRecord.subRecords != null)
                    {
                        foreach (SubRecords subrecs in landRecord.subRecords)
                        {
                            if (subrecs == null || subrecs.type == null) continue;
                            
                            if (subrecs.type == "INTV")
                            {
                                intv = subrecs as SubRecordLandINTV;
                            }
                            if (subrecs.type == "VHGT")
                            {
                                SubRecordLandVHGT subrecordLandVHGT = subrecs as SubRecordLandVHGT;
                                if (subrecordLandVHGT != null)
                                {
                                    offset = subrecordLandVHGT.offset;
                                    deltas = subrecordLandVHGT.heightdata;
                                }
                            }
                        }
                    }
                    
                    if (intv != null && deltas != null)
                    {
                        try
                        {
                            int cellX = Convert.ToInt32(intv.CellX);
                            int cellY = Convert.ToInt32(intv.CellY);
                            cellData[(cellX, cellY)] = (offset, deltas);
                        }
                        catch (System.Exception ex)
                        {
                            UnityEngine.Debug.LogWarning($"Failed to process cell data: {ex.Message}");
                        }
                    }
                }
            }
            
            UnityEngine.Debug.Log($"Found {cellData.Count} land cells");
            
            // Dictionary to store processed cell heights for neighbor lookups
            Dictionary<(int x, int y), float[][]> cellHeights = new Dictionary<(int, int), float[][]>();
            
            // Process cells in sorted order (bottom-left to top-right)
            var sortedCells = cellData.Keys.OrderBy(c => c.y).ThenBy(c => c.x).ToList();
            
            // Process each cell using the merged lands algorithm
            foreach (var (cellX, cellY) in sortedCells)
            {
                var (offset, deltas) = cellData[(cellX, cellY)];
                
                int startX = (cellX - Convert.ToInt32(MinCellX)) * CELL;
                int startY = (cellY - Convert.ToInt32(MinCellY)) * CELL;
                
                int rowOffset = (int)offset;
                float[][] cellHeightsArray = new float[CELL][];
                for (int i = 0; i < CELL; i++)
                {
                    cellHeightsArray[i] = new float[CELL];
                }
                
                int[] rowFirstColumnUnscaled = new int[CELL];
                
                for (int y = 0; y < CELL; y++)
                {
                    if (y == 0)
                    {
                        if (cellY > Convert.ToInt32(MinCellY) && cellHeights.ContainsKey((cellX, cellY - 1)))
                        {
                            float[][] bottomCell = cellHeights[(cellX, cellY - 1)];
                            float neighborHeight = bottomCell[CELL - 1][0];
                            rowOffset = (int)Math.Round(neighborHeight / TESGlobals.HEIGHT_MAP_SCALE_FACTOR);
                        }
                        else if (cellX > Convert.ToInt32(MinCellX) && cellHeights.ContainsKey((cellX - 1, cellY)))
                        {
                            float[][] leftCell = cellHeights[(cellX - 1, cellY)];
                            float neighborHeight = leftCell[0][63];
                            rowOffset = (int)Math.Round(neighborHeight / TESGlobals.HEIGHT_MAP_SCALE_FACTOR);
                        }
                        else
                        {
                            rowOffset = (int)offset;
                        }
                    }
                    else
                    {
                        if (cellX > Convert.ToInt32(MinCellX) && cellHeights.ContainsKey((cellX - 1, cellY)))
                        {
                            float[][] leftCell = cellHeights[(cellX - 1, cellY)];
                            float neighborHeight = leftCell[y][63];
                            rowOffset = (int)Math.Round(neighborHeight / TESGlobals.HEIGHT_MAP_SCALE_FACTOR);
                        }
                        else
                        {
                            rowOffset = rowFirstColumnUnscaled[y - 1];
                        }
                    }
                    
                    rowOffset += deltas[y][0];
                    rowFirstColumnUnscaled[y] = rowOffset;
                    cellHeightsArray[y][0] = rowOffset * TESGlobals.HEIGHT_MAP_SCALE_FACTOR;
                    
                    int colOffset = rowOffset;
                    for (int x = 1; x < CELL; x++)
                    {
                        colOffset += deltas[y][x];
                        cellHeightsArray[y][x] = colOffset * TESGlobals.HEIGHT_MAP_SCALE_FACTOR;
                    }
                }
                
                cellHeights[(cellX, cellY)] = cellHeightsArray;
                
                // Write to global array (still needed for PNG export)
                for (int ly = 0; ly < CELL; ly++)
                {
                    int py = startY + ly;
                    if (py < 0 || py >= height) continue;
                    
                    for (int lx = 0; lx < CELL; lx++)
                    {
                        int px = startX + lx;
                        if (px < 0 || px >= width) continue;
                        
                        _globalHeights[py][px] = cellHeightsArray[ly][lx];
                    }
                }
            }
            
            // Find global min/max from the final merged heightmap for proper normalization
            float globalMinHeight = float.MaxValue;
            float globalMaxHeight = float.MinValue;
            bool foundValidHeight = false;
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float h = _globalHeights[y][x];
                    if (h != 0f || foundValidHeight)
                    {
                        if (!foundValidHeight)
                        {
                            globalMinHeight = h;
                            globalMaxHeight = h;
                            foundValidHeight = true;
                        }
                        else
                        {
                            if (h < globalMinHeight) globalMinHeight = h;
                            if (h > globalMaxHeight) globalMaxHeight = h;
                        }
                    }
                }
            }
            
            float globalHeightRange = globalMaxHeight - globalMinHeight;
            if (globalHeightRange < 0.001f) globalHeightRange = 1f;
            
            UnityEngine.Debug.Log($"Global height range for cell sampling: min={globalMinHeight}, max={globalMaxHeight}, range={globalHeightRange}");
            
            // After the complete heightmap is built, sample each cell from the final merged heightmap
            // This ensures we get the perfect accumulated heights from the merged lands algorithm
            UnityEngine.Debug.Log("Sampling cells from final merged heightmap...");
            HashSet<(int x, int y)> cellsToSample = new HashSet<(int, int)>();
            foreach (var (cellX, cellY) in cellData.Keys)
            {
                cellsToSample.Add((cellX, cellY));
            }
            
            foreach (var (cellX, cellY) in cellsToSample)
            {
                // Sample 65x65 chunk from the final merged heightmap
                int startX = (cellX - Convert.ToInt32(MinCellX)) * CELL;
                int startY = (cellY - Convert.ToInt32(MinCellY)) * CELL;
                
                float[][] sampledCellHeights = new float[CELL][];
                for (int y = 0; y < CELL; y++)
                {
                    sampledCellHeights[y] = new float[CELL];
                    int py = startY + y;
                    if (py >= 0 && py < height)
                    {
                        for (int x = 0; x < CELL; x++)
                        {
                            int px = startX + x;
                            if (px >= 0 && px < width)
                            {
                                sampledCellHeights[y][x] = _globalHeights[py][px];
                            }
                            else
                            {
                                sampledCellHeights[y][x] = 0f;
                            }
                        }
                    }
                    else
                    {
                        for (int x = 0; x < CELL; x++)
                        {
                            sampledCellHeights[y][x] = 0f;
                        }
                    }
                }
                
                // Save the sampled cell data as RAW file using global min/max for normalization
                SaveCellRawFile(cellX, cellY, sampledCellHeights, globalMinHeight, globalMaxHeight);
            }
            
            UnityEngine.Debug.Log($"Sampled and saved {cellsToSample.Count} cell RAW files from final merged heightmap");
            
            // Collect VTEX data
            _cellVTEXData = new Dictionary<(int, int), ushort[][]>();
            HashSet<ushort> uniqueTextureIndices = new HashSet<ushort>();
            
            foreach (Record rec in _records)
            {
                RecordLand landRecord = rec as RecordLand;
                if (landRecord != null)
                {
                    int cellx = 0;
                    int celly = 0;
                    ushort[][] vtexData = null;
                    
                    foreach (SubRecords subrec in landRecord.subRecords)
                    {
                        SubRecordLandINTV intvSubrec = subrec as SubRecordLandINTV;
                        if (intvSubrec != null)
                        {
                            cellx = Convert.ToInt32(intvSubrec.CellX);
                            celly = Convert.ToInt32(intvSubrec.CellY);
                        }
                        
                        SubRecordLandVTEX vtexSubrec = subrec as SubRecordLandVTEX;
                        if (vtexSubrec != null)
                        {
                            vtexData = vtexSubrec.indices;
                        }
                    }
                    
                    if (vtexData != null)
                    {
                        _cellVTEXData[(cellx, celly)] = vtexData;
                        for (int y = 0; y < 16; y++)
                        {
                            if (vtexData[y] != null)
                            {
                                for (int x = 0; x < 16; x++)
                                {
                                    uniqueTextureIndices.Add(vtexData[y][x]);
                                }
                            }
                        }
                    }
                }
            }
            
            // Build texture index to filename mapping
            Dictionary<int, (string primary, string fallback)> textureIndexToNames = new Dictionary<int, (string, string)>();
            foreach (Record rec in _records)
            {
                RecordLTex ltexRecord = rec as RecordLTex;
                if (ltexRecord != null)
                {
                    int textureIndex = -1;
                    string filename = null;
                    string name = null;
                    
                    foreach (SubRecords subrec in ltexRecord.subRecords)
                    {
                        SubRecordLTexINTV intvSubrec = subrec as SubRecordLTexINTV;
                        if (intvSubrec != null)
                        {
                            textureIndex = intvSubrec.index;
                        }
                        
                        SubRecordLTexData dataSubrec = subrec as SubRecordLTexData;
                        if (dataSubrec != null)
                        {
                            filename = dataSubrec.filename;
                        }
                        
                        SubRecordLTexNAME nameSubrec = subrec as SubRecordLTexNAME;
                        if (nameSubrec != null)
                        {
                            name = nameSubrec.name;
                            if (!string.IsNullOrEmpty(name))
                            {
                                name = name.TrimEnd('\0', ' ', '\t', '\r', '\n');
                                name = name.Replace("\0", "");
                                name = name.Trim();
                            }
                        }
                    }
                    
                    if (textureIndex >= 0)
                    {
                        string primaryName = null;
                        string fallbackName = null;
                        
                        if (!string.IsNullOrEmpty(name))
                        {
                            primaryName = name;
                        }
                        
                        if (!string.IsNullOrEmpty(filename))
                        {
                            fallbackName = filename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                            fallbackName = fallbackName.Replace("\0", "");
                            fallbackName = fallbackName.Trim();
                        }
                        
                        if (!string.IsNullOrEmpty(primaryName) || !string.IsNullOrEmpty(fallbackName))
                        {
                            textureIndexToNames[textureIndex] = (primaryName ?? fallbackName, fallbackName);
                        }
                    }
                }
            }
            
            // Create terrain layers (reuse logic from GenerateUnityTerrain)
            _sharedTerrainLayers = new List<TerrainLayer>();
            _textureIndexToLayerIndex = new Dictionary<ushort, int>();
            
            Texture2D CreatePlaceholderTexture(string name, Color color)
            {
                Texture2D placeholder = new Texture2D(64, 64, TextureFormat.RGB24, false);
                Color[] pixels = new Color[64 * 64];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = color;
                }
                placeholder.SetPixels(pixels);
                placeholder.Apply();
                placeholder.name = name;
                return placeholder;
            }
            
            // Create mask map texture to prevent shininess
            // Mask map channels: R=Metallic, G=Ambient Occlusion, B=Height, A=Smoothness
            // To remove shininess: R=0 (non-metallic), A=0 (rough/non-shiny)
            Texture2D CreateBlackMaskTexture()
            {
                Texture2D maskTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                Color32[] pixels = new Color32[64 * 64];
                for (int i = 0; i < pixels.Length; i++)
                {
                    // R=0 (non-metallic), G=255 (full AO), B=0 (no height), A=0 (rough/non-shiny)
                    pixels[i] = new Color32(0, 255, 0, 0);
                }
                maskTexture.SetPixels32(pixels);
                maskTexture.Apply();
                maskTexture.name = "NonShinyMask";
                return maskTexture;
            }
            
            Texture2D placeholderTexture = CreatePlaceholderTexture("Placeholder", new Color(0.5f, 0.5f, 0.5f, 1f));
            Texture2D blackMaskTexture = CreateBlackMaskTexture(); // Mask texture to prevent shininess
            
            List<ushort> sortedIndices = uniqueTextureIndices.OrderBy(x => x).ToList();
            
            foreach (ushort vtexIndex in sortedIndices)
            {
                if (vtexIndex == 0)
                    continue;
                
                int ltexIndex = vtexIndex - 1;
                
                if (textureIndexToNames.TryGetValue(ltexIndex, out var names))
                {
                    UnityEngine.Debug.Log($"GenerateUnityTerrain: Processing VTEX {vtexIndex} (LTEX {ltexIndex}) - primary: '{names.primary ?? "null"}', fallback: '{names.fallback ?? "null"}'");
                    // Build path and normalize immediately to ensure consistent separators
                    string textureDir = System.IO.Path.GetFullPath(
                        System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Textures", _esm)
                    );
                    
                    List<string> namesToTry = new List<string>();
                    if (!string.IsNullOrEmpty(names.primary))
                    {
                        namesToTry.Add(names.primary);
                        namesToTry.Add("Tx_" + names.primary);
                        namesToTry.Add("tx_" + names.primary.ToLower());
                    }
                    if (!string.IsNullOrEmpty(names.fallback))
                    {
                        namesToTry.Add(names.fallback);
                        string fallbackBase = System.IO.Path.GetFileNameWithoutExtension(names.fallback);
                        namesToTry.Add(fallbackBase);
                    }
                    namesToTry = namesToTry.Distinct().ToList();
                    
                    string texturePath = null;
                    string foundBaseName = null;
                    
                    if (Directory.Exists(textureDir))
                    {
                        string[] files = Directory.GetFiles(textureDir);
                        
                        foreach (string nameToTry in namesToTry)
                        {
                            string baseNameNoExt = System.IO.Path.GetFileNameWithoutExtension(nameToTry);
                            string[] extensions = new[] { ".png", ".dds", ".tga" };
                            string originalExt = System.IO.Path.GetExtension(nameToTry);
                            if (!string.IsNullOrEmpty(originalExt) && !extensions.Contains(originalExt.ToLower()))
                            {
                                extensions = new[] { originalExt.ToLower() }.Concat(extensions).ToArray();
                            }
                            
                            foreach (string ext in extensions)
                            {
                                string searchFilename = baseNameNoExt + ext;
                                foreach (string file in files)
                                {
                                    string fileName = System.IO.Path.GetFileName(file);
                                    if (fileName.Equals(searchFilename, StringComparison.OrdinalIgnoreCase))
                                    {
                                        texturePath = file;
                                        foundBaseName = fileName;
                                        break;
                                    }
                                }
                                if (texturePath != null) break;
                            }
                            if (texturePath != null) break;
                        }
                    }
                    
                    Texture2D texture = null;
                    bool usePlaceholder = false;
                    
                    // Check global texture library first by VTEX index (most reliable)
                    TESLTextureLibrary.TextureEntry cachedEntry = TESLTextureLibrary.GetTextureByVTEXIndex(vtexIndex);
                    if (cachedEntry != null)
                    {
                        texture = cachedEntry.Texture;
                        UnityEngine.Debug.Log($"Reusing cached texture from library (by VTEX {vtexIndex}): {cachedEntry.Filename}");
                    }
                    
                    // Also check by path if we have one
                    if (texture == null && texturePath != null)
                    {
                        cachedEntry = TESLTextureLibrary.GetTextureByPath(texturePath);
                        if (cachedEntry != null)
                        {
                            texture = cachedEntry.Texture;
                            UnityEngine.Debug.Log($"Reusing cached texture from library (by path): {System.IO.Path.GetFileName(texturePath)}");
                        }
                    }
                    
                    if (texture == null && texturePath != null && File.Exists(texturePath))
                    {
                        try
                        {
                            string textureExtension = System.IO.Path.GetExtension(texturePath).ToLower();
                            
                            if (textureExtension == ".dds")
                            {
                                string pngPath = System.IO.Path.ChangeExtension(texturePath, ".png");
                                if (!System.IO.File.Exists(pngPath))
                                {
                                    byte[] ddsData = System.IO.File.ReadAllBytes(texturePath);
                                    if (ConvertDDSToPNG(ddsData, pngPath))
                                    {
                                        #if UNITY_EDITOR
                                        SetTextureImportSettings(pngPath);
                                        #endif
                                        texturePath = pngPath;
                                        textureExtension = ".png";
                                        foundBaseName = System.IO.Path.GetFileName(pngPath);
                                        
                                        // Check library again with PNG path after conversion
                                        if (texture == null)
                                        {
                                            TESLTextureLibrary.TextureEntry convertedEntry = TESLTextureLibrary.GetTextureByPath(pngPath);
                                            if (convertedEntry != null)
                                            {
                                                texture = convertedEntry.Texture;
                                                UnityEngine.Debug.Log($"Reusing cached texture from library (after DDS->PNG conversion): {System.IO.Path.GetFileName(pngPath)}");
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    texturePath = pngPath;
                                    textureExtension = ".png";
                                    foundBaseName = System.IO.Path.GetFileName(pngPath);
                                    
                                    // Check library again with PNG path
                                    if (texture == null)
                                    {
                                        TESLTextureLibrary.TextureEntry cachedEntry2 = TESLTextureLibrary.GetTextureByPath(pngPath);
                                        if (cachedEntry2 != null)
                                        {
                                            texture = cachedEntry2.Texture;
                                            UnityEngine.Debug.Log($"Reusing cached texture from library (PNG path): {System.IO.Path.GetFileName(pngPath)}");
                                        }
                                    }
                                }
                            }
                            
                            if (texture == null && textureExtension == ".tga")
                            {
                                string pngPath = System.IO.Path.ChangeExtension(texturePath, ".png");
                                if (!System.IO.File.Exists(pngPath))
                                {
                                    // Convert TGA to PNG (32-bit RGBA, alpha from grayscale)
                                    byte[] tgaData = System.IO.File.ReadAllBytes(texturePath);
                                    Texture2D tempTexture = new Texture2D(2, 2);
                                    if (tempTexture.LoadImage(tgaData))
                                    {
                                        // Keep as RGBA32 (32-bit with alpha) - alpha will be set to "From grayscale" in import settings
                                        Texture2D rgbaTexture = new Texture2D(tempTexture.width, tempTexture.height, TextureFormat.RGBA32, false);
                                        Color32[] pixels = tempTexture.GetPixels32();
                                        // Ensure all alpha values are 255 (opaque) - Unity will use grayscale for alpha via import settings
                                        for (int i = 0; i < pixels.Length; i++)
                                        {
                                            pixels[i].a = 255;
                                        }
                                        rgbaTexture.SetPixels32(pixels);
                                        rgbaTexture.Apply();
                                        
                                        byte[] pngData = rgbaTexture.EncodeToPNG();
                                        System.IO.File.WriteAllBytes(pngPath, pngData);
                                        #if UNITY_EDITOR
                                        SetTextureImportSettings(pngPath);
                                        #endif
                                        
                                        UnityEngine.Object.DestroyImmediate(tempTexture);
                                        UnityEngine.Object.DestroyImmediate(rgbaTexture);
                                        
                                        texturePath = pngPath;
                                        textureExtension = ".png";
                                        foundBaseName = System.IO.Path.GetFileName(pngPath);
                                        
                                        // Check library again with PNG path after conversion
                                        if (texture == null)
                                        {
                                            TESLTextureLibrary.TextureEntry convertedEntry = TESLTextureLibrary.GetTextureByPath(pngPath);
                                            if (convertedEntry != null)
                                            {
                                                texture = convertedEntry.Texture;
                                                UnityEngine.Debug.Log($"Reusing cached texture from library (after TGA->PNG conversion): {System.IO.Path.GetFileName(pngPath)}");
                                            }
                                        }
                                        
                                        UnityEngine.Object.DestroyImmediate(tempTexture);
                                    }
                                    else
                                    {
                                        UnityEngine.Object.DestroyImmediate(tempTexture);
                                    }
                                }
                                else
                                {
                                    texturePath = pngPath;
                                    textureExtension = ".png";
                                    foundBaseName = System.IO.Path.GetFileName(pngPath);
                                    
                                    // Check library again with PNG path
                                    if (texture == null)
                                    {
                                        TESLTextureLibrary.TextureEntry cachedEntry3 = TESLTextureLibrary.GetTextureByPath(pngPath);
                                        if (cachedEntry3 != null)
                                        {
                                            texture = cachedEntry3.Texture;
                                            UnityEngine.Debug.Log($"Reusing cached texture from library (PNG path): {System.IO.Path.GetFileName(pngPath)}");
                                        }
                                    }
                                }
                            }
                            
                            if (texture == null && textureExtension == ".png")
                            {
                                // Final check with global texture library
                                TESLTextureLibrary.TextureEntry cachedEntry4 = TESLTextureLibrary.GetTextureByPath(texturePath);
                                if (cachedEntry4 != null)
                                {
                                    texture = cachedEntry4.Texture;
                                }
                                else
                                {
                                    // Load texture and add to global library
                                    byte[] textureData = System.IO.File.ReadAllBytes(texturePath);
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
                                        
                                        // Set import settings to disable alpha source (prevents shininess)
                                        #if UNITY_EDITOR
                                        SetTextureImportSettings(texturePath);
                                        #endif
                                        
                                        // Add to global texture library
                                        string textureName = names.primary ?? names.fallback ?? foundBaseName ?? System.IO.Path.GetFileNameWithoutExtension(texturePath);
                                        TESLTextureLibrary.TextureEntry addedEntry = TESLTextureLibrary.AddTexture(textureName, ltexIndex, vtexIndex, texturePath, texture);
                                        // Store entry for normal map generation
                                        if (addedEntry != null)
                                        {
                                            cachedEntry = addedEntry;
                                        }
                                    }
                                    else
                                    {
                                        usePlaceholder = true;
                                    }
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            UnityEngine.Debug.LogWarning($"Error loading texture {texturePath}: {ex.Message}, using placeholder");
                            usePlaceholder = true;
                        }
                    }
                    else if (texture == null)
                    {
                        usePlaceholder = true;
                    }
                    
                    if (usePlaceholder || texture == null)
                    {
                        string placeholderName = foundBaseName ?? names.primary ?? names.fallback ?? $"Index_{ltexIndex}";
                        texture = CreatePlaceholderTexture($"Placeholder_{placeholderName}", new Color(0.7f, 0.5f, 0.3f, 1f));
                    }
                    
                    TerrainLayer layer = new TerrainLayer();
                    layer.diffuseTexture = texture;
                    layer.maskMapTexture = blackMaskTexture; // Black mask map to prevent shininess
                    layer.tileSize = new Vector2(8, 8);
                    layer.tileOffset = Vector2.zero;
                    layer.metallic = 0.0f;
                    layer.smoothness = 0.1f;
                    
                    // Get or generate normal map for this texture
                    // Find the texture entry in the library
                    TESLTextureLibrary.TextureEntry textureEntry = null;
                    if (texturePath != null)
                    {
                        textureEntry = TESLTextureLibrary.GetTextureByPath(texturePath);
                    }
                    if (textureEntry == null && vtexIndex > 0)
                    {
                        textureEntry = TESLTextureLibrary.GetTextureByVTEXIndex(vtexIndex);
                    }
                    if (textureEntry != null)
                    {
                        Texture2D normalMap = TESLTextureLibrary.GetOrGenerateNormalMap(textureEntry);
                        if (normalMap != null)
                        {
                            layer.normalMapTexture = normalMap;
                        }
                    }
                    
                    _sharedTerrainLayers.Add(layer);
                    int assignedLayerIndex = _sharedTerrainLayers.Count - 1;
                    _textureIndexToLayerIndex[vtexIndex] = assignedLayerIndex;
                }
            }
            
            if (_sharedTerrainLayers.Count == 0)
            {
                UnityEngine.Debug.LogWarning("No terrain layers found, creating default placeholder layer.");
                TerrainLayer defaultLayer = new TerrainLayer();
                defaultLayer.diffuseTexture = placeholderTexture;
                defaultLayer.maskMapTexture = blackMaskTexture; // Black mask map to prevent shininess
                defaultLayer.tileSize = new Vector2(8, 8);
                defaultLayer.tileOffset = Vector2.zero;
                defaultLayer.metallic = 0.0f;
                defaultLayer.smoothness = 0.1f;
                _sharedTerrainLayers.Add(defaultLayer);
            }
            
            UnityEngine.Debug.Log($"=== GenerateHeightMap_Cells: Complete - {cellData.Count} cells, {_sharedTerrainLayers.Count} terrain layers ===");
        }
        
        /// <summary>
        /// Saves a single cell's height data as a RAW file for faster per-cell terrain generation
        /// Uses global min/max for normalization to maintain relative heights between cells
        /// </summary>
        private void SaveCellRawFile(int cellX, int cellY, float[][] cellHeights, float globalMinHeight, float globalMaxHeight)
        {
            const int CELL = 65;
            
            // Use global min/max for normalization to maintain relative heights between cells
            float globalHeightRange = globalMaxHeight - globalMinHeight;
            if (globalHeightRange < 0.001f) globalHeightRange = 1f;
            
            // Create RAW file (16-bit little-endian)
            byte[] rawBytes = new byte[CELL * CELL * 2]; // 2 bytes per pixel (16-bit)
            int byteIndex = 0;
            
            for (int y = 0; y < CELL; y++)
            {
                for (int x = 0; x < CELL; x++)
                {
                    // Normalize height to 0-1 range using global min/max
                    float normalizedHeight = (cellHeights[y][x] - globalMinHeight) / globalHeightRange;
                    normalizedHeight = Mathf.Clamp01(normalizedHeight);
                    
                    // Convert to 16-bit (0-65535)
                    ushort heightValue = (ushort)(normalizedHeight * 65535.0f);
                    
                    // Write as little-endian (LSB first, then MSB)
                    rawBytes[byteIndex++] = (byte)(heightValue & 0xFF);        // LSB
                    rawBytes[byteIndex++] = (byte)((heightValue >> 8) & 0xFF); // MSB
                }
            }
            
            // Save to Cache/Cells directory
            string cellsDir = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Cells", _esm);
            Directory.CreateDirectory(cellsDir);
            string rawFilePath = System.IO.Path.Combine(cellsDir, $"Cell_{cellX}_{cellY}.raw");
            System.IO.File.WriteAllBytes(rawFilePath, rawBytes);
        }
        
        /// <summary>
        /// Loads a single cell's height data from RAW file
        /// </summary>
        private float[,] LoadCellRawFile(int cellX, int cellY, out float minHeight, out float maxHeight)
        {
            const int CELL = 65;
            minHeight = 0f;
            maxHeight = 1f;
            
            string cellsDir = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Cells", _esm);
            string rawFilePath = System.IO.Path.Combine(cellsDir, $"Cell_{cellX}_{cellY}.raw");
            
            if (!File.Exists(rawFilePath))
            {
                UnityEngine.Debug.LogWarning($"Cell RAW file not found: {rawFilePath}");
                return new float[CELL, CELL]; // Return empty array
            }
            
            byte[] rawData = File.ReadAllBytes(rawFilePath);
            float[,] cellHeights = new float[CELL, CELL];
            
            // Read RAW data (16-bit little-endian)
            int byteIndex = 0;
            for (int y = 0; y < CELL; y++)
            {
                for (int x = 0; x < CELL; x++)
                {
                    if (byteIndex + 1 < rawData.Length)
                    {
                        ushort heightValue = (ushort)(rawData[byteIndex] | (rawData[byteIndex + 1] << 8));
                        cellHeights[y, x] = heightValue / 65535.0f; // Normalize to 0-1
                        
                        if (cellHeights[y, x] < minHeight) minHeight = cellHeights[y, x];
                        if (cellHeights[y, x] > maxHeight) maxHeight = cellHeights[y, x];
                    }
                    byteIndex += 2;
                }
            }
            
            return cellHeights;
        }

        /// <summary>
        /// TEST FUNCTION: Generate heightmap using merged_lands algorithm exactly
        /// This resets height at the start of each row to the first column value, preventing scanlines
        /// 
        /// NOTE: This function uses records from ALL ESM files to create one unified terrain.
        /// ESM load order is important - later ESM files can override LAND/CELL records from earlier ones.
        /// See TESESMLibrary.ScanAndLoadESMs() for details on ESM stacking and load order.
        /// </summary>
        public void GenerateHeightMap_MergedLands(Record[] _records, string esm = "Morrowind")
        {
            _esm = System.IO.Path.GetFileNameWithoutExtension(esm);
            
            UnityEngine.Debug.Log("=== GenerateHeightMap_MergedLands: Starting test heightmap generation ===");
            
            // Calculate dimensions (matching old GenerateHeightMap calculation)
            // Note: This uses (Abs(Min) + Max) which gives one less cell than (Max - Min + 1)
            // but matches the cell positioning calculation below
            int width = (Math.Abs(Convert.ToInt32(MinCellX)) + Convert.ToInt32(MaxCellX)) * 65;
            int height = (Math.Abs(Convert.ToInt32(MinCellY)) + Convert.ToInt32(MaxCellY)) * 65;
            
            UnityEngine.Debug.Log($"Heightmap dimensions: {width}x{height}");
            
            // Create global height array (no count array needed - direct write)
            float[][] globalHeights = new float[height][];
            bool[][] globalHeightsValid = new bool[height][]; // Track which pixels are from valid cells
            for (int i = 0; i < height; i++)
            {
                globalHeights[i] = new float[width];
                globalHeightsValid[i] = new bool[width];
            }
            
            const int CELL = 65;
            // Use global constant from TESGlobals
            
            int cx = width / 2;
            int cy = height / 2;
            
            // Collect cell data (offset and deltas)
            Dictionary<(int x, int y), (float offset, sbyte[][] deltas)> cellData = 
                new Dictionary<(int, int), (float, sbyte[][])>();
            
            foreach (Record recs in _records)
            {
                RecordLand landRecord = recs as RecordLand;
                if (landRecord != null)
                {
                    SubRecordLandINTV intv = null;
                    float offset = 0f;
                    sbyte[][] deltas = null;
                    
                    if (landRecord.subRecords != null)
                    {
                        foreach (SubRecords subrecs in landRecord.subRecords)
                        {
                            if (subrecs == null || subrecs.type == null) continue;
                            
                            if (subrecs.type == "INTV")
                            {
                                intv = subrecs as SubRecordLandINTV;
                            }
                            if (subrecs.type == "VHGT")
                            {
                                SubRecordLandVHGT subrecordLandVHGT = subrecs as SubRecordLandVHGT;
                                if (subrecordLandVHGT != null)
                                {
                                    offset = subrecordLandVHGT.offset;
                                    deltas = subrecordLandVHGT.heightdata;
                                }
                            }
                        }
                    }
                    
                    if (intv != null && deltas != null)
                    {
                        try
                        {
                            int cellX = Convert.ToInt32(intv.CellX);
                            int cellY = Convert.ToInt32(intv.CellY);
                            cellData[(cellX, cellY)] = (offset, deltas);
                        }
                        catch (System.Exception ex)
                        {
                            UnityEngine.Debug.LogWarning($"Failed to process cell data: {ex.Message}");
                        }
                    }
                }
            }
            
            UnityEngine.Debug.Log($"Found {cellData.Count} land cells");
            
            // Dictionary to store processed cell heights for neighbor lookups
            Dictionary<(int x, int y), float[][]> cellHeights = new Dictionary<(int, int), float[][]>();
            
            // Process cells in sorted order (bottom-left to top-right) to ensure neighbors are calculated first
            var sortedCells = cellData.Keys.OrderBy(c => c.y).ThenBy(c => c.x).ToList();
            
            // Process each cell using OpenMW's algorithm with neighbor alignment (like CalculateCellHeightsWithNeighbors)
            foreach (var (cellX, cellY) in sortedCells)
            {
                var (offset, deltas) = cellData[(cellX, cellY)];
                
                // Calculate cell position in global array
                int startX = (cellX - Convert.ToInt32(MinCellX)) * CELL;
                int startY = (cellY - Convert.ToInt32(MinCellY)) * CELL;
                
                // Use integer arithmetic first (like OpenMW and CalculateCellHeightsWithNeighbors)
                // Maintain unscaled integer value throughout to avoid precision loss
                int rowOffset = (int)offset; // Start with offset (unscaled)
                
                // Temporary array for this cell's heights (2D for easier neighbor access)
                float[][] cellHeightsArray = new float[CELL][];
                for (int i = 0; i < CELL; i++)
                {
                    cellHeightsArray[i] = new float[CELL];
                }
                
                // Store unscaled integer values for each row's first column to avoid precision loss
                // when converting back from float for subsequent rows
                int[] rowFirstColumnUnscaled = new int[CELL];
                
                // Process each row with neighbor alignment (like CalculateCellHeightsWithNeighbors)
                for (int y = 0; y < CELL; y++)
                {
                    // At start of each row, align with neighbors
                    // Priority: bottom neighbor (row 0 only) > left neighbor > previous row
                    if (y == 0)
                    {
                        // Row 0: Check bottom neighbor first, then left neighbor
                        if (cellY > Convert.ToInt32(MinCellY) && cellHeights.ContainsKey((cellX, cellY - 1)))
                        {
                            // Align with bottom neighbor's top edge
                            // Use row 64 (top edge) and column 0 (first column) to match exactly
                            float[][] bottomCell = cellHeights[(cellX, cellY - 1)];
                            float neighborHeight = bottomCell[CELL - 1][0]; // Use row 64, column 0
                            rowOffset = (int)Math.Round(neighborHeight / TESGlobals.HEIGHT_MAP_SCALE_FACTOR);
                        }
                        else if (cellX > Convert.ToInt32(MinCellX) && cellHeights.ContainsKey((cellX - 1, cellY)))
                        {
                            // No bottom neighbor: align with left neighbor's row 0, column 63
                            // This ensures leftmost cells without bottom neighbors still align horizontally
                            float[][] leftCell = cellHeights[(cellX - 1, cellY)];
                            float neighborHeight = leftCell[0][63]; // Use row 0, column 63
                            rowOffset = (int)Math.Round(neighborHeight / TESGlobals.HEIGHT_MAP_SCALE_FACTOR);
                        }
                        else
                        {
                            // No neighbors: explicitly reset to offset to ensure clean start
                            // This is the critical case - use the offset directly without conversion
                            rowOffset = (int)offset;
                        }
                    }
                    else
                    {
                        // Subsequent rows: Check left neighbor first, then use previous row
                        if (cellX > Convert.ToInt32(MinCellX) && cellHeights.ContainsKey((cellX - 1, cellY)))
                        {
                            // Align with left neighbor (use column 63 instead of 64 to avoid edge artifacts)
                            float[][] leftCell = cellHeights[(cellX - 1, cellY)];
                            float neighborHeight = leftCell[y][63]; // Use column 63
                            rowOffset = (int)Math.Round(neighborHeight / TESGlobals.HEIGHT_MAP_SCALE_FACTOR);
                        }
                        else
                        {
                            // Standard row reset: use first column's unscaled integer value from previous row
                            // This avoids precision loss from float-to-int conversion
                            rowOffset = rowFirstColumnUnscaled[y - 1];
                        }
                    }
                    
                    // OpenMW's algorithm: add first column's delta, then accumulate horizontally
                    // rowOffset += vhgt.mHeightData[y * LandRecordData::sLandSize];
                    rowOffset += deltas[y][0];
                    
                    // Store unscaled value for next row to avoid precision loss
                    rowFirstColumnUnscaled[y] = rowOffset;
                    
                    // data.mHeights[y * LandRecordData::sLandSize] = rowOffset * Land::sHeightScale;
                    cellHeightsArray[y][0] = rowOffset * TESGlobals.HEIGHT_MAP_SCALE_FACTOR;
                    
                    // float colOffset = rowOffset;
                    int colOffset = rowOffset;
                    
                    // for (unsigned x = 1; x < LandRecordData::sLandSize; x++)
                    for (int x = 1; x < CELL; x++)
                    {
                        // colOffset += vhgt.mHeightData[y * LandRecordData::sLandSize + x];
                        colOffset += deltas[y][x];
                        
                        // data.mHeights[x + y * LandRecordData::sLandSize] = colOffset * Land::sHeightScale;
                        cellHeightsArray[y][x] = colOffset * TESGlobals.HEIGHT_MAP_SCALE_FACTOR;
                    }
                }
                
                // Store for neighbor lookups
                cellHeights[(cellX, cellY)] = cellHeightsArray;
                
                // Write to global array
                for (int ly = 0; ly < CELL; ly++)
                {
                    int py = startY + ly;
                    if (py < 0 || py >= height) continue;
                    
                    for (int lx = 0; lx < CELL; lx++)
                    {
                        int px = startX + lx;
                        if (px < 0 || px >= width) continue;
                        
                        globalHeights[py][px] = cellHeightsArray[ly][lx];
                        globalHeightsValid[py][px] = true; // Mark as valid (from existing cell)
                    }
                }
            }
            
            // Extract 64x64 chunks from each cell (skip the 65th row/column which is for neighbor alignment)
            // Calculate number of cells using the same formula as RAW generation: (Abs(Min) + Max)
            // This matches the calculation used in GenerateHeightMap_MergedLands for width/height
            int numCellsX = (Math.Abs(Convert.ToInt32(MinCellX)) + Convert.ToInt32(MaxCellX));
            int numCellsY = (Math.Abs(Convert.ToInt32(MinCellY)) + Convert.ToInt32(MaxCellY));
            int extractedWidth = numCellsX * 64;  // 64 samples per cell
            int extractedHeight = numCellsY * 64; // 64 samples per cell
            
            // Create extracted heightmap (64x64 per cell)
            float[][] extractedHeights = new float[extractedHeight][];
            bool[][] extractedHeightsValid = new bool[extractedHeight][]; // Track which pixels were written
            for (int i = 0; i < extractedHeight; i++)
            {
                extractedHeights[i] = new float[extractedWidth];
                extractedHeightsValid[i] = new bool[extractedWidth];
            }
            
            // Extract 64x64 chunks from the 65x65 cell data
            for (int cellY = 0; cellY < numCellsY; cellY++)
            {
                for (int cellX = 0; cellX < numCellsX; cellX++)
                {
                    // Calculate actual cell coordinates
                    int actualCellX = Convert.ToInt32(MinCellX) + cellX;
                    int actualCellY = Convert.ToInt32(MinCellY) + cellY;
                    
                    // Check if this cell exists in cellData
                    bool cellExists = cellData.ContainsKey((actualCellX, actualCellY));
                    
                    // Source position in 65x65 grid
                    int srcCellStartX = cellX * (int)TESGlobals.HEIGHTMAP_CELL_SIZE;
                    int srcCellStartY = cellY * (int)TESGlobals.HEIGHTMAP_CELL_SIZE;
                    
                    // Destination position in 64x64 grid
                    int dstCellStartX = cellX * (int)TESGlobals.CELL_SIZE;
                    int dstCellStartY = cellY * (int)TESGlobals.CELL_SIZE;
                    
                    // Copy 64x64 chunk (excluding the 65th row/column)
                    for (int localY = 0; localY < 64; localY++)
                    {
                        for (int localX = 0; localX < 64; localX++)
                        {
                            int srcX = srcCellStartX + localX;
                            int srcY = srcCellStartY + localY;
                            int dstX = dstCellStartX + localX;
                            int dstY = dstCellStartY + localY;
                            
                            if (srcX < width && srcY < height && dstX < extractedWidth && dstY < extractedHeight)
                            {
                                extractedHeights[dstY][dstX] = globalHeights[srcY][srcX];
                                extractedHeightsValid[dstY][dstX] = cellExists; // Mark as valid only if cell exists
                            }
                        }
                    }
                }
            }
            
            UnityEngine.Debug.Log($"Extracted heightmap dimensions: {extractedWidth}x{extractedHeight} (from {width}x{height} with 64x64 per cell)");
            
            // Find min/max from EXTRACTED heights (not global heights) for PNG normalization
            // Only consider valid (existing) cells
            float globalMinHeight = float.MaxValue;
            float globalMaxHeight = float.MinValue;
            bool foundValidHeight = false;
            
            for (int y = 0; y < extractedHeight; y++)
            {
                for (int x = 0; x < extractedWidth; x++)
                {
                    // Only consider heights from valid (existing) cells
                    if (extractedHeightsValid[y][x])
                    {
                        float h = extractedHeights[y][x];
                        if (!foundValidHeight)
                        {
                            globalMinHeight = h;
                            globalMaxHeight = h;
                            foundValidHeight = true;
                        }
                        else
                        {
                            if (h < globalMinHeight) globalMinHeight = h;
                            if (h > globalMaxHeight) globalMaxHeight = h;
                        }
                    }
                }
            }
            
            UnityEngine.Debug.Log($"Extracted height range: min={globalMinHeight}, max={globalMaxHeight}, foundValid={foundValidHeight}");
            
            // Generate PNG from extracted heightmap
            Texture2D heightTexture = new Texture2D(extractedWidth, extractedHeight, TextureFormat.RGBA32, false);
            Color[] heightPixels = new Color[extractedWidth * extractedHeight];
            
            // Always set pixels, even if all heights are 0
            if (foundValidHeight)
            {
                // Normalize from actual terrain min/max to preserve brightness
                // This ensures the terrain uses the full brightness range (0-1)
                float globalHeightRange = globalMaxHeight - globalMinHeight;
                if (globalHeightRange < 0.001f) globalHeightRange = 1f; // Avoid division by zero
                
                UnityEngine.Debug.Log($"PNG export: Normalizing from actual terrain min={globalMinHeight}, max={globalMaxHeight}, range={globalHeightRange}");
                
                for (int y = 0; y < extractedHeight; y++)
                {
                    for (int x = 0; x < extractedWidth; x++)
                    {
                        // Check if this pixel is from a valid (existing) cell
                        if (extractedHeightsValid[y][x])
                    {
                        float h = extractedHeights[y][x];
                        // Normalize: min maps to 0.0 (black), max maps to 1.0 (white)
                        float heightValue = (h - globalMinHeight) / globalHeightRange;
                        heightValue = Mathf.Clamp01(heightValue);
                        heightPixels[y * extractedWidth + x] = new UnityEngine.Color(heightValue, heightValue, heightValue, 1f);
                        }
                        else
                        {
                            // Set non-existent cells to black (sea level) instead of transparent
                            // This prevents cliff plateaus in areas where cells don't exist
                            heightPixels[y * extractedWidth + x] = new UnityEngine.Color(0f, 0f, 0f, 1f);
                        }
                    }
                }
            }
            else
            {
                // If no valid heights found, set all pixels to black (sea level) instead of transparent
                UnityEngine.Debug.LogWarning("PNG export: No valid heights found, using black (sea level) for all pixels");
                for (int y = 0; y < extractedHeight; y++)
                {
                    for (int x = 0; x < extractedWidth; x++)
                    {
                        heightPixels[y * extractedWidth + x] = new UnityEngine.Color(0f, 0f, 0f, 1f);
                    }
                }
            }
            
            heightTexture.SetPixels(heightPixels);
            heightTexture.Apply();
            
            // Save PNG
            Directory.CreateDirectory(Application.dataPath + "/StreamingAssets/Data/UOMW/Cache");
            byte[] hbytes = heightTexture.EncodeToPNG();
            string hfilePath = Application.dataPath + "/StreamingAssets/Data/UOMW/Cache/" + _esm + "_MapHeight_MergedLands.png";
            System.IO.File.WriteAllBytes(hfilePath, hbytes);
            
            UnityEngine.Debug.Log($"=== GenerateHeightMap_MergedLands: Saved test heightmap to {hfilePath} ===");
            
            // Export RAW file for Unity terrain (16-bit grayscale, little-endian)
            if (foundValidHeight)
            {
                // Normalize from actual terrain min/max to preserve brightness
                // This ensures the terrain uses the full 16-bit range (0-65535)
                float globalHeightRange = globalMaxHeight - globalMinHeight;
                if (globalHeightRange < 0.001f) globalHeightRange = 1f; // Avoid division by zero
                
                UnityEngine.Debug.Log($"RAW export: Normalizing from actual terrain min={globalMinHeight}, max={globalMaxHeight}, range={globalHeightRange}");
                
                // RAW format: 16-bit unsigned short (ushort) per pixel, little-endian
                // No header, just raw pixel data
                byte[] rawBytes = new byte[width * height * 2]; // 2 bytes per pixel (16-bit)
                int byteIndex = 0;
                int missingPixelCount = 0;
                int writtenPixelCount = 0;
                
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        // Check if this pixel is from a valid (existing) cell
                        if (globalHeightsValid[y][x])
                        {
                            writtenPixelCount++;
                            float h = globalHeights[y][x];
                            // Normalize: min maps to 0.0 (black), max maps to 1.0 (white)
                            float heightValue = (h - globalMinHeight) / globalHeightRange;
                            heightValue = Mathf.Clamp01(heightValue);
                            
                            // Convert to 16-bit (0-65535)
                            ushort rawHeightValue = (ushort)(heightValue * 65535.0f);
                            
                            // Write as little-endian (LSB first, then MSB)
                            rawBytes[byteIndex++] = (byte)(rawHeightValue & 0xFF);        // LSB
                            rawBytes[byteIndex++] = (byte)((rawHeightValue >> 8) & 0xFF); // MSB
                        }
                        else
                        {
                            missingPixelCount++;
                            // Missing cells (ocean areas without LAND records): write 0 (black/sea level)
                            // WARNING: This can create cliffs where terrain cells meet ocean cells
                            // If you see cliffs in the ocean, this is likely the cause
                            rawBytes[byteIndex++] = 0;
                            rawBytes[byteIndex++] = 0;
                        }
                    }
                }
                
                string rawFilePath = Application.dataPath + "/StreamingAssets/Data/UOMW/Cache/" + _esm + "_MapHeight.raw";
                System.IO.File.WriteAllBytes(rawFilePath, rawBytes);
                UnityEngine.Debug.Log($"Exported RAW heightmap to: {rawFilePath} (Size: {width}x{height}, 16-bit), " +
                    $"normalized from min={globalMinHeight} to max={globalMaxHeight}");
                
                // Save min/max heights to a JSON file for terrain height calculation
                // This matches OpenMW's approach of tracking actual min/max from terrain data
                string heightRangePath = Application.dataPath + "/StreamingAssets/Data/UOMW/Cache/" + _esm + "_MapHeight_Range.json";
                string heightRangeJson = $"{{\"minHeight\":{globalMinHeight},\"maxHeight\":{globalMaxHeight},\"heightRange\":{globalHeightRange}}}";
                System.IO.File.WriteAllText(heightRangePath, heightRangeJson);
                UnityEngine.Debug.Log($"Saved height range to: {heightRangePath} (min={globalMinHeight}, max={globalMaxHeight}, range={globalHeightRange})");
            }
        }

        public void GenerateHeightMap(Record[] _records, string esm = "Morrowind")
        {
            _esm = System.IO.Path.GetFileNameWithoutExtension(esm);
            //figure out texture size
            int width = (Math.Abs(Convert.ToInt32(MinCellX)) + Convert.ToInt32(MaxCellX)) * 65;
            int height = (Math.Abs(Convert.ToInt32(MinCellY)) + Convert.ToInt32(MaxCellY)) * 65;
            Texture2D newTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Texture2D normTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Texture2D heightTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Texture2D colorTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);

            UnityEngine.Debug.Log("Texure size: " + width + " x " + height);

            Color[] pixels = new Color[width * height];
            Color[] npixels = new Color[width * height];
            Color[] heightPixels = new Color[width * height];
            Color[] colorPixels = new Color[width * height];

            // First pass: collect all absolute heights to find global min/max
            // Use float for precision (offset is float, so maintain precision)
            float[][] globalHeights = new float[height][];
            int[][] globalHeightsCount = new int[height][]; // Track how many times each pixel was written (for averaging)
            VNML[][] globalNormals = new VNML[height][]; // Store normals for boundary correction
            
            for (int i = 0; i < height; i++)
            {
                globalHeights[i] = new float[width];
                globalHeightsCount[i] = new int[width];
                globalNormals[i] = new VNML[width];
            }

            foreach (Record recs in _records)
            {
                RecordLand recland = recs as RecordLand;
                if (recland != null)
                {
                    int cellx = 0;
                    int celly = 0;
                    float offset = 0;
                    sbyte[][] heightdata = new sbyte[65][];
                    VNML[][] normdata = new VNML[65][];
                    VNML[][] colordata = null;
                    for (int yy = 0; yy < 65; yy++)
                    {
                        heightdata[yy] = new sbyte[65];
                        normdata[yy] = new VNML[65];
                    }

                    foreach (SubRecords subrecs in recland.subRecords)
                    {
                        SubRecordLandINTV subRecordLandINTV = subrecs as SubRecordLandINTV;
                        if (subRecordLandINTV != null)
                        {
                            cellx = Convert.ToInt32(subRecordLandINTV.CellX);
                            celly = Convert.ToInt32(subRecordLandINTV.CellY);

                        }
                        SubRecordLandVHGT subrecordLandVHGT = subrecs as SubRecordLandVHGT;
                        if (subrecordLandVHGT != null)
                        {
                            heightdata = subrecordLandVHGT.heightdata;
                            offset = subrecordLandVHGT.offset;
                        }
                        SubRecordLandVNML subrecordLandVNML = subrecs as SubRecordLandVNML;
                        if (subrecordLandVNML != null)
                        {
                            normdata = subrecordLandVNML.normals;
                        }
                        SubRecordLandVCLR subrecordLandVCLR = subrecs as SubRecordLandVCLR;
                        if (subrecordLandVCLR != null)
                        {
                            colordata = subrecordLandVCLR.colors;
                        }


                    }


                    BlitCellJagged_DeltasGray_NoSeams(pixels, width, height, cellx, celly, MinCellX, MaxCellX, MinCellY, MaxCellY, heightdata /*byte[65][65]*/);
                    BlitCellNormals_NoSeams(npixels, width, height, cellx, celly, MinCellX, MaxCellX, MinCellY, MaxCellY, normdata /*byte[65][65]*/);
                    if (colordata != null)
                    {
                        BlitCellColors_NoSeams(colorPixels, width, height, cellx, celly, MinCellX, MaxCellX, MinCellY, MaxCellY, colordata);
                    }
                }
            }

            // First pass: Collect all cell data
            // Store cell data in a dictionary
            Dictionary<(int x, int y), (float offset, sbyte[][] deltas, VNML[][] normals)> cellData = new Dictionary<(int, int), (float, sbyte[][], VNML[][])>();
            
            foreach (Record recs in _records)
            {
                RecordLand recland = recs as RecordLand;
                if (recland != null)
                {
                    int cellx = 0;
                    int celly = 0;
                    float offset = 0;
                    sbyte[][] heightdata = new sbyte[65][];
                    VNML[][] normdata = new VNML[65][];
                    for (int yy = 0; yy < 65; yy++)
                    {
                        heightdata[yy] = new sbyte[65];
                        normdata[yy] = new VNML[65];
                    }

                    foreach (SubRecords subrecs in recland.subRecords)
                    {
                        SubRecordLandINTV subRecordLandINTV = subrecs as SubRecordLandINTV;
                        if (subRecordLandINTV != null)
                        {
                            cellx = Convert.ToInt32(subRecordLandINTV.CellX);
                            celly = Convert.ToInt32(subRecordLandINTV.CellY);
                        }
                        SubRecordLandVHGT subrecordLandVHGT = subrecs as SubRecordLandVHGT;
                        if (subrecordLandVHGT != null)
                        {
                            heightdata = subrecordLandVHGT.heightdata;
                            offset = subrecordLandVHGT.offset;
                        }
                        SubRecordLandVNML subrecordLandVNML = subrecs as SubRecordLandVNML;
                        if (subrecordLandVNML != null)
                        {
                            normdata = subrecordLandVNML.normals;
                        }
                    }
                    
                    // Store cell data (with normals)
                    cellData[(cellx, celly)] = (offset, heightdata, normdata);
                }
            }
            
            // Calculate cell heights in sorted order (bottom-left to top-right)
            // This ensures neighbors are calculated before cells that need them
            Dictionary<(int x, int y), float[][]> cellHeights = new Dictionary<(int, int), float[][]>();
            var sortedCellKeys = cellData.Keys.OrderBy(c => c.y).ThenBy(c => c.x).ToList();
            foreach (var cellKey in sortedCellKeys)
            {
                int cellx = cellKey.x;
                int celly = cellKey.y;
                var cellInfo = cellData[(cellx, celly)];
                float[][] cellHeight = CalculateCellHeightsWithNeighbors(cellx, celly, cellInfo.offset, cellInfo.deltas, cellHeights, MinCellX, MaxCellX, MinCellY, MaxCellY);
                cellHeights[(cellx, celly)] = cellHeight;
            }
            
            // Second pass: Write all cell heights to global array
            // Process cells in order (bottom-left to top-right) to ensure neighbors are calculated first
            var sortedCells = cellData.Keys.OrderBy(c => c.y).ThenBy(c => c.x).ToList();
            foreach (var cellKey in sortedCells)
            {
                int cellx = cellKey.x;
                int celly = cellKey.y;
                float[][] cellHeight = cellHeights[(cellx, celly)];
                VNML[][] normdata = cellData[(cellx, celly)].normals;
                
                BlitCellHeightMap_WriteHeights(globalHeights, globalHeightsCount, globalNormals, width, height, 
                    cellx, celly, MinCellX, MaxCellX, MinCellY, MaxCellY, cellHeight, normdata);
            }

            // First-write-wins: heights are already set correctly (no averaging needed)
            // This helps identify where cell boundaries are misaligned

            // Find global min/max across all cells
            // Only consider pixels that were actually written (exclude transparent/unwritten areas)
            bool foundValidHeight = false;
            float globalMinHeight = float.MaxValue;
            float globalMaxHeight = float.MinValue;
            
            // Find min/max - only consider pixels that were actually written
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (globalHeightsCount[y][x] > 0) // Only consider written pixels
                    {
                        float h = globalHeights[y][x];
                        if (!foundValidHeight)
                        {
                            globalMinHeight = h;
                            globalMaxHeight = h;
                            foundValidHeight = true;
                        }
                        else
                        {
                            if (h < globalMinHeight) globalMinHeight = h;
                            if (h > globalMaxHeight) globalMaxHeight = h;
                        }
                    }
                }
            }

            // Second pass: normalize and write to height pixels
            if (foundValidHeight)
            {
                // Use the actual min/max to ensure sea level maps to black
                // The minimum height should map to 0.0 (black) and maximum to 1.0 (white)
                float globalHeightRange = globalMaxHeight - globalMinHeight;
                if (globalHeightRange < 0.001f) globalHeightRange = 1f; // Avoid division by zero

                UnityEngine.Debug.Log($"Height range: min={globalMinHeight}, max={globalMaxHeight}, range={globalHeightRange}");

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        // Only write pixels that were actually written to (exclude transparent areas)
                        if (globalHeightsCount[y][x] > 0)
                        {
                            float h = globalHeights[y][x];
                            // Normalize: min maps to 0.0 (black), max maps to 1.0 (white)
                            float normalizedHeight = (h - globalMinHeight) / globalHeightRange;
                            
                            // Ensure proper clamping - min should be exactly 0.0, max should be exactly 1.0
                            normalizedHeight = Mathf.Clamp01(normalizedHeight);
                            
                            // Apply a curve to enhance contrast: darken lower values more aggressively
                            // Use a curve that pushes low values towards black while preserving high values
                            // This formula: output = input^2 for values < 0.5, linear for values >= 0.5
                            if (normalizedHeight < 0.5f)
                            {
                                normalizedHeight = normalizedHeight * normalizedHeight * 2.0f; // Squared curve for lower half
                            }
                            // Upper half stays linear to preserve peak brightness
                            
                            normalizedHeight = Mathf.Clamp01(normalizedHeight); // Final safety clamp
                            heightPixels[y * width + x] = new UnityEngine.Color(normalizedHeight, normalizedHeight, normalizedHeight, 1f);
                        }
                        else
                        {
                            // Set unwritten pixels (non-existent cells) to black (sea level) instead of transparent
                            // This prevents cliff plateaus in areas where cells don't exist
                            heightPixels[y * width + x] = new UnityEngine.Color(0f, 0f, 0f, 1f);
                        }
                    }
                }
            }
            else
            {
                // If no valid heights found, set all pixels to black (sea level) instead of transparent
                UnityEngine.Debug.LogWarning("Height map: No valid heights found, using black (sea level) for all pixels");
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        heightPixels[y * width + x] = new UnityEngine.Color(0f, 0f, 0f, 1f);
                    }
                }
            }

            Directory.CreateDirectory(Application.dataPath + "/StreamingAssets/Data/UOMW");
            Directory.CreateDirectory(Application.dataPath + "/StreamingAssets/Data/UOMW/Cache");


            newTexture.SetPixels(pixels);
            newTexture.Apply();
            byte[] bytes = newTexture.EncodeToPNG(); // or texture2D.EncodeToJPG(quality)
            string filePath = Application.dataPath + "/StreamingAssets/Data/UOMW/Cache/" + _esm + "_MapBumps.png"; // Or any desired path
            System.IO.File.WriteAllBytes(filePath, bytes);

            normTexture.SetPixels(npixels);
            normTexture.Apply();
            byte[] nbytes = normTexture.EncodeToPNG(); // or texture2D.EncodeToJPG(quality)
            string nfilePath = Application.dataPath + "/StreamingAssets/Data/UOMW/Cache/" + _esm + "_MapNorms.png"; // Or any desired path
            System.IO.File.WriteAllBytes(nfilePath, nbytes);

            heightTexture.SetPixels(heightPixels);
            heightTexture.Apply();
            byte[] hbytes = heightTexture.EncodeToPNG(); // or texture2D.EncodeToJPG(quality)
            string hfilePath = Application.dataPath + "/StreamingAssets/Data/UOMW/Cache/" + _esm + "_MapHeight.png"; // Or any desired path
            System.IO.File.WriteAllBytes(hfilePath, hbytes);

            colorTexture.SetPixels(colorPixels);
            colorTexture.Apply();
            byte[] cbytes = colorTexture.EncodeToPNG(); // or texture2D.EncodeToJPG(quality)
            string cfilePath = Application.dataPath + "/StreamingAssets/Data/UOMW/Cache/" + _esm + "_MapColors.png"; // Or any desired path
            System.IO.File.WriteAllBytes(cfilePath, cbytes);
            
            // Export RAW file for Unity terrain (16-bit grayscale, little-endian)
            if (foundValidHeight)
            {
                // Morrowind max terrain height is 32,768 units (after scaling by 8.0)
                // Sea level is 0 in Morrowind coordinates
                // Normalize from sea level (0) to max height (32,768) so sea level maps to black (0.0)
                // This ensures the lowest parts of the terrain are pure black, not RGB 4,4,4
                const float MORROWIND_SEA_LEVEL = 0f;
                const float MORROWIND_MAX_HEIGHT = 32768f; // Max terrain height in Morrowind
                float morrowindHeightRange = MORROWIND_MAX_HEIGHT - MORROWIND_SEA_LEVEL;
                
                UnityEngine.Debug.Log($"Height normalization: Morrowind sea level={MORROWIND_SEA_LEVEL}, max height={MORROWIND_MAX_HEIGHT}, " +
                    $"actual terrain min={globalMinHeight}, actual terrain max={globalMaxHeight}");
                
                // RAW format: 16-bit unsigned short (ushort) per pixel, little-endian
                // No header, just raw pixel data
                byte[] rawBytes = new byte[width * height * 2]; // 2 bytes per pixel (16-bit)
                int byteIndex = 0;
                int missingPixelCount = 0;
                int writtenPixelCount = 0;
                
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (globalHeightsCount[y][x] > 0)
                        {
                            writtenPixelCount++;
                            // Use raw height value (before normalization/contrast curve)
                            float h = globalHeights[y][x];
                            // Normalize from sea level (0) to max height (32,768)
                            // This ensures sea level maps to black (0.0) and max height maps to white (1.0)
                            // Heights below sea level will be negative and clamped to 0
                            float normalizedHeight = (h - MORROWIND_SEA_LEVEL) / morrowindHeightRange;
                            normalizedHeight = Mathf.Clamp01(normalizedHeight);
                            
                            // Convert to 16-bit (0-65535)
                            ushort heightValue = (ushort)(normalizedHeight * 65535.0f);
                            
                            // Write as little-endian (LSB first, then MSB)
                            rawBytes[byteIndex++] = (byte)(heightValue & 0xFF);        // LSB
                            rawBytes[byteIndex++] = (byte)((heightValue >> 8) & 0xFF); // MSB
                        }
                        else
                        {
                            missingPixelCount++;
                            // Unwritten pixels (missing cells): write 0 (sea level / black)
                            // WARNING: This can create cliffs where terrain cells meet ocean cells
                            // If you see cliffs in the ocean, this is likely the cause
                            rawBytes[byteIndex++] = 0;
                            rawBytes[byteIndex++] = 0;
                        }
                    }
                }
                
                string rawFilePath = Application.dataPath + "/StreamingAssets/Data/UOMW/Cache/" + _esm + "_MapHeight.raw";
                System.IO.File.WriteAllBytes(rawFilePath, rawBytes);
                UnityEngine.Debug.Log($"Exported RAW heightmap to: {rawFilePath} (Size: {width}x{height}, 16-bit), " +
                    $"normalized from sea level (0) to max height ({MORROWIND_MAX_HEIGHT})");
            }

            // COMMENTED OUT: Export cell offsets as solid colors (for visualization)
            // No longer needed now that heightmap generation is working
            /*
            Texture2D offsetTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] offsetPixels = new Color[width * height];
            
            // Initialize to black
            for (int i = 0; i < offsetPixels.Length; i++)
            {
                offsetPixels[i] = new UnityEngine.Color(0, 0, 0, 0); // Transparent black
            }
            
            // Collect all offsets to find min/max
            Dictionary<(int x, int y), float> cellOffsets = new Dictionary<(int, int), float>();
            float minOffset = float.MaxValue;
            float maxOffset = float.MinValue;
            bool foundOffset = false;
            
            foreach (Record recs in _records)
            {
                RecordLand recland = recs as RecordLand;
                if (recland != null)
                {
                    int cellx = 0;
                    int celly = 0;
                    float offset = 0;
                    
                    foreach (SubRecords subrecs in recland.subRecords)
                    {
                        SubRecordLandINTV subRecordLandINTV = subrecs as SubRecordLandINTV;
                        if (subRecordLandINTV != null)
                        {
                            cellx = Convert.ToInt32(subRecordLandINTV.CellX);
                            celly = Convert.ToInt32(subRecordLandINTV.CellY);
                        }
                        SubRecordLandVHGT subrecordLandVHGT = subrecs as SubRecordLandVHGT;
                        if (subrecordLandVHGT != null)
                        {
                            offset = subrecordLandVHGT.offset;
                        }
                    }
                    
                    cellOffsets[(cellx, celly)] = offset;
                    if (!foundOffset)
                    {
                        minOffset = offset;
                        maxOffset = offset;
                        foundOffset = true;
                    }
                    else
                    {
                        if (offset < minOffset) minOffset = offset;
                        if (offset > maxOffset) maxOffset = offset;
                    }
                }
            }
            
            // Render each cell as a solid color based on its offset
            if (foundOffset)
            {
                float offsetRange = maxOffset - minOffset;
                if (offsetRange < 0.001f) offsetRange = 1f; // Avoid division by zero
                
                UnityEngine.Debug.Log($"Offset range: min={minOffset}, max={maxOffset}, range={offsetRange}");
                
                // Define constants for offset visualization
                const int CELL = 65;
                const int STEP = 64;
                const int HALF = CELL / 2;
                int offsetCx = width / 2, offsetCy = height / 2;
                
                foreach (var kvp in cellOffsets)
                {
                    int cellx = kvp.Key.x;
                    int celly = kvp.Key.y;
                    float offset = kvp.Value;
                    
                    // Normalize offset to 0-1 range
                    float normalizedOffset = (offset - minOffset) / offsetRange;
                    normalizedOffset = Mathf.Clamp01(normalizedOffset);
                    
                    // Calculate cell bounds
                    int startX = offsetCx - HALF + cellx * STEP;
                    int startY = offsetCy - HALF + celly * STEP;
                    
                    // Determine write bounds (exclude right/bottom edges if neighbor exists)
                    bool hasRight = (cellx < MaxCellX);
                    bool hasDown = (celly < MaxCellY);
                    int writeW = CELL - (hasRight ? 1 : 0);
                    int writeH = CELL - (hasDown ? 1 : 0);
                    
                    // Fill the cell with the solid color
                    UnityEngine.Color offsetColor = new UnityEngine.Color(normalizedOffset, normalizedOffset, normalizedOffset, 1f);
                    for (int ly = 0; ly < writeH; ly++)
                    {
                        int py = startY + ly;
                        if ((uint)py >= (uint)height) continue;
                        
                        for (int lx = 0; lx < writeW; lx++)
                        {
                            int px = startX + lx;
                            if ((uint)px >= (uint)width) continue;
                            
                            offsetPixels[py * width + px] = offsetColor;
                        }
                    }
                }
            }
            
            offsetTexture.SetPixels(offsetPixels);
            offsetTexture.Apply();
            byte[] obytes = offsetTexture.EncodeToPNG();
            string ofilePath = Application.dataPath + "/StreamingAssets/Data/MapOffsets.png";
            System.IO.File.WriteAllBytes(ofilePath, obytes);
            UnityEngine.Debug.Log($"Exported cell offsets visualization to: {ofilePath}");
            */



        }

        public void BlitCellJagged_DeltasGray_NoSeams(
            UnityEngine.Color[] pixels, int W, int H,
            int cellX, int cellY,
            int minCellX, int maxCellX,
            int minCellY, int maxCellY,
            sbyte[][] deltas,
            int sgnY = +1
        )
        {
            const int CELL = 65;   // samples per tile
            const int STEP = 65;   // spacing between cell origins (critical) - matches heightmap samples per cell
            const int HALF = CELL / 2; // 32

            int cx = W / 2, cy = H / 2;

            // Place tiles 64 apart so borders line up
            int startX = cx - HALF + cellX * STEP;
            int startY = cy - HALF + sgnY * cellY * STEP;

            // If there’s a neighbor to the right/bottom, let the *neighbor* own that edge
            bool hasRight = (cellX < maxCellX);
            bool hasDown = (cellY < maxCellY);

            int writeW = CELL - (hasRight ? 1 : 0); // skip rightmost column if neighbor exists
            int writeH = CELL - (hasDown ? 1 : 0); // skip bottom row if neighbor exists

            for (int ly = 0; ly < writeH; ly++)
            {
                int py = startY + ly;
                if ((uint)py >= (uint)H) continue;
                int rowBase = py * W;

                var srcRow = deltas[ly]; // your orientation already matches
                for (int lx = 0; lx < writeW; lx++)
                {
                    int px = startX + lx;
                    if ((uint)px >= (uint)W) continue;

                    // map -128..+127 -> 0..1
                    //float gf = (srcRow[lx]) / 255f;
                    float gf = (srcRow[lx] + 128) / 255f;
                    pixels[rowBase + px] = new UnityEngine.Color(gf, gf, gf, 1f);
                }
            }
        }

        public void BlitCellNormals_NoSeams(
            UnityEngine.Color[] pixels, int W, int H,
            int cellX, int cellY,
            int minCellX, int maxCellX,
            int minCellY, int maxCellY,
            VNML[][] normals65,         // VNML[65][65] jagged, oriented the way you like
            bool flipGreen = false,     // set true if lighting looks “inside out”
            int sgnY = +1               // use -1 if +cellY should go up
        )
        {
            const int CELL = 65;
            const int STEP = 64;
            const int HALF = CELL / 2;

            int cx = W / 2, cy = H / 2;
            int startX = cx - HALF + cellX * STEP;
            int startY = cy - HALF + sgnY * cellY * STEP;

            bool hasRight = (cellX < maxCellX);
            bool hasDown = (cellY < maxCellY);
            int writeW = CELL - (hasRight ? 1 : 0);
            int writeH = CELL - (hasDown ? 1 : 0);

            for (int ly = 0; ly < writeH; ly++)
            {
                int py = startY + ly;
                if ((uint)py >= (uint)H) continue;
                int rowBase = py * W;

                VNML[] srcRow = normals65[ly];
                for (int lx = 0; lx < writeW; lx++)
                {
                    int px = startX + lx;
                    if ((uint)px >= (uint)W) continue;

                    VNML n = srcRow[lx];
                    if (n == null)
                    {
                        n = new VNML();
                        n.x = -127;
                        n.y = -127;
                        n.z = -127;
                    }
                    // Decode −128 .. +127 → −1 .. +1
                    float nx = Mathf.Clamp(n.x / 127f, -1f, 1f);
                    float ny = Mathf.Clamp(n.y / 127f, -1f, 1f);
                    float nz = Mathf.Clamp(n.z / 127f, -1f, 1f);

                    if (flipGreen) ny = -ny;

                    // Normalize to be safe
                    float len = Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (len > 1e-6f) { nx /= len; ny /= len; nz /= len; }
                    else { nx = ny = 0f; nz = 1f; }

                    // Pack back to 0..1 for RGB
                    float r = nx * 0.5f + 0.5f;
                    float g = ny * 0.5f + 0.5f;
                    float b = nz * 0.5f + 0.5f;

                    pixels[rowBase + px] = new UnityEngine.Color(r, g, b, 1f);
                }
            }
        }

        public void BlitCellColors_NoSeams(
            UnityEngine.Color[] pixels, int W, int H,
            int cellX, int cellY,
            int minCellX, int maxCellX,
            int minCellY, int maxCellY,
            VNML[][] colors65,         // VNML[65][65] jagged, contains RGB color data
            int sgnY = +1               // use -1 if +cellY should go up
        )
        {
            const int CELL = 65;
            const int STEP = 64;
            const int HALF = CELL / 2;

            int cx = W / 2, cy = H / 2;
            int startX = cx - HALF + cellX * STEP;
            int startY = cy - HALF + sgnY * cellY * STEP;

            bool hasRight = (cellX < maxCellX);
            bool hasDown = (cellY < maxCellY);
            int writeW = CELL - (hasRight ? 1 : 0);
            int writeH = CELL - (hasDown ? 1 : 0);

            for (int ly = 0; ly < writeH; ly++)
            {
                int py = startY + ly;
                if ((uint)py >= (uint)H) continue;
                int rowBase = py * W;

                VNML[] srcRow = colors65[ly];
                for (int lx = 0; lx < writeW; lx++)
                {
                    int px = startX + lx;
                    if ((uint)px >= (uint)W) continue;

                    VNML c = srcRow[lx];
                    if (c == null)
                    {
                        // Default to white if no color data
                        pixels[rowBase + px] = new UnityEngine.Color(1f, 1f, 1f, 1f);
                        continue;
                    }

                    // Convert sbyte (-128 to 127) to unsigned byte (0-255) then normalize to 0-1
                    // VCLR stores RGB as bytes (0-255), but they're read as sbyte
                    // Convert: sbyte value -> unsigned byte -> normalized float
                    // For sbyte: 0-127 map to 0-127, -128 to -1 map to 128-255
                    byte rByte = (byte)(c.x & 0xFF);
                    byte gByte = (byte)(c.y & 0xFF);
                    byte bByte = (byte)(c.z & 0xFF);
                    float r = rByte / 255f;
                    float g = gByte / 255f;
                    float b = bByte / 255f;

                    // Clamp to ensure valid color range
                    r = Mathf.Clamp01(r);
                    g = Mathf.Clamp01(g);
                    b = Mathf.Clamp01(b);

                    pixels[rowBase + px] = new UnityEngine.Color(r, g, b, 1f);
                }
            }
        }
        
        // Calculate cell heights with neighbor alignment
        // When resetting at the start of each row, use neighbor heights if available
        private float[][] CalculateCellHeightsWithNeighbors(
            int cellX, int cellY,
            float offset,
            sbyte[][] deltas,
            Dictionary<(int x, int y), float[][]> cellHeights,
            int minCellX, int maxCellX, int minCellY, int maxCellY)
        {
            const int CELL = 65;
            // Use global constant from TESGlobals
            
            float[][] absoluteHeights = new float[CELL][];
            for (int i = 0; i < CELL; i++)
            {
                absoluteHeights[i] = new float[CELL];
            }
            
            // Reconstruct absolute heights from deltas and offset
            // According to TES3 wiki: deltas are differences between adjacent pixels
            // Each pixel's height = previous pixel's height + delta
            // The "previous pixel" follows scan order: left-to-right within each row, top-to-bottom across rows
            // Row reset: at end of each row, reset to that row's first column (which accumulated first-column deltas)
            // This matches Rust implementation: height = grid_height.get(Index2D::new(0, y)) at end of each row
            
            // Use integer arithmetic first (matching Rust implementation), then convert to float
            // Rust uses i32 for offset and i8 for deltas, accumulating as i32
            int heightInt = (int)offset; // Start with offset (height at position 0,0)
            
            for (int y = 0; y < CELL; y++)
            {
                // At start of each row, check if we should align with neighbors
                // This fixes the mosaic effect by ensuring cells align at boundaries
                if (y == 0 && cellY > minCellY && cellHeights.ContainsKey((cellX, cellY - 1)))
                {
                    // Align with bottom neighbor's top edge, but use row 64 and column 1 to avoid edge artifacts
                    // The outermost rows (64, 63) might have artifacts, so use two pixels in
                    // Since we're reading from the top of the bottom neighbor cell, use column 2 to avoid edge artifacts
                    float[][] bottomCell = cellHeights[(cellX, cellY - 1)];
                    float neighborHeight = bottomCell[63][1]; // Use row 64, column 1 (top edge of bottom neighbor, further in to avoid artifacts)
                    heightInt = (int)Math.Round(neighborHeight / TESGlobals.HEIGHT_MAP_SCALE_FACTOR);
                }
                else if (cellX > minCellX && cellHeights.ContainsKey((cellX - 1, cellY)))
                {
                    // Align with left neighbor, but use column 63 instead of 64 to avoid edge artifacts
                    // The outermost column (64) might have artifacts that propagate, so use one pixel in
                    float[][] leftCell = cellHeights[(cellX - 1, cellY)];
                    float neighborHeight = leftCell[y][63]; // Use column 63 instead of 64 to avoid edge artifacts
                    heightInt = (int)Math.Round(neighborHeight / TESGlobals.HEIGHT_MAP_SCALE_FACTOR);
                }
                else if (y > 0)
                {
                    // Standard row reset: use first column's height of previous row
                    // Use the unscaled integer value directly to avoid precision loss
                    // absoluteHeights[y-1][0] is already scaled, so we need to unscale it
                    // But we stored it as scaled float, so we need to convert back
                    // Actually, we can't avoid this conversion since we're storing as float
                    // But we can be more precise by avoiding rounding
                    float prevRowFirstHeight = absoluteHeights[y - 1][0];
                    heightInt = (int)(prevRowFirstHeight / TESGlobals.HEIGHT_MAP_SCALE_FACTOR + 0.5f);
                }
                
                // Accumulate across this row
                for (int x = 0; x < CELL; x++)
                {
                    // Deltas are differences: 0 = same as previous, +n = n units higher, -n = n units lower
                    // Accumulate: height += delta
                    heightInt += deltas[y][x];
                    absoluteHeights[y][x] = heightInt;
                }
            }
            
            // Apply scale factor after reconstruction (scale everything by 8)
            // This matches the Rust implementation: scale after accumulating
            for (int y = 0; y < CELL; y++)
            {
                for (int x = 0; x < CELL; x++)
                {
                    absoluteHeights[y][x] *= TESGlobals.HEIGHT_MAP_SCALE_FACTOR;
                }
            }
            
            return absoluteHeights;
        }
        
        // Write pre-calculated cell heights to global array
        private void BlitCellHeightMap_WriteHeights(
            float[][] globalHeights, int[][] globalHeightsCount, VNML[][] globalNormals, int W, int H,
            int cellX, int cellY,
            int minCellX, int maxCellX,
            int minCellY, int maxCellY,
            float[][] cellHeight,
            VNML[][] normals,
            int sgnY = +1)
        {
            const int CELL = 65;
            const int STEP = 64;
            const int HALF = CELL / 2;
            
            int cx = W / 2, cy = H / 2;
            int startX = cx - HALF + cellX * STEP;
            int startY = cy - HALF + sgnY * cellY * STEP;
            
            bool hasRight = (cellX < maxCellX);
            bool hasDown = (cellY < maxCellY);
            int writeW = CELL - (hasRight ? 1 : 0);
            int writeH = CELL - (hasDown ? 1 : 0);
            
            for (int ly = 0; ly < writeH; ly++)
            {
                int py = startY + ly;
                if ((uint)py >= (uint)H) continue;
                
                for (int lx = 0; lx < writeW; lx++)
                {
                    int px = startX + lx;
                    if ((uint)px >= (uint)W) continue;
                    
                    // For overlapping boundaries (especially bottom/top edges), average the heights
                    // This should fix the darker bottom row issue where cells overlap at boundaries
                    if (globalHeightsCount[py][px] == 0)
                    {
                        globalHeights[py][px] = cellHeight[ly][lx];
                        globalHeightsCount[py][px] = 1;
                    }
                    else
                    {
                        // Average overlapping boundary pixels to smooth the transition
                        // This helps fix the darker bottom row where cells overlap
                        globalHeights[py][px] = (globalHeights[py][px] * globalHeightsCount[py][px] + cellHeight[ly][lx]) / (globalHeightsCount[py][px] + 1);
                        globalHeightsCount[py][px]++;
                    }
                    
                    if (globalHeightsCount[py][px] == 1 && normals != null && ly < normals.Length && lx < normals[ly].Length)
                    {
                        globalNormals[py][px] = normals[ly][lx];
                    }
                }
            }
        }
        
        
        public void BlitCellHeightMap_CollectHeights(
            float[][] globalHeights, int[][] globalHeightsCount, VNML[][] globalNormals, int W, int H,
            int cellX, int cellY,
            int minCellX, int maxCellX,
            int minCellY, int maxCellY,
            sbyte[][] deltas,
            float offset,
            VNML[][] normals,
            int sgnY = +1
        )
        {
            const int CELL = 65;   // samples per tile
            const int STEP = 65;   // spacing between cell origins (critical) - matches heightmap samples per cell
            const int HALF = CELL / 2; // 32
            // Use global constant from TESGlobals // TES3 height scale factor

            int cx = W / 2, cy = H / 2;

            // Place tiles 64 apart so borders line up
            int startX = cx - HALF + cellX * STEP;
            int startY = cy - HALF + sgnY * cellY * STEP;

            // If there's a neighbor to the right/bottom, let the *neighbor* own that edge
            bool hasRight = (cellX < maxCellX);
            bool hasDown = (cellY < maxCellY);

            int writeW = CELL - (hasRight ? 1 : 0); // skip rightmost column if neighbor exists
            int writeH = CELL - (hasDown ? 1 : 0); // skip bottom row if neighbor exists

            // Reconstruct absolute heights from deltas and offset
            // Based on merged_lands/land/height_map.rs calculate_height_map function
            // Use float precision to maintain accuracy
            float[][] absoluteHeights = new float[CELL][];
            for (int i = 0; i < CELL; i++)
            {
                absoluteHeights[i] = new float[CELL];
            }

            // Reconstruct absolute heights from deltas and offset
            // According to TES3 wiki: deltas are differences between adjacent pixels
            // Each pixel's height = previous pixel's height + delta
            // The "previous pixel" follows scan order: bottom-left to top-right (continuous accumulation)
            // Y-direction is from bottom up, so we accumulate continuously across the entire grid
            // No row resets - continuous accumulation from (0,0) to (64,64)
            
            // Use integer arithmetic first (matching Rust implementation), then convert to float
            // Rust uses i32 for offset and i8 for deltas, accumulating as i32
            int heightInt = (int)offset; // Start with offset (height at position 0,0 - bottom-left)
            
            // Continuous accumulation: scan from bottom-left to top-right
            // Process each row from left to right, moving from bottom (y=0) to top (y=64)
            for (int y = 0; y < CELL; y++)
            {
                for (int x = 0; x < CELL; x++)
                {
                    // Deltas are differences: 0 = same as previous, +n = n units higher, -n = n units lower
                    // Accumulate: height += delta (continuous, no row reset)
                    heightInt += deltas[y][x];
                    absoluteHeights[y][x] = heightInt;
                }
                // NO RESET: Continue accumulating from the last pixel of current row to first pixel of next row
                // The delta at the start of the next row (deltas[y+1][0]) represents the change from
                // the last pixel of the previous row (at x=64) to the first pixel of the next row (at x=0)
            }

            // Apply scale factor after reconstruction (scale everything by 8)
            // This matches the Rust implementation: scale after accumulating
            for (int y = 0; y < CELL; y++)
            {
                for (int x = 0; x < CELL; x++)
                {
                    absoluteHeights[y][x] *= TESGlobals.HEIGHT_MAP_SCALE_FACTOR;
                }
            }

            // Write absolute heights and normals to global array (no normalization yet)
            // Accumulate heights and count writes for edge averaging
            for (int ly = 0; ly < writeH; ly++)
            {
                int py = startY + ly;
                if ((uint)py >= (uint)H) continue;

                for (int lx = 0; lx < writeW; lx++)
                {
                    int px = startX + lx;
                    if ((uint)px >= (uint)W) continue;

                    // Use first-write-wins for overlapping boundaries
                    // This avoids averaging which can mask alignment issues
                    if (globalHeightsCount[py][px] == 0)
                    {
                        globalHeights[py][px] = absoluteHeights[ly][lx];
                        globalHeightsCount[py][px] = 1;
                    }
                    else
                    {
                        // For overlapping pixels, keep the first value (don't average)
                        // This helps identify where offsets are misaligned
                        globalHeightsCount[py][px]++;
                    }
                    
                    // Store normals (use the first one if multiple cells write to same pixel)
                    if (globalHeightsCount[py][px] == 1 && normals != null && ly < normals.Length && lx < normals[ly].Length)
                    {
                        globalNormals[py][px] = normals[ly][lx];
                    }
                }
            }
        }

        // EXPERIMENTAL: Generate heightmap by combining normals (for base/alignment) + bump deltas
        // Uses normals to establish base height field and cell alignment, then adds bump map deltas
        public void BlitCellHeightMap_FromNormalsAndBumps(
            float[][] globalHeightsFromNormals, int[][] globalHeightsCount, VNML[][] globalNormals, int W, int H,
            int cellX, int cellY,
            int minCellX, int maxCellX,
            int minCellY, int maxCellY,
            VNML[][] normals,
            sbyte[][] bumpDeltas,
            int sgnY = +1
        )
        {
            const int CELL = 65;
            const int STEP = 64;
            const int HALF = CELL / 2;
            const float CELL_SIZE_WORLD = 64.0f; // Cell size in world units
            const float HEIGHT_SCALE = 8.0f; // TES3 height scale
            
            int cx = W / 2, cy = H / 2;
            int startX = cx - HALF + cellX * STEP;
            int startY = cy - HALF + sgnY * cellY * STEP;
            
            bool hasRight = (cellX < maxCellX);
            bool hasDown = (cellY < maxCellY);
            int writeW = CELL - (hasRight ? 1 : 0);
            int writeH = CELL - (hasDown ? 1 : 0);
            
            // Step 1: Reconstruct base height field from normals using gradient integration
            // This gives us the overall shape and helps with cell alignment
            float[][] baseHeightsFromNormals = new float[CELL][];
            for (int i = 0; i < CELL; i++)
            {
                baseHeightsFromNormals[i] = new float[CELL];
            }
            
            float baseHeight = 0.0f;
            baseHeightsFromNormals[0][0] = baseHeight;
            
            // Integrate normals to get base height field
            for (int y = 0; y < CELL; y++)
            {
                for (int x = 0; x < CELL; x++)
                {
                    if (normals == null || y >= normals.Length || x >= normals[y].Length || normals[y][x] == null)
                    {
                        baseHeightsFromNormals[y][x] = (y > 0) ? baseHeightsFromNormals[y - 1][x] : 
                                                       (x > 0) ? baseHeightsFromNormals[y][x - 1] : baseHeight;
                        continue;
                    }
                    
                    VNML n = normals[y][x];
                    float nx = n.x / 127.0f;
                    float ny = n.y / 127.0f;
                    float nz = n.z / 127.0f;
                    
                    float len = Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (len < 0.001f)
                    {
                        baseHeightsFromNormals[y][x] = (y > 0) ? baseHeightsFromNormals[y - 1][x] : 
                                                       (x > 0) ? baseHeightsFromNormals[y][x - 1] : baseHeight;
                        continue;
                    }
                    nx /= len; ny /= len; nz /= len;
                    
                    // Calculate height gradients from normal
                    float gradientX = 0.0f;
                    float gradientY = 0.0f;
                    
                    if (Mathf.Abs(nz) > 0.01f)
                    {
                        gradientX = -nx / nz;
                        gradientY = -ny / nz;
                    }
                    
                    // Scale gradients appropriately
                    float heightDeltaX = gradientX * (CELL_SIZE_WORLD / (CELL - 1));
                    float heightDeltaY = gradientY * (CELL_SIZE_WORLD / (CELL - 1));
                    
                    // Integrate from neighbors
                    float height = baseHeight;
                    if (x > 0)
                    {
                        height = baseHeightsFromNormals[y][x - 1] + heightDeltaX;
                    }
                    else if (y > 0)
                    {
                        height = baseHeightsFromNormals[y - 1][x] + heightDeltaY;
                    }
                    
                    baseHeightsFromNormals[y][x] = height;
                }
            }
            
            // Step 2: Add bump map deltas to the base height field
            // The bump deltas provide the fine detail
            float[][] finalHeights = new float[CELL][];
            for (int i = 0; i < CELL; i++)
            {
                finalHeights[i] = new float[CELL];
            }
            
            // Start with base height from normals, then accumulate bump deltas
            float currentHeight = baseHeightsFromNormals[0][0];
            for (int y = 0; y < CELL; y++)
            {
                for (int x = 0; x < CELL; x++)
                {
                    // Start each row with the base height from normals at that position
                    if (x == 0)
                    {
                        currentHeight = baseHeightsFromNormals[y][0];
                    }
                    
                    // Add bump delta (scaled by height scale factor)
                    currentHeight += bumpDeltas[y][x] * HEIGHT_SCALE;
                    finalHeights[y][x] = currentHeight;
                }
                // Reset to first column's height for next row
                currentHeight = finalHeights[y][0];
            }
            
            // Write to global array
            for (int ly = 0; ly < writeH; ly++)
            {
                int py = startY + ly;
                if ((uint)py >= (uint)H) continue;
                
                for (int lx = 0; lx < writeW; lx++)
                {
                    int px = startX + lx;
                    if ((uint)px >= (uint)W) continue;
                    
                    if (globalHeightsCount[py][px] == 1)
                    {
                        globalHeightsFromNormals[py][px] = finalHeights[ly][lx];
                    }
                    else
                    {
                        globalHeightsFromNormals[py][px] = (globalHeightsFromNormals[py][px] + finalHeights[ly][lx]) * 0.5f;
                    }
                }
            }
        }

        /// <summary>
        /// Helper method to search for a texture in a specific cache directory
        /// </summary>
        private string FindTextureInCacheDirectories(string textureDir, List<string> namesToTry, ushort vtexIndex)
        {
            if (!Directory.Exists(textureDir))
                return null;
                
            string[] files = Directory.GetFiles(textureDir);
            //UnityEngine.Debug.Log($"[VTEX {vtexIndex}] Searching in directory '{textureDir}' with {files.Length} files");
            
            // For each name to try, search with multiple extensions
            foreach (string nameToTry in namesToTry)
            {
                string baseNameNoExt = System.IO.Path.GetFileNameWithoutExtension(nameToTry);
                
                // Try multiple extensions: PNG (converted from DDS/TGA), DDS (if not converted), TGA, and original
                string[] extensions = new[] { ".png", ".dds", ".tga" };
                string originalExt = System.IO.Path.GetExtension(nameToTry);
                if (!string.IsNullOrEmpty(originalExt) && !extensions.Contains(originalExt.ToLower()))
                {
                    extensions = new[] { originalExt.ToLower() }.Concat(extensions).ToArray();
                }
                
                foreach (string ext in extensions)
                {
                    string searchFilename = baseNameNoExt + ext;
                    
                    foreach (string file in files)
                    {
                        string fileName = System.IO.Path.GetFileName(file);
                        // Case-insensitive comparison
                        if (fileName.Equals(searchFilename, StringComparison.OrdinalIgnoreCase))
                        {
                            //UnityEngine.Debug.Log($"[VTEX {vtexIndex}] ✓ Found: {fileName} in {textureDir}");
                            return file;
                        }
                    }
                }
            }
            
            // If still not found, try a more aggressive search - check if any file contains the base name
            if (namesToTry.Count > 0)
            {
                string firstBaseName = System.IO.Path.GetFileNameWithoutExtension(namesToTry[0]);
                
                foreach (string file in files)
                {
                    string fileName = System.IO.Path.GetFileName(file);
                    string fileNameNoExt = System.IO.Path.GetFileNameWithoutExtension(fileName);
                    
                    // Try removing underscores and spaces for comparison
                    string normalizedSearch = firstBaseName.Replace("_", "").Replace(" ", "").Replace("-", "").ToLower();
                    string normalizedFile = fileNameNoExt.Replace("_", "").Replace(" ", "").Replace("-", "").ToLower();
                    
                    if (normalizedFile.Contains(normalizedSearch) || normalizedSearch.Contains(normalizedFile))
                    {
                        // Check if it's a valid texture extension
                        string fileExt = System.IO.Path.GetExtension(fileName).ToLower();
                        if (fileExt == ".png" || fileExt == ".dds" || fileExt == ".tga")
                        {
                            //UnityEngine.Debug.Log($"[VTEX {vtexIndex}] ✓ Found via fuzzy match: {fileName} in {textureDir}");
                            return file;
                        }
                    }
                }
            }
            
            return null;
        }

    }
}
