using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;

namespace MsTests
{
    /// <summary>R1t D20: the hash chain of the audit trail (compute, link, verify, every kind of break) and the verification endpoint.</summary>
    [TestClass]
    public class AuditChainR1Tests
    {
        private static readonly byte[] KeyOne = System.Text.Encoding.UTF8.GetBytes("chain-test-key-one");
        private static readonly byte[] KeyTwo = System.Text.Encoding.UTF8.GetBytes("chain-test-key-two");
        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 10, 28, 9, 30, 0, TimeSpan.FromHours(3.5));

        private static FnAuditLog Row(int n) => new FnAuditLog
        {
            Id = n, EntityType = "FnVoucher", EntityId = 100 + n, Action = n % 2 == 0 ? "Modified" : "Added", Summary = "ردیف " + n,
            BeforeJson = n % 2 == 0 ? "{\"Status\":\"Draft\"}" : null, AfterJson = "{\"Status\":\"Final\"}", UserId = 40 + n, At = T0.AddMinutes(n), Ip = n % 3 == 0 ? null : "10.0.0." + n,
        };

        /// <summary>A chain of <paramref name="count"/> rows sealed the way the context seals them.</summary>
        private static List<FnAuditLog> Chain(int count, byte[] key)
        {
            var rows = new List<FnAuditLog>();
            string prev = null;
            for (int i = 1; i <= count; i++)
            {
                FnAuditLog row = Row(i);
                AuditChain.Seal(row, i, prev, key);
                prev = row.Hash;
                rows.Add(row);
            }
            return rows;
        }

        private static AuditChain.ChainBreak Walk(IEnumerable<FnAuditLog> rows, byte[] key, long? previousSeq = 0, string previousHash = null)
        {
            var walker = new AuditChain.Walker(key, previousSeq, previousHash);
            foreach (FnAuditLog row in rows)
            {
                AuditChain.ChainBreak broken = walker.Feed(row);
                if (broken != null) return broken;
            }
            return null;
        }

        private static FnAuditLog Copy(FnAuditLog r) => new FnAuditLog
        {
            Id = r.Id, EntityType = r.EntityType, EntityId = r.EntityId, Action = r.Action, Summary = r.Summary, BeforeJson = r.BeforeJson, AfterJson = r.AfterJson, UserId = r.UserId, At = r.At, Ip = r.Ip,
            Seq = r.Seq, PrevHash = r.PrevHash, Hash = r.Hash,
        };

        private static List<FnAuditLog> CopyOf(List<FnAuditLog> rows) => rows.Select(Copy).ToList();

        // ------------------------------------------------------------------------------------------ the hash

        [TestMethod]
        public void The_Hash_Is_Stable_Prefixed_And_Keyed()
        {
            FnAuditLog row = Row(1);
            string plain = AuditChain.Compute(1, null, row, null);
            string keyed = AuditChain.Compute(1, null, row, KeyOne);
            Assert.AreEqual(plain, AuditChain.Compute(1, null, Copy(row), null), "the same row gives the same hash");
            Assert.IsTrue(plain.StartsWith("v0:", StringComparison.Ordinal) && plain.Length == 3 + 64);
            Assert.IsTrue(keyed.StartsWith("v1:", StringComparison.Ordinal) && keyed.Length == 3 + 64);
            Assert.AreNotEqual(plain.Substring(3), keyed.Substring(3), "the key changes the hash");
            Assert.AreNotEqual(keyed, AuditChain.Compute(1, null, row, KeyTwo), "another key gives another hash");
            Assert.IsTrue(plain.Length <= 80 && keyed.Length <= 80, "fits the column");
        }

        [TestMethod]
        public void Every_Field_Of_The_Row_Changes_The_Hash()
        {
            FnAuditLog row = Row(2);
            string baseline = AuditChain.Compute(5, "v0:aa", row, KeyOne);
            var changes = new Dictionary<string, Func<FnAuditLog, FnAuditLog>>
            {
                ["entity type"] = r => { r.EntityType += "x"; return r; },
                ["entity id"] = r => { r.EntityId = 999; return r; },
                ["entity id removed"] = r => { r.EntityId = null; return r; },
                ["action"] = r => { r.Action = "Deleted"; return r; },
                ["summary"] = r => { r.Summary += " "; return r; },
                ["before"] = r => { r.BeforeJson = "{\"Status\":\"Final\"}"; return r; },
                ["before removed"] = r => { r.BeforeJson = null; return r; },
                ["after"] = r => { r.AfterJson = "{\"Status\":\"Draft\"}"; return r; },
                ["user"] = r => { r.UserId += 1; return r; },
                ["moment"] = r => { r.At = r.At.AddTicks(1); return r; },
                ["address"] = r => { r.Ip = "10.9.9.9"; return r; },
                ["address removed"] = r => { r.Ip = null; return r; },
            };
            foreach (var change in changes)
                Assert.AreNotEqual(baseline, AuditChain.Compute(5, "v0:aa", change.Value(Copy(row)), KeyOne), "changing the " + change.Key + " must change the hash");
            Assert.AreNotEqual(baseline, AuditChain.Compute(6, "v0:aa", row, KeyOne), "the place in the chain is part of the hash");
            Assert.AreNotEqual(baseline, AuditChain.Compute(5, "v0:ab", row, KeyOne), "the hash of the row before is part of the hash");
            Assert.AreNotEqual(baseline, AuditChain.Compute(5, null, row, KeyOne), "the first row differs from a row with a predecessor");
        }

