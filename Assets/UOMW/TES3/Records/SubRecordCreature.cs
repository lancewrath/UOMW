using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using System.IO;
using System.Text.RegularExpressions;

namespace ESMSharp.TES3
{
    public class SubRecordCREANAME : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordCREANAME()
        {



        }
        public SubRecordCREANAME(string type)
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

    public class SubRecordCREAMODL : SubRecords
    {
        private string _model;

        public string model { get { return _model; } }

        public SubRecordCREAMODL()
        {



        }
        public SubRecordCREAMODL(string type)
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


    public class SubRecordCREAFNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordCREAFNAM()
        {



        }
        public SubRecordCREAFNAM(string type)
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

    public class SubRecordCREADNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordCREADNAM()
        {



        }
        public SubRecordCREADNAM(string type)
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

    public class SubRecordCREARNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordCREARNAM()
        {



        }
        public SubRecordCREARNAM(string type)
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


    public class SubRecordCREAXSCL : SubRecords
    {
        private float _scale;

        public float scale { get { return _scale; } }

        public SubRecordCREAXSCL()
        {



        }
        public SubRecordCREAXSCL(string type)
        {
            _type = type;
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _scale = reader.ReadSingle();

        }
    }


    public class SubRecordCREADODT : SubRecords
    {
        private float _x = 0;
        private float _y = 0;
        private float _z = 0;

        private float _roll = 0;
        private float _yaw = 0;
        private float _pitch = 0;

        public float x { get { return _x; } }
        public float y { get { return _y; } }
        public float z { get { return _z; } }

        public float roll { get { return _roll; } }
        public float yaw { get { return _yaw; } }
        public float pitch { get { return _pitch; } }

        public SubRecordCREADODT()
        {

        }
        public SubRecordCREADODT(string type)
        {
            _type = type;
        }
        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _x = reader.ReadSingle();
            _y = reader.ReadSingle();
            _z = reader.ReadSingle();
            _roll = reader.ReadSingle();
            _yaw = reader.ReadSingle();
            _pitch = reader.ReadSingle();

            // Commented out excessive logging - can cause severe performance issues
            // Utils.LogBuffer("\t- x: {0}", _x);
            // Utils.LogBuffer("\t- y: {0}", _y);
            // Utils.LogBuffer("\t- z: {0}", _z);
            // Utils.LogBuffer("\t- roll: {0}", _roll);
            // Utils.LogBuffer("\t- yaw: {0}", _yaw);
            // Utils.LogBuffer("\t- pitch: {0}", _pitch);
        }
    }


    public class SubRecordCREAFLAG : SubRecords
    {
        private uint _flag = 0;

        public uint flag { get { return _flag; } }

        public SubRecordCREAFLAG()
        {
        }
        public SubRecordCREAFLAG(string type)
        {
            _type = type;
        }
        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _flag = reader.ReadUInt32();


        }

    }


    public class SubRecordCREANPDT : SubRecords
    {
        private string _name;
        private CREATUREDATA _data;

        public string name { get { return _name; } }
        public CREATUREDATA data { get { return _data; } }


        public SubRecordCREANPDT()
        {

        }
        public SubRecordCREANPDT(string type)
        {
            _type = type;
        }

        public void Deserialize(BetterReader reader, string name, int size)
        {
            Utils.LogBuffer("\t- NPDT SIZE: {0}", size);
            _type = name;
            _data = new CREATUREDATA(reader.ReadUInt32(), reader.ReadUInt32(), new uint[8] { reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32() }, reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32());

        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;


        }
    }


    public class SubRecordCREACNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordCREACNAM()
        {



        }
        public SubRecordCREACNAM(string type)
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

    public class SubRecordCREASCRI : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordCREASCRI()
        {



        }
        public SubRecordCREASCRI(string type)
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


    //Inventory stock of items
    public class SubRecordCREANPCO : SubRecords
    {
        private int _count = 0;
        private string _objectName;

        public int count { get { return _count; } }
        public string objectName { get { return _objectName; } }

        public SubRecordCREANPCO()
        {
        }
        public SubRecordCREANPCO(string type)
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

    public class SubRecordCREANPCS : SubRecords
    {

        private string _spells;

        public string spells { get { return _spells; } }

        public SubRecordCREANPCS()
        {
        }
        public SubRecordCREANPCS(string type)
        {
            _type = type;
        }
        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _spells = reader.ReadString(32);


        }

    }

    public class CREATUREDATA
    {
        private uint _type;
        private uint _level;
        private uint[] _attributes;
        private uint _health;
        private uint _spellpoints;
        private uint _fatigue;
        private uint _soul;
        private uint _combat;
        private uint _magic;
        private uint _stealth;
        private uint _attackMin1;
        private uint _attackMax1;
        private uint _attackMin2;
        private uint _attackMax2;
        private uint _attackMin3;
        private uint _attackMax3;
        private uint _gold;

        public uint type { get { return _type; } }
        public uint level { get { return _level; } }
        public uint[] attributes { get { return _attributes; } }
        public uint health { get { return _health; } }
        public uint spellpoints { get { return _spellpoints; } }
        public uint fatigue { get { return _fatigue; } }
        public uint soul { get { return _soul; } }
        public uint combat { get { return _combat; } }
        public uint magic { get { return _magic; } }
        public uint stealth { get { return _stealth; } }
        public uint attackMin1 { get { return _attackMin1; } }
        public uint attackMax1 { get { return _attackMax1; } }
        public uint attackMin2 { get { return _attackMin2; } }
        public uint attackMax2 { get { return _attackMax2; } }
        public uint attackMin3 { get { return _attackMin3; } }
        public uint attackMax3 { get { return _attackMax3; } }
        public uint gold { get { return _gold; } }

        public CREATUREDATA(uint t, uint l, uint[] at, uint h, uint sp, uint f, uint sl, uint c, uint m, uint s, uint amn1, uint amx1, uint amn2, uint amx2, uint amn3, uint amx3, uint g)
        {
            _type = t;
            _level = t;
            _attributes = at;
            _health = h;
            _spellpoints = sp;
            _fatigue = f;
            _soul = s;
            _combat = c;
            _magic = m;
            _attackMin1 = amn1;
            _attackMax1 = amx1;
            _attackMin2 = amn2;
            _attackMax2 = amx2;
            _attackMin3 = amn3;
            _attackMax3 = amx3;
            _gold = g;


        }

        public CREATUREDATA()
        {

        }



    }
}