using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using UnityEngine;
using static ESMSharp.TES3.Master;

namespace ESMSharp.TES3
{

    public class RecordNPC : Record
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
            Utils.LogBuffer("\t- NPCS SubRecord: {0}", srecord);
            uint subrecordsize = reader.ReadUInt32();
            SubRecords subrecord = null;


            switch (srecord)
            {

                case "NAME":
                    SubRecordNPCNAME subRecordNPCNAME = new SubRecordNPCNAME();
                    subRecordNPCNAME.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordNPCNAME;
                    break;

                case "MODL":
                    SubRecordNPCMODL subRecordNPCMODL = new SubRecordNPCMODL();
                    subRecordNPCMODL.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordNPCMODL;
                    break;

                case "FNAM":
                    SubRecordNPCFNAM subRecordNPCFNAM = new SubRecordNPCFNAM();
                    subRecordNPCFNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordNPCFNAM;
                    break;

                case "RNAM":
                    SubRecordNPCRNAM subRecordNPCRNAM = new SubRecordNPCRNAM();
                    subRecordNPCRNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordNPCRNAM;
                    break;

                case "CNAM":
                    SubRecordNPCCNAM subRecordNPCCNAM = new SubRecordNPCCNAM();
                    subRecordNPCCNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordNPCCNAM;
                    break;

                case "ANAM":
                    SubRecordNPCANAM subRecordNPCANAM = new SubRecordNPCANAM();
                    subRecordNPCANAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordNPCANAM;
                    break;

                case "BNAM":
                    SubRecordNPCBNAM subRecordNPCBNAM = new SubRecordNPCBNAM();
                    subRecordNPCBNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordNPCBNAM;
                    break;

                case "KNAM":
                    SubRecordNPCKNAM subRecordNPCKNAM = new SubRecordNPCKNAM();
                    subRecordNPCKNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordNPCKNAM;
                    break;

                case "SCRI":
                    SubRecordNPCSCRI subRecordNPCSCRI = new SubRecordNPCSCRI();
                    subRecordNPCSCRI.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordNPCSCRI;
                    break;

                case "NPDT":
                    SubRecordNPCNPDT subRecordNPCNPDT = new SubRecordNPCNPDT();
                    subRecordNPCNPDT.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordNPCNPDT;
                    break;

                case "FLAG":
                    SubRecordNPCFLAG subRecordNPCFLAG = new SubRecordNPCFLAG();
                    subRecordNPCFLAG.Deserialize(reader, srecord);
                    subrecord = subRecordNPCFLAG;
                    break;

                case "NPCO":
                    SubRecordNPCNPCO subRecordNPCNPCO = new SubRecordNPCNPCO();
                    subRecordNPCNPCO.Deserialize(reader, srecord);
                    subrecord = subRecordNPCNPCO;
                    break;

                case "NPCS":
                    SubRecordNPCNPCS subRecordNPCNPCS = new SubRecordNPCNPCS();
                    subRecordNPCNPCS.Deserialize(reader, srecord);
                    subrecord = subRecordNPCNPCS;
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
