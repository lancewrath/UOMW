using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using Unity.VisualScripting;
using UnityEngine;
using static ESMSharp.TES3.Master;

namespace ESMSharp.TES3
{
    public class RecordArmor : Record
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
            Utils.LogBuffer("\t- Armor SubRecord: {0}", srecord);
            uint subrecordsize = reader.ReadUInt32();
            SubRecords subrecord = null;


            switch (srecord)
            {

                case "NAME":
                    SubRecordArmorNAME subRecordArmorNAME = new SubRecordArmorNAME();
                    subRecordArmorNAME.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordArmorNAME;
                    break;

                case "MODL":
                    SubRecordArmorMODL subRecordArmorMODL = new SubRecordArmorMODL();
                    subRecordArmorMODL.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordArmorMODL;
                    break;

                case "FNAM":
                    SubRecordArmorFNAM subRecordArmorFNAM = new SubRecordArmorFNAM();
                    subRecordArmorFNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordArmorFNAM;
                    break;

                case "SCRI":
                    SubRecordArmorSCRI subRecordArmorSCRI = new SubRecordArmorSCRI();
                    subRecordArmorSCRI.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordArmorSCRI;
                    break;

                case "AODT":
                    SubRecordArmorAODT subRecordArmorAODT = new SubRecordArmorAODT();
                    subRecordArmorAODT.Deserialize(reader, srecord);
                    subrecord = subRecordArmorAODT;
                    break;

                case "ITEX":
                    SubRecordArmorITEX subRecordArmorITEX = new SubRecordArmorITEX();
                    subRecordArmorITEX.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordArmorITEX;
                    break;



                case "INDX":
                    SubRecordArmorINDX subRecordArmorINDX = new SubRecordArmorINDX();
                    subRecordArmorINDX.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordArmorINDX;
                    break;

                case "BNAM":
                    SubRecordArmorBNAM subRecordArmorBNAM = new SubRecordArmorBNAM();
                    subRecordArmorBNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordArmorBNAM;
                    break;

                case "CNAM":
                    SubRecordArmorCNAM subRecordArmorCNAM = new SubRecordArmorCNAM();
                    subRecordArmorCNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordArmorCNAM;
                    break;

                case "ENAM":
                    SubRecordArmorENAM subRecordArmorENAM = new SubRecordArmorENAM();
                    subRecordArmorENAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordArmorENAM;
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
