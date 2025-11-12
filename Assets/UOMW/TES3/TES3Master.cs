using BSASharp;
using ESMSharp.Core;
using ESMSharp.TES3.Records;
using ESMSharp.TES3Terrain;
using ESMSharp.NIF;
using System;
using System.Collections.Generic;
using System.IO;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;
using UOMW;


namespace ESMSharp.TES3
{
    public class TES3Master
    {
        TESTerrain testerrain = null;
        NIFModels nifModels = null;
        CellManager cellManager = null;
        
        // Cell bounds and max height (now retrieved from TESESMLibrary)
        public long MinCellX { get; private set; } = 0;
        public long MinCellY { get; private set; } = 0;
        public long MaxCellX { get; private set; } = 0;
        public long MaxCellY { get; private set; } = 0;
        public float MaxHeight { get; private set; } = 0;

        public Record[] Records { get { return TESESMLibrary.GetAllRecords(); } }
        public bool Loaded { get; private set; } = false;

        /// <summary>
        /// Default constructor - uses global TESESMLibrary and TESBSALibrary
        /// </summary>
        public TES3Master()
        {
            // Libraries are loaded separately via InitializeLibraries()
        }

        /// <summary>
        /// Legacy constructor for backward compatibility
        /// </summary>
        [Obsolete("Use InitializeLibraries() instead. This constructor will load a single ESM for backward compatibility.")]
        public TES3Master(string esmfile, string bsafile)
        {
            // For backward compatibility, load the single ESM/BSA
            if (TESESMLibrary.Count == 0)
            {
                TESESMLibrary.LoadESM(esmfile);
                TESBSALibrary.LoadBSA(bsafile, null, esmfile);
            }
            UpdateCellBounds();
            Loaded = TESESMLibrary.Count > 0;
        }

        /// <summary>
        /// Initializes the ESM and BSA libraries by scanning and loading all files
        /// </summary>
        /// <param name="dataFolder">Optional custom data folder path (defaults to StreamingAssets/Data)</param>
        /// <returns>True if at least one ESM was loaded</returns>
        public static bool InitializeLibraries(string dataFolder = null)
        {
            // Scan and load all ESM files
            int esmCount = TESESMLibrary.ScanAndLoadESMs(dataFolder);
            if (esmCount == 0)
            {
                Debug.LogError("TES3Master: No ESM files were loaded!");
                return false;
            }

            // Scan and load all BSA files
            int bsaCount = TESBSALibrary.ScanAndLoadBSAs(dataFolder);
            if (bsaCount == 0)
            {
                Debug.LogWarning("TES3Master: No BSA files were loaded. Some content may be missing.");
            }

            Debug.Log($"TES3Master: Initialized libraries - {esmCount} ESM(s), {bsaCount} BSA(s)");
            return true;
        }

        /// <summary>
        /// Updates cell bounds from the global ESM library
        /// </summary>
        public void UpdateCellBounds()
        {
            TESESMLibrary.GetGlobalCellBounds(out long minX, out long minY, out long maxX, out long maxY, out float maxH);
            MinCellX = minX;
            MinCellY = minY;
            MaxCellX = maxX;
            MaxCellY = maxY;
            MaxHeight = maxH;
            Loaded = TESESMLibrary.Count > 0;
        }


        public void GenerateStatics()
        {
            Record[] records = TESESMLibrary.GetAllRecords();
            
            // Get the primary ESM filename (first loaded ESM, typically Morrowind.esm)
            string[] loadedESMs = TESESMLibrary.GetLoadedESMFilenames();
            string primaryESM = loadedESMs.Length > 0 ? loadedESMs[0] : "Morrowind.esm";
            string primaryBSA = System.IO.Path.GetFileNameWithoutExtension(primaryESM) + ".bsa";
            
            nifModels = new NIFModels();
            nifModels.GatherModels(records, primaryBSA, primaryESM);
            cellManager = CreateCells();
            
            // Get terrain object if it exists
            Terrain terrain = GameObject.FindFirstObjectByType<Terrain>();
            
            PlaceStatics placeStatics = new PlaceStatics(primaryESM, primaryBSA);
            
            // Place different object types separately
            //placeStatics.PlaceLargeStructures(records, cellManager);
            //placeStatics.PlaceTrees(records, cellManager, terrain);
            //placeStatics.PlaceGrass(records, cellManager, terrain);
        }
        [Obsolete("Method1 is deprecated, please use GenerateTerrainMaps_MergedLands instead.")]
        public void GenerateTerrainMaps()
        {
            UpdateCellBounds();
            Record[] records = TESESMLibrary.GetAllRecords();
            
            // Get the primary ESM filename (first loaded ESM, typically Morrowind.esm)
            string[] loadedESMs = TESESMLibrary.GetLoadedESMFilenames();
            string primaryESM = loadedESMs.Length > 0 ? loadedESMs[0] : "Morrowind.esm";
            string primaryBSA = System.IO.Path.GetFileNameWithoutExtension(primaryESM) + ".bsa";

            testerrain = new TESTerrain(Convert.ToInt32(MinCellX), Convert.ToInt32(MaxCellX), Convert.ToInt32(MinCellY), Convert.ToInt32(MaxCellY));
            testerrain.GenerateHeightMap(records, primaryESM);
            testerrain.GatherLandTextures(records, primaryBSA, primaryESM);
            //testerrain.GenerateUnityTerrain(records, primaryESM, terrainHeight: 128f, waterY: 23f);
        }


