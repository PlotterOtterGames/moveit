using System;
using UnityEngine;

namespace MoveIt.Games.Sports
{
    /// <summary>
    /// Sporte!'s art made outside Unity (art/sports/venues.py, crowd.py; tools/sports/textures.py) and the materials the
    /// scene builder made for it, handed over by <see cref="SportsGame"/> once.
    /// </summary>
    [Serializable]
    public sealed class SportsArt
    {
        public static SportsArt Instance { get; private set; }

        [Tooltip("Assets/Games/Sports/Art/Venues/*.fbx")] public GameObject football, track, beach;
        [Tooltip("The venues' seats (one a line: x y z yaw) for the crowd.")] public TextAsset footballSeats, trackSeats, beachSeats;
        [Tooltip("Tundu/Toon, vertex colours: the venues' structures, seats and lines.")] public Material world;
        [Tooltip("Tundu/Toon, vertex colours and a tiling detail texture: the grass, the track, the sand.")] public Material ground;
        public Texture2D grassDetail, trackDetail, sandDetail;
        [Tooltip("Tundu/Screen with the ad strip, scrolling.")] public Material boards;
        [Tooltip("Tundu/Screen with the big screen's picture.")] public Material screen;
        [Tooltip("Tundu/Net.")] public Material net;
        [Tooltip("Tundu/Crowd with crowd_atlas.png.")] public Material crowd;

        public static void Register(SportsArt art) => Instance = art;
    }
}
