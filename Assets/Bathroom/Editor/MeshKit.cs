// Procedural mesh helpers for the bathroom builder. All inputs are in inches; output is meters.
using System.Collections.Generic;
using UnityEngine;

namespace MomsBathroom.EditorTools
{
    public class MeshKit
    {
        public const float In = 0.0254f;

        readonly List<Vector3> _v = new();
        readonly List<Vector2> _uv = new();
        readonly List<int> _t = new();

        public bool IsEmpty => _t.Count == 0;

        // a,b,c,d clockwise when viewed from the front (Unity winding). Positions in inches.
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            var i = _v.Count;
            _v.Add(a * In); _v.Add(b * In); _v.Add(c * In); _v.Add(d * In);
            _uv.Add(ua); _uv.Add(ub); _uv.Add(uc); _uv.Add(ud);
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
            _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
        }

        // Rectangle spanned by U (rightwards) and V (upwards); front normal = Cross(V, U).
        // UVs are the rect's size divided by uvScale (inches per texture repeat).
        public void Rect(Vector3 origin, Vector3 U, Vector3 V, float uvScale, Vector2 uvOffset = default)
        {
            var w = U.magnitude / uvScale; var h = V.magnitude / uvScale;
            Quad(origin, origin + V, origin + U + V, origin + U,
                uvOffset, uvOffset + new Vector2(0, h), uvOffset + new Vector2(w, h), uvOffset + new Vector2(w, 0));
        }

        // Axis-aligned box from min to max (inches); faces listed in `skip` are omitted.
        public void Box(Vector3 min, Vector3 max, float uvScale, string skip = "")
        {
            var s = max - min;
            Vector3 X = new(s.x, 0, 0), Y = new(0, s.y, 0), Z = new(0, 0, s.z);
            if (!skip.Contains("-z")) Rect(min, X, Y, uvScale);                                  // front (-z)
            if (!skip.Contains("+z")) Rect(min + Z + X, -X, Y, uvScale);                         // back (+z)
            if (!skip.Contains("-x")) Rect(min + Z, -Z, Y, uvScale);                             // left (-x)
            if (!skip.Contains("+x")) Rect(min + X, Z, Y, uvScale);                              // right (+x)
            if (!skip.Contains("+y")) Rect(min + Y, X, Z, uvScale);                              // top
            if (!skip.Contains("-y")) Rect(min + Z, X, -Z, uvScale);                             // bottom
        }

        // ---------------------------------------------------------------- Tiles

        public struct R
        {
            public float x0, y0, x1, y1;
            public R(float x0, float y0, float x1, float y1) { this.x0 = x0; this.y0 = y0; this.x1 = x1; this.y1 = y1; }
            public bool Valid => x1 - x0 > 0.01f && y1 - y0 > 0.01f;
        }

        public static List<R> Subtract(List<R> rects, IEnumerable<R> holes)
        {
            var cur = rects;
            foreach (var h in holes)
            {
                var next = new List<R>();
                foreach (var r in cur)
                {
                    if (h.x1 <= r.x0 || h.x0 >= r.x1 || h.y1 <= r.y0 || h.y0 >= r.y1) { next.Add(r); continue; }
                    var pieces = new[]
                    {
                        new R(r.x0, r.y0, Mathf.Min(h.x0, r.x1), r.y1),
                        new R(Mathf.Max(h.x1, r.x0), r.y0, r.x1, r.y1),
                        new R(Mathf.Max(h.x0, r.x0), r.y0, Mathf.Min(h.x1, r.x1), Mathf.Min(h.y0, r.y1)),
                        new R(Mathf.Max(h.x0, r.x0), Mathf.Max(h.y1, r.y0), Mathf.Min(h.x1, r.x1), r.y1),
                    };
                    foreach (var p in pieces) if (p.Valid) next.Add(p);
                }
                cur = next;
            }
            return cur;
        }

