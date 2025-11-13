using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using System.Drawing;
using System.Reflection;
using UnityEngine;


namespace ESMSharp.TES3
{

    public class SubRecordNPCNAME : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordNPCNAME()
        {



        }
        public SubRecordNPCNAME(string type)
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

    public class SubRecordNPCMODL : SubRecords
    {
        private string _model;

        public string model { get { return _model; } }

        public SubRecordNPCMODL()
        {



        }
        public SubRecordNPCMODL(string type)
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


    public class SubRecordNPCFNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordNPCFNAM()
        {



        }
        public SubRecordNPCFNAM(string type)
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

    public class SubRecordNPCRNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordNPCRNAM()
        {



        }
        public SubRecordNPCRNAM(string type)
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

    public class SubRecordNPCCNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordNPCCNAM()
        {



        }
        public SubRecordNPCCNAM(string type)
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

    public class SubRecordNPCANAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordNPCANAM()
        {



        }
        public SubRecordNPCANAM(string type)
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

    public class SubRecordNPCBNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordNPCBNAM()
        {



        }
        public SubRecordNPCBNAM(string type)
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

    public class SubRecordNPCKNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordNPCKNAM()
        {



        }
        public SubRecordNPCKNAM(string type)
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
    
    public class SubRecordNPCSCRI : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordNPCSCRI()
        {



        }
        public SubRecordNPCSCRI(string type)
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

    public class SubRecordNPCNPDT : SubRecords
    {
        private string _name;
        private NPCDATA _data;

        public string name { get { return _name; } }
        public NPCDATA data { get { return _data; } }


        public SubRecordNPCNPDT()
        {

        }
        public SubRecordNPCNPDT(string type)
        {
            _type = type;
        }

        public void Deserialize(BetterReader reader, string name, int size)
        {
            Utils.LogBuffer("\t- NPDT SIZE: {0}", size);
            _type = name;
            if(size < 13)
            {
                _data = new NPCDATA(reader.ReadUInt16(), reader.ReadByte(),reader.ReadByte(),reader.ReadByte(),reader.ReadBytes(3),reader.ReadUInt32());
            }
            else
            {
                _data = new NPCDATA(reader.ReadUInt16(), reader.ReadBytes(8), reader.ReadBytes(27), reader.ReadByte(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadByte(),reader.ReadByte(),reader.ReadByte(),reader.ReadByte(),reader.ReadUInt32());
            }
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;


        }
    }

    public class SubRecordNPCFLAG : SubRecords
    {
        private uint _flag = 0;

        public uint flag { get { return _flag; } }

        public SubRecordNPCFLAG()
        {
        }
        public SubRecordNPCFLAG(string type)
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
    public class SubRecordNPCNPCO : SubRecords
    {
        private int _count = 0;
        private string _objectName;

        public int count { get { return _count; } }
        public string objectName { get { return _objectName; } }

        public SubRecordNPCNPCO()
        {
        }
        public SubRecordNPCNPCO(string type)
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

    public class SubRecordNPCNPCS : SubRecords
    {

        private string _spells;

        public string spells { get { return _spells; } }

        public SubRecordNPCNPCS()
        {
        }
        public SubRecordNPCNPCS(string type)
        {
            _type = type;
        }
        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _spells = reader.ReadString(32);


        }

    }



    public class NPCDATA
    {
        //12 data
        private ushort _level;
        private byte _disposition;
        private byte _reputation;
        private byte _rank;
        private byte[] _alignment;
        private uint _gold;
        //52 byte data
        private byte[] _attributes;
        private byte[] _skills;
        private ushort _health;
        private ushort _spellPoints;
        private ushort _fatigue;

        public ushort level { get { return _level; } }
        public byte disposition { get { return _disposition; } }
        public byte reputation { get { return _reputation; } }
        public byte rank { get { return _rank; } } 
        public byte[] alignment { get { return _alignment; } }
        public uint gold { get { return _gold; } }
        public byte[] attributes { get { return _attributes; } }
        public byte[] skills { get { return _skills; } }
        public ushort health { get { return _health; } }
        public ushort spellPoints { get { return _spellPoints; } }
        public ushort fatigue { get { return _fatigue; } }

        public NPCDATA()
        {

        }
        //12 byte constructor
        public NPCDATA(ushort level, byte disposition, byte reputation, byte rank, byte[] alignment, uint gold)
        {
            _level = level;
            _disposition = disposition;
            _reputation = reputation;
            _rank = rank;
            _alignment = alignment;
            _gold = gold;

        }

        //52 byte constructor
        public NPCDATA(ushort level, byte[] attributes, byte[] skills, byte alignment, ushort health, ushort spellPoints, ushort fatigue, byte disposition, byte reputation, byte rank, byte alignmentb, uint gold)
        {
            _level = level;
            _disposition = disposition;
            _reputation = reputation;
            _rank = rank;
            _alignment = new byte[2] { alignment, alignmentb };
            _gold = gold;
            _attributes = attributes;
            _skills = skills;
            _health = health;
            _spellPoints = spellPoints;
            _fatigue = fatigue;
        }
    }
}
