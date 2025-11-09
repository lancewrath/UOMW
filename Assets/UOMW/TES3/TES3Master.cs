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
        private string _esm = "";
        private string _bsa = "";
        private Record[] _records;
        TESTerrain testerrain = null;
        NIFModels nifModels = null;
        CellManager cellManager = null;
        //move this to a more appropriate spot later
        public long MinCellX = 0, MinCellY = 0, MaxCellX = 0, MaxCellY = 0;
        public float MaxHeight = 0;

        public Record[] Records { get { return _records; } }
        private bool _loaded = false;

        public bool Loaded { get { return _loaded; } }

        public TES3Master(string esmfile, string bsafile)
        {
            _bsa = bsafile;
            _esm = esmfile;
            // Open ESM File
            string esmPath = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", _esm);
            if (!System.IO.File.Exists(esmPath))
            {
                UnityEngine.Debug.LogError($"ESM file not found: {esmPath}");
                return;
            }
            using (var reader = new BetterBinaryReader(File.OpenRead(esmPath)))
            {
                var tes3 = new Record();
                tes3.Deserialize(reader, reader.ReadString(4));

                if (tes3.Type != "TES3")
                    throw new Exception("That's not a Morrowind master file.");

                Utils.LogBuffer("# Loading Morrowind");
                Utils.LogBuffer("\t- Record: {0}", tes3.Type);

                var mDico = new List<string>();
                var mRecords = new List<Record>();
                Record mRecord = null;

                while (reader.Position < reader.Length)
                {
                    string name = reader.ReadString(4);
                    //Utils.LogBuffer("\t- Record: {0}", name);
                    switch (name)
                    {
                        case "LAND":
                            Debug.Log("Land Record");
                            RecordLand lndrecord = new RecordLand();
                            lndrecord.Deserialize(reader, name);
                            mRecord = lndrecord;
                            if(lndrecord.maxheight>MaxHeight)
                                MaxHeight = lndrecord.maxheight;
                            if (lndrecord.MinCellX < MinCellX)
                                MinCellX = lndrecord.MinCellX;
                            if (lndrecord.MaxCellX > MaxCellX)
                                MaxCellX = lndrecord.MaxCellX;

                            if (lndrecord.MinCellY < MinCellY)
                                MinCellY = lndrecord.MinCellY;
                            if (lndrecord.MaxCellY > MaxCellY)
                                MaxCellY = lndrecord.MaxCellY;

                            break;

                        case "LTEX":
                            Debug.Log("Land Texture Record");
                            RecordLTex ltexrecord = new RecordLTex();
                            ltexrecord.Deserialize(reader, name);
                            mRecord = ltexrecord;
                            break;

                        case "CELL":
                            Debug.Log("Cell Record");
                            RecordCell cellrecord = new RecordCell();
                            cellrecord.Deserialize(reader, name);
                            mRecord = cellrecord;
                            break;

                        case "STAT":
                            Debug.Log("Static Record");
                            RecordStat statrecord = new RecordStat();
                            statrecord.Deserialize(reader, name);
                            mRecord = statrecord;
                            break;
                        default:
                            mRecord = new Record();
                            mRecord.Deserialize(reader,name);
                            break;
                    }



                    mRecords.Add(mRecord);

                    if (!mDico.Contains(mRecord.Type))
                    {
                        mDico.Add(mRecord.Type);
                        Utils.LogBuffer("\t- Record: {0}", mRecord.Type);
                    }
                }
                
                _records = mRecords.ToArray();
                _loaded = true;
            }
            Debug.Log("Max Terrain Height: " + MaxHeight);
        }


        public void GenerateStatics()
        {
            nifModels = new NIFModels();
            nifModels.GatherModels(_records, _bsa, _esm);
            cellManager = CreateCells();
            
            // Get terrain object if it exists
            Terrain terrain = GameObject.FindFirstObjectByType<Terrain>();
            
            PlaceStatics placeStatics = new PlaceStatics(_esm, _bsa);
            
            // Place different object types separately
            //placeStatics.PlaceLargeStructures(_records, cellManager);
            //placeStatics.PlaceTrees(_records, cellManager, terrain);
            //placeStatics.PlaceGrass(_records, cellManager, terrain);
        }

        public void GenerateTerrainMaps()
        {

            testerrain = new TESTerrain(Convert.ToInt32(MinCellX), Convert.ToInt32(MaxCellX), Convert.ToInt32(MinCellY), Convert.ToInt32(MaxCellY));
            testerrain.GenerateHeightMap(_records, _esm);
            testerrain.GatherLandTextures(_records, _bsa, _esm);
            //testerrain.GenerateUnityTerrain(_records, "Morrowind.esm", terrainHeight: 128f, waterY: 23f);


        }

        public void GenerateTerrain()
        {
            const float MORROWIND_TO_TERRAIN_SCALE = 64f / 8192f;
            const float MORROWIND_MAX_TERRAIN_HEIGHT = 32768f * MORROWIND_TO_TERRAIN_SCALE; // 256 units
            // Sea level is 0 in Morrowind, which maps to Y=0 in Unity
            // Terrain is positioned at Y=-16 (quarter cell lower) so sea level aligns properly
            // This allows underwater areas to be below Y=0 and water plane at Y=0 covers them
            testerrain.GenerateUnityTerrain(_records, "Morrowind.esm", MORROWIND_MAX_TERRAIN_HEIGHT, 0f);
        }



        /// <summary>
        /// Creates cell GameObjects from CELL records
        /// </summary>
        public CellManager CreateCells(Transform parent = null)
        {
            ESMSharp.TES3Terrain.CellManager cellManager = new ESMSharp.TES3Terrain.CellManager();
            cellManager.CreateCells(_records, parent, _esm, _bsa);
            
            return cellManager;
        }

        /// <summary>
        /// Places large static structures from CELL records onto the terrain
        /// </summary>
        public void PlaceLargeStructures(CellManager cellManager = null, Transform parent = null)
        {
            PlaceStatics placeStatics = new PlaceStatics(_esm, _bsa);
            placeStatics.PlaceLargeStructures(_records, cellManager, parent);
        }

        /// <summary>
        /// Places trees from CELL records as Unity terrain trees
        /// </summary>
        public void PlaceTrees(CellManager cellManager = null)
        {
            Terrain terrain = GameObject.FindFirstObjectByType<Terrain>();
            PlaceStatics placeStatics = new PlaceStatics(_esm, _bsa);
            placeStatics.PlaceTrees(_records, cellManager, terrain);
        }

        /// <summary>
        /// Places grass from CELL records as Unity terrain details
        /// </summary>
        public void PlaceGrass(CellManager cellManager = null)
        {
            Terrain terrain = GameObject.FindFirstObjectByType<Terrain>();
            PlaceStatics placeStatics = new PlaceStatics(_esm, _bsa);
            placeStatics.PlaceGrass(_records, cellManager, terrain);
        }

    }


}