        [TestMethod]
        public void The_Moment_Counts_In_Ticks_Whatever_The_Offset()
        {
            FnAuditLog a = Row(3);
            FnAuditLog b = Copy(a);
            b.At = a.At.ToOffset(TimeSpan.Zero);   // the same instant read back with another offset
            Assert.AreEqual(AuditChain.Compute(1, null, a, null), AuditChain.Compute(1, null, b, null));
        }

        [TestMethod]
        public void Neighbouring_Fields_Cannot_Trade_Characters()
        {
            // the canonical text carries the length of every field, so moving a character from one field to the next is not a no-op
            FnAuditLog a = Row(4); a.Action = "Modified"; a.Summary = "x";
            FnAuditLog b = Copy(a); b.Action = "Modifie"; b.Summary = "dx";
            Assert.AreNotEqual(AuditChain.Canonical(1, null, a), AuditChain.Canonical(1, null, b));
            Assert.AreNotEqual(AuditChain.Compute(1, null, a, null), AuditChain.Compute(1, null, b, null));

            FnAuditLog c = Row(4); c.Ip = null;
            FnAuditLog d = Copy(c); d.Ip = "";
            Assert.AreNotEqual(AuditChain.Compute(1, null, c, null), AuditChain.Compute(1, null, d, null), "null and empty are different values");

            FnAuditLog e = Row(4); e.BeforeJson = "~";
            FnAuditLog f = Copy(e); f.BeforeJson = null;
            Assert.AreNotEqual(AuditChain.Compute(1, null, e, null), AuditChain.Compute(1, null, f, null), "the text '~' is not the mark of null");
        }

        [TestMethod]
        public void Rows_Of_One_Save_Are_Placed_In_A_Fixed_Order()
        {
            List<FnAuditLog> rows = Enumerable.Range(1, 8).Select(Row).ToList();
            rows.Add(Copy(rows[0]));   // two rows alike in every field may sit in either order
            List<string> forward = AuditChain.InChainOrder(rows).Select(r => r.Summary + "|" + r.At.UtcTicks).ToList();
            rows.Reverse();
            List<string> backward = AuditChain.InChainOrder(rows).Select(r => r.Summary + "|" + r.At.UtcTicks).ToList();
            CollectionAssert.AreEqual(forward, backward);
            Assert.IsTrue(AuditChain.InChainOrder(rows).Zip(AuditChain.InChainOrder(rows).Skip(1), (x, y) => x.At.UtcTicks <= y.At.UtcTicks).All(ok => ok), "earlier moments first");
        }

        // ------------------------------------------------------------------------------------------ the walk

        [TestMethod]
        public void A_Clean_Chain_Walks_Without_A_Break_Plain_Or_Keyed()
        {
            foreach (byte[] key in new byte[][] { null, KeyOne })
            {
                var walker = new AuditChain.Walker(key, 0, null);
                foreach (FnAuditLog row in Chain(40, key)) Assert.IsNull(walker.Feed(row));
                Assert.AreEqual(40, walker.Checked);
                Assert.AreEqual(40L, walker.LastSeq);
            }
        }

        [TestMethod]
        public void A_Walk_Can_Start_In_The_Middle_From_The_Row_Before()
        {
            List<FnAuditLog> chain = Chain(10, KeyOne);
            Assert.IsNull(Walk(chain.Skip(5), KeyOne, 5, chain[4].Hash));
            AuditChain.ChainBreak wrong = Walk(chain.Skip(5), KeyOne, 5, chain[3].Hash);
            Assert.AreEqual(AuditChain.BreakLink, wrong.Kind, "the row before the range is not the one the first row points at");
        }

        [TestMethod]
        public void A_Changed_Row_Is_Found_At_Its_Own_Place()
        {
            foreach (byte[] key in new byte[][] { null, KeyOne })
            {
                List<FnAuditLog> chain = Chain(8, key);
                chain[3].Summary = "مبلغ دیگر";   // row 4 is edited, its hash is left as it was
                AuditChain.ChainBreak broken = Walk(chain, key);
                Assert.IsNotNull(broken);
                Assert.AreEqual(AuditChain.BreakContent, broken.Kind);
                Assert.AreEqual(4L, broken.Seq);
                Assert.AreEqual(4, broken.Id);
            }
        }

