namespace WebApi.Controllers
{
    /// <summary>
    /// R1a: role lists (as written in the JWT, upper case, comma separated = any of them) for the WRITE actions of the legacy controllers
    /// (Contracts, Tenders, BuyOrders, Equipments, Processes ...). Reads of these controllers stay open to every signed-in user.
    ///
    /// ★ This matrix is the architect's provisional decision and must be confirmed by the company. PMO is in every list (the legacy system administrator).
    /// The CEO is deliberately in none of them: the CEO approves at gates and does not edit master data.
    /// </summary>
    public static class LegacyRoles
    {
        public const string Pmo = "PMO";

        /// <summary>Contracts, contract equipment, work orders, project charter, contracts for providers: create and update. Delete is <see cref="Pmo"/> only.</summary>
        public const string ContractWriters = "PMO,PLANNING,COMMERCIAL,PROJECTMANAGER";

        /// <summary>Tenders (and the guarantees of a tender).</summary>
        public const string TenderWriters = "PMO,COMMERCIAL,SALES";

        /// <summary>Purchasing: legacy inquiries, buy orders, buy order processes, outgoing buy orders, providers and provider categories.</summary>
        public const string Purchasing = "PMO,COMMERCIAL";

        /// <summary>Contract guarantees.</summary>
        public const string GuarantyWriters = "PMO,COMMERCIAL,FINANCIAL,FINANCEMANAGER,TREASURY";

        /// <summary>Contract provisional statements.</summary>
        public const string ProvisionalStatementWriters = "PMO,FINANCIAL,FINANCEMANAGER,PROJECTMANAGER";

        /// <summary>Engineering definitions: equipment, assemblies, BOMs, processes and their files.</summary>
        public const string EngineeringWriters = "PMO,ENGINEERING,ENGINEER";

        /// <summary>Engineering gates and overrides that an individual engineer must not use on his own work (accept documents, revert a status, assign an engineer).</summary>
        public const string EngineeringManagers = "PMO,ENGINEERING";

        /// <summary>Machines.</summary>
        public const string MachineWriters = "PMO,PRODUCTIONMANAGER,MAINTENANCE,ENGINEERING";

        /// <summary>Holidays.</summary>
        public const string HolidayWriters = "PMO,HR,PLANNING";

        // ---- sensitive legacy reads (R1a2, M8; see LegacyFieldAccess): the reads stay open to every signed-in user, these two fields are not ----

        /// <summary>
        /// The bank accounts of a provider (account number, SHABA, card number) in <c>GET Provider</c>, <c>Provider/providers</c>, <c>Provider/vendor-list</c> and wherever a provider is nested in an answer,
        /// and (R1a2, L-6) the IBAN of the supplier profile in <c>GET procurement/suppliers</c> (the roles that write the profile, COMMERCIAL and PMO, are in the list).
        /// Everybody else gets the number masked to its last four digits. ★ to be confirmed by the company.
        /// </summary>
        public const string ProviderBankReaders = "PMO,COMMERCIAL,FINANCIAL,FINANCEMANAGER,TREASURY";

        /// <summary>
        /// The price of a contract, the price and the accepted price of a provisional statement, the price of an inquiry and the finance price of a buy order
        /// (R1a2, L-6: and the finance price in the supplier-return report of a non-conformance report).
        /// Everybody else gets null. ★ to be confirmed by the company.
        /// </summary>
        public const string PriceReaders = "PMO,CEO,COMMERCIAL,FINANCIAL,FINANCEMANAGER,PLANNING,PROJECTMANAGER,SALES";

        /// <summary>
        /// R1a2 (L-6): the price text of a contract guaranty and of a tender guaranty ("500,000,000"): the price roles and the roles that write guaranties. The treasury writes contract guaranties
        /// but is not a price role; a writer who got null back for the price would send null again with his next edit and erase it. Everybody else gets null. ★ to be confirmed by the company.
        /// </summary>
        public const string GuarantyPriceReaders = PriceReaders + "," + GuarantyWriters + "," + TenderWriters;

        // ---- legacy buy order / assembly workflow: each stage belongs to the role that sees it on the card board (CardBoardService) ----

        /// <summary>Technical score of an inquiry and the "technical scores are complete" step of a buy order (the card board shows the stage to engineering).</summary>
        public const string TechnicalScoring = "PMO,ENGINEERING";

        /// <summary>Buy order stage: the financial department sets the finance documents of the winner.</summary>
        public const string BuyOrderFinance = "PMO,FINANCIAL";

        /// <summary>Buy order stage: the warehouse registers the temporary receipt, accepts or rejects the goods and gives the final receipt.</summary>
        public const string BuyOrderWarehouse = "PMO,WAREHOUSE";

        /// <summary>Buy order stage: quality control accepts or rejects the goods of a temporary receipt.</summary>
        public const string BuyOrderQuality = "PMO,QC";

        /// <summary>Buy order stage: the assembler delivers a buy order that is built in-house.</summary>
        public const string BuyOrderAssembler = "PMO,ASSEMBLER";

        /// <summary>Buy order stage: the production department delivers a buy order that is built in-house.</summary>
        public const string BuyOrderConstruction = "PMO,CONSTRUCTION";

        /// <summary>Everyone who works on a buy order on the card board; they attach and remove the files of their stage (finance documents, inquiries ...).</summary>
        public const string BuyOrderStageActors = "PMO,COMMERCIAL,FINANCIAL,ENGINEERING,QC,WAREHOUSE,ASSEMBLER,CONSTRUCTION";

        /// <summary>Assembly stage: the assembler assembles and final-assembles.</summary>
        public const string AssemblyAssembler = "PMO,ASSEMBLER";

        /// <summary>Assembly stage: quality control accepts or rejects an assembly.</summary>
        public const string AssemblyQuality = "PMO,QC";
    }
}
