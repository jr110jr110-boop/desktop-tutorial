# Local AI and CEO voice dashboard — design and backend brief (release A1)

Company rule: during an internet outage only the Claude seats (engineering design) may stop; every critical operation continues on the **company's own AI server**, fully offline. Hardware is not bought yet: the software must run against any OpenAI-compatible local inference endpoint, and be fully testable in CI with a deterministic mock.

## 1. Principles (binding)
1. **Numbers come from PMIS, never from the model.** The model only selects tools, reads their results, writes Persian text and drafts. Every figure in an answer must appear verbatim in a tool result of the same conversation; the gateway verifies this (numeric-claim check: every number token in the answer, after Persian/Arabic digit normalisation and thousands-separator removal, must exist in the tool results or the user's question; otherwise the answer is replaced by the tool results in a fixed template and the event is logged).
2. **The AI acts as the user, never more.** Every tool call runs with the caller's JWT/roles through the existing services (same authorisation, same row filtering). Scheduled seats run as a dedicated service user per seat with only the roles listed for that seat.
3. **Drafts and gates.** The AI may create drafts only through existing "draft" operations (purchase request draft, letter draft, NCR draft, voucher draft, report narrative). It never approves, finalises, pays, releases or deletes. Approval is always a human action in the UI (click + the user's own session; for money above a threshold also a PIN re-entry ★).
4. **Every AI action is audited** (prompt, tools called with arguments, results hash, answer, model id, latency, user/seat) in an append-only table; retention configurable (default 3 years ★).
5. **Offline.** No external call from this module except the separate Claude gateway (§7). Models, voices and embeddings are installed from an offline bundle.
6. **Degrade, don't fail.** If the LLM is down, dashboards and all PMIS functions keep working; voice/text assistant answers with "تحلیل هوشمند موقتاً در دسترس نیست" and shows the deterministic dashboard; seats' jobs queue and resume.

## 2. Components
- **Inference server(s)** (Linux, GPU): OpenAI-compatible HTTP endpoint (vLLM or llama.cpp server). Model chosen at procurement time from open-weight instruct models with strong Persian and tool calling; acceptance = passes the Persian evaluation set (§8). Two logical endpoints: `primary` (large model) and `fallback` (smaller model on a second GPU or CPU) — the gateway fails over automatically by health check.
- **Speech**: STT service (faster-whisper large-v3 class, Persian) and TTS service (Persian voice, e.g. Piper fa_IR class) — local HTTP services with simple JSON/WAV APIs; the gateway adapts to them through `ISpeechToText` / `ITextToSpeech` interfaces (configurable URLs).
- **Embeddings + retrieval** for documents (procedures, work instructions, standards excerpts the company owns, letters, minutes): local embedding endpoint (OpenAI-compatible `/embeddings`), vectors stored in SQL Server (VARBINARY + cosine in .NET for the first release ★; SQL Server 2025 native vector type when the server runs 2025 — detect and use). Retrieval results always carry document id + revision for citation.
- **PMIS AI gateway** (inside the PMIS backend, .NET): conversation API, tool registry, seat scheduler, verification, audit, failover. Tables prefix `Ai`, routes `api/v1/ai`.

## 3. Tool registry (read tools)
- A tool = name, Persian description, JSON schema of arguments, required roles, and a C# delegate calling an existing service method; results trimmed to a token budget (lists: top N with total count).
- Release A1 tools (wrap existing services, read only): CEO dashboard; finance dashboard, trial balance summary, cash position, receivables ageing, payables due; stock of an item / items below minimum / item cardex summary; purchase requests and orders status (by number, supplier, project); supplier profile and grade; project portfolio, project status (SPI/CPI, late activities, next milestones), change requests, guarantees expiring; quality dashboard, open NCRs/CAPAs, calibration due, ITP status of a work order; MDR status of a project; document search (retrieval); "explain alert" (returns the alert's source records).
- Tool results include record ids and front-end links so the UI renders clickable citations.

## 4. Conversation API
- `POST ai/chat` {conversationId?, text} → {answer, citations[{type,id,title,link}], toolCalls[], verified:bool, model}. Streaming optional (SSE) ★.
- `POST ai/voice` (multipart audio webm/wav) → STT → chat → TTS → {transcript, answer, audioUrl/base64, citations}. Push-to-talk in the UI; no always-on microphone ★.
- System prompt (Persian), fixed and versioned in the database (`AiPromptVersion`), states the principles, the company context and the answer style (short, numbers with units and dates in Persian calendar, always cite).
- Max tool-call rounds 6 ★; timeout 60 s ★; conversation memory = last 10 turns ★.

## 5. Seats (scheduled assistants) — release A1
Each seat: code, Persian title, service user + roles, schedule (cron-like, server-local), trigger events (optional), prompt version, enabled flag, output kind. Outputs land as **drafts or notes in the module's own gate**, never as final records.
- `CEO-BRIEF` daily 07:00 ★: Persian daily briefing (text + audio file) from CEO dashboard, alerts, cash, projects red/amber — read at the start of the CEO dashboard and playable.
- `PMO-WEEKLY` weekly on the data-date weekday after the snapshot: narrative for the internal weekly report (causes of slippage from late activities, change requests) → stored as a draft narrative attached to the report issue; PMO edits/approves.
- `WH-REORDER` daily: items below minimum without open PR → drafts purchase requests (existing draft API) with reason text; planning approves as today.
- `PROC-COMPARE`: on quotation set complete → draft comparison memo (technical/commercial notes) for the committee; the selection rule stays deterministic in the procurement service.
- `QC-NCR-ASSIST`: on new NCR → suggested classification (Minor/Major) and root-cause method + similar past NCRs (retrieval); QC accepts/edits.
- `FIN-ANOMALY` daily: unusual vouchers (duplicate amounts/parties, round amounts, weekend postings, vendor IBAN changes), bank lines unmatched > 7 days → finance dashboard notes.
- `OFFICE-DRAFT` (after office automation exists): letter reply drafts and minutes summaries.
- Each seat run is a row `AiSeatRun` (status, started, finished, outputs, errors); failures alert PMO.

## 6. CEO voice dashboard (front)
- The existing CEO dashboard stays the main screen (deterministic). Add: assistant panel (text box + push-to-talk button), daily briefing player, answers with citation chips that open the record, and "show me" navigation commands (the model can return a front-end route to open).
- Voice approvals: not in A1. The CEO can ask the assistant to open the approval screen; the approval itself is a click.
- Persian speech: numbers spoken in Persian words by the TTS normaliser (implement a Persian number-to-words helper for rial amounts with "میلیارد/میلیون" scaling ★).

## 7. Claude gateway (internet-dependent, engineering only)
- Separate component `ClaudeGateway` with an outbox: requests from engineering seats are queued; when internet is down they wait (status visible); never used by critical seats. API key stored only in server configuration (never in the database or logs). Detailed seat design waits for the engineering module (the user's Claude team will define it).

## 8. Evaluation and acceptance
- `AiEvalCase` set (Persian questions + expected tools + expected facts) seeded with ≥ 60 cases ★ derived from the smoke-test dataset; endpoint runs the set against the configured model and reports pass rate, numeric-check failures, latency p50/p95. Acceptance for go-live ★: ≥ 90 % pass, 0 unverified numbers shown, p95 latency ≤ 8 s text / ≤ 12 s voice.

## 9. Implementation for release A1 (code)
- Entities: `AiModelEndpoint` (name, kind {Chat, Embeddings, Stt, Tts}, url, model name, priority, enabled, timeout, last health), `AiPromptVersion`, `AiConversation`, `AiMessage`, `AiToolCallLog`, `AiAuditEvent` (append-only), `AiSeat`, `AiSeatRun`, `AiDocumentChunk` (source type/id/revision, text, embedding bytes, model), `AiEvalCase`, `AiEvalRun`.
- Services: `ILlmClient` (OpenAI chat-completions with tools; JSON tool-call parsing; failover by priority), `IToolRegistry`, `IAnswerVerifier` (numeric check), `IAiChatService`, `ISeatScheduler` (hosted service), `ISpeechToText`/`ITextToSpeech` HTTP adapters, `IRetrievalService`, `IPersianNumberWords`.
- Admin endpoints (role PMO ★ + CEO read): endpoints health, seats enable/disable/run now, prompt versions (new version, activate), eval run, audit query.
- CI: a **mock LLM** (small ASP.NET minimal app or Python script started in the smoke job) implementing `/v1/chat/completions` with deterministic scripted behaviour (keyword → tool call → answer that quotes tool numbers; one script that invents a number to prove the verifier blocks it), `/v1/embeddings` (hash-based vectors), STT mock (returns fixed transcript) and TTS mock (returns a tiny WAV). Smoke tests: chat answer uses the CEO dashboard tool and cites; unverified number replaced; tool respects roles (warehouse user cannot get finance tool results → tool returns forbidden and the answer says so); failover to the fallback endpoint when primary is down; WH-REORDER seat run creates draft PRs only (status Draft) and is idempotent on a second run; audit rows written; voice endpoint round-trip with mocks; eval run reports pass rate on mock.
- Migration name `LocalAi`.
