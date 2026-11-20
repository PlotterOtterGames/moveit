using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Paint
{
    /// <summary>
    /// The areas of a colouring page (the dino's body, the egg, each balloon), found from its lines, so that
    /// <b>paint stays inside the lines</b> and an area that is mostly coloured fills itself: what a child's hand in
    /// the air can't do, the page does. (Kids' colouring apps on tablets do the same on touch-down; the camera's hand
    /// point wobbles by 10-25 px while it moves, far more than a finger.)
    ///
    /// <see cref="Labels"/> holds an area number per canvas pixel (1..<see cref="Count"/>; the line pixels belong to the
    /// nearest area, so paint from both sides meets under the line, which is drawn on top). The brush's stamps are
    /// clipped to one area on the GPU (Tundu/Brush compares against the label texture), chosen by <see cref="RegionLock"/>.
    /// How much of an area is coloured is tracked on a coarse grid (<see cref="Cell"/> px cells, no read-back from the
    /// GPU): past <see cref="CompleteAt"/> the game fills the gaps (<see cref="ShouldComplete"/>) and the area is done;
    /// when every area that counts is done, the picture is finished.
    ///
    /// Coordinates are canvas pixels, row 0 at the bottom, like <see cref="PaintCanvas"/>. No engine objects: tested in
    /// PageRegionsTests.
    /// </summary>
    public sealed class PageRegions
    {
        public const int Cell = 8;
        public const int MaxRegions = 250;

        public readonly int Width, Height;
        /// <summary>Area number per pixel, 0 only on a page without any area.</summary>
        public readonly byte[] Labels;
        public int Count { get; private set; }
        /// <summary>An area is filled up when this share of it is coloured.</summary>
        public float CompleteAt = 0.7f;

        int[] _area = Array.Empty<int>();          // pixels between the lines, by area (index 0 unused)
        int _gw, _gh;
        byte[] _cellRegion = Array.Empty<byte>();
        bool[] _covered = Array.Empty<bool>();
        int[] _cells = Array.Empty<int>(), _coveredCells = Array.Empty<int>();
        bool[] _done = Array.Empty<bool>(), _counts = Array.Empty<bool>();

        PageRegions(int width, int height)
        {
            Width = width;
            Height = height;
            Labels = new byte[width * height];
        }

        /// <summary>
        /// Finds the areas between the lines. <paramref name="walls"/>: true = a line pixel (canvas-sized, row 0 at the
        /// bottom). Specks smaller than <paramref name="minArea"/> pixels (gaps between two lines) join their
        /// neighbours. An area counts toward "finished" from <paramref name="countArea"/> pixels up to
        /// <paramref name="backgroundShare"/> of the page (the sky behind everything is left to the child).
        /// </summary>
        public static PageRegions FromWalls(bool[] walls, int width, int height, int minArea = 60, int countArea = 1200,
                                            float backgroundShare = 0.3f)
        {
            if (walls == null || walls.Length != width * height) throw new ArgumentException("walls must be width x height");
            var r = new PageRegions(width, height);
            var labels = new int[width * height];
            var areas = new List<int> { 0 };
            var stack = new Stack<int>();
            for (int start = 0; start < labels.Length; start++)
            {
                if (walls[start] || labels[start] != 0) continue;
                int id = areas.Count, n = 0;
                labels[start] = id;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    n++;
                    int x = i % width, y = i / width;
                    if (x > 0) Visit(i - 1);
                    if (x + 1 < width) Visit(i + 1);
                    if (y > 0) Visit(i - width);
                    if (y + 1 < height) Visit(i + width);
                }
                areas.Add(n);

                void Visit(int k)
                {
                    if (walls[k] || labels[k] != 0) return;
                    labels[k] = id;
                    stack.Push(k);
                }
            }

            // keep the areas big enough to matter (at most MaxRegions, the biggest), numbered from 1
            var order = new List<int>();
            for (int id = 1; id < areas.Count; id++)
                if (areas[id] >= minArea) order.Add(id);
            order.Sort((a, b) => areas[b].CompareTo(areas[a]));
            if (order.Count > MaxRegions) order.RemoveRange(MaxRegions, order.Count - MaxRegions);
            var renumber = new int[areas.Count];
            r.Count = order.Count;
            r._area = new int[r.Count + 1];
            for (int k = 0; k < order.Count; k++)
            {
                renumber[order[k]] = k + 1;
                r._area[k + 1] = areas[order[k]];
            }
            var queue = new Queue<int>();
            for (int i = 0; i < labels.Length; i++)
            {
                r.Labels[i] = (byte)renumber[labels[i]];
                if (r.Labels[i] != 0) queue.Enqueue(i);
            }
            // the lines (and the specks) go to the nearest area, breadth first
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                byte id = r.Labels[i];
                int x = i % width, y = i / width;
                if (x > 0) Grow(i - 1);
                if (x + 1 < width) Grow(i + 1);
                if (y > 0) Grow(i - width);
                if (y + 1 < height) Grow(i + width);

                void Grow(int k)
                {
                    if (r.Labels[k] != 0) return;
                    r.Labels[k] = id;
                    queue.Enqueue(k);
                }
            }

            r._gw = (width + Cell - 1) / Cell;
            r._gh = (height + Cell - 1) / Cell;
            r._cellRegion = new byte[r._gw * r._gh];
            r._covered = new bool[r._gw * r._gh];
            r._cells = new int[r.Count + 1];
            r._coveredCells = new int[r.Count + 1];
            r._done = new bool[r.Count + 1];
            r._counts = new bool[r.Count + 1];
            for (int gy = 0; gy < r._gh; gy++)
            for (int gx = 0; gx < r._gw; gx++)
            {
                int px = Mathf.Min(width - 1, gx * Cell + Cell / 2), py = Mathf.Min(height - 1, gy * Cell + Cell / 2);
                // a cell belongs to the area under its middle, if that is paper (a cell on a line needs no paint)
                byte id = walls[py * width + px] ? (byte)0 : r.Labels[py * width + px];
                r._cellRegion[gy * r._gw + gx] = id;
                r._cells[id]++;
            }
            float page = (float)width * height;
            for (int id = 1; id <= r.Count; id++)
                r._counts[id] = r._area[id] >= countArea && r._area[id] <= backgroundShare * page;
            return r;
        }

        /// <summary>The area under a canvas point (clamped to the page).</summary>
        public int At(float x, float y)
        {
            int px = Mathf.Clamp((int)x, 0, Width - 1), py = Mathf.Clamp((int)y, 0, Height - 1);
            return Labels[py * Width + px];
        }

        public int Area(int region) => region > 0 && region <= Count ? _area[region] : 0;

        /// <summary>Does this area count toward "the picture is finished"? (Big enough for a hand, and not the sky.)</summary>
        public bool Counts(int region) => region > 0 && region <= Count && _counts[region];

        public bool Done(int region) => region > 0 && region <= Count && _done[region];

        /// <summary>The share of an area that is coloured, 0..1.</summary>
        public float Coverage(int region)
        {
            if (region <= 0 || region > Count) return 0f;
            if (_done[region]) return 1f;
            return _cells[region] == 0 ? 0f : _coveredCells[region] / (float)_cells[region];
        }

        /// <summary>A stamp of the brush landed in this area: marks what it covers. Returns the cells newly covered.</summary>
        public int Cover(int region, Vector2 center, float radius)
        {
            if (region <= 0 || region > Count) return 0;
            if (_cells[region] == 0)   // smaller than a cell: one stamp colours it
            {
                _done[region] = true;
                return 0;
            }
            int added = 0;
            float reach = radius + Cell * 0.5f;
            int x0 = Mathf.Max(0, (int)((center.x - reach) / Cell)), x1 = Mathf.Min(_gw - 1, (int)((center.x + reach) / Cell));
            int y0 = Mathf.Max(0, (int)((center.y - reach) / Cell)), y1 = Mathf.Min(_gh - 1, (int)((center.y + reach) / Cell));
            for (int gy = y0; gy <= y1; gy++)
            for (int gx = x0; gx <= x1; gx++)
            {
                int k = gy * _gw + gx;
                if (_covered[k] || _cellRegion[k] != region) continue;
                float dx = gx * Cell + Cell * 0.5f - center.x, dy = gy * Cell + Cell * 0.5f - center.y;
                if (dx * dx + dy * dy > reach * reach) continue;
                _covered[k] = true;
                added++;
            }
            _coveredCells[region] += added;
            return added;
        }

        /// <summary>The eraser rubbed here: what it uncovers is to colour again (in every area it touches).</summary>
        public void Uncover(Vector2 center, float radius)
        {
            int x0 = Mathf.Max(0, (int)((center.x - radius) / Cell)), x1 = Mathf.Min(_gw - 1, (int)((center.x + radius) / Cell));
            int y0 = Mathf.Max(0, (int)((center.y - radius) / Cell)), y1 = Mathf.Min(_gh - 1, (int)((center.y + radius) / Cell));
            for (int gy = y0; gy <= y1; gy++)
            for (int gx = x0; gx <= x1; gx++)
            {
                int k = gy * _gw + gx;
                int id = _cellRegion[k];
                if (id == 0) continue;
                float dx = gx * Cell + Cell * 0.5f - center.x, dy = gy * Cell + Cell * 0.5f - center.y;
                if (dx * dx + dy * dy > radius * radius) continue;
                if (_done[id])   // a finished area is whole: the eraser opens it again
                {
                    _done[id] = false;
                    _coveredCells[id] = 0;
                    for (int c = 0; c < _covered.Length; c++)
                        if (_cellRegion[c] == id)
                        {
                            _covered[c] = true;
                            _coveredCells[id]++;
                        }
                }
                if (!_covered[k]) continue;
                _covered[k] = false;
                _coveredCells[id]--;
            }
        }

        /// <summary>Coloured enough to fill the rest up? (Not when it's done already.)</summary>
        public bool ShouldComplete(int region) =>
            region > 0 && region <= Count && !_done[region] && _cells[region] > 0 && Coverage(region) >= CompleteAt;

        /// <summary>The whole area is coloured (the game filled the gaps, or the bucket filled it).</summary>
        public void MarkDone(int region)
        {
            if (region > 0 && region <= Count) _done[region] = true;
        }

        /// <summary>A fresh page: nothing coloured.</summary>
        public void Clear()
        {
            Array.Clear(_covered, 0, _covered.Length);
            Array.Clear(_coveredCells, 0, _coveredCells.Length);
            Array.Clear(_done, 0, _done.Length);
        }

        /// <summary>How many areas count toward "finished", and how many of them are done.</summary>
        public int CountedTotal
        {
            get
            {
                int n = 0;
                for (int id = 1; id <= Count; id++)
                    if (_counts[id]) n++;
                return n;
            }
        }

        public int CountedDone
        {
            get
            {
                int n = 0;
                for (int id = 1; id <= Count; id++)
                    if (_counts[id] && _done[id]) n++;
                return n;
            }
        }

        /// <summary>Every area that counts is coloured: the picture is finished.</summary>
        public bool Finished => CountedTotal > 0 && CountedDone == CountedTotal;

        /// <summary>The middle of an area (canvas pixels): where its sparkle goes. The cell nearest the centre of mass.</summary>
        public Vector2 Middle(int region)
        {
            if (region <= 0 || region > Count || _cells[region] == 0) return new Vector2(Width * 0.5f, Height * 0.5f);
            double sx = 0, sy = 0;
            int n = 0;
            for (int k = 0; k < _cellRegion.Length; k++)
            {
                if (_cellRegion[k] != region) continue;
                sx += k % _gw;
                sy += k / _gw;
                n++;
            }
            float cx = (float)(sx / n), cy = (float)(sy / n);
            int best = -1;
            float bestD = float.MaxValue;
            for (int k = 0; k < _cellRegion.Length; k++)
            {
                if (_cellRegion[k] != region) continue;
                float dx = k % _gw - cx, dy = k / _gw - cy, d = dx * dx + dy * dy;
                if (d < bestD)
                {
                    bestD = d;
                    best = k;
                }
            }
            return new Vector2((best % _gw + 0.5f) * Cell, (best / _gw + 0.5f) * Cell);
        }

        // ---------------------------------------------------------------- undo

        /// <summary>What is coloured right now, to bring back with <see cref="Restore"/> (the game keeps one per undo step).</summary>
        public sealed class Snapshot
        {
            internal bool[] Covered, Done;
            internal int[] CoveredCells;
        }

        public Snapshot Save(Snapshot reuse = null)
        {
            var s = reuse ?? new Snapshot();
            s.Covered = Copy(_covered, s.Covered);
            s.Done = Copy(_done, s.Done);
            s.CoveredCells = Copy(_coveredCells, s.CoveredCells);
            return s;
        }

        public void Restore(Snapshot s)
        {
            if (s?.Covered == null || s.Covered.Length != _covered.Length || s.Done.Length != _done.Length) return;
            Array.Copy(s.Covered, _covered, _covered.Length);
            Array.Copy(s.Done, _done, _done.Length);
            Array.Copy(s.CoveredCells, _coveredCells, _coveredCells.Length);
        }

        static T[] Copy<T>(T[] from, T[] into)
        {
            if (into == null || into.Length != from.Length) into = new T[from.Length];
            Array.Copy(from, into, from.Length);
            return into;
        }
    }

    /// <summary>
    /// Which area a brush is colouring. It <b>sticks</b>: the area under the brush when it goes down, and it changes
    /// only when the brush is <see cref="SwitchDistance"/> pixels into another area (from where it crossed the line), or
    /// has stayed in it for <see cref="SwitchSeconds"/>. So a scribble that overshoots a line paints nothing beyond it
    /// (the stamps are clipped to the locked area), and moving on into the next part of the picture colours that one.
    /// (0.6 s: a scribble that turns around just beyond a line is out there for 0.3-0.5 s; with 0.3 the scripted
    /// player's to-and-fro left a blob of paint outside at every turn.)
    /// </summary>
    public sealed class RegionLock
    {
        public float SwitchDistance = 24f;
        public float SwitchSeconds = 0.6f;

        /// <summary>The locked area, 0 before the first step.</summary>
        public int Region { get; private set; }
        /// <summary>Where the brush crossed into the area it just switched to (for painting that bit after all).</summary>
        public Vector2 EnteredAt { get; private set; }

        int _other;
        float _time;
        Vector2 _crossed;

        public void Reset()
        {
            Region = _other = 0;
            _time = 0f;
        }

        /// <summary>
        /// The brush is at <paramref name="position"/> over area <paramref name="under"/>, <paramref name="seconds"/>
        /// after the last step. Returns true when the lock just changed to another area.
        /// </summary>
        public bool Step(int under, Vector2 position, float seconds = 0f)
        {
            if (Region == 0)
            {
                Region = under;
                _other = 0;
                return false;
            }
            if (under == Region || under == 0)
            {
                _other = 0;
                return false;
            }
            if (under != _other)
            {
                _other = under;
                _time = 0f;
                _crossed = position;
            }
            _time += seconds;
            if (Vector2.Distance(position, _crossed) < SwitchDistance && _time < SwitchSeconds) return false;
            Region = under;
            EnteredAt = _crossed;
            _other = 0;
            return true;
        }
    }
}
