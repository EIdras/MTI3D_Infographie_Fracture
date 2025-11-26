using UnityEngine;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class EscherVoidGenerator : MonoBehaviour
{
    [Header("Assets")]
    public List<Module> modulePrefabs;

    [Header("Volume de Génération")]
    public Vector3Int mapSize = new Vector3Int(80, 80, 80); 
    
    [Header("Le Cœur Vide")]
    public float centralVoidRadius = 20f; 

    [Header("Densité Automatique")]
    [Tooltip("Distance moyenne entre deux structures (en mètres/unités). Plus c'est petit, plus c'est dense.")]
    public float structureSpacing = 25f; // Remplace "NumberOfSeeds"
    
    [Tooltip("Limite de sécurité pour ne pas générer 1 million de graines par erreur")]
    public int maxSeedSafety = 5000; 

    [Header("Paramètres Limites")]
    public int totalBlocksLimit = 50000; // Monte ça si tu fais du 500x500
    
    [Header("Style Berserk")]
    [Range(0f, 1f)] public float stairContinuity = 0.98f; 
    [Range(0f, 1f)] public float branchingRate = 0.05f;   
    public int minStairLength = 8;
    public float gridSize = 2.0f;

    [Header("Vitesse Editeur")]
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

    // --- COMMANDES ---
    [ContextMenu("Générer Nuage")]
    public void GenerateStructure()
    {
        ClearStructure();
        Debug.Log("Démarrage génération Nuage...");
        
        modulePrefabs.RemoveAll(x => x == null);
        if(modulePrefabs.Count == 0) { Debug.LogError("Liste prefabs vide !"); return; }

        occupiedCells.Clear();
        generationQueue.Clear();
        activeCrawlers.Clear();
        currentBlockCount = 0;

        this.gameObject.SetActive(false); 

        generationQueue.Enqueue(InitializeSeedsPhase);

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
    }

    [ContextMenu("STOP")]
    public void StopGeneration()
    {
        #if UNITY_EDITOR
        EditorApplication.update -= EditorUpdateLoop;
        #endif
        this.gameObject.SetActive(true);
        EditorUtility.ClearProgressBar();
        Debug.Log($"Génération terminée. {currentBlockCount} blocs.");
    }

    // --- BOUCLE PRINCIPALE ---
    void EditorUpdateLoop()
    {
        if (this == null) { StopGeneration(); return; }

        if (generationQueue.Count == 0 && activeCrawlers.Count == 0) { StopGeneration(); return; }

        int ops = 0;
        while (generationQueue.Count > 0 && ops < operationsPerFrame) {
            generationQueue.Dequeue().Invoke();
            ops = operationsPerFrame; 
        }

        if (activeCrawlers.Count > 0) {
            for (int i = activeCrawlers.Count - 1; i >= 0; i--) {
                if (ops >= operationsPerFrame) break;
                bool keepAlive = ProcessCrawlerStep(activeCrawlers[i]);
                if (!keepAlive) activeCrawlers.RemoveAt(i);
                ops++;
            }
        }

        if (currentBlockCount % 200 == 0) {
            EditorUtility.DisplayProgressBar("Génération...", $"{currentBlockCount} blocs", (float)currentBlockCount / totalBlocksLimit);
        }
    }

    // --- LOGIQUE DE GENERATION ---

    void InitializeSeedsPhase()
    {
        // 1. CALCUL DU NOMBRE DE GRAINES BASÉ SUR LE VOLUME
        long volume = (long)mapSize.x * mapSize.y * mapSize.z;
        // Volume d'une "cellule" virtuelle définie par l'espacement
        float cellVolume = Mathf.Pow(structureSpacing, 3);
        
        // Nombre théorique de graines pour remplir ce volume
        int targetSeeds = Mathf.FloorToInt(volume / cellVolume);
        
        // Sécurité
        targetSeeds = Mathf.Clamp(targetSeeds, 1, maxSeedSafety);

        Debug.Log($"Volume: {volume}. Espacement: {structureSpacing}. Objectif Graines: {targetSeeds}");

        Module starter = modulePrefabs.FirstOrDefault(m => m.type == ModuleType.Structure) ?? modulePrefabs[0];
        int seedsPlaced = 0;
        int attempts = 0;

        while (seedsPlaced < targetSeeds && attempts < targetSeeds * 10)
        {
            attempts++;
            
            // Position aléatoire
            Vector3Int randomPos = new Vector3Int(
                Random.Range(-mapSize.x/2, mapSize.x/2),
                Random.Range(-mapSize.y/2, mapSize.y/2),
                Random.Range(-mapSize.z/2, mapSize.z/2)
            );

            if (IsInCentralVoid(randomPos)) continue;
            if (occupiedCells.Contains(randomPos)) continue;

            Vector3 randomDir = Random.onUnitSphere; 
            Vector3 cardinalDir = GetCardinalDirection(randomDir);

            Module m = SpawnEditorBlock(randomPos, cardinalDir, starter, $"Seed_{seedsPlaced}");
            if (m) {
                activeCrawlers.Add(new CrawlerData { currentModule = m, name = $"C_{seedsPlaced}", stepsTaken = 0, chainLength = 0 });
                seedsPlaced++;
            }
        }
        Debug.Log($"Graines placées : {seedsPlaced}");
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
        if (IsInCentralVoid(targetGridPos)) return false; 

        Module newInstance = SpawnEditorBlock(targetGridPos, Vector3.up, nextPrefab, $"{crawler.name}_{crawler.stepsTaken}", targetRot);
        
        if (newInstance.type == ModuleType.Structure && Random.value < branchingRate)
        {
            activeCrawlers.Add(new CrawlerData { currentModule = newInstance, name = crawler.name + "_B", stepsTaken = 0, chainLength = 0 });
        }

        crawler.currentModule = newInstance;
        crawler.stepsTaken++;
        return true;
    }

    Module SpawnEditorBlock(Vector3Int gridPos, Vector3 upAxis, Module prefab, string name, Quaternion? forceRot = null)
    {
        if (occupiedCells.Contains(gridPos) || currentBlockCount >= totalBlocksLimit) return null;

        Quaternion rot = forceRot.HasValue ? forceRot.Value : Quaternion.LookRotation(Vector3.forward, upAxis);
        
        Module instance = (Module)PrefabUtility.InstantiatePrefab(prefab, transform);
        instance.transform.position = GridToWorld(gridPos);
        instance.transform.rotation = rot;
        instance.name = name;
        instance.gameObject.isStatic = true;
        instance.gameObject.SetActive(true);

        if (instance.allSockets == null || instance.allSockets.Count == 0)
            instance.allSockets = instance.GetComponentsInChildren<SocketTag>(true).ToList();

        occupiedCells.Add(gridPos);
        currentBlockCount++;
        return instance;
    }

    // --- MATHS & GIZMOS ---

    bool IsInCentralVoid(Vector3Int gridPos)
    {
        float dist = Vector3.Magnitude(new Vector3(gridPos.x, gridPos.y, gridPos.z));
        // On considère le rayon en unités grille
        return dist < centralVoidRadius;
    }

    bool IsOutOfBounds(Vector3Int p) {
        return Mathf.Abs(p.x) > mapSize.x/2 || Mathf.Abs(p.y) > mapSize.y/2 || Mathf.Abs(p.z) > mapSize.z/2;
    }

    Vector3 GetCardinalDirection(Vector3 dir) {
        float max = Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.y), Mathf.Abs(dir.z));
        if (max == Mathf.Abs(dir.x)) return dir.x > 0 ? Vector3.right : Vector3.left;
        if (max == Mathf.Abs(dir.y)) return dir.y > 0 ? Vector3.up : Vector3.down;
        return dir.z > 0 ? Vector3.forward : Vector3.back;
    }

    void OnDrawGizmos() {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position, new Vector3(mapSize.x * gridSize, mapSize.y * gridSize, mapSize.z * gridSize));
        
        Gizmos.color = new Color(1, 0, 0, 0.3f);
        Gizmos.DrawSphere(transform.position, centralVoidRadius * gridSize);
    }

    // --- UTILITAIRES STANDARDS ---
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