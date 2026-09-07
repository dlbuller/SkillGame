using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using SkillGame;

namespace SkillGameWpf
{
    public partial class GameStatusView
    {
        // ---- Ball physics ------------------------------------------------------------------------------
        // The board's black rails become a collision bitmap the ball can't pass through; gravity pulls it down the slants and it zig-zags, like the real Skill-Roll.
        private void BuildCollisionMap()
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit(); bmp.UriSource = new Uri(@"C:\SkillGame\playfield.png");
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bmp.EndInit();
                var scaled = new System.Windows.Media.Imaging.TransformedBitmap(bmp,
                    new ScaleTransform((double)PfW / bmp.PixelWidth, (double)PfH / bmp.PixelHeight));
                var conv = new System.Windows.Media.Imaging.FormatConvertedBitmap(scaled, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight;
                var px = new byte[w * h * 4]; conv.CopyPixels(px, w * 4, 0);
                var rail = new bool[w, h];
                for (int yy = 0; yy < h; yy++)
                    for (int xx = 0; xx < w; xx++)
                    {
                        int i = (yy * w + xx) * 4;
                        rail[xx, yy] = px[i] < 72 && px[i + 1] < 72 && px[i + 2] < 72;   // near-black = a rail
                    }
                // Ignore the header art (score reels/scale/instructions) and the outer frame — not playfield rails.
                for (int yy = 0; yy < h; yy++)
                    for (int xx = 0; xx < w; xx++)
                        if (yy < 118 || yy > h - 6 || xx < 5 || xx > w - 5) rail[xx, yy] = false;
                // Red arrows are direction hints, not rails; clear them and their shadow so only the black rails block the coin.
                const int rad = 6;
                for (int yy = 0; yy < h; yy++)
                    for (int xx = 0; xx < w; xx++)
                    {
                        int i = (yy * w + xx) * 4;
                        if (px[i + 2] > 150 && px[i + 1] < 90 && px[i] < 90)   // bright red
                            for (int dy = -rad; dy <= rad; dy++)
                                for (int dx = -rad; dx <= rad; dx++)
                                {
                                    int nx = xx + dx, ny = yy + dy;
                                    if (nx >= 0 && nx < w && ny >= 0 && ny < h) rail[nx, ny] = false;
                                }
                    }
                // Gobble/drain holes are solid black filled circles, not rails; clear them so the coin skims over them (black all around at radius 7 = a filled blob).
                var core = new bool[w, h];
                for (int yy = 7; yy < h - 7; yy++)
                    for (int xx = 7; xx < w - 7; xx++)
                        if (rail[xx, yy] && rail[xx + 7, yy] && rail[xx - 7, yy] && rail[xx, yy + 7] && rail[xx, yy - 7]
                            && rail[xx + 5, yy + 5] && rail[xx - 5, yy - 5] && rail[xx + 5, yy - 5] && rail[xx - 5, yy + 5])
                            core[xx, yy] = true;
                for (int yy = 0; yy < h; yy++)
                    for (int xx = 0; xx < w; xx++)
                        if (core[xx, yy])
                            for (int dy = -14; dy <= 14; dy++)
                                for (int dx = -14; dx <= 14; dx++)
                                {
                                    int nx = xx + dx, ny = yy + dy;
                                    if (nx >= 0 && nx < w && ny >= 0 && ny < h) rail[nx, ny] = false;
                                }
                _rail = rail; _railW = w; _railH = h;
                BuildComponents();   // label each rail as a component so the coin can lock onto the one it's on
            }
            catch (Exception ex) { Log.Error("collision map build failed", ex); _rail = null; }
        }

        // Momentum + gravity engine: a flipper fires the coin up the scoring rail until it drops through the hole, then it rolls down the slanted rail below to the next flipper; rails are connected components so the coin can't hop rails.
        private int[,]? _comp;                                       // connected-component id per cell
        private readonly Dictionary<int, int[]> _compTop = new();    // comp -> per-column topmost y (-1 = none)
        private readonly Dictionary<int, int> _compSize = new(), _compLow = new(), _compW = new();   // _compW = horizontal width of the rail (guide vs. narrow cup)
        private List<(double x, double y)> _coinPath = new();        // precomputed smooth path for the current shot
        private double[] _cum = System.Array.Empty<double>();        // cumulative arc length along _coinPath
        private double _coinDist, _coinLen, _restX = 34, _restY = 180, _dropDist, _coinStep = 3.0;
        private string? _pendingHole; private bool _pendingFired; private bool _soundFired;
        private const double CoinShotTicks = 135;                    // each shot animates over ~2.2s regardless of length

