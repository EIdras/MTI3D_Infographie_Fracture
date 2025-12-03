using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class MeshCombiner : MonoBehaviour
{
    private const float CHUNK_SIZE = 32.0f; 

    public void CombineForBaking()
    {
        #if UNITY_EDITOR 
        
        // 1. Nettoyage de l'ancienne structure baked si elle existe
        Transform existing = transform.Find("BAKED_STRUCTURE");
        if (existing != null) DestroyImmediate(existing.gameObject);

        // Reset temporaire de la position/rotation du parent pour le calcul
        Vector3 oldPos = transform.position;
        Quaternion oldRot = transform.rotation;
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;

        MeshFilter[] sourceMeshFilters = GetComponentsInChildren<MeshFilter>();
        var spatialMap = new Dictionary<Vector3Int, Dictionary<Material, List<CombineInstance>>>();

        Debug.Log($"Scan de {sourceMeshFilters.Length} éléments...");

        // 2. Tri Spatial et FILTRAGE
        int ignoredCrystals = 0;

        foreach (var mf in sourceMeshFilters)
        {
            if (mf.sharedMesh == null || mf.gameObject == this.gameObject) continue;
            
            // --- PROTECTION DES CRISTAUX ---
            // Si l'objet s'appelle "CRYSTAL_..." OU s'il contient une Lumière, on l'ignore pour le merge.
            if (mf.gameObject.name.Contains("CRYSTAL") || mf.GetComponentInChildren<Light>() != null) {
                ignoredCrystals++;
                continue; 
            }
            // -------------------------------

            MeshRenderer mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || mr.sharedMaterial == null) continue;
            if (!mf.gameObject.activeInHierarchy) continue;

            Vector3 pos = mf.transform.position;
            Vector3Int coord = new Vector3Int(
                Mathf.FloorToInt(pos.x / CHUNK_SIZE),
                Mathf.FloorToInt(pos.y / CHUNK_SIZE),
                Mathf.FloorToInt(pos.z / CHUNK_SIZE)
            );

            if (!spatialMap.ContainsKey(coord)) spatialMap[coord] = new Dictionary<Material, List<CombineInstance>>();
            if (!spatialMap[coord].ContainsKey(mr.sharedMaterial)) spatialMap[coord][mr.sharedMaterial] = new List<CombineInstance>();

            CombineInstance ci = new CombineInstance();
            ci.mesh = mf.sharedMesh;
            ci.transform = mf.transform.localToWorldMatrix;
            spatialMap[coord][mr.sharedMaterial].Add(ci);
        }

        Debug.Log($"Structure triée. {ignoredCrystals} cristaux ignorés (préservés).");

        GameObject root = new GameObject("BAKED_STRUCTURE");
        root.transform.position = oldPos;
        // On remet root enfant du generateur pour garder l'ordre, mais attention à ne pas le détruire après
        root.transform.parent = this.transform; 
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;

        // 3. Fusion et UVs
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
                int limit = 1500; 

                while (ptr < instances.Count)
                {
                    int count = Mathf.Min(limit, instances.Count - ptr);
                    var batch = instances.GetRange(ptr, count).ToArray();

                    Mesh bigMesh = new Mesh();
                    bigMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    bigMesh.CombineMeshes(batch, true, true);

                    // --- GENERATION DES UVs LIGHTMAP ---
                    UnityEditor.UnwrapParam param;
                    UnityEditor.UnwrapParam.SetDefaults(out param);
                    param.hardAngle = 88.0f; 
                    param.packMargin = 0.02f; 
                    UnityEditor.Unwrapping.GenerateSecondaryUVSet(bigMesh, param);
                    
                    GameObject meshGO = new GameObject($"{mat.name}_Part");
                    meshGO.transform.parent = chunkObj.transform;
                    meshGO.transform.position = Vector3.zero; 
                    meshGO.transform.rotation = Quaternion.identity;

                    meshGO.AddComponent<MeshFilter>().sharedMesh = bigMesh;
                    meshGO.AddComponent<MeshRenderer>().sharedMaterial = mat;
                    meshGO.isStatic = true; 

                    ptr += count;
                }
            }
        }

        // 4. SUPPRESSION DES ORIGINAUX (SAUF CRISTAUX)
        // On fait une liste séparée pour éviter les erreurs de modification de collection
        List<GameObject> childrenToDestroy = new List<GameObject>();

        foreach (Transform child in transform) 
        {
            // On ne touche pas au dossier BAKED qu'on vient de créer
            if (child == root.transform) continue;

            // --- PROTECTION DES CRISTAUX LORS DE LA DESTRUCTION ---
            bool isCrystal = child.name.Contains("CRYSTAL") || child.GetComponentInChildren<Light>() != null;
            
            if (!isCrystal) {
                childrenToDestroy.Add(child.gameObject);
            }
        }

        int deletedCount = childrenToDestroy.Count;
        foreach(var obj in childrenToDestroy) {
            DestroyImmediate(obj);
        }

        transform.position = oldPos;
        transform.rotation = oldRot;
        
        Debug.Log($"Optimisation terminée. {deletedCount} blocs structurels supprimés. Cristaux conservés.");
        #endif
    }
}