using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class SpatialMeshCombiner : MonoBehaviour
{
    // Taille d'un "Chunk" spatial (30m x 30m x 30m)
    // Plus c'est petit, meilleur est l'occlusion culling, mais plus il y a de draw calls.
    // 30 est un bon équilibre.
    private const float CHUNK_SIZE = 32.0f; 
    private const int MAX_VERTICES = 65000;

    public void CombineMeshesSpatially()
    {
        Vector3 oldPos = transform.position;
        Quaternion oldRot = transform.rotation;
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;

        // 1. Récupération
        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>();
        Debug.Log($"Traitement de {meshFilters.Length} objets...");

        // 2. Dictionnaire Spatial : Clé = Position du Chunk (x,y,z), Valeur = Liste d'objets
        // On sous-divise aussi par Material
        Dictionary<Vector3Int, Dictionary<Material, List<CombineInstance>>> spatialMap = 
            new Dictionary<Vector3Int, Dictionary<Material, List<CombineInstance>>>();

        foreach (var mf in meshFilters)
        {
            if (mf.sharedMesh == null || mf.gameObject == this.gameObject) continue;
            MeshRenderer mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || mr.sharedMaterial == null) continue;

            // Calcul de la coordonnée du chunk
            Vector3 pos = mf.transform.position;
            Vector3Int chunkCoord = new Vector3Int(
                Mathf.FloorToInt(pos.x / CHUNK_SIZE),
                Mathf.FloorToInt(pos.y / CHUNK_SIZE),
                Mathf.FloorToInt(pos.z / CHUNK_SIZE)
            );

            if (!spatialMap.ContainsKey(chunkCoord))
                spatialMap[chunkCoord] = new Dictionary<Material, List<CombineInstance>>();

            if (!spatialMap[chunkCoord].ContainsKey(mr.sharedMaterial))
                spatialMap[chunkCoord][mr.sharedMaterial] = new List<CombineInstance>();

            CombineInstance ci = new CombineInstance();
            ci.mesh = mf.sharedMesh;
            ci.transform = mf.transform.localToWorldMatrix;
            spatialMap[chunkCoord][mr.sharedMaterial].Add(ci);

            mf.gameObject.SetActive(false);
        }

        // 3. Génération des Chunks
        GameObject rootContainer = new GameObject("OPTIMIZED_SPATIAL_STRUCTURE");
        rootContainer.transform.position = oldPos; // Pour garder le pivot cohérent si besoin

        int chunkCount = 0;

        foreach (var chunkEntry in spatialMap)
        {
            Vector3Int coord = chunkEntry.Key;
            Dictionary<Material, List<CombineInstance>> matGroups = chunkEntry.Value;

            // Création d'un parent pour ce secteur spatial (Optionnel, pour organisation)
            GameObject sectorObj = new GameObject($"Sector_{coord.x}_{coord.y}_{coord.z}");
            sectorObj.transform.parent = rootContainer.transform;
            // On place le pivot au centre du chunk pour aider Unity
            sectorObj.transform.position = new Vector3(coord.x * CHUNK_SIZE, coord.y * CHUNK_SIZE, coord.z * CHUNK_SIZE) + (Vector3.one * CHUNK_SIZE / 2);

            foreach (var matEntry in matGroups)
            {
                Material mat = matEntry.Key;
                List<CombineInstance> instances = matEntry.Value;

                // Gestion limite vertices
                int startIndex = 0;
                int subChunkID = 0;

                while (startIndex < instances.Count)
                {
                    int count = 0;
                    int vertexCount = 0;
                    for (int i = startIndex; i < instances.Count; i++) {
                        vertexCount += instances[i].mesh.vertexCount;
                        if (vertexCount > MAX_VERTICES) break;
                        count++;
                    }

                    Mesh bigMesh = new Mesh();
                    bigMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    
                    // Correction position relative : Les meshs ont été capturés en WorldPos via localToWorldMatrix
                    // Pour les mettre dans le rootContainer, on n'a pas besoin de correction si le root est à 0,0,0 pendant la fusion
                    // Mais comme on a créé un sectorObj déplacé, il faudrait compenser.
                    // SIMPLIFICATION : On attache tout au RootContainer pour éviter les prises de tête de matrices inverses.
                    // sectorObj sert juste de dossier.
                    
                    var chunkInstances = instances.GetRange(startIndex, count).ToArray();
                    bigMesh.CombineMeshes(chunkInstances, true, true);
                    
                    GameObject meshObj = new GameObject($"{mat.name}_Part_{subChunkID}");
                    meshObj.transform.parent = sectorObj.transform;
                    meshObj.transform.position = Vector3.zero; // Reset position car les vertices sont déjà en World Space
                    meshObj.transform.rotation = Quaternion.identity;
                    
                    meshObj.AddComponent<MeshFilter>().sharedMesh = bigMesh;
                    meshObj.AddComponent<MeshRenderer>().sharedMaterial = mat;
                    meshObj.isStatic = true;

                    startIndex += count;
                    subChunkID++;
                    chunkCount++;
                }
            }
        }

        transform.position = oldPos;
        transform.rotation = oldRot;
        Debug.Log($"Fusion terminée : {chunkCount} meshs générés.");
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(SpatialMeshCombiner))]
public class SpatialMeshCombinerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        SpatialMeshCombiner script = (SpatialMeshCombiner)target;
        if (GUILayout.Button("FUSION SPATIALE (Smart)", GUILayout.Height(40)))
        {
            script.CombineMeshesSpatially();
        }
    }
}
#endif