using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class SimpleMeshCombiner : MonoBehaviour
{
    // Limite de vertices par mesh (Unity gère mieux < 65k vertices par chunk)
    private const int MAX_VERTICES = 65000;

    public void CombineMeshes()
    {
        // 1. Sauvegarde de la position actuelle
        Vector3 oldPos = transform.position;
        Quaternion oldRot = transform.rotation;
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;

        // 2. Récupérer tous les MeshFilters des enfants
        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>();
        Debug.Log($"Trouvé {meshFilters.Length} blocs à fusionner...");

        // 3. Grouper par Matériau (On ne peut pas fusionner deux matériaux différents)
        Dictionary<Material, List<CombineInstance>> combineData = new Dictionary<Material, List<CombineInstance>>();

        foreach (var mf in meshFilters)
        {
            if (mf.sharedMesh == null) continue;
            if (mf.gameObject == this.gameObject) continue; // Ne pas s'inclure soi-même

            MeshRenderer mr = mf.GetComponent<MeshRenderer>();
            if (mr == null) continue;

            Material mat = mr.sharedMaterial;
            if (mat == null) continue;

            if (!combineData.ContainsKey(mat))
            {
                combineData[mat] = new List<CombineInstance>();
            }

            CombineInstance ci = new CombineInstance();
            ci.mesh = mf.sharedMesh;
            ci.transform = mf.transform.localToWorldMatrix; // Position absolue
            combineData[mat].Add(ci);

            // On désactive l'objet original pour ne pas le rendre en double
            mf.gameObject.SetActive(false); 
        }

        // 4. Créer les nouveaux objets fusionnés
        GameObject container = new GameObject("OPTIMIZED_STRUCTURE");
        
        foreach (var kvp in combineData)
        {
            Material mat = kvp.Key;
            List<CombineInstance> instances = kvp.Value;
            
            // On découpe en chunks pour ne pas dépasser la limite de vertices
            int startIndex = 0;
            int chunkID = 0;

            while (startIndex < instances.Count)
            {
                int count = 0;
                int vertexCount = 0;

                // Calculer combien d'objets on peut mettre dans ce chunk
                for (int i = startIndex; i < instances.Count; i++)
                {
                    vertexCount += instances[i].mesh.vertexCount;
                    if (vertexCount > MAX_VERTICES) break;
                    count++;
                }

                // Création du Mesh
                Mesh bigMesh = new Mesh();
                bigMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // Autorise les gros meshs
                
                // Extraction du sous-groupe
                var chunkInstances = instances.GetRange(startIndex, count).ToArray();
                bigMesh.CombineMeshes(chunkInstances, true, true);
                
                // Création du GameObject
                GameObject chunk = new GameObject($"{mat.name}_Chunk_{chunkID}");
                chunk.transform.parent = container.transform;
                chunk.AddComponent<MeshFilter>().sharedMesh = bigMesh;
                chunk.AddComponent<MeshRenderer>().sharedMaterial = mat;
                chunk.isStatic = true; // Important pour le Light Baking

                // Ajout d'un Collider (Optionnel mais utile pour la physique)
                // chunk.AddComponent<MeshCollider>().sharedMesh = bigMesh; 

                startIndex += count;
                chunkID++;
            }
        }

        // 5. Restauration de la position
        transform.position = oldPos;
        transform.rotation = oldRot;
        
        Debug.Log("Fusion terminée ! Structure optimisée créée.");
    }
}

// Petit bouton dans l'éditeur pour lancer la fusion
#if UNITY_EDITOR
[CustomEditor(typeof(SimpleMeshCombiner))]
public class SimpleMeshCombinerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        SimpleMeshCombiner script = (SimpleMeshCombiner)target;
        if (GUILayout.Button("FUSIONNER TOUT (OPTIMISATION)", GUILayout.Height(40)))
        {
            script.CombineMeshes();
        }
    }
}
#endif