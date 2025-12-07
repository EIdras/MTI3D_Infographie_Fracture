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
    public float islandRadius = 18f; 
    public int islandDepth = 15;
    
    [Header("Bruit & Organique")]
    public float noiseScale = 0.15f; 
    public float noiseThreshold = 0.3f; 
    public float stalactiteFactor = 1.5f; 

    [Header("Cristaux d'Île")]
    public GameObject crystalPrefab;
    [Range(0f, 1f)] public float crystalDensity = 0.2f; 
    [Range(0f, 5f)] public float crystalOffset = 0.0f;

    [Header("Contrôle")]
    public List<Module> specificTopModules;

    // --- INTERNE ---
    private Transform islandContainer;
    private HashSet<Vector3Int> islandOccupiedCells = new HashSet<Vector3Int>();

    [ContextMenu("Générer Île")]
    public void GenerateIsland()
    {
        if (mainGenerator == null) {
            mainGenerator = GetComponent<EscherVoidGenerator>();
            if(mainGenerator == null) mainGenerator = FindObjectOfType<EscherVoidGenerator>();
        }
        if(mainGenerator == null) { Debug.LogError("Pas de EscherVoidGenerator trouvé !"); return; }

        ClearIslandOnly();
        CreateContainer();
        islandOccupiedCells.Clear(); 

        Debug.Log("Sculpture de l'île centrale...");

        var allModules = mainGenerator.modulePrefabs;
        if (allModules == null || allModules.Count == 0) return;

        int radiusInBlocks = Mathf.FloorToInt(islandRadius / mainGenerator.gridSize);
        float gridSize = mainGenerator.gridSize;

        List<EscherVoidGenerator.WeightedModule> flatCandidates = new List<EscherVoidGenerator.WeightedModule>();
        if (specificTopModules != null && specificTopModules.Count > 0) {
            foreach(var m in specificTopModules) flatCandidates.Add(new EscherVoidGenerator.WeightedModule { module = m, weight = 10 });
        } else {
            flatCandidates = allModules.Where(m => IsFlatModule(m.module)).ToList();
        }
        if (flatCandidates.Count == 0) flatCandidates = allModules;

        float seedX = Random.Range(0f, 100f);
        float seedZ = Random.Range(0f, 100f);
        int blockCount = 0;

        for (int x = -radiusInBlocks; x <= radiusInBlocks; x++)
        {
            for (int z = -radiusInBlocks; z <= radiusInBlocks; z++)
            {
                float dist = Mathf.Sqrt(x*x + z*z);
                if (dist > radiusInBlocks) continue;

                float noiseVal = Mathf.PerlinNoise(seedX + x * noiseScale, seedZ + z * noiseScale);
                float falloff = Mathf.Pow(1f - (dist / radiusInBlocks), 0.5f);
                float hCheck = noiseVal * falloff; 

                if (hCheck < noiseThreshold) continue; 

                int depth = Mathf.FloorToInt((hCheck - noiseThreshold) * stalactiteFactor * islandDepth);
                depth = Mathf.Clamp(depth, 1, islandDepth);

                for (int y = 0; y > -depth; y--)
                {
                    Vector3Int gridPos = new Vector3Int(x, y, z);

                    if (islandOccupiedCells.Contains(gridPos)) continue;

                    Vector3 worldPos = new Vector3(x * gridSize, y * gridSize, z * gridSize);
                    EscherVoidGenerator.WeightedModule selected;
                    Quaternion rot;

                    if (y == 0) {
                        selected = PickWeighted(flatCandidates);
                        rot = Quaternion.Euler(0, Random.Range(0, 4) * 90f, 0); 
                    } else {
                        selected = PickWeighted(allModules);
                        rot = Quaternion.Euler(Random.Range(0, 4)*90f, Random.Range(0, 4)*90f, Random.Range(0, 4)*90f);
                    }

                    if (CanFitModule(gridPos, rot, selected.module))
                    {
                        SpawnBlockInContainer(worldPos, rot, selected.module, $"Island_{x}_{y}_{z}");
                        MarkOccupied(gridPos, rot, selected.module);
                        blockCount++;
                    }
                    else
                    {
                        var fallback = flatCandidates.Count > 0 ? flatCandidates[0].module : selected.module;
                        SpawnBlockInContainer(worldPos, Quaternion.identity, fallback, $"Island_Fallback_{x}_{y}_{z}");
                        MarkOccupied(gridPos, Quaternion.identity, fallback);
                        blockCount++;
                    }
                }
            }
        }

        GenerateContourCrystals(gridSize);
        Debug.Log($"Île ajoutée : {blockCount} blocs.");
    }

    // --- FONCTION DE BAKE SPECIFIQUE A L'ILE ---
    public void BakeIslandOnly()
    {
        if (islandContainer == null) islandContainer = transform.Find("ISLAND_ROOT");
        if (islandContainer == null) { Debug.LogError("Pas d'île à baker !"); return; }

        // On cherche le MeshCombiner sur le parent ou on en ajoute un temporaire
        MeshCombiner combiner = GetComponent<MeshCombiner>();
        if (combiner == null) combiner = GetComponentInParent<MeshCombiner>();
        if (combiner == null) { Debug.LogError("Pas de script MeshCombiner trouvé sur cet objet ou son parent."); return; }

        Debug.Log("Lancement du Bake pour l'Île seulement...");
        // On cible uniquement le conteneur de l'île
        combiner.CombineTarget(islandContainer, "BAKED_ISLAND");
    }

    void GenerateContourCrystals(float gridSize)
    {
        if (crystalPrefab == null) return;
        int count = 0;
        
        Vector3Int[] directions = new Vector3Int[] {
            Vector3Int.up, Vector3Int.down,
            Vector3Int.left, Vector3Int.right,
            Vector3Int.forward, Vector3Int.back
        };

        foreach(Vector3Int blockPos in islandOccupiedCells)
        {
            foreach(Vector3Int dir in directions)
            {
                Vector3Int neighborPos = blockPos + dir;
                if (!islandOccupiedCells.Contains(neighborPos))
                {
                    if (Random.value > crystalDensity) continue;

                    Vector3 worldBlockCenter = new Vector3(blockPos.x * gridSize, blockPos.y * gridSize, blockPos.z * gridSize);
                    Vector3 spawnPos = worldBlockCenter + (new Vector3(dir.x, dir.y, dir.z) * (gridSize * 0.5f + crystalOffset));

                    GameObject crystal = (GameObject)PrefabUtility.InstantiatePrefab(crystalPrefab, islandContainer);
                    crystal.transform.localPosition = spawnPos;
                    
                    Quaternion lookRot = Quaternion.LookRotation(new Vector3(dir.x, dir.y, dir.z));
                    crystal.transform.localRotation = lookRot * Quaternion.Euler(Random.Range(-20, 20), Random.Range(-20, 20), Random.Range(-180, 180));
                    
                    float scale = Random.Range(50f, 120f);
                    crystal.transform.localScale = Vector3.one * scale;
                    crystal.name = $"CRYSTAL_Edge_{count}";
                    count++;
                }
            }
        }
    }
    
    // --- UTILITAIRES DE GRILLE ---
    bool CanFitModule(Vector3Int rootPos, Quaternion rot, Module mod)
    {
        if (mod == null) return true;
        foreach(var offset in mod.occupiedOffsets)
        {
            Vector3 rotatedOffsetFloat = rot * (Vector3)offset;
            Vector3Int rotatedOffset = new Vector3Int(Mathf.RoundToInt(rotatedOffsetFloat.x), Mathf.RoundToInt(rotatedOffsetFloat.y), Mathf.RoundToInt(rotatedOffsetFloat.z));
            if (islandOccupiedCells.Contains(rootPos + rotatedOffset)) return false;
        }
        return true;
    }

    void MarkOccupied(Vector3Int rootPos, Quaternion rot, Module mod)
    {
        if (mod == null) { islandOccupiedCells.Add(rootPos); return; }
        foreach(var offset in mod.occupiedOffsets)
        {
            Vector3 rotatedOffsetFloat = rot * (Vector3)offset;
            Vector3Int rotatedOffset = new Vector3Int(Mathf.RoundToInt(rotatedOffsetFloat.x), Mathf.RoundToInt(rotatedOffsetFloat.y), Mathf.RoundToInt(rotatedOffsetFloat.z));
            islandOccupiedCells.Add(rootPos + rotatedOffset);
        }
    }

    void CreateContainer()
    {
        if (islandContainer == null) {
            Transform existing = transform.Find("ISLAND_ROOT");
            if (existing != null) islandContainer = existing;
            else {
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
        Transform container = transform.Find("ISLAND_ROOT");
        if (container != null) DestroyImmediate(container.gameObject);
        islandContainer = null;
    }

    void SpawnBlockInContainer(Vector3 localPos, Quaternion rot, Module prefab, string name)
    {
        if(prefab == null || islandContainer == null) return;
        Module instance = (Module)PrefabUtility.InstantiatePrefab(prefab, islandContainer); 
        instance.transform.localPosition = localPos;
        instance.transform.localRotation = rot;
        instance.name = name;
    }

    bool IsFlatModule(Module m) {
        if (m == null) return false;
        string name = m.name.ToLower();
        return name.Contains("slab") || name.Contains("floor") || name.Contains("block");
    }

    EscherVoidGenerator.WeightedModule PickWeighted(List<EscherVoidGenerator.WeightedModule> list) {
        if (list.Count == 0) return new EscherVoidGenerator.WeightedModule();
        float totalWeight = list.Sum(x => x.weight);
        float rnd = Random.value * totalWeight;
        foreach(var item in list) {
            if (rnd < item.weight) return item;
            rnd -= item.weight;
        }
        return list[0];
    }
    
#if UNITY_EDITOR
    [CustomEditor(typeof(VoidIslandGenerator))]
    public class VoidIslandGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            VoidIslandGenerator script = (VoidIslandGenerator)target;
            
            GUILayout.Space(10);
            if (GUILayout.Button("1. GÉNÉRER ÎLE", GUILayout.Height(30)))
            {
                script.GenerateIsland();
            }
            if (GUILayout.Button("2. BAKER ÎLE UNIQUEMENT", GUILayout.Height(30)))
            {
                script.BakeIslandOnly();
            }
            GUILayout.Space(10);
            if (GUILayout.Button("EFFACER ÎLE"))
            {
                script.ClearIslandOnly();
            }
        }
    }
#endif
}