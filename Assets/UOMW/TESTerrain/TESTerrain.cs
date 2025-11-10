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
        private static bool _debugXStitchingLogged = false; // Track if we've logged X-direction stitching
        private static int _debugCellCount = 0; // Track number of cells logged for debug
        private string _esm = "";
        private string _bsa = "";
        public TESTerrain() { }

        public TESTerrain(int minX, int maxX, int minY, int maxY)
        {
            MinCellX = minX; MaxCellX = maxX; MinCellY = minY; MaxCellY = maxY;
        }

        // Helper function to convert DDS to PNG using Pfim
        #if UNITY_EDITOR
        /// <summary>
        /// Sets texture import settings to disable alpha source (prevents shininess in URP terrain)
        /// </summary>
        private void SetTextureImportSettings(string texturePath)
        {
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
                    importer.alphaSource = TextureImporterAlphaSource.None;
                    importer.SaveAndReimport();
                    AssetDatabase.Refresh();
                }
            }
            catch (System.Exception ex)
            {
                //UnityEngine.Debug.LogWarning($"Failed to set texture import settings for {texturePath}: {ex.Message}");
            }
        }
        #endif

        private bool ConvertDDSToPNG(byte[] ddsData, string outputPngPath)
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
                            // RGBA32: 4 bytes per pixel (R, G, B, A)
                            for (int y = 0; y < height; y++)
                            {
                                for (int x = 0; x < width; x++)
                                {
                                    int rowOffset = y * stride;
                                    int pixelOffset = rowOffset + x * 4;
                                    if (pixelOffset + 3 < image.Data.Length)
                                    {
                                        pixels[y * width + x] = new Color(
                                            image.Data[pixelOffset] / 255f,
                                            image.Data[pixelOffset + 1] / 255f,
                                            image.Data[pixelOffset + 2] / 255f,
                                            image.Data[pixelOffset + 3] / 255f
                                        );
                                    }
                                }
                            }
                            break;

                        case ImageFormat.Rgb24:
                            // RGB24: 3 bytes per pixel (R, G, B)
                            for (int y = 0; y < height; y++)
                            {
                                for (int x = 0; x < width; x++)
                                {
                                    int rowOffset = y * stride;
                                    int pixelOffset = rowOffset + x * 3;
                                    if (pixelOffset + 2 < image.Data.Length)
                                    {
                                        pixels[y * width + x] = new Color(
                                            image.Data[pixelOffset] / 255f,
                                            image.Data[pixelOffset + 1] / 255f,
                                            image.Data[pixelOffset + 2] / 255f,
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
                                // Assume RGBA32
                                for (int y = 0; y < height; y++)
                                {
                                    for (int x = 0; x < width; x++)
                                    {
                                        int rowOffset = y * stride;
                                        int pixelOffset = rowOffset + x * 4;
                                        if (pixelOffset + 3 < image.Data.Length)
                                        {
                                            pixels[y * width + x] = new Color(
                                                image.Data[pixelOffset] / 255f,
                                                image.Data[pixelOffset + 1] / 255f,
                                                image.Data[pixelOffset + 2] / 255f,
                                                image.Data[pixelOffset + 3] / 255f
                                            );
                                        }
                                    }
                                }
                            }
                            else if (bytesPerPixel == 3)
                            {
                                // Assume RGB24
                                for (int y = 0; y < height; y++)
                                {
                                    for (int x = 0; x < width; x++)
                                    {
                                        int rowOffset = y * stride;
                                        int pixelOffset = rowOffset + x * 3;
                                        if (pixelOffset + 2 < image.Data.Length)
                                        {
                                            pixels[y * width + x] = new Color(
                                                image.Data[pixelOffset] / 255f,
                                                image.Data[pixelOffset + 1] / 255f,
                                                image.Data[pixelOffset + 2] / 255f,
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

                    // Create Unity texture and encode to PNG
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

            // Step 2: Open BSA archive
            string bsaPath = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", _bsa);
            if (!System.IO.File.Exists(bsaPath))
            {
                UnityEngine.Debug.LogError($"BSA file not found: {bsaPath}");
                return;
            }

            BSASharp.BSA bsaArchive = null;
            try
            {
                bsaArchive = new BSASharp.BSA();
                bsaArchive.Open(bsaPath);
                //UnityEngine.Debug.Log($"Opened BSA archive: {bsaPath}");
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"Failed to open BSA archive: {ex.Message}");
                return;
            }

            // Step 3: Create output directory
            string outputDir = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Textures", _esm);
            System.IO.Directory.CreateDirectory(outputDir);

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

            // Step 5: Extract textures from BSA
            int extractedCount = 0;
            int failedCount = 0;

            // Get all file names from BSA for path resolution
            HashSet<string> bsaFileNames = new HashSet<string>(bsaArchive.GetFileNames(), StringComparer.OrdinalIgnoreCase);
            
            // Debug: Log some sample texture file names from BSA to understand path structure
            int sampleCount = 0;
            foreach (string bsaFileName in bsaFileNames)
            {
                string lowerName = bsaFileName.ToLower();
                if ((lowerName.Contains(".tga") || lowerName.Contains(".dds")) && sampleCount < 10)
                {
                    //UnityEngine.Debug.Log($"Sample BSA texture path: '{bsaFileName}'");
                    sampleCount++;
                }
            }

            foreach (int textureIndex in uniqueTextureIndices)
            {
                if (textureIndexToNames.TryGetValue(textureIndex, out var names))
                {
                    // Try primary name first (from NAME subrecord), then fallback (from DATA subrecord)
                    string[] namesToTry = new string[] { names.primary, names.fallback }
                        .Where(n => !string.IsNullOrEmpty(n))
                        .Distinct()
                        .ToArray();
                    
                    // Check if texture is already cached
                    bool alreadyCached = false;
                    string cachedPngPath = null;
                    string cachedOriginalPath = null;
                    
                    foreach (string textureName in namesToTry)
                    {
                        string baseFilename = System.IO.Path.GetFileName(textureName);
                        string baseNameNoExt = System.IO.Path.GetFileNameWithoutExtension(baseFilename);
                        
                        // Check if PNG already exists (preferred format)
                        string pngPath = System.IO.Path.Combine(outputDir, baseNameNoExt + ".png");
                        if (System.IO.File.Exists(pngPath))
                        {
                            alreadyCached = true;
                            cachedPngPath = pngPath;
                            extractedCount++;
                            //UnityEngine.Debug.Log($"Texture already cached as PNG: {baseNameNoExt}.png (skipping extraction)");
                            break;
                        }
                        
                        // Check if original format exists (DDS, TGA, etc.)
                        string[] extensions = new[] { ".dds", ".tga", ".png" };
                        string originalExt = System.IO.Path.GetExtension(baseFilename);
                        if (!string.IsNullOrEmpty(originalExt) && !extensions.Contains(originalExt.ToLower()))
                        {
                            extensions = new[] { originalExt.ToLower() }.Concat(extensions).ToArray();
                        }
                        
                        foreach (string ext in extensions)
                        {
                            string testPath = System.IO.Path.Combine(outputDir, baseNameNoExt + ext);
                            if (System.IO.File.Exists(testPath))
                            {
                                cachedOriginalPath = testPath;
                                // Check if we need to convert to PNG
                                if (ext == ".dds" || ext == ".tga")
                                {
                                    // Original exists but PNG doesn't - convert it
                                    try
                                    {
                                        byte[] fileData = System.IO.File.ReadAllBytes(testPath);
                                        if (ext == ".dds")
                                        {
                                            if (ConvertDDSToPNG(fileData, pngPath))
                                            {
                                                extractedCount++;
                                                //UnityEngine.Debug.Log($"Converted cached DDS to PNG: {baseNameNoExt}.dds -> {baseNameNoExt}.png");
                                                alreadyCached = true;
                                                cachedPngPath = pngPath;
                                                break;
                                            }
                                        }
                                        else if (ext == ".tga")
                                        {
                                            Texture2D tempTexture = new Texture2D(2, 2);
                                            if (tempTexture.LoadImage(fileData))
                                            {
                                                byte[] pngData = tempTexture.EncodeToPNG();
                                                System.IO.File.WriteAllBytes(pngPath, pngData);
                                                extractedCount++;
                                                //UnityEngine.Debug.Log($"Converted cached TGA to PNG: {baseNameNoExt}.tga -> {baseNameNoExt}.png");
                                                UnityEngine.Object.DestroyImmediate(tempTexture);
                                                #if UNITY_EDITOR
                                                SetTextureImportSettings(pngPath);
                                                #endif
                                                alreadyCached = true;
                                                cachedPngPath = pngPath;
                                                break;
                                            }
                                            UnityEngine.Object.DestroyImmediate(tempTexture);
                                        }
                                    }
                                    catch (System.Exception ex)
                                    {
                                        //UnityEngine.Debug.LogWarning($"Failed to convert cached {ext} to PNG: {ex.Message}");
                                    }
                                }
                                else
                                {
                                    // Already PNG or other format, skip
                                    alreadyCached = true;
                                    extractedCount++;
                                    //UnityEngine.Debug.Log($"Texture already cached: {baseNameNoExt}{ext} (skipping extraction)");
                                    break;
                                }
                            }
                        }
                        if (alreadyCached) break;
                    }
                    
                    // Skip extraction if already cached
                    if (alreadyCached)
                        continue;
                    
                    try
                    {
                        string foundPath = null;
                        string usedName = null;
                        
                        foreach (string textureName in namesToTry)
                        {
                            string baseFilename = System.IO.Path.GetFileName(textureName);
                            
                            // Debug: log what we're looking for
                            //UnityEngine.Debug.Log($"Looking for texture: '{textureName}' (base: '{baseFilename}')");
                            
                            // Try multiple path variations
                            string[] pathVariations = new string[]
                            {
                                textureName.Replace('/', '\\'),  // Original path with normalized separators
                                baseFilename,  // Just the filename
                                "textures\\" + baseFilename,  // textures\filename
                                "textures\\landscape\\" + baseFilename,  // textures\landscape\filename
                                "textures\\terrain\\" + baseFilename,  // textures\terrain\filename
                                "textures\\tx\\" + baseFilename,  // textures\tx\filename
                            };

                            foreach (string pathVar in pathVariations)
                            {
                                // Check in our hashset first (faster)
                                if (bsaFileNames.Contains(pathVar))
                                {
                                    foundPath = pathVar;
                                    usedName = textureName;
                                    break;
                                }
                                
                                // Also check directly in BSA (in case hashset missed it)
                                if (bsaArchive.FileExists(pathVar))
                                {
                                    foundPath = pathVar;
                                    usedName = textureName;
                                    break;
                                }
                            }
                            
                            if (foundPath != null) break;

                            // If not found in common paths, try to find any file with matching name
                            // Try case-insensitive search
                            foreach (string bsaFileName in bsaFileNames)
                            {
                                string bsaBaseName = System.IO.Path.GetFileName(bsaFileName);
                                if (bsaBaseName.Equals(baseFilename, StringComparison.OrdinalIgnoreCase))
                                {
                                    foundPath = bsaFileName;
                                    usedName = textureName;
                                    //UnityEngine.Debug.Log($"Found texture via search: '{baseFilename}' -> '{foundPath}'");
                                    break;
                                }
                            }
                            
                            if (foundPath != null) break;
                            
                            // If still not found, try partial match (filename might have different extension or case)
                            string baseNameNoExt = System.IO.Path.GetFileNameWithoutExtension(baseFilename);
                            foreach (string bsaFileName in bsaFileNames)
                            {
                                string bsaBaseNameNoExt = System.IO.Path.GetFileNameWithoutExtension(bsaFileName);
                                if (bsaBaseNameNoExt.Equals(baseNameNoExt, StringComparison.OrdinalIgnoreCase))
                                {
                                    foundPath = bsaFileName;
                                    usedName = textureName;
                                    //UnityEngine.Debug.Log($"Found texture via partial match: '{baseFilename}' -> '{foundPath}'");
                                    break;
                                }
                            }
                            
                            if (foundPath != null) break;
                        }

                        if (foundPath != null)
                        {
                            // Extract file
                            var fileEntry = bsaArchive.GetFileEntry(foundPath);
                            if (fileEntry == null)
                            {
                                UnityEngine.Debug.LogWarning($"File entry not found for: {foundPath}");
                                failedCount++;
                                continue;
                            }
                            
                            //UnityEngine.Debug.Log($"Extracting: {foundPath} (Size: {fileEntry.FileSize} bytes, Offset: {fileEntry.Offset})");
                            byte[] fileData = bsaArchive.ExtractFile(foundPath);
                            
                            if (fileData == null || fileData.Length != fileEntry.FileSize)
                            {
                                UnityEngine.Debug.LogError($"Extracted data size mismatch for {foundPath}: expected {fileEntry.FileSize}, got {fileData?.Length ?? 0}");
                                failedCount++;
                                continue;
                            }
                            
                            // Use the primary name for the output filename (or fallback if primary not available)
                            string outputName = usedName ?? names.primary ?? names.fallback;
                            string baseFilename = System.IO.Path.GetFileName(outputName);
                            
                            // Get the actual file extension from the BSA path (might be different from ESM filename)
                            string bsaExtension = System.IO.Path.GetExtension(foundPath).ToLower();
                            string esmExtension = System.IO.Path.GetExtension(baseFilename).ToLower();
                            
                            // Use the actual extension from BSA if available, otherwise use ESM extension
                            string actualExtension = !string.IsNullOrEmpty(bsaExtension) ? bsaExtension : esmExtension;
                            string outputFilename = System.IO.Path.ChangeExtension(baseFilename, actualExtension);
                            string outputPath = System.IO.Path.Combine(outputDir, outputFilename);
                            
                            // Handle different texture formats
                            if (actualExtension == ".dds")
                            {
                                // DDS files - Convert to PNG (works in both editor and play mode)
                                try
                                {
                                    string pngFilename = System.IO.Path.ChangeExtension(baseFilename, ".png");
                                    string pngOutputPath = System.IO.Path.Combine(outputDir, pngFilename);
                                    
                                    // Try to convert DDS to PNG
                                    if (ConvertDDSToPNG(fileData, pngOutputPath))
                                    {
                                        extractedCount++;
                                        //UnityEngine.Debug.Log($"Extracted and converted DDS->PNG: {foundPath} -> {pngOutputPath}");
                                        #if UNITY_EDITOR
                                        SetTextureImportSettings(pngOutputPath);
                                        #endif
                                    }
                                    else
                                    {
                                        // If conversion failed, try editor API as fallback
                                        #if UNITY_EDITOR
                                        System.IO.File.WriteAllBytes(outputPath, fileData);
                                        string assetPath = "Assets" + outputPath.Replace(Application.dataPath, "").Replace('\\', '/');
                                        UnityEditor.AssetDatabase.ImportAsset(assetPath, UnityEditor.ImportAssetOptions.ForceUpdate);
                                        
                                        Texture2D tempTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                                        if (tempTexture != null)
                                        {
                                            byte[] pngData = tempTexture.EncodeToPNG();
                                            System.IO.File.WriteAllBytes(pngOutputPath, pngData);
                                            System.IO.File.Delete(outputPath);
                                            UnityEditor.AssetDatabase.DeleteAsset(assetPath);
                                            extractedCount++;
                                            //UnityEngine.Debug.Log($"Extracted and converted DDS->PNG (via AssetDatabase): {foundPath} -> {pngOutputPath} ({tempTexture.width}x{tempTexture.height})");
                                        }
                                        else
                                        {
                                            // Keep DDS if all conversion methods fail
                                            extractedCount++;
                                            UnityEngine.Debug.LogWarning($"Could not convert DDS, keeping as DDS: {outputPath}");
                                        }
                                        #else
                                        // At runtime, if conversion failed, save as DDS
                                        System.IO.File.WriteAllBytes(outputPath, fileData);
                                        extractedCount++;
                                        UnityEngine.Debug.LogWarning($"Could not convert DDS at runtime, saved as DDS: {outputPath}");
                                        #endif
                                    }
                                }
                                catch (System.Exception ex)
                                {
                                    UnityEngine.Debug.LogError($"Error processing DDS file {baseFilename}: {ex.Message}");
                                    System.IO.File.WriteAllBytes(outputPath, fileData);
                                    extractedCount++;
                                }
                            }
                            else if (actualExtension == ".tga")
                            {
                                // TGA files - try to convert to PNG
                                try
                                {
                                    Texture2D tempTexture = new Texture2D(2, 2);
                                    if (tempTexture.LoadImage(fileData))
                                    {
                                        // Convert to PNG
                                        byte[] pngData = tempTexture.EncodeToPNG();
                                        string pngFilename = System.IO.Path.ChangeExtension(baseFilename, ".png");
                                        outputPath = System.IO.Path.Combine(outputDir, pngFilename);
                                        
                                        System.IO.File.WriteAllBytes(outputPath, pngData);
                                        extractedCount++;
                                        //UnityEngine.Debug.Log($"Extracted and converted TGA->PNG: {foundPath} -> {outputPath} ({tempTexture.width}x{tempTexture.height})");
                                        #if UNITY_EDITOR
                                        SetTextureImportSettings(outputPath);
                                        #endif
                                        UnityEngine.Object.DestroyImmediate(tempTexture);
                                    }
                                    else
                                    {
                                        // If LoadImage fails, save as-is
                                        UnityEngine.Debug.LogWarning($"Failed to load TGA image, saving as-is: {baseFilename}");
                                        System.IO.File.WriteAllBytes(outputPath, fileData);
                                        extractedCount++;
                                        UnityEngine.Object.DestroyImmediate(tempTexture);
                                    }
                                }
                                catch (System.Exception ex)
                                {
                                    UnityEngine.Debug.LogError($"Error converting TGA to PNG for {baseFilename}: {ex.Message}");
                                    System.IO.File.WriteAllBytes(outputPath, fileData);
                                    extractedCount++;
                                }
                            }
                            else
                            {
                                // Other formats (PNG, JPG, etc.) - save as-is
                                System.IO.File.WriteAllBytes(outputPath, fileData);
                                extractedCount++;
                                //UnityEngine.Debug.Log($"Extracted: {foundPath} -> {outputPath}");
                            }
                        }
                        else
                        {
                            // Texture not found in BSA, try file system
                            string fileSystemPath = null;
                            string bsaDirectory = System.IO.Path.GetDirectoryName(bsaPath);
                            
                            // Try common texture directory locations relative to BSA file
                            string[] textureDirs = new[]
                            {
                                System.IO.Path.Combine(bsaDirectory, "Textures"),
                                System.IO.Path.Combine(bsaDirectory, "textures"),
                                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(bsaDirectory), "Textures"),
                                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(bsaDirectory), "textures"),
                            };
                            
                            foreach (string textureName in namesToTry)
                            {
                                string baseFilename = System.IO.Path.GetFileName(textureName);
                                string baseNameNoExt = System.IO.Path.GetFileNameWithoutExtension(baseFilename);
                                
                                // Try multiple extensions
                                string[] extensions = new[] { ".dds", ".tga", ".png" };
                                string originalExt = System.IO.Path.GetExtension(textureName);
                                if (!string.IsNullOrEmpty(originalExt) && !extensions.Contains(originalExt.ToLower()))
                                {
                                    extensions = new[] { originalExt.ToLower() }.Concat(extensions).ToArray();
                                }
                                
                                foreach (string textureDir in textureDirs)
                                {
                                    if (System.IO.Directory.Exists(textureDir))
                                    {
                                        foreach (string ext in extensions)
                                        {
                                            string testPath = System.IO.Path.Combine(textureDir, baseNameNoExt + ext);
                                            if (System.IO.File.Exists(testPath))
                                            {
                                                fileSystemPath = testPath;
                                                usedName = textureName;
                                                break;
                                            }
                                            
                                            // Also try with full path from textureName
                                            string fullPath = System.IO.Path.Combine(textureDir, textureName.Replace('/', '\\'));
                                            if (System.IO.File.Exists(fullPath))
                                            {
                                                fileSystemPath = fullPath;
                                                usedName = textureName;
                                                break;
                                            }
                                        }
                                        if (fileSystemPath != null) break;
                                    }
                                }
                                if (fileSystemPath != null) break;
                            }
                            
                            if (fileSystemPath != null && System.IO.File.Exists(fileSystemPath))
                            {
                                // Copy file from file system to cache
                                try
                                {
                                    string outputName = usedName ?? names.primary ?? names.fallback;
                                    string baseFilename = System.IO.Path.GetFileName(outputName);
                                    string actualExtension = System.IO.Path.GetExtension(fileSystemPath).ToLower();
                                    string outputFilename = System.IO.Path.ChangeExtension(baseFilename, actualExtension);
                                    string outputPath = System.IO.Path.Combine(outputDir, outputFilename);
                                    
                                    // Handle DDS conversion if needed
                                    if (actualExtension == ".dds")
                                    {
                                        byte[] fileData = System.IO.File.ReadAllBytes(fileSystemPath);
                                        string pngFilename = System.IO.Path.ChangeExtension(baseFilename, ".png");
                                        string pngOutputPath = System.IO.Path.Combine(outputDir, pngFilename);
                                        
                                        if (ConvertDDSToPNG(fileData, pngOutputPath))
                                        {
                                            extractedCount++;
                                            //UnityEngine.Debug.Log($"Copied and converted DDS->PNG from file system: {fileSystemPath} -> {pngOutputPath}");
                                            #if UNITY_EDITOR
                                            SetTextureImportSettings(pngOutputPath);
                                            #endif
                                        }
                                        else
                                        {
                                            // Copy DDS as-is if conversion fails
                                            System.IO.File.Copy(fileSystemPath, outputPath, true);
                                            extractedCount++;
                                            //UnityEngine.Debug.Log($"Copied DDS from file system: {fileSystemPath} -> {outputPath}");
                                        }
                                    }
                                    else if (actualExtension == ".tga")
                                    {
                                        // Try to convert TGA to PNG
                                        byte[] fileData = System.IO.File.ReadAllBytes(fileSystemPath);
                                        Texture2D tempTexture = new Texture2D(2, 2);
                                        if (tempTexture.LoadImage(fileData))
                                        {
                                            byte[] pngData = tempTexture.EncodeToPNG();
                                            string pngFilename = System.IO.Path.ChangeExtension(baseFilename, ".png");
                                            string pngOutputPath = System.IO.Path.Combine(outputDir, pngFilename);
                                            System.IO.File.WriteAllBytes(pngOutputPath, pngData);
                                            extractedCount++;
                                            //UnityEngine.Debug.Log($"Copied and converted TGA->PNG from file system: {fileSystemPath} -> {pngOutputPath}");
                                            #if UNITY_EDITOR
                                            SetTextureImportSettings(pngOutputPath);
                                            #endif
                                            UnityEngine.Object.DestroyImmediate(tempTexture);
                                        }
                                        else
                                        {
                                            System.IO.File.Copy(fileSystemPath, outputPath, true);
                                            extractedCount++;
                                            //UnityEngine.Debug.Log($"Copied TGA from file system: {fileSystemPath} -> {outputPath}");
                                            UnityEngine.Object.DestroyImmediate(tempTexture);
                                        }
                                    }
                                    else
                                    {
                                        // Copy other formats as-is
                                        System.IO.File.Copy(fileSystemPath, outputPath, true);
                                        extractedCount++;
                                        //UnityEngine.Debug.Log($"Copied from file system: {fileSystemPath} -> {outputPath}");
                                    }
                                }
                                catch (System.Exception ex)
                                {
                                    UnityEngine.Debug.LogError($"Failed to copy texture from file system {fileSystemPath}: {ex.Message}");
                                    failedCount++;
                                }
                            }
                            else
                            {
                                // Build list of names we tried for error message
                                string triedNames = string.Join(", ", namesToTry.Select(n => $"'{n}'"));
                                //UnityEngine.Debug.LogWarning($"Texture not found in BSA or file system for index {textureIndex} (tried names: {triedNames})");
                                failedCount++;
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        string triedNames = string.Join(", ", namesToTry.Select(n => $"'{n}'"));
                        UnityEngine.Debug.LogError($"Failed to extract texture for index {textureIndex} (tried: {triedNames}): {ex.Message}");
                        failedCount++;
                    }
                }
                else
                {
                    //UnityEngine.Debug.LogWarning($"No filename mapping found for texture index {textureIndex}");
                    failedCount++;
                }
            }

            // Clean up
            if (bsaArchive != null)
            {
                bsaArchive.Close();
            }

            //UnityEngine.Debug.Log($"Texture extraction complete: {extractedCount} extracted, {failedCount} failed");
        }

        public void GenerateUnityTerrain(Record[] _records, string esm = "Morrowind", float terrainHeight = 256f, float waterY = 0f)
        {
            _esm = System.IO.Path.GetFileNameWithoutExtension(esm);

            // Step 1: Load the heightmap RAW file
            string rawFilePath = Application.dataPath + "/StreamingAssets/Data/UOMW/Cache/" + _esm + "_MapHeight.raw";
            if (!File.Exists(rawFilePath))
            {
                UnityEngine.Debug.LogError($"Heightmap RAW file not found: {rawFilePath}. Please run GenerateHeightMap first.");
                return;
            }

            // Read the RAW file dimensions from the heightmap generation
            // RAW file uses 65 samples per cell (for seamless edges)
            int rawWidth = (Math.Abs(Convert.ToInt32(MinCellX)) + Convert.ToInt32(MaxCellX)) * 65;
            int rawHeight = (Math.Abs(Convert.ToInt32(MinCellY)) + Convert.ToInt32(MaxCellY)) * 65;

            // Calculate actual world size: cells are 64 units apart (not 65)
            // Number of cells in each direction
            int numCellsX = Convert.ToInt32(MaxCellX) - Convert.ToInt32(MinCellX) + 1;
            int numCellsY = Convert.ToInt32(MaxCellY) - Convert.ToInt32(MinCellY) + 1;
            
            // The heightmap has 65 samples per cell, but each cell spans 64 units in world space
            // Unity's heightmap resolution is the number of vertices, which will be rawWidth + 1
            // To match the heightmap data extent, we need to scale the terrain size
            // Each sample represents 64/65 units, so rawWidth samples span rawWidth * (64/65) units
            // But Unity uses heightmapResolution - 1 intervals, so we use rawWidth intervals
            float worldWidth = rawWidth * (64f / 65f);
            float worldHeight = rawHeight * (64f / 65f);

            // Read RAW heightmap data (16-bit little-endian)
            byte[] rawData = File.ReadAllBytes(rawFilePath);
            
            // Unity terrain heightmap must be (width+1) x (height+1)
            // The RAW file is rawWidth x rawHeight, so we need to pad it
            int heightmapWidth = rawWidth + 1;
            int heightmapHeight = rawHeight + 1;
            float[,] heights = new float[heightmapHeight, heightmapWidth];

            // Read RAW data and pad edges
            for (int y = 0; y < heightmapHeight; y++)
            {
                for (int x = 0; x < heightmapWidth; x++)
                {
                    // For pixels within RAW bounds, read from file
                    if (x < rawWidth && y < rawHeight)
                    {
                        int byteIndex = (y * rawWidth + x) * 2;
                        if (byteIndex + 1 < rawData.Length)
                        {
                            ushort heightValue = (ushort)(rawData[byteIndex] | (rawData[byteIndex + 1] << 8));
                            heights[y, x] = heightValue / 65535.0f; // Normalize to 0-1
                        }
                        else
                        {
                            heights[y, x] = 0f; // Default if out of bounds
                        }
                    }
                    else
                    {
                        // Pad edges by duplicating the last valid pixel
                        int srcX = Mathf.Clamp(x, 0, rawWidth - 1);
                        int srcY = Mathf.Clamp(y, 0, rawHeight - 1);
                        int byteIndex = (srcY * rawWidth + srcX) * 2;
                        if (byteIndex + 1 < rawData.Length)
                        {
                            ushort heightValue = (ushort)(rawData[byteIndex] | (rawData[byteIndex + 1] << 8));
                            heights[y, x] = heightValue / 65535.0f;
                        }
                        else
                        {
                            heights[y, x] = 0f;
                        }
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
                
                // Adjust world size proportionally to maintain correct aspect ratio
                // The heightmap is now square, but world size should maintain original proportions
                // Don't change world size - Unity will handle the mapping correctly
            }
            
            // Set Unity's heightmap resolution to the valid resolution we chose
            terrainData.heightmapResolution = actualHeightmapResolution;
            
            // Set terrain size to match world coordinates (64 units per cell)
            // This ensures the terrain matches the cell grid placement
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

            Texture2D placeholderTexture = CreatePlaceholderTexture("Placeholder", new Color(0.5f, 0.5f, 0.5f, 1f)); // Gray placeholder

            // Step 6: Create TerrainLayers for each unique texture
            List<TerrainLayer> terrainLayers = new List<TerrainLayer>();
            Dictionary<ushort, int> textureIndexToLayerIndex = new Dictionary<ushort, int>();

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
                    string textureDir = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Textures", _esm);
                    
                    // Try both NAME and DATA values, and also try with common Morrowind texture prefixes
                    List<string> namesToTry = new List<string>();
                    if (!string.IsNullOrEmpty(names.primary))
                    {
                        namesToTry.Add(names.primary);
                        // Try with common prefixes
                        namesToTry.Add("Tx_" + names.primary);
                        namesToTry.Add("tx_" + names.primary.ToLower());
                    }
                    if (!string.IsNullOrEmpty(names.fallback))
                    {
                        namesToTry.Add(names.fallback);
                        string fallbackBase = System.IO.Path.GetFileNameWithoutExtension(names.fallback);
                        namesToTry.Add(fallbackBase);
                    }
                    // Remove duplicates
                    namesToTry = namesToTry.Distinct().ToList();
                    
                    // Try to find the texture file (case-insensitive)
                    string texturePath = null;
                    string foundBaseName = null;
                    
                    if (Directory.Exists(textureDir))
                    {
                        string[] files = Directory.GetFiles(textureDir);
                        
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
                    
                    if (texturePath != null && File.Exists(texturePath))
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
                                }
                            }
                            
                            // Handle TGA files - convert to PNG if PNG doesn't exist
                            if (textureExtension == ".tga")
                            {
                                // Check if PNG version exists
                                string pngPath = System.IO.Path.ChangeExtension(texturePath, ".png");
                                if (!System.IO.File.Exists(pngPath))
                                {
                                    // Convert TGA to PNG
                                    UnityEngine.Debug.Log($"TGA file found but no PNG exists, converting: {texturePath}");
                                    byte[] tgaData = System.IO.File.ReadAllBytes(texturePath);
                                    Texture2D tempTexture = new Texture2D(2, 2);
                                    if (tempTexture.LoadImage(tgaData))
                                    {
                                        byte[] pngData = tempTexture.EncodeToPNG();
                                        System.IO.File.WriteAllBytes(pngPath, pngData);
                                        UnityEngine.Debug.Log($"Successfully converted TGA to PNG: {texturePath} -> {pngPath}");
                                        #if UNITY_EDITOR
                                        SetTextureImportSettings(pngPath);
                                        #endif
                                        texturePath = pngPath; // Use PNG instead
                                        textureExtension = ".png";
                                        foundBaseName = System.IO.Path.GetFileName(pngPath);
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

                                    UnityEngine.Debug.Log($"Loaded texture layer {terrainLayers.Count}: {foundBaseName ?? System.IO.Path.GetFileName(texturePath)} ({texture.width}x{texture.height})");
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
                    else
                    {
                        string triedNames = string.Join(", ", namesToTry);
                        UnityEngine.Debug.LogWarning($"Texture file not found for index {ltexIndex} (tried: {triedNames}) (searched in: {textureDir}), using placeholder");
                        usePlaceholder = true;
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
                    layer.tileSize = new Vector2(8, 8); // Tiling size for better texture appearance
                    layer.tileOffset = Vector2.zero;
                    
                    // Set metallic and smoothness to reduce shininess (URP terrain shader uses these)
                    layer.metallic = 0.0f; // No metallic
                    layer.smoothness = 0.1f; // Low smoothness to reduce shininess
                    
                    terrainLayers.Add(layer);
                    textureIndexToLayerIndex[vtexIndex] = terrainLayers.Count - 1;
                }
            }

            // Always create at least one layer (placeholder if needed)
            if (terrainLayers.Count == 0)
            {
                UnityEngine.Debug.LogWarning("No terrain layers found, creating default placeholder layer.");
                TerrainLayer defaultLayer = new TerrainLayer();
                defaultLayer.diffuseTexture = placeholderTexture;
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
            const int STEP = 64;
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
                
                // Start position: each cell is 65 pixels, but spaced 64 apart
                // The first cell starts at HALF (32) pixels from the origin
                // Offset adjustment: splat map was one cell too far on Z (positive) and two cells too far on X (negative)
                // Z positive = subtract from Y (since Y becomes Z after coordinate conversion)
                // X negative = add to X (move forward in positive X direction)
                int startX = HALF + cellOffsetX + (STEP/2)-2;  // Add 1 cells to fix X offset
                int startY = HALF + cellOffsetY - (STEP/2)-2;         // Subtract 1 cell to fix Z offset

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
                        // Each cell is 64x64, VTEX is 16x16, so each VTEX entry covers 64/16 = 4 terrain pixels
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

            // Step 9: Create Terrain GameObject
            GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            terrainObject.name = _esm + "_Terrain";
            Terrain terrain = terrainObject.GetComponent<Terrain>();
            terrain.heightmapPixelError = 5f;
            terrain.basemapDistance = 1000f;
            
            // Position terrain at its center (not at cell 0,0)
            // Terrain size is worldWidth x worldHeight, so center it at (-worldWidth/2, 0, -worldHeight/2)
            // Offset by half a cell (32 units) to center on cell center rather than cell corner
            // Morrowind terrain starts a quarter cell (16 units) lower so that sea level aligns properly
            // This allows the lowest parts of the map (which are underwater) to be below sea level
            const float HALF_CELL = 32f; // Half of 64 (cell size in world units) - should never change
            const float TERRAIN_Y_OFFSET = -16f; // Quarter cell lower (16 = 64/4) to align sea level
            float terrainPositionX = (-worldWidth / 2f + HALF_CELL)-2;
            float terrainPositionZ = (-worldHeight / 2f + HALF_CELL)-2;
            
            terrainObject.transform.position = new Vector3(terrainPositionX, TERRAIN_Y_OFFSET, terrainPositionZ);
            
            UnityEngine.Debug.Log($"Terrain positioned at center: ({terrainPositionX}, {TERRAIN_Y_OFFSET}, {terrainPositionZ}), terrain size: {worldWidth}x{worldHeight}, offset by {HALF_CELL} units to center on cell, Y offset {TERRAIN_Y_OFFSET} for sea level alignment");
            
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
            
            // Position water plane at terrain center (terrain center is offset by +32 units to center on cell)
            float waterCenterX = HALF_CELL; // Terrain center is offset by +32 units
            float waterCenterZ = HALF_CELL;
            waterPlane.transform.position = new Vector3(waterCenterX, waterY, waterCenterZ);
            waterPlane.transform.localScale = new Vector3(worldWidth * 0.1f, 1f, worldHeight * 0.1f);
            
            UnityEngine.Debug.Log($"Water plane positioned at ({waterCenterX}, {waterY}, {waterCenterZ}) to align with terrain center (offset by {HALF_CELL} units)");
            
            Renderer waterRenderer = waterPlane.GetComponent<Renderer>();
            
            // Try URP shaders first, fallback to Standard if URP not available
            Shader waterShader = Shader.Find("Universal Render Pipeline/Lit");
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
                // Fallback to Standard shader if URP shaders not found
                waterShader = Shader.Find("Standard");
            }
            
            Material waterMaterial = new Material(waterShader);
            
            // Set up transparent water material
            if (waterShader.name.Contains("Universal Render Pipeline"))
            {
                // URP shader setup
                waterMaterial.SetFloat("_Surface", 1); // Transparent surface
                waterMaterial.SetFloat("_Blend", 0); // Alpha blend
                waterMaterial.SetColor("_BaseColor", new Color(0.2f, 0.4f, 0.8f, 0.6f));
                waterMaterial.SetFloat("_Smoothness", 0.9f);
                waterMaterial.SetFloat("_Metallic", 0f);
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
            }
            
            waterRenderer.material = waterMaterial;
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
                    BlitCellNormals_NoSeams(npixels, width, height, cellx, celly, MinCellX, MaxCellX,MinCellY, MaxCellY, normdata /*byte[65][65]*/);
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
                        // Leave unwritten pixels as black (default) - these are transparent areas
                    }
                }
            }

            Directory.CreateDirectory(Application.dataPath + "/StreamingAssets/Data/UOMW");
            Directory.CreateDirectory(Application.dataPath + "/StreamingAssets/Data/UOMW/Cache");


            newTexture.SetPixels(pixels);
            newTexture.Apply();
            byte[] bytes = newTexture.EncodeToPNG(); // or texture2D.EncodeToJPG(quality)
            string filePath = Application.dataPath + "/StreamingAssets/Data/UOMW/Cache/"+ _esm + "_MapBumps.png"; // Or any desired path
            System.IO.File.WriteAllBytes(filePath, bytes);

            normTexture.SetPixels(npixels);
            normTexture.Apply();
            byte[] nbytes = normTexture.EncodeToPNG(); // or texture2D.EncodeToJPG(quality)
            string nfilePath = Application.dataPath + "/StreamingAssets/Data/UOMW/Cache/"+ _esm + "_MapNorms.png"; // Or any desired path
            System.IO.File.WriteAllBytes(nfilePath, nbytes);

            heightTexture.SetPixels(heightPixels);
            heightTexture.Apply();
            byte[] hbytes = heightTexture.EncodeToPNG(); // or texture2D.EncodeToJPG(quality)
            string hfilePath = Application.dataPath + "/StreamingAssets/Data/UOMW/Cache/"+ _esm + "_MapHeight.png"; // Or any desired path
            System.IO.File.WriteAllBytes(hfilePath, hbytes);

            colorTexture.SetPixels(colorPixels);
            colorTexture.Apply();
            byte[] cbytes = colorTexture.EncodeToPNG(); // or texture2D.EncodeToJPG(quality)
            string cfilePath = Application.dataPath + "/StreamingAssets/Data/UOMW/Cache/"+ _esm + "_MapColors.png"; // Or any desired path
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
                
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (globalHeightsCount[y][x] > 0)
                        {
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
                            // Unwritten pixels (missing cells): write 0 (sea level / black)
                            // This fills transparent areas with sea level to avoid cliffs in Unity terrain
                            rawBytes[byteIndex++] = 0;
                            rawBytes[byteIndex++] = 0;
                        }
                    }
                }
                
                string rawFilePath = Application.dataPath + "/StreamingAssets/Data/UOMW/Cache/"+ _esm + "_MapHeight.raw";
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
            const int STEP = 64;   // spacing between cell origins (critical)
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

        // Calculate cell heights from deltas and offset (without writing to global array)
        private float[][] CalculateCellHeights(sbyte[][] deltas, float offset)
        {
            const int CELL = 65;
            const float HEIGHT_MAP_SCALE_FACTOR = 8.0f;
            
            float[][] absoluteHeights = new float[CELL][];
            for (int i = 0; i < CELL; i++)
            {
                absoluteHeights[i] = new float[CELL];
            }
            
            // Reconstruct absolute heights from deltas and offset
            int heightInt = (int)offset;
            
            for (int y = 0; y < CELL; y++)
            {
                for (int x = 0; x < CELL; x++)
                {
                    heightInt += deltas[y][x];
                    absoluteHeights[y][x] = heightInt;
                }
                heightInt = (int)absoluteHeights[y][0];
            }
            
            // Apply scale factor
            for (int y = 0; y < CELL; y++)
            {
                for (int x = 0; x < CELL; x++)
                {
                    absoluteHeights[y][x] *= HEIGHT_MAP_SCALE_FACTOR;
                }
            }
            
            return absoluteHeights;
        }
        
        // Align cell boundaries with neighbors by adjusting boundary heights
        // Process cells in order (left-to-right, top-to-bottom) to propagate alignment
        private void AlignCellBoundaries(float[][] cellHeight, int cellX, int cellY, 
            Dictionary<(int x, int y), float[][]> allCellHeights,
            int minCellX, int maxCellX, int minCellY, int maxCellY)
        {
            const int CELL = 65;
            float totalAdjustment = 0f;
            int adjustmentCount = 0;
            
            // Check left neighbor: align left edge of this cell with right edge of left cell
            if (cellX > minCellX && allCellHeights.ContainsKey((cellX - 1, cellY)))
            {
                float[][] leftCell = allCellHeights[(cellX - 1, cellY)];
                // Average height along the shared edge (left cell's right edge, this cell's left edge)
                float leftEdgeAvg = 0f;
                float rightEdgeAvg = 0f;
                for (int y = 0; y < CELL; y++)
                {
                    leftEdgeAvg += leftCell[y][64]; // Left cell's right edge (x=64)
                    rightEdgeAvg += cellHeight[y][0]; // This cell's left edge (x=0)
                }
                leftEdgeAvg /= CELL;
                rightEdgeAvg /= CELL;
                float diff = leftEdgeAvg - rightEdgeAvg;
                totalAdjustment += diff;
                adjustmentCount++;
            }
            
            // Check top neighbor: align top edge of this cell with bottom edge of top cell
            if (cellY > minCellY && allCellHeights.ContainsKey((cellX, cellY - 1)))
            {
                float[][] topCell = allCellHeights[(cellX, cellY - 1)];
                // Average height along the shared edge (top cell's bottom edge, this cell's top edge)
                float topEdgeAvg = 0f;
                float bottomEdgeAvg = 0f;
                for (int x = 0; x < CELL; x++)
                {
                    topEdgeAvg += topCell[64][x]; // Top cell's bottom edge (y=64)
                    bottomEdgeAvg += cellHeight[0][x]; // This cell's top edge (y=0)
                }
                topEdgeAvg /= CELL;
                bottomEdgeAvg /= CELL;
                float diff = topEdgeAvg - bottomEdgeAvg;
                totalAdjustment += diff;
                adjustmentCount++;
            }
            
            // Apply average adjustment if we have any neighbors
            if (adjustmentCount > 0)
            {
                float adjustment = totalAdjustment / adjustmentCount;
                // Apply adjustment to entire cell
                for (int y = 0; y < CELL; y++)
                {
                    for (int x = 0; x < CELL; x++)
                    {
                        cellHeight[y][x] += adjustment;
                    }
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
            const float HEIGHT_MAP_SCALE_FACTOR = 8.0f;
            
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
                    // Align with bottom neighbor's top edge, but use row 62 instead of 64 to avoid edge artifacts
                    // The outermost rows (64, 63) might have artifacts, so use two pixels in
                    float[][] bottomCell = cellHeights[(cellX, cellY - 1)];
                    float neighborHeight = bottomCell[62][0]; // Use row 62 instead of 64 to avoid edge artifacts
                    heightInt = (int)Math.Round(neighborHeight / HEIGHT_MAP_SCALE_FACTOR);
                }
                else if (cellX > minCellX && cellHeights.ContainsKey((cellX - 1, cellY)))
                {
                    // Align with left neighbor, but use column 63 instead of 64 to avoid edge artifacts
                    // The outermost column (64) might have artifacts that propagate, so use one pixel in
                    float[][] leftCell = cellHeights[(cellX - 1, cellY)];
                    float neighborHeight = leftCell[y][63]; // Use column 63 instead of 64 to avoid edge artifacts
                    heightInt = (int)Math.Round(neighborHeight / HEIGHT_MAP_SCALE_FACTOR);
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
                    heightInt = (int)(prevRowFirstHeight / HEIGHT_MAP_SCALE_FACTOR + 0.5f);
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
                    absoluteHeights[y][x] *= HEIGHT_MAP_SCALE_FACTOR;
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
            const int STEP = 64;   // spacing between cell origins (critical)
            const int HALF = CELL / 2; // 32
            const float HEIGHT_MAP_SCALE_FACTOR = 8.0f; // TES3 height scale factor

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
                    absoluteHeights[y][x] *= HEIGHT_MAP_SCALE_FACTOR;
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

        private void CorrectHeightsUsingNormals(
            float[][] globalHeights, VNML[][] globalNormals, int[][] globalHeightsCount,
            int width, int height)
        {
            // Create a working copy for iterative smoothing
            float[][] smoothedHeights = new float[height][];
            for (int i = 0; i < height; i++)
            {
                smoothedHeights[i] = new float[width];
                for (int j = 0; j < width; j++)
                {
                    smoothedHeights[i][j] = globalHeights[i][j];
                }
            }
            
            // Multiple passes to smooth boundaries using normal-guided blending
            for (int pass = 0; pass < 5; pass++)
            {
                for (int y = 1; y < height - 1; y++)
                {
                    for (int x = 1; x < width - 1; x++)
                    {
                        if (globalHeightsCount[y][x] == 0) continue;
                        
                        float currentHeight = smoothedHeights[y][x];
                        float sumHeight = currentHeight;
                        float sumWeight = 1.0f;
                        
                        // Check all 4 neighbors
                        int[] dx = { -1, 1, 0, 0 };
                        int[] dy = { 0, 0, -1, 1 };
                        
                        for (int dir = 0; dir < 4; dir++)
                        {
                            int nx = x + dx[dir];
                            int ny = y + dy[dir];
                            
                            if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                            if (globalHeightsCount[ny][nx] == 0) continue;
                            
                            float neighborHeight = smoothedHeights[ny][nx];
                            float heightDiff = Mathf.Abs(neighborHeight - currentHeight);
                            
                            // If there's a large discontinuity (likely a cell boundary issue)
                            // Lower threshold to catch more subtle misalignments
                            if (heightDiff > 20.0f) // Threshold for detecting cell boundary issues
                            {
                                VNML n = globalNormals[y][x];
                                if (n != null)
                                {
                                    // Decode normal
                                    float nx_comp = n.x / 127.0f;
                                    float ny_comp = n.y / 127.0f;
                                    float nz_comp = n.z / 127.0f;
                                    
                                    // Normalize
                                    float len = Mathf.Sqrt(nx_comp * nx_comp + ny_comp * ny_comp + nz_comp * nz_comp);
                                    if (len > 0.001f)
                                    {
                                        // Use normal to determine if we should blend
                                        // If normal suggests a gentle slope, blend more aggressively
                                        // If normal suggests a steep slope, be more conservative
                                        float slopeFactor = 1.0f - Mathf.Abs(nz_comp); // Higher for steeper slopes
                                        
                                        // Blend factor based on normal and discontinuity size
                                        // Increase blending strength for better seam removal
                                        float blendWeight = 0.35f * (1.0f - slopeFactor * 0.5f);
                                        blendWeight = Mathf.Clamp01(blendWeight);
                                        
                                        sumHeight += neighborHeight * blendWeight;
                                        sumWeight += blendWeight;
                                    }
                                }
                                else
                                {
                                    // No normal available, use simple averaging with stronger blend
                                    sumHeight += neighborHeight * 0.25f;
                                    sumWeight += 0.25f;
                                }
                            }
                        }
                        
                        // Update height with weighted average
                        if (sumWeight > 1.0f)
                        {
                            smoothedHeights[y][x] = sumHeight / sumWeight;
                        }
                    }
                }
                
                // Copy back for next iteration
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        globalHeights[y][x] = smoothedHeights[y][x];
                    }
                }
            }
        }

        private void AdjustCellOffsetsSimple(
            Dictionary<(int x, int y), (float offset, sbyte[][] deltas)> cellData,
            int minCellX, int maxCellX, int minCellY, int maxCellY)
        {
            const float HEIGHT_SCALE = 8.0f;
            const int CELL = 65;
            
            // Simple approach: use first cell as reference, adjust others to minimize edge discontinuities
            // Find first cell to use as reference
            int refCellX = minCellX;
            int refCellY = minCellY;
            if (!cellData.ContainsKey((refCellX, refCellY)))
            {
                foreach (var kvp in cellData)
                {
                    refCellX = kvp.Key.x;
                    refCellY = kvp.Key.y;
                    break;
                }
            }
            
            if (!cellData.ContainsKey((refCellX, refCellY))) return;
            
            // Multiple passes to iteratively align cells
            for (int pass = 0; pass < 3; pass++)
            {
                Dictionary<(int, int), float> adjustments = new Dictionary<(int, int), float>();
                
                foreach (var kvp in cellData)
                {
                    int cellX = kvp.Key.x;
                    int cellY = kvp.Key.y;
                    if (cellX == refCellX && cellY == refCellY) continue; // Don't adjust reference
                    
                    float currentOffset = kvp.Value.offset;
                    sbyte[][] deltas = kvp.Value.deltas;
                    
                    float totalAdjustment = 0.0f;
                    int neighborCount = 0;
                    
                    // Check left neighbor - align right edge of left cell with left edge of this cell
                    if (cellData.ContainsKey((cellX - 1, cellY)))
                    {
                        var leftData = cellData[(cellX - 1, cellY)];
                        float leftOffset = leftData.offset;
                        sbyte[][] leftDeltas = leftData.deltas;
                        
                        // Calculate left neighbor's height at its right edge (x=63, since we skip 64)
                        float leftHeight = leftOffset;
                        for (int y = 0; y < CELL; y++)
                        {
                            for (int x = 0; x < CELL - 1; x++) // Skip last column (owned by neighbor)
                            {
                                leftHeight += leftDeltas[y][x];
                            }
                            if (y == 0) break; // Just first row for alignment
                        }
                        
                        // Calculate this cell's height at its left edge (x=0)
                        float thisHeight = currentOffset;
                        for (int y = 0; y < CELL; y++)
                        {
                            thisHeight += deltas[y][0];
                            if (y == 0) break; // Just first row
                        }
                        
                        // Calculate adjustment needed (divide by scale since we'll scale later)
                        float adjustment = (leftHeight - thisHeight) / HEIGHT_SCALE;
                        totalAdjustment += adjustment;
                        neighborCount++;
                    }
                    
                    // Check top neighbor - align bottom edge of top cell with top edge of this cell
                    if (cellData.ContainsKey((cellX, cellY - 1)))
                    {
                        var topData = cellData[(cellX, cellY - 1)];
                        float topOffset = topData.offset;
                        sbyte[][] topDeltas = topData.deltas;
                        
                        // Calculate top neighbor's height at its bottom edge (y=63)
                        float topHeight = topOffset;
                        for (int y = 0; y < CELL - 1; y++) // Skip last row
                        {
                            for (int x = 0; x < CELL; x++)
                            {
                                topHeight += topDeltas[y][x];
                            }
                        }
                        
                        // Calculate this cell's height at its top edge (y=0, first row)
                        float thisHeight = currentOffset;
                        for (int y = 0; y < 1; y++)
                        {
                            for (int x = 0; x < CELL; x++)
                            {
                                thisHeight += deltas[y][x];
                            }
                        }
                        
                        // Calculate adjustment needed
                        float adjustment = (topHeight - thisHeight) / HEIGHT_SCALE;
                        totalAdjustment += adjustment;
                        neighborCount++;
                    }
                    
                    // Average adjustments from neighbors
                    if (neighborCount > 0)
                    {
                        float avgAdjustment = totalAdjustment / neighborCount;
                        // Apply gentle adjustment (20% per pass)
                        adjustments[(cellX, cellY)] = avgAdjustment * 0.2f;
                    }
                }
                
                // Apply adjustments
                foreach (var adj in adjustments)
                {
                    var key = adj.Key;
                    if (cellData.ContainsKey(key))
                    {
                        var data = cellData[key];
                        cellData[key] = (data.offset + adj.Value, data.deltas);
                    }
                }
            }
        }


    }
}