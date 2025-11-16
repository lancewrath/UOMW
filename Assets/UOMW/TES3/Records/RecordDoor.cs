using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using Unity.VisualScripting;
using UnityEngine;
using static ESMSharp.TES3.Master;

namespace ESMSharp.TES3
{
    public class RecordDoor : Record
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
            Utils.LogBuffer("\t- Door SubRecord: {0}", srecord);
            uint subrecordsize = reader.ReadUInt32();
            SubRecords subrecord = null;


            switch (srecord)
            {

                case "NAME":
                    SubRecordDoorNAME subRecordDoorNAME = new SubRecordDoorNAME();
                    subRecordDoorNAME.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordDoorNAME;
                    break;

                case "MODL":
                    SubRecordDoorMODL subRecordDoorMODL = new SubRecordDoorMODL();
                    subRecordDoorMODL.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordDoorMODL;
                    break;

                case "FNAM":
                    SubRecordDoorFNAM subRecordDoorFNAM = new SubRecordDoorFNAM();
                    subRecordDoorFNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordDoorFNAM;
                    break;

                case "SCRI":
                    SubRecordDoorSCRI subRecordDoorSCRI = new SubRecordDoorSCRI();
                    subRecordDoorSCRI.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordDoorSCRI;
                    break;

                case "SNAM":
                    SubRecordDoorSNAM subRecordDoorSNAM = new SubRecordDoorSNAM();
                    subRecordDoorSNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordDoorSNAM;
                    break;

                case "ANAM":
                    SubRecordDoorANAM subRecordDoorANAM = new SubRecordDoorANAM();
                    subRecordDoorANAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordDoorANAM;
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
