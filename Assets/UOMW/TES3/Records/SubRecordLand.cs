using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;
using UnityEngine;

namespace ESMSharp.TES3
{

    public class SubRecordLandINTV : SubRecords
    {

        long _cellX = 0;
        long _cellY = 0;


        public long CellX { get { return _cellX; } }
        public long CellY { get { return _cellY; } }



        public SubRecordLandINTV()
        {



        }
        public SubRecordLandINTV(string type)
        {
            _type = type;
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _cellX = reader.ReadInt32();
            _cellY = reader.ReadInt32();
            Utils.LogBuffer("\t- Cell X: {0}", _cellX);
            Utils.LogBuffer("\t- Cell Y: {0}", _cellY);
        }

    }

    public class SubRecordLandVHGT : SubRecords
    {
        float _hoffset = 0;
        byte _unknown2;
        sbyte[][] _heightData;
        ushort _unknown3;

        public float offset { get { return _hoffset; } }
        public sbyte[][] heightdata { get { return _heightData; } }

        public SubRecordLandVHGT()
        {



        }
        public SubRecordLandVHGT(string type)
        {
            _type = type;
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _hoffset = BitConverter.ToSingle(reader.ReadBytes(4));
            _unknown2 = reader.ReadByte(); // 0x00;
            _heightData = new sbyte[65][];
            for (int yy = 0; yy < 65; yy++)
            {
                _heightData[yy] = new sbyte[65];
                for (int xx = 0; xx < 65; xx++)
                {
                    // Read byte and convert to signed byte properly
                    // Values 0-127 stay positive, 128-255 become -128 to -1
                    byte b = reader.ReadByte();
                    _heightData[yy][xx] = (sbyte)(b > 127 ? (int)b - 256 : (int)b);
                }
            }
            _unknown3 = reader.ReadUInt16();
        }

    }

    public class SubRecordLandVNML : SubRecords
    {

        VNML[][] _normals;

        public VNML[][] normals { get { return _normals; } }

        public SubRecordLandVNML()
        {



        }
        public SubRecordLandVNML(string type)
        {
            _type = type;
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _normals = new VNML[65][];
            for (int yy = 0; yy < 65; yy++)
            {
                _normals[yy] = new VNML[65];
                for (int xx = 0; xx < 65; xx++)
                {
                    _normals[yy][xx] = new VNML();
                    _normals[yy][xx].x = (sbyte)reader.ReadByte();
                    _normals[yy][xx].y = (sbyte)reader.ReadByte();
                    _normals[yy][xx].z = (sbyte)reader.ReadByte();

                }
            }
        }

    }

    public class SubRecordLandVCLR : SubRecords
    {

        VNML[][] _colors;

        public VNML[][] colors { get { return _colors; } }

        public SubRecordLandVCLR()
        {



        }
        public SubRecordLandVCLR(string type)
        {
            _type = type;
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            _colors = new VNML[65][];
            for (int yy = 0; yy < 65; yy++)
            {
                _colors[yy] = new VNML[65];
                for (int xx = 0; xx < 65; xx++)
                {
                    _colors[yy][xx] = new VNML();
                    _colors[yy][xx].x = (sbyte)reader.ReadByte();
                    _colors[yy][xx].y = (sbyte)reader.ReadByte();
                    _colors[yy][xx].z = (sbyte)reader.ReadByte();

                }
            }
        }

    }




    public class VNML
    {
        public VNML() { }
        public VNML(sbyte _x, sbyte _y, sbyte _z)
        {
            x = _x; y = _y; z = _z;
        }

        public sbyte x, y, z;
    }
}