        private void BuildComponents()
        {
            _comp = null; _compTop.Clear(); _compSize.Clear(); _compLow.Clear(); _compW.Clear();
            if (_rail == null) return;
            int w = _railW, h = _railH;
            var comp = new int[w, h];
            var stack = new Stack<(int, int)>();
            int next = 1;
            for (int sy = 0; sy < h; sy++)
                for (int sx = 0; sx < w; sx++)
                {
                    if (!_rail[sx, sy] || comp[sx, sy] != 0) continue;
                    int id = next++; comp[sx, sy] = id; stack.Push((sx, sy));
                    var top = new int[w]; for (int k = 0; k < w; k++) top[k] = -1;
                    int size = 0;
                    while (stack.Count > 0)
                    {
                        var (cx, cy) = stack.Pop(); size++;
                        if (top[cx] < 0 || cy < top[cx]) top[cx] = cy;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = cx + dx, ny = cy + dy;
                                if (nx >= 0 && nx < w && ny >= 0 && ny < h && _rail[nx, ny] && comp[nx, ny] == 0)
                                { comp[nx, ny] = id; stack.Push((nx, ny)); }
                            }
                    }
                    _compTop[id] = top; _compSize[id] = size;
                    int lowCol = -1, lowY = -1, minCol = -1, maxCol = -1;
                    for (int k = 0; k < w; k++)
                    {
                        if (top[k] > lowY) { lowY = top[k]; lowCol = k; }
                        if (top[k] >= 0) { if (minCol < 0) minCol = k; maxCol = k; }
                    }
                    _compLow[id] = lowCol;
                    _compW[id] = (minCol >= 0) ? (maxCol - minCol + 1) : 0;
                }
            _comp = comp;
        }

        // Tight surface: topmost rail pixel within a small band of the coin's base (stays on THIS rail).
        private double Ts(double x, double cy, int win = 8)
        {
            int xi = (int)Math.Round(x);
            if (_rail == null || xi < 3 || xi >= _railW - 3) return double.NaN;
            double b = cy + BallR;
            for (int off = 0; off <= win; off++)
            {
                int s1 = (int)(b + off), s2 = (int)(b - off);
                if (s1 >= 0 && s1 < _railH && _rail[xi, s1]) return s1;
                if (s2 >= 0 && s2 < _railH && _rail[xi, s2]) return s2;
            }
            return double.NaN;
        }
        private double SlopeAt(double x, double cy)
        {
            double s1 = Ts(x - 3, cy), s2 = Ts(x + 3, cy);
            if (double.IsNaN(s1) || double.IsNaN(s2)) return 0;
            return (s2 - s1) / 6.0;
        }
        private bool RailAhead(double x, double cy, int d)
        {
            for (int dx = 5; dx < 56; dx++) if (!double.IsNaN(Ts(x + d * dx, cy))) return true;   // skim across wider hole gaps
            return false;
        }
        // First BIG rail below (skips the small hole-cups, and optionally the rail the coin is already on).
        private (double y, int lab) CompBelow(double x, double y, int minsize = 800, int exclude = -1)
        {
            int xi = (int)Math.Round(x);
            if (_rail == null || _comp == null || xi < 0 || xi >= _railW) return (double.NaN, -1);
            int from = (int)(y + BallR + 2), to = Math.Min(_railH - 1, (int)(y + 180));
            for (int yy = from; yy <= to; yy++)
                if (_rail[xi, yy]) { int l = _comp[xi, yy]; if (l != exclude && _compSize.TryGetValue(l, out var sz) && sz >= minsize) return (yy, l); }
            return (double.NaN, -1);
        }

        // Shoot the coin along the scoring rail; gravity bleeds its momentum; it skims other holes and drops in the target.
        private (List<(double, double)> pts, double ex, double ey) FireSim(double x, double y, double vx, double targetX, bool force)
        {
            var pts = new List<(double, double)> { (x, y) };
            for (int it = 0; it < 800; it++)
            {
                if (force && Math.Abs(x - targetX) < 9) return (pts, x, y);   // reached the scored hole -> drop in
                vx += 1.3 * SlopeAt(x, y); vx *= 0.994;
                double nx = x + vx; int d = vx >= 0 ? 1 : -1;
                double s = Ts(nx, y);
                if (double.IsNaN(s))
                {
                    if (Math.Abs(nx - targetX) < 14) return (pts, x, y);   // drop into the target hole
                    if (RailAhead(nx, y, d)) x = nx;                       // skim across other holes (keep height)
                    else return (pts, x, y);                              // ran off the rail end
                }
                else
                {
                    double ny = s - BallR;
                    if (Math.Abs(ny - y) > 8) ny = y + (ny > y ? 8 : -8);
                    x = nx; y = ny;
                }
                if (x < BallR) { x = BallR; vx = -vx * 0.5; }
                if (x > _railW - BallR) { x = _railW - BallR; vx = -vx * 0.5; }
                pts.Add((x, y));
                if (Math.Abs(vx) < 0.18) return (pts, x, y);
            }
            return (pts, x, y);
        }
        // Smooth Catmull-Rom polyline through the waypoints (endpoints duplicated).
        private static List<(double, double)> Catmull(List<(double x, double y)> way, int per = 12)
        {
            var outp = new List<(double, double)>();
            if (way.Count < 2) { outp.AddRange(way); return outp; }
            var P = new List<(double x, double y)> { way[0] }; P.AddRange(way); P.Add(way[^1]);
            outp.Add(way[0]);
            for (int i = 1; i < P.Count - 2; i++)
            {
                var p0 = P[i - 1]; var p1 = P[i]; var p2 = P[i + 1]; var p3 = P[i + 2];
                for (int k = 1; k <= per; k++)
                {
                    double t = (double)k / per, t2 = t * t, t3 = t2 * t;
                    double px = 0.5 * ((2 * p1.x) + (-p0.x + p2.x) * t + (2 * p0.x - 5 * p1.x + 4 * p2.x - p3.x) * t2 + (-p0.x + 3 * p1.x - 3 * p2.x + p3.x) * t3);
                    double py = 0.5 * ((2 * p1.y) + (-p0.y + p2.y) * t + (2 * p0.y - 5 * p1.y + 4 * p2.y - p3.y) * t2 + (-p0.y + 3 * p1.y - 3 * p2.y + p3.y) * t3);
                    outp.Add((px, py));
                }
            }
            return outp;
        }

        // The scoring-rail height at column x for a level, from its cups (the coin rides ON TOP of this rail).
        private double CupLevelY(double x, List<(double x, double y)> cups)
        {
            if (cups.Count == 0) return 200;
            if (x <= cups[0].x) return cups[0].y;
            if (x >= cups[^1].x) return cups[^1].y;
            for (int i = 0; i < cups.Count - 1; i++)
                if (x >= cups[i].x && x <= cups[i + 1].x)
                {
                    double t = (x - cups[i].x) / Math.Max(1, cups[i + 1].x - cups[i].x);
                    return cups[i].y + (cups[i + 1].y - cups[i].y) * t;
                }
            return cups[^1].y;
        }

        // Flipper shot to the scored hole: the coin rides on top of the rail (never through it), climbing over humps and up to the scoring level, skimming non-target holes, ending on top of the target cup.
        private (List<(double, double)> pts, double ex, double ey) FireRide(double x, double y, double targetX, double targetY, int level)
        {
            int dirn = targetX > x ? 1 : -1;
            var cups = new List<(double x, double y)>();
            foreach (var sw in SwitchMap.All)
                if (sw.Kind == SwitchKind.Score && sw.Level == level && CupPos.TryGetValue(sw.Id, out var c)) cups.Add(c);
            cups.Sort((a, b) => a.x.CompareTo(b.x));
            var pts = new List<(double, double)> { (x, y) };
            double lastY = y; int g = 0;
            while (((dirn > 0 && x < targetX - 2) || (dirn < 0 && x > targetX + 2)) && g < 800)
            {
                g++; x += dirn * 2;
                double clY = CupLevelY(x, cups) - BallR;         // where the coin SHOULD be (on top of the scoring rail)
                bool overHole = false;                           // skim across a non-target hole (roll over the gap)
                foreach (var c in cups) if (Math.Abs(c.x - targetX) > 3 && Math.Abs(x - c.x) < 13) { overHole = true; break; }
                if (!overHole)
                {
                    // if riding well below the scoring level, search upward for the scoring rail and climb to it
                    int win = lastY - clY > 10 ? 22 : 11;
                    double s = Ts(x, lastY, win);
                    if (!double.IsNaN(s))
                    {
                        double py = s - BallR;
                        if (py - lastY > 4) py = lastY + 4;      // sink cap — don't plunge to a far-lower rail
                        else if (lastY - py > 8) py = lastY - 8; // climb cap — up over humps / onto the scoring rail
                        lastY = py;
                    }
                    // else: no rail near (a gap) -> hold height (skim)
                }
                pts.Add((x, lastY));
            }
            double se = Ts(targetX, lastY, 16); double ey = double.IsNaN(se) ? lastY : se - BallR;
            pts.Add((targetX, ey));
            return (pts, targetX, ey);
        }

        // The highest y (smallest) the coin may reach on a level = just above its highest hole. Blocks jumping up a level.
        private double LevelCeiling(int level)
        {
            double c = double.MaxValue;
            foreach (var sw in SwitchMap.All)
                if (sw.Kind == SwitchKind.Score && sw.Level == level && _holePos.TryGetValue(sw.Id, out var q) && q.y < c) c = q.y;
            return (c > 1e8 ? 120 : c) - 8;
        }

        private double Calib(double x, double y, double targetX)
        {
            bool right = targetX > x; double lo = 0.6, hi = 20.0;
            for (int i = 0; i < 20; i++)
            {
                double v = (lo + hi) / 2; int s = right ? 1 : -1;
                var (pp, _, _) = FireSim(x, y, s * v, targetX, false);
                double reach = right ? -1e9 : 1e9;
                foreach (var p in pp) reach = right ? Math.Max(reach, p.Item1) : Math.Min(reach, p.Item1);
                if ((right && reach < targetX) || (!right && reach > targetX)) lo = v; else hi = v;
            }
            return (right ? 1 : -1) * (lo + hi) / 2;
        }
        // Roll along the landed rail toward the next-flipper side, downhill only to its lowest end, with an arced drop-on.
        private (List<(double, double)> pts, double ex, double ey) FollowDir(double x, int lab, double startY, int dirn)
        {
            var pts = new List<(double, double)>();
            if (!_compTop.TryGetValue(lab, out var top)) { pts.Add((x, startY)); return (pts, x, startY); }
            int xi = (int)Math.Round(x);
            double landY = ((xi >= 0 && xi < _railW && top[xi] >= 0) ? top[xi] : startY + BallR) - BallR;
            double dropH = Math.Max(0, landY - startY), arcW = Math.Min(26, dropH * 0.5);   // arced drop, not dead-straight
            for (int i = 1; i <= 6; i++)
            {
                double t = (double)i / 6.0, ax = Math.Max(4, Math.Min(_railW - 4, x + dirn * arcW * t));
                pts.Add((ax, startY + (landY - startY) * t * t));
            }
            xi = (int)Math.Round(Math.Max(4, Math.Min(_railW - 4, x + dirn * arcW)));
            double cy = (xi >= 0 && xi < _railW && top[xi] >= 0) ? top[xi] - BallR : landY; int g = 0;
            while (g < 1300)
            {
                g++; int nxt = xi + dirn;
                if (nxt < 4 || nxt > _railW - 4 || top[nxt] < 0) break;   // rail ends on this side
                double ny = top[nxt] - BallR;
                if (ny < cy - 3) break;                                    // would climb -> the low point, stop
                xi = nxt; cy = ny; pts.Add((xi, cy));
            }
            return (pts, xi, cy);
        }

        private double Clx(double v) => Math.Max(4, Math.Min(_railW - 4, v));

        // Nearest rail pixel near the coin's base (base-up .. base+down); null = free space under the coin.
        private int? RailUnder(double x, double y, int down = 4, int up = 0)
        {
            int xi = (int)Math.Round(x);
            if (_rail == null || xi < 3 || xi >= _railW - 3) return null;
            int bas = (int)(y + BallR);
            for (int off = -up; off <= down; off++)
            {
                int b = bas + off;
                if (b >= 0 && b < _railH && _rail[xi, b]) return b;
            }
            return null;
        }

        // Local slope of the rail under the coin (+ = descending toward +x).
        private double DescSlope(double x, double y)
        {
            var a = RailUnder(x - 3, y, 10, 10); var b = RailUnder(x + 3, y, 10, 10);
            if (a == null || b == null) return 0.0;
            return (b.Value - a.Value) / 6.0;
        }

        // Descend to the next flipper riding on top of the rails: roll toward the flipper, and where the rail ends fall straight down through the gap to the next rail's top; capped at targetY so it never reaches the next scoring row.
        private (List<(double, double)> pts, double ex, double ey) DescendSim(double x, double y, int dirn, double targetY)
        {
            var pts = new List<(double, double)>();
            double cap = targetY;
            // DROP THROUGH the scored hole: start the descent just BELOW the scoring rail the cup is in.
            if (_rail != null)
            {
                int xh = (int)Math.Round(x), bh = (int)(y + BallR), ry = -1;
                for (int yy = Math.Max(0, bh - 6); yy < Math.Min(_railH, bh + 42); yy++)
                    if (xh >= 0 && xh < _railW && _rail[xh, yy]) { ry = yy; break; }
                if (ry >= 0) { int yy = ry; while (yy < _railH && _rail[xh, yy]) yy++; y = yy - BallR; }
            }
            int g = 0;
            while (g < 3000)
            {
                g++;
                if (y >= cap - 1) { y = cap; pts.Add((x, y)); break; }
                if ((dirn > 0 && x >= _railW - 26) || (dirn < 0 && x <= 26)) break;
                var under = RailUnder(x, y, 3, 0);
                if (under != null)                                        // ---- ON a rail: ROLL toward the flipper (on top) ----
                {
                    y = Math.Min(under.Value - BallR, cap);
                    double nx = Clx(x + dirn * 2);
                    var ns = RailUnder(nx, y - 3, 12, 0);                 // surface at next x, at/just below current level
                    if (ns != null && (ns.Value - BallR) >= y - 2) { x = nx; y = Math.Min(ns.Value - BallR, cap); }
                    else x = nx;                                          // rolled off the end -> next iter falls
                    pts.Add((x, y));
                }
                else                                                      // ---- ran off the rail: FALL STRAIGHT DOWN through the gap ----
                {
                    int xi = (int)Math.Round(x), land = -1;
                    for (int yy = (int)(y + BallR + 1); yy < Math.Min(_railH, (int)(cap + BallR + 2)); yy++)
                        if (xi >= 0 && xi < _railW && _rail![xi, yy]) { land = yy; break; }
                    double ny = (land >= 0) ? Math.Min(land - BallR, cap) : cap;
                    for (double fy = y + 5; fy < ny; fy += 5) pts.Add((x, fy));   // vertical fall points (through the gap)
                    y = ny; pts.Add((x, y));
                }
            }
            if (pts.Count == 0) pts.Add((x, y));
            return (pts, x, y);
        }

        // Known flipper rest positions (odd = left, even = right), traced from the verified walk-through.
        private static readonly Dictionary<int, (double x, double y)> FlipperPos = new()
        {
            [1] = (9, 182), [2] = (421, 257), [3] = (14, 316), [4] = (413, 385),
            [5] = (16, 445), [6] = (422, 510), [7] = (15, 568), [8] = (421, 618),
        };

        // Dan's hand-drawn exact paths keyed by switch id; when present the coin follows the drawn line verbatim, no physics.
        private readonly Dictionary<string, List<(double, double)>> _drawnPaths = new();
        private void LoadDrawnPaths()
        {
            _drawnPaths.Clear();
            try
            {
                string dir = @"C:\SkillGame\paths";
                if (!System.IO.Directory.Exists(dir)) return;
                foreach (var f in System.IO.Directory.GetFiles(dir, "*.json"))
                {
                    try
                    {
                        string id = System.IO.Path.GetFileNameWithoutExtension(f).ToUpperInvariant();
                        var arr = System.Text.Json.JsonSerializer.Deserialize<double[][]>(System.IO.File.ReadAllText(f));
                        if (arr == null) continue;
                        var pl = new List<(double, double)>();
                        foreach (var p in arr) if (p.Length >= 2) pl.Add((p[0], p[1]));
                        if (pl.Count >= 2) _drawnPaths[id] = pl;
                    }
                    catch (Exception ex) { Log.Error("drawn path load failed: " + f, ex); }
                }
            }
            catch (Exception ex) { Log.Error("drawn paths dir load failed", ex); }
        }

        // Compute a hole's full path: use Dan's drawn one verbatim if present, else physics (fire from the flipper into the cup, drop through, roll to the next flipper).
        private List<(double, double)> ComputePath(string holeId, out int fireEnd)
        {
            if (_drawnPaths.TryGetValue(holeId.ToUpperInvariant(), out var drawn))
            {
                var copy = new List<(double, double)>(drawn);
                fireEnd = copy.Count - 1;
                if (CupPos.TryGetValue(holeId, out var cp))   // light the hole where the path passes its cup
                {
                    double bd = double.MaxValue;
                    for (int i = 0; i < copy.Count; i++)
                    {
                        double dd = (copy[i].Item1 - cp.x) * (copy[i].Item1 - cp.x) + (copy[i].Item2 - cp.y) * (copy[i].Item2 - cp.y);
                        if (dd < bd) { bd = dd; fireEnd = i; }
                    }
                }
                return copy;
            }
            fireEnd = 0;
            var pts = new List<(double, double)>();
            if (_rail == null || _comp == null) return pts;
            int L = Math.Max(1, LevelOf(holeId));
            var f = FlipperPos.TryGetValue(L, out var fp) ? fp : (x: 34.0, y: 180.0);
            double x = f.x, y = f.y;
            pts.Add((x, y));
            if (CupPos.TryGetValue(holeId, out var hp) || _holePos.TryGetValue(holeId, out hp))
            {
                var (fpp, fx, fy) = FireRide(x, y, hp.x, hp.y, L);
                pts.AddRange(fpp); x = fx; y = fy;
            }
            fireEnd = pts.Count - 1;
            int dirn = (L % 2 == 1) ? 1 : -1;                              // odd level -> right, even -> left
            (double x, double y)? next = FlipperPos.TryGetValue(L + 1, out var np) ? np : null;
            double targetY = next?.y ?? (_railH - 16);
            var (dp, dx, dy) = DescendSim(x, y, dirn, targetY);
            pts.AddRange(dp); x = dx; y = dy;
            if (next != null) pts.Add(next.Value);                          // end exactly at the next flipper
            for (int i = 0; i < pts.Count; i++)                             // safety: never leave the board
                pts[i] = (Math.Max(4, Math.Min(_railW - 4, pts[i].Item1)), Math.Max(112, Math.Min(_railH - 6, pts[i].Item2)));
            return pts;
        }

        // Build one shot for the live coin.
        private void BuildCoinPath(double fromX, double fromY, string? holeId)
        {
            if (_rail == null || _comp == null || holeId == null) return;
            var pts = ComputePath(holeId, out int fireEnd);
            if (pts.Count < 2) return;
            SetCoinPath(pts, fireEnd);
            _restX = pts[^1].Item1; _restY = pts[^1].Item2;
            _pendingHole = holeId; _pendingFired = false; _soundFired = false;
            if (WalkMode || PickerMode) DrawTrail(pts);
        }

        private Polyline? _trail;
        private void DrawTrail(List<(double, double)> pts)
        {
            if (_trail != null) PlayfieldCanvas.Children.Remove(_trail);
            _trail = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(0xC0, 0x00, 0xE0)), StrokeThickness = 2.5, IsHitTestVisible = false };
            foreach (var p in pts) _trail.Points.Add(new Point(p.Item1, p.Item2));
            PlayfieldCanvas.Children.Add(_trail);
        }

        // Preview a hole's path without firing the coin (draws the magenta trail; coin stays parked).
        private void PreviewHole(string holeId)
        {
            var pts = ComputePath(holeId, out _);
            if (pts.Count >= 2) DrawTrail(pts);
        }

        // ---- Multi-path overlay (checkbox picker): each switch draws its path in its own color ----
        private readonly Dictionary<string, Polyline> _pathTrails = new();
        private void AddColoredPath(string sid, Color c)
        {
            RemoveColoredPath(sid);
            var pts = ComputePath(sid, out _);
            if (pts.Count < 2) return;
            var pl = new Polyline { Stroke = new SolidColorBrush(c), StrokeThickness = 2.5, IsHitTestVisible = false };
            foreach (var p in pts) pl.Points.Add(new Point(p.Item1, p.Item2));
            PlayfieldCanvas.Children.Add(pl); _pathTrails[sid] = pl;
        }
        private void RemoveColoredPath(string sid)
        {
            if (_pathTrails.TryGetValue(sid, out var pl)) { PlayfieldCanvas.Children.Remove(pl); _pathTrails.Remove(sid); }
        }
        private static Color SwitchColor(string id)
        {
            int n = int.TryParse(id.Length > 1 ? id.Substring(1) : "0", out var v) ? v : 0;
            return HsvToColor((n * 360.0 / 13.0) % 360.0, 0.85, 1.0);   // spread hues so adjacent switches differ
        }
        private static Color HsvToColor(double h, double s, double v)
        {
            double c = v * s, x = c * (1 - Math.Abs((h / 60.0) % 2 - 1)), m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
            return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        private void SetCoinPath(List<(double, double)> pts, int fireEndIdx)
        {
            for (int i = 0; i < pts.Count; i++)   // safety: the coin can never leave the board
                pts[i] = (Math.Max(4, Math.Min(_railW - 4, pts[i].Item1)), Math.Max(112, Math.Min(_railH - 6, pts[i].Item2)));
            _coinPath = pts; _coinDist = 0;
            _cum = new double[pts.Count];
            double tot = 0; _cum[0] = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                double ddx = pts[i].Item1 - pts[i - 1].Item1, ddy = pts[i].Item2 - pts[i - 1].Item2;
                tot += Math.Sqrt(ddx * ddx + ddy * ddy); _cum[i] = tot;
            }
            _coinLen = tot;
            _coinStep = Math.Max(2.2, tot / CoinShotTicks);   // constant shot duration -> smooth, fits the demo step
            _dropDist = (fireEndIdx >= 0 && fireEndIdx < _cum.Length) ? _cum[fireEndIdx] : tot;
        }

        private void LaunchBall()   // coin-up: a fresh coin drops in at the top-left slot and settles at flipper 1
        {
            if (_coin == null || _rail == null || _comp == null) return;
            _ballLive = true;
            var f1 = FlipperPos[1];
            List<(double, double)> pts;
            if (_drawnPaths.TryGetValue("S0", out var drawn) && drawn.Count >= 2)
                pts = new List<(double, double)>(drawn);          // use Dan's hand-drawn coin-in path if present
            else
            {
                // fall back: drop straight down from the slot onto the first rail, then settle at flipper 1
                pts = new List<(double, double)> { (34, 118) };
                var (railY, lab) = CompBelow(34, 118, 200);
                double landY = double.IsNaN(railY) ? 172 : railY - BallR;
                for (int i = 1; i <= 10; i++) pts.Add((34, 118 + (landY - 118) * i / 10.0));   // vertical drop onto the rail
                pts.Add((f1.x, f1.y));
            }
            SetCoinPath(pts, pts.Count - 1);
            _restX = f1.x; _restY = f1.y; _pendingHole = null; _pendingFired = true;
            _coin.Visibility = Visibility.Visible;
            if (!_phys.IsEnabled) _phys.Start();
        }

        private void FireCoin(string id)   // a flipper fires the coin toward the scored hole
        {
            if (!_ballLive || _coin == null) return;
            FlipNearest(_restX, _restY);   // snap the handle the coin launches from
            if (_rail == null || _comp == null) { HighlightHole(id); return; }
            BuildCoinPath(_restX, _restY, id);
        }

        // On a loss, drain the coin into the gobble path (G1-G7) whose start is nearest where the coin rests.
        private void FireGobblePath()
        {
            if (!_ballLive || _coin == null || _rail == null) return;
            FlipNearest(_restX, _restY);   // the drain shot snaps a handle too
            string? best = null; double bd = double.MaxValue;
            for (int i = 1; i <= 7; i++)
                if (_drawnPaths.TryGetValue($"G{i}", out var p) && p.Count > 0)
                {
                    double d = (p[0].Item1 - _restX) * (p[0].Item1 - _restX) + (p[0].Item2 - _restY) * (p[0].Item2 - _restY);
                    if (d < bd) { bd = d; best = $"G{i}"; }
                }
            if (best != null) BuildCoinPath(_restX, _restY, best);
        }

        private void StopBall()
        {
            _ballLive = false; _phys.Stop();
            _coinPath = new(); _coinLen = 0;
            if (_coin != null) _coin.Visibility = Visibility.Collapsed;
        }

        private void PhysTick()
        {
            if (!_ballLive || _coin == null || _coinPath.Count < 2) return;
            _coinDist = Math.Min(_coinLen, _coinDist + _coinStep);
            // Fire the scoring sound just before the drop so audio latency lands it on the flash — but keep the lead
            // in the last stretch of the path so it hits at the hole (the switch), never back at the lever on short shots.
            double soundLead = Math.Min(6 * _coinStep, _coinLen * 0.22);
            if (!_soundFired && _pendingHole != null && _coinDist >= _dropDist - soundLead)
            {
                var swS = System.Array.Find(SwitchMap.All.ToArray(), z => z.Id == _pendingHole);
                if (swS != null && swS.Kind == SwitchKind.Score && AppState.SoundFxOn)
                    AppState.Audio.PlaySound(AppState.SoundPackage, $"{swS.Points} pts");
                _soundFired = true;
            }
            // the scored hole lights AND the deferred score/switch/reactions reveal the instant the coin drops through
            if (!_pendingFired && _pendingHole != null && _coinDist >= _dropDist)
            {
                HighlightHole(_pendingHole);
                // this drop is the next switch activating — it ends any distract running from the previous hole
                if (_demoDistractActive) { _demoDistractActive = false; StopDistractShow(); }
                // reveal the deferred score, score-scale lights and score reaction NOW — together with the flash
                if (_pendingScore >= 0)
                {
                    ScoreText.Text = _pendingScore.ToString();
                    UpdateScoreLights(_pendingScore);
                    UpdateChase(_pendingScore);
                    ReactToScore(_pendingScore);
                    PulseScore();
                    _pendingScore = -1;
                }
                if (_pendingSwitch != null)   // LAST SWITCH updates now, as the coin hits the switch
                {
                    SetMixedText(LastSwitchText, string.IsNullOrEmpty(_pendingSwitch) ? "None" : _pendingSwitch);
                    _pendingSwitch = null;
                }
                if (_pendingEnd != null)      // WIN/LOSS declared once the final coin has dropped through
                {
                    FinalizeEnd(_pendingEnd, _pendingEndScore);
                    _pendingEnd = null;
                }
                // Demo of the machine's distract mechanic: scoring a high-value hole kicks off the distract lights and music.
                // (Skipped while any switch is stuck — a fault shuts distract off in every mode.)
                if (_demoCts != null && !_demoCts.IsCancellationRequested && !_demoDistractActive && !Faults.Any)
                {
                    var dsw = _pendingHole != null ? System.Array.Find(SwitchMap.All.ToArray(), z => z.Id == _pendingHole) : null;
                    if (dsw != null && dsw.Distract)
                    {
                        _demoDistractActive = true;
                        FlashMode("DISTRACT · " + DistractName(), "PurpleBrush");                        // purple banner + distract LED burst up top
                        if (AppState.SoundFxOn) System.Threading.Tasks.Task.Run(() => AppState.Audio.PlayDistractMusic());
                    }
                }
                _pendingFired = true;
            }
            // interpolate position along the polyline
            int i = 1; while (i < _cum.Length - 1 && _cum[i] < _coinDist) i++;
            var a = _coinPath[i - 1]; var b = _coinPath[i];
            double seg = _cum[i] - _cum[i - 1];
            double t = seg > 1e-6 ? (_coinDist - _cum[i - 1]) / seg : 0;
            double px = a.Item1 + (b.Item1 - a.Item1) * t, py = a.Item2 + (b.Item2 - a.Item2) * t;
            Canvas.SetLeft(_coin, px - 11); Canvas.SetTop(_coin, py - 11);
            // the coin has come to rest at the next flipper — advance the LEVEL now (never before it rests)
            if (_restLevelPending > 0 && _coinDist >= _coinLen) { LevelText.Text = _restLevelPending.ToString(); _restLevelPending = -1; }
            // the game ended but the final winning coin finishes first, then go idle
            if (_endIdlePending && _coinDist >= _coinLen) { _endIdlePending = false; SetPlayState(false); }
        }
        private bool _endIdlePending;
        private bool _demoDistractActive;   // demo: distract lights/music are currently running (stopped by the next hit)
        private volatile bool _demoPaused;  // demo is paused because the page isn't showing
        private TaskCompletionSource<bool>? _resume;

        // The demo loop awaits this before each step; it blocks while the page is away and releases on return.
        private Task DemoAwaitResume(CancellationToken tok)
        {
            var r = _resume;
            if (!_demoPaused || r == null) return Task.CompletedTask;
            tok.Register(() => r.TrySetResult(true));   // cancelling a paused demo lets the loop exit
            return r.Task;
        }

        private void ResumeDemo()
        {
            _demoPaused = false;
            if (_ballLive && !_phys.IsEnabled) _phys.Start();   // let a frozen coin finish its flight
            _resume?.TrySetResult(true);
            _resume = null;
        }
        private static readonly string[] _distractNames = { "MARQUEE", "BREATHE", "STROBE", "SPARKLE", "LIGHTNING", "CONFETTI" };
        private string DistractName() => _distractNames[_rnd.Next(_distractNames.Length)];
        private int _restLevelPending = -1;
    }
}
