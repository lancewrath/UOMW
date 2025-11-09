using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using UnityEngine;


namespace ESMSharp.TES3
{
    public class SubRecordLTexData : SubRecords
    {
        private string _filename = "";

        public string filename { get { return _filename; } }

        public SubRecordLTexData()
        {
        }
        public SubRecordLTexData(string type)
        {
            _type = type;
        }

        public void Deserialize(BetterReader reader, string name, int size)
        {
            _type = name;
            _filename = reader.ReadString(size);
            Utils.LogBuffer("\t- Name: {0}", _filename);
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;


        }
    }

    public class SubRecordLTexINTV : SubRecords
    {
        private int _index = 0;

        public int index { get { return _index; } }

        public SubRecordLTexINTV()
        {
        }
        public SubRecordLTexINTV(string type)
        {
            _type = type;
        }
        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _index = reader.ReadInt32();
            Utils.LogBuffer("\t- Index: {0}", _index);

        }

    }


    public class SubRecordLTexNAME : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordLTexNAME()
        {



        }
        public SubRecordLTexNAME(string type)
        {
            _type = type;
        }

        public void Deserialize(BetterReader reader, string name, int size)
        {
            _type = name;
            _name = reader.ReadString(size);
            Utils.LogBuffer("\t- Name: {0}", _name);
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;


        }
    }
}