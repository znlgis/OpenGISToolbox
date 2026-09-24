using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Managed Voronoi / Delaunay geometry, implemented in the application layer so the
/// tools do not depend on an engine (OGU) release. The Voronoi diagram is built by
/// clipping each site's cell against the perpendicular-bisector half-planes of every
/// other site and then against the extent rectangle — a convex intersection that
/// always tiles the extent. Clipping to a closed extent is deliberate: it avoids the
/// "open outer cells balloon to the whole frame / overlap" failure seen when Voronoi
/// cells are not clipped to a real boundary. Delaunay uses Bowyer–Watson insertion.
/// </summary>
internal static class TriangulationOps
{
    public readonly struct Pt
    {
        public readonly double X;
        public readonly double Y;
        public Pt(double x, double y) { X = x; Y = y; }
    }

    // ─── areas / hull ───

    /// <summary>Shoelace absolute area of a simple polygon.</summary>
    public static double PolygonArea(IReadOnlyList<Pt> ring)
    {
        if (ring.Count < 3) return 0;
        double a = 0;
        for (var i = 0; i < ring.Count; i++)
        {
            var p = ring[i];
            var q = ring[(i + 1) % ring.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return Math.Abs(a) / 2.0;
    }

    /// <summary>Andrew's monotone-chain convex hull (CCW, no duplicate closing vertex).</summary>
    public static List<Pt> ConvexHull(IReadOnlyList<Pt> points)
    {
        var pts = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        var n = pts.Count;
        if (n <= 2) return pts;
        var hull = new List<Pt>();
        // lower
        for (var i = 0; i < n; i++)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], pts[i]) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(pts[i]);
        }
        // upper
        var lower = hull.Count + 1;
        for (var i = n - 2; i >= 0; i--)
        {
            while (hull.Count >= lower && Cross(hull[^2], hull[^1], pts[i]) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(pts[i]);
        }
        hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    private static double Cross(Pt o, Pt a, Pt b)
        => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    // ─── Voronoi (half-plane clipping) ───

    /// <summary>
    /// Computes the Voronoi cell polygon for each site within the given extent
    /// (minx, miny, maxx, maxy). Returns one WKT POLYGON per site whose cell is
    /// non-degenerate; <paramref name="siteToIndex"/> maps input order to output
    /// feature order is preserved by the caller.
    /// </summary>
    public static List<(int SiteIndex, Pt Site, string Wkt)> VoronoiCells(
        IReadOnlyList<Pt> sites, double minx, double miny, double maxx, double maxy)
    {
        var result = new List<(int, Pt, string)>();
        if (sites.Count == 0) return result;
        var bbox = RectangleRing(minx, miny, maxx, maxy);

        for (var i = 0; i < sites.Count; i++)
        {
            var cell = new List<Pt>(bbox);
            var s = sites[i];
            for (var j = 0; j < sites.Count && cell.Count >= 3; j++)
            {
                if (j == i) continue;
                cell = ClipHalfPlane(cell, s, sites[j]);
            }
            if (cell.Count >= 3)
            {
                var area = PolygonArea(cell);
                if (area > 0) result.Add((i, s, ToPolygonWkt(cell)));
            }
        }
        return result;
    }

    /// <summary>Keeps the part of <paramref name="poly"/> closer to site S than to site O.</summary>
    private static List<Pt> ClipHalfPlane(List<Pt> poly, Pt s, Pt o)
    {
        // Points P with |P-S|^2 <= |P-O|^2 expand to  a*P.x + b*P.y <= c
        double a = 2 * (o.X - s.X);
        double b = 2 * (o.Y - s.Y);
        double c = o.X * o.X + o.Y * o.Y - s.X * s.X - s.Y * s.Y;
        if (Math.Abs(a) < 1e-12 && Math.Abs(b) < 1e-12) return poly; // coincident sites

        var output = new List<Pt>();
        var n = poly.Count;
        for (var idx = 0; idx < n; idx++)
        {
            var cur = poly[idx];
            var nxt = poly[(idx + 1) % n];
            var curIn = a * cur.X + b * cur.Y <= c + 1e-12;
            var nxtIn = a * nxt.X + b * nxt.Y <= c + 1e-12;
            if (curIn)
            {
                output.Add(cur);
                if (!nxtIn) output.Add(Intersect(cur, nxt, a, b, c));
            }
            else if (nxtIn)
            {
                output.Add(Intersect(cur, nxt, a, b, c));
            }
        }
        return output;

        static Pt Intersect(Pt p1, Pt p2, double a, double b, double c)
        {
            double d1 = a * p1.X + b * p1.Y - c;
            double d2 = a * p2.X + b * p2.Y - c;
            double t = d1 / (d1 - d2);
            return new Pt(p1.X + t * (p2.X - p1.X), p1.Y + t * (p2.Y - p1.Y));
        }
    }

    private static List<Pt> RectangleRing(double minx, double miny, double maxx, double maxy) => new()
    {
        new Pt(minx, miny), new Pt(maxx, miny), new Pt(maxx, maxy), new Pt(minx, maxy)
    };

    private static string ToPolygonWkt(List<Pt> ring)
    {
        string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        var pts = ring.Select(p => $"{F(p.X)} {F(p.Y)}").ToList();
        pts.Add($"{F(ring[0].X)} {F(ring[0].Y)}"); // close
        return "POLYGON ((" + string.Join(",", pts) + "))";
    }

    // ─── Delaunay (Bowyer–Watson) ───

    public readonly struct Tri
    {
        public readonly int A, B, C;
        public Tri(int a, int b, int c) { A = a; B = b; C = c; }
    }

    /// <summary>Delaunay triangulation of the input points as index triangles.</summary>
    public static List<Tri> Delaunay(IReadOnlyList<Pt> points)
    {
        var n = points.Count;
        var tris = new List<Tri>();
        if (n < 3) return tris;

        double minx = points.Min(p => p.X), maxx = points.Max(p => p.X);
        double miny = points.Min(p => p.Y), maxy = points.Max(p => p.Y);
        double dx = maxx - minx, dy = maxy - miny;
        double dmax = Math.Max(dx, dy);
        if (dmax <= 0) return tris; // all coincident
        double midx = (minx + maxx) / 2, midy = (miny + maxy) / 2;

        // Append super-triangle vertices, scaled well beyond the point set.
        var all = new List<Pt>(points)
        {
            new Pt(midx - 20 * dmax, midy - dmax),
            new Pt(midx, midy + 20 * dmax),
            new Pt(midx + 20 * dmax, midy - dmax)
        };
        int s0 = n, s1 = n + 1, s2 = n + 2;
        tris.Add(new Tri(s0, s1, s2));

        for (var i = 0; i < n; i++)
        {
            var p = all[i];
            var bad = new List<Tri>();
            foreach (var t in tris)
                if (InCircumcircle(all[t.A], all[t.B], all[t.C], p)) bad.Add(t);

            // Boundary edges = edges belonging to exactly one bad triangle.
            var edges = new List<(int, int)>();
            foreach (var t in bad)
            {
                edges.Add((t.A, t.B));
                edges.Add((t.B, t.C));
                edges.Add((t.C, t.A));
            }
            var boundary = edges
                .GroupBy(e => (Math.Min(e.Item1, e.Item2), Math.Max(e.Item1, e.Item2)))
                .Where(g => g.Count() == 1)
                .Select(g => g.First())
                .ToList();

            foreach (var t in bad) tris.Remove(t);
            foreach (var (a, b) in boundary) tris.Add(new Tri(a, b, i));
        }

        return tris.Where(t => t.A < n && t.B < n && t.C < n).ToList();
    }

    private static bool InCircumcircle(Pt a, Pt b, Pt c, Pt d)
    {
        // Ensure CCW orientation of (a,b,c); sign of determinant then tells inside.
        double orient = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (Math.Abs(orient) < 1e-12) return false; // degenerate (collinear)
        double adx = a.X - d.X, ady = a.Y - d.Y;
        double bdx = b.X - d.X, bdy = b.Y - d.Y;
        double cdx = c.X - d.X, cdy = c.Y - d.Y;
        double alift = adx * adx + ady * ady;
        double blift = bdx * bdx + bdy * bdy;
        double clift = cdx * cdx + cdy * cdy;
        double det = orient * (alift * (bdx * cdy - bdy * cdx)
                             - blift * (adx * cdy - ady * cdx)
                             + clift * (adx * bdy - ady * bdx));
        return det > 0;
    }
}
