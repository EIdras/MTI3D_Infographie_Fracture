namespace Procedural_Map_Generation
{
    using UnityEngine;
    using System.Collections.Generic;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    public class CrystalGenerator : MonoBehaviour
    {
        public GameObject crystalPrefab; // Ton prefab wireframe avec lumière
        [Range(0, 1)] public float density = 0.05f; // Rareté
        public int checkRadius = 3; // Rayon de vide nécessaire (en blocs)

        public void GenerateCrystals(EscherVoidGenerator generator)
        {
            // On récupère la map des cases occupées du générateur (il faut passer occupiedCells en public ou faire un getter)
            // Pour simplifier ici, on va scanner physiquement ou supposer qu'on a accès à la liste.
            // L'idéal est d'ajouter un getter dans EscherVoidGenerator : public HashSet<Vector3Int> GetOccupiedCells() { return occupiedCells; }

            Debug.Log("Génération des cristaux...");
            // Hack simple : On parcourt un volume aléatoire
            int count = 0;
            int maxAttempts = 5000;
            Vector3Int mapSize = generator.mapSize;

            for (int i = 0; i < maxAttempts; i++)
            {
                Vector3Int pos = new Vector3Int(
                    Random.Range(-mapSize.x / 2, mapSize.x / 2),
                    Random.Range(-mapSize.y / 2, mapSize.y / 2),
                    Random.Range(-mapSize.z / 2, mapSize.z / 2)
                );

                if (IsSpaceEmpty(pos, checkRadius, generator.gridSize))
                {
                    if (Random.value < density)
                    {
                        GameObject crystal =
                            (GameObject)PrefabUtility.InstantiatePrefab(crystalPrefab, generator.transform);
                        crystal.transform.position = new Vector3(pos.x * generator.gridSize, pos.y * generator.gridSize,
                            pos.z * generator.gridSize);

                        // Randomisation
                        crystal.transform.rotation = Random.rotation;
                        float scale = Random.Range(80f, 130f);
                        crystal.transform.localScale = Vector3.one * scale;

                        count++;
                    }
                }
            }

            Debug.Log($"Cristaux générés : {count}");
        }

        bool IsSpaceEmpty(Vector3Int center, int radius, float gridSize)
        {
            // On utilise Physics.CheckSphere pour être sûr de ne pas toucher de géométrie existante
            // Attention : tes blocs doivent avoir des Colliders pour que ça marche en Editor mode !
            return !Physics.CheckSphere(
                new Vector3(center.x * gridSize, center.y * gridSize, center.z * gridSize),
                radius * gridSize * 0.8f
            );
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(CrystalGenerator))]
    public class CrystalGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            CrystalGenerator script = (CrystalGenerator)target;
            EscherVoidGenerator gen = script.GetComponent<EscherVoidGenerator>();

            if (GUILayout.Button("GÉNÉRER CRISTAUX"))
            {
                if (gen != null) script.GenerateCrystals(gen);
            }
        }
    }
#endif
}