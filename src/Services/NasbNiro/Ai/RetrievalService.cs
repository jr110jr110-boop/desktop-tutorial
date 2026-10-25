using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.AiDtos;

namespace Services.NasbNiro.Ai
{
    public static class AiSourceTypes
    {
        public const string ControlledDocument = "ControlledDocument";
        public const string MeetingMinutes = "MeetingMinutes";

        /// <summary>Letters are never indexed: the secretariat reads Normal and Confidential letters but a Secret letter only as register data, and the AI seats must stay Normal-only, so no letter text goes to the index.</summary>
        public static readonly string[] All = { ControlledDocument, MeetingMinutes };
    }

    public interface IRetrievalService
    {
        /// <summary>Replaces the chunks of a source. False when the embeddings endpoint did not answer (the old chunks stay).</summary>
        Task<bool> IndexAsync(string sourceType, int sourceId, string revision, string title, string text, CancellationToken ct);

        /// <summary>The best chunks for a query. Source types are filtered by the caller's roles (and by the visibility of a meeting); null = every type the caller may read.</summary>
        Task<List<AiSearchHitDto>> SearchAsync(string query, int topK, IReadOnlyCollection<string> allowedSourceTypes, CancellationToken ct);

        Task<AiIndexRebuildResultDto> RebuildAllAsync(CancellationToken ct);
    }

    public class RetrievalService : IRetrievalService
    {
        public const int ChunkSize = 800;
        public const int ChunkOverlap = 100;
        private const int PageRows = 500;

        /// <summary>★ Hits at or below this cosine are noise and are dropped.</summary>
        public const double MinScore = 0.05;

        private readonly IUnitOfWork _uow;
        private readonly IEmbeddingClient _embeddings;
        private readonly IInventoryUserContext _user;
        private readonly IMeetingService _meetings;

        public RetrievalService(IUnitOfWork uow, IEmbeddingClient embeddings, IInventoryUserContext user, IMeetingService meetings)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _embeddings = embeddings ?? throw new ArgumentNullException(nameof(embeddings));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _meetings = meetings ?? throw new ArgumentNullException(nameof(meetings));
        }

        // ------------------------------------------------------------------------------------------ pure helpers

        /// <summary>About <paramref name="size"/> characters per chunk, <paramref name="overlap"/> shared with the previous one, cut at a space where possible.</summary>
        public static List<string> Chunk(string text, int size = ChunkSize, int overlap = ChunkOverlap)
        {
            var chunks = new List<string>();
            string s = (text ?? string.Empty).Replace("\r\n", "\n").Trim();
            if (s.Length == 0) return chunks;
            int start = 0;
            while (start < s.Length)
            {
                int end = Math.Min(s.Length, start + size);
                if (end < s.Length)
                {
                    int cut = s.LastIndexOfAny(new[] { ' ', '\n', '‌' }, end - 1, Math.Min(end - start, 120));
                    if (cut > start + size / 2) end = cut + 1;
                }
                string piece = s.Substring(start, end - start).Trim();
                if (piece.Length > 0) chunks.Add(piece);
                if (end >= s.Length) break;
                start = Math.Max(end - overlap, start + 1);
            }
            return chunks;
        }

