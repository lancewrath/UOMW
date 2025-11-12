using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using UnityEngine;

namespace ESMSharp.TES3
{
    public class RecordScpt : Record
    {
        private MWScriptHeader _header = null;
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
            Utils.LogBuffer("\t- SCPT SubRecord: {0}", srecord);
            uint subrecordsize = reader.ReadUInt32();
            SubRecords subrecord = null;


            switch (srecord)
            {
                case "SCHD":
                    SubRecordScptSCHD subRecordSCptSCHD = new SubRecordScptSCHD();
                    subRecordSCptSCHD.Deserialize(reader, srecord);
                    subrecord = subRecordSCptSCHD;
                    _header = subRecordSCptSCHD.scriptHeader;

                    break;

                case "SCVR":
                    SubRecordScptSCVR subRecordScptSCVR = new SubRecordScptSCVR();
                    subRecordScptSCVR.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordScptSCVR;
                    break;

                case "SCDT":
                    subrecord = new SubRecords(srecord);
                    reader.ReadBytes((int)subrecordsize);
                    break;

                case "SCTX":
                    if (_header != null)
                    {
                        SubRecordSctpSCTX subRecordsctpSCTX = new SubRecordSctpSCTX();
                        subRecordsctpSCTX.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize),_header.name);
                        subrecord = subRecordsctpSCTX;
                    }
                    else
                    {
                        subrecord = new SubRecords(srecord);
                        reader.ReadBytes((int)subrecordsize);
                    }
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
