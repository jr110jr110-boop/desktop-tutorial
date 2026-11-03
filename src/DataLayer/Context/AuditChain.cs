using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataLayer.Context
{
    /// <summary>
    /// R1t D20: the hash chain of the audit trail (<see cref="FnAuditLog"/>). Every row written by the database context carries its place in the chain (<c>Seq</c>), the hash of the row before it (<c>PrevHash</c>) and its own
    /// <c>Hash</c> over the place, the previous hash and every content field (entity, id, action, summary, before and after values, user, moment, address). A row that is edited, a row that is deleted, a row that is
    /// slipped in or a chain that is cut and re-joined shows as the first break of <see cref="Walker"/>.
    /// The hash is an HMAC-SHA256 with the same server key the trail already uses for the masked national code, phone and account hashes (<c>Identity:AuditHmacKey</c>): without the key
    /// nobody who can write to the table can recompute a chain that verifies. When no key is set the rows are hashed with plain SHA-256 ("v0:"), which still catches an accidental or careless change but not a deliberate rewrite of the whole chain;
    /// once a keyed row exists an unkeyed row after it is itself reported as a break (the key cannot be quietly switched off).
    /// Writers are serialised with an application lock on SQL Server (the database context takes it around the save), and the place in the chain is a unique index, so two writers can never extend the same row.
    /// </summary>
    public static class AuditChain
    {
        public const string SettingKey = "Identity:AuditHmacKey";
        public const string Keyed = "v1:";
        public const string Plain = "v0:";
        public const string LockResource = "PMIS.FnAuditLogChain";

        /// <summary>How long a writer waits for the chain lock before the save is refused (milliseconds).</summary>
        public const int LockTimeoutMs = 20000;

        /// <summary>L2 (review R1t): the chain does not use the audit key itself but a key derived from it for this one purpose, so a hash of the masked-values trail can never be replayed as a chain seal.</summary>
        public static byte[] KeyFrom(IConfiguration configuration)
        {
            string key = configuration?[SettingKey];
            return string.IsNullOrWhiteSpace(key) ? null : HMACSHA256.HashData(Encoding.UTF8.GetBytes(key.Trim()), Encoding.UTF8.GetBytes("PMIS-AUDIT-CHAIN"));
        }

        // ------------------------------------------------------------------------------------------ the hash

        /// <summary>The text that is hashed: each field with its length in front (so "ab"+"c" and "a"+"bc" differ) and a mark for null.</summary>
        public static string Canonical(long seq, string prevHash, FnAuditLog row)
        {
            var text = new StringBuilder("PMIS-AUDIT-1");
            void Field(string value)
            {
                text.Append('|');
                if (value == null) text.Append('~');
                else text.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
            }
            Field(seq.ToString(CultureInfo.InvariantCulture));
            Field(prevHash);
            Field(row.EntityType);
            Field(row.EntityId?.ToString(CultureInfo.InvariantCulture));
            Field(row.Action);
            Field(row.Summary);
            Field(row.BeforeJson);
            Field(row.AfterJson);
            Field(row.UserId.ToString(CultureInfo.InvariantCulture));
            Field(row.At.UtcTicks.ToString(CultureInfo.InvariantCulture));
            Field(row.Ip);
            return text.ToString();
        }

        public static string Compute(long seq, string prevHash, FnAuditLog row, byte[] key)
        {
            byte[] data = Encoding.UTF8.GetBytes(Canonical(seq, prevHash, row));
            return key == null
                ? Plain + Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()
                : Keyed + Convert.ToHexString(HMACSHA256.HashData(key, data)).ToLowerInvariant();
        }

        /// <summary>Writes the place, the previous hash and the hash into the row.</summary>
        public static void Seal(FnAuditLog row, long seq, string prevHash, byte[] key)
        {
            row.Seq = seq;
            row.PrevHash = string.IsNullOrEmpty(prevHash) ? null : prevHash;
            row.Hash = Compute(seq, row.PrevHash, row, key);
        }

        /// <summary>The order in which the rows of one save are placed in the chain: by moment, then by their content, so the same rows always get the same places.</summary>
        public static List<FnAuditLog> InChainOrder(IEnumerable<FnAuditLog> rows) =>
            rows.OrderBy(r => r.At.UtcTicks).ThenBy(r => r.EntityType, StringComparer.Ordinal).ThenBy(r => r.EntityId ?? -1).ThenBy(r => r.Action, StringComparer.Ordinal)
                .ThenBy(r => r.Summary, StringComparer.Ordinal).ThenBy(r => r.AfterJson, StringComparer.Ordinal).ThenBy(r => r.BeforeJson, StringComparer.Ordinal).ToList();

        // ------------------------------------------------------------------------------------------ the check

        public const string BreakUnsealed = "Unsealed";
        public const string BreakGap = "Gap";
        public const string BreakOrder = "Order";
        public const string BreakLink = "Link";
        public const string BreakContent = "Content";
        public const string BreakDowngrade = "Downgrade";
        public const string BreakKeyMissing = "KeyMissing";
        public const string BreakAnchor = "Anchor";

        public sealed class ChainBreak
        {
            public string Kind { get; set; }
            public long? Seq { get; set; }
            public int Id { get; set; }
            public string Message { get; set; }
        }

        /// <summary>
        /// Walks the rows in chain order and returns the first break. It holds only the last row it saw, so a table of any size is read in pages.
        /// Start it with the row before the first one to check (or nothing for the whole chain).
        /// </summary>
        public sealed class Walker
        {
            private readonly byte[] _key;
            private long? _prevSeq;
            private string _prevHash;
            private bool _seenKeyed;

            public Walker(byte[] key, long? previousSeq = null, string previousHash = null)
            {
                _key = key;
                _prevSeq = previousSeq;
                _prevHash = previousHash;
                _seenKeyed = previousHash != null && previousHash.StartsWith(Keyed, StringComparison.Ordinal);
            }

            public long Checked { get; private set; }

            public long? LastSeq => _prevSeq;

            public string LastHash => _prevHash;

            public ChainBreak Feed(FnAuditLog row)
            {
                if (row.Seq == null || string.IsNullOrEmpty(row.Hash))
                    return Fail(BreakUnsealed, row, "ردیف شماره " + row.Id + " در سوابق، مهر زنجیره ندارد (پس از آغاز زنجیره نوشته شده، یا مهر آن برداشته شده است).");
                long seq = row.Seq.Value;

                if (_prevSeq.HasValue)
                {
                    if (seq > _prevSeq.Value + 1)
                        return Fail(BreakGap, row, "ردیف‌های شماره " + (_prevSeq.Value + 1) + " تا " + (seq - 1) + " زنجیره در سوابق نیستند؛ حذف شده‌اند.");
                    if (seq <= _prevSeq.Value)
                        return Fail(BreakOrder, row, "ترتیب زنجیره به هم خورده است: ردیف شماره " + seq + " بعد از ردیف شماره " + _prevSeq.Value + " آمده است.");
                    if (!string.Equals(row.PrevHash ?? string.Empty, _prevHash ?? string.Empty, StringComparison.Ordinal))
                        return Fail(BreakLink, row, "پیوند ردیف شماره " + seq + " به ردیف پیش از خود درست نیست: ردیف پیشین پس از ثبت تغییر کرده یا ردیفی میان آن‌ها افزوده شده است.");
                }
                else if (seq == 1 && !string.IsNullOrEmpty(row.PrevHash))
                {
                    return Fail(BreakLink, row, "نخستین ردیف زنجیره نباید ردیف پیشین داشته باشد.");
                }

                string hash = row.Hash;
                bool keyed = hash.StartsWith(Keyed, StringComparison.Ordinal);
                bool plain = hash.StartsWith(Plain, StringComparison.Ordinal);
                if (!keyed && !plain)
                    return Fail(BreakContent, row, "روش مهر ردیف شماره " + seq + " شناخته‌شده نیست.");
                if (plain && _seenKeyed)
                    return Fail(BreakDowngrade, row, "ردیف شماره " + seq + " بدون کلید مهر شده در حالی که ردیف‌های پیش از آن با کلید مهر شده‌اند؛ کلید مهر نباید برداشته شود.");
                if (keyed && _key == null)
                    return Fail(BreakKeyMissing, row, "کلید " + SettingKey + " تنظیم نشده است؛ ردیف شماره " + seq + " با کلید مهر شده و بدون آن قابل بررسی نیست.");

                string expected = Compute(seq, string.IsNullOrEmpty(row.PrevHash) ? null : row.PrevHash, row, keyed ? _key : null);
                if (!string.Equals(expected, hash, StringComparison.Ordinal))
                    return Fail(BreakContent, row, "محتوای ردیف شماره " + seq + " (شناسه " + row.Id + ") پس از ثبت تغییر کرده است.");

                _prevSeq = seq;
                _prevHash = hash;
                _seenKeyed |= keyed;
                Checked++;
                return null;
            }

            private static ChainBreak Fail(string kind, FnAuditLog row, string message) => new ChainBreak { Kind = kind, Seq = row.Seq, Id = row.Id, Message = message };
        }

        // ------------------------------------------------------------------------------------------ writing (the database context)

        private sealed class Head
        {
            public long Seq { get; set; }
            public string Hash { get; set; }
        }

        // H4 (review R1t): outside a transaction the lock belongs to the session and is released after the save. Inside an outer transaction (the caller wraps several saves) the lock belongs to the
        // TRANSACTION and is not released here: it ends with the commit or rollback, so a second writer cannot place rows on the same head between two saves of one transaction.
        public static string LockSql(bool inTransaction) => "DECLARE @r int; EXEC @r = sp_getapplock @Resource = N'" + LockResource + "', @LockMode = N'Exclusive', @LockOwner = N'" + (inTransaction ? "Transaction" : "Session") + "', @LockTimeout = 20000; "
            + "IF @r < 0 THROW 51000, N'ثبت در دفتر حسابرسی ممکن نشد: نوبت ثبت زنجیره در دسترس نیست؛ لحظه‌ای بعد دوباره تلاش کنید.', 1;";

        private const string UnlockSql = "EXEC sp_releaseapplock @Resource = N'" + LockResource + "', @LockOwner = N'Session';";

        /// <summary>
        /// Holds the chain lock (SQL Server) from the moment the new rows are placed in the chain until the save is over. Dispose it in a finally block.
        /// </summary>
        public sealed class Scope : IDisposable, IAsyncDisposable
        {
            private readonly DbContext _context;
            private bool _opened, _locked, _transactionLock;

            internal Scope(DbContext context) => _context = context;

            internal void Lock()
            {
                if (!_context.Database.IsSqlServer()) return;
                _context.Database.OpenConnection();   // the lock belongs to this connection: the head is read and the rows are saved on it too
                _opened = true;
                _transactionLock = _context.Database.CurrentTransaction != null;
                _context.Database.ExecuteSqlRaw(LockSql(_transactionLock));
                _locked = !_transactionLock;
            }

            internal async Task LockAsync(CancellationToken ct)
            {
                if (!_context.Database.IsSqlServer()) return;
                await _context.Database.OpenConnectionAsync(ct);
                _opened = true;
                _transactionLock = _context.Database.CurrentTransaction != null;
                await _context.Database.ExecuteSqlRawAsync(LockSql(_transactionLock), ct);
                _locked = !_transactionLock;
            }

            public void Dispose()
            {
                try { if (_locked) _context.Database.ExecuteSqlRaw(UnlockSql); }
                catch { /* the connection was lost: the lock went with it */ }
                finally
                {
                    _locked = false;
                    if (_opened) { _opened = false; try { _context.Database.CloseConnection(); } catch { } }
                }
            }

            public async ValueTask DisposeAsync()
            {
                try { if (_locked) await _context.Database.ExecuteSqlRawAsync(UnlockSql); }
                catch { /* the connection was lost: the lock went with it */ }
                finally
                {
                    _locked = false;
                    if (_opened) { _opened = false; try { await _context.Database.CloseConnectionAsync(); } catch { } }
                }
            }
        }

        private static List<FnAuditLog> NewRows(DbContext context) =>
            context.ChangeTracker.Entries<FnAuditLog>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();

        private static void Place(List<FnAuditLog> rows, Head head, byte[] key)
        {
            long seq = head?.Seq ?? 0;
            string prev = head?.Hash;
            foreach (FnAuditLog row in InChainOrder(rows))
            {
                seq++;
                Seal(row, seq, prev, key);
                prev = row.Hash;
            }
        }

        private static IQueryable<Head> HeadQuery(DbContext context) =>
            context.Set<FnAuditLog>().AsNoTracking().Where(a => a.Seq != null).OrderByDescending(a => a.Seq).Select(a => new Head { Seq = a.Seq.Value, Hash = a.Hash }).Take(1);

        /// <summary>The configuration of the application (the key lives in it), or null in a host without one.</summary>
        internal static IConfiguration ConfigurationOf(DbContext context)
        {
            try
            {
                IServiceProvider own = ((IInfrastructure<IServiceProvider>)context).Instance;
                IConfiguration found = own.GetService<IConfiguration>();
                if (found != null) return found;
                // the context built its own service provider: the application's one is kept in the options
                IServiceProvider application = own.GetService<IDbContextOptions>()?.FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider;
                return application?.GetService<IConfiguration>();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Places the new audit rows of this save in the chain. Returns the scope that holds the chain lock (dispose it after the save), or null when there is nothing to place.
        /// </summary>
        internal static Scope Begin(DbContext context)
        {
            List<FnAuditLog> rows = NewRows(context);
            if (rows.Count == 0) return null;
            var scope = new Scope(context);
            try
            {
                scope.Lock();
                Place(rows, HeadQuery(context).FirstOrDefault(), KeyFrom(ConfigurationOf(context)));
                return scope;
            }
            catch
            {
                scope.Dispose();
                throw;
            }
        }

        internal static async Task<Scope> BeginAsync(DbContext context, CancellationToken ct)
        {
            List<FnAuditLog> rows = NewRows(context);
            if (rows.Count == 0) return null;
            var scope = new Scope(context);
            try
            {
                await scope.LockAsync(ct);
                Place(rows, await HeadQuery(context).FirstOrDefaultAsync(ct), KeyFrom(ConfigurationOf(context)));
                return scope;
            }
            catch
            {
                await scope.DisposeAsync();
                throw;
            }
        }
    }
}
