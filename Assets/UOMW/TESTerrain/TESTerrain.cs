using ESMSharp.TES3;
using ESMSharp.TES3.Records;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace ESMSharp.TES3Terrain
{
    public partial class TESTerrain
    {
        int MinCellX, MaxCellX, MinCellY, MaxCellY;
        private static bool _debugXStitchingLogged = false; // Track if we've logged X-direction stitching
        private static int _debugCellCount = 0; // Track number of cells logged for debug

        public TESTerrain() { }

        public TESTerrain(int minX, int maxX, int minY, int maxY)
        {
            MinCellX = minX; MaxCellX = maxX; MinCellY = minY; MaxCellY = maxY;
        }

        public void GenerateHeightMap(Record[] _records)
        {

            //figure out texture size
            int width = (Math.Abs(Convert.ToInt32(MinCellX)) + Convert.ToInt32(MaxCellX)) * 65;
            int height = (Math.Abs(Convert.ToInt32(MinCellY)) + Convert.ToInt32(MaxCellY)) * 65;
            Texture2D newTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Texture2D normTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Texture2D heightTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);

            UnityEngine.Debug.Log("Texure size: " + width + " x " + height);

            Color[] pixels = new Color[width * height];
            Color[] npixels = new Color[width * height];
            Color[] heightPixels = new Color[width * height];

            // First pass: collect all absolute heights to find global min/max
            // Use float for precision (offset is float, so maintain precision)
            float[][] globalHeights = new float[height][];
            float[][] globalHeightsFromNormals = new float[height][]; // EXPERIMENTAL: Heights from normals
            int[][] globalHeightsCount = new int[height][]; // Track how many times each pixel was written (for averaging)
            VNML[][] globalNormals = new VNML[height][]; // Store normals for boundary correction
            
            for (int i = 0; i < height; i++)
            {
                globalHeights[i] = new float[width];
                globalHeightsFromNormals[i] = new float[width];
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


                    BlitCellJagged_DeltasGray_NoSeams(pixels, width, height, cellx, celly, MinCellX, MaxCellX, MinCellY, MaxCellY, heightdata /*byte[65][65]*/);
                    BlitCellNormals_NoSeams(npixels, width, height, cellx, celly, MinCellX, MaxCellX,MinCellY, MaxCellY, normdata /*byte[65][65]*/);
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

            newTexture.SetPixels(pixels);
            newTexture.Apply();
            byte[] bytes = newTexture.EncodeToPNG(); // or texture2D.EncodeToJPG(quality)
            string filePath = Application.dataPath + "/StreamingAssets/Data/MapBumps.png"; // Or any desired path
            System.IO.File.WriteAllBytes(filePath, bytes);

            normTexture.SetPixels(npixels);
            normTexture.Apply();
            byte[] nbytes = normTexture.EncodeToPNG(); // or texture2D.EncodeToJPG(quality)
            string nfilePath = Application.dataPath + "/StreamingAssets/Data/MapNorms.png"; // Or any desired path
            System.IO.File.WriteAllBytes(nfilePath, nbytes);

            heightTexture.SetPixels(heightPixels);
            heightTexture.Apply();
            byte[] hbytes = heightTexture.EncodeToPNG(); // or texture2D.EncodeToJPG(quality)
            string hfilePath = Application.dataPath + "/StreamingAssets/Data/MapHeight.png"; // Or any desired path
            System.IO.File.WriteAllBytes(hfilePath, hbytes);
            
            // Export RAW file for Unity terrain (16-bit grayscale, little-endian)
            if (foundValidHeight)
            {
                // Calculate height range for normalization
                float globalHeightRange = globalMaxHeight - globalMinHeight;
                if (globalHeightRange < 0.001f) globalHeightRange = 1f; // Avoid division by zero
                
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
                            // Normalize to 0-1 range
                            float normalizedHeight = (h - globalMinHeight) / globalHeightRange;
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
                
                string rawFilePath = Application.dataPath + "/StreamingAssets/Data/MapHeight.raw";
                System.IO.File.WriteAllBytes(rawFilePath, rawBytes);
                UnityEngine.Debug.Log($"Exported RAW heightmap to: {rawFilePath} (Size: {width}x{height}, 16-bit)");
            }

            // Export cell offsets as solid colors (for visualization)
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

            // EXPERIMENTAL: Export heightmap generated from normals
            // This uses the normal-based heights that were calculated separately
            Texture2D heightFromNormalsTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] heightFromNormalsPixels = new Color[width * height];
            
            // Find min/max for the normal-based heights
            bool foundNormalHeight = false;
            float normalMinHeight = float.MaxValue;
            float normalMaxHeight = float.MinValue;
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (globalHeightsCount[y][x] > 0)
                    {
                        float h = globalHeightsFromNormals[y][x];
                        if (!foundNormalHeight)
                        {
                            normalMinHeight = h;
                            normalMaxHeight = h;
                            foundNormalHeight = true;
                        }
                        else
                        {
                            if (h < normalMinHeight) normalMinHeight = h;
                            if (h > normalMaxHeight) normalMaxHeight = h;
                        }
                    }
                }
            }
            
            if (foundNormalHeight)
            {
                float normalHeightRange = normalMaxHeight - normalMinHeight;
                if (normalHeightRange < 0.001f) normalHeightRange = 1f;
                
                UnityEngine.Debug.Log($"Normal-based height range: min={normalMinHeight}, max={normalMaxHeight}, range={normalHeightRange}");
                
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (globalHeightsCount[y][x] > 0)
                        {
                            float h = globalHeightsFromNormals[y][x];
                            float normalizedHeight = (h - normalMinHeight) / normalHeightRange;
                            
                            // Apply contrast curve
                            if (normalizedHeight < 0.5f)
                            {
                                normalizedHeight = normalizedHeight * normalizedHeight * 2.0f;
                            }
                            normalizedHeight = Mathf.Clamp01(normalizedHeight);
                            heightFromNormalsPixels[y * width + x] = new UnityEngine.Color(normalizedHeight, normalizedHeight, normalizedHeight, 1f);
                        }
                    }
                }
            }
            
            heightFromNormalsTexture.SetPixels(heightFromNormalsPixels);
            heightFromNormalsTexture.Apply();
            byte[] nfhbytes = heightFromNormalsTexture.EncodeToPNG();
            string nfhfilePath = Application.dataPath + "/StreamingAssets/Data/MapHeight_FromNormals.png";
            System.IO.File.WriteAllBytes(nfhfilePath, nfhbytes);



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