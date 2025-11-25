using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public enum ModuleType
{
    Structure,  // Cube, Slab...
    Stair,      // Escaliers
    Transition  // Arche, coin...
}

public class Module : MonoBehaviour
{
    [Header("Configuration")]
    public ModuleType type; // <-- IMPORTANT : Défini ça dans l'inspecteur pour chaque prefab !
    
    [HideInInspector] public List<SocketTag> allSockets;

    void Awake()
    {
        allSockets = GetComponentsInChildren<SocketTag>().ToList();
    }

// Dans Module.cs

    public SocketTag GetRandomOpenSocket()
    {
        if (allSockets == null || allSockets.Count == 0) return null;
        var shuffledSockets = allSockets.OrderBy(x => Random.value).ToList();

        foreach (var socket in shuffledSockets)
        {
            // --- MODIFICATION ICI : ON DÉSACTIVE LE RAYCAST ---
            // On commente ces lignes pour que le socket soit toujours considéré comme libre.
            // Le système de Grille du générateur gérera les collisions plus tard.
            
            // if (!Physics.Raycast(socket.transform.position, socket.transform.forward, 1.0f))
            // {
            return socket;
            // }
            // --------------------------------------------------
        }
        // Si on a commenté le if, cette ligne ne sera jamais atteinte s'il y a des sockets.
        return allSockets.Count > 0 ? shuffledSockets[0] : null; 
    }
    
    // Pour visualiser dans l'éditeur
    // void OnDrawGizmosSelected()
    // {
    //     if (allSockets == null) allSockets = GetComponentsInChildren<SocketTag>().ToList();
    //     foreach (var s in allSockets)
    //     {
    //         if(s==null) continue;
    //         Gizmos.color = type == ModuleType.Stair ? Color.cyan : Color.blue;
    //         Gizmos.DrawLine(s.transform.position, s.transform.position + s.transform.forward * 0.4f);
    //     }
    // }
}