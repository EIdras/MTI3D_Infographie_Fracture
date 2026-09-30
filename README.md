# Fracture - Unity HDRP Lighting Showcase

<p align="center">
  <video src="https://github.com/user-attachments/assets/118bce7a-fa1e-405b-89e4-d7cbdb061f3c" autoplay loop muted playsinline width="100%"></video>
</p>

> Projet individuel réalisé dans le cadre d'un cours d'éclairage temps réel.  
> **Thématique :** *« Du ciel aux abysses : exploration de nouveaux plans d'existence et dépassement des limites perceptuelles. »*

---

## 🎯 Aperçu

Ce projet a été conçu et produit intégralement en solo, couvrant l'ensemble du pipeline 3D et temps réel : de la recherche d'intention visuelle jusqu'au compositing final et au rendu sous Unity HDRP.

L'objectif principal était de traduire visuellement la transition entre l'aérien et l'abyssal à travers une narration portée par la lumière, les volumes et l'ambiance atmosphérique.

---

## 🛠️️ Pipeline et réalisation

### 1. Préparation, DA
* **Recherche & Intention :** Moodboard, notes d'intention visuelle et définition de la palette de couleurs.
* **Composition de la scène :** Définition des points focaux, des lignes directrices et du cadrage de la caméra cinématique.

### 2. Modélisation, Unwrap, Texturing (Blender, Adobe Substance 3D)
* **Maillages :** Création des assets 3D de la scène (géométrie optimisée pour le temps réel).
* **Dépliage UV (Unwrap) :** Gestion propre des coutures et optimisation du placement dans l'espace UV.
* **Texturing & Shading :** Textures PBR, gestion de la texel density et préparation des matériaux pour le rendu physically based.

### 3. Intégration, Scénographie (Unity HDRP)
* **Organisation du projet :** Respect des conventions de nommage, hiérarchie de scène claire et séparation modulaire des éléments.
* **Intégration moteur :** Setup des prefabs, collisionneurs et placement spatial des assets.
* **Génération procédurale :** Script paramétrable permettant la génération de structures complexes (Démo d'une partie de ce dernier ci-dessous)
<img width="400" height="243" alt="gen_preview" src="https://github.com/user-attachments/assets/4982baa9-61c0-4b45-9f92-0b38de3291e1" />

### 4. Lighting & Rendu Temps Réel
* **Setup HDRP :** Éclairage directionnel, lumières ponctuelles/spots avec attenuation physically-based.
* **Brouillard volumétrique & Atmosphère :** Utilisation des volumes HDRP pour créer la profondeur abyssale et les faisceaux lumineux (god rays).
* **Post-processing & Compositing :** Color grading, bloom, tone mapping, exposition et effets de caméra.
* **Scripting :** Mise en place d'animations/trajectoires de caméra et déclencheurs d'ambiance.

---

## 💻 Spécifications Techniques

* **Moteur :** Unity (High Definition Render Pipeline - HDRP)
* **Outils :** Blender, Adobe Substance 3D Sampler & Painter, Photoshop
* **Pipeline Matériaux :** PBR (Albedo, Normal, Mask/Metallic/Smoothness, AO)
* **Format Démo :** Cinématique temps réel capturée in-engine