        public static byte[] ToBytes(float[] vector)
        {
            var bytes = new byte[vector.Length * 4];
            for (int i = 0; i < vector.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4, 4), vector[i]);
            return bytes;
        }

        public static float[] FromBytes(byte[] bytes)
        {
            var vector = new float[bytes.Length / 4];
            for (int i = 0; i < vector.Length; i++) vector[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4, 4));
            return vector;
        }

        public static double Cosine(float[] a, float[] b)
        {
            if (a.Length != b.Length || a.Length == 0) return 0;
            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * (double)b[i];
                na += a[i] * (double)a[i];
                nb += b[i] * (double)b[i];
            }
            return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
        }

        private static string[] Words(string text) =>
            AiNumbers.NormalizeDigits(text ?? string.Empty).ToLowerInvariant()
                .Split(new[] { ' ', '\n', '\r', '\t', '.', ',', '،', ':', ';', '؛', '(', ')', '-', '/', '؟', '?', '!', '‌' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 1).Distinct().ToArray();

        /// <summary>Used when no embeddings endpoint answers: the share of the query words found in the chunk.</summary>
        public static double Lexical(string query, string chunk)
        {
            string[] q = Words(query);
            if (q.Length == 0) return 0;
            string c = AiNumbers.NormalizeDigits(chunk ?? string.Empty).ToLowerInvariant();
            return q.Count(w => c.Contains(w)) / (double)q.Length;
        }

        public static string Link(string sourceType, int sourceId) => sourceType switch
        {
            AiSourceTypes.ControlledDocument => "/quality/documents/" + sourceId.ToString(CultureInfo.InvariantCulture),
            AiSourceTypes.MeetingMinutes => "/office/meetings/" + sourceId.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };

        // ------------------------------------------------------------------------------------------ index

        public async Task<bool> IndexAsync(string sourceType, int sourceId, string revision, string title, string text, CancellationToken ct)
        {
            if (!AiSourceTypes.All.Contains(sourceType)) throw new ArgumentException("unknown source type", nameof(sourceType));
            List<string> chunks = Chunk(text);
            if (chunks.Count == 0) return true;
            EmbeddingResult embedded = await _embeddings.EmbedAsync(chunks.Select(c => (title + "\n" + c)).ToList(), ct);
            if (embedded == null || embedded.Vectors.Count != chunks.Count) return false;

            List<AiDocumentChunk> old = await _uow.Set<AiDocumentChunk>().Where(c => c.SourceType == sourceType && c.SourceId == sourceId).ToListAsync(ct);
            _uow.RemoveRange(old);
            DateTimeOffset now = DateTimeOffset.Now;
            for (int i = 0; i < chunks.Count; i++)
            {
                _uow.Set<AiDocumentChunk>().Add(new AiDocumentChunk
                {
                    SourceType = sourceType, SourceId = sourceId, Revision = AiText.Cut(revision, 50), ChunkIndex = i, Title = AiText.Cut(title, 300),
                    Text = AiText.Cut(chunks[i], 4000), Embedding = ToBytes(embedded.Vectors[i]), Dim = embedded.Vectors[i].Length, Model = AiText.Cut(embedded.Model, 200), CreatedAt = now,
                });
            }
            await _uow.SaveChangesAsync(ct);
            return true;
        }

        // ------------------------------------------------------------------------------------------ search

        private List<string> ReadableTypes(IReadOnlyCollection<string> requested)
        {
            var types = new List<string>();
            if (_user.IsInRole(global::Services.NasbNiro.Quality.QualityRoles.Readers)) types.Add(AiSourceTypes.ControlledDocument);
            types.Add(AiSourceTypes.MeetingMinutes); // visibility of each meeting is checked by the meeting service
            if (requested != null && requested.Count > 0) types = types.Where(requested.Contains).ToList();
            return types;
        }

        public async Task<List<AiSearchHitDto>> SearchAsync(string query, int topK, IReadOnlyCollection<string> allowedSourceTypes, CancellationToken ct)
        {
            var hits = new List<AiSearchHitDto>();
            if (string.IsNullOrWhiteSpace(query)) return hits;
            topK = Math.Max(1, Math.Min(topK, 20));
            List<string> types = ReadableTypes(allowedSourceTypes);
            if (types.Count == 0) return hits;

            EmbeddingResult embedded = await _embeddings.EmbedAsync(new[] { query }, ct);
            float[] q = embedded?.Vectors.Count == 1 ? embedded.Vectors[0] : null;
            string queryModel = AiText.Cut(embedded?.Model, 200);

            // best chunk of every source
            var best = new Dictionary<(string Type, int Id), AiSearchHitDto>();
            int lastId = 0;
            while (true)
            {
                var page = await _uow.Set<AiDocumentChunk>().AsNoTracking().Where(c => types.Contains(c.SourceType) && c.Id > lastId)
                    .OrderBy(c => c.Id).Take(PageRows).Select(c => new { c.Id, c.SourceType, c.SourceId, c.Revision, c.Title, c.Text, c.Embedding, c.Dim, c.Model }).ToListAsync(ct);
                if (page.Count == 0) break;
                foreach (var c in page)
                {
                    // vectors are comparable only when the same model made them and they have the same size
                    double score = q != null
                        ? (c.Embedding != null && c.Dim == q.Length && string.Equals(c.Model, queryModel, StringComparison.Ordinal) ? Cosine(q, FromBytes(c.Embedding)) : 0)
                        : Lexical(query, c.Title + " " + c.Text);
                    if (score <= (q != null ? MinScore : 0)) continue;
                    var key = (c.SourceType, c.SourceId);
                    if (best.TryGetValue(key, out AiSearchHitDto current) && current.Score >= score) continue;
                    best[key] = new AiSearchHitDto
                    {
                        SourceType = c.SourceType, SourceId = c.SourceId, Revision = c.Revision, Title = c.Title, Snippet = AiText.Cut(c.Text, 300),
                        Score = Math.Round(score, 4), Link = Link(c.SourceType, c.SourceId),
                    };
                }
                lastId = page[page.Count - 1].Id;
            }

            foreach (AiSearchHitDto hit in best.Values.OrderByDescending(h => h.Score).ThenBy(h => h.SourceId).Take(topK * 4))
            {
                if (hit.SourceType == AiSourceTypes.MeetingMinutes)
                {
                    var meeting = await _meetings.GetMeetingAsync(hit.SourceId, ct);
                    if (!meeting.Success) continue;
                }
                hits.Add(hit);
                if (hits.Count >= topK) break;
            }
            return hits;
        }

        // ------------------------------------------------------------------------------------------ rebuild

        public async Task<AiIndexRebuildResultDto> RebuildAllAsync(CancellationToken ct)
        {
            var result = new AiIndexRebuildResultDto();

            List<ControlledDocument> documents = await _uow.Set<ControlledDocument>().AsNoTracking().Include(d => d.Revisions)
                .Where(d => !d.Deleted && (d.Status == ControlledDocumentStatus.Approved || d.Status == ControlledDocumentStatus.Effective)).OrderBy(d => d.Id).ToListAsync(ct);
            foreach (ControlledDocument d in documents)
            {
                DocumentRevisionRecord revision = (d.Revisions ?? new List<DocumentRevisionRecord>()).Where(r => !r.Deleted && r.Revision == d.CurrentRevision).OrderByDescending(r => r.Id).FirstOrDefault();
                var sb = new StringBuilder();
                sb.Append("کد: ").Append(d.Code).Append("\nعنوان: ").Append(d.Title);
                if (!string.IsNullOrWhiteSpace(d.Department)) sb.Append("\nبخش: ").Append(d.Department);
                if (!string.IsNullOrWhiteSpace(d.ResponsibleUnit)) sb.Append("\nواحد مسئول: ").Append(d.ResponsibleUnit);
                if (!string.IsNullOrWhiteSpace(d.CurrentRevision)) sb.Append("\nویرایش: ").Append(d.CurrentRevision);
                if (d.EffectiveDate.HasValue) sb.Append("\nتاریخ اجرا: ").Append(InventoryText.PersianDate(d.EffectiveDate.Value));
                if (!string.IsNullOrWhiteSpace(revision?.ChangeSummary)) sb.Append("\nشرح تغییرات: ").Append(revision.ChangeSummary);
                if (await IndexAsync(AiSourceTypes.ControlledDocument, d.Id, d.CurrentRevision ?? "0", (d.Code + " " + d.Title).Trim(), sb.ToString(), ct)) result.ControlledDocuments++;
                else result.Failed++;
            }

            List<OfMeeting> meetings = await _uow.Set<OfMeeting>().AsNoTracking().Include(m => m.Agenda).Include(m => m.Decisions)
                .Where(m => !m.Deleted && m.Status == OfMeetingStatus.MinutesApproved).OrderBy(m => m.Id).ToListAsync(ct);
            foreach (OfMeeting m in meetings)
            {
                var sb = new StringBuilder();
                sb.Append("صورت‌جلسه ").Append(m.Number).Append(": ").Append(m.Title).Append("\nتاریخ: ").Append(InventoryText.PersianDate(m.StartAt));
                foreach (OfMeetingAgenda a in (m.Agenda ?? new List<OfMeetingAgenda>()).OrderBy(a => a.Seq))
                {
                    sb.Append("\nدستور جلسه: ").Append(a.Title);
                    if (!string.IsNullOrWhiteSpace(a.Minutes)) sb.Append("\n").Append(a.Minutes);
                }
                foreach (OfDecision dec in (m.Decisions ?? new List<OfDecision>()).OrderBy(x => x.Seq))
                    sb.Append("\nمصوبه: ").Append(dec.Text);
                if (await IndexAsync(AiSourceTypes.MeetingMinutes, m.Id, "1", (m.Number + " " + m.Title).Trim(), sb.ToString(), ct)) result.MeetingMinutes++;
                else result.Failed++;
            }

            // sources that are no longer indexable (not approved, obsolete, deleted, minutes not approved) leave the index
            List<int> documentIds = documents.Select(d => d.Id).ToList();
            List<int> meetingIds = meetings.Select(m => m.Id).ToList();
            List<AiDocumentChunk> stale = await _uow.Set<AiDocumentChunk>().Where(c =>
                (c.SourceType == AiSourceTypes.ControlledDocument && !documentIds.Contains(c.SourceId)) ||
                (c.SourceType == AiSourceTypes.MeetingMinutes && !meetingIds.Contains(c.SourceId)) ||
                (c.SourceType != AiSourceTypes.ControlledDocument && c.SourceType != AiSourceTypes.MeetingMinutes)).ToListAsync(ct);
            if (stale.Count > 0)
            {
                _uow.RemoveRange(stale);
                await _uow.SaveChangesAsync(ct);
            }
            result.Chunks = await _uow.Set<AiDocumentChunk>().CountAsync(ct);
            result.Message = result.Failed > 0 ? "بخشی از منابع نمایه نشد؛ سرویس embedding را بررسی کنید." : "نمایه‌سازی کامل شد.";
            return result;
        }
    }
}