        [TestMethod]
        public void A_Row_Edited_And_Re_Hashed_Alone_Breaks_The_Link_To_The_Next_Row()
        {
            List<FnAuditLog> chain = Chain(8, null);
            chain[3].AfterJson = "{\"Status\":\"Posted\"}";
            chain[3].Hash = AuditChain.Compute(4, chain[3].PrevHash, chain[3], null);   // plain SHA-256: anybody can recompute this one row
            AuditChain.ChainBreak broken = Walk(chain, null);
            Assert.AreEqual(AuditChain.BreakLink, broken.Kind);
            Assert.AreEqual(5L, broken.Seq, "the row after the edited one no longer points at it");
        }

        [TestMethod]
        public void A_Deleted_Row_Leaves_A_Gap()
        {
            List<FnAuditLog> chain = Chain(8, KeyOne);
            chain.RemoveAt(2);   // row 3
            AuditChain.ChainBreak broken = Walk(chain, KeyOne);
            Assert.AreEqual(AuditChain.BreakGap, broken.Kind);
            Assert.AreEqual(4L, broken.Seq);
            StringAssert.Contains(broken.Message, "3");

            List<FnAuditLog> head = Chain(8, KeyOne);
            head.RemoveAt(0);   // the first row is gone
            Assert.AreEqual(AuditChain.BreakGap, Walk(head, KeyOne).Kind, "a chain that does not start at 1 is not whole");
        }

        [TestMethod]
        public void A_Repeated_Or_Out_Of_Order_Row_Is_Found()
        {
            List<FnAuditLog> chain = Chain(6, KeyOne);
            var repeated = chain.Take(3).Concat(new[] { Copy(chain[2]) }).Concat(chain.Skip(3)).ToList();
            AuditChain.ChainBreak broken = Walk(repeated, KeyOne);
            Assert.AreEqual(AuditChain.BreakOrder, broken.Kind);
            Assert.AreEqual(3L, broken.Seq);

            var swapped = chain.Take(2).Concat(new[] { chain[3], chain[2] }).Concat(chain.Skip(4)).ToList();
            Assert.IsNotNull(Walk(swapped, KeyOne), "swapped rows are not accepted");
        }

        [TestMethod]
        public void A_Forged_Row_Slipped_Into_The_Middle_Is_Found()
        {
            // without the key the forger cannot make a row that verifies; with the plain hash he can make one that fits its own place, and the real row behind it is then out of order
            List<FnAuditLog> chain = Chain(6, null);
            FnAuditLog forged = Row(99);
            AuditChain.Seal(forged, 4, chain[2].Hash, null);
            var withForged = chain.Take(3).Concat(new[] { forged }).Concat(chain.Skip(3)).ToList();
            AuditChain.ChainBreak broken = Walk(withForged, null);
            Assert.AreEqual(AuditChain.BreakOrder, broken.Kind);

            List<FnAuditLog> keyed = Chain(6, KeyOne);
            FnAuditLog forgedPlain = Row(99);
            AuditChain.Seal(forgedPlain, 4, keyed[2].Hash, null);   // sealed without the key
            var mixed = keyed.Take(3).Concat(new[] { forgedPlain }).ToList();
            Assert.AreEqual(AuditChain.BreakDowngrade, Walk(mixed, KeyOne).Kind, "a plain seal after keyed rows is itself the break");
        }

        [TestMethod]
        public void A_Chain_Cut_And_Re_Joined_Is_Found()
        {
            List<FnAuditLog> chain = Chain(10, KeyOne);
            var cut = chain.Take(4).Concat(chain.Skip(7)).ToList();   // rows 5, 6, 7 removed
            AuditChain.ChainBreak broken = Walk(cut, KeyOne);
            Assert.AreEqual(AuditChain.BreakGap, broken.Kind);
            Assert.AreEqual(8L, broken.Seq);
            // and re-numbering the rows behind the cut does not help: their hashes cover the number
            List<FnAuditLog> renumbered = CopyOf(cut);
            for (int i = 4; i < renumbered.Count; i++) renumbered[i].Seq = i + 1;
            Assert.IsNotNull(Walk(renumbered, KeyOne));
        }

