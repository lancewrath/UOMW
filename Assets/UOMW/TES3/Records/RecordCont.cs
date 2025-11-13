using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using Unity.VisualScripting;
using UnityEngine;
using static ESMSharp.TES3.Master;

namespace ESMSharp.TES3
{

    public class RecordCont : Record
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
                    SubRecordContNAME subRecordContNAME = new SubRecordContNAME();
                    subRecordContNAME.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordContNAME;
                    break;

                case "MODL":
                    SubRecordContMODL subRecordContMODL = new SubRecordContMODL();
                    subRecordContMODL.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordContMODL;
                    break;

                case "FNAM":
                    SubRecordContFNAM subRecordContFNAM = new SubRecordContFNAM();
                    subRecordContFNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordContFNAM;
                    break;

                case "CNDT":
                    SubRecordContCNDT subRecordContCNDT = new SubRecordContCNDT();
                    subRecordContCNDT.Deserialize(reader, srecord);
                    subrecord = subRecordContCNDT;
                    break;

                case "FLAG":
                    SubRecordContFLAG subRecordContFLAG = new SubRecordContFLAG();
                    subRecordContFLAG.Deserialize(reader, srecord);
                    subrecord = subRecordContFLAG;
                    break;

                case "NPCO":
                    SubRecordContNPCO subRecordContNPCO = new SubRecordContNPCO();
                    subRecordContNPCO.Deserialize(reader, srecord);
                    subrecord = subRecordContNPCO;
                    break;

                case "SCRI":
                    SubRecordContSCRI subRecordContSCRI = new SubRecordContSCRI();
                    subRecordContSCRI.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordContSCRI;
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