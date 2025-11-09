using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using System.Reflection;
using UnityEngine;


namespace ESMSharp.TES3
{
    public class SubRecordStatMODL : SubRecords
    {
        private string _model;

        public string model { get { return _model; } }

        public SubRecordStatMODL()
        {



        }
        public SubRecordStatMODL(string type)
        {
            _type = type;
        }

        public void Deserialize(BetterReader reader, string name, int size)
        {
            _type = name;
            _model = reader.ReadString(size);
            Utils.LogBuffer("\t- Static Model: {0}", _model);
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;


        }
    }

    public class SubRecordStatNAME : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordStatNAME()
        {



        }
        public SubRecordStatNAME(string type)
        {
            _type = type;
        }

        public void Deserialize(BetterReader reader, string name, int size)
        {
            _type = name;
            _name = reader.ReadString(size);
            Utils.LogBuffer("\t- Static Name: {0}", _name);
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;


        }
    }
}
