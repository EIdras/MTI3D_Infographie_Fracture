using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(ProfessionalMeshCombiner))]
public class ProfessionalMeshCombinerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        
        ProfessionalMeshCombiner script = (ProfessionalMeshCombiner)target;
        
        GUILayout.Space(10);
        
        // Ce bouton appelle la fonction du script principal
        if (GUILayout.Button("COMBINER & GÉNÉRER UVs (Lourd)", GUILayout.Height(50)))
        {
            script.CombineForBaking();
        }
    }
}