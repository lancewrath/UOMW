using ESMSharp.Core;
using ESMSharp.TES3.Records;
using System;

namespace ESMSharp.TES3
{
    /// <summary>
    /// Second NAME record in CELL reference - contains the Object ID (model ID) for static objects
    /// </summary>
    public class SubRecordCellObjectID : SubRecords
    {
        private string _objectId;

        public string objectId { get { return _objectId; } }

        public SubRecordCellObjectID()
        {
        }

        public SubRecordCellObjectID(string type)
        {
            _type = type;
        }

        public void Deserialize(BetterReader reader, string name, int size)
        {
            _type = name;
            _objectId = reader.ReadString(size);
            // Commented out logging to improve performance with many references
            // Utils.LogBuffer("\t- Object ID: {0}", _objectId);
        }

        public override void Deserialize(BetterReader reader, string name)
        {
            _type = name;
        }
    }
}

