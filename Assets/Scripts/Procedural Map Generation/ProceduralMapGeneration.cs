using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class ProceduralMapGeneration : MonoBehaviour
{
    [Serializable]
    public class TileEntry {
        [Tooltip("Prefab de la tuile (wrapper/variant centré recommandé).")]
        public GameObject prefab;
        [Min(1)] [Tooltip("Probabilité relative de tirage de cette tuile.")]
        public int weight = 1;
        [Tooltip("Autoriser les 24 orientations (rotations de cube). Sinon: identité uniquement.")]
        public bool allowAllRotations = true;
    }

    [Header("Espace")]
    [Tooltip("Dimensions de la grille (X,Y,Z) en cellules.")]
    public Vector3Int gridSize = new Vector3Int(10, 3, 10);
    [Tooltip("Taille d’une cellule en unités Unity.")]
    public float cellSize = 2f;

    [Header("Génération")]
    [Tooltip("Graine RNG. 0 = aléatoire à chaque run.")]
    public int seed = 12345;
    [Tooltip("Lancer automatiquement dans Start().")]
    public bool runOnStart = true;
    [Tooltip("Effacer les enfants existants avant génération.")]
    public bool clearBefore = true;
    [Tooltip("Autoriser des cellules vides.")]
    public bool allowEmpty = true;
    [Min(0)] [Tooltip("Poids de base du vide.")]
    public int emptyWeight = 10;

    [Header("Morphologie / Parcimonie")]
    [Tooltip("Marge centrale (en cellules depuis chaque bord) interdite aux blocs non vides.")]
    [Min(0)] public int centerMargin = 2;
    [Tooltip("Amplification du poids du vide en allant vers le centre. 0 = neutre.")]
    [Range(0f, 5f)] public float emptyFalloff = 1.5f;
    [Tooltip("Exposant de la falloff. >1 rend le centre encore plus vide.")]
    [Range(0.5f, 4f)] public float falloffExponent = 2f;

    [Header("Croissance")]
    [Tooltip("Nombre de seeds aléatoires posés sur les bords.")]
    public int edgeSeeds = 6;
    [Tooltip("Arrêt après ce nombre de placements non vides. -1 = sans limite.")]
    public int targetPlacements = 120;
    [Tooltip("Itérations par frame.")]
    public int stepsPerFrame = 400;
    [Tooltip("Garde-fou anti-freeze.")]
    public int maxSteps = 200000;
    [Tooltip("Probabilité d’éviter les cellules avec >1 voisins posés. 1 = très linéaire.")]
    [Range(0f,1f)] public float branchBias = 0.9f;
    [Tooltip("Connexions max créées par une cellule.")]
    [Min(1)] public int maxConnectionsPerCell = 2;
    [Tooltip("Bonus si le candidat prolonge une connexion.")]
    public float continueBias = 2.0f;
    [Tooltip("Bonus additionnel si la connexion est 'side.stair'.")]
    public float stairBonus = 1.5f;

    [Header("Tuiles")]
    [Tooltip("Catalogue des tuiles utilisables.")]
    public List<TileEntry> tiles = new();

    [Header("Logs")]
    [Tooltip("Activer les logs.")]
    public bool enableLogs = true;
    [Range(0, 5000)] [Tooltip("Nombre max de logs.")]
    public int logLimit = 200;
    [Tooltip("Afficher le détail des voisins pour '0 candidat'.")]
    public bool logZeroCandidateNeighbors = true;

    // ---------- internals ----------
    enum Dir { PX, NX, PY, NY, PZ, NZ }
    struct SocketCat { public string main; public string sub; }
    struct SocketLocal { public Vector3 fwdLocal; public SocketCat cat; }
    struct SocketRot { public Dir dir; public SocketCat cat; }
    class Variant {
        public GameObject prefab;  // null = vide
        public Quaternion rot;
        public SocketRot[] sockets; // 6
        public int weight;
        public bool IsEmpty => prefab == null;
    }
    class Placed {
        public Variant variant;
        public GameObject go;      // null si vide
        public SocketRot[] sockets;
    }

    System.Random rng;
    Transform root;
    Dictionary<GameObject, List<Variant>> cacheVariants = new();
    List<Variant> emptyVariants;
    Placed[,,] grid;
    HashSet<(int x,int y,int z)> frontier = new();
    Queue<(int x,int y,int z)> frontierQueue = new();
    int placedCount, stepCount, logCount;

    void Start() { if (runOnStart) StartGeneration(); }

    [ContextMenu("Generate")]
    public void StartGeneration() {
        StopAllCoroutines();
        Init();
        StartCoroutine(GrowRoutine());
    }

    void Init() {
        rng = seed == 0 ? new System.Random() : new System.Random(seed);
        placedCount = 0; stepCount = 0; logCount = 0;
        frontier.Clear(); frontierQueue.Clear();

        if (clearBefore) {
            for (int i = transform.childCount - 1; i >= 0; --i)
                DestroyImmediate(transform.GetChild(i).gameObject);
        }
        root = new GameObject("MapRoot").transform;
        root.SetParent(transform, false);
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one;

        cacheVariants.Clear();
        foreach (var t in tiles) BuildVariants(t);
        BuildEmptyVariant();

        grid = new Placed[gridSize.x, gridSize.y, gridSize.z];
        PreplaceEdgeSeeds();
        BootstrapFrontierFromSeeds();
    }

    IEnumerator GrowRoutine() {
        while (frontierQueue.Count > 0) {
            int budget = stepsPerFrame;
            while (budget-- > 0 && frontierQueue.Count > 0) {
                if (maxSteps > 0 && stepCount++ > maxSteps) { LogWarn("MaxSteps atteint"); yield break; }

                var cell = frontierQueue.Dequeue();
                if (!frontier.Contains(cell)) continue;
                frontier.Remove(cell);

                int x = cell.x, y = cell.y, z = cell.z;
                if (grid[x,y,z] != null) continue;

                // éviter les embranchements
                int neighborCount = CountPlacedNeighbors(x,y,z);
                if (neighborCount > 1 && rng.NextDouble() < branchBias) {
                    frontier.Add(cell); frontierQueue.Enqueue(cell); continue;
                }

                bool mustBeNonEmpty = MustBeNonEmpty(x,y,z);
                var candidates = GetCandidates(x,y,z, mustBeNonEmpty);

                if (candidates.Count == 0) {
                    // vide si autorisé et pas requis non-vide
                    if (allowEmpty && !mustBeNonEmpty) {
                        PlaceVariant(x,y,z, GetEmpty());
                    } else {
                        if (logZeroCandidateNeighbors)
                            Log($"0 candidat @({x},{y},{z}) | {NeighborSignature(x,y,z)}");
                        continue;
                    }
                } else {
                    var v = PickWeightedWithContinuation(x,y,z, candidates);
                    PlaceVariant(x,y,z, v);
                    // Validation stricte après placement
                    if (!ValidateNeighbors(x,y,z)) {
                        // conflit: on bascule vide
                        if (v.prefab != null) DestroyImmediate(grid[x,y,z].go);
                        grid[x,y,z] = new Placed { variant = GetEmpty(), go = null, sockets = GetEmpty().sockets };
                    }
                }

                if (targetPlacements > 0 && placedCount >= targetPlacements) { Log($"Arrêt: targetPlacements={targetPlacements}"); yield break; }
            }
            yield return null;
        }
        Log("Terminé: plus de frontière");
    }

    // ---------- placement ----------
    void PlaceVariant(int x,int y,int z, Variant v) {
        var lp = new Vector3(x * cellSize, y * cellSize, z * cellSize);
        GameObject go = null;
        if (!v.IsEmpty) {
            go = Instantiate(v.prefab);
            var t = go.transform;
            t.SetParent(root, false);
            t.localPosition = lp;
            t.localRotation = v.rot;
            t.localScale = v.prefab.transform.localScale;
        }
        grid[x,y,z] = new Placed { variant = v, go = go, sockets = v.sockets };
        placedCount++;

        PushNeighbor(x+1,y,z); PushNeighbor(x-1,y,z);
        PushNeighbor(x,y+1,z); PushNeighbor(x,y-1,z);
        PushNeighbor(x,y,z+1); PushNeighbor(x,y,z-1);
    }

    void PushNeighbor(int x,int y,int z) {
        if (x<0||y<0||z<0||x>=gridSize.x||y>=gridSize.y||z>=gridSize.z) return;
        if (grid[x,y,z] != null) return;
        var key = (x,y,z);
        if (frontier.Add(key)) frontierQueue.Enqueue(key);
    }

    void BootstrapFrontierFromSeeds() {
        for (int x=0;x<gridSize.x;x++)
        for (int y=0;y<gridSize.y;y++)
        for (int z=0;z<gridSize.z;z++) {
            if (grid[x,y,z] != null) {
                PushNeighbor(x+1,y,z); PushNeighbor(x-1,y,z);
                PushNeighbor(x,y+1,z); PushNeighbor(x,y-1,z);
                PushNeighbor(x,y,z+1); PushNeighbor(x,y,z-1);
            }
        }
        if (frontier.Count == 0) {
            var c = (gridSize.x/2, gridSize.y/2, gridSize.z/2);
            frontier.Add(c); frontierQueue.Enqueue(c);
        }
    }

    void PreplaceEdgeSeeds() {
        int attempts = 0, placed = 0, maxAttempts = Math.Max(1, edgeSeeds) * 50;
        while (placed < edgeSeeds && attempts++ < maxAttempts) {
            int x = rng.Next(gridSize.x);
            int y = rng.Next(gridSize.y);
            int z = rng.Next(gridSize.z);
            bool isEdge = x==0 || y==0 || z==0 || x==gridSize.x-1 || y==gridSize.y-1 || z==gridSize.z-1;
            if (!isEdge) continue;
            if (grid[x,y,z] != null) continue;

            var cand = GetCandidates(x,y,z, forbidEmpty:false);
            cand.RemoveAll(v => v.IsEmpty || FacesOnlyBorder(v, x,y,z));
            if (cand.Count == 0) continue;

            var v = PickWeightedWithContinuation(x,y,z, cand);
            PlaceVariant(x,y,z, v);
            Log($"Seed placé @({x},{y},{z}) -> {(v.IsEmpty ? "EMPTY" : v.prefab.name)} rot={v.rot.eulerAngles}");
            placed++;
        }
        Log($"Seeds placés: {placed}/{edgeSeeds}");
    }

    // ---------- règles locales ----------
    bool FacesOnlyBorder(Variant v, int x,int y,int z) {
        for (int i = 0; i < 6; i++) {
            var s = v.sockets[i];
            if (s.cat.main != "face") continue;
            var (nx,ny,nz) = NeighborOf(x,y,z,s.dir);
            bool inside = nx>=0 && ny>=0 && nz>=0 && nx<gridSize.x && ny<gridSize.y && nz<gridSize.z;
            if (inside) return false;
        }
        return true;
    }

    bool MustBeNonEmpty(int x,int y,int z) {
        // zone centrale interdite
        if (!IsInBuildZone(x,y,z)) return false; // on autorise vide, même si voisins demandent
        foreach (Dir d in Enum.GetValues(typeof(Dir))) {
            var (nx,ny,nz) = NeighborOf(x,y,z,d);
            if (nx<0||ny<0||nz<0||nx>=gridSize.x||ny>=gridSize.y||nz>=gridSize.z) continue;
            var nb = grid[nx,ny,nz];
            if (nb == null) continue;
            var sb = nb.sockets[(int)d]; // voisin -> case
            if (sb.cat.main == "face") return true;
        }
        return false;
    }

    // Interdit le non-vide dans un noyau central
    bool IsInBuildZone(int x,int y,int z) {
        bool inX = x >= centerMargin && x < gridSize.x - centerMargin;
        bool inY = y >= centerMargin && y < gridSize.y - centerMargin;
        bool inZ = z >= centerMargin && z < gridSize.z - centerMargin;
        // si centerMargin==0 -> toute la zone est buildable
        return centerMargin == 0 || (inX || inY || inZ); // au moins un axe proche du bord
    }

    (int,int,int) NeighborOf(int x,int y,int z, Dir d) => d switch {
        Dir.PX => (x+1,y,z), Dir.NX => (x-1,y,z),
        Dir.PY => (x,y+1,z), Dir.NY => (x,y-1,z),
        Dir.PZ => (x,y,z+1), _ => (x,y,z-1)
    };

    int CountPlacedNeighbors(int x,int y,int z) {
        int c=0;
        foreach (Dir d in Enum.GetValues(typeof(Dir))) {
            var (nx,ny,nz) = NeighborOf(x,y,z,d);
            if (nx<0||ny<0||nz<0||nx>=gridSize.x||ny>=gridSize.y||nz>=gridSize.z) continue;
            if (grid[nx,ny,nz] != null) c++;
        }
        return c;
    }

    // ---------- candidats ----------
    List<Variant> GetCandidates(int x,int y,int z, bool forbidEmpty) {
        var list = new List<Variant>(64);

        // vide avec falloff radial
        if (allowEmpty && !forbidEmpty) {
            int eff = Mathf.Max(1, Mathf.RoundToInt(emptyWeight * EmptyMultiplier(x,y,z)));
            for (int i=0;i<eff;i++) list.Add(GetEmpty());
        }

        foreach (var kv in cacheVariants) {
            foreach (var v in kv.Value) {
                if (!FitsNeighbors(x,y,z,v)) continue;
                if (CountConnectionsIfPlaced(x,y,z,v) > maxConnectionsPerCell) continue;
                list.Add(v);
            }
        }

        if (forbidEmpty) list.RemoveAll(v => v.IsEmpty);
        return list;
    }

    float EmptyMultiplier(int x,int y,int z) {
        // t = distance normalisée au bord (0 = bord, 1 = centre de l’axe le plus court)
        float dx = Mathf.Min(x, gridSize.x-1-x) / (0.5f*(gridSize.x-1));
        float dy = Mathf.Min(y, gridSize.y-1-y) / (0.5f*(gridSize.y-1));
        float dz = Mathf.Min(z, gridSize.z-1-z) / (0.5f*(gridSize.z-1));
        float t = Mathf.Clamp01(Mathf.Max(dx, Mathf.Max(dy, dz))); // max = plus profond
        return 1f + emptyFalloff * Mathf.Pow(t, falloffExponent);
    }

    Variant GetEmpty() => emptyVariants[0];

    Variant PickWeightedWithContinuation(int x,int y,int z, List<Variant> list) {
        float[] scores = new float[list.Count];
        float total = 0f;
        for (int i=0;i<list.Count;i++) {
            float s = Math.Max(1, list[i].weight);
            int cont = CountConnectionsIfPlaced(x,y,z,list[i]);
            if (cont >= 1) s *= continueBias;
            if (ConnectsStairSide(x,y,z,list[i])) s *= stairBonus;
            scores[i] = s; total += s;
        }
        float r = (float)rng.NextDouble() * total;
        for (int i=0;i<list.Count;i++) { if (r < scores[i]) return list[i]; r -= scores[i]; }
        return list[0];
    }

    int CountConnectionsIfPlaced(int x,int y,int z, Variant v) {
        int c = 0;
        for (int i=0;i<6;i++) {
            var s = v.sockets[i];
            var (nx,ny,nz) = NeighborOf(x,y,z,s.dir);
            if (nx<0||ny<0||nz<0||nx>=gridSize.x||ny>=gridSize.y||nz>=gridSize.z) continue;
            var nb = grid[nx,ny,nz];
            if (nb == null) continue;
            var sb = nb.sockets[(int)Opp(s.dir)];
            if (MakesConnection(s, sb)) c++;
        }
        return c;
    }

    bool ConnectsStairSide(int x,int y,int z, Variant v) {
        for (int i=0;i<6;i++) {
            var s = v.sockets[i];
            if (s.cat.main != "side" || s.cat.sub != "stair") continue;
            var (nx,ny,nz) = NeighborOf(x,y,z,s.dir);
            if (nx<0||ny<0||nz<0||nx>=gridSize.x||ny>=gridSize.y||nz>=gridSize.z) continue;
            var nb = grid[nx,ny,nz];
            if (nb == null) continue;
            var sb = nb.sockets[(int)Opp(s.dir)];
            if (sb.cat.main=="side" && sb.cat.sub=="stair") return true;
        }
        return false;
    }

    bool FitsNeighbors(int x,int y,int z, Variant v) {
        for (int i = 0; i < 6; i++) {
            var s = v.sockets[i];
            var (nx,ny,nz) = NeighborOf(x,y,z,s.dir);
            bool inside = nx>=0 && ny>=0 && nz>=0 && nx<gridSize.x && ny<gridSize.y && nz<gridSize.z;

            if (!inside) {
                if (s.cat.main == "face") return false; // face vers bord interdit
                continue;
            }

            var nb = grid[nx, ny, nz];
            if (nb == null) continue;

            var sb = nb.sockets[(int)Opp(s.dir)];

            // Contraintes strictes demandées
            if (s.cat.main == "void" || sb.cat.main == "void") return false;

            bool aNone = s.cat.main == "none";
            bool bNone = sb.cat.main == "none";
            if (aNone || bNone) {
                if ((aNone && sb.cat.main == "face") || (bNone && s.cat.main == "face")) return false;
                // side vs none OK, none vs none OK
                continue;
            }

            if (!OppCanConnectStrict(s, sb)) return false;
        }
        return true;
    }

    // Validation après placement
    bool ValidateNeighbors(int x,int y,int z) {
        var p = grid[x,y,z];
        if (p == null) return true;
        for (int i=0;i<6;i++) {
            var s = p.sockets[i];
            var (nx,ny,nz) = NeighborOf(x,y,z,s.dir);
            if (nx<0||ny<0||nz<0||nx>=gridSize.x||ny>=gridSize.y||nz>=gridSize.z) {
                if (s.cat.main == "face") return false; // face collée au bord
                continue;
            }
            var nb = grid[nx,ny,nz];
            if (nb == null) continue;
            var sb = nb.sockets[(int)Opp(s.dir)];

            if (s.cat.main == "void" || sb.cat.main == "void") return false;

            bool aNone = s.cat.main == "none";
            bool bNone = sb.cat.main == "none";
            if (aNone || bNone) {
                if ((aNone && sb.cat.main == "face") || (bNone && s.cat.main == "face")) return false;
                continue;
            }
            if (!OppCanConnectStrict(s, sb)) return false;
        }
        return true;
    }

    // ----- règles -----
    static bool OppCanConnectStrict(SocketRot a, SocketRot b) {
        if (a.dir != Opp(b.dir)) return false;
        if (a.cat.main == "face" && b.cat.main == "face") return true;
        if (a.cat.main == "side" && b.cat.main == "side" && a.cat.sub == b.cat.sub && a.cat.sub != "") return true;
        return false;
    }
    static bool MakesConnection(SocketRot a, SocketRot b) {
        if (a.dir != Opp(b.dir)) return false;
        if (a.cat.main == "face" && b.cat.main == "face") return true;
        if (a.cat.main == "side" && b.cat.main == "side" && a.cat.sub == b.cat.sub && a.cat.sub != "") return true;
        return false;
    }
    static Dir Opp(Dir d) => d switch {
        Dir.PX => Dir.NX, Dir.NX => Dir.PX,
        Dir.PY => Dir.NY, Dir.NY => Dir.PY,
        Dir.PZ => Dir.NZ, Dir.NZ => Dir.PZ
    };

    // ----- variantes & sockets -----
    void BuildVariants(TileEntry t) {
        if (t.prefab == null) return;
        if (cacheVariants.ContainsKey(t.prefab)) return;

        var sockets = ExtractLocalSockets(t.prefab);
        var rots = t.allowAllRotations ? CubeRotations() : new List<Quaternion> { Quaternion.identity };
        var variants = new List<Variant>(rots.Count);

        foreach (var q in rots) {
            var rs = new SocketRot[6];
            for (int i = 0; i < 6; i++) rs[i] = new SocketRot { dir = (Dir)i, cat = new SocketCat { main = "none", sub = "" } };
            foreach (var s in sockets) {
                var d = QuantizeDir(q * s.fwdLocal);
                rs[(int)d] = new SocketRot { dir = d, cat = s.cat };
            }
            variants.Add(new Variant { prefab = t.prefab, rot = q, sockets = rs, weight = t.weight });
        }
        cacheVariants[t.prefab] = variants;
    }

    void BuildEmptyVariant() {
        var rs = new SocketRot[6];
        for (int i = 0; i < 6; i++) rs[i] = new SocketRot { dir = (Dir)i, cat = new SocketCat { main = "none", sub = "" } };
        var empty = new Variant { prefab = null, rot = Quaternion.identity, sockets = rs, weight = Math.Max(1, emptyWeight) };
        emptyVariants = new List<Variant> { empty };
    }

    static List<SocketLocal> ExtractLocalSockets(GameObject prefab) {
        var list = new List<SocketLocal>();
        foreach (var tr in prefab.GetComponentsInChildren<Transform>(true)) {
            if (tr == prefab.transform) continue;

            var tag = tr.GetComponent<SocketTag>();
            if (tag != null) {
                list.Add(new SocketLocal {
                    fwdLocal = tr.forward,
                    cat = new SocketCat { main = (tag.main ?? "none").ToLowerInvariant(),
                                          sub  = (tag.sub  ?? "").ToLowerInvariant() }
                });
                continue;
            }

            // fallback: "Socket+X.side.stair"
            var name = tr.name.Trim();
            string catToken = "none";
            int dot = name.IndexOf('.');
            if (dot >= 0 && dot + 1 < name.Length) catToken = name[(dot + 1)..].ToLowerInvariant();
            var parts = catToken.Split('.');
            var cat = new SocketCat { main = parts[0], sub = parts.Length > 1 ? parts[1] : "" };

            if (TryParseDirFromName(name, out var dirFromName)) {
                Vector3 f = dirFromName switch {
                    Dir.PX => Vector3.right,  Dir.NX => Vector3.left,
                    Dir.PY => Vector3.up,     Dir.NY => Vector3.down,
                    Dir.PZ => Vector3.forward,Dir.NZ => Vector3.back
                };
                list.Add(new SocketLocal { fwdLocal = f, cat = cat });
            } else {
                list.Add(new SocketLocal { fwdLocal = tr.forward, cat = cat });
            }
        }
        return list;
    }

    static bool TryParseDirFromName(string name, out Dir dir) {
        dir = Dir.PX;
        int dot = name.IndexOf('.');
        string head = dot >= 0 ? name.Substring(0, dot) : name;
        head = head.Replace(" ", "");
        if (!head.StartsWith("Socket", StringComparison.OrdinalIgnoreCase)) return false;
        if (head.EndsWith("+X", StringComparison.OrdinalIgnoreCase)) { dir = Dir.PX; return true; }
        if (head.EndsWith("-X", StringComparison.OrdinalIgnoreCase)) { dir = Dir.NX; return true; }
        if (head.EndsWith("+Y", StringComparison.OrdinalIgnoreCase)) { dir = Dir.PY; return true; }
        if (head.EndsWith("-Y", StringComparison.OrdinalIgnoreCase)) { dir = Dir.NY; return true; }
        if (head.EndsWith("+Z", StringComparison.OrdinalIgnoreCase)) { dir = Dir.PZ; return true; }
        if (head.EndsWith("-Z", StringComparison.OrdinalIgnoreCase)) { dir = Dir.NZ; return true; }
        return false;
    }

    static Dir QuantizeDir(Vector3 v) {
        v.Normalize();
        float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
        if (ax >= ay && ax >= az) return v.x >= 0 ? Dir.PX : Dir.NX;
        if (ay >= ax && ay >= az) return v.y >= 0 ? Dir.PY : Dir.NY;
        return v.z >= 0 ? Dir.PZ : Dir.NZ;
    }

    static List<Quaternion> CubeRotations() {
        var res = new List<Quaternion>(24);
        Vector3[] axes = { Vector3.right, -Vector3.right, Vector3.up, -Vector3.up, Vector3.forward, -Vector3.forward };
        foreach (var f in axes) {
            foreach (var u in axes) {
                if (Mathf.Abs(Vector3.Dot(f, u)) > 0.001f) continue;
                var q = Quaternion.LookRotation(f, u);
                bool dup = false;
                foreach (var q2 in res) if (Quaternion.Dot(q, q2) > 0.9999f) { dup = true; break; }
                if (!dup) res.Add(q);
            }
        }
        return res;
    }

    // ---------- logs ----------
    void Log(string msg) { if (!enableLogs) return; if (logCount++ >= logLimit) return; Debug.Log($"[PMG] {msg}"); }
    void LogWarn(string msg) { if (!enableLogs) return; if (logCount++ >= logLimit) return; Debug.LogWarning($"[PMG] {msg}"); }
    string NeighborSignature(int x,int y,int z) {
        string SigFor(Dir d, int nx,int ny,int nz) {
            if (nx<0||ny<0||nz<0||nx>=gridSize.x||ny>=gridSize.y||nz>=gridSize.z) return $"{d}:(OUT)";
            var nb = grid[nx,ny,nz];
            if (nb == null) return $"{d}:(?)";
            var sb = nb.sockets[(int)d];
            return $"{d}:{sb.cat.main}.{sb.cat.sub}";
        }
        return $"{SigFor(Dir.NX,x-1,y,z)} | {SigFor(Dir.PX,x+1,y,z)} | {SigFor(Dir.NY,x,y-1,z)} | {SigFor(Dir.PY,x,y+1,z)} | {SigFor(Dir.NZ,x,y,z-1)} | {SigFor(Dir.PZ,x,y,z+1)}";
    }

    // ---------- gizmos ----------
    void OnDrawGizmosSelected() {
        if (root == null) return;
        Gizmos.matrix = Matrix4x4.identity;
        foreach (Transform t in root) {
            foreach (Transform c in t.GetComponentsInChildren<Transform>()) {
                var tag = c.GetComponent<SocketTag>();
                string name = tag != null ? $"{tag.main}.{tag.sub}" : c.name.ToLowerInvariant();
                if (!(name.StartsWith("face") || name.StartsWith("side") || name.StartsWith("void") || name.StartsWith("none"))) continue;
                Gizmos.DrawRay(c.position, c.forward * 0.5f);
            }
        }
    }
}
