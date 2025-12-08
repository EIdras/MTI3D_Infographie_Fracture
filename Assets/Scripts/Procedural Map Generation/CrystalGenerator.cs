namespace Procedural_Map_Generation
{
    using UnityEngine;
    using System.Collections.Generic;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    public class CrystalGenerator : MonoBehaviour
    {
        public GameObject crystalPrefab; 
        [Range(0, 1)] public float density = 0.05f; 
        public int checkRadius = 3; 

        public void GenerateCrystals(EscherVoidGenerator generator)
        {
            Debug.Log("Génération des cristaux...");
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

                // Vérification logique (si la case est occupée par la structure)
                if (generator.IsCellOccupied(pos)) continue;

                // Vérification physique (espace vide autour)
                if (IsSpaceEmpty(pos, checkRadius, generator.gridSize))
                {
                    if (Random.value < density)
                    {
#if UNITY_EDITOR
                        GameObject crystal = (GameObject)PrefabUtility.InstantiatePrefab(crystalPrefab, generator.transform);
#else
                        GameObject crystal = Instantiate(crystalPrefab, generator.transform);
#endif
                        
                        crystal.transform.position = new Vector3(pos.x * generator.gridSize, pos.y * generator.gridSize, pos.z * generator.gridSize);
                        crystal.transform.rotation = Random.rotation;
                        
                        float scale = Random.Range(80f, 130f);
                        crystal.transform.localScale = Vector3.one * scale;

                        // NOMENCLATURE IMPORTANTE POUR LE MESH COMBINER
                        crystal.name = $"CRYSTAL_{count}";

                        count++;
                    }
                }
            }

            Debug.Log($"Cristaux générés : {count}");
        }

        bool IsSpaceEmpty(Vector3Int center, int radius, float gridSize)
        {
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