using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Paint
{
    /// <summary>
    /// The picture being painted: a transparent RenderTexture with <b>premultiplied</b> colour (so it composites
    /// cleanly over the paper or the camera picture), plus undo snapshots and the paint bucket.
    /// Brush stamps are collected with <see cref="Stamp"/> and drawn in one batch per frame by <see cref="Flush"/>.
    /// Coordinates are canvas pixels, (0, 0) = bottom-left, like the screen.
    ///
    /// <b>Wet paint:</b> a hand's stamps (owner 0 or more) stay wet for <see cref="WetSeconds"/>: they are shown at
    /// once (<see cref="Shown"/> = the picture plus the wet stamps) but only dry into the picture later. When a brush
    /// lifts because the arm dropped, the game wipes the stamps of that fast last movement (<see cref="Wipe"/>), so
    /// strokes don't get a tail down to where the hand came to rest.
    /// </summary>
    public sealed class PaintCanvas : IDisposable
    {
        public const int MaxUndo = 8;

        public readonly int Width, Height;
        /// <summary>The dried picture (what undo, the bucket and photos use).</summary>
        public RenderTexture Texture { get; }
        /// <summary>What to show: the picture with the wet stamps on top.</summary>
        public Texture Shown => _wet.Count > 0 && _shown != null ? _shown : Texture;
        public int UndoCount => _undoCount;
        /// <summary>Seconds; set by the game every frame. Wet stamps dry this long after they were made.</summary>
        public float Clock;
        public float WetSeconds = 0.4f;
        public int WetCount => _wet.Count;

        struct StampData
        {
            public Vector2 Center;
            public float Size, Degrees, Time;
            public Color Color;
            public BrushShape Shape;
            public bool Erase;
            public int Owner;
        }

        /// <summary>Stamps turned into one mesh (quads with atlas UVs and vertex colours).</summary>
        sealed class Batch
        {
            readonly List<Vector3> _vertices = new List<Vector3>();
            readonly List<Vector2> _uvs = new List<Vector2>();
            readonly List<Color> _colors = new List<Color>();
            readonly List<int> _indices = new List<int>();
            public readonly Mesh Mesh;

            public Batch(string name)
            {
                Mesh = new Mesh { name = name };
                Mesh.MarkDynamic();
            }

            public int Count => _vertices.Count / 4;

            public void Add(in StampData s, bool linear)
            {
                if (_vertices.Count >= 60000) return;   // a frame never needs this many; keep within 16-bit indices
                float half = s.Size * 0.5f, rad = s.Degrees * Mathf.Deg2Rad;
                var right = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * half;
                var up = new Vector2(-right.y, right.x);
                _vertices.Add(s.Center - right - up);
                _vertices.Add(s.Center + right - up);
                _vertices.Add(s.Center + right + up);
                _vertices.Add(s.Center - right + up);
                var uv = PaintArt.ShapeUv(s.Shape);
                _uvs.Add(new Vector2(uv.xMin, uv.yMin));
                _uvs.Add(new Vector2(uv.xMax, uv.yMin));
                _uvs.Add(new Vector2(uv.xMax, uv.yMax));
                _uvs.Add(new Vector2(uv.xMin, uv.yMax));
                var c = s.Color;
                if (linear) c = new Color(c.r, c.g, c.b).linear.WithAlpha(c.a);   // vertex colours aren't converted
                for (int i = 0; i < 4; i++) _colors.Add(c);
            }

            /// <summary>Builds the mesh from the stamps and empties the list.</summary>
            public void Build(float width, float height)
            {
                Mesh.Clear();
                Mesh.SetVertices(_vertices);
                Mesh.SetUVs(0, _uvs);
                Mesh.SetColors(_colors);
                _indices.Clear();
                for (int q = 0; q < _vertices.Count; q += 4)
                {
                    _indices.Add(q); _indices.Add(q + 1); _indices.Add(q + 2);
                    _indices.Add(q); _indices.Add(q + 2); _indices.Add(q + 3);
                }
                Mesh.SetTriangles(_indices, 0, false);
                Mesh.bounds = new Bounds(new Vector3(width / 2f, height / 2f, 0f), new Vector3(width * 4f, height * 4f, 2f));
                Clear();
            }

            public void Clear()
            {
                _vertices.Clear();
                _uvs.Clear();
                _colors.Clear();
            }
        }

        readonly Material _brush;
        readonly CommandBuffer _cmd = new CommandBuffer { name = "Paint strokes" };
        readonly List<StampData> _dry = new List<StampData>();   // waiting for the next Flush, in order
        readonly List<StampData> _wet = new List<StampData>();
        readonly List<Batch> _batches = new List<Batch>();
        readonly RenderTexture[] _undo = new RenderTexture[MaxUndo];
        int _undoHead, _undoCount;
        RenderTexture _shown;
        Texture2D _cpu;
        readonly bool _linear = QualitySettings.activeColorSpace == ColorSpace.Linear;

        public PaintCanvas(int width, int height, Material brushMaterial)
        {
            Width = width;
            Height = height;
            _brush = brushMaterial;
            _brush.mainTexture = PaintArt.BrushAtlas;
            Texture = NewTexture("Paint canvas");
            Clear();
        }

        RenderTexture NewTexture(string name)
        {
            var rt = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            rt.Create();
            return rt;
        }

        public void Dispose()
        {
            foreach (var rt in new List<RenderTexture>(_undo) { Texture, _shown })
                if (rt != null)
                {
                    rt.Release();
                    Discard(rt);
                }
            foreach (var b in _batches) Discard(b.Mesh);
            if (_cpu != null) Discard(_cpu);
            _cmd.Release();
        }

        static void Discard(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);   // edit-mode tests
        }

        /// <summary>Back to fully transparent (the paper or camera picture shows through).</summary>
        public void Clear()
        {
            _wet.Clear();
            _dry.Clear();
            var active = RenderTexture.active;
            RenderTexture.active = Texture;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = active;
        }

        // ---------------------------------------------------------------- stamps

        /// <summary>
        /// One brush stamp: a shape from the atlas centred at <paramref name="center"/> (canvas pixels),
        /// <paramref name="size"/> pixels across, rotated by <paramref name="degrees"/>. <paramref name="color"/> is the
        /// straight colour with the stamp's opacity in alpha. Erase stamps remove paint instead. A hand's stamps
        /// (<paramref name="owner"/> 0 or more) start wet; others go straight into the next <see cref="Flush"/>.
        /// </summary>
        public void Stamp(Vector2 center, float size, float degrees, Color color, BrushShape shape, bool erase = false, int owner = -1)
        {
            if (size <= 0.5f || color.a <= 0f) return;
            var s = new StampData
            {
                Center = center, Size = size, Degrees = degrees, Color = color, Shape = shape, Erase = erase, Owner = owner, Time = Clock,
            };
            (owner >= 0 && WetSeconds > 0f ? _wet : _dry).Add(s);
        }

        /// <summary>Dries the wet stamps that are old enough (they join the next <see cref="Flush"/>).</summary>
        public void Dry()
        {
            int n = 0;
            while (n < _wet.Count && Clock - _wet[n].Time >= WetSeconds) n++;
            DryFirst(n);
        }

        public void DryAll() => DryFirst(_wet.Count);

        void DryFirst(int n)
        {
            for (int i = 0; i < n; i++) _dry.Add(_wet[i]);
            _wet.RemoveRange(0, n);
        }

        /// <summary>Removes one hand's wet stamps made at or after <paramref name="since"/> (seconds). Returns how many.</summary>
        public int Wipe(int owner, float since) => _wet.RemoveAll(s => s.Owner == owner && s.Time >= since);

        /// <summary>Draws the dried stamps into the picture, in the order they were made.</summary>
        public void Flush()
        {
            if (_dry.Count == 0) return;
            Draw(Texture, _dry);
            _dry.Clear();
        }

        /// <summary>Updates <see cref="Shown"/>: the picture plus the wet stamps. Call once a frame, after <see cref="Flush"/>.</summary>
        public void Present()
        {
            if (_wet.Count == 0) return;
            if (_shown == null) _shown = NewTexture("Paint canvas (shown)");
            Graphics.CopyTexture(Texture, _shown);
            Draw(_shown, _wet);
        }

        /// <summary>One mesh per run of paint or erase stamps, drawn in order (erasing a fresh stroke must work).</summary>
        void Draw(RenderTexture target, List<StampData> stamps)
        {
            _cmd.Clear();
            _cmd.SetRenderTarget(target);
            _cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0f, Width, 0f, Height, -1f, 1f));
            int run = 0;
            for (int i = 0; i < stamps.Count;)
            {
                bool erase = stamps[i].Erase;
                if (run == _batches.Count) _batches.Add(new Batch($"Paint stamps {run}"));
                var batch = _batches[run++];
                for (; i < stamps.Count && stamps[i].Erase == erase; i++) batch.Add(stamps[i], _linear);
                batch.Build(Width, Height);
                _cmd.DrawMesh(batch.Mesh, Matrix4x4.identity, _brush, 0, erase ? 1 : 0);
            }
            Graphics.ExecuteCommandBuffer(_cmd);
        }

        // ---------------------------------------------------------------- undo

        /// <summary>Remembers the picture as it is now; <see cref="Undo"/> brings it back. Keeps the last <see cref="MaxUndo"/>.</summary>
        public void PushUndo()
        {
            Flush();
            ref var slot = ref _undo[_undoHead];
            if (slot == null) slot = NewTexture("Paint undo");
            Graphics.CopyTexture(Texture, slot);
            _undoHead = (_undoHead + 1) % MaxUndo;
            _undoCount = Mathf.Min(_undoCount + 1, MaxUndo);
        }

        public bool Undo()
        {
            if (_undoCount == 0) return false;
            _wet.Clear();
            _dry.Clear();
            _undoHead = (_undoHead + MaxUndo - 1) % MaxUndo;
            _undoCount--;
            Graphics.CopyTexture(_undo[_undoHead], Texture);
            return true;
        }

        // ---------------------------------------------------------------- reading back, bucket

        /// <summary>Copies the canvas to the CPU (premultiplied, sRGB). Row 0 = bottom. Reuses one buffer.</summary>
        public Color32[] ReadPixels()
        {
            DryAll();
            Flush();
            if (_cpu == null) _cpu = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false) { name = "Paint canvas (CPU)" };
            var active = RenderTexture.active;
            RenderTexture.active = Texture;
            _cpu.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            RenderTexture.active = active;
            return _cpu.GetPixels32();
        }

        void WritePixels(Color32[] pixels)
        {
            _cpu.SetPixels32(pixels);
            _cpu.Apply(false, false);
            Graphics.Blit(_cpu, Texture);
        }

        /// <summary>
        /// Paint bucket: fills the area around <paramref name="at"/> that has the same colour as that spot (a
        /// transparent area counts as one colour), stopping at different paint and at <paramref name="walls"/> (a
        /// colouring page's lines, canvas-sized, true = line). The fill creeps 2 pixels under its borders, so no pale
        /// seam is left along soft brush edges or the anti-aliased lines. Returns the number of pixels filled.
        /// </summary>
        public int Fill(Vector2 at, Color color, bool[] walls = null, int tolerance = 60)
        {
            int sx = Mathf.Clamp((int)at.x, 0, Width - 1), sy = Mathf.Clamp((int)at.y, 0, Height - 1);
            if (walls != null && walls[sy * Width + sx]) return 0;
            var pixels = ReadPixels();
            var seed = pixels[sy * Width + sx];
            var fill = (Color32)color;
            fill.a = 255;
            if (Same(seed, fill, 8)) return 0;

            var region = new bool[pixels.Length];
            var stack = new Stack<int>();
            stack.Push(sy * Width + sx);
            int count = 0;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                int y = i / Width, x = i % Width;
                // scanline: walk left to the run start, then fill right, queueing the rows above and below
                while (x > 0 && Open(x - 1, y)) x--;
                bool above = false, below = false;
                for (; x < Width && Open(x, y); x++)
                {
                    int k = y * Width + x;
                    region[k] = true;
                    count++;
                    if (y + 1 < Height)
                    {
                        bool open = Open(x, y + 1);
                        if (open && !above) stack.Push(k + Width);
                        above = open;
                    }
                    if (y > 0)
                    {
                        bool open = Open(x, y - 1);
                        if (open && !below) stack.Push(k - Width);
                        below = open;
                    }
                }
            }

            // creep under the edges (2 px): hides the seam along soft strokes and anti-aliased page lines
            for (int pass = 0; pass < 2; pass++)
            {
                var grown = (bool[])region.Clone();
                for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int k = y * Width + x;
                    if (region[k]) continue;
                    if ((x > 0 && region[k - 1]) || (x + 1 < Width && region[k + 1]) ||
                        (y > 0 && region[k - Width]) || (y + 1 < Height && region[k + Width]))
                        grown[k] = true;
                }
                region = grown;
            }
            for (int k = 0; k < pixels.Length; k++)
                if (region[k]) pixels[k] = fill;   // under a page line too: the line art is drawn on top anyway
            WritePixels(pixels);
            return count;

            bool Open(int x, int y)
            {
                int k = y * Width + x;
                return !region[k] && (walls == null || !walls[k]) && Same(pixels[k], seed, tolerance);
            }
        }

        static bool Same(Color32 a, Color32 b, int tolerance) =>
            Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a) <= tolerance;

        /// <summary>Counts pixels with paint on them (alpha above <paramref name="minAlpha"/>), e.g. for tests.</summary>
        public int CountPainted(byte minAlpha = 32)
        {
            int n = 0;
            foreach (var p in ReadPixels())
                if (p.a > minAlpha) n++;
            return n;
        }
    }

    static class ColorExtensions
    {
        public static Color WithAlpha(this Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
