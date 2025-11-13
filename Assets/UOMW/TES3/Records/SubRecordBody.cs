using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using System.Drawing;
using System.Reflection;
using UnityEngine;


namespace ESMSharp.TES3
{

    public class SubRecordBodyNAME : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordBodyNAME()
        {



        }
        public SubRecordBodyNAME(string type)
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

    public class SubRecordBodyMODL : SubRecords
    {
        private string _model;

        public string model { get { return _model; } }

        public SubRecordBodyMODL()
        {



        }
        public SubRecordBodyMODL(string type)
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

    public class SubRecordBodyFNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordBodyFNAM()
        {



        }
        public SubRecordBodyFNAM(string type)
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

    public class SubRecordBodyBYDT : SubRecords
    {
        private byte _part = 0;
        private byte _vampire = 0;
        private byte _flags = 0;
        private byte _partType = 0;

        public byte part { get { return _part; } }
        public byte vampire {  get { return _vampire; } }
        public byte flags { get { return _flags; } }
        public byte partType { get { return _partType; } }

        public SubRecordBodyBYDT()
        {
        }
        public SubRecordBodyBYDT(string type)
        {
            _type = type;
        }
        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _part = reader.ReadByte();
            _vampire = reader.ReadByte();
            _flags = reader.ReadByte();
            _partType = reader.ReadByte();
            // Commented out logging to improve performance with many references
            // Utils.LogBuffer("\t- Index: {0}", _index);
        }

    }
}
