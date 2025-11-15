using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using System.Reflection;
using UnityEngine;
using static UnityEngine.Rendering.DebugUI;


namespace ESMSharp.TES3
{

    public class SubRecordLightNAME : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordLightNAME()
        {



        }
        public SubRecordLightNAME(string type)
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

    public class SubRecordLightMODL : SubRecords
    {
        private string _model;

        public string model { get { return _model; } }

        public SubRecordLightMODL()
        {



        }
        public SubRecordLightMODL(string type)
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

    public class SubRecordLightFNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordLightFNAM()
        {



        }
        public SubRecordLightFNAM(string type)
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

    public class SubRecordLightITEX : SubRecords
    {
        private string _icon;

        public string icon { get { return _icon; } }

        public SubRecordLightITEX()
        {



        }
        public SubRecordLightITEX(string type)
        {
            _type = type;
        }

        public void Deserialize(BetterReader reader, string name, int size)
        {
            _type = name;
            _icon = reader.ReadString(size);
            Utils.LogBuffer("\t- Icon: {0}", _icon);
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;


        }
    }

    public class SubRecordLightLHDT : SubRecords
    {
        private LightData _light;

        public LightData light { get { return _light; } }

        public SubRecordLightLHDT()
        {

        }
        public SubRecordLightLHDT(string type)
        {
            _type = type;

        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _light = new LightData(reader.ReadSingle(),reader.ReadUInt32(),reader.ReadInt32(),reader.ReadUInt32(),reader.ReadBytes(4),reader.ReadUInt32());


        }

    }

    public class SubRecordLightSNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordLightSNAM()
        {



        }
        public SubRecordLightSNAM(string type)
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
    
    public class SubRecordLightSCRI : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordLightSCRI()
        {



        }
        public SubRecordLightSCRI(string type)
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

    public class LightData
    {
        private float _weight = 0f;
        private uint _value = 0;
        private int _time = 0;
        private uint _radius = 0;
        //4 bytes for color R,G,B, A? 
        private byte[] _color;
        private uint _flags;

        public float weight { get { return _weight; } }
        public uint value { get { return _value; } }
        public int time { get { return _time; } }
        public uint radius { get { return _radius; } }
        public byte[] color { get { return _color; } }
        public uint flags { get { return _flags; } }


        public LightData() { }

        public LightData(float w, uint v, int t, uint r, byte[] col, uint f)
        {
            _weight = w;
            _value = v;
            _time = t;
            _radius = r;
            _color = col;
            _flags = f;

        }


    }
}