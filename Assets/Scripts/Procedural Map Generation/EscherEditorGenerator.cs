using UnityEngine;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class EscherEditorGenerator : MonoBehaviour
{
    [Header("Assets")]
    public List<Module> modulePrefabs;

    [Header("Dimensions")]
    public int mapSizeX = 60;
    public int mapSizeZ = 60;
    public int ceilingHeight = 80;

    [Header("Socle")]
    public int bedrockThickness = 6; 
    [Range(0, 1)] public float bedrockDensity = 0.85f;

    [Header("Horizon")]
    public int horizonGapMin = 35; 
    public int horizonGapMax = 45;

    [Header("Filaments")]
    [Range(0, 1)] public float fillPercentage = 0.10f;
    public int totalBlocksLimit = 40000;
    
    [Header("Style")]
    [Range(0f, 1f)] public float stairContinuity = 0.98f; 
    [Range(0f, 1f)] public float branchingRate = 0.02f;   
    public int minStairLength = 10;
    public float gridSize = 2.0f;

    [Header("Vitesse Génération")]
    public int operationsPerFrame = 500; 

    // --- ETAT INTERNE ---
    private HashSet<Vector3Int> occupiedCells = new HashSet<Vector3Int>();
    private int currentBlockCount = 0;
    
    private Queue<System.Action> generationQueue = new Queue<System.Action>();
    private List<CrawlerData> activeCrawlers = new List<CrawlerData>();

    class CrawlerData {
        public Module currentModule;
        public string name;
        public int stepsTaken;
        public int chainLength;
    }

    // --- FONCTIONS PUBLIQUES ---
    [ContextMenu("Lancer Génération")]
    public void GenerateStructure()
    {
        ClearStructure();
        Debug.Log("Démarrage de la génération...");
        occupiedCells.Clear();
        generationQueue.Clear();
        activeCrawlers.Clear();
        currentBlockCount = 0;

        this.gameObject.SetActive(false); 

        generationQueue.Enqueue(GenerateBedrockPhase);
        generationQueue.Enqueue(InitializeCrawlersPhase);

        #if UNITY_EDITOR
        EditorApplication.update += EditorUpdateLoop;
        #endif
    }

    [ContextMenu("Tout Effacer")]
    public void ClearStructure()
    {
        var children = new List<GameObject>();
        foreach (Transform child in transform) children.Add(child.gameObject);
        foreach (var child in children) DestroyImmediate(child);
        
        occupiedCells.Clear();
        currentBlockCount = 0;
        Debug.Log("Structure effacée.");
    }

    [ContextMenu("STOP")]
    public void StopGeneration()
    {
        #if UNITY_EDITOR
        EditorApplication.update -= EditorUpdateLoop;
        #endif
        this.gameObject.SetActive(true);
        EditorUtility.ClearProgressBar();
        Debug.Log($"Génération arrêtée/terminée. {currentBlockCount} blocs.");
    }

    // --- BOUCLE EDITEUR ---
    void EditorUpdateLoop()
    {
        if (generationQueue.Count == 0 && activeCrawlers.Count == 0)
        {
            StopGeneration();
            return;
        }

        int ops = 0;

        while (generationQueue.Count > 0 && ops < operationsPerFrame)
        {
            var action = generationQueue.Dequeue();
            action.Invoke();
            ops = operationsPerFrame; 
        }

        if (activeCrawlers.Count > 0)
        {
            for (int i = activeCrawlers.Count - 1; i >= 0; i--)
            {
                if (ops >= operationsPerFrame) break;
                bool keepAlive = ProcessCrawlerStep(activeCrawlers[i]);
                if (!keepAlive) activeCrawlers.RemoveAt(i);
                ops++;
            }
        }

        if (currentBlockCount % 100 == 0)
        {
            float progress = (float)currentBlockCount / totalBlocksLimit;
            EditorUtility.DisplayProgressBar("Génération structures", $"Construction... {currentBlockCount} blocs", progress);
        }
    }

    // --- PHASES ---
    void GenerateBedrockPhase()
    {
        Module cubePrefab = modulePrefabs.FirstOrDefault(m => m.type == ModuleType.Structure);
        if (cubePrefab == null) cubePrefab = modulePrefabs[0];

        int halfX = mapSizeX / 2;
        int halfZ = mapSizeZ / 2;

        for (int x = -halfX; x < halfX; x++)
        {
            for (int z = -halfZ; z < halfZ; z++)
            {
                float noise = Mathf.PerlinNoise((x + 1000) * 0.1f, (z + 1000) * 0.1f); 
                int localThickness = Mathf.RoundToInt(bedrockThickness * (0.5f + noise));
                int startY = Mathf.Max(0, localThickness - 2); 

                for (int y = startY; y < localThickness; y++)
                {
                    if (Random.value < bedrockDensity) 
                        SpawnEditorBlock(new Vector3Int(x, y, z), Vector3.up, cubePrefab);
                }
                for (int y = startY; y < localThickness; y++)
                {
                    if (Random.value < bedrockDensity) 
                        SpawnEditorBlock(new Vector3Int(x, ceilingHeight - y, z), Vector3.down, cubePrefab);
                }
            }
        }
    }

    void InitializeCrawlersPhase()
    {
        Debug.Log("Initialisation des Crawlers...");
        int halfX = mapSizeX / 2;
        int halfZ = mapSizeZ / 2;
        int step = 6; 
        
        List<CrawlerData> seeds = new List<CrawlerData>();
        Module starter = modulePrefabs.FirstOrDefault(m => m.type == ModuleType.Structure) ?? modulePrefabs[0];

        for (int x = -halfX; x < halfX; x += step)
        {
            for (int z = -halfZ; z < halfZ; z += step)
            {
                float noise = Mathf.PerlinNoise((x + 1000) * 0.1f, (z + 1000) * 0.1f); 
                int localHeight = Mathf.RoundToInt(bedrockThickness * (0.5f + noise));
                int safeY = localHeight + 1; 

                Vector3Int posFloor = new Vector3Int(x, safeY, z);
                if (!occupiedCells.Contains(posFloor))
                {
                    Module m = SpawnEditorBlock(posFloor, Vector3.up, starter, $"RootF_{x}_{z}");
                    if(m) activeCrawlers.Add(new CrawlerData { currentModule = m, name = $"F_{x}_{z}", stepsTaken = 0, chainLength = 0 });
                }

                int safeCeilingY = ceilingHeight - localHeight - 1;
                Vector3Int posCeil = new Vector3Int(x, safeCeilingY, z);
                if (!occupiedCells.Contains(posCeil))
                {
                    Module m = SpawnEditorBlock(posCeil, Vector3.down, starter, $"RootC_{x}_{z}");
                    if(m) activeCrawlers.Add(new CrawlerData { currentModule = m, name = $"C_{x}_{z}", stepsTaken = 0, chainLength = 0 });
                }
            }
        }
        
        activeCrawlers = activeCrawlers.OrderBy(x => Random.value).ToList();
        int limit = Mathf.FloorToInt(activeCrawlers.Count * fillPercentage);
        if (activeCrawlers.Count > limit) activeCrawlers.RemoveRange(limit, activeCrawlers.Count - limit);
    }

    bool ProcessCrawlerStep(CrawlerData crawler)
    {
        if (crawler.stepsTaken > 200 || currentBlockCount >= totalBlocksLimit) return false;

        SocketTag startSocket = crawler.currentModule.GetRandomOpenSocket();
        if (startSocket == null) return false;

        Module nextPrefab = PickWeightedModule(startSocket, crawler.currentModule, ref crawler.chainLength);
        if (nextPrefab == null) { crawler.stepsTaken++; return true; }

        SocketTag endSocket = GetMatchingSocket(nextPrefab, startSocket);
        if (endSocket == null) { crawler.stepsTaken++; return true; }

        Vector3 targetWorldPos; Quaternion targetRot;
        CalculateAlignment(startSocket, endSocket, out targetWorldPos, out targetRot);
        Vector3Int targetGridPos = WorldToGrid(targetWorldPos);

        if (occupiedCells.Contains(targetGridPos) || IsOutOfBounds(targetGridPos)) return true;
        if (IsInHorizonGap(targetGridPos.y)) return false; 

        Module newInstance = SpawnEditorBlock(targetGridPos, Vector3.up, nextPrefab, $"{crawler.name}_{crawler.stepsTaken}", targetRot);
        
        if (newInstance.type == ModuleType.Structure && Random.value < branchingRate)
        {
            activeCrawlers.Add(new CrawlerData { currentModule = newInstance, name = crawler.name + "_B", stepsTaken = 0, chainLength = 0 });
        }

        crawler.currentModule = newInstance;
        crawler.stepsTaken++;
        return true;
    }

    Module SpawnEditorBlock(Vector3Int gridPos, Vector3 upAxis, Module prefab, string name = "Block", Quaternion? forceRot = null)
    {
        if (occupiedCells.Contains(gridPos) || currentBlockCount >= totalBlocksLimit) return null;

        Quaternion rot = forceRot.HasValue ? forceRot.Value : Quaternion.LookRotation(Vector3.forward, upAxis);
        if (!forceRot.HasValue) rot *= Quaternion.Euler(0, Random.Range(0, 4) * 90, 0); 

        Module instance = (Module)PrefabUtility.InstantiatePrefab(prefab, transform);
        instance.transform.position = GridToWorld(gridPos);
        instance.transform.rotation = rot;
        instance.name = name;
        instance.gameObject.isStatic = true;
        instance.gameObject.SetActive(true);

        if (instance.allSockets == null || instance.allSockets.Count == 0)
        {
            instance.allSockets = instance.GetComponentsInChildren<SocketTag>(true).ToList();
        }

        occupiedCells.Add(gridPos);
        currentBlockCount++;
        return instance;
    }

    // --- VISUALISATION (GIZMOS) ---
    // C'est la partie que j'ai rajoutée pour que tu voies tes zones
    void OnDrawGizmos()
    {
        // 1. La boite globale (Jaune)
        Gizmos.color = Color.yellow;
        Vector3 center = transform.position + new Vector3(0, ceilingHeight * gridSize / 2, 0);
        Vector3 size = new Vector3(mapSizeX * gridSize, ceilingHeight * gridSize, mapSizeZ * gridSize);
        Gizmos.DrawWireCube(center, size);

        // 2. La Zone Vide Horizon (Rouge transparent)
        Gizmos.color = new Color(1, 0, 0, 0.2f);
        float gapCenterY = (horizonGapMin + horizonGapMax) / 2f * gridSize;
        float gapHeight = (horizonGapMax - horizonGapMin) * gridSize;
        Gizmos.DrawCube(transform.position + new Vector3(0, gapCenterY, 0), new Vector3(mapSizeX * gridSize, gapHeight, mapSizeZ * gridSize));

        // 3. Le Socle Sol (Bleu transparent)
        Gizmos.color = new Color(0, 0, 1, 0.2f);
        Gizmos.DrawCube(transform.position + new Vector3(0, bedrockThickness * gridSize / 2, 0), new Vector3(mapSizeX * gridSize, bedrockThickness * gridSize, mapSizeZ * gridSize));
    }

    // --- MATHS ---
    bool IsInHorizonGap(int y) { return y > horizonGapMin && y < horizonGapMax; }
    bool IsOutOfBounds(Vector3Int p) { return Mathf.Abs(p.x) > mapSizeX/2+5 || Mathf.Abs(p.z) > mapSizeZ/2+5 || p.y < 0 || p.y > ceilingHeight; }
    Vector3Int WorldToGrid(Vector3 wp) => new Vector3Int(Mathf.RoundToInt(wp.x/gridSize), Mathf.RoundToInt(wp.y/gridSize), Mathf.RoundToInt(wp.z/gridSize));
    Vector3 GridToWorld(Vector3Int gp) => new Vector3(gp.x*gridSize, gp.y*gridSize, gp.z*gridSize);

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
}