using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public enum ModuleType
{
    Structure,  // Cube, Slab, Mur simple...
    Stair,      // Escaliers
    Transition, // Arches décoratives
    MacroStruct,// Grosses structures
    Portal,     // Portails (Nécessite ancrage double strict)
    Obelisk     // Fin de chaîne (Terminal)
}

public class Module : MonoBehaviour
{
    [Header("Configuration")]
    public ModuleType type;
    
    [Header("Emplacement & Collisions")]
    [Tooltip("Liste des cases occupées par l'objet (relatif au pivot). IMPORTANT : Utiliser des entiers.")]
    public List<Vector3Int> occupiedOffsets = new List<Vector3Int> { Vector3Int.zero };

    [Header("Contraintes Spéciales (Portails)")]
    [Tooltip("Si coché, l'ancre DOIT toucher un bloc existant. Idéal pour les Portails.")]
    public bool strictAnchorCheck = false; 
    
    [Tooltip("Positions relatives qui doivent être valides (ou toucher un bloc en mode strict).")]
    public List<Vector3Int> requiredAnchors = new List<Vector3Int>();

    [HideInInspector] public List<SocketTag> allSockets;

    void Awake()
    {
        // Récupère tous les sockets, même désactivés
        allSockets = GetComponentsInChildren<SocketTag>(true).ToList();
    }

    public SocketTag GetRandomOpenSocket()
    {
        if (allSockets == null || allSockets.Count == 0) return null;
        return allSockets[Random.Range(0, allSockets.Count)];
    }

    // Visualisation dans l'éditeur
    void OnDrawGizmosSelected()
    {
        // Encombrement (ROUGE)
        Gizmos.color = new Color(1, 0, 0, 0.4f);
        foreach(var offset in occupiedOffsets)
        {
            Vector3 worldOffset = transform.rotation * (Vector3)offset * 2.0f; 
            Gizmos.DrawCube(transform.position + worldOffset, Vector3.one * 1.9f);
        }

        // Ancres (VERT)
        Gizmos.color = new Color(0, 1, 0, 0.8f);
        foreach(var anchor in requiredAnchors)
        {
            Vector3 worldOffset = transform.rotation * (Vector3)anchor * 2.0f;
            Gizmos.DrawWireCube(transform.position + worldOffset, Vector3.one * 1.0f);
        }
    }
}