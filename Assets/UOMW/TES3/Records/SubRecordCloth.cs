using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using System.Reflection;
using UnityEngine;
using static UnityEngine.Rendering.DebugUI;


namespace ESMSharp.TES3
{

    public class SubRecordClothNAME : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordClothNAME()
        {



        }
        public SubRecordClothNAME(string type)
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

    public class SubRecordClothtMODL : SubRecords
    {
        private string _model;

        public string model { get { return _model; } }

        public SubRecordClothtMODL()
        {



        }
        public SubRecordClothtMODL(string type)
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

    public class SubRecordClothFNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordClothFNAM()
        {



        }
        public SubRecordClothFNAM(string type)
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

    public class SubRecordClothCTDT : SubRecords
    {
        private ClothingData _data;

        public ClothingData data { get { return _data; } }

        public SubRecordClothCTDT()
        {



        }
        public SubRecordClothCTDT(string type)
        {
            _type = type;
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _data = new ClothingData(reader.ReadUInt32(),reader.ReadSingle(),reader.ReadUInt16(),reader.ReadUInt16());

        }
    }

    public class SubRecordClothITEX : SubRecords
    {
        private string _icon;

        public string icon { get { return _icon; } }

        public SubRecordClothITEX()
        {



        }
        public SubRecordClothITEX(string type)
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
    
    public class SubRecordClothSCRI : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordClothSCRI()
        {



        }
        public SubRecordClothSCRI(string type)
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

    public class SubRecordClothINDX : SubRecords
    {
        private byte _index;
        private string _bnam;
        private string _cnam;

        public byte index { get { return _index; } }

        public SubRecordClothINDX()
        {



        }
        public SubRecordClothINDX(string type)
        {
            _type = type;
        }


        public void Deserialize(BetterReader reader, string name, int size)
        {
            _type = name;
            _index = reader.ReadByte();
            if (size > 1)
            {
                Debug.Log("record size " + size);
                string bnam_a = reader.ReadString(size - 1);
                Debug.Log("Clothing ENAM/BNAM" + bnam_a);

                if (bnam_a[0].ToString().Contains('\0'))
                {

                }

                Utils.LogBuffer("\t- Cloth CNAM/BNAM : {0}", bnam_a);
            }
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;


        }
    }

    public class SubRecordClothENAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordClothENAM()
        {



        }
        public SubRecordClothENAM(string type)
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

    public class SubRecordClothBNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordClothBNAM()
        {



        }
        public SubRecordClothBNAM(string type)
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
    
    public class SubRecordClothCNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordClothCNAM()
        {



        }
        public SubRecordClothCNAM(string type)
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



    public class ClothingData
    {
        private uint _type;
        private float _weight;
        private ushort _value;
        private ushort _enchant;

        public uint type { get { return _type; } }
        public float weight { get { return _weight; } }
        public ushort value { get { return _value; } }
        public ushort enchant { get { return _enchant; } }

        public ClothingData()
        {

        }
    
        public ClothingData(uint type, float weight, ushort value, ushort enchant)
        {
            _type = type;
            _weight = weight;
            _value = value;
            _enchant = enchant;
        }
    }
}
