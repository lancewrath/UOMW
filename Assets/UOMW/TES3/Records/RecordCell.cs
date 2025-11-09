using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using UnityEngine;
using static ESMSharp.TES3.Master;

namespace ESMSharp.TES3
{
    public class RecordCell : Record
    {
        private bool hasCellData = false;
        private bool hasNameData = false;
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
            // Commented out logging to improve performance - can be very verbose with many cells
            // Utils.LogBuffer("\t- Cell SubRecord: {0}", srecord);
            uint subrecordsize = reader.ReadUInt32();
            SubRecords subrecord = null;

            switch (srecord)
            {
                case "NAME":
                    // First NAME is cell name, second NAME is object ID (model ID for statics)
                    if (!hasNameData)
                    {
                        SubRecordCellNAME subRecordCellNAME = new SubRecordCellNAME();
                        subRecordCellNAME.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                        subrecord = subRecordCellNAME;
                        hasNameData = true;
                    }
                    else
                    {
                        SubRecordCellObjectID subRecordCellObjectID = new SubRecordCellObjectID();
                        subRecordCellObjectID.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                        subrecord = subRecordCellObjectID;
                    }
                    break;

                case "DATA":
                    if (!hasCellData)
                    {
                        SubRecordCellDATA subRecordCellDATA = new SubRecordCellDATA();
                        subRecordCellDATA.Deserialize(reader, srecord);
                        subrecord = subRecordCellDATA;
                        hasCellData = true;
                    } else
                    {
                        SubRecordCellREFP subRecordCellREFP = new SubRecordCellREFP();
                        subRecordCellREFP.Deserialize(reader, srecord);
                        subrecord = subRecordCellREFP;
                        hasCellData = true;
                    }
                    break;

                case "RGNN":
                    SubRecordCellRGNN subRecordCellRGNN = new SubRecordCellRGNN();
                    subRecordCellRGNN.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordCellRGNN;
                    break;

                case "NAM0":
                    reader.ReadUInt32();
                    break;

                case "NAM5":
                    reader.ReadUInt32();
                    break;

                case "WHGT":
                    reader.ReadSingle();
                    break;

                case "AMBI":
                    reader.ReadInt32();
                    reader.ReadInt32();
                    reader.ReadInt32();
                    reader.ReadSingle();
                    break;

                case "FRMR":
                    SubRecordCellFRMR subRecordCellFRMR = new SubRecordCellFRMR();
                    subRecordCellFRMR.Deserialize(reader, srecord);
                    subrecord = subRecordCellFRMR;
                    break;

                case "XSCL":
                    SubRecordCellXSCL subRecordCellXSCL = new SubRecordCellXSCL();
                    subRecordCellXSCL.Deserialize(reader, srecord);
                    subrecord = subRecordCellXSCL;
                    break;

                case "DELE":
                    reader.ReadInt32();
                    break;

                case "DODT":
                    SubRecordCellDODT subRecordCellDODT = new SubRecordCellDODT();
                    subRecordCellDODT.Deserialize(reader, srecord);
                    subrecord = subRecordCellDODT;
                    break;

                case "DNAM":
                    SubRecordCellDNAM subRecordCellDNAM = new SubRecordCellDNAM();
                    subRecordCellDNAM.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordCellDNAM;
                    break;

                case "FLTV":
                    reader.ReadUInt32();
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
