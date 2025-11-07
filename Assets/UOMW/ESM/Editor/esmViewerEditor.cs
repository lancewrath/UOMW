using UnityEngine;
using UnityEditor;
using ESMSharp.TES3;
using ESMSharp;
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

            if (GUILayout.Button("Load ESM Data"))
            {
                Utils.LogToFile = true;
                // Call a method on your component when the button is clicked
                _esmviewer.tesesm = new TES3Master(Application.dataPath + "/StreamingAssets/Data/" + _esmviewer.esmFile);
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
                        _esmviewer.tesesm.GenerateTerrainMaps();
                    }
                }
            }
            GUILayout.Label("Records");
        }
    }
}
