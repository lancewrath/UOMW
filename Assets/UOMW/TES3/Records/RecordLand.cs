using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using UnityEngine;

namespace ESMSharp.TES3
{
    public class RecordLand : Record
    {

        public long MinCellX = 0, MinCellY = 0, MaxCellX = 0, MaxCellY = 0;
        public float maxheight = 0;
        public override void Deserialize(BetterReader reader, string name)
        {
            type = name;
            dataSize = reader.ReadUInt32();
            unknow = reader.ReadUInt32();
            flags = reader.ReadUInt32();
            long newpos = reader.Position + dataSize;
            while (reader.Position < newpos)
            {
                uint newsize = Convert.ToUInt32(newpos - reader.Position);
                ExtractSubRecords(reader, newsize);
            }

            
            
        }

        protected override void ExtractSubRecords(BetterReader reader, uint size)
        {
            string srecord = reader.ReadString(4);
            Utils.LogBuffer("\t- Land SubRecord: {0}", srecord);
            uint subrecordsize = reader.ReadUInt32();
            SubRecords subrecord = null;

            switch (srecord)
            {
                case "INTV":
                    SubRecordLandINTV subrecordland = new SubRecordLandINTV();
                    subrecordland.Deserialize(reader, srecord);
                    subrecord = subrecordland;

                    if(subrecordland.CellX <  MinCellX)
                        MinCellX = subrecordland.CellX;
                    if (subrecordland.CellX > MaxCellX)
                        MaxCellX = subrecordland.CellX;

                    if (subrecordland.CellY < MinCellY)
                        MinCellY = subrecordland.CellY;
                    if (subrecordland.CellY > MaxCellY)
                        MaxCellY = subrecordland.CellY;

                    //reader.ReadBytes((int)size-4);
                    break;

                case "DATA":
                    reader.ReadInt32();
                    break;

                case "VNML":
                    SubRecordLandVNML subrecordvnml = new SubRecordLandVNML();
                    subrecordvnml.Deserialize(reader, srecord);
                    subrecord = subrecordvnml;
                    break;

                case "VHGT":
                    SubRecordLandVHGT subrecordvght = new SubRecordLandVHGT();
                    subrecordvght.Deserialize(reader, srecord);
                    if(subrecordvght.offset>maxheight)
                        maxheight = subrecordvght.offset;
                    subrecord = subrecordvght;
                    break;

                case "WNAM":
                    reader.ReadBytes(81);
                    break;

                case "VCLR":                 
                    SubRecordLandVCLR subrecordvclr = new SubRecordLandVCLR();
                    subrecordvclr.Deserialize(reader, srecord);
                    subrecord = subrecordvclr;
                    break;

                case "VTEX":
                    SubRecordLandVTEX subrecordvtex = new SubRecordLandVTEX();
                    subrecordvtex.Deserialize(reader, srecord);
                    subrecord = subrecordvtex;
                    break;

                default:
                    subrecord = new SubRecords(srecord);
                    reader.ReadBytes((int)subrecordsize);
                    break;

            }

            //SubRecordLand subRecordLand = new SubRecordLand();
            //subRecordLand.Deserialize(reader, srecord);
            _subrecords.Add(subrecord);
        }
    }
}
