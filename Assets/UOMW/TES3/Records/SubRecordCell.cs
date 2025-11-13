using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using System.Drawing;
using System.Reflection;
using UnityEngine;


namespace ESMSharp.TES3
{

    public class SubRecordCellXSCL : SubRecords
    {
        private float _scale;

        public float scale { get { return _scale; } }

        public SubRecordCellXSCL()
        {



        }
        public SubRecordCellXSCL(string type)
        {
            _type = type;
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _scale = reader.ReadSingle();

        }
    }

    public class SubRecordCellREFP : SubRecords
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

        public SubRecordCellREFP()
        {

        }
        public SubRecordCellREFP(string type)
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

            // Commented out excessive logging - can cause severe performance issues with many references
            // Each cell can have hundreds of references, and each reference logs 6 values
            // Utils.LogBuffer("\t- x: {0}", _x);
            // Utils.LogBuffer("\t- y: {0}", _y);
            // Utils.LogBuffer("\t- z: {0}", _z);
            // Utils.LogBuffer("\t- roll: {0}", _roll);
            // Utils.LogBuffer("\t- yaw: {0}", _yaw);
            // Utils.LogBuffer("\t- pitch: {0}", _pitch);
        }
    }

    public class SubRecordCellDNAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordCellDNAM()
        {



        }
        public SubRecordCellDNAM(string type)
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

    public class SubRecordCellANAM : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordCellANAM()
        {



        }
        public SubRecordCellANAM(string type)
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


    public class SubRecordCellDODT : SubRecords
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

        public SubRecordCellDODT()
        {

        }
        public SubRecordCellDODT(string type)
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

    public class SubRecordCellFRMR : SubRecords
    {
        private uint _index = 0;

        public uint index { get { return _index; } }

        public SubRecordCellFRMR()
        {
        }
        public SubRecordCellFRMR(string type)
        {
            _type = type;
        }
        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _index = reader.ReadUInt32();
            // Commented out logging to improve performance with many references
            // Utils.LogBuffer("\t- Index: {0}", _index);
        }

    }


    public class SubRecordCellDATA : SubRecords
    {
        private uint _flags = 0;
        private int _gridX = 0;
        private int _gridY = 0;

        public uint flags { get { return _flags; } }
        public int gridX { get { return _gridX; } }
        public int gridY { get { return _gridY; } }
    
        public SubRecordCellDATA()
        {

        }
        public SubRecordCellDATA(string type)
        {
            _type = type;
        }
        public override void Deserialize(BetterReader reader, string name)
        {

            _type = name;
            _flags = reader.ReadUInt32();
            _gridX = reader.ReadInt32();
            _gridY = reader.ReadInt32();
            Utils.LogBuffer("\t- flags: {0}", _flags);
            Utils.LogBuffer("\t- grid x: {0}", _gridX);
            Utils.LogBuffer("\t- grid y: {0}", _gridY);
        }
    }


    public class SubRecordCellRGNN : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordCellRGNN()
        {



        }
        public SubRecordCellRGNN(string type)
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

    public class SubRecordCellNAME : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordCellNAME()
        {



        }
        public SubRecordCellNAME(string type)
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
