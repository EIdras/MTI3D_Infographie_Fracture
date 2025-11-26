using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(EscherVoidGenerator))]
public class EscherEditorButton : Editor
{
    public override void OnInspectorGUI()
    {
        // Affiche l'interface par défaut (les variables)
        DrawDefaultInspector();

        EscherVoidGenerator script = (EscherVoidGenerator)target;

        GUILayout.Space(20);

        if (GUILayout.Button("GÉNÉRER STRUCTURE (Edit Mode)", GUILayout.Height(40)))
        {
            script.GenerateStructure();
        }

        GUILayout.Space(10);

        if (GUILayout.Button("EFFACER TOUT", GUILayout.Height(30)))
        {
            script.ClearStructure();
        }
        
        if (GUILayout.Button("STOP / FINIR", GUILayout.Height(30)))
        {
            script.StopGeneration();
        }
    }
}