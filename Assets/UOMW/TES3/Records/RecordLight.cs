using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using Unity.VisualScripting;
using UnityEngine;
using static ESMSharp.TES3.Master;

namespace ESMSharp.TES3
{

    public class RecordLight : Record
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
            Utils.LogBuffer("\t- Container SubRecord: {0}", srecord);
            uint subrecordsize = reader.ReadUInt32();
            SubRecords subrecord = null;


            switch (srecord)
            {

                case "NAME":
                    SubRecordLightNAME subRecordLightNAME = new SubRecordLightNAME();
                    subRecordLightNAME.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordLightNAME;
                    break;

                case "MODL":
                    SubRecordLightMODL subRecordLightMODL = new SubRecordLightMODL();
                    subRecordLightMODL.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordLightMODL;
                    break;

                case "FNAM":
                    SubRecordLightFNAM subRecordLightFNAM = new SubRecordLightFNAM();
                    subRecordLightFNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordLightFNAM;
                    break;

                case "ITEX":
                    SubRecordLightITEX subRecordLightITEX = new SubRecordLightITEX();
                    subRecordLightITEX.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordLightITEX;
                    break;

                case "LHDT":
                    SubRecordLightLHDT subRecordLightLHDT = new SubRecordLightLHDT();
                    subRecordLightLHDT.Deserialize(reader, srecord);
                    subrecord = subRecordLightLHDT;
                    break;

                case "SNAM":
                    SubRecordLightSNAM subRecordLightSNAM = new SubRecordLightSNAM();
                    subRecordLightSNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordLightSNAM;
                    break;

                case "SCRI":
                    SubRecordLightSCRI subRecordLightSCRI = new SubRecordLightSCRI();
                    subRecordLightSCRI.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordLightSCRI;
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
