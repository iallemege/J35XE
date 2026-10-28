using System.Collections.Generic;
using UnityEngine;

namespace J35XE
{
    /// <summary>Square Color32 canvas for the round MFD. Coordinates are y-down; Upload flips into texture rows.</summary>
    internal sealed class Pix
    {
        public readonly int N;
        public readonly Color32[] Px;
        public readonly float C;
        /// <summary>When above zero, pixels farther than sqrt(ClipR2) from the centre are dropped.</summary>
        public float ClipR2;

        public Pix(int n)
        {
            N = n;
            Px = new Color32[n * n];
            C = (n - 1) * 0.5f;
        }

        public void Fill(Color32 c)
        {
            for (int i = 0; i < Px.Length; i++)
                Px[i] = c;
        }

        public void Set(int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= N || y >= N)
                return;
            if (ClipR2 > 0f)
            {
                float dx = x - C, dy = y - C;
                if (dx * dx + dy * dy > ClipR2)
                    return;
            }
            Px[(N - 1 - y) * N + x] = c;
        }

        public void Line(float x0, float y0, float x1, float y1, Color32 c)
        {
            int ax = Mathf.RoundToInt(x0), ay = Mathf.RoundToInt(y0);
            int bx = Mathf.RoundToInt(x1), by = Mathf.RoundToInt(y1);
            int dx = Mathf.Abs(bx - ax), dy = Mathf.Abs(by - ay);
            if (dx > 4 * N || dy > 4 * N)
                return;
            int sx = ax < bx ? 1 : -1, sy = ay < by ? 1 : -1, err = dx - dy;
            while (true)
            {
                Set(ax, ay, c);
                if (ax == bx && ay == by)
                    break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; ax += sx; }
                if (e2 < dx) { err += dx; ay += sy; }
            }
        }

        public void Thick(float x0, float y0, float x1, float y1, Color32 c)
        {
            Line(x0, y0, x1, y1, c);
            Line(x0 + 1f, y0, x1 + 1f, y1, c);
            Line(x0, y0 + 1f, x1, y1 + 1f, c);
        }

        public void Box(int x, int y, int w, int h, Color32 c, bool fill)
        {
            if (fill)
            {
                for (int j = 0; j < h; j++)
                    for (int i = 0; i < w; i++)
                        Set(x + i, y + j, c);
                return;
            }
            Line(x, y, x + w - 1, y, c);
            Line(x, y + h - 1, x + w - 1, y + h - 1, c);
            Line(x, y, x, y + h - 1, c);
            Line(x + w - 1, y, x + w - 1, y + h - 1, c);
        }

        public void Circle(float cx, float cy, float r, Color32 c)
        {
            const int seg = 40;
            float px = cx + r, py = cy;
            for (int i = 1; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                float nx = cx + Mathf.Cos(a) * r, ny = cy + Mathf.Sin(a) * r;
                Line(px, py, nx, ny, c);
                px = nx;
                py = ny;
            }
        }

        public static int Width(string s) => string.IsNullOrEmpty(s) ? 0 : s.Length * 6 - 1;

        public void Text(int x, int y, string s, Color32 c)
        {
            if (string.IsNullOrEmpty(s))
                return;
            for (int i = 0; i < s.Length; i++)
            {
                bool[] g = Font5x7.Get(s[i]);
                if (g != null)
                {
                    for (int row = 0; row < 7; row++)
                        for (int col = 0; col < 5; col++)
                            if (g[row * 5 + col])
                                Set(x + i * 6 + col, y + row, c);
                }
            }
        }

        public void TextC(float cx, int y, string s, Color32 c) => Text(Mathf.RoundToInt(cx - Width(s) * 0.5f), y, s, c);

        /// <summary>Text on a dark plate so it reads over the attitude ball or the OTIS picture.</summary>
        public void Label(float cx, int y, string s, Color32 c, Color32 plate)
        {
            int w = Width(s);
            int x = Mathf.RoundToInt(cx - w * 0.5f);
            Box(x - 2, y - 2, w + 4, 11, plate, true);
            Text(x, y, s, c);
        }

        public void Upload(Texture2D tex)
        {
            tex.SetPixels32(Px);
            tex.Apply(false, false);
        }
    }

    internal static class Font5x7
    {
        static readonly Dictionary<char, bool[]> Glyphs = new Dictionary<char, bool[]>();

        internal static bool[] Get(char ch)
        {
            if (Glyphs.Count == 0)
                Build();
            if (ch >= 'a' && ch <= 'z')
                ch = (char)(ch - 32);
            bool[] g;
            return Glyphs.TryGetValue(ch, out g) ? g : null;
        }

        static void Add(char ch, params string[] rows)
        {
            bool[] g = new bool[35];
            for (int r = 0; r < 7; r++)
                for (int c = 0; c < 5; c++)
                    g[r * 5 + c] = rows[r][c] == '#';
            Glyphs[ch] = g;
        }

        static void Build()
        {
            Add('0', ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###.");
            Add('1', "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###.");
            Add('2', ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####");
            Add('3', "#####", "...#.", "..#..", "...#.", "....#", "#...#", ".###.");
            Add('4', "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#.");
            Add('5', "#####", "#....", "####.", "....#", "....#", "#...#", ".###.");
            Add('6', "..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###.");
            Add('7', "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#...");
            Add('8', ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###.");
            Add('9', ".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##..");
            Add('A', ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#");
            Add('B', "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####.");
            Add('C', ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###.");
            Add('D', "###..", "#..#.", "#...#", "#...#", "#...#", "#..#.", "###..");
            Add('E', "#####", "#....", "#....", "####.", "#....", "#....", "#####");
            Add('F', "#####", "#....", "#....", "####.", "#....", "#....", "#....");
            Add('G', ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####");
            Add('H', "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#");
            Add('I', ".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###.");
            Add('J', "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##..");
            Add('K', "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#");
            Add('L', "#....", "#....", "#....", "#....", "#....", "#....", "#####");
            Add('M', "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#");
            Add('N', "#...#", "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#");
            Add('O', ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###.");
            Add('P', "####.", "#...#", "#...#", "####.", "#....", "#....", "#....");
            Add('Q', ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#");
            Add('R', "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#");
            Add('S', ".####", "#....", "#....", ".###.", "....#", "....#", "####.");
            Add('T', "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#..");
            Add('U', "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###.");
            Add('V', "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#..");
            Add('W', "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "#.#.#", ".#.#.");
            Add('X', "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#");
            Add('Y', "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#..");
            Add('Z', "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####");
            Add(' ', ".....", ".....", ".....", ".....", ".....", ".....", ".....");
            Add('.', ".....", ".....", ".....", ".....", ".....", ".##..", ".##..");
            Add(',', ".....", ".....", ".....", ".....", ".##..", "..#..", ".#...");
            Add('-', ".....", ".....", ".....", "#####", ".....", ".....", ".....");
            Add('+', ".....", "..#..", "..#..", "#####", "..#..", "..#..", ".....");
            Add('/', "....#", "...#.", "...#.", "..#..", ".#...", ".#...", "#....");
            Add(':', ".....", ".##..", ".##..", ".....", ".##..", ".##..", ".....");
            Add('%', "##..#", "##..#", "...#.", "..#..", ".#...", "#..##", "#..##");
            Add('>', ".#...", "..#..", "...#.", "....#", "...#.", "..#..", ".#...");
            Add('<', "...#.", "..#..", ".#...", "#....", ".#...", "..#..", "...#.");
            Add('[', ".###.", ".#...", ".#...", ".#...", ".#...", ".#...", ".###.");
            Add(']', ".###.", "...#.", "...#.", "...#.", "...#.", "...#.", ".###.");
            Add('(', "...#.", "..#..", ".#...", ".#...", ".#...", "..#..", "...#.");
            Add(')', ".#...", "..#..", "...#.", "...#.", "...#.", "..#..", ".#...");
            Add('*', ".....", "#.#.#", ".###.", "#####", ".###.", "#.#.#", ".....");
            Add('=', ".....", ".....", "#####", ".....", "#####", ".....", ".....");
            Add('#', ".#.#.", "#####", ".#.#.", ".#.#.", ".#.#.", "#####", ".#.#.");
            Add('_', ".....", ".....", ".....", ".....", ".....", ".....", "#####");
        }
    }
}
