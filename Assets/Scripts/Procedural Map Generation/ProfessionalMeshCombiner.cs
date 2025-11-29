using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// Ce script peut être ajouté sur un GameObject car il n'est pas dans le dossier Editor
public class ProfessionalMeshCombiner : MonoBehaviour
{
    private const float CHUNK_SIZE = 32.0f; 

    public void CombineForBaking()
    {
        #if UNITY_EDITOR 
        // Le code de baking ne s'exécute que dans l'éditeur, pas dans le jeu final
        
        // 1. Nettoyage
        Transform existing = transform.Find("BAKED_STRUCTURE");
        if (existing != null) DestroyImmediate(existing.gameObject);

        Vector3 oldPos = transform.position;
        Quaternion oldRot = transform.rotation;
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;

        MeshFilter[] sourceMeshFilters = GetComponentsInChildren<MeshFilter>();
        var spatialMap = new Dictionary<Vector3Int, Dictionary<Material, List<CombineInstance>>>();

        // 2. Tri Spatial
        foreach (var mf in sourceMeshFilters)
        {
            if (mf.sharedMesh == null || mf.gameObject == this.gameObject) continue;
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

        GameObject root = new GameObject("BAKED_STRUCTURE");
        root.transform.position = oldPos;

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

                    // --- GENERATION DES UVs (LUMIERE) ---
                    // Cette fonction n'existe que dans l'éditeur Unity
                    UnityEditor.UnwrapParam param;
                    UnityEditor.UnwrapParam.SetDefaults(out param);
                    param.hardAngle = 88.0f; 
                    param.packMargin = 0.02f; 
                    UnityEditor.Unwrapping.GenerateSecondaryUVSet(bigMesh, param);
                    // ------------------------------------

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

        // 4. Désactivation des originaux
        foreach (Transform child in transform) 
        {
            if (child != root.transform) child.gameObject.SetActive(false);
        }

        transform.position = oldPos;
        transform.rotation = oldRot;
        Debug.Log("Optimisation terminée avec UVs générés.");
        #endif
    }
}