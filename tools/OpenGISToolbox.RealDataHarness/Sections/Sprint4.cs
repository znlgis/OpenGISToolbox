using OpenGISToolbox.TestKit;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGIS.Utils.Geometry;

namespace OpenGISToolbox.RealDataHarness.Sections;

/// <summary>
/// Sprint-4 managed Voronoi / Delaunay against real point data. The cross-checks
/// are independent of the tools: the Voronoi cell areas must exactly tile the
/// sites' bounding box, and the Delaunay triangles must cover the convex hull
/// area computed here via a separate monotone-chain.
/// </summary>
public static class Sprint4
{
    public static async Task Run()
    {
        Program.Checks.BeginSection("13. Sprint-4 triangulation sweep");

        var pt = Program.Catalog.Point;
        var dir = GdalEnv.Dir("harness", "sprint4");
        if (pt == null)
        {
            Program.Checks.Skip("triangulation sweep", "no point layer available");
            return;
        }

        // Subsample to a manageable, distinct coordinate set for O(n²) managed geometry.
        var src = GdalEnv.ReadLayer(pt.ShpPath);
        var sites = new List<(double X, double Y)>();
        var seen = new HashSet<string>();
        foreach (var f in src.Features)
        {
            if (string.IsNullOrEmpty(f.Wkt)) continue;
            var xy = GeometryOps_Extract(f.Wkt);
            if (xy == null) continue;
            var key = $"{xy.Value.X:R},{xy.Value.Y:R}";
            if (!seen.Add(key)) continue;
            sites.Add(xy.Value);
            if (sites.Count >= 60) break;
        }
        if (sites.Count < 3)
        {
            Program.Checks.Skip("triangulation sweep", "fewer than 3 distinct points");
            return;
        }

        // Write the subsampled sites to a fresh point layer the tools can read.
        var input = Path.Combine(dir, "sites.geojson");
        var layer = new OguLayer { Name = "sites", GeometryType = GeometryType.POINT, Wkid = src.Wkid };
        layer.AddField(new OguField { Name = "sid", DataType = FieldDataType.INTEGER });
        int fid = 0;
        foreach (var (x, y) in sites)
        {
            var feat = new OguFeature
            {
                Fid = fid,
                Wkt = $"POINT ({x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} " +
                       $"{y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)})"
            };
            feat.SetValue("sid", fid);
            layer.AddFeature(feat);
            fid++;
        }
        OguLayerUtil.WriteLayer(DataFormatType.GEOJSON, layer, input);

        double minx = sites.Min(s => s.X), maxx = sites.Max(s => s.X);
        double miny = sites.Min(s => s.Y), maxy = sites.Max(s => s.Y);
        double bboxArea = (maxx - minx) * (maxy - miny);

        // Voronoi must exactly tile the extent.
        await Program.Checks.RunAsync("voronoi tiles extent", async () =>
        {
            var outPath = Path.Combine(dir, "voronoi.shp");
            await Program.RunRegisteredAsync("voronoi", new()
            {
                ["input"] = input, ["output"] = outPath
            }, "voronoi");
            var l = GdalEnv.ReadLayer(outPath);
            double sum = l.Features.Where(f => !string.IsNullOrEmpty(f.Wkt))
                .Sum(f => GeometryUtil.AreaWkt(f.Wkt!));
            Program.Require((int)l.GetFeatureCount() == sites.Count, "one cell per distinct site",
                $"cells={l.GetFeatureCount()} sites={sites.Count}");
            double rel = Math.Abs(sum - bboxArea) / Math.Max(bboxArea, 1e-12);
            Program.RequireWithWarn(rel < 1e-6, "Voronoi cell areas tile the extent",
                $"sum={sum:R} bbox={bboxArea:R} rel={rel:E2}");
        });

        // Delaunay must cover the convex hull area (independently computed).
        await Program.Checks.RunAsync("delaunay covers hull", async () =>
        {
            var outPath = Path.Combine(dir, "delaunay.shp");
            await Program.RunRegisteredAsync("delaunay", new()
            {
                ["input"] = input, ["output"] = outPath
            }, "delaunay");
            var l = GdalEnv.ReadLayer(outPath);
            int tris = (int)l.GetFeatureCount();
            Program.Require(tris >= 1, "delaunay produced triangles", $"n={tris}");
            double sum = l.Features.Where(f => !string.IsNullOrEmpty(f.Wkt))
                .Sum(f => GeometryUtil.AreaWkt(f.Wkt!));
            double hull = HullArea(sites);
            double rel = Math.Abs(sum - hull) / Math.Max(hull, 1e-12);
            Program.RequireWithWarn(rel < 1e-6, "Delaunay area == convex hull area",
                $"triSum={sum:R} hull={hull:R} rel={rel:E2}");
        });
    }

    private static (double X, double Y)? GeometryOps_Extract(string wkt)
    {
        var coords = LayerCompare.ParseCoords(wkt);
        return coords.Count > 0 ? (coords[0].X, coords[0].Y) : null;
    }

    // Independent Andrew monotone-chain convex hull area (shoelace over the hull).
    private static double HullArea(List<(double X, double Y)> pts)
    {
        var p = pts.OrderBy(a => a.X).ThenBy(a => a.Y).ToList();
        int n = p.Count;
        if (n <= 2) return 0;
        double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b)
            => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var hull = new List<(double X, double Y)>();
        for (var i = 0; i < n; i++)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], p[i]) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(p[i]);
        }
        int lower = hull.Count + 1;
        for (var i = n - 2; i >= 0; i--)
        {
            while (hull.Count >= lower && Cross(hull[^2], hull[^1], p[i]) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(p[i]);
        }
        hull.RemoveAt(hull.Count - 1);
        double a2 = 0;
        for (var i = 0; i < hull.Count; i++)
        {
            var q = hull[i]; var r = hull[(i + 1) % hull.Count];
            a2 += q.X * r.Y - r.X * q.Y;
        }
        return Math.Abs(a2) / 2.0;
    }
}
