using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using ESMSharp.Core;

namespace ESMSharp.NIF
{
    /// <summary>
    /// NIF file version constants (Morrowind uses 4.0.0.2)
    /// </summary>
    public static class NIFVersion
    {
        public const uint VER_MW = 0x04000002; // 4.0.0.2 - Main Morrowind NIF version

        public static string VersionToString(uint version)
        {
            byte major = (byte)((version >> 24) & 0xFF);
            byte minor = (byte)((version >> 16) & 0xFF);
            byte patch = (byte)((version >> 8) & 0xFF);
            byte rev = (byte)(version & 0xFF);
            return $"{major}.{minor}.{patch}.{rev}";
        }
    }

    /// <summary>
    /// Represents a NIF mesh with vertices, triangles, UVs, and normals
    /// </summary>
    public class NIFMesh
    {
        public List<Vector3> Vertices = new List<Vector3>();
        public List<int> Triangles = new List<int>();
        public List<Vector2> UVs = new List<Vector2>();
        public List<Vector3> Normals = new List<Vector3>();
        public List<Color> Colors = new List<Color>();
        public string Name = "";
        
        // Per-mesh material and texture information
        public Dictionary<string, NIFTexture> Textures = new Dictionary<string, NIFTexture>();
        public NIFMaterial Material = new NIFMaterial();
        
        // Flag to indicate if this is a collision mesh (from RootCollisionNode or no material/texture)
        public bool IsCollisionMesh = false;
    }

    /// <summary>
    /// Represents texture information from a NIF file
    /// </summary>
    public class NIFTexture
    {
        public string FilePath = "";
        public bool Enabled = true;
        public int UVSet = 0;
    }

    /// <summary>
    /// Represents material information from a NIF file
    /// </summary>
    public class NIFMaterial
    {
        public Color AmbientColor = Color.white;
        public Color DiffuseColor = Color.white;
        public Color SpecularColor = Color.white;
        public Color EmissiveColor = Color.black;
        public float Glossiness = 0.0f;
        public float Alpha = 1.0f;
        public bool HasAlpha = false;
    }

    /// <summary>
    /// Internal structure to hold parsed NIF object data
    /// </summary>
    internal class NIFObjectData
    {
        public string Type = "";
        public long DataStart = 0;
        public long DataEnd = 0;
        public int Index = -1;
        public Dictionary<string, object> Fields = new Dictionary<string, object>();
    }

    /// <summary>
    /// Main NIF file parser for Morrowind NIF files
    /// Based on the structure from the Blender io_scene_mw plugin
    /// </summary>
    public class NIFParser
    {
        private BetterBinaryReader _reader;
        private uint _version = 0;
        private List<NIFObjectData> _objects = new List<NIFObjectData>();
        private Dictionary<int, NIFObjectData> _objectMap = new Dictionary<int, NIFObjectData>();

