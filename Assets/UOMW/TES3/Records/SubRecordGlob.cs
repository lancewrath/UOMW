using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using System.Reflection;
using UnityEngine;


namespace ESMSharp.TES3
{

    public class SubRecordGlobNAME : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordGlobNAME()
        {



        }
        public SubRecordGlobNAME(string type)
        {
            _type = type;
        }

        public void Deserialize(BetterReader reader, string name, int size)
        {
            _type = name;
            _name = reader.ReadString(size);
            Utils.LogBuffer("\t- Global Name: {0}", _name);
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;


        }
    }


    public class SubRecordGlobFLTV : SubRecords
    {
        private byte[] _value;

        public byte[] value { get { return _value; } }

        public SubRecordGlobFLTV()
        {

        }
        public SubRecordGlobFLTV(string type)
        {
            _type = type;
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _value = reader.ReadBytes(4);
            Utils.LogBuffer("\t- Global Value bytes read: {0}", _value.Length);

        }
    }

    public class SubRecordGlobFNAM : SubRecords
    {
        private string _value;

        public string value { get { return _value; } }

        public SubRecordGlobFNAM()
        {



        }
        public SubRecordGlobFNAM(string type)
        {
            _type = type;
        }


        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _value = reader.ReadString(1);
            Utils.LogBuffer("\t- Global Type: {0}", _value);


        }
    }

}
