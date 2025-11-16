using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using System.Reflection;
using UnityEngine;
using static UnityEngine.Rendering.DebugUI;


namespace ESMSharp.TES3
{

    public class SubRecordArmorNAME : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordArmorNAME()
        {



        }
        public SubRecordArmorNAME(string type)
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

    public class SubRecordArmorMODL : SubRecords
    {
        private string _model;

        public string model { get { return _model; } }

        public SubRecordArmorMODL()
        {



        }
        public SubRecordArmorMODL(string type)
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

    public class SubRecordArmorFNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordArmorFNAM()
        {



        }
        public SubRecordArmorFNAM(string type)
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

    public class SubRecordArmorAODT : SubRecords
    {
        private ArmorData _data;

        public ArmorData data { get { return _data; } }

        public SubRecordArmorAODT()
        {



        }
        public SubRecordArmorAODT(string type)
        {
            _type = type;
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _data = new ArmorData(reader.ReadUInt32(), reader.ReadSingle(), reader.ReadUInt32(), reader.ReadUInt32(),reader.ReadUInt32(),reader.ReadUInt32());

        }
    }

    public class SubRecordArmorITEX : SubRecords
    {
        private string _icon;

        public string icon { get { return _icon; } }

        public SubRecordArmorITEX()
        {



        }
        public SubRecordArmorITEX(string type)
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

    public class SubRecordArmorSCRI : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordArmorSCRI()
        {



        }
        public SubRecordArmorSCRI(string type)
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

    public class SubRecordArmorINDX : SubRecords
    {
        private byte _index;
        private string _bnam;
        private string _cnam;

        public byte index { get { return _index; } }

        public SubRecordArmorINDX()
        {



        }
        public SubRecordArmorINDX(string type)
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

    public class SubRecordArmorENAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordArmorENAM()
        {



        }
        public SubRecordArmorENAM(string type)
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

    public class SubRecordArmorBNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordArmorBNAM()
        {



        }
        public SubRecordArmorBNAM(string type)
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

    public class SubRecordArmorCNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordArmorCNAM()
        {



        }
        public SubRecordArmorCNAM(string type)
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



    public class ArmorData
    {
        private uint _type;
        private float _weight;
        private uint _value;
        private uint _health;
        private uint _enchant;
        private uint _armor;

        public uint type { get { return _type; } }
        public float weight { get { return _weight; } }
        public uint value { get { return _value; } }
        public uint health { get { return _health; } }
        public uint enchant { get { return _enchant; } }
        public uint armor { get { return _armor; } }

        public ArmorData()
        {

        }

        public ArmorData(uint type, float weight, uint value, uint health, uint enchant, uint armor)
        {
            _type = type;
            _weight = weight;
            _value = value;
            _health = health;
            _enchant = enchant;
            _armor = armor;
        }
    }
}
