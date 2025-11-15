using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using Unity.VisualScripting;
using UnityEngine;
using static ESMSharp.TES3.Master;

namespace ESMSharp.TES3
{
    public class RecordClothing : Record
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
            Utils.LogBuffer("\t- Clothing SubRecord: {0}", srecord);
            uint subrecordsize = reader.ReadUInt32();
            SubRecords subrecord = null;


            switch (srecord)
            {

                case "NAME":
                    SubRecordClothNAME subRecordClothNAME = new SubRecordClothNAME();
                    subRecordClothNAME.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordClothNAME;
                    break;

                case "MODL":
                    SubRecordClothtMODL subRecordClothMODL = new SubRecordClothtMODL();
                    subRecordClothMODL.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordClothMODL;
                    break;

                case "FNAM":
                    SubRecordClothFNAM subRecordClothFNAM = new SubRecordClothFNAM();
                    subRecordClothFNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordClothFNAM;
                    break;

                case "CTDT":
                    SubRecordClothCTDT subRecordClothCTDT = new SubRecordClothCTDT();
                    subRecordClothCTDT.Deserialize(reader, srecord);
                    subrecord = subRecordClothCTDT;
                    break;

                case "ITEX":
                    SubRecordClothITEX subRecordClothITEX = new SubRecordClothITEX();
                    subRecordClothITEX.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordClothITEX;
                    break;

                case "SCRI":
                    SubRecordClothSCRI subRecordClothSCRI = new SubRecordClothSCRI();
                    subRecordClothSCRI.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordClothSCRI;
                    break;

                case "INDX":
                    SubRecordClothINDX subRecordClothINDX = new SubRecordClothINDX();
                    subRecordClothINDX.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordClothINDX;
                    break;

                case "BNAM":
                    SubRecordClothBNAM subRecordClothBNAM = new SubRecordClothBNAM();
                    subRecordClothBNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordClothBNAM;
                    break;

                case "CNAM":
                    SubRecordClothCNAM subRecordClothCNAM = new SubRecordClothCNAM();
                    subRecordClothCNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordClothCNAM;
                    break;

                case "ENAM":
                    SubRecordClothENAM subRecordClothENAM = new SubRecordClothENAM();
                    subRecordClothENAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordClothENAM;
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
