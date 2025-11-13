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


    public class MWScriptHeader
    {
        private string _name;
        private uint _numshorts;
        private uint _numlongs;
        private uint _numfloats;
        private uint _datasize;
        private uint _varsize;

        public MWScriptHeader()
        {

        }

        public MWScriptHeader(string n, uint ns, uint nl, uint nf, uint ds, uint vs)
        {
            _name = n;
            _numshorts = ns;
            _numlongs = nl;
            _numfloats = nf;
            _datasize = ds;
            _varsize = vs;
        }


        public string name { get { return _name; } }
        public uint numshorts { get { return _numshorts; } }
        public uint numlongs { get { return _numlongs; } }
        public uint numfloats { get { return _numfloats; } }
        public uint datasize { get { return _datasize; } }
        public uint varsize { get { return _varsize; } }
    }

    public class SubRecordScptSCHD : SubRecords
    {
        private MWScriptHeader _scriptheader = null;

        public MWScriptHeader scriptHeader { get { return _scriptheader; } }

        public SubRecordScptSCHD()
        {



        }
        public SubRecordScptSCHD(string type)
        {
            _type = type;
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
            string _name = reader.ReadString(32);
            uint _numshorts = reader.ReadUInt32();
            uint _numlongs = reader.ReadUInt32();
            uint _numfloats = reader.ReadUInt32();
            uint _datasize = reader.ReadUInt32();
            uint _varsize = reader.ReadUInt32();

            _scriptheader = new MWScriptHeader(_name, _numshorts, _numlongs, _numfloats, _datasize, _varsize);

        }
    }

    public class SubRecordScptSCVR : SubRecords
    {
        private string[] _variables = null;
        
        public string[] variables { get { return _variables; } }

        public SubRecordScptSCVR()
        {



        }
        public SubRecordScptSCVR(string type)
        {
            _type = type;
        }

        public void Deserialize(BetterReader reader, string name, int size)
        {
            _type = name;
            string _name = reader.ReadString(size);
            char nullChar = '\0';
            _variables = _name.Split(new char[] { nullChar }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < _variables.Length; i++)
            {
                Utils.LogBuffer("\t- Script Variable: {0}", _variables[i]);
            }
            
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;


        }
    }

    public class SubRecordSctpSCTX : SubRecords
    {
        private string _name;

        public string name { get { return _name; } }

        public SubRecordSctpSCTX()
        {



        }
        public SubRecordSctpSCTX(string type)
        {
            _type = type;
        }

        public void Deserialize(BetterReader reader, string name, int size, string filename)
        {
            Deserialize(reader, name, size, filename, null, null);
        }
        
        public void Deserialize(BetterReader reader, string name, int size, string filename, MWScriptHeader header, string[] variables)
        {
            _type = name;
            _name = reader.ReadString(size);
            filename = SanitizeFileName(filename)+".mws";
            Debug.Log("File: " + filename);
            
            // Create cache directory and save script to file
            Directory.CreateDirectory(System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Scripts"));      
            File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Scripts", filename), _name);
            
            // Note: Script registration with TESMWScriptManager is handled in TESESMLibrary
            // after the full RecordScpt is parsed, so we have access to the source ESM filename
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;


        }

        public static string SanitizeFileName(string fileName)
        {
            // Get the array of invalid characters for filenames
            char[] invalidChars = Path.GetInvalidFileNameChars();

            // Create a regex pattern to match any of these invalid characters
            // Regex.Escape is used to ensure special regex characters in invalidChars are treated literally
            string pattern = $"[{Regex.Escape(new string(invalidChars))}]";

            // Replace all invalid characters with an underscore or an empty string
            // You can choose to replace with an empty string if you prefer to simply remove them
            string sanitizedFileName = Regex.Replace(fileName, pattern, "_");

            // Optional: Trim leading/trailing whitespace
            sanitizedFileName = sanitizedFileName.Trim();

            // Optional: Handle special Windows reserved filenames (CON, PRN, AUX, etc.)
            // This requires a more comprehensive check if cross-platform compatibility is critical,
            // but for basic cleaning, removing invalid characters is usually sufficient.

            return sanitizedFileName;
        }
    }

}
