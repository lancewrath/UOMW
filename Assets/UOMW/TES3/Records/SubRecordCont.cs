using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using System.Reflection;
using UnityEngine;


namespace ESMSharp.TES3
{

    public class SubRecordContNAME : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordContNAME()
        {



        }
        public SubRecordContNAME(string type)
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

    public class SubRecordContMODL : SubRecords
    {
        private string _model;

        public string model { get { return _model; } }

        public SubRecordContMODL()
        {



        }
        public SubRecordContMODL(string type)
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

    public class SubRecordContFNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordContFNAM()
        {



        }
        public SubRecordContFNAM(string type)
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

    public class SubRecordContCNDT : SubRecords
    {
        private float _value;

        public float value { get { return _value; } }

        public SubRecordContCNDT()
        {

        }
        public SubRecordContCNDT(string type)
        {
            _type = type;
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _value = reader.ReadSingle();


        }
    }

    public class SubRecordContFLAG : SubRecords
    {
        private uint _flag = 0;

        public uint flag { get { return _flag; } }

        public SubRecordContFLAG()
        {
        }
        public SubRecordContFLAG(string type)
        {
            _type = type;
        }
        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _flag = reader.ReadUInt32();


        }

    }

    //Inventory stock of items
    public class SubRecordContNPCO : SubRecords
    {
        private int _count = 0;
        private string _objectName;

        public int count { get { return _count; } }
        public string objectName { get { return _objectName; } }

        public SubRecordContNPCO()
        {
        }
        public SubRecordContNPCO(string type)
        {
            _type = type;
        }
        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _count = reader.ReadInt32();
            _objectName = reader.ReadString(32);


        }

    }

    public class SubRecordContSCRI : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordContSCRI()
        {



        }
        public SubRecordContSCRI(string type)
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
