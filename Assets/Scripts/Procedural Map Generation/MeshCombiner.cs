using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class MeshCombiner : MonoBehaviour
{
    private const float CHUNK_SIZE = 32.0f; 

    // VERSION GLOBALE (Pour tout le générateur)
    public void CombineForBaking()
    {
        CombineTarget(this.transform, "BAKED_STRUCTURE");
    }

    // VERSION CIBLÉE (Appelable par l'île)
    public void CombineTarget(Transform targetRoot, string bakeName)
    {
        #if UNITY_EDITOR 
        
        // 1. Nettoyage de l'ancien bake s'il existe DANS la cible
        Transform existing = targetRoot.Find(bakeName);
        if (existing != null) DestroyImmediate(existing.gameObject);

        // Sauvegarde Position
        Vector3 oldPos = targetRoot.position;
        Quaternion oldRot = targetRoot.rotation;
        targetRoot.position = Vector3.zero;
        targetRoot.rotation = Quaternion.identity;

        // 2. Scan des MeshFilters
        MeshFilter[] sourceMeshFilters = targetRoot.GetComponentsInChildren<MeshFilter>();
        var spatialMap = new Dictionary<Vector3Int, Dictionary<Material, List<CombineInstance>>>();
        
        // Liste précise des objets (blocs) à détruire après fusion
        List<GameObject> objectsToDestroy = new List<GameObject>();

        Debug.Log($"Scan de {sourceMeshFilters.Length} éléments dans {targetRoot.name}...");
        int ignoredCrystals = 0;

        foreach (var mf in sourceMeshFilters)
        {
            if (mf.sharedMesh == null || mf.gameObject == targetRoot.gameObject) continue;
            
            // --- PROTECTION CRISTAUX & LUMIERES ---
            // Si c'est un cristal ou une lampe, on IGNORE tout (pas de merge, pas de destruction)
            if (mf.gameObject.name.Contains("CRYSTAL") || mf.GetComponentInChildren<Light>() != null) {
                ignoredCrystals++;
                continue; 
            }
            
            MeshRenderer mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || mr.sharedMaterial == null) continue;
            if (!mf.gameObject.activeInHierarchy) continue;

            // Si on est ici, c'est un BLOC valide à fusionner
            // On l'ajoute à la liste des condamnés (on le supprimera à la fin)
            objectsToDestroy.Add(mf.gameObject);

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

        // 3. Création du conteneur BAKED
        GameObject root = new GameObject(bakeName);
        root.transform.position = oldPos; // On le place virtuellement pour l'instant
        root.transform.parent = targetRoot; // On le range dans la cible
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;

        // 4. Génération des Meshes
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
                while (ptr < instances.Count)
                {
                    int count = Mathf.Min(1500, instances.Count - ptr);
                    var batch = instances.GetRange(ptr, count).ToArray();

                    Mesh bigMesh = new Mesh();
                    bigMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    bigMesh.CombineMeshes(batch, true, true);

                    UnityEditor.UnwrapParam param;
                    UnityEditor.UnwrapParam.SetDefaults(out param);
                    param.hardAngle = 88.0f; 
                    param.packMargin = 0.02f; 
                    UnityEditor.Unwrapping.GenerateSecondaryUVSet(bigMesh, param);
                    
                    GameObject meshGO = new GameObject($"{mat.name}_Part");
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

        // 5. SUPPRESSION CHIRURGICALE
        // On ne supprime QUE les blocs qui ont été fusionnés.
        // On ne touche pas aux parents (ISLAND_ROOT) ni aux cristaux ignorés.
        foreach(var obj in objectsToDestroy)
        {
            if (obj != null) DestroyImmediate(obj);
        }

        // Reset positions
        targetRoot.position = oldPos;
        targetRoot.rotation = oldRot;
        
        Debug.Log($"Baking sur '{targetRoot.name}' terminé. {objectsToDestroy.Count} blocs originaux supprimés. Cristaux conservés.");
        #endif
    }
}