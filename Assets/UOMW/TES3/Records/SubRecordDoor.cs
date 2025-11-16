using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using System.Reflection;
using UnityEngine;
using static UnityEngine.Rendering.DebugUI;


namespace ESMSharp.TES3
{

    public class SubRecordDoorNAME : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordDoorNAME()
        {



        }
        public SubRecordDoorNAME(string type)
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

    public class SubRecordDoorMODL : SubRecords
    {
        private string _model;

        public string model { get { return _model; } }

        public SubRecordDoorMODL()
        {



        }
        public SubRecordDoorMODL(string type)
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

    public class SubRecordDoorFNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordDoorFNAM()
        {



        }
        public SubRecordDoorFNAM(string type)
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

    public class SubRecordDoorSCRI : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordDoorSCRI()
        {



        }
        public SubRecordDoorSCRI(string type)
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

    public class SubRecordDoorSNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordDoorSNAM()
        {



        }
        public SubRecordDoorSNAM(string type)
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

    public class SubRecordDoorANAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordDoorANAM()
        {



        }
        public SubRecordDoorANAM(string type)
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
