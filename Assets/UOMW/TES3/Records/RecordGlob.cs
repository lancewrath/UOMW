using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using UnityEngine;

namespace ESMSharp.TES3
{

    public class RecordGlob : Record
    {
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
            Utils.LogBuffer("\t- GLOB SubRecord: {0}", srecord);
            uint subrecordsize = reader.ReadUInt32();
            SubRecords subrecord = null;


            switch (srecord)
            {
                case "NAME":
                    SubRecordGlobNAME subRecordGlobNAME = new SubRecordGlobNAME();
                    subRecordGlobNAME.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordGlobNAME;
                    break;

                case "FLVT":
                    SubRecordGlobFLTV subRecordGlobFLVT = new SubRecordGlobFLTV();
                    subRecordGlobFLVT.Deserialize(reader, srecord);
                    subrecord = subRecordGlobFLVT;
                    break;

                case "FNAM":
                    SubRecordGlobFNAM subRecordGlobFNAM = new SubRecordGlobFNAM();
                    subRecordGlobFNAM.Deserialize(reader, srecord);
                    subrecord = subRecordGlobFNAM;
                    break;

                default:
                    subrecord = new SubRecords(srecord);
                    reader.ReadBytes((int)subrecordsize);
                    break;
            }

            _subrecords.Add(subrecord);
        }
    }
}