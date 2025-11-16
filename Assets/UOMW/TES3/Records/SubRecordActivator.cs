using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using System.Reflection;
using UnityEngine;
using static UnityEngine.Rendering.DebugUI;


namespace ESMSharp.TES3
{
    public class SubRecordActiNAME : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordActiNAME()
        {



        }
        public SubRecordActiNAME(string type)
        {
            _type = type;
        }

        public void Deserialize(BetterReader reader, string name, int size)
        {
            _type = name;
            _name = reader.ReadString(size);
            Utils.LogBuffer("\t- Clothing Name: {0}", _name);
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;


        }
    }

    public class SubRecordActiMODL : SubRecords
    {
        private string _model;

        public string model { get { return _model; } }

        public SubRecordActiMODL()
        {



        }
        public SubRecordActiMODL(string type)
        {
            _type = type;
        }

        public void Deserialize(BetterReader reader, string name, int size)
        {
            _type = name;
            _model = reader.ReadString(size);
            Utils.LogBuffer("\t- Clothing Model: {0}", _model);
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;


        }
    }

    public class SubRecordActiFNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordActiFNAM()
        {



        }
        public SubRecordActiFNAM(string type)
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

    public class SubRecordActiSCRI : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordActiSCRI()
        {



        }
        public SubRecordActiSCRI(string type)
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
