using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Fly
{
    /// <summary>Tiles of the city texture array (tools/fly/city_textures.py, in the same order).</summary>
    public enum Slice
    {
        Plain, Apartment, Office, Classic, Balcony, Shop, Cafe, Brick,
        Roof, Road, Crossroads, Pavement, Grass, Cobbles, Water, Leaves,
    }

    /// <summary>
    /// Vertex lists for one city block, in the layout Tundu/City reads: position, normal, colour (rgb paints walls, a
    /// darkens), uv (repeating tile coordinates) and uv2.x (which tile). Flat-shaded quads, triangles and simple solids.
    /// Reused between blocks (no garbage). Front faces are given bottom-left, top-left, top-right, bottom-right as seen
    /// from the front (Unity's clockwise winding).
    /// </summary>
    public sealed class CityMesh
    {
        public readonly List<Vector3> Positions = new List<Vector3>(16384);
        public readonly List<Vector3> Normals = new List<Vector3>(16384);
        public readonly List<Color32> Colors = new List<Color32>(16384);
        public readonly List<Vector2> Uvs = new List<Vector2>(16384);
        public readonly List<Vector2> Tiles = new List<Vector2>(16384);
        public readonly List<int> Indices = new List<int>(24576);

        public int VertexCount => Positions.Count;

        public void Clear()
        {
            Positions.Clear();
            Normals.Clear();
            Colors.Clear();
            Uvs.Clear();
            Tiles.Clear();
            Indices.Clear();
        }

        void Vertex(Vector3 p, Vector3 n, Color32 c, Vector2 uv, Slice s)
        {
            Positions.Add(p);
            Normals.Add(n);
            Colors.Add(c);
            Uvs.Add(uv);
            Tiles.Add(new Vector2((float)s, 0f));
        }

        /// <summary>A quad a (bottom-left), b (top-left), c (top-right), d (bottom-right); uv0 at a, uv1 at c.
        /// <paramref name="bottomShade"/> darkens a and d (0..255, 255 = none).</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uv0, Vector2 uv1, Slice s, Color32 color,
                         byte bottomShade = 255)
        {
            var n = Vector3.Cross(b - a, d - a).normalized;
            int i = Positions.Count;
            var low = color;
            low.a = (byte)(color.a * bottomShade / 255);
            Vertex(a, n, low, uv0, s);
            Vertex(b, n, color, new Vector2(uv0.x, uv1.y), s);
            Vertex(c, n, color, uv1, s);
            Vertex(d, n, low, new Vector2(uv1.x, uv0.y), s);
            Indices.Add(i);
            Indices.Add(i + 1);
            Indices.Add(i + 2);
            Indices.Add(i);
            Indices.Add(i + 2);
            Indices.Add(i + 3);
        }

        /// <summary><see cref="Quad"/>, mirrored left-right if needed so its front faces <paramref name="outward"/>.</summary>
        public void QuadFacing(Vector3 outward, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uv0, Vector2 uv1, Slice s,
                               Color32 color)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, d - a), outward) < 0f) Quad(d, c, b, a, uv0, uv1, s, color);
            else Quad(a, b, c, d, uv0, uv1, s, color);
        }

        /// <summary><see cref="Triangle"/>, turned if needed so its front faces <paramref name="outward"/>.</summary>
        public void TriangleFacing(Vector3 outward, Vector3 a, Vector3 b, Vector3 c, Slice s, Color32 color)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) (b, c) = (c, b);
            Triangle(a, b, c, Vector2.zero, new Vector2(0.5f, 1f), Vector2.right, s, color);
        }

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector2 uvA, Vector2 uvB, Vector2 uvC, Slice s, Color32 color)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            int i = Positions.Count;
            Vertex(a, n, color, uvA, s);
            Vertex(b, n, color, uvB, s);
            Vertex(c, n, color, uvC, s);
            Indices.Add(i);
            Indices.Add(i + 1);
            Indices.Add(i + 2);
        }

        /// <summary>A vertical wall from <paramref name="from"/> to <paramref name="to"/> (on the ground, left to right
        /// seen from the front), y0..y1; the tile repeats every <paramref name="tileW"/> x <paramref name="tileH"/>
        /// metres, snapped to whole tiles so windows are never cut at a corner.</summary>
        public void Wall(Vector3 from, Vector3 to, float y0, float y1, Slice s, Color32 color, float tileW, float tileH,
                         byte bottomShade = 255, bool snap = true, float v0 = 0f)
        {
            float length = Vector3.Distance(from, to);
            float u = snap ? Mathf.Max(1f, Mathf.Round(length / tileW)) : length / tileW;
            float v = snap ? Mathf.Max(1f, Mathf.Round((y1 - y0) / tileH)) : (y1 - y0) / tileH;
            Quad(new Vector3(from.x, y0, from.z), new Vector3(from.x, y1, from.z), new Vector3(to.x, y1, to.z),
                 new Vector3(to.x, y0, to.z), new Vector2(0f, v0), new Vector2(u, v0 + v), s, color, bottomShade);
        }

        /// <summary>A flat top at height y over [x0,x1] x [z0,z1], the tile repeating every <paramref name="tile"/> m.</summary>
        public void Top(float x0, float z0, float x1, float z1, float y, Slice s, Color32 color, float tile)
        {
            Quad(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0),
                 new Vector2(x0 / tile, z0 / tile), new Vector2(x1 / tile, z1 / tile), s, color);
        }

        /// <summary>A box without a bottom: the four sides with <paramref name="side"/>, the top with
        /// <paramref name="top"/> (tiles in metres: one tile per <paramref name="tile"/> m).</summary>
        public void Box(Vector3 min, Vector3 max, Slice side, Slice top, Color32 sideColor, Color32 topColor, float tile = 1f,
                        byte bottomShade = 255)
        {
            Vector3 a = new Vector3(min.x, 0f, min.z), b = new Vector3(max.x, 0f, min.z);
            Vector3 c = new Vector3(max.x, 0f, max.z), d = new Vector3(min.x, 0f, max.z);
            Wall(a, b, min.y, max.y, side, sideColor, tile, tile, bottomShade, false);   // south
            Wall(b, c, min.y, max.y, side, sideColor, tile, tile, bottomShade, false);   // east
            Wall(c, d, min.y, max.y, side, sideColor, tile, tile, bottomShade, false);   // north
            Wall(d, a, min.y, max.y, side, sideColor, tile, tile, bottomShade, false);   // west
            Top(min.x, min.z, max.x, max.z, max.y, top, topColor, tile);
        }

        /// <summary>A prism with <paramref name="sides"/> faces around (x, z), y0..y1, with a flat top.</summary>
        public void Prism(Vector3 centre, float radius, float y0, float y1, int sides, Slice s, Color32 color, bool cap = true)
        {
            for (int k = 0; k < sides; k++)
            {
                // the angle grows from +x toward +z: seen from outside, p0 is on the left
                float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
                var p0 = centre + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
                var p1 = centre + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
                Wall(p0, p1, y0, y1, s, color, 1f, 1f, 255, false);
                if (cap)
                    Triangle(new Vector3(centre.x, y1, centre.z), new Vector3(p1.x, y1, p1.z), new Vector3(p0.x, y1, p0.z),
                             Vector2.zero, Vector2.right, Vector2.up, s, color);
            }
        }

        /// <summary>A cone (roof of a water tower, a tree top) over a prism's top.</summary>
        public void Cone(Vector3 centre, float radius, float y0, float height, int sides, Slice s, Color32 color)
        {
            var tip = new Vector3(centre.x, y0 + height, centre.z);
            for (int k = 0; k < sides; k++)
            {
                float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
                var p0 = new Vector3(centre.x + Mathf.Cos(a0) * radius, y0, centre.z + Mathf.Sin(a0) * radius);
                var p1 = new Vector3(centre.x + Mathf.Cos(a1) * radius, y0, centre.z + Mathf.Sin(a1) * radius);
                Triangle(p0, tip, p1, Vector2.zero, new Vector2(0.5f, 1f), new Vector2(1f, 0f), s, color);
            }
        }

        static readonly Vector3[] Ico = BuildIcosahedron();
        static readonly int[] IcoFaces =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };

        static Vector3[] BuildIcosahedron()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < v.Length; i++) v[i] = v[i].normalized;
            return v;
        }

        /// <summary>A faceted blob (a tree crown, a bush): an icosahedron scaled to <paramref name="size"/>, its lower
        /// faces a little darker. <paramref name="jitter"/> (0..1) dents it so no two look alike.</summary>
        public void Blob(Vector3 centre, Vector3 size, Slice s, Color32 color, System.Random rng, float jitter = 0.18f)
        {
            var bent = _blob;
            for (int i = 0; i < Ico.Length; i++)
                bent[i] = Vector3.Scale(Ico[i] * (1f + ((float)rng.NextDouble() - 0.5f) * jitter * 2f), size * 0.5f) + centre;
            for (int f = 0; f < IcoFaces.Length; f += 3)
            {
                // the face list is clockwise seen from outside in Unity's (left-handed) space
                Vector3 a = bent[IcoFaces[f]], b = bent[IcoFaces[f + 1]], c = bent[IcoFaces[f + 2]];
                float lowness = Mathf.Clamp01(0.5f - ((a.y + b.y + c.y) / 3f - centre.y) / size.y);
                var shaded = color;
                shaded.a = (byte)(255 - 70 * lowness);
                Triangle(a, b, c, new Vector2(a.x, a.y) * 0.4f, new Vector2(b.x, b.y) * 0.4f, new Vector2(c.x, c.z) * 0.4f, s, shaded);
            }
        }

        readonly Vector3[] _blob = new Vector3[12];
    }

    /// <summary>
    /// Builds one city cell into a <see cref="CityMesh"/>, in the cell's own coordinates (its crossroads at the origin,
    /// x east, z north): the crossroads with its zebra crossings, the street segments north and east of it with pavements,
    /// lamps and trees (or, where <see cref="FlyRoute"/> says a segment is missing, a tall building that closes the
    /// street), and the block north-east of it: houses all around (a few with shops, balconies, glass offices, gable
    /// roofs, water towers) or a park with a pond. The same cell always looks the same (seeded by the route).
    /// </summary>
    public sealed class CityBuilder
    {
        public const float Road = 9f, Curb = 0.18f, FloorH = 3.2f, GroundH = 3.8f, BayW = 3.2f;

        static readonly Color32 White = new Color32(255, 255, 255, 255);
        static readonly Color32[] Walls =
        {
            new Color32(255, 200, 96, 255), new Color32(255, 146, 124, 255), new Color32(124, 192, 240, 255),
            new Color32(172, 150, 255, 255), new Color32(146, 214, 128, 255), new Color32(246, 226, 192, 255),
            new Color32(255, 176, 98, 255), new Color32(236, 236, 242, 255), new Color32(190, 210, 226, 255),
            new Color32(255, 178, 196, 255), new Color32(110, 206, 196, 255),
        };
        static readonly Color32[] Accents =
        {
            new Color32(255, 92, 80, 255), new Color32(52, 190, 240, 255), new Color32(108, 206, 104, 255),
            new Color32(128, 104, 255, 255), new Color32(255, 196, 60, 255),
        };
        static readonly Color32 Cornice = new Color32(246, 242, 234, 255);
        static readonly Color32 RoofGray = new Color32(186, 184, 182, 255);
        static readonly Color32 RoofTile = new Color32(214, 116, 88, 255);
        static readonly Color32 Metal = new Color32(96, 102, 118, 255);
        static readonly Color32 Lamp = new Color32(255, 238, 196, 255);
        static readonly Color32 Curbstone = new Color32(178, 178, 176, 255);
        static readonly Color32 Trunk = new Color32(132, 92, 64, 255);
        static readonly Color32[] Greens =
        {
            new Color32(120, 196, 92, 255), new Color32(96, 176, 84, 255), new Color32(146, 206, 96, 255),
        };

        readonly FlyRoute _route;
        readonly CityMesh _segment = new CityMesh();
        float P => _route.Pitch;
        float W => _route.StreetWidth;

        public CityBuilder(FlyRoute route) => _route = route;

        public void Build(CityMesh m, int i, int j)
        {
            m.Clear();
            var rng = new System.Random((int)(_route.Hash01(i, j, 10) * int.MaxValue));
            Crossroads(m, i, j);
            Segment(m, rng, i, j, northSouth: true);
            Segment(m, rng, i, j, northSouth: false);
            Block(m, rng, i, j);
        }

        static float Range(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
        static T Pick<T>(System.Random r, T[] items) => items[r.Next(items.Length)];

        // ---------------------------------------------------------------- streets

        void Crossroads(CityMesh m, int i, int j)
        {
            float h = W * 0.5f, r = Road * 0.5f;
            m.Quad(new Vector3(-h, 0f, -h), new Vector3(-h, 0f, h), new Vector3(h, 0f, h), new Vector3(h, 0f, -h),
                   Vector2.zero, Vector2.one, Slice.Crossroads, White);
            // pavement at the four corners, and across the mouth of a street that isn't there
            foreach (float sx in Sides)
            foreach (float sz in Sides)
                Pavement(m, Mathf.Min(sx * r, sx * h), Mathf.Min(sz * r, sz * h), Mathf.Max(sx * r, sx * h), Mathf.Max(sz * r, sz * h));
            if (!_route.Street(i, j, 0)) Pavement(m, -r, r, r, h);
            if (!_route.Street(i, j, 1)) Pavement(m, r, -r, h, r);
            if (!_route.Street(i, j, 2)) Pavement(m, -r, -h, r, -r);
            if (!_route.Street(i, j, 3)) Pavement(m, -h, -r, -r, r);
        }

        void Pavement(CityMesh m, float x0, float z0, float x1, float z1)
        {
            m.Box(new Vector3(x0, 0f, z0), new Vector3(x1, Curb, z1), Slice.Plain, Slice.Pavement, Curbstone, White, 3f);
        }

        /// <summary>The street segment north (or east) of this cell's crossroads, with its pavements and lamps; or,
        /// if the route has no street there, a long tall building that closes it.</summary>
        void Segment(CityMesh m, System.Random rng, int i, int j, bool northSouth)
        {
            float h = W * 0.5f, r = Road * 0.5f, a = h, b = P - h;
            bool exists = northSouth ? _route.NorthSouth(i, j) : _route.EastWest(i, j);
            // build it as if it ran north (x across, z along), then turn east-west ones by swapping axes
            var local = _segment;
            local.Clear();
            if (exists)
            {
                local.Quad(new Vector3(-r, 0f, a), new Vector3(-r, 0f, b), new Vector3(r, 0f, b), new Vector3(r, 0f, a),
                           new Vector2(0f, a / 6f), new Vector2(1f, b / 6f), Slice.Road, White);
                Pavement(local, -h, a, -r, b);
                Pavement(local, r, a, h, b);
                for (float z = a + 8f, k = 0; z < b - 4f; z += 16f, k++)
                {
                    float side = k % 2 == 0 ? -1f : 1f;
                    StreetLamp(local, new Vector3(side * (r + 0.6f), Curb, z), -side);
                    if (rng.NextDouble() < 0.75) Tree(local, rng, new Vector3(-side * (r + 1.2f), Curb, z + Range(rng, -2f, 2f)), 0.85f);
                }
            }
            else
            {
                Pavement(local, -h, a, h, b);
                float height = Range(rng, 20f, 27f);
                Building(local, rng, -h + 1f, a + 2f, h - 1f, b - 2f, height, landmark: true);
            }
            Append(m, local, northSouth);
        }

        static void Append(CityMesh into, CityMesh part, bool northSouth)
        {
            int start = into.VertexCount;
            for (int k = 0; k < part.VertexCount; k++)
            {
                var p = part.Positions[k];
                var n = part.Normals[k];
                // north-south: as built. east-west: (x, z) -> (z, -x) turns the street to run east (mirrored winding fixed below)
                into.Positions.Add(northSouth ? p : new Vector3(p.z, p.y, p.x));
                into.Normals.Add(northSouth ? n : new Vector3(n.z, n.y, n.x));
                into.Colors.Add(part.Colors[k]);
                into.Uvs.Add(part.Uvs[k]);
                into.Tiles.Add(part.Tiles[k]);
            }
            // swapping x and z mirrors the geometry: reverse each triangle to keep the faces pointing out
            for (int k = 0; k < part.Indices.Count; k += 3)
            {
                into.Indices.Add(start + part.Indices[k]);
                into.Indices.Add(start + part.Indices[northSouth ? k + 1 : k + 2]);
                into.Indices.Add(start + part.Indices[northSouth ? k + 2 : k + 1]);
            }
        }

        void StreetLamp(CityMesh m, Vector3 foot, float toRoad)
        {
            const float pole = 0.09f, height = 5.2f;
            m.Box(foot + new Vector3(-pole, 0f, -pole), foot + new Vector3(pole, height, pole), Slice.Plain, Slice.Plain, Metal, Metal);
            var arm = foot + new Vector3(toRoad * 0.8f, height - 0.1f, 0f);
            m.Box(new Vector3(Mathf.Min(foot.x, arm.x), height - 0.16f + foot.y, foot.z - 0.06f),
                  new Vector3(Mathf.Max(foot.x, arm.x), height + foot.y, foot.z + 0.06f), Slice.Plain, Slice.Plain, Metal, Metal);
            m.Box(arm + new Vector3(-0.28f, -0.22f, -0.16f), arm + new Vector3(0.28f, -0.05f, 0.16f), Slice.Plain, Slice.Plain, Lamp, Metal);
        }

        void Tree(CityMesh m, System.Random rng, Vector3 foot, float scale)
        {
            float trunk = 2.2f * scale;
            m.Prism(foot, 0.16f * scale, foot.y, foot.y + trunk, 5, Slice.Plain, Trunk, cap: false);
            var green = Pick(rng, Greens);
            float size = Range(rng, 2.6f, 3.4f) * scale;
            m.Blob(foot + Vector3.up * (trunk + size * 0.35f), new Vector3(size, size * 0.9f, size), Slice.Leaves, green, rng);
            if (rng.NextDouble() < 0.5)
                m.Blob(foot + new Vector3(Range(rng, -0.6f, 0.6f), trunk + size * 0.75f, Range(rng, -0.6f, 0.6f)) ,
                       new Vector3(size, size, size) * 0.6f, Slice.Leaves, green, rng);
        }

        // ---------------------------------------------------------------- blocks

        void Block(CityMesh m, System.Random rng, int i, int j)
        {
            float x0 = W * 0.5f, x1 = P - W * 0.5f, z0 = x0, z1 = x1;
            double kind = rng.NextDouble();
            if (kind < 0.14)
            {
                Park(m, rng, x0, z0, x1, z1);
                return;
            }
            bool low = kind < 0.32;   // a quarter of the blocks: low houses with gable roofs
            float depth = low ? Range(rng, 9f, 11f) : Range(rng, 11f, 15f);
            // courtyard (seen from above, between the houses)
            m.Top(x0 + depth - 0.5f, z0 + depth - 0.5f, x1 - depth + 0.5f, z1 - depth + 0.5f, 0.02f,
                  rng.NextDouble() < 0.6 ? Slice.Grass : Slice.Cobbles, White, 4f);
            for (int k = 0; k < 3; k++)
                Tree(m, rng, new Vector3(Range(rng, x0 + depth + 3f, x1 - depth - 3f), 0f, Range(rng, z0 + depth + 3f, z1 - depth - 3f)), 1f);
            // rows along the four sides; the north and south rows own the corners
            Row(m, rng, x0, z0, x1, z0 + depth, alongX: true, low);
            Row(m, rng, x0, z1 - depth, x1, z1, alongX: true, low);
            Row(m, rng, x0, z0 + depth, x0 + depth, z1 - depth, alongX: false, low);
            Row(m, rng, x1 - depth, z0 + depth, x1, z1 - depth, alongX: false, low);
        }

        void Row(CityMesh m, System.Random rng, float x0, float z0, float x1, float z1, bool alongX, bool low)
        {
            float start = alongX ? x0 : z0, end = alongX ? x1 : z1;
            while (end - start > 0.5f)
            {
                float w = low ? Range(rng, 7f, 11f) : Range(rng, 9f, 17f);
                if (end - start - w < 6f) w = end - start;
                float height = low ? GroundH + FloorH * rng.Next(1, 3) : GroundH + FloorH * rng.Next(2, 7);
                if (!low && rng.NextDouble() < 0.12) height = GroundH + FloorH * rng.Next(6, 9);
                if (alongX) Building(m, rng, start, z0, start + w, z1, height, gable: low);
                else Building(m, rng, x0, start, x1, start + w, height, gable: low);
                start += w;
            }
        }

        /// <summary>One house: a ground floor (shops toward the street on some), storeys of one facade style, a cornice
        /// and a flat roof with things on it, or a gable roof.</summary>
        void Building(CityMesh m, System.Random rng, float x0, float z0, float x1, float z1, float height, bool gable = false,
                      bool landmark = false)
        {
            Slice facade;
            double f = rng.NextDouble();
            if (landmark) facade = Slice.Classic;
            else if (height > 18f && f < 0.6) facade = Slice.Office;
            else facade = f < 0.3 ? Slice.Apartment : f < 0.5 ? Slice.Balcony : f < 0.7 ? Slice.Classic : f < 0.85 ? Slice.Brick : Slice.Apartment;
            var wall = landmark ? new Color32(250, 236, 212, 255) : facade == Slice.Office ? Pick(rng, OfficeWalls) : Pick(rng, Walls);
            bool shops = !landmark && facade != Slice.Office && rng.NextDouble() < 0.55;
            var ground = shops ? (rng.NextDouble() < 0.6 ? Slice.Shop : Slice.Cafe) : facade;
            var accent = shops ? Pick(rng, Accents) : wall;
            int floors = Mathf.Max(1, Mathf.RoundToInt((height - GroundH) / FloorH));
            float top = GroundH + floors * FloorH;

            Vector3 sw = new Vector3(x0, 0f, z0), se = new Vector3(x1, 0f, z0), ne = new Vector3(x1, 0f, z1), nw = new Vector3(x0, 0f, z1);
            Side(m, sw, se, ground, facade, wall, accent, top);    // south
            Side(m, se, ne, ground, facade, wall, accent, top);    // east
            Side(m, ne, nw, ground, facade, wall, accent, top);    // north
            Side(m, nw, sw, ground, facade, wall, accent, top);    // west

            if (gable)
            {
                GableRoof(m, x0, z0, x1, z1, top, wall);
                return;
            }
            // cornice: a light ledge all round, the roof on it
            const float o = 0.22f, ledge = 0.45f;
            m.Box(new Vector3(x0 - o, top, z0 - o), new Vector3(x1 + o, top + ledge, z1 + o), Slice.Plain, Slice.Roof, Cornice, RoofGray, 4f);
            RoofThings(m, rng, x0, z0, x1, z1, top + ledge, landmark);
        }

        static void Side(CityMesh m, Vector3 from, Vector3 to, Slice ground, Slice upper, Color32 wall, Color32 accent, float top)
        {
            m.Wall(from, to, 0f, GroundH, ground, ground == upper ? wall : accent, BayW * 1.1f, GroundH, bottomShade: 150);
            m.Wall(from, to, GroundH, top, upper, wall, BayW, FloorH);
        }

        static void GableRoof(CityMesh m, float x0, float z0, float x1, float z1, float top, Color32 wall)
        {
            // the ridge runs along the longer side; "along" and "across" are that side's axes
            bool alongX = x1 - x0 >= z1 - z0;
            var along = alongX ? Vector3.right : Vector3.forward;
            var across = alongX ? Vector3.forward : Vector3.right;
            var centre = new Vector3((x0 + x1) * 0.5f, top, (z0 + z1) * 0.5f);
            float length = (alongX ? x1 - x0 : z1 - z0) * 0.5f, width = (alongX ? z1 - z0 : x1 - x0) * 0.5f;
            float eave = width + 0.3f, rise = eave * 0.55f, overhang = length + 0.3f;
            var ridge = Vector3.up * rise;
            var uv1 = new Vector2(overhang, Mathf.Sqrt(eave * eave + rise * rise) / 2f);
            foreach (float side in Sides)
            {
                // a tiled slope from its eave up to the ridge
                var e = centre + across * (side * eave);
                m.QuadFacing(across * side + Vector3.up, e - along * overhang, centre + ridge - along * overhang,
                             centre + ridge + along * overhang, e + along * overhang, Vector2.zero, uv1, Slice.Cobbles, RoofTile);
                // the gable end: a triangle of wall under the roof
                var end = centre + along * (side * length);
                m.TriangleFacing(along * side, end - across * width, end + ridge - Vector3.up * 0.2f, end + across * width, Slice.Plain, wall);
            }
        }

        static readonly float[] Sides = { -1f, 1f };
        static readonly Color32[] OfficeWalls = { Walls[7], Walls[8], Walls[2] };

        void RoofThings(CityMesh m, System.Random rng, float x0, float z0, float x1, float z1, float roof, bool landmark)
        {
            float w = x1 - x0, d = z1 - z0;
            if (landmark)
            {
                // a clock tower on the town hall
                var c = new Vector3((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f);
                m.Box(new Vector3(c.x - 2.2f, roof, c.z - 2.2f), new Vector3(c.x + 2.2f, roof + 6f, c.z + 2.2f), Slice.Classic, Slice.Roof,
                      new Color32(250, 236, 212, 255), RoofGray, 3.2f);
                m.Cone(new Vector3(c.x, roof + 6f, c.z), 3.2f, roof + 6f, 4f, 4, Slice.Plain, RoofTile);
                return;
            }
            int things = rng.Next(1, 4);
            for (int k = 0; k < things; k++)
            {
                float px = Range(rng, x0 + 1.5f, x1 - 1.5f), pz = Range(rng, z0 + 1.5f, z1 - 1.5f);
                double t = rng.NextDouble();
                if (t < 0.25 && w > 6f && d > 6f)
                {
                    // water tower on legs with a pointed lid
                    var c = new Vector3(px, 0f, pz);
                    for (int l = 0; l < 4; l++)
                    {
                        var leg = c + new Vector3(l % 2 == 0 ? -0.8f : 0.8f, 0f, l < 2 ? -0.8f : 0.8f);
                        m.Box(new Vector3(leg.x - 0.08f, roof, leg.z - 0.08f), new Vector3(leg.x + 0.08f, roof + 1.6f, leg.z + 0.08f),
                              Slice.Plain, Slice.Plain, Metal, Metal);
                    }
                    var wood = new Color32(176, 124, 86, 255);
                    m.Prism(c, 1.25f, roof + 1.6f, roof + 3.6f, 8, Slice.Plain, wood);
                    m.Cone(new Vector3(c.x, roof + 3.6f, c.z), 1.4f, roof + 3.6f, 1.1f, 8, Slice.Plain, Metal);
                }
                else if (t < 0.6)
                {
                    // air conditioning boxes
                    float s = Range(rng, 0.8f, 1.4f);
                    m.Box(new Vector3(px - s, roof, pz - s * 0.6f), new Vector3(px + s, roof + s * 0.9f, pz + s * 0.6f),
                          Slice.Plain, Slice.Plain, new Color32(222, 224, 228, 255), new Color32(200, 202, 208, 255));
                }
                else if (t < 0.8)
                {
                    // a little roof garden
                    m.Box(new Vector3(px - 1.6f, roof, pz - 1.2f), new Vector3(px + 1.6f, roof + 0.4f, pz + 1.2f),
                          Slice.Plain, Slice.Grass, new Color32(150, 120, 96, 255), White, 2f);
                    m.Blob(new Vector3(px, roof + 0.9f, pz), new Vector3(1.6f, 1.2f, 1.6f), Slice.Leaves, Pick(rng, Greens), rng);
                }
                else
                {
                    // an antenna
                    m.Box(new Vector3(px - 0.06f, roof, pz - 0.06f), new Vector3(px + 0.06f, roof + Range(rng, 2.5f, 4.5f), pz + 0.06f),
                          Slice.Plain, Slice.Plain, Metal, Metal);
                }
            }
        }

        void Park(CityMesh m, System.Random rng, float x0, float z0, float x1, float z1)
        {
            float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
            m.Top(x0, z0, x1, z1, 0.02f, Slice.Grass, White, 4f);
            // crossing paths and a pond in the middle with a stone rim
            m.Top(cx - 1.6f, z0, cx + 1.6f, z1, 0.04f, Slice.Cobbles, new Color32(246, 236, 220, 255), 2f);
            m.Top(x0, cz - 1.6f, x1, cz + 1.6f, 0.04f, Slice.Cobbles, new Color32(246, 236, 220, 255), 2f);
            float r = Range(rng, 7f, 10f);
            m.Prism(new Vector3(cx, 0f, cz), r + 0.6f, 0f, 0.45f, 12, Slice.Cobbles, new Color32(236, 230, 220, 255));
            m.Prism(new Vector3(cx, 0f, cz), r, 0f, 0.3f, 12, Slice.Water, White);
            // a fountain
            m.Prism(new Vector3(cx, 0f, cz), 0.9f, 0.3f, 1.4f, 8, Slice.Cobbles, new Color32(236, 230, 220, 255));
            m.Prism(new Vector3(cx, 0f, cz), 0.3f, 1.4f, 2.4f, 6, Slice.Water, White);
            int trees = rng.Next(12, 18);
            for (int k = 0; k < trees; k++)
            {
                var at = new Vector3(Range(rng, x0 + 3f, x1 - 3f), 0.02f, Range(rng, z0 + 3f, z1 - 3f));
                if (Mathf.Abs(at.x - cx) < r + 3f && Mathf.Abs(at.z - cz) < r + 3f) continue;   // not in the pond
                if (Mathf.Abs(at.x - cx) < 3f || Mathf.Abs(at.z - cz) < 3f) continue;           // nor on a path
                Tree(m, rng, at, Range(rng, 1.1f, 1.6f));
            }
            // hedges along the edges
            m.Box(new Vector3(x0 + 0.5f, 0f, z0 + 0.5f), new Vector3(cx - 2.5f, 0.9f, z0 + 1.5f), Slice.Leaves, Slice.Leaves, Greens[1], Greens[0], 1.5f);
            m.Box(new Vector3(cx + 2.5f, 0f, z0 + 0.5f), new Vector3(x1 - 0.5f, 0.9f, z0 + 1.5f), Slice.Leaves, Slice.Leaves, Greens[1], Greens[0], 1.5f);
            m.Box(new Vector3(x0 + 0.5f, 0f, z1 - 1.5f), new Vector3(cx - 2.5f, 0.9f, z1 - 0.5f), Slice.Leaves, Slice.Leaves, Greens[1], Greens[0], 1.5f);
            m.Box(new Vector3(cx + 2.5f, 0f, z1 - 1.5f), new Vector3(x1 - 0.5f, 0.9f, z1 - 0.5f), Slice.Leaves, Slice.Leaves, Greens[1], Greens[0], 1.5f);
        }
    }
}