        // Lays large-format tiles over a planar surface (origin, U = right, V = up; normal = Cross(V,U)).
        // Each tile shows the full texture with a random 90-degree rotation, like real porcelain faces.
        // Grout is emitted into `grout` as a backing plane 1 mm behind the tiles.
        public static void TileSurface(MeshKit tiles, MeshKit grout, Vector3 origin, Vector3 uDir, Vector3 vDir,
            float width, float height, float tileW, float tileH, IList<R> holes, int seed,
            float gap = 0.08f, bool centerS = true, bool centerT = false)
        {
            uDir.Normalize(); vDir.Normalize();
            var normal = Vector3.Cross(vDir, uDir).normalized;
            var rng = new System.Random(seed);
            var offS = centerS ? -((Mathf.Ceil(width / tileW) * tileW - width) / 2f) : 0f;
            var offT = centerT ? -((Mathf.Ceil(height / tileH) * tileH - height) / 2f) : 0f;

            Vector3 P(float s, float t, float lift = 0) => origin + uDir * s + vDir * t + normal * lift;

            for (var cs = offS; cs < width; cs += tileW)
            for (var ct = offT; ct < height; ct += tileH)
            {
                var cell = new R(cs, ct, cs + tileW, ct + tileH);
                var r = new R(Mathf.Max(cell.x0 + gap / 2, 0), Mathf.Max(cell.y0 + gap / 2, 0),
                    Mathf.Min(cell.x1 - gap / 2, width), Mathf.Min(cell.y1 - gap / 2, height));
                if (!r.Valid) continue;
                var rot = rng.Next(4);
                foreach (var p in Subtract(new List<R> { r }, holes))
                {
                    Vector2 UV(float s, float t)
                    {
                        var u = (s - cell.x0) / tileW; var v = (t - cell.y0) / tileH;
                        for (var k = 0; k < rot; k++) (u, v) = (v, 1 - u);
                        return new Vector2(u, v);
                    }
                    tiles.Quad(P(p.x0, p.y0), P(p.x0, p.y1), P(p.x1, p.y1), P(p.x1, p.y0),
                        UV(p.x0, p.y0), UV(p.x0, p.y1), UV(p.x1, p.y1), UV(p.x1, p.y0));
                }
            }

            if (grout == null) return;
            foreach (var p in Subtract(new List<R> { new(0, 0, width, height) }, holes))
                grout.Quad(P(p.x0, p.y0, -0.04f), P(p.x0, p.y1, -0.04f), P(p.x1, p.y1, -0.04f), P(p.x1, p.y0, -0.04f),
                    Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
        }

        // Inward-facing (recessed) rectangular opening returns: depth goes along -normal.
        public static void OpeningReturns(MeshKit kit, Vector3 origin, Vector3 uDir, Vector3 vDir, R h, float depth,
            float uvScale, bool skipBottom = false)
        {
            uDir.Normalize(); vDir.Normalize();
            var n = Vector3.Cross(vDir, uDir).normalized;
            var back = -n * depth;
            Vector3 P(float s, float t) => origin + uDir * s + vDir * t;
            var w = h.x1 - h.x0; var ht = h.y1 - h.y0;
            // left jamb faces +U
            kit.Rect(P(h.x0, h.y0), back, vDir * ht, uvScale);
            // right jamb faces -U
            kit.Rect(P(h.x1, h.y0) + back, -back, vDir * ht, uvScale);
            // head faces -V
            kit.Rect(P(h.x0, h.y1) + back, uDir * w, -back, uvScale);
            // sill faces +V
            if (!skipBottom) kit.Rect(P(h.x0, h.y0), uDir * w, back, uvScale);
        }

        // ---------------------------------------------------------------- Lofts and tubes

        public struct Ring
        {
            public float y, cx, cz, rx, rz, p;
            public Ring(float y, float cx, float cz, float rx, float rz, float p = 2)
            { this.y = y; this.cx = cx; this.cz = cz; this.rx = rx; this.rz = rz; this.p = p; }
        }

        static Vector3 SuperEllipse(Ring r, float a)
        {
            var c = Mathf.Cos(a); var s = Mathf.Sin(a);
            var e = 2f / r.p;
            var x = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), e) * r.rx;
            var z = Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), e) * r.rz;
            return new Vector3(r.cx + x, r.y, r.cz + z);
        }

        // Smooth vertical loft through superellipse rings (bottom to top). inward = normals face the axis.
        public void Loft(IList<Ring> rings, int seg = 48, bool capBottom = false, bool capTop = true, bool inward = false,
            float uvScale = 12f)
        {
            var start = _v.Count;
            for (var i = 0; i < rings.Count; i++)
            for (var j = 0; j <= seg; j++)
            {
                var a = -j * Mathf.PI * 2 / seg;
                _v.Add(SuperEllipse(rings[i], a) * In);
                _uv.Add(new Vector2(j / (float)seg * 4f, rings[i].y / uvScale));
            }
            var row = seg + 1;
            for (var i = 0; i < rings.Count - 1; i++)
            for (var j = 0; j < seg; j++)
            {
                int a = start + i * row + j, b = a + row, c = b + 1, d = a + 1;
                if (inward) { _t.Add(a); _t.Add(b); _t.Add(c); _t.Add(a); _t.Add(c); _t.Add(d); }
                else { _t.Add(a); _t.Add(c); _t.Add(b); _t.Add(a); _t.Add(d); _t.Add(c); }
            }
            if (capTop) Cap(rings[rings.Count - 1], seg, up: !inward);
            if (capBottom) Cap(rings[0], seg, up: inward);
        }

        void Cap(Ring r, int seg, bool up)
        {
            var c = _v.Count;
            _v.Add(new Vector3(r.cx, r.y, r.cz) * In); _uv.Add(new Vector2(0.5f, 0.5f));
            for (var j = 0; j <= seg; j++)
            {
                var a = -j * Mathf.PI * 2 / seg;
                var p = SuperEllipse(r, a);
                _v.Add(p * In);
                _uv.Add(new Vector2((p.x - r.cx) / (2 * r.rx) + 0.5f, (p.z - r.cz) / (2 * r.rz) + 0.5f));
            }
            for (var j = 0; j < seg; j++)
            {
                if (up) { _t.Add(c); _t.Add(c + 1 + j); _t.Add(c + 2 + j); }
                else { _t.Add(c); _t.Add(c + 2 + j); _t.Add(c + 1 + j); }
            }
        }

        // Round tube along a polyline (inches), with end caps.
        public void Tube(IList<Vector3> path, float radius, int seg = 16, bool caps = true)
        {
            var start = _v.Count;
            var frames = new List<(Vector3 n, Vector3 b)>();
            var prevN = Vector3.zero;
            for (var i = 0; i < path.Count; i++)
            {
                var tan = (i == 0 ? path[1] - path[0] : i == path.Count - 1 ? path[i] - path[i - 1] : path[i + 1] - path[i - 1]).normalized;
                var n = prevN == Vector3.zero ? Vector3.Cross(tan, Mathf.Abs(tan.y) < 0.9f ? Vector3.up : Vector3.right).normalized
                                              : Vector3.ProjectOnPlane(prevN, tan).normalized;
                prevN = n;
                frames.Add((n, Vector3.Cross(tan, n).normalized));
            }
            for (var i = 0; i < path.Count; i++)
            for (var j = 0; j <= seg; j++)
            {
                var a = j * Mathf.PI * 2 / seg;
                var off = (frames[i].n * Mathf.Cos(a) + frames[i].b * Mathf.Sin(a)) * radius;
                _v.Add((path[i] + off) * In);
                _uv.Add(new Vector2(j / (float)seg, i / (float)path.Count));
            }
            var row = seg + 1;
            for (var i = 0; i < path.Count - 1; i++)
            for (var j = 0; j < seg; j++)
            {
                int a = start + i * row + j, b = a + row, c = b + 1, d = a + 1;
                _t.Add(a); _t.Add(c); _t.Add(b); _t.Add(a); _t.Add(d); _t.Add(c);
            }
            if (!caps) return;
            foreach (var (idx, sign) in new[] { (0, -1f), (path.Count - 1, 1f) })
            {
                var c = _v.Count; _v.Add(path[idx] * In); _uv.Add(Vector2.zero);
                for (var j = 0; j <= seg; j++) { _v.Add(_v[start + idx * row + j]); _uv.Add(Vector2.zero); }
                for (var j = 0; j < seg; j++)
                {
                    if (sign > 0) { _t.Add(c); _t.Add(c + 1 + j); _t.Add(c + 2 + j); }
                    else { _t.Add(c); _t.Add(c + 2 + j); _t.Add(c + 1 + j); }
                }
            }
        }

        // Disc/cylinder with axis along `axis`, centered at `center` (inches).
        public void Cylinder(Vector3 center, Vector3 axis, float radius, float length, int seg = 32)
        {
            axis.Normalize();
            Tube(new[] { center - axis * length / 2, center + axis * length / 2 }, radius, seg);
        }

        public static List<Vector3> Arc(Vector3 center, Vector3 from, Vector3 axis, float degrees, int steps = 12)
        {
            var pts = new List<Vector3>();
            for (var i = 0; i <= steps; i++)
                pts.Add(center + Quaternion.AngleAxis(degrees * i / steps, axis) * (from - center));
            return pts;
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (_v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(_v); m.SetUVs(0, _uv); m.SetTriangles(_t, 0);
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }
    }
}
