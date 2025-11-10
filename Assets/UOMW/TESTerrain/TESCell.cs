using ESMSharp.TES3;
using ESMSharp.TES3.Records;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ESMSharp.TES3Terrain
{
    public class TESCell : MonoBehaviour
    {
        public RecordCell RecordCell { get; private set; }
        public CellManager CellManager { get; private set; }
        private Record[] _allRecords = null;
        private string _esm = "";
        private string _bsa = "";

        public bool staticsGenerated = false;
        public float vhgtHeight { get; private set; } = 0f; // VHGT height offset for this cell (cached for efficiency)

        public void SetCell(RecordCell recordCell, CellManager cellManager, Record[] allRecords, string esm, string bsa, float vhgtHeight = 0f)
        {
            RecordCell = recordCell;
            CellManager = cellManager;
            _allRecords = allRecords;
            _esm = esm;
            _bsa = bsa;
            this.vhgtHeight = vhgtHeight;
        }

        /// <summary>
        /// Generates statics for this cell only
        /// </summary>
        public void GenerateStatics()
        {
            if (RecordCell == null || _allRecords == null)
            {
                //UnityEngine.Debug.LogWarning($"TESCell: Cannot generate statics - missing RecordCell or records");
                return;
            }

            // Skip interior cells
            bool isInterior = false;
            foreach (SubRecords subrec in RecordCell.subRecords)
            {
                if (subrec is SubRecordCellDATA dataSubrec)
                {
                    isInterior = (dataSubrec.flags & 0x01) != 0;
                    break;
                }
            }

            if (isInterior)
            {
                //UnityEngine.Debug.Log($"TESCell: Skipping interior cell at ({GetGridX()}, {GetGridY()})");
                return;
            }

            // Get terrain object if it exists
            Terrain terrain = GameObject.FindFirstObjectByType<Terrain>();

            // Create PlaceStatics instance
            PlaceStatics placeStatics = new PlaceStatics(_esm, _bsa);

            // Place statics for this cell only
            //UnityEngine.Debug.Log($"TESCell: Generating statics for cell ({GetGridX()}, {GetGridY()})");
            placeStatics.PlaceCellStatics(RecordCell, _allRecords, CellManager, transform, terrain);

            staticsGenerated = true;
        }

        /// <summary>
        /// Gets the grid X coordinate for this cell
        /// </summary>
        public int GetGridX()
        {
            if (RecordCell?.subRecords == null) return 0;
            foreach (SubRecords subrec in RecordCell.subRecords)
            {
                if (subrec is SubRecordCellDATA dataSubrec)
                {
                    return dataSubrec.gridX;
                }
            }
            return 0;
        }

        /// <summary>
        /// Gets the grid Y coordinate for this cell
        /// </summary>
        public int GetGridY()
        {
            if (RecordCell?.subRecords == null) return 0;
            foreach (SubRecords subrec in RecordCell.subRecords)
            {
                if (subrec is SubRecordCellDATA dataSubrec)
                {
                    return dataSubrec.gridY;
                }
            }
            return 0;
        }

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {

        }
    }
}
