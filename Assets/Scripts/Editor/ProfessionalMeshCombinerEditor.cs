using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(MeshCombiner))]
public class MeshCombinerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        
        MeshCombiner script = (MeshCombiner)target;
        
        GUILayout.Space(10);
        
        // Ce bouton appelle la fonction du script principal
        if (GUILayout.Button("COMBINER & GÉNÉRER UVs (Lourd)", GUILayout.Height(50)))
        {
            script.CombineForBaking();
        }
    }
}