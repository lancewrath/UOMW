using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BSASharp;
using Niflib;
using Pfim;
using ESMSharp.TES3;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ESMSharp.NIF
{
    /// <summary>
    /// Loads Morrowind NIF (NetImmerse) files and converts them to Unity GameObjects
    /// </summary>
    public class NIFLoader
    {
        private string _esm = "";
        private string _bsa = "";
        private BSA _bsaArchive = null;
        
        /// <summary>
        /// Gets the ESM name (for async loading access)
        /// </summary>
        public string ESMName { get { return _esm; } }

        public NIFLoader(string esm = "Morrowind", string bsa = "Morrowind.bsa")
        {
            _esm = System.IO.Path.GetFileNameWithoutExtension(esm);
            _bsa = bsa;
        }

        /// <summary>
        /// Loads a NIF file and creates a Unity GameObject with mesh and materials
        /// </summary>
        public GameObject LoadNIF(string nifPath, bool combineMeshes = false)
        {
            if (!File.Exists(nifPath))
            {
                UnityEngine.Debug.LogError($"NIF file not found: {nifPath}");
                return null;
            }

            try
            {
                byte[] nifData = File.ReadAllBytes(nifPath);
                return LoadNIFFromBytes(nifData, System.IO.Path.GetFileName(nifPath), combineMeshes);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error loading NIF file {nifPath}: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Combines multiple NIF meshes into a single Unity mesh with submeshes
        /// Each submesh can have its own material/texture
        /// </summary>
        private (Mesh mesh, Material[] materials) CombineMeshes(List<NIFMesh> meshes, float vertexScale = 1.0f, Quaternion rotation = default(Quaternion), string esm = "", bool isTree = false)
        {
            if (meshes == null || meshes.Count == 0)
                return (null, null);

            // Note: Vertices are already transformed by NiNode transforms and coordinate system conversion in ExtractMeshFromTriShape
            // We only need to apply vertex scaling here (rotation parameter is kept for backward compatibility but not used)

            List<Vector3> combinedVertices = new List<Vector3>();
            List<Vector2> combinedUVs = new List<Vector2>();
            List<Vector3> combinedNormals = new List<Vector3>();
            List<Color> combinedColors = new List<Color>();
            
            // Store triangles per submesh
            List<List<int>> submeshTriangles = new List<List<int>>();
            List<Material> submeshMaterials = new List<Material>();

            int vertexOffset = 0;

            foreach (NIFMesh nifMesh in meshes)
            {
                if (nifMesh.Vertices == null || nifMesh.Vertices.Count == 0)
                    continue;

                // Vertices are already transformed by NiNode transforms and coordinate system conversion
                // We only need to apply vertex scaling here
                foreach (Vector3 vertex in nifMesh.Vertices)
                {
                    Vector3 scaledVertex = new Vector3(
                        vertex.x * vertexScale,
                        vertex.y * vertexScale,
                        vertex.z * vertexScale
                    );
                    combinedVertices.Add(scaledVertex);
                }

                // Store triangles for this submesh with vertex offset
                List<int> submeshTris = new List<int>();
                if (nifMesh.Triangles != null && nifMesh.Triangles.Count > 0)
                {
                    foreach (int index in nifMesh.Triangles)
                    {
                        submeshTris.Add(index + vertexOffset);
                    }
                }
                submeshTriangles.Add(submeshTris);

                // Add UVs (pad with Vector2.zero if missing)
                if (nifMesh.UVs != null && nifMesh.UVs.Count == nifMesh.Vertices.Count)
                {
                    combinedUVs.AddRange(nifMesh.UVs);
                }
                else
                {
                    // Pad with zero UVs
                    for (int i = 0; i < nifMesh.Vertices.Count; i++)
                    {
                        combinedUVs.Add(Vector2.zero);
                    }
                }

                // Add normals with rotation applied (recalculate if missing)
                if (nifMesh.Normals != null && nifMesh.Normals.Count == nifMesh.Vertices.Count)
                {
                    // Normals are already transformed by NiNode transforms and coordinate system conversion
                    // Just add them directly
                    combinedNormals.AddRange(nifMesh.Normals);
                }
                else
                {
                    // Pad with zero normals (will recalculate later)
                    for (int i = 0; i < nifMesh.Vertices.Count; i++)
                    {
                        combinedNormals.Add(Vector3.zero);
                    }
                }

                // Add colors (pad with white if missing)
                if (nifMesh.Colors != null && nifMesh.Colors.Count == nifMesh.Vertices.Count)
                {
                    combinedColors.AddRange(nifMesh.Colors);
                }
                else
                {
                    // Pad with white colors
                    for (int i = 0; i < nifMesh.Vertices.Count; i++)
                    {
                        combinedColors.Add(Color.white);
                    }
                }

                // Create material for this submesh using per-mesh textures and material
                Material submeshMat = CreateMaterialFromNIF(nifMesh.Material, nifMesh.Textures, esm, isTree: isTree);
                submeshMaterials.Add(submeshMat);

                vertexOffset += nifMesh.Vertices.Count;
            }

            if (combinedVertices.Count == 0)
                return (null, null);

            // Create combined mesh with submeshes
            Mesh combinedMesh = new Mesh();
            combinedMesh.name = "CombinedMesh";
            combinedMesh.vertices = combinedVertices.ToArray();
            combinedMesh.uv = combinedUVs.ToArray();

            // Recalculate normals if needed
            bool needsRecalc = false;
            for (int i = 0; i < combinedNormals.Count; i++)
            {
                if (combinedNormals[i] == Vector3.zero)
                {
                    needsRecalc = true;
                    break;
                }
            }

            if (needsRecalc || combinedNormals.Count != combinedVertices.Count)
            {
                combinedMesh.RecalculateNormals();
            }
            else
            {
                combinedMesh.normals = combinedNormals.ToArray();
            }

            // Set colors if we have them
            if (combinedColors.Count == combinedVertices.Count)
            {
                combinedMesh.colors = combinedColors.ToArray();
            }

            // Set up submeshes
            combinedMesh.subMeshCount = submeshTriangles.Count;
            for (int i = 0; i < submeshTriangles.Count; i++)
            {
                combinedMesh.SetTriangles(submeshTriangles[i], i);
            }

            combinedMesh.RecalculateBounds();

            return (combinedMesh, submeshMaterials.ToArray());
        }

        /// <summary>
        /// Loads a NIF file from cached models directory
        /// </summary>
        public GameObject LoadNIFFromCache(string modelFilename, bool combineMeshes = false, bool isTreeOrGrass = false)
        {
            // Normalize path separators - ensure forward slashes are used consistently
            // Path.Combine will use the correct separator for the OS, but we need to normalize the filename first
            string normalizedFilename = modelFilename?.Replace('\\', '/');
            normalizedFilename = System.IO.Path.GetFileName(normalizedFilename); // Get just the filename, ignore any path
            
            string cachePath = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", _esm, normalizedFilename);
            // Normalize path separators for consistency (Windows uses backslashes, but we want forward slashes)
            cachePath = cachePath.Replace('\\', '/');
            
            // Also try with original filename in case normalization changed it
            if (!File.Exists(cachePath))
            {
                // Try original filename
                string originalPath = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", _esm, modelFilename);
                originalPath = originalPath.Replace('\\', '/');
                if (File.Exists(originalPath))
                {
                    cachePath = originalPath;
                }
                else
                {
                    // Try case-insensitive search in cache directory
                    string cacheDir = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", _esm);
                    cacheDir = cacheDir.Replace('\\', '/');
                    if (Directory.Exists(cacheDir))
                    {
                        string[] files = Directory.GetFiles(cacheDir, "*.nif", SearchOption.TopDirectoryOnly);
                        string searchFilename = normalizedFilename ?? modelFilename;
                        foreach (string file in files)
                        {
                            string fileName = System.IO.Path.GetFileName(file);
                            if (string.Equals(fileName, searchFilename, System.StringComparison.OrdinalIgnoreCase))
                            {
                                cachePath = file;
                                break;
                            }
                        }
                    }
                    
                    if (!File.Exists(cachePath))
                    {
                        // Normalize path separators in error message for consistency
                        string normalizedCachePath = cachePath.Replace('\\', '/');
                        // Use LogWarning instead of LogError - missing files are not fatal, loading should continue
                        UnityEngine.Debug.LogWarning($"NIF file not found: {normalizedCachePath} (searched for: {modelFilename}, normalized: {normalizedFilename})");
                        return null;
                    }
                }
            }

            try
            {
                byte[] nifData = File.ReadAllBytes(cachePath);
                return LoadNIFFromBytes(nifData, System.IO.Path.GetFileName(cachePath), combineMeshes, isTreeOrGrass);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error loading NIF file {cachePath}: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Loads a NIF file from byte array
        /// </summary>
        internal GameObject LoadNIFFromBytes(byte[] nifData, string filename, bool combineMeshes = false, bool isTreeOrGrass = false)
        {
            // Check if this is a skeleton file (base_anim.nif, etc.)
            // Skeleton files need special handling to create bone hierarchy
            bool isSkeletonFile = filename.Contains("base_anim", StringComparison.OrdinalIgnoreCase) ||
                                   filename.Contains("baseanim", StringComparison.OrdinalIgnoreCase) ||
                                   filename.Contains("skeleton", StringComparison.OrdinalIgnoreCase);
            
            if (isSkeletonFile)
            {
                try
                {
                    return LoadSkeletonFromBytesNiflib(nifData, filename);
                }
                catch (System.Exception ex)
                {
                    UnityEngine.Debug.LogError($"Failed to load skeleton file {filename}: {ex.Message}\n{ex.StackTrace}");
                    return null;
                }
            }
            
            // Use niflib.net (primary loader) for regular files
            // No fallback - if niflib.net fails, we want to see the error and fix it
            try
            {
                return LoadNIFFromBytesNiflib(nifData, filename, combineMeshes, isTreeOrGrass);
            }
            catch (System.Exception ex)
            {
                // Log full exception details including inner exception
                string errorDetails = $"niflib.net failed for {filename}:\n";
                errorDetails += $"Exception Type: {ex.GetType().Name}\n";
                errorDetails += $"Message: {ex.Message}\n";
                errorDetails += $"Stack Trace: {ex.StackTrace}\n";
                
                if (ex.InnerException != null)
                {
                    errorDetails += $"\nInner Exception Type: {ex.InnerException.GetType().Name}\n";
                    errorDetails += $"Inner Message: {ex.InnerException.Message}\n";
                    errorDetails += $"Inner Stack Trace: {ex.InnerException.StackTrace}\n";
                }
                
                UnityEngine.Debug.LogError(errorDetails);
                
                // Return null - no fallback, so we can focus on fixing niflib.net
                return null;
            }
        }

        /// <summary>
        /// Creates a Unity material from NIF material and texture data
        /// </summary>
        private Material CreateMaterialFromNIF(NIFMaterial nifMaterial, Dictionary<string, NIFTexture> textures, string esm, bool isTree = false)
        {
            Material material = CreateURPMaterial("NIFMaterial", null, null, nifMaterial.DiffuseColor, isTree: isTree);
            bool textureLoaded = false;

            // Load textures
            if (textures.ContainsKey("BaseTexture") || textures.ContainsKey("Diffuse"))
            {
                NIFTexture baseTex = textures.ContainsKey("BaseTexture") ? textures["BaseTexture"] : textures["Diffuse"];
                if (baseTex.Enabled && !string.IsNullOrEmpty(baseTex.FilePath))
                {
                    //UnityEngine.Debug.Log($"Loading texture for material: {baseTex.FilePath}");
                    var (diffuseTexture, textureEntry) = LoadTextureForModelWithEntry(baseTex.FilePath, esm);
                    if (diffuseTexture != null)
                    {
                        //UnityEngine.Debug.Log($"Successfully loaded texture: {baseTex.FilePath} ({diffuseTexture.width}x{diffuseTexture.height})");
                        if (isTree && (material.shader.name.Contains("Speedtree") || material.shader.name.Contains("SpeedTree") || material.shader.name.Contains("Nature")))
                        {
                            // URP/Nature/Speedtree9_URP or Nature shader uses _MainTex
                            material.SetTexture("_MainTex", diffuseTexture);
                        }
                        else if (material.shader.name.Contains("Universal Render Pipeline"))
                        {
                            material.SetTexture("_BaseMap", diffuseTexture);
                        }
                        else
                        {
                            material.SetTexture("_MainTex", diffuseTexture);
                        }
                        
                        // Get or generate normal map for this texture
                        if (textureEntry != null)
                        {
                            Texture2D normalMap = TESLTextureLibrary.GetOrGenerateNormalMap(textureEntry);
                            if (normalMap != null)
                            {
                                // Apply normal map to material
                                if (material.shader.name.Contains("Universal Render Pipeline"))
                                {
                                    material.SetTexture("_BumpMap", normalMap);
                                    material.EnableKeyword("_NORMALMAP");
                                }
                                else
                                {
                                    material.SetTexture("_BumpMap", normalMap);
                                }
                            }
                        }
                        
                        textureLoaded = true;
                    }
                    else
                    {
                        //UnityEngine.Debug.LogWarning($"Failed to load texture: {baseTex.FilePath}, creating invisible material");
                    }
                }
                else
                {
                    //UnityEngine.Debug.LogWarning($"BaseTexture has empty or disabled path. Enabled: {baseTex?.Enabled}, Path: {baseTex?.FilePath}, creating invisible material");
                }
            }
            else
            {
                //UnityEngine.Debug.LogWarning($"No BaseTexture or Diffuse texture found in textures dictionary. Available keys: {string.Join(", ", textures.Keys)}, creating invisible material");
            }

            // If no texture was loaded, make the material invisible (for editor-specific meshes)
            if (!textureLoaded)
            {
                if (material.shader.name.Contains("Universal Render Pipeline"))
                {
                    material.SetFloat("_Surface", 1); // Transparent
                    material.SetFloat("_Blend", 0); // Alpha blend
                    Color baseColor = material.GetColor("_BaseColor");
                    baseColor.a = 0f; // Fully transparent (invisible)
                    material.SetColor("_BaseColor", baseColor);
                    material.renderQueue = 3000; // Transparent queue
                }
                else
                {
                    material.SetFloat("_Mode", 3); // Transparent mode
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0);
                    material.EnableKeyword("_ALPHABLEND_ON");
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.renderQueue = 3000;
                    Color baseColor = material.GetColor("_Color");
                    baseColor.a = 0f; // Fully transparent (invisible)
                    material.SetColor("_Color", baseColor);
                }
                return material; // Return invisible material early
            }

            // Set material properties (only if texture was loaded)
            if (isTree)
            {
                if (material.shader.name.Contains("Speedtree") || material.shader.name.Contains("SpeedTree") || material.shader.name.Contains("Nature"))
                {
                    // SpeedTree/Nature shader properties
                    material.SetColor("_Color", nifMaterial.DiffuseColor);
                    // Ensure alpha clipping is enabled for trees
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.SetFloat("_Cutoff", 0.5f);
                }
                else if (material.shader.name.Contains("Universal Render Pipeline"))
                {
                    // URP Lit shader - use alpha clipping mode
                    material.SetColor("_BaseColor", nifMaterial.DiffuseColor);
                    material.SetFloat("_Metallic", 0.0f);
                    material.SetFloat("_Smoothness", nifMaterial.Glossiness);
                    
                    // Enable alpha clipping for URP Lit
                    material.SetFloat("_Surface", 0); // Opaque surface mode
                    material.SetFloat("_AlphaClip", 1); // Enable alpha clipping
                    material.SetFloat("_Cutoff", 0.5f); // Alpha threshold
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.renderQueue = 2450; // AlphaTest queue
                }
                else
                {
                    // Standard shader fallback
                    material.SetColor("_Color", nifMaterial.DiffuseColor);
                    material.SetFloat("_Metallic", 0.0f);
                    material.SetFloat("_Glossiness", nifMaterial.Glossiness);
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.SetFloat("_Cutoff", 0.5f);
                }
            }
            else if (material.shader.name.Contains("Universal Render Pipeline"))
            {
                material.SetColor("_BaseColor", nifMaterial.DiffuseColor);
                material.SetFloat("_Metallic", 0.0f);
                material.SetFloat("_Smoothness", nifMaterial.Glossiness);
            }
            else
            {
                material.SetColor("_Color", nifMaterial.DiffuseColor);
                material.SetFloat("_Metallic", 0.0f);
                material.SetFloat("_Glossiness", nifMaterial.Glossiness);
            }

            // Handle alpha
            if (nifMaterial.HasAlpha && nifMaterial.Alpha < 1.0f)
            {
                if (material.shader.name.Contains("Universal Render Pipeline"))
                {
                    material.SetFloat("_Surface", 1); // Transparent
                    material.SetFloat("_Blend", 0); // Alpha blend
                    Color baseColor = material.GetColor("_BaseColor");
                    baseColor.a = nifMaterial.Alpha;
                    material.SetColor("_BaseColor", baseColor);
                    material.renderQueue = 3000; // Transparent queue
                }
                else
                {
                    material.SetFloat("_Mode", 3); // Transparent mode
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0);
                    material.EnableKeyword("_ALPHABLEND_ON");
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.renderQueue = 3000;
                }
            }

            return material;
        }

        /// <summary>
        /// Loads a texture for a NIF model, checking cache first, then extracting from BSA if needed
        /// Returns the texture and the texture entry (for normal map generation)
        /// </summary>
        private (Texture2D texture, TESLTextureLibrary.TextureEntry entry) LoadTextureForModelWithEntry(string texturePath, string esm = "Morrowind")
        {
            if (string.IsNullOrEmpty(texturePath))
                return (null, null);

            // Clean the texture path
            texturePath = texturePath.TrimEnd('\0', ' ', '\t', '\r', '\n');
            texturePath = texturePath.Replace("\0", "");
            texturePath = texturePath.Trim();

            string baseFilename = System.IO.Path.GetFileName(texturePath);
            string baseNameNoExt = System.IO.Path.GetFileNameWithoutExtension(baseFilename);
            
            // Check all ESM cache directories (not just the one passed in)
            string[] loadedESMs = TESESMLibrary.GetLoadedESMFilenames();
            string foundTexturePath = null;

            // Try to find texture in cache across all ESM cache directories
            string[] extensions = new[] { ".png", ".dds", ".tga" };
            string originalExt = System.IO.Path.GetExtension(baseFilename);
            if (!string.IsNullOrEmpty(originalExt) && !extensions.Contains(originalExt.ToLower()))
            {
                extensions = new[] { originalExt.ToLower() }.Concat(extensions).ToArray();
            }

            // Try preferred ESM first if specified
            if (!string.IsNullOrEmpty(esm))
            {
                string preferredTextureDir = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Textures", esm);
                foreach (string ext in extensions)
                {
                    string testPath = System.IO.Path.Combine(preferredTextureDir, baseNameNoExt + ext);
                    if (File.Exists(testPath))
                    {
                        foundTexturePath = testPath;
                        break;
                    }
                }
            }
            
            // Check all other ESM cache directories if not found
            if (foundTexturePath == null)
            {
                foreach (string esmFilename in loadedESMs)
                {
                    string esmName = System.IO.Path.GetFileNameWithoutExtension(esmFilename);
                    // Skip if we already checked this one
                    if (!string.IsNullOrEmpty(esm) && string.Equals(esmName, esm, StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    string textureDir = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Textures", esmName);
            foreach (string ext in extensions)
            {
                string testPath = System.IO.Path.Combine(textureDir, baseNameNoExt + ext);
                if (File.Exists(testPath))
                {
                    foundTexturePath = testPath;
                    break;
                        }
                    }
                    if (foundTexturePath != null) break;
                }
            }

            // If not found in cache, try to extract from BSA
            if (foundTexturePath == null)
            {
                // Use preferred ESM's cache directory for extraction, or first loaded ESM
                string preferredESM = esm;
                if (string.IsNullOrEmpty(preferredESM) && loadedESMs.Length > 0)
                {
                    preferredESM = System.IO.Path.GetFileNameWithoutExtension(loadedESMs[0]);
                }
                string textureDir = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Textures", preferredESM ?? "Morrowind");
                foundTexturePath = ExtractTextureFromBSA(texturePath, textureDir, preferredESM);
            }

            if (foundTexturePath != null)
            {
                // Check global texture library first
                TESLTextureLibrary.TextureEntry cachedEntry = TESLTextureLibrary.GetTextureByPath(foundTexturePath);
                if (cachedEntry != null)
                {
                    return (cachedEntry.Texture, cachedEntry);
                }
                
                // Load from file and add to library
                Texture2D texture = LoadTextureFromFile(foundTexturePath);
                if (texture != null)
                {
                    // Add to global texture library (no LTEX/VTEX index for model textures)
                    cachedEntry = TESLTextureLibrary.AddTexture(baseNameNoExt, -1, 0, foundTexturePath, texture);
                    // Generate normal map for this texture
                    if (cachedEntry != null)
                    {
                        TESLTextureLibrary.GetOrGenerateNormalMap(cachedEntry);
                    }
                }
                return (texture, cachedEntry);
            }

            //UnityEngine.Debug.LogWarning($"Texture not found: {texturePath}");
            return (null, null);
        }
        
        /// <summary>
        /// Loads a texture for a NIF model, checking cache first, then extracting from BSA if needed
        /// </summary>
        private Texture2D LoadTextureForModel(string texturePath, string esm = "Morrowind")
        {
            var (texture, entry) = LoadTextureForModelWithEntry(texturePath, esm);
            return texture;
        }

        /// <summary>
        /// Extracts a texture from BSA archives (searches all BSAs using TESBSALibrary)
        /// </summary>
        private string ExtractTextureFromBSA(string texturePath, string outputDir, string esm)
        {
            // Use TESBSALibrary to search across all BSAs (same approach as model extraction)
            HashSet<string> allBSAFiles = TESBSALibrary.GetAllFileNames();

            // Normalize the texture path first
            string normalizedPath = NormalizeTexturePath(texturePath);
            
            // Try multiple path variations (similar to terrain texture extraction and MWGE approach)
            // MWGE tries: "textures\filename", "data files\textures\filename", and variations with DDS extension
            string normalizedBaseFilename = System.IO.Path.GetFileName(normalizedPath);
            string normalizedBaseNameNoExt = System.IO.Path.GetFileNameWithoutExtension(normalizedBaseFilename);
            string originalExt = System.IO.Path.GetExtension(normalizedPath);
            
            // Build path variations - try with and without "textures\" prefix
            List<string> pathVariations = new List<string>();
            
            // Original path variations
            pathVariations.Add(normalizedPath);
            pathVariations.Add(normalizedPath.Replace('/', '\\'));
            pathVariations.Add(normalizedPath.Replace('\\', '/'));
            
            // With "textures\" prefix (MWGE style)
            if (!normalizedPath.StartsWith("textures\\") && !normalizedPath.StartsWith("textures/"))
            {
                pathVariations.Add("textures\\" + normalizedPath);
                pathVariations.Add("textures/" + normalizedPath);
                pathVariations.Add("Textures\\" + normalizedPath);
                pathVariations.Add("Textures/" + normalizedPath);
                pathVariations.Add("TEXTURES\\" + normalizedPath);
                pathVariations.Add("TEXTURES/" + normalizedPath);
            }
            
            // Try DDS extension variations (MWGE prefers DDS)
            if (!string.IsNullOrEmpty(originalExt) && originalExt.ToLower() != ".dds")
            {
                string ddsPath = System.IO.Path.ChangeExtension(normalizedPath, ".dds");
                pathVariations.Add(ddsPath);
                pathVariations.Add(ddsPath.Replace('/', '\\'));
                pathVariations.Add("textures\\" + ddsPath);
                pathVariations.Add("textures/" + ddsPath);
            }
            
            string foundPath = null;

            // Search in HashSet first (faster, case-insensitive)
            foreach (string pathVar in pathVariations)
            {
                if (allBSAFiles.Contains(pathVar))
                {
                    foundPath = pathVar;
                    break;
                }
            }

            // Case-insensitive fallback - search by filename
            if (foundPath == null)
            {
                foreach (string bsaFileName in allBSAFiles)
                {
                    string bsaBaseName = System.IO.Path.GetFileName(bsaFileName);
                    if (bsaBaseName.Equals(normalizedBaseFilename, StringComparison.OrdinalIgnoreCase))
                {
                        foundPath = bsaFileName;
                        break;
                    }
                }
            }
            
            // If still not found, try partial match (filename might have different extension)
            if (foundPath == null)
            {
                foreach (string bsaFileName in allBSAFiles)
                {
                    string bsaBaseNameNoExt = System.IO.Path.GetFileNameWithoutExtension(bsaFileName);
                    if (bsaBaseNameNoExt.Equals(normalizedBaseNameNoExt, StringComparison.OrdinalIgnoreCase))
                    {
                        foundPath = bsaFileName;
                        break;
                    }
                }
            }

            if (foundPath != null)
            {
                // Determine which ESM's BSA contains this file
                var esmEntries = TESESMLibrary.GetLoadedESMEntries();
                string sourceESM = null;
                string exactBSAPath = foundPath; // Use the found path as default
                
                // Try path variations to find the exact path format in the BSA
                string[] bsaPathVariations = new[]
                {
                    foundPath,
                    foundPath.Replace('/', '\\'),
                    foundPath.Replace('\\', '/')
                };
                
                foreach (var esmEntry in esmEntries)
                {
                    var bsaEntry = TESBSALibrary.GetBSAEntryForESM(esmEntry.ESMFilename);
                    if (bsaEntry != null && bsaEntry.IsLoaded)
                    {
                        // Try path variations
                        foreach (string pathVar in bsaPathVariations)
                        {
                            if (bsaEntry.FileNames.Contains(pathVar))
                            {
                                exactBSAPath = pathVar;
                                sourceESM = System.IO.Path.GetFileNameWithoutExtension(esmEntry.ESMFilename);
                                break;
                            }
                        }
                        
                        if (!string.IsNullOrEmpty(sourceESM)) break;
                        
                        // If not found with path variations, try filename-only match
                        string foundBaseFilename = System.IO.Path.GetFileName(foundPath);
                        foreach (string bsaFileName in bsaEntry.FileNames)
                        {
                            if (System.IO.Path.GetFileName(bsaFileName).Equals(foundBaseFilename, StringComparison.OrdinalIgnoreCase))
                            {
                                exactBSAPath = bsaFileName;
                                sourceESM = System.IO.Path.GetFileNameWithoutExtension(esmEntry.ESMFilename);
                                break;
                            }
                        }
                        
                        if (!string.IsNullOrEmpty(sourceESM)) break;
                    }
                }
                
                // If we couldn't determine source ESM, use the provided esm parameter or first loaded
                if (string.IsNullOrEmpty(sourceESM))
                {
                    sourceESM = esm;
                    if (string.IsNullOrEmpty(sourceESM) && esmEntries.Length > 0)
                    {
                        sourceESM = System.IO.Path.GetFileNameWithoutExtension(esmEntries[0].ESMFilename);
                    }
                }
                
                // Extract to the correct ESM cache directory
                string targetCacheDir = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Textures", sourceESM);
                System.IO.Directory.CreateDirectory(targetCacheDir);
                
                // Determine output filename based on actual BSA file extension
                string actualExt = System.IO.Path.GetExtension(exactBSAPath).ToLower();
                if (string.IsNullOrEmpty(actualExt))
                {
                    actualExt = System.IO.Path.GetExtension(foundPath).ToLower();
                }
                string outputPath = System.IO.Path.Combine(targetCacheDir, normalizedBaseNameNoExt + actualExt);
                
                // Use TESBSALibrary to extract with the exact BSA path
                // Try preferred ESM first, then all others
                bool extractSuccess = false;
                if (!string.IsNullOrEmpty(sourceESM))
                {
                    extractSuccess = TESBSALibrary.ExtractFile(exactBSAPath, outputPath, sourceESM + ".esm");
                }
                
                // If that failed, try without preferred ESM (searches all BSAs)
                if (!extractSuccess)
                {
                    extractSuccess = TESBSALibrary.ExtractFile(exactBSAPath, outputPath, null);
                }
                
                if (extractSuccess)
                {
                        // If it's a DDS, try to convert to PNG
                    if (actualExt == ".dds")
                        {
                        try
                        {
                            byte[] ddsData = System.IO.File.ReadAllBytes(outputPath);
                            string pngPath = System.IO.Path.ChangeExtension(outputPath, ".png");
                            if (ConvertDDSToPNG(ddsData, pngPath))
                            {
                                #if UNITY_EDITOR
                                SetTextureImportSettings(pngPath);
                                #endif
                                //UnityEngine.Debug.Log($"Extracted and converted DDS to PNG: {foundPath} -> {System.IO.Path.GetFileName(pngPath)} (from {sourceESM})");
                                return pngPath; // Return PNG path instead
                            }
                        }
                        catch (Exception ex)
                        {
                            UnityEngine.Debug.LogWarning($"Error converting extracted DDS to PNG: {ex.Message}");
                        }
                    }
                    else if (actualExt == ".tga")
                    {
                        try
                        {
                            // Convert TGA to PNG
                            byte[] tgaData = System.IO.File.ReadAllBytes(outputPath);
                            string pngPath = System.IO.Path.ChangeExtension(outputPath, ".png");
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
                                //UnityEngine.Debug.Log($"Extracted and converted TGA to PNG: {foundPath} -> {System.IO.Path.GetFileName(pngPath)} (from {sourceESM})");
                                return pngPath; // Return PNG path instead
                            }
                            UnityEngine.Object.DestroyImmediate(tempTexture);
                        }
                        catch (Exception ex)
                        {
                            UnityEngine.Debug.LogWarning($"Error converting extracted TGA to PNG: {ex.Message}");
                        }
                        }

                    //UnityEngine.Debug.Log($"Extracted texture from BSA: {foundPath} -> {System.IO.Path.GetFileName(outputPath)} (from {sourceESM})");
                        return outputPath;
                    }
                else
                {
                    UnityEngine.Debug.LogWarning($"Failed to extract texture '{foundPath}' from BSA archives");
                }
            }

            return null;
        }

        /// <summary>
        /// Loads a texture from file (handles PNG, DDS, TGA)
        /// </summary>
        private Texture2D LoadTextureFromFile(string texturePath)
        {
            if (!File.Exists(texturePath))
            {
                return null;
            }

            try
            {
                byte[] textureData = File.ReadAllBytes(texturePath);
                string ext = System.IO.Path.GetExtension(texturePath).ToLower();

                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                {
                    Texture2D texture = new Texture2D(2, 2);
                    if (texture.LoadImage(textureData))
                    {
                        texture.wrapMode = TextureWrapMode.Repeat;
                        texture.filterMode = FilterMode.Bilinear;
                        texture.anisoLevel = 1;
                        return texture;
                    }
                }
                else if (ext == ".dds")
                {
                    // Try to convert DDS to PNG first, then load PNG
                    string pngPath = System.IO.Path.ChangeExtension(texturePath, ".png");
                    if (!File.Exists(pngPath))
                    {
                        // Convert DDS to PNG
                        if (ConvertDDSToPNG(textureData, pngPath))
                        {
                            // Load the converted PNG
                            byte[] pngData = File.ReadAllBytes(pngPath);
                            Texture2D texture = new Texture2D(2, 2);
                            if (texture.LoadImage(pngData))
                            {
                                texture.wrapMode = TextureWrapMode.Repeat;
                                texture.filterMode = FilterMode.Bilinear;
                                texture.anisoLevel = 1;
                                return texture;
                            }
                        }
                    }
                    else
                    {
                        // PNG already exists, load it
                        byte[] pngData = File.ReadAllBytes(pngPath);
                        Texture2D texture = new Texture2D(2, 2);
                        if (texture.LoadImage(pngData))
                        {
                            texture.wrapMode = TextureWrapMode.Repeat;
                            texture.filterMode = FilterMode.Bilinear;
                            texture.anisoLevel = 1;
                            return texture;
                        }
                    }
                    
                    // Fallback: try AssetDatabase in editor
                    #if UNITY_EDITOR
                    string assetPath = texturePath.Replace('\\', '/');
                    if (assetPath.StartsWith(Application.dataPath))
                    {
                        assetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);
                    }
                    Texture2D texture2 = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    if (texture2 != null)
                    {
                        return texture2;
                    }
                    #endif
                    //UnityEngine.Debug.LogWarning($"Failed to load DDS texture: {texturePath}");
                }
                else if (ext == ".tga")
                {
                    Texture2D texture = new Texture2D(2, 2);
                    if (texture.LoadImage(textureData))
                    {
                        texture.wrapMode = TextureWrapMode.Repeat;
                        texture.filterMode = FilterMode.Bilinear;
                        texture.anisoLevel = 1;
                        return texture;
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error loading texture {texturePath}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Creates a URP-compatible material for a NIF model
        /// </summary>
        private Material CreateURPMaterial(string materialName, Texture2D diffuseTexture = null, Texture2D normalTexture = null, Color? diffuseColor = null, bool isTree = false)
        {
            // Tree shader check removed - not using SpeedTree9_URP anymore
            Shader shader = null;
            
            // If not a tree or tree shader not found, try URP shaders
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Universal Render Pipeline/Simple Lit");
                }
                if (shader == null)
                {
                    shader = Shader.Find("Universal Render Pipeline/Unlit");
                }
                if (shader == null)
                {
                    // Fallback to Standard shader
                    shader = Shader.Find("Standard");
                }
            }

            if (shader == null)
            {
                UnityEngine.Debug.LogError("Could not find suitable shader for material");
                return null;
            }

            Material material = new Material(shader);
            material.name = materialName;

            // Set shader properties based on shader type
            if (isTree)
            {
                if (shader.name.Contains("Speedtree") || shader.name.Contains("SpeedTree") || shader.name.Contains("Nature"))
                {
                    // URP/Nature/Speedtree9_URP or Nature shader properties
                    if (diffuseTexture != null)
                    {
                        // SpeedTree9_URP uses _MainTex for diffuse
                        material.SetTexture("_MainTex", diffuseTexture);
                    }
                    material.SetColor("_Color", diffuseColor ?? Color.white);
                    // Tree shaders typically use _Cutoff for alpha cutoff
                    material.SetFloat("_Cutoff", 0.5f);
                    
                    // Enable alpha clipping/cutout for trees to mask black areas in leaves
                    // For SpeedTree shaders, enable the alpha test keyword
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.DisableKeyword("_ALPHABLEND_ON");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    
                    // Set render queue to AlphaTest (2450) for proper rendering
                    material.renderQueue = 2450;
                }
                else if (shader.name.Contains("Universal Render Pipeline"))
                {
                    // URP Lit shader fallback - enable alpha clipping
                    if (diffuseTexture != null)
                    {
                        material.SetTexture("_BaseMap", diffuseTexture);
                    }
                    material.SetColor("_BaseColor", diffuseColor ?? Color.white);
                    material.SetFloat("_Metallic", 0.0f);
                    material.SetFloat("_Smoothness", 0.5f);
                    
                    // Enable alpha clipping for URP Lit
                    material.SetFloat("_Surface", 0); // Opaque surface mode
                    material.SetFloat("_AlphaClip", 1); // Enable alpha clipping
                    material.SetFloat("_Cutoff", 0.5f); // Alpha threshold
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.renderQueue = 2450; // AlphaTest queue
                }
                else
                {
                    // Standard shader fallback
                    if (diffuseTexture != null)
                    {
                        material.SetTexture("_MainTex", diffuseTexture);
                    }
                    material.SetColor("_Color", diffuseColor ?? Color.white);
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.SetFloat("_Cutoff", 0.5f);
                    material.renderQueue = 2450;
                }
            }
            else if (shader.name.Contains("Universal Render Pipeline"))
            {
                // URP shader properties
                if (diffuseTexture != null)
                {
                    material.SetTexture("_BaseMap", diffuseTexture);
                }
                if (normalTexture != null)
                {
                    material.SetTexture("_BumpMap", normalTexture);
                    material.EnableKeyword("_NORMALMAP");
                }
                material.SetColor("_BaseColor", diffuseColor ?? Color.white);
                material.SetFloat("_Metallic", 0.0f);
                material.SetFloat("_Smoothness", 0.5f);
            }
            else
            {
                // Standard shader fallback
                if (diffuseTexture != null)
                {
                    material.SetTexture("_MainTex", diffuseTexture);
                }
                if (normalTexture != null)
                {
                    material.SetTexture("_BumpMap", normalTexture);
                }
                material.SetColor("_Color", diffuseColor ?? Color.white);
                material.SetFloat("_Metallic", 0.0f);
                material.SetFloat("_Glossiness", 0.5f);
            }

            return material;
        }

        /// <summary>
        /// Loads a NIF file using niflib.net (more reliable parser)
        /// </summary>
        private GameObject LoadNIFFromBytesNiflib(byte[] nifData, string filename, bool combineMeshes = false, bool isTreeOrGrass = false)
        {
            using (MemoryStream stream = new MemoryStream(nifData))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                // Parse NIF file using niflib.net
                Niflib.NiFile nifFile = new Niflib.NiFile(reader);
                
                // Extract meshes from all root nodes
                List<NIFMesh> renderMeshes = new List<NIFMesh>();
                List<NIFMesh> collisionMeshes = new List<NIFMesh>();
                Dictionary<string, NIFTexture> textures = new Dictionary<string, NIFTexture>();
                NIFMaterial material = new NIFMaterial();
                
                // Get all root nodes
                foreach (var rootRef in nifFile.Footer.RootNodes)
                {
                    if (!rootRef.IsValid() || rootRef.Object == null)
                        continue;
                    
                    // Check if this is a RootCollisionNode (Morrowind collision mesh)
                    var rootCollisionNode = rootRef.Object as Niflib.RootCollisionNode;
                    if (rootCollisionNode != null)
                    {
                        // Extract collision meshes from RootCollisionNode
                        ExtractMeshesFromNode(rootCollisionNode, collisionMeshes, textures, material, nifFile, Matrix4x4.identity, isCollision: true);
                        continue;
                    }
                    
                    var rootNode = rootRef.Object as Niflib.NiNode;
                    if (rootNode == null)
                        continue;
                    
                    // Extract render meshes from regular root nodes
                    // Start with identity transform - we'll apply child node transforms but not root transform
                    // (similar to Blender's "discard_root_transforms" option)
                    ExtractMeshesFromNode(rootNode, renderMeshes, textures, material, nifFile, Matrix4x4.identity, isCollision: false, depth: 0);
                }
                
                // Separate render meshes from collision meshes
                List<NIFMesh> renderMeshesList = new List<NIFMesh>();
                List<NIFMesh> collisionMeshesList = new List<NIFMesh>(collisionMeshes);
                
                //UnityEngine.Debug.Log($"Initial collision meshes from RootCollisionNode: {collisionMeshesList.Count}, render meshes: {renderMeshes.Count}");
                
                // Also check render meshes for collision candidates (no material/texture)
                // Many static objects have a mesh with no texture data that should be used as collision mesh
                foreach (var mesh in renderMeshes)
                {
                    if (mesh.Textures.Count == 0)
                    {
                        // Mesh with no textures is likely a collision mesh (name can be empty or not)
                        mesh.IsCollisionMesh = true;
                        collisionMeshesList.Add(mesh);
                        //UnityEngine.Debug.Log($"Detected collision mesh (no textures): '{mesh.Name}' (empty name is common for collision meshes)");
                    }
                    else
                    {
                        // Keep as render mesh
                        renderMeshesList.Add(mesh);
                    }
                }
                
                //UnityEngine.Debug.Log($"Final separation: {renderMeshesList.Count} render meshes, {collisionMeshesList.Count} collision meshes");
                
                if (renderMeshesList.Count == 0 && collisionMeshesList.Count == 0)
                {
                    UnityEngine.Debug.LogWarning($"No meshes found in NIF file (niflib.net): {filename}");
                    return null;
                }
                
                // Create root GameObject
                GameObject rootObj = new GameObject(System.IO.Path.GetFileNameWithoutExtension(filename));
                // Root stays at identity - coordinate system conversion applied to child meshes individually
                rootObj.transform.rotation = Quaternion.identity;
                
                // Morrowind NIF files use inches, Unity uses meters
                // For static objects: use MORROWIND_TO_TERRAIN_SCALE (64/8192 = 0.0078125) to match terrain coordinate system
                // For trees: use standard inches-to-meters conversion (0.0254) at mesh level
                const float INCHES_TO_METERS_STANDARD = 0.0254f;
                const float MORROWIND_TO_TERRAIN_SCALE = 64f / 8192f; // 0.0078125
                // For combined meshes (trees/grass), coordinate system conversion is already handled in ConvertMorrowindTransformToUnity
                // No additional rotation needed - the transform conversion handles it
                Quaternion meshRotation = Quaternion.identity; // Render mesh rotation
                Quaternion collisionMeshRotation = Quaternion.identity; // Collision mesh rotation
                
                // For trees (combineMeshes=true): scale at mesh level using standard conversion
                // For static objects (combineMeshes=false): scale at GameObject level using MORROWIND_TO_TERRAIN_SCALE
                // Note: XSCL record will multiply this base scale, so we use MORROWIND_TO_TERRAIN_SCALE here
                float vertexScale = combineMeshes ? INCHES_TO_METERS_STANDARD : 1.0f;
                float gameObjectScale = combineMeshes ? 1.0f : MORROWIND_TO_TERRAIN_SCALE;
                
                // Apply GameObject-level scale for static objects
                rootObj.transform.localScale = Vector3.one * gameObjectScale;
                
                if (combineMeshes)
                {
                    // Combine all render meshes into a single mesh with submeshes (each with its own material)
                    // Exclude collision meshes from render mesh (they're not needed for rendering)
                    // For trees and grass, use isTree: true to enable alpha clipping
                    var (combinedMesh, submeshMaterials) = CombineMeshes(renderMeshesList, vertexScale, meshRotation, _esm, isTree: true);
                    if (combinedMesh != null)
                    {
                        MeshFilter meshFilter = rootObj.AddComponent<MeshFilter>();
                        meshFilter.mesh = combinedMesh;
                        
                        MeshRenderer meshRenderer = rootObj.AddComponent<MeshRenderer>();
                        // Use sharedMaterials array for multiple materials (one per submesh)
                        meshRenderer.sharedMaterials = submeshMaterials;
                        
                        // Combine collision meshes into a single mesh for MeshCollider
                        if (collisionMeshesList.Count > 0)
                        {
                            var (collisionMesh, _) = CombineMeshes(collisionMeshesList, vertexScale, collisionMeshRotation, _esm, isTree: false);
                            if (collisionMesh != null)
                            {
                                MeshCollider meshCollider = rootObj.AddComponent<MeshCollider>();
                                meshCollider.sharedMesh = collisionMesh;
                                meshCollider.convex = false; // Trees/grass typically use non-convex colliders
                                // Note: For grass, isTrigger will be set in PlaceGrassDetail
                                //UnityEngine.Debug.Log($"Added MeshCollider to combined mesh {filename} using {collisionMeshesList.Count} collision mesh(es)");
                            }
                        }
                        else
                        {
                            // If no collision mesh exists, create one from the render mesh for grass (trees might not need it)
                            // This will be handled in PlaceGrassDetail if needed
                        }
                        
                        //UnityEngine.Debug.Log($"Created combined NIF model (niflib.net): {filename} with {renderMeshesList.Count} render mesh(es) combined into {combinedMesh.subMeshCount} submesh(es) on root, {collisionMeshesList.Count} collision mesh(es) for collider");
                        return rootObj;
                    }
                }
                
                // Create separate GameObjects for each render mesh (for static objects)
                for (int i = 0; i < renderMeshesList.Count; i++)
                {
                    NIFMesh nifMesh = renderMeshesList[i];
                    
                    GameObject meshObj = new GameObject(string.IsNullOrEmpty(nifMesh.Name) ? $"Mesh_{i}" : nifMesh.Name);
                    meshObj.transform.SetParent(rootObj.transform);
                    meshObj.transform.localPosition = Vector3.zero;
                    // Coordinate system conversion is already handled in ExtractMeshFromTriShape at vertex level
                    // No additional rotation or scale needed - the vertex-level conversion handles it
                    meshObj.transform.localRotation = Quaternion.identity;
                    meshObj.transform.localScale = Vector3.one;
                    
                    Mesh unityMesh = CreateUnityMeshFromNIFMesh(nifMesh, vertexScale);
                    
                    MeshFilter meshFilter = meshObj.AddComponent<MeshFilter>();
                    meshFilter.mesh = unityMesh;
                    
                    MeshRenderer meshRenderer = meshObj.AddComponent<MeshRenderer>();
                    Material mat = CreateMaterialFromNIF(nifMesh.Material, nifMesh.Textures, _esm, isTree: isTreeOrGrass);
                    meshRenderer.material = mat;
                }
                
                // Create GameObjects for collision meshes with MeshFilter and MeshCollider (but no MeshRenderer)
                for (int i = 0; i < collisionMeshesList.Count; i++)
                {
                    NIFMesh nifMesh = collisionMeshesList[i];
                    
                    GameObject collisionObj = new GameObject(string.IsNullOrEmpty(nifMesh.Name) ? $"Collision_{i}" : nifMesh.Name + "_Collision");
                    collisionObj.transform.SetParent(rootObj.transform);
                    collisionObj.transform.localPosition = Vector3.zero;
                    // Coordinate system conversion is already handled in ExtractMeshFromTriShape at vertex level
                    // No additional rotation or scale needed - the vertex-level conversion handles it
                    collisionObj.transform.localRotation = Quaternion.identity;
                    collisionObj.transform.localScale = Vector3.one;
                    // Note: We previously flipped X scale here to fix mirroring, but this was a workaround.
                    // The correct fix is to use OpenMW's rotation conversion (negated axes) in PlaceStatics.cs
                    
                    Mesh unityMesh = CreateUnityMeshFromNIFMesh(nifMesh, vertexScale);
                    
                    // Add MeshFilter to store the mesh data
                    MeshFilter meshFilter = collisionObj.AddComponent<MeshFilter>();
                    meshFilter.mesh = unityMesh;
                    
                    // Explicitly ensure no MeshRenderer is added (Unity shouldn't add one automatically, but just in case)
                    MeshRenderer existingRenderer = collisionObj.GetComponent<MeshRenderer>();
                    if (existingRenderer != null)
                    {
                        //UnityEngine.Debug.LogWarning($"Removing unexpected MeshRenderer from collision mesh: {collisionObj.name}");
                        UnityEngine.Object.DestroyImmediate(existingRenderer);
                    }
                    
                    // Add MeshCollider that references the mesh in the MeshFilter (no MeshRenderer = not rendered)
                    MeshCollider meshCollider = collisionObj.AddComponent<MeshCollider>();
                    meshCollider.sharedMesh = unityMesh;
                    meshCollider.convex = false; // Use non-convex for complex collision meshes
                    
                    //UnityEngine.Debug.Log($"Created collision mesh GameObject: {collisionObj.name} with MeshFilter and MeshCollider (no MeshRenderer)");
                }
                
                //UnityEngine.Debug.Log($"Created NIF model (niflib.net): {filename} with {renderMeshesList.Count} render mesh(es) and {collisionMeshesList.Count} collision mesh(es)");
                return rootObj;
            }
        }
        
        /// <summary>
        /// Loads a skeleton NIF file and creates GameObjects for bones (NiNode hierarchy) and meshes
        /// This is specialized for skeleton files like base_anim.nif which contain bone hierarchies
        /// </summary>
        private GameObject LoadSkeletonFromBytesNiflib(byte[] nifData, string filename)
        {
            using (MemoryStream stream = new MemoryStream(nifData))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                // Parse NIF file using niflib.net
                Niflib.NiFile nifFile = new Niflib.NiFile(reader);
                
                // Create root GameObject for skeleton
                GameObject skeletonRoot = new GameObject(System.IO.Path.GetFileNameWithoutExtension(filename));
                // Root stays at identity - coordinate system conversion applied to bones individually
                skeletonRoot.transform.rotation = Quaternion.identity;
                
                // Skeleton files are in Morrowind units, same as static objects
                // Scale will be applied by the caller (PlaceStatics.cs)
                skeletonRoot.transform.localScale = Vector3.one;
                
                // Process all root nodes and create bone hierarchy
                foreach (var rootRef in nifFile.Footer.RootNodes)
                {
                    if (!rootRef.IsValid() || rootRef.Object == null)
                        continue;
                    
                    var rootNode = rootRef.Object as Niflib.NiNode;
                    if (rootNode == null)
                        continue;
                    
                    // Create bone hierarchy from NiNode tree
                    // This creates GameObjects for each NiNode (bone) with proper transforms
                    // Pass null as parentWorldTransform to indicate this is the root (will be handled in CreateBoneHierarchy)
                    CreateBoneHierarchy(rootNode, skeletonRoot.transform, null);
                }
                
                return skeletonRoot;
            }
        }
        
        /// <summary>
        /// Recursively creates GameObjects for NiNode bones and processes meshes
        /// </summary>
        private void CreateBoneHierarchy(Niflib.NiNode node, Transform parentTransform, Matrix4x4? parentWorldTransform)
        {
            if (node == null)
                return;
            
            // Get node name (bone name)
            string nodeName = node.Name != null ? node.Name.Value : "UnnamedBone";
            
            // Create GameObject for this bone
            GameObject boneObj = new GameObject(nodeName);
            boneObj.transform.SetParent(parentTransform, false);
            
            // Extract transform components directly from NiAVObject (not from matrix)
            // This ensures we get the correct local transform relative to parent
            Vector3 morrowindTranslation = node.Translation;
            Matrix4x4 morrowindRotation = (Matrix4x4)node.Rotation;
            float morrowindScale = node.Scale;
            
            // Convert Morrowind coordinate system (left-handed, Z-up) to Unity (left-handed, Y-up)
            // Morrowind: X=East, Y=North, Z=Up
            // Unity: X=East, Y=Up, Z=South
            // Conversion: X stays X, Y becomes -Z, Z becomes Y
            
            // Convert translation
            Vector3 unityTranslation = ConvertMorrowindTranslationToUnity(morrowindTranslation);
            
            // Convert rotation (Z-up to Y-up)
            Quaternion unityRotation = ConvertMorrowindRotationToUnity(morrowindRotation);
            
            // Scale is uniform (single float in NIF) - no coordinate system change needed for scale
            Vector3 unityScale = new Vector3(morrowindScale, morrowindScale, morrowindScale);
            
            // Check if this is the root node (parentWorldTransform is null)
            // Root node typically has identity transform, but we should still apply it if it has one
            bool isRootNode = !parentWorldTransform.HasValue;
            
            // Always apply the transform - even root nodes should have their transforms applied
            // (though root nodes in skeleton files typically have identity transforms)
            boneObj.transform.localPosition = unityTranslation;
            boneObj.transform.localRotation = unityRotation;
            boneObj.transform.localScale = unityScale;
            
            // Build transform matrix for world space calculations (for children)
            Matrix4x4 nodeLocalTransform = Matrix4x4.TRS(unityTranslation, unityRotation, unityScale);
            Matrix4x4 nodeWorldTransform = isRootNode
                ? nodeLocalTransform
                : parentWorldTransform.Value * nodeLocalTransform;
            
            // Process children (both NiNode bones and NiTriShape meshes)
            if (node.Children != null)
            {
                foreach (var childRef in node.Children)
                {
                    if (!childRef.IsValid() || childRef.Object == null)
                        continue;
                    
                    var childNode = childRef.Object as Niflib.NiNode;
                    if (childNode != null)
                    {
                        // Recursively create bone hierarchy, passing the world transform
                        CreateBoneHierarchy(childNode, boneObj.transform, nodeWorldTransform);
                    }
                    
                    // Note: We skip NiTriShape meshes for skeleton files
                    // Skeleton bones typically don't have visible meshes, just transforms
                    // If a bone has a mesh, it's usually a collision helper or debug visualization
                }
            }
        }
        
        /// <summary>
        /// Creates a Unity Mesh from a NIFMesh (helper method to avoid code duplication)
        /// </summary>
        private Mesh CreateUnityMeshFromNIFMesh(NIFMesh nifMesh, float vertexScale)
        {
            Mesh unityMesh = new Mesh();
            unityMesh.name = nifMesh.Name;
            
            // Vertices are already transformed by NiNode transforms and coordinate system conversion in ExtractMeshFromTriShape
            // We only need to apply vertex scaling here (vertexScale is 1.0f for static objects, scaling happens at GameObject level)
            Vector3[] transformedVertices = nifMesh.Vertices.ToArray();
            Vector3[] unityVertices = new Vector3[transformedVertices.Length];
            for (int v = 0; v < transformedVertices.Length; v++)
            {
                // Apply vertex scale only (coordinate system conversion already applied in ExtractMeshFromTriShape)
                unityVertices[v] = new Vector3(
                    transformedVertices[v].x * vertexScale,
                    transformedVertices[v].y * vertexScale,
                    transformedVertices[v].z * vertexScale
                );
            }
            unityMesh.vertices = unityVertices;
            unityMesh.triangles = nifMesh.Triangles.ToArray();
            
            if (nifMesh.UVs.Count > 0)
                unityMesh.uv = nifMesh.UVs.ToArray();
            
            if (nifMesh.Normals.Count > 0)
            {
                // Normals are already transformed by NiNode transforms and coordinate system conversion in ExtractMeshFromTriShape
                Vector3[] transformedNormals = nifMesh.Normals.ToArray();
                unityMesh.normals = transformedNormals;
            }
            else
            {
                unityMesh.RecalculateNormals();
            }
            
            if (nifMesh.Colors.Count > 0 && nifMesh.Colors.Count == nifMesh.Vertices.Count)
                unityMesh.colors = nifMesh.Colors.ToArray();
            
            unityMesh.RecalculateBounds();
            
            return unityMesh;
        }
        
        /// <summary>
        /// Converts a transform from Morrowind's coordinate system (left-handed, Z-up) to Unity's (left-handed, Y-up)
        /// Morrowind: X=East, Y=North, Z=Up (left-handed)
        /// Unity: X=East, Y=Up, Z=South (left-handed)
        /// Conversion: Rotate -90 degrees around X axis (Z-up to Y-up), then flip Y scale to fix mirroring
        /// The Y scale flip is needed because the coordinate system conversion causes mirroring
        /// </summary>
        private Matrix4x4 ConvertMorrowindTransformToUnity(Matrix4x4 morrowindTransform)
        {
            // Morrowind to Unity coordinate system conversion:
            // X stays X (East)
            // Y (North) becomes -Z (South)
            // Z (Up) becomes Y (Up)
            // This is a -90 degree rotation around X axis
            
            // Create the coordinate system conversion matrix (Z-up to Y-up)
            // This rotates -90 degrees around X: Y -> Z, Z -> -Y
            Matrix4x4 zUpToYUp = Matrix4x4.Rotate(Quaternion.Euler(-90f, 0f, 0f));
            
            // Apply conversion: Unity transform = conversion * Morrowind transform
            Matrix4x4 converted = zUpToYUp * morrowindTransform;
            
            // Fix mirroring: Flip Y scale (since we converted Z-up to Y-up, the original Z inversion becomes Y inversion)
            // Extract scale from the converted transform
            Vector3 scale = new Vector3(
                new Vector3(converted.m00, converted.m01, converted.m02).magnitude,
                new Vector3(converted.m10, converted.m11, converted.m12).magnitude,
                new Vector3(converted.m20, converted.m21, converted.m22).magnitude
            );
            
            // Flip Y scale to fix mirroring (this replaces the old negative Z scale workaround)
            scale.y = -scale.y;
            
            // Reconstruct the transform with flipped Y scale
            // Extract rotation and translation
            Vector3 translation = new Vector3(converted.m03, converted.m13, converted.m23);
            
            // When we flip Y scale, we also need to flip Y translation to maintain correct positioning
            // This ensures child meshes are positioned at the correct height
            translation.y = -translation.y;
            
            // Normalize rotation columns to get pure rotation
            Vector3 col0 = new Vector3(converted.m00, converted.m01, converted.m02).normalized;
            Vector3 col1 = new Vector3(converted.m10, converted.m11, converted.m12).normalized;
            Vector3 col2 = new Vector3(converted.m20, converted.m21, converted.m22).normalized;
            
            // Rebuild matrix with flipped Y scale
            Matrix4x4 result = Matrix4x4.identity;
            result.m00 = col0.x * scale.x; result.m01 = col0.y * scale.x; result.m02 = col0.z * scale.x;
            result.m10 = col1.x * scale.y; result.m11 = col1.y * scale.y; result.m12 = col1.z * scale.y;
            result.m20 = col2.x * scale.z; result.m21 = col2.y * scale.z; result.m22 = col2.z * scale.z;
            result.m03 = translation.x; result.m13 = translation.y; result.m23 = translation.z;
            
            return result;
        }
        
        /// <summary>
        /// Converts a translation vector from Morrowind's coordinate system to Unity's
        /// Morrowind: X=East, Y=North, Z=Up
        /// Unity: X=East, Y=Up, Z=South
        /// Note: Translation doesn't need Y flip - only scale/rotation transforms need it
        /// </summary>
        private Vector3 ConvertMorrowindTranslationToUnity(Vector3 morrowindTranslation)
        {
            // X stays X (East)
            // Y (North) becomes -Z (South)
            // Z (Up) becomes Y (Up)
            return new Vector3(
                morrowindTranslation.x,      // East stays East
                morrowindTranslation.z,       // Up becomes Up
                -morrowindTranslation.y       // North becomes South (negated)
            );
        }
        
        /// <summary>
        /// Converts a rotation matrix from Morrowind's coordinate system to Unity's
        /// Applies the Z-up to Y-up rotation conversion
        /// Note: Rotation doesn't need Y flip - the coordinate conversion handles it
        /// </summary>
        private Quaternion ConvertMorrowindRotationToUnity(Matrix4x4 morrowindRotation)
        {
            // Create coordinate system conversion rotation (-90 degrees around X)
            Quaternion zUpToYUp = Quaternion.Euler(-90f, 0f, 0f);
            
            // Convert Morrowind rotation matrix to quaternion
            Quaternion morrowindQuat = morrowindRotation.rotation;
            
            // Apply conversion: Unity rotation = conversion * Morrowind rotation
            return zUpToYUp * morrowindQuat;
        }
        
        /// <summary>
        /// Builds a transform matrix from an NiAVObject's transform components
        /// Uses proper matrix composition: Translation * Rotation * Scale (separate matrices)
        /// This prevents scale from being mixed with rotation, which can cause reflection issues
        /// </summary>
        private Matrix4x4 BuildTransformMatrix(Niflib.NiAVObject obj)
        {
            if (obj == null)
                return Matrix4x4.identity;
            
            // Check for negative scale in the Scale field
            if (obj.Scale < 0)
            {
                UnityEngine.Debug.LogWarning($"NIF file contains negative scale field: {obj.Scale} in object '{obj.Name?.Value ?? "unnamed"}' - this may indicate mirroring");
            }
            
            // OpenMW's toMatrix() multiplies rotation elements by scale directly
            // This is what the NIF format expects, even though it mixes rotation and scale
            Matrix4x4 rotation = (Matrix4x4)obj.Rotation;
            
            // Build transform matrix matching OpenMW's approach:
            // 1. Start with translation
            Matrix4x4 transform = Matrix4x4.Translate(obj.Translation);
            
            // 2. Multiply each rotation matrix element by scale (mixing rotation and scale)
            transform.m00 = rotation.m00 * obj.Scale;
            transform.m01 = rotation.m01 * obj.Scale;
            transform.m02 = rotation.m02 * obj.Scale;
            transform.m10 = rotation.m10 * obj.Scale;
            transform.m11 = rotation.m11 * obj.Scale;
            transform.m12 = rotation.m12 * obj.Scale;
            transform.m20 = rotation.m20 * obj.Scale;
            transform.m21 = rotation.m21 * obj.Scale;
            transform.m22 = rotation.m22 * obj.Scale;
            
            return transform;
        }
        
        /// <summary>
        /// Accumulates transforms from the parent chain (from root to this object)
        /// </summary>
        private Matrix4x4 AccumulateParentTransforms(Niflib.NiAVObject obj)
        {
            Matrix4x4 accumulatedTransform = Matrix4x4.identity;
            Niflib.NiAVObject current = obj;
            
            // Walk up the parent chain, accumulating transforms
            while (current != null)
            {
                // Apply this object's transform
                Matrix4x4 currentTransform = BuildTransformMatrix(current);
                // Parent transforms are applied first (right-to-left multiplication)
                accumulatedTransform = currentTransform * accumulatedTransform;
                
                // Move to parent
                current = current.Parent;
            }
            
            return accumulatedTransform;
        }
        
        /// <summary>
        /// Extracts meshes from a NiNode tree using niflib.net
        /// </summary>
        private void ExtractMeshesFromNode(Niflib.NiNode node, List<NIFMesh> meshes, Dictionary<string, NIFTexture> textures, NIFMaterial material, Niflib.NiFile nifFile = null, Matrix4x4 parentTransform = default(Matrix4x4), bool isCollision = false, int depth = 0)
        {
            if (node == null)
                return;
            
            // Build transform for this node (still in Morrowind coordinate system)
            Matrix4x4 nodeTransformMorrowind = BuildTransformMatrix(node);
            
            // Convert node transform to Unity coordinate system
            // Use the same conversion for both render and collision meshes since they share the same hierarchy
            Matrix4x4 nodeTransformUnity = ConvertMorrowindTransformToUnity(nodeTransformMorrowind);
            
            // Accumulate transform: if parentTransform is identity, use nodeTransform
            // Otherwise, combine parent and node transforms (parent * node)
            // Note: parentTransform should already be in Unity coordinate system if it came from a parent node
            // If parentTransform is default (identity), it means we're at the root
            // Root node transform is typically ignored for meshes, but we convert it anyway for consistency
            Matrix4x4 nodeTransformAccumulated = parentTransform == default(Matrix4x4)
                ? Matrix4x4.identity  // Root node: ignore its transform (as per OpenMW convention)
                : parentTransform * nodeTransformUnity;  // Child node: accumulate transform (parent * child)
            
            // Process children
            if (node.Children != null)
            {
                foreach (var childRef in node.Children)
                {
                    if (!childRef.IsValid() || childRef.Object == null)
                        continue;
                    
                    var childNode = childRef.Object as Niflib.NiNode;
                    if (childNode != null)
                    {
                        // Pass accumulated transform to child nodes, preserve collision flag, increment depth
                        ExtractMeshesFromNode(childNode, meshes, textures, material, nifFile, nodeTransformAccumulated, isCollision, depth + 1);
                    }
                    
                    var triShape = childRef.Object as Niflib.NiTriShape;
                    if (triShape != null)
                    {
                        // Apply accumulated parent transform to the mesh, mark as collision if needed
                        ExtractMeshFromTriShape(triShape, meshes, textures, material, nifFile, nodeTransformAccumulated, isCollision);
                    }
                }
            }
        }
        
        /// <summary>
        /// Resolves a property by walking up the parent chain (similar to MWGE's ResolveProperty)
        /// </summary>
        private Niflib.NiProperty ResolveProperty(Niflib.NiAVObject obj, System.Type propertyType)
        {
            if (obj == null)
                return null;
            
            // Check immediate object for the property
            if (obj.Properties != null)
            {
                foreach (var propRef in obj.Properties)
                {
                    if (propRef.IsValid() && propRef.Object != null && propertyType.IsInstanceOfType(propRef.Object))
                    {
                        return propRef.Object as Niflib.NiProperty;
                    }
                }
            }
            
            // Walk up parent chain
            // Note: Parent is directly a NiNode, not a NiRef
            if (obj.Parent != null)
            {
                // Parent is a NiNode which inherits from NiAVObject, so we can use it directly
                return ResolveProperty(obj.Parent, propertyType);
            }
            
            return null;
        }
        
        /// <summary>
        /// Extracts mesh data from a NiTriShape using niflib.net
        /// </summary>
        private void ExtractMeshFromTriShape(Niflib.NiTriShape triShape, List<NIFMesh> meshes, Dictionary<string, NIFTexture> textures, NIFMaterial material, Niflib.NiFile nifFile = null, Matrix4x4 parentTransform = default(Matrix4x4), bool isCollision = false)
        {
            if (triShape == null || triShape.Data == null || !triShape.Data.IsValid() || triShape.Data.Object == null)
                return;
            
            var shapeData = triShape.Data.Object as Niflib.NiTriShapeData;
            if (shapeData == null || !shapeData.HasVertices || shapeData.Vertices == null || shapeData.Vertices.Length == 0)
                return;
            
            NIFMesh mesh = new NIFMesh();
            mesh.Name = triShape.Name != null ? triShape.Name.Value : "";
            
            // Check for texturing property early - if mesh has no textures, it's likely a collision mesh
            // This allows us to process it correctly during vertex extraction (before vertices are processed)
            var texturingPropEarly = ResolveProperty(triShape, typeof(Niflib.NiTexturingProperty)) as Niflib.NiTexturingProperty;
            bool hasNoTextures = (texturingPropEarly == null || texturingPropEarly.TextureCount == 0 || texturingPropEarly.BaseTexture == null || texturingPropEarly.BaseTexture.Source == null);
            
            // If mesh has no textures and wasn't explicitly marked as collision, treat it as collision mesh
            // This ensures meshes with no textures get the correct vertex processing (Z scale flip for collision)
            if (!isCollision && hasNoTextures)
            {
                isCollision = true;
            }
            
            // Build transform for this NiTriShape (it's also a NiAVObject, so it has Translation/Rotation/Scale)
            Matrix4x4 triShapeTransformMorrowind = BuildTransformMatrix(triShape);
            
            // Convert to Unity coordinate system
            // Use the same conversion for both render and collision meshes since they share the same hierarchy
            Matrix4x4 triShapeTransformUnity = ConvertMorrowindTransformToUnity(triShapeTransformMorrowind);
            
            // Combine with parent transform: parentTransform * triShapeTransform
            // If parentTransform is default (identity), just use triShapeTransform
            // Note: parentTransform should already be in Unity coordinate system if it came from ExtractMeshesFromNode
            Matrix4x4 unityTransform = parentTransform == default(Matrix4x4) 
                ? triShapeTransformUnity 
                : parentTransform * triShapeTransformUnity;
            
            // Check for reflection (negative determinant) in the upper-left 3x3 matrix
            // This indicates a handedness flip that needs correction
            float determinant = unityTransform.m00 * (unityTransform.m11 * unityTransform.m22 - unityTransform.m21 * unityTransform.m12)
                              - unityTransform.m01 * (unityTransform.m10 * unityTransform.m22 - unityTransform.m20 * unityTransform.m12)
                              + unityTransform.m02 * (unityTransform.m10 * unityTransform.m21 - unityTransform.m20 * unityTransform.m11);
            
            // Check for reflection (negative determinant) - this indicates a handedness flip
            // Instead of flipping Z scale (which causes render issues), we'll handle it by reversing triangle winding
            // The coordinate system conversion should handle the scale correctly without needing negative Z scale
            Matrix4x4 correctedTransform = unityTransform;
            // Note: We no longer flip Z scale here - the coordinate conversion should handle it correctly
            // If there's a reflection, we'll just reverse triangle winding order
            
            // Convert vertices from niflib Vector3 to Unity Vector3
            // Apply Unity transform (already includes coordinate system conversion from Z-up to Y-up)
            // Render meshes: Apply +90° rotation around X and Y scale flip
            // Collision meshes: May need different handling (render meshes are perfect, don't change them)
            Quaternion zUpToYUpRotation = Quaternion.Euler(90f, 0f, 0f);
            mesh.Vertices = new List<Vector3>();
            foreach (var v in shapeData.Vertices)
            {
                Vector3 vertex = new Vector3(v.x, v.y, v.z);
                // Apply Unity transform (includes coordinate system conversion)
                Vector3 unityVertex = correctedTransform.MultiplyPoint3x4(vertex);
                
                if (isCollision)
                {
                    // Collision meshes: Apply +180° rotation around X axis
                    Quaternion xRotation = Quaternion.Euler(180f, 0f, 0f);
                    unityVertex = xRotation * unityVertex;
                }
                else
                {
                    // Render meshes: Apply +90° rotation around X to ensure Y is up (not Z)
                    unityVertex = zUpToYUpRotation * unityVertex;
                    // Apply Y scale flip at vertex level to fix mirroring (instead of GameObject level)
                    unityVertex = new Vector3(unityVertex.x, -unityVertex.y, unityVertex.z);
                }
                
                mesh.Vertices.Add(unityVertex);
            }
            
            // Convert triangles
            // Render meshes: Reverse winding order (Z-up to Y-up with Y scale flip requires reversed winding)
            // Collision meshes: May need different winding order depending on their coordinate conversion
            if (shapeData.HasTriangles && shapeData.Triangles != null)
            {
                mesh.Triangles = new List<int>();
                foreach (var tri in shapeData.Triangles)
                {
                    // Triangle class has X, Y, Z as ushort properties
                    // Both render and collision meshes use the same winding order reversal
                    // Reverse winding order: X, Z, Y instead of X, Y, Z
                    mesh.Triangles.Add((int)tri.X);
                    mesh.Triangles.Add((int)tri.Z);
                    mesh.Triangles.Add((int)tri.Y);
                }
            }
            
            // Convert normals
            // Normals are directions, so we apply rotation and scale but not translation
            // Extract rotation/scale from the corrected transform matrix (no translation)
            if (shapeData.HasNormals && shapeData.Normals != null)
            {
                mesh.Normals = new List<Vector3>();
                // For normals, we only need rotation/scale (no translation)
                // Extract rotation/scale matrix from unityTransform (upper-left 3x3)
                Matrix4x4 rotationScaleMatrix = unityTransform;
                // Remove translation (set bottom row to 0,0,0,1)
                rotationScaleMatrix.m03 = 0f;
                rotationScaleMatrix.m13 = 0f;
                rotationScaleMatrix.m23 = 0f;
                
                foreach (var n in shapeData.Normals)
                {
                    Vector3 normal = new Vector3(n.x, n.y, n.z);
                    // Apply Unity rotation and scale (no translation) using MultiplyVector
                    // This already includes coordinate system conversion
                    Vector3 unityNormal = rotationScaleMatrix.MultiplyVector(normal);
                    
                    if (isCollision)
                    {
                        // Collision meshes: Apply +180° rotation around X axis to match vertex conversion
                        Quaternion xRotation = Quaternion.Euler(180f, 0f, 0f);
                        unityNormal = xRotation * unityNormal;
                    }
                    else
                    {
                        // Render meshes: Apply additional +90° rotation around X to match vertex conversion
                        unityNormal = zUpToYUpRotation * unityNormal;
                        // Apply Y scale flip at vertex level to match vertex conversion (instead of GameObject level)
                        unityNormal = new Vector3(unityNormal.x, -unityNormal.y, unityNormal.z);
                    }
                    
                    // Normalize to maintain unit length
                    unityNormal.Normalize();
                    mesh.Normals.Add(unityNormal);
                }
            }
            
            // Convert UVs
            if (shapeData.HasUV && shapeData.UVSets != null && shapeData.UVSets.Length > 0 && shapeData.UVSets[0] != null)
            {
                mesh.UVs = new List<Vector2>();
                foreach (var uv in shapeData.UVSets[0])
                {
                    // niflib.net Vector2 is Unity's Vector2 when UNITY is defined
                    // Morrowind NIF files: Testing without V flip first
                    // If textures appear upside down, we may need to flip: V_unity = 1.0 - V_morrowind
                    // Standard conversion: DirectX (V=0 at top) -> Unity (V=0 at bottom) requires V flip
                    // However, if textures are already upside down with the flip, try without it first
                    // If still upside down, restore the flip: mesh.UVs.Add(new Vector2(uv.x, 1f - uv.y));
                    mesh.UVs.Add(new Vector2(uv.x, uv.y));
                }
            }
            
            // Convert vertex colors
            if (shapeData.HasVertexColors && shapeData.VertexColors != null)
            {
                mesh.Colors = new List<Color>();
                foreach (var c in shapeData.VertexColors)
                {
                    // niflib.net Color4 is Unity's Color when UNITY is defined
                    mesh.Colors.Add(new Color(c.r, c.g, c.b, c.a));
                }
            }
            
            // Extract texture information using MWGE approach: ResolveProperty to walk up parent chain
            // Store textures per mesh so we can use submeshes when combining
            Dictionary<string, NIFTexture> meshTextures = new Dictionary<string, NIFTexture>();
            NIFMaterial meshMaterial = new NIFMaterial();
            
            // Use the texturing property we already checked earlier (or check again if needed)
            var texturingProp = texturingPropEarly ?? (ResolveProperty(triShape, typeof(Niflib.NiTexturingProperty)) as Niflib.NiTexturingProperty);
            if (texturingProp != null)
            {
                //UnityEngine.Debug.Log($"Found NiTexturingProperty for mesh '{mesh.Name}' (via ResolveProperty), TextureCount={texturingProp.TextureCount}");
                
                // Check TextureCount (MWGE checks this)
                if (texturingProp.TextureCount > 0 && texturingProp.BaseTexture != null)
                {
                    //UnityEngine.Debug.Log($"BaseTexture is not null, checking Source...");
                    // Extract BaseTexture (main diffuse texture) - MWGE uses GetTexture(0) which is BaseTexture
                    if (texturingProp.BaseTexture.Source != null)
                    {
                        // Manually resolve the reference if needed (FixRefs might not have resolved nested references in TexDesc)
                        var sourceRef = texturingProp.BaseTexture.Source;
                        if (sourceRef.IsValid() && sourceRef.Object == null && nifFile != null)
                        {
                            // Try to manually resolve the reference using the NiFile
                            //UnityEngine.Debug.LogWarning($"BaseTexture.Source is valid (RefId={sourceRef.RefId}) but Object is null. Attempting manual resolution...");
                            try
                            {
                                sourceRef.SetRef(nifFile);
                                //UnityEngine.Debug.Log($"Manually resolved BaseTexture.Source reference, Object={sourceRef.Object?.GetType().Name ?? "null"}");
                            }
                            catch (System.Exception ex)
                            {
                                UnityEngine.Debug.LogError($"Failed to manually resolve BaseTexture.Source reference (RefId={sourceRef.RefId}): {ex.Message}");
                            }
                        }
                        
                        //UnityEngine.Debug.Log($"BaseTexture.Source is not null, IsValid={texturingProp.BaseTexture.Source.IsValid()}, RefId={texturingProp.BaseTexture.Source.RefId}, Object={texturingProp.BaseTexture.Source.Object?.GetType().Name ?? "null"}");
                        if (texturingProp.BaseTexture.Source.IsValid() && texturingProp.BaseTexture.Source.Object != null)
                        {
                            var sourceTex = texturingProp.BaseTexture.Source.Object as Niflib.NiSourceTexture;
                            if (sourceTex != null)
                            {
                                //UnityEngine.Debug.Log($"NiSourceTexture found, UseExternal={sourceTex.UseExternal}, FileName='{sourceTex.FileName?.Value ?? "null"}'");
                                // MWGE checks IsTextureExternal() - we check UseExternal
                                if (sourceTex.UseExternal)
                                {
                                    if (sourceTex.FileName != null && !string.IsNullOrEmpty(sourceTex.FileName.Value))
                                    {
                                        string originalPath = sourceTex.FileName.Value;
                                        string texPath = NormalizeTexturePath(originalPath);
                                        //UnityEngine.Debug.Log($"Texture path: original='{originalPath}', normalized='{texPath}'");
                                        if (!string.IsNullOrEmpty(texPath))
                                        {
                                            // Store in both per-mesh dictionary and shared dictionary (for backward compatibility)
                                            meshTextures["BaseTexture"] = new NIFTexture { FilePath = texPath, Enabled = true };
                                            if (!textures.ContainsKey("BaseTexture"))
                                            {
                                                textures["BaseTexture"] = new NIFTexture { FilePath = texPath, Enabled = true };
                                            }
                                            //UnityEngine.Debug.Log($"✓ Extracted BaseTexture from NIF: {texPath} (external texture)");
                                        }
                                    }
                                    else
                                    {
                                        //UnityEngine.Debug.LogWarning($"NiSourceTexture has UseExternal=true but FileName is null or empty");
                                    }
                                }
                                else
                                {
                                    //UnityEngine.Debug.LogWarning($"NiSourceTexture is not external (UseExternal=false), skipping");
                                }
                            }
                            else
                            {
                                //UnityEngine.Debug.LogWarning($"BaseTexture.Source.Object is not a NiSourceTexture: {texturingProp.BaseTexture.Source.Object?.GetType().Name ?? "null"}");
                            }
                        }
                        else
                        {
                            //UnityEngine.Debug.LogWarning($"BaseTexture.Source is not valid or Object is null");
                        }
                    }
                    else
                    {
                        //UnityEngine.Debug.LogWarning($"BaseTexture.Source is null");
                    }
                }
                else
                {
                    //UnityEngine.Debug.LogWarning($"NiTexturingProperty has TextureCount={texturingProp?.TextureCount ?? 0} or BaseTexture is null");
                }
            }
            else
            {
                //UnityEngine.Debug.LogWarning($"No NiTexturingProperty found for mesh '{mesh.Name}' (checked object and parent chain)");
            }
            
            // Extract material properties
            var materialProp = ResolveProperty(triShape, typeof(Niflib.NiMaterialProperty)) as Niflib.NiMaterialProperty;
            if (materialProp != null)
            {
                meshMaterial.DiffuseColor = new Color(materialProp.DiffuseColor.r, materialProp.DiffuseColor.g, materialProp.DiffuseColor.b, materialProp.DiffuseColor.a);
                meshMaterial.Glossiness = materialProp.Glossiness;
                meshMaterial.HasAlpha = materialProp.Alpha < 1.0f;
                meshMaterial.Alpha = materialProp.Alpha;
                
                // Also update shared material (for backward compatibility)
                material.DiffuseColor = meshMaterial.DiffuseColor;
                material.Glossiness = meshMaterial.Glossiness;
                material.HasAlpha = meshMaterial.HasAlpha;
                material.Alpha = meshMaterial.Alpha;
            }
            
            // Store per-mesh textures and material
            mesh.Textures = meshTextures;
            mesh.Material = meshMaterial;
            
            // Mark as collision mesh if:
            // 1. Explicitly marked as collision (from RootCollisionNode)
            // Note: We'll check for no textures later during separation, as empty names are common for collision meshes
            if (isCollision)
            {
                mesh.IsCollisionMesh = true;
            }
            
            meshes.Add(mesh);
        }

        /// <summary>
        /// Closes the BSA archive if open
        /// </summary>
        public void Close()
        {
            if (_bsaArchive != null)
            {
                _bsaArchive.Close();
                _bsaArchive = null;
            }
        }
        
        /// <summary>
        /// Converts DDS to PNG using Pfim library (reused from TESTerrain)
        /// </summary>
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
                using (var image = Pfim.Pfimage.FromStream(new MemoryStream(ddsData)))
                {
                    Color[] pixels = null;
                    int width = image.Width;
                    int height = image.Height;
                    pixels = new Color[width * height];
                    int stride = image.Stride;
                    
                    switch (image.Format)
                    {
                        case Pfim.ImageFormat.Rgba32:
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
                        case Pfim.ImageFormat.Rgb24:
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
                        default:
                            UnityEngine.Debug.LogError($"Cannot convert DDS format {image.Format}");
                            return false;
                    }

                    if (pixels == null || pixels.Length == 0)
                    {
                        UnityEngine.Debug.LogError("Failed to convert DDS pixel data");
                        return false;
                    }

                    // Create Unity texture in RGBA32 format (32-bit with alpha) for masking support (trees, etc.)
                    Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    texture.SetPixels(pixels);
                    texture.Apply();

                    byte[] pngData = texture.EncodeToPNG();
                    System.IO.File.WriteAllBytes(outputPngPath, pngData);
                    UnityEngine.Object.DestroyImmediate(texture);
                    
                    #if UNITY_EDITOR
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
        
        /// <summary>
        /// Normalizes texture path (similar to MWGE approach)
        /// - Removes null characters
        /// - Converts to lowercase
        /// - Preserves directory structure but normalizes separators
        /// Note: We keep the path as-is for BSA lookup, but normalize for file system
        /// </summary>
        private string NormalizeTexturePath(string texturePath)
        {
            if (string.IsNullOrEmpty(texturePath))
                return texturePath;
            
            // Clean null characters and whitespace
            string normalized = texturePath.TrimEnd('\0', ' ', '\t', '\r', '\n').Replace("\0", "").Trim();
            
            // Convert to lowercase (like MWGE does)
            normalized = normalized.ToLowerInvariant();
            
            // Normalize path separators
            normalized = normalized.Replace('/', '\\');
            
            // Remove leading backslashes
            normalized = normalized.TrimStart('\\', '/');
            
            return normalized;
        }
        
        #if UNITY_EDITOR
        /// <summary>
        /// Sets texture import settings to disable alpha source (prevents shininess in URP terrain)
        /// </summary>
        private void SetTextureImportSettings(string texturePath)
        {
            try
            {
                string assetPath = texturePath.Replace('\\', '/');
                if (assetPath.StartsWith(Application.dataPath))
                {
                    assetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);
                }
                
                if (!assetPath.StartsWith("Assets/"))
                    return;
                
                UnityEditor.TextureImporter importer = UnityEditor.AssetImporter.GetAtPath(assetPath) as UnityEditor.TextureImporter;
                if (importer != null)
                {
                    importer.alphaSource = UnityEditor.TextureImporterAlphaSource.None;
                    importer.SaveAndReimport();
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"Could not set texture import settings for {texturePath}: {ex.Message}");
            }
        }
        #endif
    }
}

