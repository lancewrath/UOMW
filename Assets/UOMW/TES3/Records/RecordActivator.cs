using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using Unity.VisualScripting;
using UnityEngine;
using static ESMSharp.TES3.Master;

namespace ESMSharp.TES3
{
    public class RecordActivator : Record
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
            Utils.LogBuffer("\t- Activator SubRecord: {0}", srecord);
            uint subrecordsize = reader.ReadUInt32();
            SubRecords subrecord = null;


            switch (srecord)
            {

                case "NAME":
                    SubRecordActiNAME subRecordActiNAME = new SubRecordActiNAME();
                    subRecordActiNAME.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordActiNAME;
                    break;

                case "MODL":
                    SubRecordActiMODL subRecordActiMODL = new SubRecordActiMODL();
                    subRecordActiMODL.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordActiMODL;
                    break;

                case "FNAM":
                    SubRecordActiFNAM subRecordActiFNAM = new SubRecordActiFNAM();
                    subRecordActiFNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordActiFNAM;
                    break;

                case "SCRI":
                    SubRecordActiSCRI subRecordActiSCRI = new SubRecordActiSCRI();
                    subRecordActiSCRI.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordActiSCRI;
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