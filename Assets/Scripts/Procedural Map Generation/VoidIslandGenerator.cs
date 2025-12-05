using UnityEngine;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class VoidIslandGenerator : MonoBehaviour
{
    [Header("Lien Principal")]
    public EscherVoidGenerator mainGenerator;

    [Header("Forme de l'Île")]
    [Tooltip("Doit être un peu plus petit que le Rayon du Vide du générateur principal.")]
    public float islandRadius = 18f; 
    public int islandDepth = 15; // Profondeur max vers le bas
    
    [Header("Bruit & Organique")]
    public float noiseScale = 0.15f; 
    public float noiseThreshold = 0.3f; 
    public float stalactiteFactor = 1.5f; 

    [Header("Contrôle")]
    [Tooltip("Liste manuelle optionnelle pour forcer les blocs du dessus.")]
    public List<Module> specificTopModules;

    // --- INTERNE ---
    // On garde une référence vers un conteneur spécifique pour ne pas toucher au reste
    private Transform islandContainer;

    [ContextMenu("Générer Île")]
    public void GenerateIsland()
    {
        // 1. Initialisation du lien si manquant
        if (mainGenerator == null) {
            mainGenerator = GetComponent<EscherVoidGenerator>();
            // Si toujours null, on cherche dans le parent ou les voisins
            if(mainGenerator == null) mainGenerator = FindObjectOfType<EscherVoidGenerator>();
        }

        if(mainGenerator == null) { Debug.LogError("Pas de EscherVoidGenerator trouvé !"); return; }

        // 2. Nettoyage CIBLÉ (uniquement l'île)
        ClearIslandOnly();
        CreateContainer();

        Debug.Log("Sculpture de l'île centrale (Additive)...");

        var allModules = mainGenerator.modulePrefabs;
        if (allModules == null || allModules.Count == 0) return;

        int radiusInBlocks = Mathf.FloorToInt(islandRadius / mainGenerator.gridSize);
        float gridSize = mainGenerator.gridSize;

        // Pré-calcul des blocs plats
        List<EscherVoidGenerator.WeightedModule> flatCandidates = new List<EscherVoidGenerator.WeightedModule>();
        if (specificTopModules != null && specificTopModules.Count > 0)
        {
            foreach(var m in specificTopModules) 
                flatCandidates.Add(new EscherVoidGenerator.WeightedModule { module = m, weight = 10 });
        }
        else 
        {
            flatCandidates = allModules.Where(m => IsFlatModule(m.module)).ToList();
        }
        if (flatCandidates.Count == 0) flatCandidates = allModules;

        // Bruit
        float seedX = Random.Range(0f, 100f);
        float seedZ = Random.Range(0f, 100f);
        int blockCount = 0;

        // Boucle de génération
        for (int x = -radiusInBlocks; x <= radiusInBlocks; x++)
        {
            for (int z = -radiusInBlocks; z <= radiusInBlocks; z++)
            {
                float distFromCenter = Mathf.Sqrt(x*x + z*z);
                if (distFromCenter > radiusInBlocks) continue;

                float noiseVal = Mathf.PerlinNoise(seedX + x * noiseScale, seedZ + z * noiseScale);
                float falloff = Mathf.Pow(1f - (distFromCenter / radiusInBlocks), 0.5f);
                float finalHeightCheck = noiseVal * falloff;

                if (finalHeightCheck < noiseThreshold) continue;

                int columnDepth = Mathf.FloorToInt((finalHeightCheck - noiseThreshold) * stalactiteFactor * islandDepth);
                columnDepth = Mathf.Clamp(columnDepth, 1, islandDepth);

                for (int y = 0; y > -columnDepth; y--)
                {
                    Vector3Int gridPos = new Vector3Int(x, y, z);
                    Vector3 worldPos = new Vector3(x * gridSize, y * gridSize, z * gridSize);

                    EscherVoidGenerator.WeightedModule selectedWeighted;

                    if (y == 0) // Surface
                    {
                        selectedWeighted = PickWeighted(flatCandidates);
                        Quaternion rot = Quaternion.Euler(0, Random.Range(0, 4) * 90f, 0); 
                        SpawnBlockInContainer(worldPos, rot, selectedWeighted.module, $"Island_Top_{x}_{z}");
                    }
                    else // Corps
                    {
                        selectedWeighted = PickWeighted(allModules);
                        Quaternion rot = Quaternion.Euler(Random.Range(0, 4) * 90f, Random.Range(0, 4) * 90f, Random.Range(0, 4) * 90f);
                        SpawnBlockInContainer(worldPos, rot, selectedWeighted.module, $"Island_Core_{x}_{y}_{z}");
                    }
                    blockCount++;
                }
            }
        }
        Debug.Log($"Île ajoutée : {blockCount} blocs.");
    }

    // --- GESTION DU CONTENEUR (C'est la clé de l'additif) ---
    void CreateContainer()
    {
        if (islandContainer == null)
        {
            // On cherche si un enfant s'appelle déjà comme ça
            Transform existing = transform.Find("ISLAND_ROOT");
            if (existing != null)
            {
                islandContainer = existing;
            }
            else
            {
                GameObject go = new GameObject("ISLAND_ROOT");
                go.transform.parent = transform;
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                islandContainer = go.transform;
            }
        }
    }

    [ContextMenu("Effacer Île Uniquement")]
    public void ClearIslandOnly()
    {
        // On cherche le conteneur par nom pour être sûr
        Transform container = transform.Find("ISLAND_ROOT");
        if (container != null)
        {
            DestroyImmediate(container.gameObject);
        }
        islandContainer = null;
    }

    // --- LOGIQUE METIER ---

    void SpawnBlockInContainer(Vector3 localPos, Quaternion rot, Module prefab, string name)
    {
        if(prefab == null || islandContainer == null) return;
        
        Module instance = (Module)PrefabUtility.InstantiatePrefab(prefab, islandContainer); // Parenté au container !
        instance.transform.localPosition = localPos;
        instance.transform.localRotation = rot;
        instance.name = name;
        
        // Optionnel : Si tu veux que les cristaux ne spawnent pas DANS l'île plus tard,
        // il faudrait idéalement dire au MainGenerator que cette case est occupée.
        // Mais comme l'île est au centre (zone vide exclue du main generator), ce n'est pas critique.
    }

    bool IsFlatModule(Module m)
    {
        if (m == null) return false;
        string name = m.name.ToLower();
        if (name.Contains("slab") || name.Contains("floor") || name.Contains("dalle")) return true;
        var sockets = m.GetComponentsInChildren<SocketTag>(true);
        foreach(var s in sockets) if (s.sub.Trim().ToLower() == "slab") return true;
        return false;
    }

    EscherVoidGenerator.WeightedModule PickWeighted(List<EscherVoidGenerator.WeightedModule> list)
    {
        if (list.Count == 0) return new EscherVoidGenerator.WeightedModule();
        float totalWeight = 0;
        foreach(var item in list) totalWeight += item.weight;
        float rnd = Random.value * totalWeight;
        foreach(var item in list) {
            if (rnd < item.weight) return item;
            rnd -= item.weight;
        }
        return list[0];
    }
    
    
#if UNITY_EDITOR
    [CustomEditor(typeof(VoidIslandGenerator))]
    public class CrystalGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            VoidIslandGenerator script = (VoidIslandGenerator)target;
            EscherVoidGenerator gen = script.GetComponent<EscherVoidGenerator>();

            if (GUILayout.Button("GÉNÉRER ÎLE"))
            {
                if (gen != null) script.GenerateIsland();
            }
        }
    }
#endif
}