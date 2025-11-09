using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BSASharp;
using Niflib;
using Pfim;
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
        private (Mesh mesh, Material[] materials) CombineMeshes(List<NIFMesh> meshes, float vertexScale = 1.0f, Quaternion rotation = default(Quaternion), string esm = "")
        {
            if (meshes == null || meshes.Count == 0)
                return (null, null);

            // Default rotation: -90 degrees around X axis (Morrowind to Unity conversion)
            if (rotation == default(Quaternion))
            {
                rotation = Quaternion.Euler(-90f, 0f, 0f);
            }

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

                // Add vertices with scaling and rotation applied at mesh level
                foreach (Vector3 vertex in nifMesh.Vertices)
                {
                    Vector3 scaledVertex = new Vector3(
                        vertex.x * vertexScale,
                        vertex.y * vertexScale,
                        vertex.z * vertexScale
                    );
                    // Apply rotation to vertex
                    Vector3 rotatedVertex = rotation * scaledVertex;
                    combinedVertices.Add(rotatedVertex);
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
                    foreach (Vector3 normal in nifMesh.Normals)
                    {
                        // Apply rotation to normal
                        Vector3 rotatedNormal = rotation * normal;
                        combinedNormals.Add(rotatedNormal);
                    }
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
                Material submeshMat = CreateMaterialFromNIF(nifMesh.Material, nifMesh.Textures, esm);
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
        public GameObject LoadNIFFromCache(string modelFilename, bool combineMeshes = false)
        {
            string cachePath = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", _esm, modelFilename);
            if (!File.Exists(cachePath))
            {
                UnityEngine.Debug.LogError($"NIF file not found: {cachePath}");
                return null;
            }

            try
            {
                byte[] nifData = File.ReadAllBytes(cachePath);
                return LoadNIFFromBytes(nifData, System.IO.Path.GetFileName(cachePath), combineMeshes);
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
        private GameObject LoadNIFFromBytes(byte[] nifData, string filename, bool combineMeshes = false)
        {
            // Use niflib.net (primary loader)
            try
            {
                return LoadNIFFromBytesNiflib(nifData, filename, combineMeshes);
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
                
                // No fallback - return null so we can debug the issue
                return null;
            }
        }

        /// <summary>
        /// Creates a Unity material from NIF material and texture data
        /// </summary>
        private Material CreateMaterialFromNIF(NIFMaterial nifMaterial, Dictionary<string, NIFTexture> textures, string esm)
        {
            Material material = CreateURPMaterial("NIFMaterial", null, null, nifMaterial.DiffuseColor);
            bool textureLoaded = false;

            // Load textures
            if (textures.ContainsKey("BaseTexture") || textures.ContainsKey("Diffuse"))
            {
                NIFTexture baseTex = textures.ContainsKey("BaseTexture") ? textures["BaseTexture"] : textures["Diffuse"];
                if (baseTex.Enabled && !string.IsNullOrEmpty(baseTex.FilePath))
                {
                    UnityEngine.Debug.Log($"Loading texture for material: {baseTex.FilePath}");
                    Texture2D diffuseTexture = LoadTextureForModel(baseTex.FilePath, esm);
                    if (diffuseTexture != null)
                    {
                        UnityEngine.Debug.Log($"Successfully loaded texture: {baseTex.FilePath} ({diffuseTexture.width}x{diffuseTexture.height})");
                        if (material.shader.name.Contains("Universal Render Pipeline"))
                        {
                            material.SetTexture("_BaseMap", diffuseTexture);
                        }
                        else
                        {
                            material.SetTexture("_MainTex", diffuseTexture);
                        }
                        textureLoaded = true;
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"Failed to load texture: {baseTex.FilePath}, creating invisible material");
                    }
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"BaseTexture has empty or disabled path. Enabled: {baseTex?.Enabled}, Path: {baseTex?.FilePath}, creating invisible material");
                }
            }
            else
            {
                UnityEngine.Debug.LogWarning($"No BaseTexture or Diffuse texture found in textures dictionary. Available keys: {string.Join(", ", textures.Keys)}, creating invisible material");
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
            if (material.shader.name.Contains("Universal Render Pipeline"))
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
        /// </summary>
        private Texture2D LoadTextureForModel(string texturePath, string esm = "Morrowind")
        {
            if (string.IsNullOrEmpty(texturePath))
                return null;

            // Clean the texture path
            texturePath = texturePath.TrimEnd('\0', ' ', '\t', '\r', '\n');
            texturePath = texturePath.Replace("\0", "");
            texturePath = texturePath.Trim();

            string baseFilename = System.IO.Path.GetFileName(texturePath);
            string baseNameNoExt = System.IO.Path.GetFileNameWithoutExtension(baseFilename);
            string textureDir = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Textures", esm);

            // Try to find texture in cache (prefer PNG, then DDS, then original format)
            string[] extensions = new[] { ".png", ".dds", ".tga" };
            string originalExt = System.IO.Path.GetExtension(baseFilename);
            if (!string.IsNullOrEmpty(originalExt) && !extensions.Contains(originalExt.ToLower()))
            {
                extensions = new[] { originalExt.ToLower() }.Concat(extensions).ToArray();
            }

            string foundTexturePath = null;
            foreach (string ext in extensions)
            {
                string testPath = System.IO.Path.Combine(textureDir, baseNameNoExt + ext);
                if (File.Exists(testPath))
                {
                    foundTexturePath = testPath;
                    break;
                }
            }

            // If not found in cache, try to extract from BSA
            if (foundTexturePath == null)
            {
                foundTexturePath = ExtractTextureFromBSA(texturePath, textureDir, esm);
            }

            if (foundTexturePath != null)
            {
                return LoadTextureFromFile(foundTexturePath);
            }

            UnityEngine.Debug.LogWarning($"Texture not found: {texturePath}");
            return null;
        }

        /// <summary>
        /// Extracts a texture from BSA archive
        /// </summary>
        private string ExtractTextureFromBSA(string texturePath, string outputDir, string esm)
        {
            if (_bsaArchive == null)
            {
                string bsaPath = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", _bsa);
                if (!File.Exists(bsaPath))
                {
                    UnityEngine.Debug.LogWarning($"BSA file not found: {bsaPath}");
                    return null;
                }

                try
                {
                    _bsaArchive = new BSA();
                    _bsaArchive.Open(bsaPath);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"Failed to open BSA archive: {ex.Message}");
                    return null;
                }
            }

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
            
            string[] pathVariationsArray = pathVariations.ToArray();

            HashSet<string> bsaFileNames = new HashSet<string>(_bsaArchive.GetFileNames(), StringComparer.OrdinalIgnoreCase);
            string foundPath = null;

            UnityEngine.Debug.Log($"Searching BSA for texture: {texturePath} (normalized: {normalizedPath}, trying {pathVariationsArray.Length} variations)");
            foreach (string pathVar in pathVariationsArray)
            {
                if (bsaFileNames.Contains(pathVar))
                {
                    foundPath = pathVar;
                    UnityEngine.Debug.Log($"Found texture in BSA: {foundPath}");
                    break;
                }
            }

            // Case-insensitive fallback
            if (foundPath == null)
            {
                string textureLower = texturePath.ToLower();
                foreach (string bsaFileName in bsaFileNames)
                {
                    if (bsaFileName.ToLower().EndsWith("\\" + textureLower) ||
                        bsaFileName.ToLower().EndsWith("/" + textureLower) ||
                        bsaFileName.ToLower() == textureLower)
                    {
                        foundPath = bsaFileName;
                        break;
                    }
                }
            }

            if (foundPath != null)
            {
                try
                {
                    BSAFileEntry entry = _bsaArchive.GetFileEntry(foundPath);
                    if (entry != null)
                    {
                        byte[] textureData = _bsaArchive.ExtractFile(entry);
                        string outputBaseFilename = System.IO.Path.GetFileName(texturePath);
                        string outputBaseNameNoExt = System.IO.Path.GetFileNameWithoutExtension(outputBaseFilename);
                        string outputPath = System.IO.Path.Combine(outputDir, outputBaseNameNoExt + System.IO.Path.GetExtension(foundPath).ToLower());

                        File.WriteAllBytes(outputPath, textureData);
                        UnityEngine.Debug.Log($"Extracted texture from BSA: {foundPath} -> {outputPath}");

                        // If it's a DDS, try to convert to PNG
                        if (outputPath.ToLower().EndsWith(".dds"))
                        {
                            string pngPath = System.IO.Path.ChangeExtension(outputPath, ".png");
                            if (ConvertDDSToPNG(textureData, pngPath))
                            {
                                UnityEngine.Debug.Log($"Converted DDS to PNG: {outputPath} -> {pngPath}");
                                return pngPath; // Return PNG path instead
                            }
                        }
                        else if (outputPath.ToLower().EndsWith(".tga"))
                        {
                            // Convert TGA to PNG
                            string pngPath = System.IO.Path.ChangeExtension(outputPath, ".png");
                            Texture2D tempTexture = new Texture2D(2, 2);
                            if (tempTexture.LoadImage(textureData))
                            {
                                byte[] pngData = tempTexture.EncodeToPNG();
                                System.IO.File.WriteAllBytes(pngPath, pngData);
                                UnityEngine.Object.DestroyImmediate(tempTexture);
                                #if UNITY_EDITOR
                                SetTextureImportSettings(pngPath);
                                #endif
                                UnityEngine.Debug.Log($"Converted TGA to PNG: {outputPath} -> {pngPath}");
                                return pngPath; // Return PNG path instead
                            }
                            UnityEngine.Object.DestroyImmediate(tempTexture);
                        }

                        return outputPath;
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"Error extracting texture {foundPath}: {ex.Message}");
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
                    UnityEngine.Debug.LogWarning($"Failed to load DDS texture: {texturePath}");
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
        private Material CreateURPMaterial(string materialName, Texture2D diffuseTexture = null, Texture2D normalTexture = null, Color? diffuseColor = null)
        {
            // Try URP shaders first
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
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

            if (shader == null)
            {
                UnityEngine.Debug.LogError("Could not find suitable shader for material");
                return null;
            }

            Material material = new Material(shader);
            material.name = materialName;

            // Set URP properties
            if (shader.name.Contains("Universal Render Pipeline"))
            {
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
        private GameObject LoadNIFFromBytesNiflib(byte[] nifData, string filename, bool combineMeshes = false)
        {
            using (MemoryStream stream = new MemoryStream(nifData))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                // Parse NIF file using niflib.net
                Niflib.NiFile nifFile = new Niflib.NiFile(reader);
                
                // Extract meshes from all root nodes
                List<NIFMesh> meshes = new List<NIFMesh>();
                Dictionary<string, NIFTexture> textures = new Dictionary<string, NIFTexture>();
                NIFMaterial material = new NIFMaterial();
                
                // Get all root nodes
                foreach (var rootRef in nifFile.Footer.RootNodes)
                {
                    if (!rootRef.IsValid() || rootRef.Object == null)
                        continue;
                    
                    var rootNode = rootRef.Object as Niflib.NiNode;
                    if (rootNode == null)
                        continue;
                    
                    // Extract meshes from this root node tree (pass nifFile for reference resolution)
                    ExtractMeshesFromNode(rootNode, meshes, textures, material, nifFile);
                }
                
                if (meshes.Count == 0)
                {
                    UnityEngine.Debug.LogWarning($"No meshes found in NIF file (niflib.net): {filename}");
                    return null;
                }
                
                // Create root GameObject
                GameObject rootObj = new GameObject(System.IO.Path.GetFileNameWithoutExtension(filename));
                rootObj.transform.rotation = Quaternion.identity;
                
                // Morrowind NIF files use inches, Unity uses meters
                // For static objects: use MORROWIND_TO_TERRAIN_SCALE (64/8192 = 0.0078125) to match terrain coordinate system
                // For trees: use standard inches-to-meters conversion (0.0254) at mesh level
                const float INCHES_TO_METERS_STANDARD = 0.0254f;
                const float MORROWIND_TO_TERRAIN_SCALE = 64f / 8192f; // 0.0078125
                Quaternion meshRotation = Quaternion.Euler(-90f, 0f, 0f);
                
                // For trees (combineMeshes=true): scale at mesh level using standard conversion
                // For static objects (combineMeshes=false): scale at GameObject level using MORROWIND_TO_TERRAIN_SCALE
                // Note: XSCL record will multiply this base scale, so we use MORROWIND_TO_TERRAIN_SCALE here
                float vertexScale = combineMeshes ? INCHES_TO_METERS_STANDARD : 1.0f;
                float gameObjectScale = combineMeshes ? 1.0f : MORROWIND_TO_TERRAIN_SCALE;
                
                // Apply GameObject-level scale for static objects
                rootObj.transform.localScale = Vector3.one * gameObjectScale;
                
                if (combineMeshes)
                {
                    // Combine all meshes into a single mesh with submeshes (each with its own material)
                    var (combinedMesh, submeshMaterials) = CombineMeshes(meshes, vertexScale, meshRotation, _esm);
                    if (combinedMesh != null)
                    {
                        MeshFilter meshFilter = rootObj.AddComponent<MeshFilter>();
                        meshFilter.mesh = combinedMesh;
                        
                        MeshRenderer meshRenderer = rootObj.AddComponent<MeshRenderer>();
                        // Use sharedMaterials array for multiple materials (one per submesh)
                        meshRenderer.sharedMaterials = submeshMaterials;
                        
                        UnityEngine.Debug.Log($"Created combined NIF model (niflib.net): {filename} with {meshes.Count} mesh(es) combined into {combinedMesh.subMeshCount} submesh(es) on root");
                        return rootObj;
                    }
                }
                
                // Create separate GameObjects for each mesh (for static objects)
                for (int i = 0; i < meshes.Count; i++)
                {
                    NIFMesh nifMesh = meshes[i];
                    
                    GameObject meshObj = new GameObject(string.IsNullOrEmpty(nifMesh.Name) ? $"Mesh_{i}" : nifMesh.Name);
                    meshObj.transform.SetParent(rootObj.transform);
                    meshObj.transform.localPosition = Vector3.zero;
                    meshObj.transform.localRotation = Quaternion.identity;
                    meshObj.transform.localScale = Vector3.one;
                    
                    Mesh unityMesh = new Mesh();
                    unityMesh.name = nifMesh.Name;
                    
                    // Convert and scale vertices (vertexScale is 1.0f for static objects, scaling happens at GameObject level)
                    Vector3[] morrowindVertices = nifMesh.Vertices.ToArray();
                    Vector3[] unityVertices = new Vector3[morrowindVertices.Length];
                    for (int v = 0; v < morrowindVertices.Length; v++)
                    {
                        Vector3 scaledVertex = new Vector3(
                            morrowindVertices[v].x * vertexScale,
                            morrowindVertices[v].y * vertexScale,
                            morrowindVertices[v].z * vertexScale
                        );
                        unityVertices[v] = meshRotation * scaledVertex;
                    }
                    unityMesh.vertices = unityVertices;
                    unityMesh.triangles = nifMesh.Triangles.ToArray();
                    
                    if (nifMesh.UVs.Count > 0)
                        unityMesh.uv = nifMesh.UVs.ToArray();
                    
                    if (nifMesh.Normals.Count > 0)
                    {
                        Vector3[] morrowindNormals = nifMesh.Normals.ToArray();
                        Vector3[] unityNormals = new Vector3[morrowindNormals.Length];
                        for (int n = 0; n < morrowindNormals.Length; n++)
                        {
                            unityNormals[n] = meshRotation * morrowindNormals[n];
                        }
                        unityMesh.normals = unityNormals;
                    }
                    else
                    {
                        unityMesh.RecalculateNormals();
                    }
                    
                    if (nifMesh.Colors.Count > 0 && nifMesh.Colors.Count == nifMesh.Vertices.Count)
                        unityMesh.colors = nifMesh.Colors.ToArray();
                    
                    unityMesh.RecalculateBounds();
                    
                    MeshFilter meshFilter = meshObj.AddComponent<MeshFilter>();
                    meshFilter.mesh = unityMesh;
                    
                    MeshRenderer meshRenderer = meshObj.AddComponent<MeshRenderer>();
                    Material mat = CreateMaterialFromNIF(material, textures, _esm);
                    meshRenderer.material = mat;
                }
                
                UnityEngine.Debug.Log($"Created NIF model (niflib.net): {filename} with {meshes.Count} meshes");
                return rootObj;
            }
        }
        
        /// <summary>
        /// Extracts meshes from a NiNode tree using niflib.net
        /// </summary>
        private void ExtractMeshesFromNode(Niflib.NiNode node, List<NIFMesh> meshes, Dictionary<string, NIFTexture> textures, NIFMaterial material, Niflib.NiFile nifFile = null)
        {
            if (node == null)
                return;
            
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
                        ExtractMeshesFromNode(childNode, meshes, textures, material, nifFile);
                    }
                    
                    var triShape = childRef.Object as Niflib.NiTriShape;
                    if (triShape != null)
                    {
                        ExtractMeshFromTriShape(triShape, meshes, textures, material, nifFile);
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
        private void ExtractMeshFromTriShape(Niflib.NiTriShape triShape, List<NIFMesh> meshes, Dictionary<string, NIFTexture> textures, NIFMaterial material, Niflib.NiFile nifFile = null)
        {
            if (triShape == null || triShape.Data == null || !triShape.Data.IsValid() || triShape.Data.Object == null)
                return;
            
            var shapeData = triShape.Data.Object as Niflib.NiTriShapeData;
            if (shapeData == null || !shapeData.HasVertices || shapeData.Vertices == null || shapeData.Vertices.Length == 0)
                return;
            
            NIFMesh mesh = new NIFMesh();
            mesh.Name = triShape.Name != null ? triShape.Name.Value : "";
            
            // Convert vertices from niflib Vector3 to Unity Vector3
            // niflib.net uses Unity's Vector3 when UNITY is defined, so we can use it directly
            mesh.Vertices = new List<Vector3>();
            foreach (var v in shapeData.Vertices)
            {
                // niflib.net Vector3 is Unity's Vector3 when UNITY is defined
                mesh.Vertices.Add(new Vector3(v.x, v.y, v.z));
            }
            
            // Convert triangles
            if (shapeData.HasTriangles && shapeData.Triangles != null)
            {
                mesh.Triangles = new List<int>();
                foreach (var tri in shapeData.Triangles)
                {
                    // Triangle class has X, Y, Z as ushort properties
                    mesh.Triangles.Add((int)tri.X);
                    mesh.Triangles.Add((int)tri.Y);
                    mesh.Triangles.Add((int)tri.Z);
                }
            }
            
            // Convert normals
            if (shapeData.HasNormals && shapeData.Normals != null)
            {
                mesh.Normals = new List<Vector3>();
                foreach (var n in shapeData.Normals)
                {
                    // niflib.net Vector3 is Unity's Vector3 when UNITY is defined
                    mesh.Normals.Add(new Vector3(n.x, n.y, n.z));
                }
            }
            
            // Convert UVs
            if (shapeData.HasUV && shapeData.UVSets != null && shapeData.UVSets.Length > 0 && shapeData.UVSets[0] != null)
            {
                mesh.UVs = new List<Vector2>();
                foreach (var uv in shapeData.UVSets[0])
                {
                    // niflib.net Vector2 is Unity's Vector2 when UNITY is defined
                    // Flip V coordinate for Unity
                    mesh.UVs.Add(new Vector2(uv.x, 1f - uv.y));
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
            
            var texturingProp = ResolveProperty(triShape, typeof(Niflib.NiTexturingProperty)) as Niflib.NiTexturingProperty;
            if (texturingProp != null)
            {
                UnityEngine.Debug.Log($"Found NiTexturingProperty for mesh '{mesh.Name}' (via ResolveProperty), TextureCount={texturingProp.TextureCount}");
                
                // Check TextureCount (MWGE checks this)
                if (texturingProp.TextureCount > 0 && texturingProp.BaseTexture != null)
                {
                    UnityEngine.Debug.Log($"BaseTexture is not null, checking Source...");
                    // Extract BaseTexture (main diffuse texture) - MWGE uses GetTexture(0) which is BaseTexture
                    if (texturingProp.BaseTexture.Source != null)
                    {
                        // Manually resolve the reference if needed (FixRefs might not have resolved nested references in TexDesc)
                        var sourceRef = texturingProp.BaseTexture.Source;
                        if (sourceRef.IsValid() && sourceRef.Object == null && nifFile != null)
                        {
                            // Try to manually resolve the reference using the NiFile
                            UnityEngine.Debug.LogWarning($"BaseTexture.Source is valid (RefId={sourceRef.RefId}) but Object is null. Attempting manual resolution...");
                            try
                            {
                                sourceRef.SetRef(nifFile);
                                UnityEngine.Debug.Log($"Manually resolved BaseTexture.Source reference, Object={sourceRef.Object?.GetType().Name ?? "null"}");
                            }
                            catch (System.Exception ex)
                            {
                                UnityEngine.Debug.LogError($"Failed to manually resolve BaseTexture.Source reference (RefId={sourceRef.RefId}): {ex.Message}");
                            }
                        }
                        
                        UnityEngine.Debug.Log($"BaseTexture.Source is not null, IsValid={texturingProp.BaseTexture.Source.IsValid()}, RefId={texturingProp.BaseTexture.Source.RefId}, Object={texturingProp.BaseTexture.Source.Object?.GetType().Name ?? "null"}");
                        if (texturingProp.BaseTexture.Source.IsValid() && texturingProp.BaseTexture.Source.Object != null)
                        {
                            var sourceTex = texturingProp.BaseTexture.Source.Object as Niflib.NiSourceTexture;
                            if (sourceTex != null)
                            {
                                UnityEngine.Debug.Log($"NiSourceTexture found, UseExternal={sourceTex.UseExternal}, FileName='{sourceTex.FileName?.Value ?? "null"}'");
                                // MWGE checks IsTextureExternal() - we check UseExternal
                                if (sourceTex.UseExternal)
                                {
                                    if (sourceTex.FileName != null && !string.IsNullOrEmpty(sourceTex.FileName.Value))
                                    {
                                        string originalPath = sourceTex.FileName.Value;
                                        string texPath = NormalizeTexturePath(originalPath);
                                        UnityEngine.Debug.Log($"Texture path: original='{originalPath}', normalized='{texPath}'");
                                        if (!string.IsNullOrEmpty(texPath))
                                        {
                                            // Store in both per-mesh dictionary and shared dictionary (for backward compatibility)
                                            meshTextures["BaseTexture"] = new NIFTexture { FilePath = texPath, Enabled = true };
                                            if (!textures.ContainsKey("BaseTexture"))
                                            {
                                                textures["BaseTexture"] = new NIFTexture { FilePath = texPath, Enabled = true };
                                            }
                                            UnityEngine.Debug.Log($"✓ Extracted BaseTexture from NIF: {texPath} (external texture)");
                                        }
                                    }
                                    else
                                    {
                                        UnityEngine.Debug.LogWarning($"NiSourceTexture has UseExternal=true but FileName is null or empty");
                                    }
                                }
                                else
                                {
                                    UnityEngine.Debug.LogWarning($"NiSourceTexture is not external (UseExternal=false), skipping");
                                }
                            }
                            else
                            {
                                UnityEngine.Debug.LogWarning($"BaseTexture.Source.Object is not a NiSourceTexture: {texturingProp.BaseTexture.Source.Object?.GetType().Name ?? "null"}");
                            }
                        }
                        else
                        {
                            UnityEngine.Debug.LogWarning($"BaseTexture.Source is not valid or Object is null");
                        }
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"BaseTexture.Source is null");
                    }
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"NiTexturingProperty has TextureCount={texturingProp?.TextureCount ?? 0} or BaseTexture is null");
                }
            }
            else
            {
                UnityEngine.Debug.LogWarning($"No NiTexturingProperty found for mesh '{mesh.Name}' (checked object and parent chain)");
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
                        case Pfim.ImageFormat.Rgb24:
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
                        default:
                            UnityEngine.Debug.LogError($"Cannot convert DDS format {image.Format}");
                            return false;
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