        /// <summary>
        /// TEST FUNCTION: Generate heightmap using Cells algorithm
        /// </summary>
        public void GenerateTerrainMaps_CellsLands()
        {
            UpdateCellBounds();
            Record[] records = TESESMLibrary.GetAllRecords();
            
            string[] loadedESMs = TESESMLibrary.GetLoadedESMFilenames();
            string primaryESM = loadedESMs.Length > 0 ? loadedESMs[0] : "Morrowind.esm";
            
            testerrain = new TESTerrain(Convert.ToInt32(MinCellX), Convert.ToInt32(MaxCellX), Convert.ToInt32(MinCellY), Convert.ToInt32(MaxCellY));
            testerrain.GenerateHeightMap_Cells(records, primaryESM);
        }

        /// <summary>
        /// TEST FUNCTION: Generate heightmap using merged_lands algorithm
        /// Outputs a PNG for comparison: {esm}_MapHeight_MergedLands.png
        /// </summary>
        public void GenerateTerrainMaps_MergedLands()
        {
            // Ensure libraries are initialized
            if (TESESMLibrary.Count == 0)
            {
                UnityEngine.Debug.LogWarning("TES3Master: ESM library not initialized. Calling InitializeLibraries()...");
                if (!InitializeLibraries())
                {
                    UnityEngine.Debug.LogError("TES3Master: Failed to initialize ESM/BSA libraries!");
                    return;
                }
            }
            
            UpdateCellBounds();
            Record[] records = TESESMLibrary.GetAllRecords();
            
            UnityEngine.Debug.Log($"TES3Master.GenerateTerrainMaps_MergedLands: Retrieved {records?.Length ?? 0} records from library");
            
            string[] loadedESMs = TESESMLibrary.GetLoadedESMFilenames();
            string primaryESM = loadedESMs.Length > 0 ? loadedESMs[0] : "Morrowind.esm";
            string primaryBSA = System.IO.Path.GetFileNameWithoutExtension(primaryESM) + ".bsa";

            testerrain = new TESTerrain(Convert.ToInt32(MinCellX), Convert.ToInt32(MaxCellX), Convert.ToInt32(MinCellY), Convert.ToInt32(MaxCellY));
            testerrain.GenerateHeightMap_MergedLands(records, primaryESM);
            testerrain.GatherLandTextures(records, primaryBSA, primaryESM);
        }

        public void GenerateCellsTerrain()
        {
            Record[] records = TESESMLibrary.GetAllRecords();
            
            string[] loadedESMs = TESESMLibrary.GetLoadedESMFilenames();
            string primaryESM = loadedESMs.Length > 0 ? loadedESMs[0] : "Morrowind.esm";
            
            // Use global constants from TESGlobals
            const float MORROWIND_MAX_TERRAIN_HEIGHT = 32768f * TESGlobals.MORROWIND_TO_TERRAIN_SCALE; // 256 units
            // Sea level is 0 in Morrowind, which maps to Y=0 in Unity
            // Terrain is positioned at Y=-16 (quarter cell lower) so sea level aligns properly
            // This allows underwater areas to be below Y=0 and water plane at Y=0 covers them
            testerrain.GenerateUnityTerrainCells(records, primaryESM, MORROWIND_MAX_TERRAIN_HEIGHT, 0f);
        }

