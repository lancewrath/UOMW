using ESMSharp.TES3;
using ESMSharp.TES3.Records;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UOMW;

namespace ESMSharp.TES3Terrain.Editor
{
    [CustomEditor(typeof(TESCell))]
    public class TESCellEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            TESCell tescell = (TESCell)target;
            GUILayout.Label("Cell Statics");
            if (!tescell.staticsGenerated)
            {
                
                if (GUILayout.Button("Generate Cell Statics"))
                {
                    tescell.GenerateStatics();

                }
            } else
            {
                GUILayout.Label("Statics Generated");
            }

        }
    }
}
