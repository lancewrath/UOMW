using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using System.Reflection;
using UnityEngine;


namespace ESMSharp.TES3
{
    public class SubRecordGmstNAME : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordGmstNAME()
        {



        }
        public SubRecordGmstNAME(string type)
        {
            _type = type;
        }

        public void Deserialize(BetterReader reader, string name, int size)
        {
            _type = name;
            _name = reader.ReadString(size);
            Utils.LogBuffer("\t- Setting Name: {0}", _name);
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;


        }
    }

    public class SubRecordGmstFLTV : SubRecords
    {
        private float _value;

        public float value { get { return _value; } }

        public SubRecordGmstFLTV()
        {

        }
        public SubRecordGmstFLTV(string type)
        {
            _type = type;
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _value = reader.ReadSingle();
            Utils.LogBuffer("\t- Setting Value: {0}", _value);

        }
    }

    public class SubRecordGmstINTV : SubRecords
    {
        private int _value;

        public int value { get { return _value; } }

        public SubRecordGmstINTV()
        {

        }
        public SubRecordGmstINTV(string type)
        {
            _type = type;
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _value = reader.ReadInt32();
            Utils.LogBuffer("\t- Setting Value: {0}", _value);
        }
    }

    public class SubRecordGmstSTRV : SubRecords
    {
        private string _value;

        public string value { get { return _value; } }

        public SubRecordGmstSTRV()
        {



        }
        public SubRecordGmstSTRV(string type)
        {
            _type = type;
        }

        public void Deserialize(BetterReader reader, string name, int size)
        {
            _type = name;
            _value = reader.ReadString(size);
            Utils.LogBuffer("\t- String Value: {0}", _value);
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;


        }
    }
}
