using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Services.NasbNiro.GoLive
{
    public sealed class PdfStampException : Exception
    {
        public PdfStampException(string message) : base(message) { }
    }

    /// <summary>An image to draw on every page: raw RGBA bytes (row by row, top to bottom) rendered by the browser (so the Persian text uses the program's own font, B Zar).</summary>
    public sealed class PdfStampImage
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public byte[] Rgba { get; set; }
    }

    /// <summary>
    /// Package G: the controlled-copy stamp on a PDF. No PDF library is available offline, so the stamper makes an incremental update (the original bytes are kept unchanged; the stamp is appended): one header band at the top and one footer band at
    /// the bottom of every page, as image XObjects with a soft mask. It reads classic cross-reference tables (the ordinary output of drawing and office programs). A PDF with an encrypted body, a cross-reference stream or compressed object streams
    /// is refused with a Persian message and the caller falls back to the stamped HTML print of the page.
    /// </summary>
    public static class PdfStamper
    {
        private static readonly Encoding Latin1 = Encoding.Latin1;
        private const string Unsupported = "ساختار این PDF (رمزگذاری، جدول ارجاع فشرده یا شیء فشرده) برای مهر مستقیم پشتیبانی نمی‌شود؛ چاپ با مهر صفحه انجام می‌شود.";

        private sealed class Xref { public long Offset; public int Gen; public bool InUse; }

        public static bool LooksLikePdf(byte[] pdf) => pdf != null && pdf.Length > 20 && pdf[0] == '%' && pdf[1] == 'P' && pdf[2] == 'D' && pdf[3] == 'F';

        public static byte[] Stamp(byte[] pdf, PdfStampImage header, PdfStampImage footer)
        {
            if (!LooksLikePdf(pdf)) throw new PdfStampException("فایل PDF نیست.");
            Check(header); Check(footer);
            string s = Latin1.GetString(pdf);
            int sx = s.LastIndexOf("startxref", StringComparison.Ordinal);
            if (sx < 0) throw new PdfStampException(Unsupported);
            Match m = Regex.Match(s.Substring(sx + 9), @"^\s*(\d+)");
            if (!m.Success) throw new PdfStampException(Unsupported);
            long firstXref = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);

            var table = new Dictionary<int, Xref>();
            string trailer = null;
            long at = firstXref;
            var seen = new HashSet<long>();
            while (true)
            {
                if (!seen.Add(at) || at < 0 || at >= s.Length) throw new PdfStampException(Unsupported);
                int p = SkipWs(s, (int)at);
                if (string.CompareOrdinal(s, p, "xref", 0, 4) != 0) throw new PdfStampException(Unsupported);
                p += 4;
                while (true)
                {
                    p = SkipWs(s, p);
                    if (string.CompareOrdinal(s, p, "trailer", 0, 7) == 0) { p += 7; break; }
                    Match sec = Regex.Match(s.Substring(p, Math.Min(40, s.Length - p)), @"^(\d+)\s+(\d+)\s*");
                    if (!sec.Success) throw new PdfStampException(Unsupported);
                    int first = int.Parse(sec.Groups[1].Value, CultureInfo.InvariantCulture), count = int.Parse(sec.Groups[2].Value, CultureInfo.InvariantCulture);
                    p += sec.Length;
                    for (int i = 0; i < count; i++)
                    {
                        Match e = Regex.Match(s.Substring(p, Math.Min(30, s.Length - p)), @"^\s*(\d{10})\s+(\d{5})\s+([nf])\s*");
                        if (!e.Success) throw new PdfStampException(Unsupported);
                        p += e.Length;
                        int num = first + i;
                        if (!table.ContainsKey(num)) table[num] = new Xref { Offset = long.Parse(e.Groups[1].Value, CultureInfo.InvariantCulture), Gen = int.Parse(e.Groups[2].Value, CultureInfo.InvariantCulture), InUse = e.Groups[3].Value == "n" };
                    }
                }
                string td = Dict(s, SkipWs(s, p));
                if (td == null) throw new PdfStampException(Unsupported);
                if (trailer == null) trailer = td;
                if (td.Contains("/Encrypt") || td.Contains("/XRefStm")) throw new PdfStampException(Unsupported);
                Match prev = Regex.Match(td, @"/Prev\s+(\d+)");
                if (!prev.Success) break;
                at = long.Parse(prev.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            Match root = Regex.Match(trailer, @"/Root\s+(\d+)\s+(\d+)\s+R");
            Match size = Regex.Match(trailer, @"/Size\s+(\d+)");
            if (!root.Success || !size.Success) throw new PdfStampException(Unsupported);
            int nextNum = int.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture);

            string Body(int num)
            {
                if (!table.TryGetValue(num, out Xref x) || !x.InUse) throw new PdfStampException(Unsupported);
                int p = SkipWs(s, (int)x.Offset);
                Match h = Regex.Match(s.Substring(p, Math.Min(30, s.Length - p)), @"^(\d+)\s+(\d+)\s+obj\s*");
                if (!h.Success || int.Parse(h.Groups[1].Value, CultureInfo.InvariantCulture) != num) throw new PdfStampException(Unsupported);
                int end = s.IndexOf("endobj", p, StringComparison.Ordinal);
                if (end < 0) throw new PdfStampException(Unsupported);
                return s.Substring(p + h.Length, end - p - h.Length);
            }

            var updated = new Dictionary<int, string>();   // object number -> new dictionary body
            string Current(int num) => updated.TryGetValue(num, out string v) ? v : Body(num);
            string rootBody = Body(int.Parse(root.Groups[1].Value, CultureInfo.InvariantCulture));
            Match pagesRef = Regex.Match(rootBody, @"/Pages\s+(\d+)\s+(\d+)\s+R");
            if (!pagesRef.Success) throw new PdfStampException(Unsupported);

            var pages = new List<(int Num, string Resources, string Box)>();
            void Walk(int num, string inheritedRes, string inheritedBox, int depth)
            {
                if (depth > 40 || pages.Count > 2000) throw new PdfStampException(Unsupported);
                string body = Body(num);
                if (body.Contains("stream", StringComparison.Ordinal)) throw new PdfStampException(Unsupported);
                string d = Dict(body, SkipWs(body, 0));
                if (d == null) throw new PdfStampException(Unsupported);
                string res = FindEntry(d, "/Resources") ?? inheritedRes, box = FindEntry(d, "/MediaBox") ?? inheritedBox;
                Match kids = Regex.Match(d, @"/Kids\s*\[([^\]]*)\]");
                if (kids.Success && Regex.IsMatch(d, @"/Type\s*/Pages"))
                {
                    foreach (Match k in Regex.Matches(kids.Groups[1].Value, @"(\d+)\s+\d+\s+R")) Walk(int.Parse(k.Groups[1].Value, CultureInfo.InvariantCulture), res, box, depth + 1);
                }
                else pages.Add((num, res, box));
            }
            Walk(int.Parse(pagesRef.Groups[1].Value, CultureInfo.InvariantCulture), null, null, 0);
            if (pages.Count == 0) throw new PdfStampException(Unsupported);

            // the images (shared by every page)
            var extra = new List<(int Num, byte[] Data)>();
            int AddObject(string dict, byte[] stream)
            {
                int num = nextNum++;
                using var ms = new MemoryStream();
                void W(string t) { byte[] b = Latin1.GetBytes(t); ms.Write(b, 0, b.Length); }
                W($"{num} 0 obj\n");
                if (stream == null) W(dict + "\nendobj\n");
                else { W(dict.TrimEnd('>', ' ').TrimEnd('>') + $" /Length {stream.Length.ToString(CultureInfo.InvariantCulture)} >>\nstream\n"); ms.Write(stream, 0, stream.Length); W("\nendstream\nendobj\n"); }
                extra.Add((num, ms.ToArray()));
                return num;
            }
            int Image(PdfStampImage img)
            {
                int n = img.Width * img.Height;
                var rgb = new byte[n * 3]; var alpha = new byte[n];
                for (int i = 0; i < n; i++) { rgb[i * 3] = img.Rgba[i * 4]; rgb[i * 3 + 1] = img.Rgba[i * 4 + 1]; rgb[i * 3 + 2] = img.Rgba[i * 4 + 2]; alpha[i] = img.Rgba[i * 4 + 3]; }
                int mask = AddObject($"<< /Type /XObject /Subtype /Image /Width {img.Width} /Height {img.Height} /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode >>", Deflate(alpha));
                return AddObject($"<< /Type /XObject /Subtype /Image /Width {img.Width} /Height {img.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask {mask} 0 R /Filter /FlateDecode >>", Deflate(rgb));
            }
            int hImg = Image(header), fImg = Image(footer);
            string xobj = $"/GlStampH {hImg} 0 R /GlStampF {fImg} 0 R";

            foreach (var (num, resources, box) in pages)
            {
                string body = Current(num);
                string d = Dict(body, SkipWs(body, 0));
                double x0 = 0, y0 = 0, x1 = 595, y1 = 842;
                if (box != null)
                {
                    double[] v = Regex.Matches(box, @"-?\d+(\.\d+)?").Select(x => double.Parse(x.Value, CultureInfo.InvariantCulture)).ToArray();
                    if (v.Length >= 4) { x0 = Math.Min(v[0], v[2]); y0 = Math.Min(v[1], v[3]); x1 = Math.Max(v[0], v[2]); y1 = Math.Max(v[1], v[3]); }
                }
                double w = x1 - x0, h = y1 - y0;
                if (w < 50 || h < 50) throw new PdfStampException(Unsupported);
                double hh = Math.Min(h * 0.12, w * header.Height / header.Width), fh = Math.Min(h * 0.08, w * footer.Height / footer.Width);
                string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
                string content = $"q {F(w)} 0 0 {F(hh)} {F(x0)} {F(y1 - hh)} cm /GlStampH Do Q\nq {F(w)} 0 0 {F(fh)} {F(x0)} {F(y0)} cm /GlStampF Do Q\n";
                int cNum = AddObject("<< >>", Latin1.GetBytes(content));

                // contents
                Match cm = Regex.Match(d, @"/Contents\s*(\[[^\]]*\]|\d+\s+\d+\s+R)");
                string nd;
                if (cm.Success)
                {
                    string old = cm.Groups[1].Value.Trim();
                    string arr = old.StartsWith("[", StringComparison.Ordinal) ? old.Substring(1, old.Length - 2) : old;
                    nd = d.Substring(0, cm.Index) + $"/Contents [ {arr} {cNum} 0 R ]" + d.Substring(cm.Index + cm.Length);
                }
                else nd = d.Substring(0, d.Length - 2) + $" /Contents [ {cNum} 0 R ] >>";

                // resources
                Match rr = Regex.Match(nd, @"/Resources\s*(\d+)\s+(\d+)\s+R");
                if (rr.Success) AddToResourceObject(int.Parse(rr.Groups[1].Value, CultureInfo.InvariantCulture), updated, Current, xobj);
                else
                {
                    int ri = nd.IndexOf("/Resources", StringComparison.Ordinal);
                    if (ri >= 0)
                    {
                        int ds = nd.IndexOf("<<", ri, StringComparison.Ordinal);
                        string rd = ds < 0 ? null : Dict(nd, ds);
                        if (rd == null) throw new PdfStampException(Unsupported);
                        nd = nd.Substring(0, ds) + InsertXObject(rd, xobj) + nd.Substring(ds + rd.Length);
                    }
                    else if (resources != null)
                    {
                        Match inheritedRef = Regex.Match(resources, @"^\s*(\d+)\s+(\d+)\s+R");
                        if (inheritedRef.Success)
                        {
                            AddToResourceObject(int.Parse(inheritedRef.Groups[1].Value, CultureInfo.InvariantCulture), updated, Current, xobj);
                            nd = nd.Substring(0, nd.Length - 2) + $" /Resources {inheritedRef.Groups[1].Value} {inheritedRef.Groups[2].Value} R >>";
                        }
                        else nd = nd.Substring(0, nd.Length - 2) + " /Resources " + InsertXObject(resources.Trim(), xobj) + " >>";
                    }
                    else nd = nd.Substring(0, nd.Length - 2) + $" /Resources << /XObject << {xobj} >> >> >>";
                }
                updated[num] = body.Substring(0, SkipWs(body, 0)) + nd + body.Substring(SkipWs(body, 0) + d.Length);
            }

            // write the update
            using var outp = new MemoryStream();
            outp.Write(pdf, 0, pdf.Length);
            void Raw(string t) { byte[] b = Latin1.GetBytes(t); outp.Write(b, 0, b.Length); }
            Raw("\n");
            var offsets = new SortedDictionary<int, (long Offset, int Gen)>();
            foreach (var kv in updated)
            {
                offsets[kv.Key] = (outp.Position, table[kv.Key].Gen);
                Raw($"{kv.Key} {table[kv.Key].Gen} obj\n{kv.Value.Trim()}\nendobj\n");
            }
            foreach (var (num, data) in extra) { offsets[num] = (outp.Position, 0); outp.Write(data, 0, data.Length); }
            long xrefAt = outp.Position;
            Raw("xref\n");
            foreach (var kv in offsets) Raw($"{kv.Key} 1\n{kv.Value.Offset.ToString("D10", CultureInfo.InvariantCulture)} {kv.Value.Gen.ToString("D5", CultureInfo.InvariantCulture)} n \n");
            string nt = Regex.Replace(trailer, @"/Size\s+\d+", $"/Size {nextNum}");
            nt = Regex.Replace(nt, @"/Prev\s+\d+", "");
            nt = nt.Substring(0, nt.Length - 2) + $" /Prev {firstXref} >>";
            Raw($"trailer\n{nt}\nstartxref\n{xrefAt}\n%%EOF\n");
            return outp.ToArray();
        }

        private static void AddToResourceObject(int num, Dictionary<int, string> updated, Func<int, string> current, string xobj)
        {
            string body = current(num);
            if (body.Contains("/GlStampH", StringComparison.Ordinal)) return;
            int st = SkipWs(body, 0);
            string d = Dict(body, st);
            if (d == null || body.Contains("stream", StringComparison.Ordinal)) throw new PdfStampException(Unsupported);
            updated[num] = body.Substring(0, st) + InsertXObject(d, xobj) + body.Substring(st + d.Length);
        }

        /// <summary>Adds the stamp images to the XObject entry of a resources dictionary (inline entry, or creates it). A referenced XObject dictionary is not supported (refused).</summary>
        private static string InsertXObject(string resources, string xobj)
        {
            if (resources.Contains("/GlStampH", StringComparison.Ordinal)) return resources;
            Match x = Regex.Match(resources, @"/XObject\s*<<");
            if (x.Success) return resources.Substring(0, x.Index + x.Length) + " " + xobj + " " + resources.Substring(x.Index + x.Length);
            if (Regex.IsMatch(resources, @"/XObject\s*\d+\s+\d+\s+R")) throw new PdfStampException(Unsupported);
            return resources.Substring(0, 2) + $" /XObject << {xobj} >> " + resources.Substring(2);
        }

        private static string FindEntry(string dict, string key)
        {
            int i = dict.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return null;
            int p = SkipWs(dict, i + key.Length);
            if (p >= dict.Length) return null;
            if (dict[p] == '[') { int e = dict.IndexOf(']', p); return e < 0 ? null : dict.Substring(p, e - p + 1); }
            if (dict[p] == '<') return Dict(dict, p);
            Match r = Regex.Match(dict.Substring(p), @"^\d+\s+\d+\s+R");
            return r.Success ? r.Value : null;
        }

        /// <summary>The balanced &lt;&lt; ... &gt;&gt; dictionary that starts at <paramref name="start"/>.</summary>
        private static string Dict(string s, int start)
        {
            if (start < 0 || start + 1 >= s.Length || s[start] != '<' || s[start + 1] != '<') return null;
            int depth = 0;
            for (int i = start; i < s.Length - 1; i++)
            {
                if (s[i] == '<' && s[i + 1] == '<') { depth++; i++; }
                else if (s[i] == '>' && s[i + 1] == '>') { depth--; i++; if (depth == 0) return s.Substring(start, i - start + 1); }
                else if (s[i] == '(') { int lvl = 1; i++; while (i < s.Length && lvl > 0) { if (s[i] == '\\') i++; else if (s[i] == '(') lvl++; else if (s[i] == ')') lvl--; i++; } i--; }
            }
            return null;
        }

        private static int SkipWs(string s, int p) { while (p < s.Length && (s[p] == ' ' || s[p] == '\r' || s[p] == '\n' || s[p] == '\t' || s[p] == '\0' || s[p] == '\f')) p++; return p; }

        private static byte[] Deflate(byte[] data)
        {
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(data, 0, data.Length);
            return ms.ToArray();
        }

        private static void Check(PdfStampImage i)
        {
            if (i == null || i.Rgba == null || i.Width <= 0 || i.Height <= 0 || i.Width > 4000 || i.Height > 1500 || i.Rgba.Length != i.Width * i.Height * 4) throw new PdfStampException("تصویر مهر نامعتبر است.");
        }
    }
}
