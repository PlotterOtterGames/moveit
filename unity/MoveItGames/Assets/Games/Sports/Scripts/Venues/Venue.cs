using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Sports
{
    /// <summary>
    /// One of Sporte!'s venues (art/sports/venues.py) placed in the world: its pieces get their materials (the ground
    /// its detail texture, the ad boards their scrolling strip, the big screen its picture), the stands their crowd
    /// (<see cref="Fans"/>). Everything under one root the activity destroys with its world.
    /// </summary>
    public sealed class Venue
    {
        public enum Kind { Football, Track, Beach }

        public Transform Root { get; private set; }
        public Fans Fans { get; private set; }
        public Renderer Screen { get; private set; }
        Material _ground, _screen;

        public static Venue Load(Kind kind, Transform parent, int seed = 1, float occupancy = 0.85f, Material water = null)
        {
            var art = SportsArt.Instance;
            var v = new Venue();
            if (art == null)
            {
                Debug.LogError("[Venue] SportsArt not registered");
                v.Root = new GameObject("Venue").transform;
                v.Root.SetParent(parent, false);
                return v;
            }
            GameObject prefab;
            TextAsset seats;
            Texture2D detail;
            switch (kind)
            {
                case Kind.Track: prefab = art.track; seats = art.trackSeats; detail = art.trackDetail; break;
                case Kind.Beach: prefab = art.beach; seats = art.beachSeats; detail = art.sandDetail; break;
                default: prefab = art.football; seats = art.footballSeats; detail = art.grassDetail; break;
            }
            var go = prefab != null ? Object.Instantiate(prefab, parent, false) : new GameObject("Venue");
            if (go.transform.parent != parent) go.transform.SetParent(parent, false);
            go.name = $"Venue {kind}";
            v.Root = go.transform;
            v._ground = new Material(art.ground);
            v._ground.SetTexture("_BaseMap", detail);
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                switch (r.name)
                {
                    case "Ground":
                        r.sharedMaterial = v._ground;
                        r.shadowCastingMode = ShadowCastingMode.Off;
                        break;
                    case "Lines":
                        r.sharedMaterial = art.world;
                        r.shadowCastingMode = ShadowCastingMode.Off;
                        break;
                    case "Water":
                        r.sharedMaterial = water != null ? water : art.world;
                        r.shadowCastingMode = ShadowCastingMode.Off;
                        break;
                    case "Boards":
                        r.sharedMaterial = art.boards;
                        r.shadowCastingMode = ShadowCastingMode.On;
                        break;
                    case "Screen":
                        v._screen = new Material(art.screen);
                        r.sharedMaterial = v._screen;
                        v.Screen = r;
                        break;
                    default:
                        r.sharedMaterial = art.world;
                        r.shadowCastingMode = ShadowCastingMode.On;
                        break;
                }
                r.receiveShadows = true;
            }
            if (seats != null && art.crowd != null) v.Fans = Fans.Build(v.Root, seats.text, art.crowd, seed, occupancy);
            go.AddComponent<Owner>().Venue = v;
            return v;
        }

        /// <summary>Shows a picture on the big screen (null: the venue's own).</summary>
        public void ShowOnScreen(Texture texture)
        {
            if (_screen != null) _screen.SetTexture("_BaseMap", texture != null ? texture : SportsArt.Instance.screen.GetTexture("_BaseMap"));
        }

        public void Dispose()
        {
            if (_ground != null) Object.Destroy(_ground);
            if (_screen != null) Object.Destroy(_screen);
            _ground = _screen = null;
            Shader.SetGlobalFloat("_CrowdCheer", 0f);
        }

        /// <summary>Frees the venue's own materials when its world goes.</summary>
        sealed class Owner : MonoBehaviour
        {
            public Venue Venue;
            void OnDestroy() => Venue?.Dispose();
        }

        /// <summary>Parses "x y z yaw" lines (# comments).</summary>
        public static System.Collections.Generic.List<Vector4> ParseSeats(string text)
        {
            var list = new System.Collections.Generic.List<Vector4>();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split(' ');
                if (p.Length < 4) continue;
                list.Add(new Vector4(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture),
                                     float.Parse(p[2], CultureInfo.InvariantCulture), float.Parse(p[3], CultureInfo.InvariantCulture)));
            }
            return list;
        }
    }
}
