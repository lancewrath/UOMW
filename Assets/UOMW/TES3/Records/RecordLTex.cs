using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using UnityEngine;
using static ESMSharp.TES3.Master;

namespace ESMSharp.TES3
{
    public class RecordLTex : Record
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
            Utils.LogBuffer("\t- LTex SubRecord: {0}", srecord);
            uint subrecordsize = reader.ReadUInt32();
            SubRecords subrecord = null;


            switch (srecord)
            {

                case "NAME":
                    SubRecordLTexNAME subRecordLTexNAME = new SubRecordLTexNAME();
                    subRecordLTexNAME.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordLTexNAME;
                    break;

                case "INTV":
                    SubRecordLTexINTV subRecordLTexINTV = new SubRecordLTexINTV();
                    subRecordLTexINTV.Deserialize(reader, srecord);
                    subrecord = subRecordLTexINTV;
                    break;

                case "DATA":
                    SubRecordLTexData subRecordLTexDATA = new SubRecordLTexData();
                    subRecordLTexDATA.Deserialize(reader, srecord, Convert.ToInt32(subrecordsize));
                    subrecord = subRecordLTexDATA;
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
