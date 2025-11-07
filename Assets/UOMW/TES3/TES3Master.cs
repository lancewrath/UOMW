using ESMSharp.Core;
using ESMSharp.TES3.Records;
using ESMSharp.TES3Terrain;
using System;
using System.Collections.Generic;
using System.IO;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;


namespace ESMSharp.TES3
{
    public class TES3Master
    {
        private Record[] _records;
        //move this to a more appropriate spot later
        public long MinCellX = 0, MinCellY = 0, MaxCellX = 0, MaxCellY = 0;


        public Record[] Records { get { return _records; } }
        private bool _loaded = false;

        public bool Loaded { get { return _loaded; } }

        public TES3Master(string filename)
        {
            using (var reader = new BetterBinaryReader(File.OpenRead(filename)))
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
                            if (lndrecord.MinCellX < MinCellX)
                                MinCellX = lndrecord.MinCellX;
                            if (lndrecord.MaxCellX > MaxCellX)
                                MaxCellX = lndrecord.MaxCellX;

                            if (lndrecord.MinCellY < MinCellY)
                                MinCellY = lndrecord.MinCellY;
                            if (lndrecord.MaxCellY > MaxCellY)
                                MaxCellY = lndrecord.MaxCellY;

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
        }


        public void GenerateTerrainMaps()
        {

            TESTerrain testerrain = new TESTerrain(Convert.ToInt32(MinCellX), Convert.ToInt32(MaxCellX), Convert.ToInt32(MinCellY), Convert.ToInt32(MaxCellY));
            testerrain.GenerateHeightMap(_records);
            
            // EXPERIMENTAL: Also try generating heightmap from normals
            // COMMENTED OUT: Not using this method anymore
            //int width = Convert.ToInt32((MaxCellX - MinCellX + 1) * 64 + 1);
            //int height = Convert.ToInt32((MaxCellY - MinCellY + 1) * 64 + 1);
            //testerrain.ExportHeightmapFromNormals(_records, width, height);
            
            // EXPERIMENTAL: Export individual cell heightmaps and create Unity Terrain objects
            // COMMENTED OUT: Going back to single heightmap export for now
            //int width = Convert.ToInt32((MaxCellX - MinCellX + 1) * 64 + 1);
            //int height = Convert.ToInt32((MaxCellY - MinCellY + 1) * 64 + 1);
            //testerrain.ExportCellHeightmaps(_records, width, height);

        }

    }


}