        /// <summary>
        /// Parses a NIF file and extracts mesh data
        /// </summary>
        public bool Parse(byte[] nifData, out List<NIFMesh> meshes, out Dictionary<string, NIFTexture> textures, out NIFMaterial material)
        {
            meshes = new List<NIFMesh>();
            textures = new Dictionary<string, NIFTexture>();
            material = new NIFMaterial();

            try
            {
                using (MemoryStream stream = new MemoryStream(nifData))
                {
                    _reader = new BetterBinaryReader(stream);
                    
                    // Read NIF header
                    if (!ReadHeader())
                    {
                        UnityEngine.Debug.LogError("Failed to read NIF header");
                        return false;
                    }

                    // UnityEngine.Debug.Log($"NIF Version: {NIFVersion.VersionToString(_version)}"); // Verbose, commented out for performance

                    // Read objects list
                    if (!ReadObjectsList())
                    {
                        UnityEngine.Debug.LogError("Failed to read NIF objects list");
                        return false;
                    }

                    // Read root links
                    ReadRootLinks();

                    // Extract texture file paths from NiSourceTexture objects
                    ExtractTextures(textures);

                    // Extract mesh data from NiTriShapeData objects
                    ExtractMeshes(meshes, textures, material);

                    return meshes.Count > 0;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error parsing NIF file: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Reads the NIF file header
        /// For Morrowind (version 0x04000002), the header contains:
        /// - Header string (ends with newline)
        /// - Version number (uint32) - if version >= 0x0303000D
        /// - Number of blocks (uint32) - if version >= 0x0303000D
        /// </summary>
        private bool ReadHeader()
        {
            try
            {
                // Read version string (e.g., "NetImmerse File Format, Version 4.0.0.2\n")
                string versionString = ReadVersionString();
                if (string.IsNullOrEmpty(versionString) || !versionString.Contains("NetImmerse File Format"))
                {
                    return false;
                }

                // Read version number (uint32) - for versions >= 0x0303000D (Morrowind is 0x04000002)
                _version = _reader.ReadUInt32();
                
                if (_version != NIFVersion.VER_MW)
                {
                    UnityEngine.Debug.LogWarning($"NIF version {NIFVersion.VersionToString(_version)} may not be fully supported. Expected {NIFVersion.VersionToString(NIFVersion.VER_MW)}");
                }

                // Read number of blocks (uint32) - for versions >= 0x0303000D (Morrowind is 0x04000002)
                // This is read here but we'll read it again in ReadObjectsList for clarity
                // Actually, we should read it here and pass it to ReadObjectsList, but for now we'll read it in ReadObjectsList
                // The important thing is that we don't skip it - it's part of the header

                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error reading NIF header: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reads the version string from NIF header (ends with newline)
        /// </summary>
        private string ReadVersionString()
        {
            List<byte> bytes = new List<byte>();
            byte b;
            while ((b = _reader.ReadByte()) != 0x0A) // '\n'
            {
                bytes.Add(b);
            }
            return System.Text.Encoding.ASCII.GetString(bytes.ToArray());
        }

        /// <summary>
        /// Reads a null-terminated string
        /// </summary>
        private string ReadNullTerminatedString()
        {
            List<byte> bytes = new List<byte>();
            byte b;
            while ((b = _reader.ReadByte()) != 0)
            {
                bytes.Add(b);
            }
            return System.Text.Encoding.ASCII.GetString(bytes.ToArray());
        }

        /// <summary>
        /// Reads a length-prefixed string (uint32 length + bytes)
        /// Used for strings within objects (like name fields) and object type names
        /// </summary>
        private string ReadLengthPrefixedString()
        {
            uint length = _reader.ReadUInt32();
            if (length == 0)
                return "";
            
            // Validate length to prevent overflow/underflow
            if (length > 1024 * 1024) // Max 1MB string (sanity check)
            {
                // Disabled for performance: UnityEngine.Debug.LogWarning($"Suspicious string length: {length}, treating as empty");
                return "";
            }
            
            byte[] bytes = _reader.ReadBytes((int)length);
            return System.Text.Encoding.ASCII.GetString(bytes);
        }

        /// <summary>
        /// Reads an object type name (for versions < 5.0.0.1, like Morrowind)
        /// Validates length according to niflib standards (6-30 characters)
        /// </summary>
        private string ReadObjectTypeName()
        {
            uint objectTypeLength = _reader.ReadUInt32();
            
            // According to niflib, object type names should be between 6-30 characters
            // If outside this range, we're likely misaligned
            if (objectTypeLength < 6 || objectTypeLength > 30)
            {
                throw new Exception($"Invalid object type name length: {objectTypeLength} (expected 6-30). File may be misaligned.");
            }
            
            byte[] bytes = _reader.ReadBytes((int)objectTypeLength);
            string objectType = System.Text.Encoding.ASCII.GetString(bytes);
            
            // Handle special commands for very old versions (Morrowind shouldn't have these, but check anyway)
            if (_version < 0x0303000D)
            {
                if (objectType == "Top Level Object")
                {
                    return null; // Signal to skip this object
                }
                if (objectType == "End Of File")
                {
                    return "EOF"; // Signal end of file
                }
            }
            
            return objectType;
        }

        /// <summary>
        /// Reads the objects list from the NIF file
        /// Structure: num_objects (uint), then for each object: type_name (length-prefixed string: uint32 length + bytes), object_data
        /// </summary>
        private bool ReadObjectsList()
        {
            try
            {
                uint numObjects = _reader.ReadUInt32();
                // UnityEngine.Debug.Log($"NIF file contains {numObjects} objects"); // Verbose, commented out for performance

                for (uint i = 0; i < numObjects; i++)
                {
                    long objStartPos = _reader.Position;
                    
                    // Read object type name (for Morrowind, this is a length-prefixed string with validation)
                    string objectType = null;
                    try
                    {
                        objectType = ReadObjectTypeName();
                        
                        // Handle special commands
                        if (objectType == null)
                        {
                            // "Top Level Object" - skip this object
                            continue;
                        }
                        if (objectType == "EOF")
                        {
                            // "End Of File" - stop reading
                            break;
                        }
                    }
                    catch (Exception)
                    {
                        // Try to recover by skipping bytes until we find a valid object type name
                        // Disabled for performance: UnityEngine.Debug.LogWarning($"Error reading object type name at position {objStartPos} (object {i}). Attempting recovery...");
                        
                        long recoveryStartPos = _reader.Position;
                        bool recovered = false;
                        
                        // Try skipping 1-200 bytes to find alignment (increased from 50)
                        for (int skipBytes = 1; skipBytes <= 200 && !recovered; skipBytes++)
                        {
                            _reader.Position = recoveryStartPos + skipBytes;
                            try
                            {
                                objectType = ReadObjectTypeName();
                                if (!string.IsNullOrEmpty(objectType) && objectType != "EOF" && objectType != null)
                                {
                                    // Disabled for performance: UnityEngine.Debug.LogWarning($"  Recovered alignment by skipping {skipBytes} bytes. Next object type: {objectType}");
                                    recovered = true;
                                }
                            }
                            catch
                            {
                                // Continue trying
                            }
                        }
                        
                        if (!recovered)
                        {
                            UnityEngine.Debug.LogError($"Could not recover alignment for object {i} after {recoveryStartPos}. Stopping object list parsing but meshes extracted so far will be used.");
                            // Don't throw - instead break out of the loop so we can still use the meshes we've extracted
                            break;
                        }
                    }
                    
                    NIFObjectData objData = new NIFObjectData
                    {
                        Type = objectType,
                        Index = (int)i,
                        DataStart = _reader.Position
                    };

                    // Parse object data based on type
                    try
                    {
                        if (objectType == "NiTriShapeData")
                        {
                            ParseNiTriShapeData(objData);
                        }
                        else if (objectType == "NiTriShape")
                        {
                            ParseNiTriShape(objData);
                        }
                        else if (objectType == "NiNode" || objectType == "RootCollisionNode")
                        {
                            // RootCollisionNode is just a NiNode variant
                            ParseNiNode(objData);
                        }
                        else if (objectType == "NiTexturingProperty")
                        {
                            ParseNiTexturingProperty(objData);
                        }
                        else if (objectType == "NiMaterialProperty")
                        {
                            ParseNiMaterialProperty(objData);
                        }
                        else if (objectType == "NiAlphaProperty")
                        {
                            ParseNiAlphaProperty(objData);
                        }
                        else if (objectType == "NiSourceTexture")
                        {
                            ParseNiSourceTexture(objData);
                        }
                        else if (objectType.StartsWith("Ni") && objectType.EndsWith("Property"))
                        {
                            // Generic property parser for other property types
                            ParseNiProperty(objData);
                        }
                        else if (string.IsNullOrEmpty(objectType))
                        {
                            // Empty type name should not happen after ReadObjectTypeName validation
                            // This indicates a serious problem
                            throw new Exception($"Object {i} has empty type name at position {objStartPos}. File may be corrupted or misaligned.");
                        }
                        else
                        {
                            // For unknown types, we need to skip them properly
                            // Since NIF objects don't have size fields, we can't easily skip
                            // For now, we'll try to parse minimal base class fields
                            SkipUnknownObject(objData);
                        }
                    }
                    catch (Exception)
                    {
                        // Disabled for performance: UnityEngine.Debug.LogWarning($"Error parsing object {i} ({objectType})");
                        // Try to recover by skipping to next object
                        // This is a fallback - ideally we'd parse everything correctly
                    }

                    objData.DataEnd = _reader.Position;
                    _objects.Add(objData);
                    _objectMap[(int)i] = objData;
                    
                    // Debug: Log first few objects to see what we're parsing
                    if (i < 10)
                    {
                        // UnityEngine.Debug.Log($"Object {i}: {objectType} (pos: {objStartPos}->{objData.DataEnd}, size: {objData.DataEnd - objStartPos})"); // Verbose, commented out for performance
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error reading NIF objects list: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Reads root links (indices to root objects)
        /// </summary>
        private void ReadRootLinks()
        {
            try
            {
                uint numRoots = _reader.ReadUInt32();
                for (uint i = 0; i < numRoots; i++)
                {
                    int rootIndex = _reader.ReadInt32();
                    // Store root indices if needed
                }
            }
            catch (Exception)
            {
                // Disabled for performance: UnityEngine.Debug.LogWarning($"Error reading root links");
            }
        }

        /// <summary>
        /// Parses NiTriShapeData (contains mesh geometry data)
        /// Based on NiGeometryData -> NiTriBasedGeomData -> NiTriShapeData hierarchy
        /// </summary>
        private void ParseNiTriShapeData(NIFObjectData objData)
        {
            long startPos = _reader.Position;

            try
            {
                // NiGeometryData fields (base class) - NiObject has no fields, so we start directly with NiGeometryData
                ushort numVertices = _reader.ReadUInt16();
                uint hasVertices = _reader.ReadUInt32(); // bool as uint (Morrowind uses 32-bit bools)
                
                if (hasVertices != 0 && numVertices > 0)
                {
                    List<Vector3> vertices = new List<Vector3>();
                    for (int i = 0; i < numVertices; i++)
                    {
                        float x = _reader.ReadSingle();
                        float y = _reader.ReadSingle();
                        float z = _reader.ReadSingle();
                        vertices.Add(new Vector3(x, y, z));
                    }
                    objData.Fields["vertices"] = vertices;
                }

                uint hasNormals = _reader.ReadUInt32(); // bool as uint
                if (hasNormals != 0 && numVertices > 0)
                {
                    List<Vector3> normals = new List<Vector3>();
                    for (int i = 0; i < numVertices; i++)
                    {
                        float x = _reader.ReadSingle();
                        float y = _reader.ReadSingle();
                        float z = _reader.ReadSingle();
                        normals.Add(new Vector3(x, y, z));
                    }
                    objData.Fields["normals"] = normals;
                }

                // Center and radius (bounding sphere)
                float centerX = _reader.ReadSingle();
                float centerY = _reader.ReadSingle();
                float centerZ = _reader.ReadSingle();
                float radius = _reader.ReadSingle();

                uint hasVertexColors = _reader.ReadUInt32(); // bool as uint
                if (hasVertexColors != 0 && numVertices > 0)
                {
                    List<Color> colors = new List<Color>();
                    for (int i = 0; i < numVertices; i++)
                    {
                        float r = _reader.ReadSingle();
                        float g = _reader.ReadSingle();
                        float b = _reader.ReadSingle();
                        float a = _reader.ReadSingle();
                        colors.Add(new Color(r, g, b, a));
                    }
                    objData.Fields["colors"] = colors;
                }

                // UV sets
                ushort numUVSets = _reader.ReadUInt16();
                uint hasUVSets = _reader.ReadUInt32(); // bool as uint
                if (hasUVSets != 0 && numUVSets > 0 && numVertices > 0)
                {
                    List<Vector2> uvs = new List<Vector2>();
                    for (int uvSet = 0; uvSet < numUVSets; uvSet++)
                    {
                        for (int i = 0; i < numVertices; i++)
                        {
                            float u = _reader.ReadSingle();
                            float v = _reader.ReadSingle();
                            if (uvSet == 0) // Use first UV set
                            {
                                uvs.Add(new Vector2(u, 1.0f - v)); // Flip V coordinate for Unity
                            }
                        }
                    }
                    if (uvs.Count > 0)
                    {
                        objData.Fields["uvs"] = uvs;
                    }
                }

                // NiTriBasedGeomData fields
                ushort numTriangles = _reader.ReadUInt16();

                // NiTriShapeData fields
                uint numTrianglePoints = _reader.ReadUInt32();
                if (numTriangles > 0 && numTrianglePoints > 0)
                {
                    List<int> triangles = new List<int>();
                    // numTrianglePoints is the total number of indices (should be numTriangles * 3)
                    int numIndices = (int)numTrianglePoints;
                    for (int i = 0; i < numIndices; i++)
                    {
                        ushort index = _reader.ReadUInt16();
                        triangles.Add(index);
                    }
                    objData.Fields["triangles"] = triangles;
                    // UnityEngine.Debug.Log($"NiTriShapeData: {numVertices} vertices, {numTriangles} triangles, {numIndices} indices"); // Verbose, commented out for performance
                }
                else
                {
                    // Disabled for performance: UnityEngine.Debug.LogWarning($"NiTriShapeData: Invalid triangle data - numTriangles={numTriangles}, numTrianglePoints={numTrianglePoints}");
                }

                // Shared normals (optional)
                ushort numSharedNormals = _reader.ReadUInt16();
                // Skip shared normals for now - they're arrays of ushort arrays
                for (int i = 0; i < numSharedNormals; i++)
                {
                    ushort sharedNormalCount = _reader.ReadUInt16();
                    _reader.Seek(sharedNormalCount * 2, SeekOrigin.Current); // Skip ushort array
                }

                objData.Fields["numVertices"] = numVertices;
                objData.Fields["numTriangles"] = numTriangles;
                
                long endPos = _reader.Position;
                // UnityEngine.Debug.Log($"Successfully parsed NiTriShapeData: {numVertices} vertices, {numTriangles} triangles (pos: {startPos}->{endPos}, size: {endPos - startPos})"); // Verbose, commented out for performance
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error parsing NiTriShapeData at position {startPos}: {ex.Message}\n{ex.StackTrace}");
                // Try to recover by seeking to a safe position (but we don't know where that is)
                // This will likely cause misalignment
            }
        }

        /// <summary>
        /// Parses NiTriShape (references NiTriShapeData)
        /// </summary>
        private void ParseNiTriShape(NIFObjectData objData)
        {
            // NiTriShape inherits from NiTriBasedGeom -> NiGeometry -> NiAVObject -> NiObjectNET -> NiObject
            // Parse base class fields
            ParseNiAVObjectBase(objData);
            
            // NiGeometry fields: data link (to NiTriShapeData), skinInstance link (for version >= 0x0303000D)
            try
            {
                // Data link (int32) - points to NiTriShapeData object
                int dataLink = _reader.ReadInt32();
                objData.Fields["dataLink"] = dataLink;
                
                // SkinInstance link (int32) - for version >= 0x0303000D (Morrowind is 0x04000002, so we need this)
                if (_version >= 0x0303000D)
                {
                    int skinInstanceLink = _reader.ReadInt32();
                    objData.Fields["skinInstanceLink"] = skinInstanceLink;
                }
            }
            catch (Exception)
            {
                // Disabled for performance: UnityEngine.Debug.LogWarning($"Error parsing NiTriShape fields");
            }
        }

        /// <summary>
        /// Parses NiNode (scene graph node)
        /// </summary>
        private void ParseNiNode(NIFObjectData objData)
        {
            // NiNode inherits from NiAVObject -> NiObjectNET -> NiObject
            // Parse base class fields
            ParseNiAVObjectBase(objData);
            
            // NiNode-specific fields: children (array of links), effects (array of links)
            try
            {
                // Read children array
                uint numChildren = _reader.ReadUInt32();
                for (uint i = 0; i < numChildren; i++)
                {
                    int childLink = _reader.ReadInt32(); // Skip child links
                }
                
                // Read effects array
                uint numEffects = _reader.ReadUInt32();
                for (uint i = 0; i < numEffects; i++)
                {
                    int effectLink = _reader.ReadInt32(); // Skip effect links
                }
            }
            catch (Exception)
            {
                // Disabled for performance: UnityEngine.Debug.LogWarning($"Error parsing NiNode fields");
            }
        }

        /// <summary>
        /// Parses NiAVObject base class fields (used by NiNode, NiTriShape, etc.)
        /// NiAVObject inherits from NiObjectNET
        /// </summary>
        private void ParseNiAVObjectBase(NIFObjectData objData)
        {
            try
            {
                // NiObjectNET fields: name (string), extra_data (link), controller (link)
                string name = ReadLengthPrefixedString();
                int extraDataLink = _reader.ReadInt32();
                int controllerLink = _reader.ReadInt32();
                
                // NiAVObject fields: flags (ushort), translation (Vector3), rotation (Matrix33), scale (float), velocity (Vector3), properties (array), bounding_volume (conditional)
                ushort flags = _reader.ReadUInt16();
                
                // Translation (Vector3)
                _reader.ReadSingle(); // X
                _reader.ReadSingle(); // Y
                _reader.ReadSingle(); // Z
                
                // Rotation (Matrix33 - 9 floats)
                for (int i = 0; i < 9; i++)
                {
                    _reader.ReadSingle();
                }
                
                // Scale (float)
                _reader.ReadSingle();
                
                // Velocity (Vector3)
                _reader.ReadSingle(); // X
                _reader.ReadSingle(); // Y
                _reader.ReadSingle(); // Z
                
                // Properties array
                uint numProperties = _reader.ReadUInt32();
                for (uint i = 0; i < numProperties; i++)
                {
                    int propertyLink = _reader.ReadInt32(); // Skip property links
                }
                
                // Bounding volume (conditional bool + optional object)
                uint hasBoundingVolume = _reader.ReadUInt32(); // bool as uint (Morrowind uses 32-bit bools)
                if (hasBoundingVolume != 0)
                {
                    // Bounding volume is an embedded object with a type string
                    // Read the type and skip it (we don't need bounding volumes for mesh extraction)
                    string boundingVolumeType = ReadLengthPrefixedString();
                    // Skip the bounding volume data - most are simple (NiBoxBV, NiSphereBV, etc.)
                    // For now, just skip a reasonable amount (most are small)
                    // This is a heuristic - ideally we'd parse it properly
                    SkipUnknownObject(new NIFObjectData { Type = boundingVolumeType });
                }
            }
            catch (Exception)
            {
                // Disabled for performance: UnityEngine.Debug.LogWarning($"Error parsing NiAVObject base fields");
            }
        }

        /// <summary>
        /// Parses NiTexturingProperty (contains texture information)
        /// Inherits from NiProperty -> NiObjectNET
        /// For Morrowind version 4.0.0.2, the structure is complex and version-dependent
        /// </summary>
        private void ParseNiTexturingProperty(NIFObjectData objData)
        {
            long startPos = _reader.Position;
            
            // Parse base class fields
            ParseNiProperty(objData);
            
            long afterBasePos = _reader.Position;
            
            // NiTexturingProperty is complex and version-dependent
            // For Morrowind 4.0.0.2, based on Python implementation:
            // After base class: apply_mode (int32), then 8 texture slots
            // Each slot: hasTexture (bool as uint32), then if true: NiTexturingPropertyMap
            // NiTexturingPropertyMap: source (int32), clamp_mode (int32), filter_mode (int32), uv_set (uint32), 
            //   ps2_l (int16), ps2_k (int16), unknown_byte1 (byte), unknown_byte2 (byte)
            try
            {
                // Apply mode (int32) - texture apply mode
                int applyMode = _reader.ReadInt32();
                objData.Fields["applyMode"] = applyMode;
                
                // Note: For Morrowind 4.0.0.2, there's no num_texture_maps field
                // We always have 8 texture slots (BASE_MAP, DARK_MAP, DETAIL_MAP, GLOSS_MAP, GLOW_MAP, BUMP_MAP, DECAL_0, DECAL_1)
                // For each slot: hasTexture (bool as uint32 in Morrowind), then if true: NiTexturingPropertyMap
                int texturesFound = 0;
                for (int slot = 0; slot < 8; slot++)
                {
                    long slotStartPos = _reader.Position;
                    uint hasTexture = _reader.ReadUInt32(); // bool as uint32
                    if (hasTexture != 0)
                    {
                        texturesFound++;
                        // Parse NiTexturingPropertyMap structure for Morrowind 4.0.0.2
                        // Based on Python: source (int32), clamp_mode (int32), filter_mode (int32), uv_set (uint32),
                        //   ps2_l (int16), ps2_k (int16), unknown_byte1 (byte), unknown_byte2 (byte)
                        
                        // Source link (int32) - points to NiSourceTexture object
                        // Note: -1 (0xFFFFFFFF) means no texture
                        int sourceLink = _reader.ReadInt32();
                        
                        // Validate sourceLink (should be -1 or a valid object index)
                        // Object indices are 0-based and should be less than the total number of objects
                        // If it's a huge number, we're likely misaligned
                        if (sourceLink != -1 && (sourceLink < 0 || sourceLink > 10000))
                        {
                            // Disabled for performance: UnityEngine.Debug.LogWarning($"  Texture slot {slot}: Invalid sourceLink value {sourceLink}, likely misaligned. Skipping this slot.");
                            // Try to skip the rest of the NiTexturingPropertyMap structure to maintain alignment
                            // Map structure: source (4) + clamp_mode (4) + filter_mode (4) + uv_set (4) + ps2_l (2) + ps2_k (2) + unknown_byte1 (1) + unknown_byte2 (1) = 22 bytes
                            // We've read 4 bytes (sourceLink), so skip remaining 18 bytes
                            _reader.Seek(18, SeekOrigin.Current);
                            continue;
                        }
                        
                        // Store source link for later texture extraction (only if valid)
                        if (sourceLink >= 0)
                        {
                            if (!objData.Fields.ContainsKey("textureLinks"))
                            {
                                objData.Fields["textureLinks"] = new List<int>();
                            }
                            ((List<int>)objData.Fields["textureLinks"]).Add(sourceLink);
                            objData.Fields[$"textureSlot_{slot}_link"] = sourceLink;
                        }
                        
                        // Clamp mode (int32)
                        int clampMode = _reader.ReadInt32();
                        
                        // Filter mode (int32)
                        int filterMode = _reader.ReadInt32();
                        
                        // UV set (uint32)
                        uint uvSet = _reader.ReadUInt32();
                        
                        // PS2 specific fields (int16, int16)
                        short ps2L = _reader.ReadInt16();
                        short ps2K = _reader.ReadInt16();
                        
                        // Unknown bytes (byte, byte) - NOT ushort!
                        byte unknownByte1 = _reader.ReadByte();
                        byte unknownByte2 = _reader.ReadByte();
                        
                        long slotEndPos = _reader.Position;
                        int slotSize = (int)(slotEndPos - slotStartPos);
                        // UnityEngine.Debug.Log($"  Texture slot {slot}: sourceLink={sourceLink}, size={slotSize} bytes (hasTexture=4, map=22)"); // Verbose, commented out for performance
                    }
                }
                
                // UnityEngine.Debug.Log($"NiTexturingProperty: {texturesFound} textures found"); // Verbose, commented out for performance
                
                long endPos = _reader.Position;
                long totalSize = endPos - startPos;
                
                // Calculate expected size: base (12) + flags (2) + 8 slots * 4 (hasTexture) + texture data
                // For 3 textures at 66 bytes each: 12 + 2 + 32 + 198 = 244 bytes
                // But object is 255 bytes, so we're missing 11 bytes
                // This might be padding or additional fields we're not aware of
                
                // Check if we need to read more bytes to match the expected object size
                // The object size is determined by the next object's start position
                // If we're not aligned, there might be padding or additional fields
                // For now, we'll log a warning if the size seems off, but continue
                // The recovery logic in ReadObjectsList will handle misalignment
                
                // UnityEngine.Debug.Log($"NiTexturingProperty parsed: {totalSize} bytes"); // Verbose, commented out for performance
                // UnityEngine.Debug.Log($"  Base class: {afterBasePos - startPos} bytes, apply_mode: 4 bytes, Data: {endPos - afterBasePos} bytes"); // Verbose, commented out for performance
                // UnityEngine.Debug.Log($"  Textures: {texturesFound}, 8 slots: 32 bytes (hasTexture flags), {texturesFound} maps: {texturesFound * 22} bytes"); // Verbose, commented out for performance
                
                // Store the end position so ReadObjectsList can verify alignment
                objData.DataEnd = _reader.Position;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error parsing NiTexturingProperty fields at position {startPos}: {ex.Message}\n{ex.StackTrace}");
                // Try to recover by seeking to a known position, but we don't know where that is
                // This will likely cause misalignment
                throw;
            }
        }

        /// <summary>
        /// Parses NiSourceTexture (contains texture file path)
        /// Inherits from NiTexture -> NiObjectNET -> NiObject
        /// </summary>
        private void ParseNiSourceTexture(NIFObjectData objData)
        {
            try
            {
                // Parse base class fields (NiObjectNET: name, extra_data link, controller link)
                ParseNiObjectNETBase(objData);
                
                // NiSourceTexture fields for Morrowind 4.0.0.2:
                // useExternal (byte) - whether texture is external file
                byte useExternal = _reader.ReadByte();
                objData.Fields["useExternal"] = useExternal;
                
                string fileName = "";
                if (useExternal != 0)
                {
                    // External texture - read filename (length-prefixed string)
                    fileName = ReadLengthPrefixedString();
                    objData.Fields["fileName"] = fileName;
                    // UnityEngine.Debug.Log($"NiSourceTexture: External texture file: {fileName}"); // Verbose, commented out for performance
                }
                else
                {
                    // Embedded texture - has pixel data link
                    byte hasPixelData = _reader.ReadByte();
                    if (hasPixelData != 0)
                    {
                        int pixelDataLink = _reader.ReadInt32();
                        objData.Fields["pixelDataLink"] = pixelDataLink;
                        // UnityEngine.Debug.Log($"NiSourceTexture: Embedded texture (pixel data link: {pixelDataLink})"); // Verbose, commented out for performance
                    }
                }
                
                // Pixel layout (int32)
                int pixelLayout = _reader.ReadInt32();
                objData.Fields["pixelLayout"] = pixelLayout;
                
                // Use mipmaps (int32)
                int useMipmaps = _reader.ReadInt32();
                objData.Fields["useMipmaps"] = useMipmaps;
                
                // Alpha format (int32)
                int alphaFormat = _reader.ReadInt32();
                objData.Fields["alphaFormat"] = alphaFormat;
                
                // Is static (byte)
                byte isStatic = _reader.ReadByte();
                objData.Fields["isStatic"] = isStatic;
            }
            catch (Exception)
            {
                // Disabled for performance: UnityEngine.Debug.LogWarning($"Error parsing NiSourceTexture fields");
            }
        }

        /// <summary>
        /// Parses NiProperty base class fields
        /// NiProperty inherits from NiObjectNET
        /// </summary>
        private void ParseNiProperty(NIFObjectData objData)
        {
            // Parse NiObjectNET base class: name, extra_data link, controller link
            ParseNiObjectNETBase(objData);
            
            // NiProperty has no additional fields in Morrowind
            // Subclasses will override this and add their own fields
        }

        /// <summary>
        /// Parses NiObjectNET base class fields (name, extra_data link, controller link)
        /// </summary>
        private void ParseNiObjectNETBase(NIFObjectData objData)
        {
            try
            {
                // Name (length-prefixed string)
                string name = ReadLengthPrefixedString();
                objData.Fields["name"] = name;
                
                // Extra data link (int32)
                int extraDataLink = _reader.ReadInt32();
                objData.Fields["extraDataLink"] = extraDataLink;
                
                // Controller link (int32)
                int controllerLink = _reader.ReadInt32();
                objData.Fields["controllerLink"] = controllerLink;
            }
            catch (Exception)
            {
                // Disabled for performance: UnityEngine.Debug.LogWarning($"Error parsing NiObjectNET base fields");
            }
        }

        /// <summary>
        /// Parses NiAlphaProperty (controls alpha blending)
        /// Inherits from NiProperty -> NiObjectNET
        /// </summary>
        private void ParseNiAlphaProperty(NIFObjectData objData)
        {
            // Parse base class fields
            ParseNiProperty(objData);
            
            // NiAlphaProperty specific fields
            try
            {
                // Flags (ushort) - alpha blending flags
                ushort flags = _reader.ReadUInt16();
                objData.Fields["flags"] = flags;
                
                // Threshold (byte) - alpha test threshold
                byte threshold = _reader.ReadByte();
                objData.Fields["threshold"] = threshold;
            }
            catch (Exception)
            {
                // Disabled for performance: UnityEngine.Debug.LogWarning($"Error parsing NiAlphaProperty fields");
            }
        }

        /// <summary>
        /// Parses NiMaterialProperty (contains material information)
        /// Inherits from NiProperty -> NiObjectNET
        /// </summary>
        private void ParseNiMaterialProperty(NIFObjectData objData)
        {
            // Parse base class fields
            ParseNiProperty(objData);
            
            // NiMaterialProperty specific fields for Morrowind
            try
            {
                // Ambient color (Color3 - 3 floats)
                float ambientR = _reader.ReadSingle();
                float ambientG = _reader.ReadSingle();
                float ambientB = _reader.ReadSingle();
                
                // Diffuse color (Color3 - 3 floats)
                float diffuseR = _reader.ReadSingle();
                float diffuseG = _reader.ReadSingle();
                float diffuseB = _reader.ReadSingle();
                
                // Specular color (Color3 - 3 floats)
                float specularR = _reader.ReadSingle();
                float specularG = _reader.ReadSingle();
                float specularB = _reader.ReadSingle();
                
                // Emissive color (Color3 - 3 floats)
                float emissiveR = _reader.ReadSingle();
                float emissiveG = _reader.ReadSingle();
                float emissiveB = _reader.ReadSingle();
                
                // Glossiness (float)
                float glossiness = _reader.ReadSingle();
                
                // Alpha (float)
                float alpha = _reader.ReadSingle();
                
                objData.Fields["ambientColor"] = new Color(ambientR, ambientG, ambientB);
                objData.Fields["diffuseColor"] = new Color(diffuseR, diffuseG, diffuseB);
                objData.Fields["specularColor"] = new Color(specularR, specularG, specularB);
                objData.Fields["emissiveColor"] = new Color(emissiveR, emissiveG, emissiveB);
                objData.Fields["glossiness"] = glossiness;
                objData.Fields["alpha"] = alpha;
            }
            catch (Exception)
            {
                // Disabled for performance: UnityEngine.Debug.LogWarning($"Error parsing NiMaterialProperty fields");
            }
        }

        /// <summary>
        /// Parses common base class fields for unknown objects
        /// Most NIF objects inherit from NiObjectNET (has name, extra_data link, controller link)
        /// This allows us to at least read through the file without completely misaligning
        /// </summary>
        private void SkipUnknownObject(NIFObjectData objData)
        {
            // Try to parse NiObjectNET base class fields (most objects inherit from this)
            // NiObjectNET has: name (length-prefixed string), extra_data (link/int), controller (link/int)
            try
            {
                ParseNiObjectNETBase(objData);
                
                // After NiObjectNET, we don't know what fields this object has
                // This is a fallback - ideally all object types should have proper parsers
                // Disabled for performance: UnityEngine.Debug.LogWarning($"Skipping unknown object type: {objData.Type} (only parsed base class fields)");
            }
            catch (Exception)
            {
                // Disabled for performance: UnityEngine.Debug.LogWarning($"Error skipping unknown object {objData.Type}");
                // If we can't parse, the file will become misaligned
                // This is a fundamental limitation without full type definitions
            }
        }

        /// <summary>
        /// Extracts texture file paths from NiSourceTexture objects and maps them to texture slots
        /// </summary>
        private void ExtractTextures(Dictionary<string, NIFTexture> textures)
        {
            // First, collect all NiSourceTexture objects by their index
            Dictionary<int, string> sourceTextureFiles = new Dictionary<int, string>();
            int sourceTextureCount = 0;
            foreach (NIFObjectData objData in _objects)
            {
                if (objData.Type == "NiSourceTexture")
                {
                    sourceTextureCount++;
                    if (objData.Fields.ContainsKey("fileName"))
                    {
                        string fileName = (string)objData.Fields["fileName"];
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            sourceTextureFiles[objData.Index] = fileName;
                            // UnityEngine.Debug.Log($"Found NiSourceTexture at index {objData.Index}: {fileName}"); // Verbose, commented out for performance
                        }
                    }
                    else
                    {
                        // Disabled for performance: UnityEngine.Debug.LogWarning($"NiSourceTexture at index {objData.Index} has no fileName field");
                    }
                }
            }
            
            // UnityEngine.Debug.Log($"ExtractTextures: Found {sourceTextureCount} NiSourceTexture objects, {sourceTextureFiles.Count} with file names"); // Verbose, commented out for performance

            // Now, find all NiTexturingProperty objects and map their texture slots to file paths
            int texturingPropertyCount = 0;
            foreach (NIFObjectData objData in _objects)
            {
                if (objData.Type == "NiTexturingProperty")
                {
                    texturingPropertyCount++;
                    // Check each texture slot
                    int validSlots = 0;
                    for (int slot = 0; slot < 8; slot++)
                    {
                        string linkKey = $"textureSlot_{slot}_link";
                        if (objData.Fields.ContainsKey(linkKey))
                        {
                            int sourceLink = (int)objData.Fields[linkKey];
                            if (sourceLink >= 0)
                            {
                                if (sourceTextureFiles.ContainsKey(sourceLink))
                                {
                                    string fileName = sourceTextureFiles[sourceLink];
                                    
                                    // Map texture slot to texture info
                                    string textureKey = GetTextureSlotName(slot);
                                    if (!textures.ContainsKey(textureKey))
                                    {
                                        NIFTexture texture = new NIFTexture
                                        {
                                            FilePath = fileName,
                                            Enabled = true,
                                            UVSet = 0
                                        };
                                        textures[textureKey] = texture;
                                        // UnityEngine.Debug.Log($"Mapped texture slot {slot} ({textureKey}) to file: {fileName} (sourceLink={sourceLink})"); // Verbose, commented out for performance
                                        validSlots++;
                                    }
                                }
                                else
                                {
                                    // Disabled for performance: UnityEngine.Debug.LogWarning($"NiTexturingProperty: Texture slot {slot} has sourceLink={sourceLink} but no NiSourceTexture found at that index");
                                }
                            }
                        }
                    }
                    // UnityEngine.Debug.Log($"NiTexturingProperty at index {objData.Index}: {validSlots} valid texture slots mapped"); // Verbose, commented out for performance
                }
            }
            
            // UnityEngine.Debug.Log($"ExtractTextures complete: Found {texturingPropertyCount} NiTexturingProperty objects, mapped {textures.Count} textures"); // Verbose, commented out for performance
        }

        /// <summary>
        /// Gets the texture slot name for a given slot index
        /// </summary>
        private string GetTextureSlotName(int slot)
        {
            switch (slot)
            {
                case 0: return "BaseTexture";
                case 1: return "DarkTexture";
                case 2: return "DetailTexture";
                case 3: return "GlossTexture";
                case 4: return "GlowTexture";
                case 5: return "BumpTexture";
                case 6: return "DecalTexture0";
                case 7: return "DecalTexture1";
                default: return $"TextureSlot{slot}";
            }
        }

        /// <summary>
        /// Extracts mesh data from parsed NiTriShapeData objects
        /// </summary>
        private void ExtractMeshes(List<NIFMesh> meshes, Dictionary<string, NIFTexture> textures, NIFMaterial material)
        {
            int niTriShapeDataCount = 0;
            int totalObjects = _objects.Count;
            // UnityEngine.Debug.Log($"ExtractMeshes: Processing {totalObjects} total objects"); // Verbose, commented out for performance
            
            foreach (NIFObjectData objData in _objects)
            {
                if (objData.Type == "NiTriShapeData")
                {
                    niTriShapeDataCount++;
                    // UnityEngine.Debug.Log($"Found NiTriShapeData object {niTriShapeDataCount} (index {objData.Index}), Fields: {string.Join(", ", objData.Fields.Keys)}"); // Verbose, commented out for performance
                    
                    if (objData.Fields.ContainsKey("vertices"))
                    {
                        NIFMesh mesh = new NIFMesh();
                        
                        List<Vector3> vertices = (List<Vector3>)objData.Fields["vertices"];
                        mesh.Vertices = vertices;
                        // UnityEngine.Debug.Log($"  Vertices: {vertices.Count}"); // Verbose, commented out for performance
                        
                        if (objData.Fields.ContainsKey("normals"))
                        {
                            List<Vector3> normals = (List<Vector3>)objData.Fields["normals"];
                            mesh.Normals = normals;
                            // UnityEngine.Debug.Log($"  Normals: {normals.Count}"); // Verbose, commented out for performance
                        }
                        
                        if (objData.Fields.ContainsKey("uvs"))
                        {
                            List<Vector2> uvs = (List<Vector2>)objData.Fields["uvs"];
                            mesh.UVs = uvs;
                            // UnityEngine.Debug.Log($"  UVs: {uvs.Count}"); // Verbose, commented out for performance
                        }
                        
                        if (objData.Fields.ContainsKey("colors"))
                        {
                            List<Color> colors = (List<Color>)objData.Fields["colors"];
                            mesh.Colors = colors;
                            // UnityEngine.Debug.Log($"  Colors: {colors.Count}"); // Verbose, commented out for performance
                        }
                        
                        if (objData.Fields.ContainsKey("triangles"))
                        {
                            List<int> triangleIndices = (List<int>)objData.Fields["triangles"];
                            mesh.Triangles = triangleIndices;
                            // UnityEngine.Debug.Log($"  Triangles: {triangleIndices.Count} indices ({triangleIndices.Count / 3} triangles)"); // Verbose, commented out for performance
                        }
                        else
                        {
                            // Disabled for performance: UnityEngine.Debug.LogWarning($"  No triangles field found in NiTriShapeData");
                        }

                        if (mesh.Vertices.Count > 0)
                        {
                            if (mesh.Triangles.Count > 0)
                            {
                                mesh.Name = $"Mesh_{meshes.Count}";
                                meshes.Add(mesh);
                                // UnityEngine.Debug.Log($"  ✓ Extracted mesh: {mesh.Vertices.Count} vertices, {mesh.Triangles.Count / 3} triangles"); // Verbose, commented out for performance
                            }
                            else
                            {
                                // Disabled for performance: UnityEngine.Debug.LogWarning($"  ✗ NiTriShapeData has {mesh.Vertices.Count} vertices but no triangles");
                            }
                        }
                        else
                        {
                            // Disabled for performance: UnityEngine.Debug.LogWarning($"  ✗ NiTriShapeData has no vertices");
                        }
                    }
                    else
                    {
                        // Disabled for performance: UnityEngine.Debug.LogWarning($"  ✗ NiTriShapeData object found but has no vertices field. Available fields: {string.Join(", ", objData.Fields.Keys)}");
                        // Disabled for performance: UnityEngine.Debug.LogWarning($"    Object index: {objData.Index}, Type: {objData.Type}, DataStart: {objData.DataStart}, DataEnd: {objData.DataEnd}");
                    }
                }
            }

            // UnityEngine.Debug.Log($"ExtractMeshes complete: Found {niTriShapeDataCount} NiTriShapeData objects, extracted {meshes.Count} meshes"); // Verbose, commented out for performance
            
            if (meshes.Count == 0)
            {
                // Disabled for performance: UnityEngine.Debug.LogWarning($"No meshes extracted from NIF file. Found {niTriShapeDataCount} NiTriShapeData objects out of {totalObjects} total objects. The file may use a different structure or the parser needs enhancement.");
            }
        }
    }

    /// <summary>
    /// Basic NIF record structure (kept for compatibility)
    /// </summary>
    internal class NIFRecord
    {
        public string Type = "";
        public long StartPosition = 0;
        public uint Size = 0;
    }
}