        public void GenerateTerrain()
        {
            // Ensure libraries are initialized
            if (TESESMLibrary.Count == 0)
            {
                UnityEngine.Debug.LogWarning("TES3Master: ESM library not initialized. Calling InitializeLibraries()...");
                if (!InitializeLibraries())
                {
                    UnityEngine.Debug.LogError("TES3Master: Failed to initialize ESM/BSA libraries!");
                    return;
                }
            }
            
            Record[] records = TESESMLibrary.GetAllRecords();
            
            UnityEngine.Debug.Log($"TES3Master.GenerateTerrain: Retrieved {records?.Length ?? 0} records from library");
            
            string[] loadedESMs = TESESMLibrary.GetLoadedESMFilenames();
            string primaryESM = loadedESMs.Length > 0 ? loadedESMs[0] : "Morrowind.esm";
            
            // Terrain height and Y offset are now calculated automatically from the actual height range
            // The function loads the height range from the saved JSON file and calculates:
            // - terrainHeight = actualHeightRange * (64f / 8192f)
            // - terrainYOffset = actualMinHeight * (64f / 8192f)
            // This matches OpenMW's approach of using actual min/max from terrain data
            testerrain.GenerateUnityTerrain(records, primaryESM, 0f, 0f);
        }



        /// <summary>
        /// Creates cell GameObjects from CELL records
        /// </summary>
        public CellManager CreateCells(Transform parent = null)
        {
            Record[] records = TESESMLibrary.GetAllRecords();
            
            string[] loadedESMs = TESESMLibrary.GetLoadedESMFilenames();
            string primaryESM = loadedESMs.Length > 0 ? loadedESMs[0] : "Morrowind.esm";
            string primaryBSA = System.IO.Path.GetFileNameWithoutExtension(primaryESM) + ".bsa";
            
            ESMSharp.TES3Terrain.CellManager cellManager = new ESMSharp.TES3Terrain.CellManager();
            cellManager.CreateCells(records, parent, primaryESM, primaryBSA);
            
            return cellManager;
        }

        /// <summary>
        /// Places large static structures from CELL records onto the terrain
        /// </summary>
        public void PlaceLargeStructures(CellManager cellManager = null, Transform parent = null)
        {
            Record[] records = TESESMLibrary.GetAllRecords();
            
            string[] loadedESMs = TESESMLibrary.GetLoadedESMFilenames();
            string primaryESM = loadedESMs.Length > 0 ? loadedESMs[0] : "Morrowind.esm";
            string primaryBSA = System.IO.Path.GetFileNameWithoutExtension(primaryESM) + ".bsa";
            
            PlaceStatics placeStatics = new PlaceStatics(primaryESM, primaryBSA);
            placeStatics.PlaceLargeStructures(records, cellManager, parent);
        }

        /// <summary>
        /// Places trees from CELL records as Unity terrain trees
        /// </summary>
        public void PlaceTrees(CellManager cellManager = null)
        {
            Record[] records = TESESMLibrary.GetAllRecords();
            
            string[] loadedESMs = TESESMLibrary.GetLoadedESMFilenames();
            string primaryESM = loadedESMs.Length > 0 ? loadedESMs[0] : "Morrowind.esm";
            string primaryBSA = System.IO.Path.GetFileNameWithoutExtension(primaryESM) + ".bsa";
            
            Terrain terrain = GameObject.FindFirstObjectByType<Terrain>();
            PlaceStatics placeStatics = new PlaceStatics(primaryESM, primaryBSA);
            placeStatics.PlaceTrees(records, cellManager, terrain);
        }

        /// <summary>
        /// Places grass from CELL records as Unity terrain details
        /// </summary>
        public void PlaceGrass(CellManager cellManager = null)
        {
            Record[] records = TESESMLibrary.GetAllRecords();
            
            string[] loadedESMs = TESESMLibrary.GetLoadedESMFilenames();
            string primaryESM = loadedESMs.Length > 0 ? loadedESMs[0] : "Morrowind.esm";
            string primaryBSA = System.IO.Path.GetFileNameWithoutExtension(primaryESM) + ".bsa";
            
            Terrain terrain = GameObject.FindFirstObjectByType<Terrain>();
            PlaceStatics placeStatics = new PlaceStatics(primaryESM, primaryBSA);
            placeStatics.PlaceGrass(records, cellManager, terrain);
        }

    }


}