        [TestMethod]
        public void The_Key_Cannot_Be_Switched_Off_Or_Changed_Unnoticed()
        {
            List<FnAuditLog> chain = Chain(5, KeyOne);
            Assert.AreEqual(AuditChain.BreakKeyMissing, Walk(chain, null).Kind, "keyed rows and no key configured");
            Assert.AreEqual(AuditChain.BreakContent, Walk(chain, KeyTwo).Kind, "another key");
            Assert.AreEqual(1L, Walk(chain, KeyTwo).Seq);

            // plain first, keyed later: the upgrade of an installation that gets its key afterwards is fine
            var upgrade = new List<FnAuditLog>();
            string prev = null;
            for (int i = 1; i <= 6; i++)
            {
                FnAuditLog row = Row(i);
                AuditChain.Seal(row, i, prev, i <= 3 ? null : KeyOne);
                prev = row.Hash;
                upgrade.Add(row);
            }
            Assert.IsNull(Walk(upgrade, KeyOne));

            // keyed first, plain later: the key was removed
            var downgrade = new List<FnAuditLog>();
            prev = null;
            for (int i = 1; i <= 6; i++)
            {
                FnAuditLog row = Row(i);
                AuditChain.Seal(row, i, prev, i <= 3 ? KeyOne : null);
                prev = row.Hash;
                downgrade.Add(row);
            }
            AuditChain.ChainBreak broken = Walk(downgrade, KeyOne);
            Assert.AreEqual(AuditChain.BreakDowngrade, broken.Kind);
            Assert.AreEqual(4L, broken.Seq);
        }

        [TestMethod]
        public void A_Row_Without_A_Seal_Or_With_An_Unknown_Seal_Is_Found()
        {
            List<FnAuditLog> chain = Chain(4, KeyOne);
            chain[2].Seq = null;
            Assert.AreEqual(AuditChain.BreakUnsealed, Walk(chain, KeyOne).Kind);

            chain = Chain(4, KeyOne);
            chain[1].Hash = null;
            Assert.AreEqual(AuditChain.BreakUnsealed, Walk(chain, KeyOne).Kind);

            chain = Chain(4, KeyOne);
            chain[1].Hash = "v9:" + new string('a', 64);
            Assert.AreEqual(AuditChain.BreakContent, Walk(chain, KeyOne).Kind, "a method that this version does not know");

            chain = Chain(3, null);
            chain[0].PrevHash = "v0:" + new string('b', 64);   // the first row cannot have a predecessor
            Assert.AreEqual(AuditChain.BreakLink, Walk(chain, null).Kind);
        }

        // ------------------------------------------------------------------------------------------ the context seals the rows

        private static AppHost HostWithKey(string key) =>
            new AppHost("chain_" + Guid.NewGuid().ToString("N"), key == null ? null : new Dictionary<string, string> { ["Identity:AuditHmacKey"] = key });

        private static void Log(AppHost host, int count, string tag, ClaimsPrincipal who = null)
        {
            host.Write(uow =>
            {
                for (int i = 0; i < count; i++)
                    uow.Set<FnAuditLog>().Add(new FnAuditLog { EntityType = "Test", EntityId = i, Action = "Modified", Summary = tag + " " + i, UserId = 7, At = DateTimeOffset.Now.AddMilliseconds(i), BeforeJson = "{\"n\":" + i + "}" });
                uow.SaveChanges();
            }, who);
        }

        private static List<FnAuditLog> Stored(AppHost host) => host.Read(uow => uow.Set<FnAuditLog>().AsNoTracking().OrderBy(a => a.Id).ToList());

        [TestMethod]
        public void The_Context_Places_Every_New_Row_In_The_Chain_Save_After_Save()
        {
            AppHost host = HostWithKey(null);
            Log(host, 1, "first");
            Log(host, 3, "second");
            Log(host, 2, "third");

            List<FnAuditLog> rows = Stored(host).OrderBy(r => r.Seq).ToList();
            Assert.AreEqual(6, rows.Count);
            CollectionAssert.AreEqual(Enumerable.Range(1, 6).Select(i => (long?)i).ToList(), rows.Select(r => r.Seq).ToList(), "the places are 1, 2, 3 ... without a gap, across saves");
            Assert.IsNull(rows[0].PrevHash);
            for (int i = 1; i < rows.Count; i++) Assert.AreEqual(rows[i - 1].Hash, rows[i].PrevHash, "row " + (i + 1) + " points at the row before it");
            foreach (FnAuditLog row in rows)
            {
                Assert.IsTrue(row.Hash.StartsWith("v0:", StringComparison.Ordinal), "no key: the plain seal");
                Assert.AreEqual(row.Hash, AuditChain.Compute(row.Seq.Value, row.PrevHash, row, null), "the stored hash is the hash of the stored row");
            }
            Assert.IsNull(Walk(rows, null));
        }

