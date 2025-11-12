using ESMSharp;
using ESMSharp.TES3;
using ESMSharp.TES3Terrain;
using UnityEditor;
using UnityEngine;
namespace UOMW.Editor
{
    [CustomEditor(typeof(esmViewer))]
    public class esmViewerEditor : UnityEditor.Editor
    {
        SerializedProperty esmFileProp;
        public GUIStyle guiStyle;

        void OnEnable()
        {
            // Setup the SerializedProperties.
            //esmFileProp = serializedObject.FindProperty("esmFile");

        }

        public override void OnInspectorGUI()
        {

            esmViewer _esmviewer = (esmViewer)target;

            GUILayout.Label("ESM Viewer");
            _esmviewer.esmFile = EditorGUILayout.TextField("ESM File:", _esmviewer.esmFile);
            _esmviewer.bsaFile = EditorGUILayout.TextField("BSA File:", _esmviewer.bsaFile);
            if (GUILayout.Button("Load ESM Data"))
            {
                Utils.LogToFile = true;
                // Call a method on your component when the button is clicked
                _esmviewer.tesesm = new TES3Master(_esmviewer.esmFile, _esmviewer.bsaFile);
                /*
                foreach (Record record in _esmviewer.tesesm.Records) {
                    Debug.Log("Record: " + record.Type);
                }
                */
                Utils.Close();
            }
            if (_esmviewer.tesesm != null)
            {
                if (_esmviewer.tesesm.Loaded)
                {
                    GUILayout.Label("Map Data");

                    if (GUILayout.Button("Generate Height Map"))
                    {
                        _esmviewer.tesesm.GenerateTerrainMaps_MergedLands();
                    }

                    GUILayout.Label("Unity Terrain");
                    if (GUILayout.Button("Generate Terrain"))
                    {
                        _esmviewer.tesesm.GenerateTerrain();

                    }

                    GUILayout.Label("Statics");
                    if (GUILayout.Button("Generate Map Cells"))
                    {
                        _esmviewer.tesesm.GenerateStatics();

                    }
                }
            }
            GUILayout.Label("Records");
        }
    }
}
