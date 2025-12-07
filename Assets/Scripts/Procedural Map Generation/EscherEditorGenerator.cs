using UnityEngine;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class EscherVoidGenerator : MonoBehaviour
{
    [System.Serializable]
    public struct WeightedModule {
        public Module module;
        [Tooltip("Plus le poids est haut, plus l'objet apparaît souvent.")]
        [Range(0f, 100f)] public float weight;
    }

    [Header("Assets & Probabilités")]
    public List<WeightedModule> modulePrefabs; 

    [Header("Volume de Génération")]
    public Vector3Int mapSize = new Vector3Int(80, 80, 80); 
    public float centralVoidRadius = 20f; 

    [Header("Paramètres de Densité")]
    public float structureSpacing = 25f;
    public int totalBlocksLimit = 50000;
    
    [Header("Comportement Crawler")]
    [Range(0f, 1f)] public float stairContinuity = 0.98f; 
    [Range(0f, 1f)] public float branchingRate = 0.05f;    
    public int minStairLength = 8;
    [Tooltip("Probabilité de tenter une Obélisque quand une branche se termine.")]
    [Range(0f, 1f)] public float obeliskEndChance = 0.8f;

    [Header("Technique")]
    public float gridSize = 2.0f;
    public int operationsPerFrame = 500; 
    public bool showDebugLogs = false; 

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
        public bool isDying; 
    }

    public bool IsCellOccupied(Vector3Int pos) => occupiedCells.Contains(pos);

    // --- COMMANDES ---
    [ContextMenu("Générer Nuage")]
    public void GenerateStructure()
    {
        ClearStructure();
        modulePrefabs.RemoveAll(x => x.module == null);
        if(modulePrefabs.Count == 0) { Debug.LogError("Aucun module dans la liste !"); return; }

        occupiedCells.Clear();
        generationQueue.Clear();
        activeCrawlers.Clear();
        currentBlockCount = 0;

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

    public void StopGeneration()
    {
        #if UNITY_EDITOR
        EditorApplication.update -= EditorUpdateLoop;
        #endif
        EditorUtility.ClearProgressBar();
        Debug.Log($"Génération terminée. {currentBlockCount} blocs.");
    }

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
        
        if (currentBlockCount % 200 == 0)
            EditorUtility.DisplayProgressBar("Génération...", $"{currentBlockCount} / {totalBlocksLimit} blocs", (float)currentBlockCount / totalBlocksLimit);
    }

    // --- LOGIQUE CORE ---

    void InitializeSeedsPhase()
    {
        long volume = (long)mapSize.x * mapSize.y * mapSize.z;
        float cellVolume = Mathf.Pow(structureSpacing, 3);
        int targetSeeds = Mathf.FloorToInt(volume / cellVolume);
        targetSeeds = Mathf.Clamp(targetSeeds, 1, 5000);

        Module starter = modulePrefabs.FirstOrDefault(m => m.module.type == ModuleType.Structure).module;
        if (starter == null) starter = modulePrefabs[0].module;

        int seedsPlaced = 0;
        int attempts = 0;

        while (seedsPlaced < targetSeeds && attempts < targetSeeds * 10)
        {
            attempts++;
            Vector3Int randomPos = new Vector3Int(
                Random.Range(-mapSize.x/2, mapSize.x/2),
                Random.Range(-mapSize.y/2, mapSize.y/2),
                Random.Range(-mapSize.z/2, mapSize.z/2)
            );

            if (IsInCentralVoid(randomPos)) continue;
            if (occupiedCells.Contains(randomPos)) continue;

            Vector3 cardinalDir = GetCardinalDirection(Random.onUnitSphere);
            Module m = SpawnEditorBlock(randomPos, Quaternion.LookRotation(cardinalDir, Vector3.up), starter, $"Seed_{seedsPlaced}");
            if (m) {
                activeCrawlers.Add(new CrawlerData { currentModule = m, name = $"C_{seedsPlaced}", stepsTaken = 0, chainLength = 0 });
                seedsPlaced++;
            }
        }
    }

    bool ProcessCrawlerStep(CrawlerData crawler)
    {
        // --- 1. GESTION INTELLIGENTE DE LA FIN (SOFT LIMIT) ---
        bool softLimitReached = (currentBlockCount >= totalBlocksLimit);
        bool stepsExceeded = (crawler.stepsTaken > 200);

        // Si on dépasse la limite, on force le mode "Mourir" pour tout le monde
        if (softLimitReached) crawler.isDying = true;

        if (crawler.isDying || stepsExceeded)
        {
            if (!crawler.isDying) crawler.isDying = true; 

            // TENTATIVE 1 : Poser l'Obélisque (nécessite socket vertical)
            if (Random.value < obeliskEndChance)
            {
                SocketTag verticalSocket = GetVerticalSocket(crawler.currentModule);
                
                if (verticalSocket != null)
                {
                    var obelisks = modulePrefabs.Where(wm => wm.module.type == ModuleType.Obelisk).ToList();
                    Module obeliskPrefab = PickWeighted(obelisks, verticalSocket);

                    if (obeliskPrefab != null)
                    {
                        bool placed = TrySpawnNext(crawler, verticalSocket, obeliskPrefab, "_Obelisk");
                        if (placed) {
                            if(showDebugLogs) Debug.Log($"SUCCES FIN : Obélisque posée sur {crawler.name}");
                            return false; // Crawler meurt heureux
                        }
                    }
                }
                
                // TENTATIVE 2 : Sauvetage (Si pas de socket vertical, on pose une plateforme)
                // On ne le fait que si on n'a pas déjà abusé (chainLength sert de marqueur ici)
                if (crawler.chainLength < 1000) 
                {
                    crawler.chainLength = 1000; // Marque "Sauvetage en cours"
                    SocketTag anySocket = crawler.currentModule.GetRandomOpenSocket();
                    if (anySocket != null)
                    {
                        var platforms = modulePrefabs.Where(wm => wm.module.type == ModuleType.Structure && IsFlatModule(wm.module)).ToList();
                        Module platformPrefab = PickWeighted(platforms, anySocket);

                        if (platformPrefab != null)
                        {
                            bool saved = TrySpawnNext(crawler, anySocket, platformPrefab, "_FixPlatform");
                            if (saved) return true; // On vit un tour de plus pour poser l'obélisque
                        }
                    }
                }
            }
            return false; // Si tout échoue, on meurt
        }

        // --- 2. COMPORTEMENT STANDARD ---
        SocketTag startSocket = crawler.currentModule.GetRandomOpenSocket();
        if (startSocket == null) return false; 

        Module nextPrefab = PickWeightedModule(startSocket, crawler.currentModule, ref crawler.chainLength);
        
        if (nextPrefab == null) { 
            crawler.stepsTaken++; 
            return true; // On réessaiera au prochain tour
        } 

        // Si on tombe sur une obélisque par hasard, on déclenche la fin
        if (nextPrefab.type == ModuleType.Obelisk) crawler.isDying = true;

        bool success = TrySpawnNext(crawler, startSocket, nextPrefab, $"_{crawler.stepsTaken}");
        if (!success) crawler.stepsTaken++;

        return true;
    }

    bool TrySpawnNext(CrawlerData crawler, SocketTag startSocket, Module nextPrefab, string suffix)
    {
        SocketTag endSocket = GetMatchingSocket(nextPrefab, startSocket);
        if (endSocket == null) return false;

        Vector3 targetWorldPos; Quaternion targetRot;
        CalculateAlignment(startSocket, endSocket, out targetWorldPos, out targetRot);
        Vector3Int targetGridPos = WorldToGrid(targetWorldPos);

        // A. COLLISION
        if (CheckCollision(targetGridPos, targetRot, nextPrefab)) return false;
        
        // B. VOID CENTRAL
        if (IsInCentralVoid(targetGridPos)) return false; 

        // C. ANCRAGE STRICT (Pour les Portails)
        if (nextPrefab.type == ModuleType.Portal || nextPrefab.requiredAnchors.Count > 0)
        {
            // On utilise le paramètre du module (strict ou non)
            if (!CheckAnchors(targetGridPos, targetRot, nextPrefab, nextPrefab.strictAnchorCheck)) return false;
        }

        // --- SPAWN ET SECURITE NULL ---
        Module newInstance = SpawnEditorBlock(targetGridPos, targetRot, nextPrefab, $"{crawler.name}{suffix}");
        
        // CORRECTION IMPORTANTE : Si la limite est atteinte, newInstance est null. On arrête.
        if (newInstance == null) return false;

        // Branching (Sauf si c'est une fin type Obélisque)
        if (newInstance.type != ModuleType.Obelisk && newInstance.type == ModuleType.Structure && Random.value < branchingRate)
        {
            activeCrawlers.Add(new CrawlerData { currentModule = newInstance, name = crawler.name + "_B", stepsTaken = 0, chainLength = 0 });
        }
        
        crawler.currentModule = newInstance;
        return true;
    }

    Module SpawnEditorBlock(Vector3Int gridPos, Quaternion rotation, Module prefab, string name)
    {
        // --- LOGIQUE DE LIMITE SOUPLE ---
        
        // 1. Limite Absolue (Hard Limit) : Sécurité anti-crash
        int hardLimit = totalBlocksLimit + 200; 
        if (currentBlockCount >= hardLimit) return null;

        // 2. Limite Structurelle (Soft Limit) : On arrête les structures, mais on autorise les Obélisques
        if (currentBlockCount >= totalBlocksLimit)
        {
            if (prefab.type != ModuleType.Obelisk) return null;
        }
        // --------------------------------

        Module instance = (Module)PrefabUtility.InstantiatePrefab(prefab, transform);
        instance.transform.position = GridToWorld(gridPos);
        instance.transform.rotation = rotation;
        instance.name = name;
        instance.gameObject.isStatic = true;
        instance.gameObject.SetActive(true);

        // Init sockets si nécessaire
        if (instance.allSockets == null || instance.allSockets.Count == 0)
            instance.allSockets = instance.GetComponentsInChildren<SocketTag>(true).ToList();

        // Occupation grille
        foreach(var offset in instance.occupiedOffsets)
        {
            Vector3 rotatedOffsetFloat = rotation * (Vector3)offset;
            Vector3Int rotatedOffset = new Vector3Int(Mathf.RoundToInt(rotatedOffsetFloat.x), Mathf.RoundToInt(rotatedOffsetFloat.y), Mathf.RoundToInt(rotatedOffsetFloat.z));
            occupiedCells.Add(gridPos + rotatedOffset);
        }

        currentBlockCount++;
        return instance;
    }

    // --- SELECTION ET POIDS ---
    Module PickWeightedModule(SocketTag sourceSocket, Module currentModule, ref int chainCount) 
    {
        List<WeightedModule> candidates = new List<WeightedModule>();
        
        if (currentModule.type == ModuleType.Stair) {
            if (chainCount < minStairLength) { 
                // Force Escalier
                candidates = modulePrefabs.Where(wm => wm.module.type == ModuleType.Stair).ToList(); 
                chainCount++; 
            }
            else if (Random.value > stairContinuity) { 
                // Arrêt Escalier (Sauf Obélisque)
                candidates = modulePrefabs.Where(wm => wm.module.type != ModuleType.Stair && wm.module.type != ModuleType.Obelisk).ToList(); 
                chainCount = 0; 
            }
            else { 
                // Continue Escalier
                candidates = modulePrefabs.Where(wm => wm.module.type == ModuleType.Stair).ToList(); 
                chainCount++; 
            }
        } 
        else { 
            // Standard (Sauf Obélisque qui est réservé pour la fin)
            candidates = modulePrefabs.Where(wm => wm.module.type != ModuleType.Obelisk).ToList();
            chainCount = 0; 
        }

        return PickWeighted(candidates, sourceSocket);
    }

    Module PickWeighted(List<WeightedModule> candidates, SocketTag sourceSocket)
    {
        var validCandidates = candidates.Where(wm => HasAnyCompatibleSocket(wm.module, sourceSocket)).ToList();
        
        if (validCandidates.Count == 0) return null;

        float totalWeight = validCandidates.Sum(wm => wm.weight);
        float randomPoint = Random.value * totalWeight;

        foreach(var wm in validCandidates) {
            if (randomPoint < wm.weight) return wm.module;
            randomPoint -= wm.weight;
        }
        return validCandidates.Last().module;
    }

    // --- UTILITAIRES DE SOCKET ---
    SocketTag GetVerticalSocket(Module m)
    {
        if (m.allSockets == null) return null;
        var shuffled = m.allSockets.OrderBy(x => Random.value).ToList();
        foreach(var s in shuffled)
        {
            // Vérifie si le socket pointe vers le haut (World Space)
            if (Vector3.Dot(s.transform.up, Vector3.up) > 0.8f) return s;
        }
        return null;
    }

    bool IsFlatModule(Module m)
    {
        string n = m.name.ToLower();
        return n.Contains("slab") || n.Contains("floor") || n.Contains("cube") || n.Contains("dalle");
    }

    // --- VALIDATIONS ---
    bool CheckCollision(Vector3Int rootGridPos, Quaternion rotation, Module prefab)
    {
        foreach(var offset in prefab.occupiedOffsets)
        {
            Vector3 rotatedOffsetFloat = rotation * (Vector3)offset;
            Vector3Int rotatedOffset = new Vector3Int(Mathf.RoundToInt(rotatedOffsetFloat.x), Mathf.RoundToInt(rotatedOffsetFloat.y), Mathf.RoundToInt(rotatedOffsetFloat.z));
            Vector3Int absolutePos = rootGridPos + rotatedOffset;

            if (occupiedCells.Contains(absolutePos) || IsOutOfBounds(absolutePos)) return true;
        }
        return false;
    }

    bool CheckAnchors(Vector3Int rootGridPos, Quaternion rotation, Module prefab, bool strictMode)
    {
        foreach(var anchor in prefab.requiredAnchors)
        {
            Vector3 rotatedAnchorFloat = rotation * (Vector3)anchor;
            Vector3Int rotatedAnchor = new Vector3Int(Mathf.RoundToInt(rotatedAnchorFloat.x), Mathf.RoundToInt(rotatedAnchorFloat.y), Mathf.RoundToInt(rotatedAnchorFloat.z));
            Vector3Int targetPos = rootGridPos + rotatedAnchor;

            if (strictMode) {
                // Doit toucher un bloc existant
                if (!occupiedCells.Contains(targetPos)) return false; 
            } else {
                // Ne doit juste pas être hors limite
                if (IsOutOfBounds(targetPos) || IsInCentralVoid(targetPos)) return false;
            }
        }
        return true;
    }

    bool IsInCentralVoid(Vector3Int gridPos)
    {
        float dist = Vector3.Magnitude(new Vector3(gridPos.x, gridPos.y, gridPos.z));
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

    // --- VISUALISATION ---
    void OnDrawGizmos() {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position, new Vector3(mapSize.x * gridSize, mapSize.y * gridSize, mapSize.z * gridSize));
        Gizmos.color = new Color(1, 0, 0, 0.3f);
        Gizmos.DrawSphere(transform.position, centralVoidRadius * gridSize);
    }

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
    bool IsCompatible(SocketTag a, SocketTag b) {
        string aMain = a.main.Trim().ToLower(); string bMain = b.main.Trim().ToLower();
        string aSub = a.sub.Trim().ToLower(); string bSub = b.sub.Trim().ToLower();
        if (aMain == "void" || bMain == "void" || aMain == "none") return false;
        if (aMain == "side" && bMain == "side") return aSub == bSub;
        // Compatibilité Face/Stair pour transition
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