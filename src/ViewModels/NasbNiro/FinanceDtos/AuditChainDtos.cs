using System;

namespace ViewModels.NasbNiro.FinanceDtos
{
    /// <summary>R1t D20: the first row of the audit trail that does not fit the hash chain, and what is wrong with it.</summary>
    public class AuditChainBreakDto
    {
        /// <summary>Gap (rows missing), Order, Link (the row before it was changed or a row was slipped in), Content (the row was changed), Downgrade (sealed without the key after rows sealed with it), KeyMissing, Unsealed (a row without a seal after the chain began), Anchor (does not match the hash that the auditor recorded).</summary>
        public string Kind { get; set; }

        /// <summary>The place of the row in the chain (null for an unsealed row).</summary>
        public long? Seq { get; set; }

        /// <summary>The id of the row in the table (0 when the row itself is missing).</summary>
        public int Id { get; set; }

        /// <summary>Persian text for the reader.</summary>
        public string Message { get; set; }

        public DateTimeOffset? At { get; set; }
        public string EntityType { get; set; }
        public string Action { get; set; }
    }

    /// <summary>R1t D20: the answer of <c>GET finance/audit/verify</c>.</summary>
    public class AuditChainVerifyDto
    {
        /// <summary>True when every checked row fits the chain (and the anchor, when one was given).</summary>
        public bool Ok { get; set; }

        /// <summary>Persian summary.</summary>
        public string Message { get; set; }

        /// <summary>How many rows were walked.</summary>
        public long Checked { get; set; }

        /// <summary>The range of chain places that was asked for.</summary>
        public long FromSeq { get; set; }
        public long? ToSeq { get; set; }

        /// <summary>True when the walk started at the first row and went to the last row of the chain.</summary>
        public bool WholeChain { get; set; }

        /// <summary>The last row of the chain and its hash. Write the pair down (or print it): given back as <c>anchorSeq</c> and <c>anchorHash</c> later it proves that nothing up to that row was cut away or rewritten.</summary>
        public long? HeadSeq { get; set; }
        public string HeadHash { get; set; }

        /// <summary>Rows with a place in the chain, and the older rows written before the chain began (they have no seal).</summary>
        public long SealedRows { get; set; }
        public long LegacyRows { get; set; }

        /// <summary>False when no <c>Identity:AuditHmacKey</c> is set: the rows are then sealed with a plain SHA-256, which a person with write access to the table could recompute.</summary>
        public bool KeyConfigured { get; set; }

        public AuditChainBreakDto FirstBreak { get; set; }
        public DateTimeOffset VerifiedAt { get; set; }
    }
}
