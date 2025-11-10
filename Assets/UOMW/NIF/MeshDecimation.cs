using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ESMSharp.NIF
{
    /// <summary>
    /// Mesh decimation utility for generating LOD meshes using edge collapse algorithm
    /// Based on Quadric Error Metrics (QEM) approach
    /// </summary>
    public static class MeshDecimation
    {
        /// <summary>
        /// Decimates a mesh to a target triangle count using edge collapse
        /// </summary>
        /// <param name="sourceMesh">The source mesh to decimate</param>
        /// <param name="targetTriangleCount">Target number of triangles (must be less than source)</param>
        /// <returns>Decimated mesh, or null if decimation fails</returns>
        public static Mesh DecimateMesh(Mesh sourceMesh, int targetTriangleCount)
        {
            if (sourceMesh == null || targetTriangleCount <= 0)
                return null;

            int sourceTriangleCount = sourceMesh.triangles.Length / 3;
            if (targetTriangleCount >= sourceTriangleCount)
            {
                // No decimation needed, return copy of source
                return Object.Instantiate(sourceMesh);
            }

            // For now, use Unity's built-in mesh simplification if available
            // Otherwise, implement a simple edge collapse algorithm
            return DecimateMeshSimple(sourceMesh, targetTriangleCount);
        }

        /// <summary>
        /// Simple mesh decimation using edge collapse
        /// This is a basic implementation - for production use, consider a more sophisticated QEM algorithm
        /// </summary>
        private static Mesh DecimateMeshSimple(Mesh sourceMesh, int targetTriangleCount)
        {
            // Extract mesh data
            Vector3[] vertices = sourceMesh.vertices;
            int[] triangles = sourceMesh.triangles;
            Vector2[] uvs = sourceMesh.uv;
            Vector3[] normals = sourceMesh.normals;
            Color[] colors = sourceMesh.colors;

            // Build edge list and triangle adjacency
            Dictionary<Edge, List<int>> edgeToTriangles = new Dictionary<Edge, List<int>>();
            List<Triangle> triangleList = new List<Triangle>();

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int t0 = triangles[i];
                int t1 = triangles[i + 1];
                int t2 = triangles[i + 2];

                Triangle tri = new Triangle(t0, t1, t2, i / 3);
                triangleList.Add(tri);

                // Add edges
                AddEdge(edgeToTriangles, new Edge(t0, t1), i / 3);
                AddEdge(edgeToTriangles, new Edge(t1, t2), i / 3);
                AddEdge(edgeToTriangles, new Edge(t2, t0), i / 3);
            }

            // Simple decimation: collapse shortest edges first
            // This is a basic approach - a full QEM implementation would calculate error metrics
            int currentTriangleCount = triangleList.Count;
            int targetReduction = currentTriangleCount - targetTriangleCount;

            // Sort edges by length (shortest first)
            List<EdgeCollapse> collapseCandidates = new List<EdgeCollapse>();
            foreach (var kvp in edgeToTriangles)
            {
                if (kvp.Value.Count == 2) // Edge shared by exactly 2 triangles
                {
                    float length = Vector3.Distance(vertices[kvp.Key.v0], vertices[kvp.Key.v1]);
                    collapseCandidates.Add(new EdgeCollapse
                    {
                        edge = kvp.Key,
                        length = length,
                        triangles = kvp.Value
                    });
                }
            }

            collapseCandidates.Sort((a, b) => a.length.CompareTo(b.length));

            // Collapse edges until we reach target triangle count
            int collapsed = 0;
            HashSet<int> removedTriangles = new HashSet<int>();
            List<int> vertexMap = new List<int>();
            for (int i = 0; i < vertices.Length; i++)
            {
                vertexMap.Add(i);
            }

            foreach (var candidate in collapseCandidates)
            {
                if (collapsed >= targetReduction)
                    break;

                // Check if triangles are still valid
                if (removedTriangles.Contains(candidate.triangles[0]) || 
                    removedTriangles.Contains(candidate.triangles[1]))
                    continue;

                // Collapse edge: merge v1 into v0
                int v0 = candidate.edge.v0;
                int v1 = candidate.edge.v1;

                // Update vertex map
                for (int i = 0; i < vertexMap.Count; i++)
                {
                    if (vertexMap[i] == v1)
                        vertexMap[i] = v0;
                }

                // Mark triangles for removal
                removedTriangles.Add(candidate.triangles[0]);
                removedTriangles.Add(candidate.triangles[1]);

                collapsed += 2; // Each edge collapse removes 2 triangles
            }

            // Rebuild mesh with remaining triangles
            return RebuildMesh(sourceMesh, triangleList, removedTriangles, vertexMap);
        }

        private static void AddEdge(Dictionary<Edge, List<int>> edgeMap, Edge edge, int triangleIndex)
        {
            if (!edgeMap.ContainsKey(edge))
            {
                edgeMap[edge] = new List<int>();
            }
            edgeMap[edge].Add(triangleIndex);
        }

        private static Mesh RebuildMesh(Mesh sourceMesh, List<Triangle> triangles, HashSet<int> removedTriangles, List<int> vertexMap)
        {
            List<Vector3> newVertices = new List<Vector3>(sourceMesh.vertices);
            List<Vector2> newUVs = sourceMesh.uv.Length > 0 ? new List<Vector2>(sourceMesh.uv) : null;
            List<Vector3> newNormals = sourceMesh.normals.Length > 0 ? new List<Vector3>(sourceMesh.normals) : null;
            List<Color> newColors = sourceMesh.colors.Length > 0 ? new List<Color>(sourceMesh.colors) : null;

            List<int> newTriangles = new List<int>();
            Dictionary<int, int> vertexRemap = new Dictionary<int, int>();
            int nextIndex = 0;

            for (int i = 0; i < triangles.Count; i++)
            {
                if (removedTriangles.Contains(i))
                    continue;

                Triangle tri = triangles[i];
                int[] indices = new int[3];

                for (int j = 0; j < 3; j++)
                {
                    int originalVertex = tri[j];
                    int mappedVertex = vertexMap[originalVertex];

                    if (!vertexRemap.ContainsKey(mappedVertex))
                    {
                        vertexRemap[mappedVertex] = nextIndex++;
                    }

                    indices[j] = vertexRemap[mappedVertex];
                }

                // Skip degenerate triangles
                if (indices[0] == indices[1] || indices[1] == indices[2] || indices[2] == indices[0])
                    continue;

                newTriangles.AddRange(indices);
            }

            // Create new mesh
            Mesh decimatedMesh = new Mesh();
            decimatedMesh.name = sourceMesh.name + "_LOD";

            // Remap vertices
            Vector3[] finalVertices = new Vector3[vertexRemap.Count];
            Vector2[] finalUVs = newUVs != null ? new Vector2[vertexRemap.Count] : null;
            Vector3[] finalNormals = newNormals != null ? new Vector3[vertexRemap.Count] : null;
            Color[] finalColors = newColors != null ? new Color[vertexRemap.Count] : null;

            foreach (var kvp in vertexRemap)
            {
                finalVertices[kvp.Value] = newVertices[kvp.Key];
                if (finalUVs != null && kvp.Key < newUVs.Count)
                    finalUVs[kvp.Value] = newUVs[kvp.Key];
                if (finalNormals != null && kvp.Key < newNormals.Count)
                    finalNormals[kvp.Value] = newNormals[kvp.Key];
                if (finalColors != null && kvp.Key < newColors.Count)
                    finalColors[kvp.Value] = newColors[kvp.Key];
            }

            decimatedMesh.vertices = finalVertices;
            if (finalUVs != null)
                decimatedMesh.uv = finalUVs;
            if (finalNormals != null)
                decimatedMesh.normals = finalNormals;
            if (finalColors != null)
                decimatedMesh.colors = finalColors;

            decimatedMesh.triangles = newTriangles.ToArray();
            decimatedMesh.RecalculateBounds();
            if (finalNormals == null)
                decimatedMesh.RecalculateNormals();

            return decimatedMesh;
        }

        /// <summary>
        /// Creates multiple LOD levels for a mesh
        /// </summary>
        /// <param name="sourceMesh">Source mesh</param>
        /// <param name="lodLevels">Array of triangle counts for each LOD level (LOD0 = full detail, LOD1 = reduced, etc.)</param>
        /// <returns>Array of LOD meshes</returns>
        public static Mesh[] CreateLODLevels(Mesh sourceMesh, int[] lodLevels)
        {
            if (sourceMesh == null || lodLevels == null || lodLevels.Length == 0)
                return null;

            Mesh[] lodMeshes = new Mesh[lodLevels.Length];
            int sourceTriangleCount = sourceMesh.triangles.Length / 3;

            for (int i = 0; i < lodLevels.Length; i++)
            {
                int targetTriangles = Mathf.Min(lodLevels[i], sourceTriangleCount);
                lodMeshes[i] = DecimateMesh(sourceMesh, targetTriangles);
            }

            return lodMeshes;
        }

        #region Helper Classes

        private struct Edge
        {
            public int v0, v1;

            public Edge(int v0, int v1)
            {
                // Ensure consistent ordering (smaller index first)
                if (v0 < v1)
                {
                    this.v0 = v0;
                    this.v1 = v1;
                }
                else
                {
                    this.v0 = v1;
                    this.v1 = v0;
                }
            }

            public override bool Equals(object obj)
            {
                if (obj is Edge other)
                {
                    return v0 == other.v0 && v1 == other.v1;
                }
                return false;
            }

            public override int GetHashCode()
            {
                return v0 * 1000000 + v1;
            }
        }

        private struct Triangle
        {
            public int v0, v1, v2;
            public int index;

            public Triangle(int v0, int v1, int v2, int index)
            {
                this.v0 = v0;
                this.v1 = v1;
                this.v2 = v2;
                this.index = index;
            }

            public int this[int i]
            {
                get
                {
                    switch (i)
                    {
                        case 0: return v0;
                        case 1: return v1;
                        case 2: return v2;
                        default: throw new System.IndexOutOfRangeException();
                    }
                }
            }
        }

        private struct EdgeCollapse
        {
            public Edge edge;
            public float length;
            public List<int> triangles;
        }

        #endregion
    }
}

