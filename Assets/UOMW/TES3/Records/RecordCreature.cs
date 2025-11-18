using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using UnityEngine;

namespace ESMSharp.TES3
{
    public class RecordCreature : Record
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
            Utils.LogBuffer("\t- Creature SubRecord: {0}", srecord);
            uint subrecordsize = reader.ReadUInt32();
            SubRecords subrecord = null;


            switch (srecord)
            {

                case "NAME":
                    SubRecordCREANAME subRecordCREANAME = new SubRecordCREANAME();
                    subRecordCREANAME.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordCREANAME;
                    break;

                case "MODL":
                    SubRecordCREAMODL subRecordCREAMODL = new SubRecordCREAMODL();
                    subRecordCREAMODL.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordCREAMODL;
                    break;

                case "CNAM":
                    SubRecordCREACNAM subRecordCREACNAM = new SubRecordCREACNAM();
                    subRecordCREACNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordCREACNAM;
                    break;

                case "FNAM":
                    SubRecordCREAFNAM subRecordCREAFNAM = new SubRecordCREAFNAM();
                    subRecordCREAFNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordCREAFNAM;
                    break;

                case "SCRI":
                    SubRecordCREASCRI subRecordCREASCRI = new SubRecordCREASCRI();
                    subRecordCREASCRI.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordCREASCRI;
                    break;

                case "NPDT":
                    SubRecordCREANPDT subRecordCREANPDT = new SubRecordCREANPDT();
                    subRecordCREANPDT.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordCREANPDT;
                    break;

                case "FLAG":
                    SubRecordCREAFLAG subRecordCREAFLAG = new SubRecordCREAFLAG();
                    subRecordCREAFLAG.Deserialize(reader, srecord);
                    subrecord = subRecordCREAFLAG;
                    break;

                case "RNAM":
                    SubRecordCREARNAM subRecordCREARNAM = new SubRecordCREARNAM();
                    subRecordCREARNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordCREARNAM;
                    break;

                case "XSCL":
                    SubRecordCREAXSCL subRecordCREAXSCL = new SubRecordCREAXSCL();
                    subRecordCREAXSCL.Deserialize(reader, srecord);
                    subrecord = subRecordCREAXSCL;
                    break;

                case "NPCO":
                    SubRecordCREANPCO subRecordCREANPCO = new SubRecordCREANPCO();
                    subRecordCREANPCO.Deserialize(reader, srecord);
                    subrecord = subRecordCREANPCO;
                    break;

                case "NPCS":
                    SubRecordCREANPCS subRecordCREANPCS = new SubRecordCREANPCS();
                    subRecordCREANPCS.Deserialize(reader, srecord);
                    subrecord = subRecordCREANPCS;
                    break;

                case "DODT":
                    SubRecordCREADODT subRecordCREADODT = new SubRecordCREADODT();
                    subRecordCREADODT.Deserialize(reader, srecord);
                    subrecord = subRecordCREADODT;
                    break;

                case "DNAM":
                    SubRecordCREADNAM subRecordCREADNAM = new SubRecordCREADNAM();
                    subRecordCREADNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordCREADNAM;
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