        [TestMethod]
        public void The_Key_From_The_Configuration_Seals_With_An_Hmac()
        {
            AppHost host = HostWithKey("a-key-for-the-chain-tests");
            Log(host, 2, "keyed");
            List<FnAuditLog> rows = Stored(host).OrderBy(r => r.Seq).ToList();
            Assert.IsTrue(rows.All(r => r.Hash.StartsWith("v1:", StringComparison.Ordinal)));
            byte[] key = System.Text.Encoding.UTF8.GetBytes("a-key-for-the-chain-tests");
            Assert.IsNull(Walk(rows, key));
            Assert.AreEqual(AuditChain.BreakContent, Walk(rows, KeyOne).Kind, "another key does not verify");
            Assert.AreEqual(AuditChain.BreakKeyMissing, Walk(rows, null).Kind);
        }

        [TestMethod]
        public void A_Change_Of_An_Audited_Record_Is_Sealed_Together_With_The_Row_It_Writes()
        {
            AppHost host = HostWithKey(null);
            ClaimsPrincipal fm = FinanceTestSupport.Fm;
            host.Write(uow =>
            {
                var setting = new FnSetting { Key = "Chain.Test", Value = "1", Title = "t" };
                uow.Set<FnSetting>().Add(setting);
                uow.SaveChanges();
                setting.Value = "2";
                uow.SaveChanges();
            }, fm);

            List<FnAuditLog> rows = Stored(host).OrderBy(r => r.Seq).ToList();
            Assert.IsTrue(rows.Count >= 1, "the audit trail wrote a row for the setting");
            Assert.IsTrue(rows.All(r => r.Seq.HasValue && r.Hash != null), "every row the trail writes is sealed");
            Assert.IsNull(Walk(rows, null));
            Assert.IsTrue(rows.Any(r => r.EntityType == nameof(FnSetting) && r.UserId == 32), "the user of the request is recorded");
        }

        [TestMethod]
        public void A_Save_Without_Audit_Rows_Takes_No_Place_In_The_Chain()
        {
            AppHost host = HostWithKey(null);
            Log(host, 2, "before");
            host.Write(uow =>
            {
                uow.Set<FnAuditLog>().Add(new FnAuditLog { EntityType = "Test", EntityId = 1, Action = "Added", Summary = "x", UserId = 1, At = DateTimeOffset.Now });
                uow.Set<FnAuditLog>().Local.ToList().ForEach(r => { });   // nothing else
                uow.SaveChanges();
                uow.SaveChanges();   // a second save with nothing new
            });
            Assert.AreEqual(3, Stored(host).Count);
            Assert.IsNull(Walk(Stored(host).OrderBy(r => r.Seq), null));
        }

        [TestMethod]
        public void Rows_That_Differ_Only_By_Id_Are_Still_Counted_Once_Each()
        {
            AppHost host = HostWithKey(null);
            host.Write(uow =>
            {
                DateTimeOffset at = DateTimeOffset.Now;
                for (int i = 0; i < 4; i++)   // four rows alike in every field: one save
                    uow.Set<FnAuditLog>().Add(new FnAuditLog { EntityType = "Test", EntityId = 1, Action = "Added", Summary = "same", UserId = 1, At = at });
                uow.SaveChanges();
            });
            List<FnAuditLog> rows = Stored(host).OrderBy(r => r.Seq).ToList();
            CollectionAssert.AreEqual(new long?[] { 1, 2, 3, 4 }, rows.Select(r => r.Seq).ToArray());
            Assert.IsNull(Walk(rows, null));
        }

        // ------------------------------------------------------------------------------------------ the check endpoint

        private static Task<ResponseDto<AuditChainVerifyDto>> Verify(AppHost host, ClaimsPrincipal who, long? from = null, long? to = null, long? anchorSeq = null, string anchorHash = null) =>
            host.As(who, sp => sp.GetRequiredService<IAuditChainService>().VerifyAsync(from, to, anchorSeq, anchorHash, CancellationToken.None));

