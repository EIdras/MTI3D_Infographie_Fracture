using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class EscherSandwichGenerator : MonoBehaviour
{
    [Header("Assets")]
    public List<Module> modulePrefabs;
    
    [Header("OPTIMISATION GPU (Vital pour le Socle)")]
    public Mesh cubeMesh;          // Glisse le mesh "Cube" ici
    public Material cubeMaterial;  // Glisse ton Material gris ici

    [Header("Dimensions du Monde")]
    public int mapSizeX = 80; // Tu peux augmenter maintenant !
    public int mapSizeZ = 80;
    public int ceilingHeight = 100; 

    [Header("1. Le Socle (Densité de base)")]
    public int bedrockThickness = 8; 
    [Range(0, 1)] public float bedrockDensity = 0.9f;

    [Header("2. L'Horizon")]
    public int horizonGapMin = 40; 
    public int horizonGapMax = 60;

    [Header("3. Les Filaments")]
    [Range(0, 1)] public float fillPercentage = 0.15f;
    public int maxCrawlersSimultanes = 300;
    public int totalBlocksLimit = 50000; // On peut viser très haut maintenant
    public int blocksPerFrame = 500; // Plus rapide

    [Header("Style Berserk")]
    [Range(0f, 1f)] public float stairContinuity = 0.98f; 
    [Range(0f, 1f)] public float branchingRate = 0.02f;   
    public int minStairLength = 10;
    public float gridSize = 2.0f;

    // --- MÉMOIRE ---
    private HashSet<Vector3Int> occupiedCells = new HashSet<Vector3Int>();
    private int currentBlockCount = 0;
    private int activeCrawlers = 0;

    // --- OPTIMISATION MATRICES ---
    // On stocke les positions du socle ici au lieu de faire des GameObjects
    private List<Matrix4x4> bedrockMatrices = new List<Matrix4x4>();
    private List<List<Matrix4x4>> bedrockBatches = new List<List<Matrix4x4>>(); // Pour contourner la limite de 1023

    struct SpawnPoint {
        public Vector3Int pos;
        public Vector3 dir;
        public string id;
    }

    void Start()
    {
        modulePrefabs.RemoveAll(x => x == null);
        if (modulePrefabs.Count == 0 || cubeMesh == null || cubeMaterial == null) 
        {
            Debug.LogError("ERREUR : Assignez les Prefabs, le Cube Mesh et le Cube Material dans l'inspecteur !");
            return;
        }
        
        // Cache les prefabs originaux
        foreach(var m in modulePrefabs) if(m.gameObject.activeInHierarchy) m.gameObject.SetActive(false); 

        StartCoroutine(GenerateSandwich());
    }

    void Update()
    {
        // RENDU MAGIQUE : On dessine le socle directement sur le GPU à chaque frame
        if (bedrockBatches.Count > 0)
        {
            foreach (var batch in bedrockBatches)
            {
                Graphics.DrawMeshInstanced(cubeMesh, 0, cubeMaterial, batch);
            }
        }
    }

    IEnumerator GenerateSandwich()
    {
        occupiedCells.Clear();
        bedrockMatrices.Clear();
        bedrockBatches.Clear();
        currentBlockCount = 0;
        activeCrawlers = 0;

        Debug.Log("--- 1. Calcul du Socle (En Matrices) ---");
        
        int halfX = mapSizeX / 2;
        int halfZ = mapSizeZ / 2;
        int processedCount = 0; // Pour gérer la coroutine

        for (int x = -halfX; x < halfX; x++)
        {
            for (int z = -halfZ; z < halfZ; z++)
            {
                float noise = Mathf.PerlinNoise((x + 1000) * 0.1f, (z + 1000) * 0.1f); 
                int localThickness = Mathf.RoundToInt(bedrockThickness * (0.5f + noise));

                // On ne garde que la croute visible (Creux)
                int startY = Mathf.Max(0, localThickness - 2); 

                // SOCLE SOL
                for (int y = startY; y < localThickness; y++)
                {
                    if (Random.value < bedrockDensity)
                    {
                        Vector3Int pos = new Vector3Int(x, y, z);
                        AddBedrockBlock(pos, Vector3.up);
                    }
                }

                // SOCLE PLAFOND
                for (int y = startY; y < localThickness; y++)
                {
                    if (Random.value < bedrockDensity)
                    {
                        Vector3Int pos = new Vector3Int(x, ceilingHeight - y, z);
                        AddBedrockBlock(pos, Vector3.down);
                    }
                }
                
                // Petit frein pour ne pas freezer l'éditeur pendant le CALCUL
                processedCount++;
                if (processedCount > 1000) { processedCount = 0; yield return null; }
            }
        }
        
        // On découpe la grosse liste en paquets de 1023 (limite Unity DrawMeshInstanced)
        CreateBatches();

        Debug.Log($"--- Socle calculé ({bedrockMatrices.Count} blocs virtuels). Lancement Filaments... ---");

        // --- 2. CRAWLERS (Objets réels) ---
        List<SpawnPoint> potentialSpawns = new List<SpawnPoint>();
        int step = 6; 

        for (int x = -halfX; x < halfX; x += step)
        {
            for (int z = -halfZ; z < halfZ; z += step)
            {
                float noise = Mathf.PerlinNoise((x + 1000) * 0.1f, (z + 1000) * 0.1f); 
                int localHeight = Mathf.RoundToInt(bedrockThickness * (0.5f + noise));
                
                int startY = localHeight + Random.Range(0, 2);
                potentialSpawns.Add(new SpawnPoint { pos = new Vector3Int(x, startY, z), dir = Vector3.up, id = $"Floor_{x}_{z}" });

                int startYCeiling = ceilingHeight - localHeight - Random.Range(0, 2);
                potentialSpawns.Add(new SpawnPoint { pos = new Vector3Int(x, startYCeiling, z), dir = Vector3.down, id = $"Ceiling_{x}_{z}" });
            }
        }

        potentialSpawns = potentialSpawns.OrderBy(a => Random.value).ToList();

        int targetSpawns = Mathf.FloorToInt(potentialSpawns.Count * fillPercentage);
        int spawnedCount = 0;

        // Récupération du prefab structure pour les crawlers
        Module structurePrefab = modulePrefabs.FirstOrDefault(m => m.type == ModuleType.Structure);

        foreach (var point in potentialSpawns)
        {
            if (spawnedCount >= targetSpawns) break;
            while (activeCrawlers >= maxCrawlersSimultanes) yield return null;

            if (!occupiedCells.Contains(point.pos))
            {
                SpawnBlockAndCrawl(point.pos, point.dir, structurePrefab, point.id, 0);
                spawnedCount++;
            }
            if (spawnedCount % 10 == 0) yield return null;
        }

        while (activeCrawlers > 0) yield return null;
        Debug.Log($"Terminé. Total Crawlers: {currentBlockCount}. Total Socle (GPU): {bedrockMatrices.Count}");
    }

    // --- FONCTIONS OPTIMISATION GPU ---

    void AddBedrockBlock(Vector3Int gridPos, Vector3 upAxis)
    {
        if (occupiedCells.Contains(gridPos)) return;
        
        // On marque la case comme occupée pour que les crawlers ne rentrent pas dedans
        occupiedCells.Add(gridPos);

        // Rotation aléatoire
        Quaternion rot = Quaternion.LookRotation(Vector3.forward, upAxis);
        rot *= Quaternion.Euler(0, Random.Range(0, 4) * 90, 0);

        // Au lieu d'Instantiate, on crée une Matrice mathématique
        Vector3 position = GridToWorld(gridPos);
        Vector3 scale = Vector3.one * gridSize;
        Matrix4x4 matrix = Matrix4x4.TRS(position, rot, scale);
        
        bedrockMatrices.Add(matrix);
    }

    void CreateBatches()
    {
        // DrawMeshInstanced ne peut dessiner que 1023 objets par appel.
        // On doit découper notre liste de 40 000 blocs en listes de 1023.
        for (int i = 0; i < bedrockMatrices.Count; i += 1023)
        {
            int count = Mathf.Min(1023, bedrockMatrices.Count - i);
            bedrockBatches.Add(bedrockMatrices.GetRange(i, count));
        }
    }

    // --- CRAWLER LOGIC (GameObjects réels) ---
    void SpawnBlockAndCrawl(Vector3Int gridPos, Vector3 upAxis, Module prefab, string name, int chainLength)
    {
        if (occupiedCells.Contains(gridPos) || currentBlockCount >= totalBlocksLimit) return;

        Module instance = Instantiate(prefab, GridToWorld(gridPos), Quaternion.LookRotation(Vector3.forward, upAxis));
        instance.name = name;
        instance.transform.parent = transform;
        instance.gameObject.SetActive(true);
        occupiedCells.Add(gridPos);
        currentBlockCount++;
        StartCoroutine(Crawler(instance, name, chainLength));
    }

    IEnumerator Crawler(Module currentModule, string crawlerName, int currentChain)
    {
        activeCrawlers++;
        int stepsTaken = 0;
        int blocksBuiltThisBatch = 0;
        int maxLife = 200; 

        while (stepsTaken < maxLife && currentBlockCount < totalBlocksLimit)
        {
            blocksBuiltThisBatch++;
            if (blocksBuiltThisBatch > blocksPerFrame / Mathf.Max(1, activeCrawlers)) 
            {
                blocksBuiltThisBatch = 0;
                yield return null; 
            }

            SocketTag startSocket = currentModule.GetRandomOpenSocket();
            if (startSocket == null) break;

            Module nextPrefab = PickWeightedModule(startSocket, currentModule, ref currentChain);
            if (nextPrefab == null) { stepsTaken++; continue; }

            SocketTag endSocket = GetMatchingSocket(nextPrefab, startSocket);
            if (endSocket == null) { stepsTaken++; continue; }

            Vector3 targetWorldPos; Quaternion targetRot;
            CalculateAlignment(startSocket, endSocket, out targetWorldPos, out targetRot);
            Vector3Int targetGridPos = WorldToGrid(targetWorldPos);

            if (occupiedCells.Contains(targetGridPos) || IsOutOfBounds(targetGridPos)) { stepsTaken++; continue; }
            if (IsInHorizonGap(targetGridPos.y)) break; 

            Module newInstance = Instantiate(nextPrefab, GridToWorld(targetGridPos), targetRot);
            newInstance.transform.parent = transform;
            newInstance.gameObject.SetActive(true);
            newInstance.name = $"{crawlerName}_{stepsTaken}";
            
            occupiedCells.Add(targetGridPos);
            currentBlockCount++;

            if (newInstance.type == ModuleType.Structure && Random.value < branchingRate)
            {
                if (activeCrawlers < maxCrawlersSimultanes)
                    StartCoroutine(Crawler(newInstance, $"{crawlerName}_Br", 0));
            }

            currentModule = newInstance;
            stepsTaken++;
        }
        activeCrawlers--;
    }

    // --- UTILITAIRES ---
    // (Conserve les mêmes fonctions utilitaires : OnDrawGizmos, IsInHorizonGap, IsOutOfBounds, PickWeightedModule, etc.)
    // Je ne les remets pas pour raccourcir, mais elles sont identiques au script précédent.
    
    // RAPPEL DES FONCTIONS MANQUANTES SI BESOIN :
    void OnDrawGizmos() { /* ... code du script précédent ... */ }
    bool IsInHorizonGap(int y) { return y > horizonGapMin && y < horizonGapMax; }
    bool IsOutOfBounds(Vector3Int p) { 
        if (Mathf.Abs(p.x) > mapSizeX / 2 + 5) return true;
        if (Mathf.Abs(p.z) > mapSizeZ / 2 + 5) return true;
        if (p.y < 0 || p.y > ceilingHeight) return true; 
        return false;
    }
    bool HasAnyCompatibleSocket(Module prefab, SocketTag source) {
        if(prefab == null) return false;
        var list = prefab.allSockets; if (list == null || list.Count == 0) list = prefab.GetComponentsInChildren<SocketTag>(true).ToList();
        return list.Any(s => IsCompatible(source, s));
    }
    SocketTag GetMatchingSocket(Module prefab, SocketTag sourceSocket) {
        var list = prefab.allSockets; if (list == null || list.Count == 0) list = prefab.GetComponentsInChildren<SocketTag>(true).ToList();
        var c = list.Where(s => IsCompatible(sourceSocket, s)).ToList();
        return c.Count == 0 ? null : c[Random.Range(0, c.Count)];
    }
    Module PickWeightedModule(SocketTag sourceSocket, Module currentModule, ref int chainCount) {
        List<Module> candidates = new List<Module>();
        if (currentModule.type == ModuleType.Stair) {
            if (chainCount < minStairLength) { candidates = modulePrefabs.Where(m => m.type == ModuleType.Stair).ToList(); chainCount++; }
            else if (Random.value > stairContinuity) { candidates = modulePrefabs.Where(m => m.type != ModuleType.Stair).ToList(); chainCount = 0; }
            else { candidates = modulePrefabs.Where(m => m.type == ModuleType.Stair).ToList(); chainCount++; }
        } else { candidates = modulePrefabs; chainCount = 0; }
        var valid = candidates.Where(m => HasAnyCompatibleSocket(m, sourceSocket)).ToList();
        if (valid.Count == 0 && candidates.Count != modulePrefabs.Count) valid = modulePrefabs.Where(m => HasAnyCompatibleSocket(m, sourceSocket)).ToList();
        if (valid.Count == 0) return null;
        return valid[Random.Range(0, valid.Count)];
    }
    bool IsCompatible(SocketTag a, SocketTag b) {
        string aMain = a.main.Trim().ToLower(); string bMain = b.main.Trim().ToLower();
        string aSub = a.sub.Trim().ToLower(); string bSub = b.sub.Trim().ToLower();
        if (aMain == "void" || bMain == "void" || aMain == "none") return false;
        if (aMain == "side" && bMain == "side") return aSub == bSub;
        bool aLink = (aMain == "face" || aMain == "stair");
        bool bLink = (bMain == "face" || bMain == "stair");
        if (aLink && bLink) return true;
        return false;
    }
    void CalculateAlignment(SocketTag source, SocketTag target, out Vector3 pos, out Quaternion rot) {
         Vector3 forwardDir = -source.transform.forward; Vector3 upDir = source.transform.up;
         Module sMod = source.GetComponentInParent<Module>(); Module tMod = target.GetComponentInParent<Module>();
         if(sMod && tMod && tMod.type == ModuleType.Stair) upDir = source.transform.up;
         rot = Quaternion.LookRotation(forwardDir, upDir) * Quaternion.Inverse(target.transform.localRotation);
         Vector3 e = rot.eulerAngles; rot = Quaternion.Euler(Mathf.Round(e.x/90f)*90f, Mathf.Round(e.y/90f)*90f, Mathf.Round(e.z/90f)*90f);
         pos = source.transform.position - (rot * target.transform.localPosition);
    }
    Vector3Int WorldToGrid(Vector3 wp) => new Vector3Int(Mathf.RoundToInt(wp.x/gridSize), Mathf.RoundToInt(wp.y/gridSize), Mathf.RoundToInt(wp.z/gridSize));
    Vector3 GridToWorld(Vector3Int gp) => new Vector3(gp.x*gridSize, gp.y*gridSize, gp.z*gridSize);
}