using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public enum ModuleType
{
    Structure,  // Cube, Slab...
    Stair,      // Escaliers
    Transition, // Arches simples
    MacroStruct // Grandes structures (Arche pont, etc.)
}

public class Module : MonoBehaviour
{
    [Header("Configuration")]
    public ModuleType type;
    
    [Header("Multi-Blocs (Encombrement)")]
    [Tooltip("Liste des cases occupées RELATIVEMENT au pivot (0,0,0). Ex: (0,0,0), (0,1,0)...")]
    public List<Vector3Int> occupiedOffsets = new List<Vector3Int> { Vector3Int.zero };

    [HideInInspector] public List<SocketTag> allSockets;

    void Awake()
    {
        allSockets = GetComponentsInChildren<SocketTag>().ToList();
    }

    // Fonction utilitaire pour l'éditeur : Visualiser l'encombrement
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1, 0, 0, 0.5f);
        foreach(var offset in occupiedOffsets)
        {
            // On transforme l'offset local en position monde en tenant compte de la rotation de l'objet
            Vector3 worldOffset = transform.rotation * (Vector3)offset * 2.0f; // *2.0f car gridSize = 2
            Gizmos.DrawCube(transform.position + worldOffset, Vector3.one * 1.9f);
        }
    }

    public SocketTag GetRandomOpenSocket()
    {
        if (allSockets == null || allSockets.Count == 0) return null;
        // Pas de Raycast ici comme convenu
        return allSockets[Random.Range(0, allSockets.Count)];
    }
}