        [TestMethod]
        public async Task The_Check_Is_For_The_Auditor_And_The_Finance_Manager_Only()
        {
            AppHost host = HostWithKey(null);
            Log(host, 2, "role");
            foreach (ClaimsPrincipal allowed in new[] { FinanceTestSupport.Auditor, FinanceTestSupport.Fm })
            {
                ResponseDto<AuditChainVerifyDto> ok = await Verify(host, allowed);
                Assert.IsTrue(ok.Success, ok.Message);
                Assert.IsTrue(ok.Data.Ok);
            }
            foreach (ClaimsPrincipal refused in new[] { FinanceTestSupport.Fin, FinanceTestSupport.Tr, FinanceTestSupport.Ceo, AppHost.Principal(50, "pmo.one", "PMO") })
            {
                ResponseDto<AuditChainVerifyDto> no = await Verify(host, refused);
                Assert.IsFalse(no.Success);
                StringAssert.Contains(no.Message, "حسابرس");
            }

            var action = typeof(WebApi.Controllers.FinanceAuditChainController).GetMethod("VerifyAsync");
            var roles = action.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Select(a => a.Roles).Single(r => !string.IsNullOrEmpty(r));
            CollectionAssert.AreEquivalent(new[] { "AUDITOR", "FINANCEMANAGER" }, roles.Split(','));
            Assert.IsTrue(typeof(WebApi.Controllers.FinanceAuditChainController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Any(), "signed-in users only");
        }

        [TestMethod]
        public async Task A_Clean_Chain_Verifies_And_Reports_Its_Head()
        {
            AppHost host = HostWithKey("verify-key");
            Log(host, 5, "clean");
            AuditChainVerifyDto result = FinanceTestSupport.Ok(await Verify(host, FinanceTestSupport.Auditor));
            Assert.IsTrue(result.Ok, result.Message);
            Assert.AreEqual(5, result.Checked);
            Assert.AreEqual(5L, result.HeadSeq);
            Assert.AreEqual(5, result.SealedRows);
            Assert.AreEqual(0, result.LegacyRows);
            Assert.IsTrue(result.KeyConfigured);
            Assert.IsTrue(result.WholeChain);
            Assert.IsNull(result.FirstBreak);
            Assert.AreEqual(Stored(host).OrderBy(r => r.Seq).Last().Hash, result.HeadHash);
            StringAssert.Contains(result.Message, "سالم");

            AuditChainVerifyDto part = FinanceTestSupport.Ok(await Verify(host, FinanceTestSupport.Auditor, from: 2, to: 4));
            Assert.IsTrue(part.Ok, part.Message);
            Assert.AreEqual(3, part.Checked);
            Assert.IsFalse(part.WholeChain);
            Assert.AreEqual(5L, part.HeadSeq);
        }

        [TestMethod]
        public async Task An_Empty_Trail_And_Bad_Arguments_Are_Answered_Plainly()
        {
            AppHost host = HostWithKey(null);
            AuditChainVerifyDto empty = FinanceTestSupport.Ok(await Verify(host, FinanceTestSupport.Fm));
            Assert.IsTrue(empty.Ok);
            Assert.AreEqual(0, empty.Checked);
            Assert.IsNull(empty.HeadSeq);
            Assert.IsFalse(empty.KeyConfigured);

            Assert.IsFalse((await Verify(host, FinanceTestSupport.Fm, from: 0)).Success);
            Assert.IsFalse((await Verify(host, FinanceTestSupport.Fm, from: 5, to: 3)).Success);
            Assert.IsFalse((await Verify(host, FinanceTestSupport.Fm, anchorSeq: 3)).Success, "an anchor needs its hash");
            Assert.IsFalse((await Verify(host, FinanceTestSupport.Fm, anchorHash: "v0:abc")).Success, "an anchor needs its place");
        }

        [TestMethod]
        public async Task The_Head_Written_Down_Earlier_Is_Proof_That_The_End_Was_Not_Cut()
        {
            AppHost host = HostWithKey("anchor-key");
            Log(host, 3, "anchor");
            AuditChainVerifyDto early = FinanceTestSupport.Ok(await Verify(host, FinanceTestSupport.Auditor));
            Log(host, 4, "later");

            AuditChainVerifyDto good = FinanceTestSupport.Ok(await Verify(host, FinanceTestSupport.Auditor, anchorSeq: early.HeadSeq, anchorHash: early.HeadHash));
            Assert.IsTrue(good.Ok, good.Message);
            Assert.AreEqual(7, good.Checked);
            StringAssert.Contains(good.Message, "نشانی");

            AuditChainVerifyDto wrong = FinanceTestSupport.Ok(await Verify(host, FinanceTestSupport.Auditor, anchorSeq: early.HeadSeq, anchorHash: "v1:" + new string('0', 64)));
            Assert.IsFalse(wrong.Ok);
            Assert.AreEqual(AuditChain.BreakAnchor, wrong.FirstBreak.Kind);
            Assert.AreEqual(3L, wrong.FirstBreak.Seq);

            AuditChainVerifyDto beyond = FinanceTestSupport.Ok(await Verify(host, FinanceTestSupport.Auditor, anchorSeq: 99, anchorHash: early.HeadHash));
            Assert.IsFalse(beyond.Ok, "the recorded head is no longer in the trail: the end was cut");
            Assert.AreEqual(AuditChain.BreakAnchor, beyond.FirstBreak.Kind);
        }

        // ------------------------------------------------------------------------------------------ tampering at the database

        /// <summary>
        /// A second context on the same in-memory database that has none of the application's hooks: it can change and delete rows of the trail the way a person with write access to the SQL table could.
        /// (The application's own context refuses to change or delete a row of the trail at all.)
        /// </summary>
        private sealed class RawLogContext : DbContext
        {
            public RawLogContext(DbContextOptions options) : base(options) { }
            public DbSet<FnAuditLog> Logs { get; set; }
            protected override void OnModelCreating(ModelBuilder builder) => builder.Entity<FnAuditLog>().ToTable("FnAuditLogs").HasKey(x => x.Id);
        }

        private static void Tamper(AppHost host, Action<RawLogContext> change)
        {
            using (var raw = new RawLogContext(host.RawOptions<RawLogContext>()))
            {
                change(raw);
                raw.SaveChanges();
            }
        }

        [TestMethod]
        public async Task The_Application_Refuses_To_Change_Or_Delete_A_Row_Of_The_Trail()
        {
            AppHost host = HostWithKey(null);
            Log(host, 2, "append only");
            Assert.ThrowsExactly<InvalidOperationException>(() => host.Write(uow =>
            {
                FnAuditLog row = uow.Set<FnAuditLog>().OrderBy(r => r.Id).First();
                row.Summary = "changed";
                uow.SaveChanges();
            }));
            Assert.ThrowsExactly<InvalidOperationException>(() => host.Write(uow =>
            {
                uow.Set<FnAuditLog>().Remove(uow.Set<FnAuditLog>().OrderBy(r => r.Id).First());
                uow.SaveChanges();
            }));
            AuditChainVerifyDto result = FinanceTestSupport.Ok(await Verify(host, FinanceTestSupport.Auditor));
            Assert.IsTrue(result.Ok, result.Message);
            Assert.AreEqual(2, result.Checked, "the refused saves left no trace in the chain");
        }

        [TestMethod]
        public async Task A_Row_Changed_In_The_Database_Is_Reported_With_Its_Place_And_Details()
        {
            AppHost host = HostWithKey("tamper-key");
            Log(host, 6, "tamper");
            Tamper(host, raw => raw.Logs.Single(r => r.Seq == 4).AfterJson = "{\"amount\":1}");
            AuditChainVerifyDto result = FinanceTestSupport.Ok(await Verify(host, FinanceTestSupport.Auditor));
            Assert.IsFalse(result.Ok);
            Assert.AreEqual(AuditChain.BreakContent, result.FirstBreak.Kind);
            Assert.AreEqual(4L, result.FirstBreak.Seq);
            Assert.AreEqual("Test", result.FirstBreak.EntityType);
            Assert.AreEqual("Modified", result.FirstBreak.Action);
            Assert.IsNotNull(result.FirstBreak.At);
            Assert.AreEqual(3, result.Checked, "the rows before the break were checked");
            StringAssert.Contains(result.Message, "سالم نیست");

            // a range before the change still verifies, a range that includes it does not
            Assert.IsTrue(FinanceTestSupport.Ok(await Verify(host, FinanceTestSupport.Auditor, from: 1, to: 3)).Ok);
            Assert.IsFalse(FinanceTestSupport.Ok(await Verify(host, FinanceTestSupport.Auditor, from: 3, to: 5)).Ok);
        }

        [TestMethod]
        public async Task A_Row_Deleted_In_The_Database_Is_Reported_As_A_Gap()
        {
            AppHost host = HostWithKey(null);
            Log(host, 6, "delete");
            Tamper(host, raw => raw.Logs.Remove(raw.Logs.Single(r => r.Seq == 3)));
            AuditChainVerifyDto result = FinanceTestSupport.Ok(await Verify(host, FinanceTestSupport.Auditor));
            Assert.IsFalse(result.Ok);
            Assert.AreEqual(AuditChain.BreakGap, result.FirstBreak.Kind);
            Assert.AreEqual(4L, result.FirstBreak.Seq);

            // a range that starts right behind the hole cannot be anchored
            AuditChainVerifyDto behind = FinanceTestSupport.Ok(await Verify(host, FinanceTestSupport.Auditor, from: 4));
            Assert.IsFalse(behind.Ok);
            Assert.AreEqual(AuditChain.BreakGap, behind.FirstBreak.Kind);
            Assert.AreEqual(3L, behind.FirstBreak.Seq);
        }

        [TestMethod]
        public async Task The_Newest_Rows_Cut_Off_Show_Only_Against_The_Head_Written_Down()
        {
            AppHost host = HostWithKey("cut-key");
            Log(host, 6, "cut");
            AuditChainVerifyDto before = FinanceTestSupport.Ok(await Verify(host, FinanceTestSupport.Auditor));
            Tamper(host, raw => raw.Logs.RemoveRange(raw.Logs.Where(r => r.Seq > 4)));

            AuditChainVerifyDto without = FinanceTestSupport.Ok(await Verify(host, FinanceTestSupport.Auditor));
            Assert.IsTrue(without.Ok, "the shorter chain is still a valid chain: this is what the anchor is for");
            Assert.AreEqual(4L, without.HeadSeq);

            AuditChainVerifyDto anchored = FinanceTestSupport.Ok(await Verify(host, FinanceTestSupport.Auditor, anchorSeq: before.HeadSeq, anchorHash: before.HeadHash));
            Assert.IsFalse(anchored.Ok);
            Assert.AreEqual(AuditChain.BreakAnchor, anchored.FirstBreak.Kind);
        }

        [TestMethod]
        public async Task A_Row_Put_Into_The_Table_Beside_The_Chain_Is_Reported_As_Unsealed()
        {
            AppHost host = HostWithKey(null);
            // a row from before the chain began is fine ...
            Tamper(host, raw => raw.Logs.Add(new FnAuditLog { EntityType = "Old", Action = "Added", Summary = "written before the chain existed", UserId = 1, At = DateTimeOffset.Now.AddDays(-30) }));
            Log(host, 3, "chain");
            AuditChainVerifyDto fine = FinanceTestSupport.Ok(await Verify(host, FinanceTestSupport.Auditor));
            Assert.IsTrue(fine.Ok, fine.Message);
            Assert.AreEqual(1, fine.LegacyRows);
            Assert.AreEqual(3, fine.SealedRows);
            StringAssert.Contains(fine.Message, "قدیمی");

            // ... a row without a seal that came after it is not
            Tamper(host, raw => raw.Logs.Add(new FnAuditLog { EntityType = "FnVoucher", EntityId = 5, Action = "Modified", Summary = "slipped in", UserId = 1, At = DateTimeOffset.Now }));
            AuditChainVerifyDto bad = FinanceTestSupport.Ok(await Verify(host, FinanceTestSupport.Auditor));
            Assert.IsFalse(bad.Ok);
            Assert.AreEqual(AuditChain.BreakUnsealed, bad.FirstBreak.Kind);
            Assert.IsNull(bad.FirstBreak.Seq);
            Assert.AreEqual("FnVoucher", bad.FirstBreak.EntityType);
        }

        [TestMethod]
        public async Task A_Long_Chain_Is_Walked_Page_After_Page()
        {
            AppHost host = HostWithKey(null);
            int rows = AuditChainService.PageSize * 2 + 37;
            host.Write(uow =>
            {
                for (int i = 0; i < rows; i++)
                    uow.Set<FnAuditLog>().Add(new FnAuditLog { EntityType = "Bulk", EntityId = i, Action = "Added", Summary = "n" + i, UserId = 1, At = T0.AddSeconds(i) });
                uow.SaveChanges();
            });
            AuditChainVerifyDto whole = FinanceTestSupport.Ok(await Verify(host, FinanceTestSupport.Auditor));
            Assert.IsTrue(whole.Ok, whole.Message);
            Assert.AreEqual(rows, whole.Checked);

            Tamper(host, raw => raw.Logs.Single(r => r.Seq == AuditChainService.PageSize + 1).Summary = "second page");   // first row of the second page
            AuditChainVerifyDto bad = FinanceTestSupport.Ok(await Verify(host, FinanceTestSupport.Auditor));
            Assert.IsFalse(bad.Ok);
            Assert.AreEqual((long)AuditChainService.PageSize + 1, bad.FirstBreak.Seq);
            Assert.AreEqual(AuditChainService.PageSize, bad.Checked);
        }

        [TestMethod]
        public async Task A_Different_Key_On_The_Server_Is_Reported_At_The_First_Row()
        {
            AppHost host = HostWithKey("the-real-key");
            Log(host, 3, "keyed");
            // the same trail checked by a server whose key is another one, and by a server without a key
            foreach (var settings in new[] { new Dictionary<string, string> { ["Identity:AuditHmacKey"] = "another-key" }, new Dictionary<string, string>() })
            {
                IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
                ResponseDto<AuditChainVerifyDto> answer = await host.As(FinanceTestSupport.Auditor, sp =>
                    new AuditChainService(sp.GetRequiredService<DataLayer.Context.IUnitOfWork>(), sp.GetRequiredService<IInventoryUserContext>(), configuration).VerifyAsync(null, null, null, null, CancellationToken.None));
                AuditChainVerifyDto result = FinanceTestSupport.Ok(answer);
                Assert.IsFalse(result.Ok);
                Assert.AreEqual(1L, result.FirstBreak.Seq);
                Assert.AreEqual(settings.Count == 0 ? AuditChain.BreakKeyMissing : AuditChain.BreakContent, result.FirstBreak.Kind);
            }
        }
    }
}
