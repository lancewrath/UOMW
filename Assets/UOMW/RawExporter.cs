using System;
using System.IO;
using UnityEngine;

public static class RawExporter
{
    /// <summary>
    /// Writes a 16-bit little-endian RAW file for Unity Terrain Import.
    /// heights: normalized 0..1 float array (row-major, width*height)
    /// </summary>
    public static void WriteRaw16(string path, float[] heights, int width, int height, bool flipVertically = true)
    {
        if (heights == null || heights.Length != width * height)
        {
            Debug.LogError("Height array size does not match width*height.");
            return;
        }

        try
        {
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                // Unity’s Import RAW dialog has a “Flip Vertically” checkbox.
                // If you export top→down, set flipVertically = true so the import matches.
                for (int y = 0; y < height; y++)
                {
                    int srcY = flipVertically ? (height - 1 - y) : y;
                    int rowBase = srcY * width;

                    for (int x = 0; x < width; x++)
                    {
                        float f = Mathf.Clamp01(heights[rowBase + x]);
                        ushort value = (ushort)Mathf.RoundToInt(f * 65535f);
                        bw.Write(value); // little-endian by default
                    }
                }
            }

            Debug.Log($"RAW heightmap written: {path} ({width}x{height})");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error writing RAW file: {ex.Message}");
        }
    }
}
