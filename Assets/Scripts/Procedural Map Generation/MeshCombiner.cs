using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class MeshCombiner : MonoBehaviour
{
    [Header("Réglages de Fusion")]
    [Tooltip("Taille de la zone de regroupement.")]
    public float chunkSize = 32.0f; 

    [Tooltip("Nombre maximum d'objets combinés dans un seul mesh.")]
    [Range(500, 5000)] public int maxInstancesPerMesh = 1500;

    [Header("Test Performance")]
    [Tooltip("Divise artificiellement la densité des meshs par 2.")]
    public bool splitMeshesInHalf = false;

    // VERSION GLOBALE
    public void CombineForBaking()
    {
        CombineTarget(this.transform, "BAKED_STRUCTURE");
    }

    // VERSION CIBLÉE
    public void CombineTarget(Transform targetRoot, string bakeName)
    {
        #if UNITY_EDITOR 
        
        // 1. Nettoyage
        Transform existing = targetRoot.Find(bakeName);
        if (existing != null) DestroyImmediate(existing.gameObject);

        // Sauvegarde transform
        Vector3 oldPos = targetRoot.position;
        Quaternion oldRot = targetRoot.rotation;
        targetRoot.position = Vector3.zero;
        targetRoot.rotation = Quaternion.identity;

        // 2. Scan
        MeshFilter[] sourceMeshFilters = targetRoot.GetComponentsInChildren<MeshFilter>();
        var spatialMap = new Dictionary<Vector3Int, Dictionary<Material, List<CombineInstance>>>();
        List<GameObject> objectsToDestroy = new List<GameObject>();

        Debug.Log($"Scan de {sourceMeshFilters.Length} éléments dans {targetRoot.name}...");
        int ignoredCrystals = 0;

        foreach (var mf in sourceMeshFilters)
        {
            if (mf.sharedMesh == null || mf.gameObject == targetRoot.gameObject) continue;
            
            // Protection Cristaux
            if (mf.gameObject.name.Contains("CRYSTAL") || mf.GetComponentInChildren<Light>() != null) {
                ignoredCrystals++;
                continue; 
            }
            
            MeshRenderer mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || mr.sharedMaterial == null) continue;
            if (!mf.gameObject.activeInHierarchy) continue;

            objectsToDestroy.Add(mf.gameObject);

            Vector3 pos = mf.transform.position;
            
            // --- UTILISATION DU CHUNK SIZE VARIABLE ---
            Vector3Int coord = new Vector3Int(
                Mathf.FloorToInt(pos.x / chunkSize),
                Mathf.FloorToInt(pos.y / chunkSize),
                Mathf.FloorToInt(pos.z / chunkSize)
            );

            if (!spatialMap.ContainsKey(coord)) spatialMap[coord] = new Dictionary<Material, List<CombineInstance>>();
            if (!spatialMap[coord].ContainsKey(mr.sharedMaterial)) spatialMap[coord][mr.sharedMaterial] = new List<CombineInstance>();

            CombineInstance ci = new CombineInstance();
            ci.mesh = mf.sharedMesh;
            ci.transform = mf.transform.localToWorldMatrix;
            spatialMap[coord][mr.sharedMaterial].Add(ci);
        }

        // 3. Création Racine
        GameObject root = new GameObject(bakeName);
        root.transform.position = oldPos;
        root.transform.parent = targetRoot; 
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;

        // --- CALCUL DE LA LIMITE INTELLIGENTE ---
        // Si l'option est cochée, on divise la limite par 2
        int finalLimit = splitMeshesInHalf ? (maxInstancesPerMesh / 2) : maxInstancesPerMesh;
        Debug.Log($"Génération avec ChunkSize: {chunkSize} et Limite Batch: {finalLimit}");

        // 4. Génération
        foreach (var chunk in spatialMap)
        {
            GameObject chunkObj = new GameObject($"Chunk_{chunk.Key.x}_{chunk.Key.y}_{chunk.Key.z}");
            chunkObj.transform.parent = root.transform;
            chunkObj.isStatic = true; 

            foreach (var matEntry in chunk.Value)
            {
                Material mat = matEntry.Key;
                List<CombineInstance> instances = matEntry.Value;
                int ptr = 0;
                
                // On boucle tant qu'il reste des instances
                while (ptr < instances.Count)
                {
                    // On prend soit la limite, soit le reste
                    int count = Mathf.Min(finalLimit, instances.Count - ptr);
                    var batch = instances.GetRange(ptr, count).ToArray();

                    Mesh bigMesh = new Mesh();
                    // On passe en 32 bits pour supporter beaucoup de vertices si besoin
                    bigMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; 
                    bigMesh.CombineMeshes(batch, true, true);

                    UnityEditor.UnwrapParam param;
                    UnityEditor.UnwrapParam.SetDefaults(out param);
                    param.hardAngle = 88.0f; 
                    param.packMargin = 0.02f; 
                    UnityEditor.Unwrapping.GenerateSecondaryUVSet(bigMesh, param);
                    
                    GameObject meshGO = new GameObject($"{mat.name}_Part_{ptr/finalLimit}"); // Nommage incrémental
                    meshGO.transform.parent = chunkObj.transform;
                    meshGO.transform.localPosition = Vector3.zero; 
                    meshGO.transform.localRotation = Quaternion.identity;
                    meshGO.AddComponent<MeshFilter>().sharedMesh = bigMesh;
                    meshGO.AddComponent<MeshRenderer>().sharedMaterial = mat;
                    meshGO.isStatic = true; 

                    ptr += count;
                }
            }
        }

        // 5. Suppression
        foreach(var obj in objectsToDestroy)
        {
            if (obj != null) DestroyImmediate(obj);
        }

        targetRoot.position = oldPos;
        targetRoot.rotation = oldRot;
        
        Debug.Log($"Baking terminé. Mode Split: {splitMeshesInHalf}");
        #else
        Debug.LogWarning("Le MeshCombiner ne peut pas être exécuté dans un Build (Runtime). Utilisez-le dans l'éditeur Unity uniquement.");
        #endif
    }
}