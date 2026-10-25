# هوش مصنوعی محلی (انتشار A1): زیربنای فرانت

لایهٔ فرانت «هوش مصنوعی محلی». بک‌اند: `api/v1/ai` (`src/WebApi/Controllers/Ai/AiControllers.cs`)، DTOها در `src/ViewModels/NasbNiro/AiDtos/AiDtos.cs`، نقش‌ها در `AiRoles` (`src/Services/NasbNiro/Ai/AiCommon.cs`)، قواعد دیدن یادداشت‌ها در `AiNotes.cs` و صندلی‌ها در `AiSeats.cs`؛ طراحی: `docs/ai-backend-brief.md` بند ۶. در موارد اختلاف، کنترلرها، DTOها و سرویس‌ها مرجع صحیح هستند. شناسه‌ها و کدها به همان صورت انگلیسی مانده‌اند.

اصل ثابت: **هوش مصنوعی هیچ‌چیز را تایید نمی‌کند.** گیت‌ها و maker-checker در سرور هستند. «پذیرش» یک یادداشت فقط خود یادداشت را می‌بندد و رکوردِ مربوط را تغییر نمی‌دهد؛ تغییر رکورد را یک انسان در ماژول خودش و پشت گیت همان ماژول انجام می‌دهد. هیچ‌جای رابط کاربری دکمهٔ «تایید» از طرف دستیار ندارد. داده‌های کسب‌وکار در `localStorage` نگه داشته نمی‌شود.

## فایل‌ها
- `src/api/AiAPIs.ts`: برای هر endpoint یک تابع (۲۶ endpoint). قرارداد مانند `ServiceAPIs.ts`: هر تابع `[data, null]` یا `[null, error]` برمی‌گرداند، داده camelize شده است، فهرست‌ها صفحه‌بندی‌شده‌اند `{ data, totalItems }` (با `rowsOf()` / `usePagedList()`)، و هر تابع doc comment دارد. نقض قواعد، HTTP 200 با `success=false` است و پیام فارسی سرور به‌عنوان خطا برمی‌گردد. پارامترهای query به‌صورت PascalCase هستند. آرایهٔ ساده (بدون صفحه‌بندی): `admin/endpoints`، `admin/prompts`، `admin/seats`، `admin/eval/cases`.
- `src/composables/ai.ts`: برچسب فارسی enumها (`endpointKinds`، `noteStatuses`، `runStatuses`)، نوع رکوردِ یادداشت (`noteTypes`) و نقش‌های خواننده‌ی هر نوع (`noteTypeRoles`، `readableNoteTypes()`؛ همتای `AiNoteTypes.Roles` بک‌اند برای محدود کردن فهرست نوع)، قواعد FIN-ANOMALY (`ruleTitles`)، عنوان صندلی‌ها (`seatTitles`)، نوع رویداد ممیزی (`auditKinds`)، `frontLink(router, link)` (پیوندهای بک‌اند اکنون مسیر واقعی فرانت‌اند: `/inventory/ncr/{id}`، `/fn/vouchers/{id}`، `/fn/treasury/statements`، `/procurement/inquiries/{id}`، `/office/letters/{id}`، `/office/meetings/{id}`؛ این تابع فقط با `router.resolve` بررسی می‌کند که صفحه هست، وگرنه `null`)، `startPolling()` (خواندن مکرر هر ۲ ثانیه بدون درخواست هم‌پوشان؛ خطای خواندن تا ۳ بار پیاپی تکرار می‌شود، سپس `onGiveUp` صدا زده و خطا با `fail()` نشان داده می‌شود)، `isSeatDraft()` (بر پایهٔ `createdBySeat` پاسخ سرور) و متن‌های ثابت «راستی‌آزمایی‌نشده» و «حالت کاهش‌یافته».
- `src/composables/roles.ts`: `AiRoles` (همتای `AiRoles` بک‌اند): `readers` = PMO و CEO، `admins` = PMO، `brief` = CEO، `evalAnswers` = CEO، `financeNotes` = FINANCEMANAGER و FINANCIAL.
- `src/components/ai/`: `AssistantPanel.vue`، `BriefCard.vue`، `AiNotesBox.vue`، `AiNotesList.vue`، `AiAdminTabs.vue`.
- `src/views/ai/`: `Assistant.vue`، `Notes.vue`، `AdminEndpoints.vue`، `AdminPrompts.vue`، `AdminSeats.vue`، `AdminEval.vue`، `AdminAudit.vue`. صفحهٔ مالی: `src/views/fn/AiAnomalies.vue`.

## مسیرها و نقش‌ها
| مسیر | نام مسیر | صفحه | نقش |
|---|---|---|---|
| `/ai/assistant` | `AiAssistant` | دستیار هوشمند | همهٔ کاربران واردشده |
| `/ai/notes` | `AiNotes` | یادداشت‌های هوش مصنوعی | همهٔ کاربران (سرور فقط یادداشت‌های مجاز را می‌دهد) |
| `/ai/admin` | `AiAdmin` | تغییر مسیر به سرورها | PMO، CEO |
| `/ai/admin/endpoints` | `AiAdminEndpoints` | سرورهای هوش مصنوعی + بازسازی نمایه اسناد | خواندن PMO و CEO؛ نوشتن PMO |
| `/ai/admin/prompts` | `AiAdminPrompts` | نسخه‌های دستور سیستم | خواندن PMO و CEO؛ نوشتن PMO |
| `/ai/admin/seats` | `AiAdminSeats` | دستیارهای زمان‌بندی‌شده و تاریخچهٔ اجراها | خواندن PMO و CEO؛ روشن/خاموش و اجرا PMO |
| `/ai/admin/eval` | `AiAdminEval` | ارزیابی | خواندن PMO و CEO؛ شروع PMO؛ پاسخ‌ها فقط CEO |
| `/ai/admin/audit` | `AiAdminAudit` | ممیزی | PMO، CEO |
| `/ai/seats` | (تغییر مسیر) | پیوندی که بک‌اند در هشدار اجرای ناموفق می‌نویسد | PMO، CEO |
| `/fn/ai-anomalies` | `FnAiAnomalies` | یادداشت‌های ناهنجاری مالی | FINANCEMANAGER، FINANCIAL |

نوار کناری: بخش «هوش مصنوعی» (پیشوند نام مسیر `Ai`) با «دستیار هوشمند»، «یادداشت‌های هوش مصنوعی» (همه) و «مدیریت هوش مصنوعی» (فقط PMO و CEO؛ برای بقیه پنهان می‌شود). «یادداشت‌های ناهنجاری مالی» در بخش حسابداری فقط برای دو نقش مالی دیده می‌شود. عنوان بخش فقط وقتی یکی از فرزندان دیدنی باشد نمایش داده می‌شود.

## جدول نگاشت API
| تابع | Endpoint | نقش |
|---|---|---|
| `aiChat({conversationId?, text})` | POST ai/chat | همه |
| `aiVoice(file, fileName, conversationId?)` (multipart: فیلدهای `file` و `conversationId`) | POST ai/voice | همه |
| `getAiConversations(params)` / `getAiConversation(id)` | GET ai/conversations، ai/conversations/{id} | همه (فقط مالک) |
| `getAiNotes(params)` `{TargetType, TargetId, Status, Seat, Rule}` | GET ai/notes | همه (فیلتر سرور) |
| `getAiNote(id)` | GET ai/notes/{id} | همه |
| `decideAiNote({id, decision: accept یا dismiss, note?})` | POST ai/notes/decide | مردم همان ماژول |
| `getAiBrief()` | GET ai/brief/latest | CEO |
| `getAiBriefAudio()` (blob با هدر Authorization) | GET ai/brief/latest/audio | CEO |
| `getAiEndpoints()` / `saveAiEndpoint(dto)` / `checkAiEndpoints()` | GET/POST ai/admin/endpoints، POST ai/admin/endpoints/health-check | خواندن PMO و CEO؛ نوشتن PMO |
| `getAiPrompts(code?)` / `newAiPrompt({code, text})` / `activateAiPrompt(id)` | GET/POST ai/admin/prompts، POST ai/admin/prompts/activate | خواندن PMO و CEO؛ نوشتن PMO |
| `getAiAudit(params)` `{Kind, SeatCode, UserId, From, To}` | GET ai/admin/audit | PMO، CEO |
| `rebuildAiIndex()` | POST ai/index/rebuild | PMO |
| `getAiSeats()` / `enableAiSeat(code, enabled)` | GET ai/admin/seats، POST ai/admin/seats/enable | خواندن PMO و CEO؛ نوشتن PMO |
| `runAiSeat(code, asOf?)` / `getAiSeatRun(id)` / `getAiSeatRuns(params)` `{Seat, Status}` | POST ai/admin/seats/run، GET ai/admin/seats/runs/{id}، GET ai/admin/seats/runs | اجرا PMO؛ خواندن PMO و CEO |
| `getAiEvalCases()` / `runAiEval()` / `getAiEvalRuns(params)` / `getAiEvalRun(id)` | GET ai/admin/eval/cases، POST ai/admin/eval/run، GET ai/admin/eval/runs، GET ai/admin/eval/runs/{id} | شروع PMO؛ خواندن PMO و CEO |

## رفتار صفحه‌ها
**پنل دستیار (`AssistantPanel.vue`)** روی داشبورد مدیرعامل (کارت کنار/بالای داشبورد قطعی؛ داشبورد قطعی همچنان محتوای اصلی است و اگر هوش مصنوعی از کار بیفتد بدون تغییر کار می‌کند) و صفحهٔ «دستیار هوشمند». کادر متن (حداکثر ۲۰۰۰ نویسه، Enter ارسال، Shift+Enter خط تازه) و دکمهٔ میکروفون. در پاسخ: متن، نشان «راستی‌آزمایی‌شده»، وقتی `verified=false` متن «پاسخ مدل قابل راستی‌آزمایی نبود؛ نتایج مستقیم سامانه نمایش داده شد» با رنگ هشدار، نشان و هشدار راستی‌آزمایی مستقل از `degraded` نمایش داده می‌شوند (اگر پاسخ از قبل با همان جمله شروع شود، جمله تکرار نمی‌شود و فقط رنگ هشدار می‌ماند)؛ اعلان «تحلیل هوشمند موقتاً در دسترس نیست» فقط وقتی `degraded` است و نه ابزاری فراخوانی شده نه مدلی گزارش شده (کاهش‌یافتگی واقعی)؛ اگر مدل یا ابزار بوده و فقط `degraded` است (خرابی گفتار)، یادداشت سبک «پاسخ صوتی در دسترس نبود»؛ نام مدل (ریز)، چیپ‌های ارجاع (با `router.push` به پیوند، پس از نگاشت `frontLink`)، فهرست ابزارها (بسته؛ `allowed=false` با «بدون دسترسی») و پخش صدای برگشتی (`audioBase64` به‌صورت WAV در عنصر `<audio>`). `conversationId` نگه داشته می‌شود تا گفت‌وگو ادامه یابد؛ «گفت‌وگوی تازه» آن را پاک می‌کند؛ «تاریخچه» کشوی گفت‌وگوها را از `GET conversations` می‌خواند. دکمهٔ ارسال و میکروفون هنگام انتظار غیرفعال‌اند و چرخان نمایش داده می‌شود؛ خطاها با `fail()`. «تاریخچه» و «گفت‌وگوی تازه» هنگام ارسال یا ضبط غیرفعال‌اند.
میکروفون (push-to-talk): نگه‌داشتن (بیش از حدود نیم ثانیه، رها کردن = ارسال) یا کلیک برای شروع و کلیک برای پایان (یا Enter/Space). `getUserMedia` فقط هنگام فشردن دکمه فراخوانی می‌شود و بلافاصله پس از پایان ضبط، همهٔ trackها بسته می‌شوند (هیچ میکروفون همیشه‌روشن نیست)؛ سقف ضبط ۱۲۰ ثانیه؛ `MediaRecorder` با `audio/webm` (در صورت نبود پشتیبانی، پیش‌فرض مرورگر)؛ ضبط خیلی کوتاه رد می‌شود. میکروفون نیاز به HTTPS (یا localhost) دارد. قالب ضبط به‌ترتیب `audio/webm;codecs=opus`، `audio/webm`، `audio/ogg;codecs=opus` با `MediaRecorder.isTypeSupported` انتخاب می‌شود (پسوند فایل از نوع)؛ اگر هیچ‌کدام نبود یا اتصال امن نباشد، دکمهٔ میکروفون غیرفعال با راهنما (مثلاً «گفت‌وگوی صوتی نیاز به اتصال امن (HTTPS) دارد؛ گفت‌وگوی متنی در دسترس است») نمایش داده می‌شود و متن راهنمای خالی‌بودن فقط به نوشتن اشاره می‌کند. اگر پنل هنگام پرسش مجوز میکروفون بسته شود، ضبط شروع نمی‌شود و هیچ صدایی ارسال نمی‌شود.

**گزارش روزانه (`BriefCard.vue`)** فقط برای CEO روی داشبورد مدیرعامل: تاریخ (`dateText`)، متن، نشان راستی‌آزمایی، «پخش گزارش صوتی» (blob با توکن، سپس `<audio>`). اگر گزارشی نباشد یا هوش مصنوعی در دسترس نباشد، پیام سرور (مثلاً «هنوز گزارش روزانه‌ای ساخته نشده است.») در خود کارت نمایش داده می‌شود نه به‌صورت خطای صفحه. برای گزارش راستی‌آزمایی‌نشده، همان هشدار یک‌خطی کنار نشان نمایش داده می‌شود.

**جعبهٔ یادداشت (`AiNotesBox.vue`)** props: `targetType`، `targetId`، `ruleCode`. یادداشت‌های همان رکورد را می‌آورد (عنوان صندلی، زمان فارسی `createdAtText`، متن با حفظ خط‌های جدید، نشان راستی‌آزمایی، وضعیت جدید/پذیرفته/ردشده). دکمه‌های «پذیرش» و «رد» (با توضیح اختیاری) فقط برای یادداشت جدید؛ تصمیم را سرور می‌گیرد و اگر نپذیرد، پیام سرور نمایش و دکمه‌های همان یادداشت پنهان می‌شود. اگر سرور خواندن را نپذیرد یا یادداشتی نباشد، جعبه اصلاً نمایش داده نمی‌شود. خواندن ۵۰ یادداشت در هر صفحه با دکمهٔ «بیشتر»؛ پاسخ دیررسیدهٔ درخواست قدیمی دور ریخته می‌شود؛ پس از خطای تصمیم، فهرست دوباره خوانده می‌شود. توضیح تصمیم حداکثر ۱۰۰۰ نویسه (`maxlength` در `PromptModal`).

محل جعبه‌ها:
| targetType | صفحه |
|---|---|
| `QualityNcr` | `src/views/inventory/NcrEdit.vue` (مسیر `/inventory/ncr/:id`) |
| `PurchaseRfq` | `src/views/procurement/InquiryEdit.vue` (استعلام و جدول مقایسهٔ پیشنهادها، `/procurement/inquiries/:id`؛ شناسهٔ یادداشت = شناسهٔ استعلام) |
| `FinanceJournal` | `src/views/fn/VoucherEdit.vue` (`/fn/vouchers/:id`) |
| `OfficeLetter` | `src/views/office/LetterEdit.vue` (`/office/letters/:id`؛ برای نامهٔ محرمانه‌ای که فقط ابرداده نشان داده می‌شود جعبه نمایش داده نمی‌شود) |
| `OfficeMeeting` | `src/views/office/MeetingEdit.vue` (`/office/meetings/:id`) |
| `PcWeeklyReport` | `src/views/projectControl/Reports.vue` (پنجرهٔ جزئیات گزارش نوع «هفتگی داخلی»، فقط PMO و PLANNING) |
| `FinanceJournal`، `BankLine`، `SupplierIbanChange` | فهرست `src/views/fn/AiAnomalies.vue` با فیلتر نوع، وضعیت و قاعده (DUPLICATE، ROUND، OFFDAY، UNMATCHED، IBAN)؛ بدون انتخاب نوع، انواع ثابت به‌صورت فهرست جداشده با ویرگول در یک `TargetType` فرستاده می‌شوند و صفحه‌بندی در سرور انجام می‌شود |
| همهٔ انواعِ مجاز | فهرست `/ai/notes` با فیلتر نوع (فقط انواعی که نقش شما می‌خواند)، وضعیت و صندلی |

**پیش‌نویس‌های دستیار انبار (WH-REORDER):** درخواست خرید واقعی است و سازندهٔ آن کاربر سرویس صندلی است. در `Requests.vue` و `RequestEdit.vue` نشان «پیش‌نویس دستیار انبار» وقتی `createdBySeat` در پاسخ سرور درست باشد نمایش داده می‌شود (نام سازنده فقط جایگزین پاسخ‌های قدیمی است).

**مدیریت (PMO بنویسد، CEO بخواند):**
- سرورها: جدول نام، نوع (گفت‌وگو/بردارسازی/گفتار به متن/متن به گفتار)، نشانی، مدل، اولویت، فعال، مهلت، آخرین سلامت (زمان فارسی، سالم یا خطا)، پنجرهٔ ویرایش، «بررسی سلامت»، و کارت «بازسازی نمایه اسناد» با شمارش نتیجه.
- نسخه‌های دستور سیستم: به تفکیک `code`؛ مشاهدهٔ متن، ساخت نسخهٔ جدید (غیرفعال)، «ساخت از روی این نسخه»، فعال‌سازی با پنجرهٔ تایید.
- دستیارها: کد، عنوان فارسی، نقش‌ها، زمان‌بندی فارسی، کلید روشن/خاموش، آخرین اجرا، «اجرای همین حالا» با روز داده (اختیاری، `FnDate`)؛ اجرای برگشتی با وضعیت «در حال اجرا» هر ۲ ثانیه با `GET admin/seats/runs/{id}` خوانده می‌شود تا تمام شود، سپس خروجی/خطا نمایش داده می‌شود؛ تاریخچهٔ اجراها با فیلتر دستیار و وضعیت.
- ارزیابی: «شروع ارزیابی» ← بازخوانی هر ۲ ثانیه تا `running=false`؛ جدول اجراها (کل، قبول، درصد، شکست عددی، p50، p95، مدل)؛ جزئیات هر مورد. پاسخ‌ها و دلیل واقعیت‌ها فقط برای CEO می‌آید (سرور برای PMO `null` می‌دهد و رابط شمارش‌ها را نشان می‌دهد). رنگ‌ها بر پایهٔ معیار پذیرش طراحی: دست‌کم ۹۰٪ قبولی، صفر شکست عددی، p95 تا ۸ ثانیه.
- ممیزی: فیلتر نوع رویداد (با برچسب فارسی)، دستیار، شناسهٔ کاربر، از/تا (تاریخ شمسی)؛ صفحه‌بندی؛ ردیف‌ها فقط‌خواندنی‌اند.

## نکته‌ها و کمبودهای بک‌اند برای رابط
- ممیزی `userName` و نسخهٔ دستور سیستم `createdAtText` و `activatedAtText` را از سرور می‌گیرد و صفحه‌ها همان‌ها را نشان می‌دهند. پیوندهای دستیار به `/project-control/changes?id=` و `/project-control/guarantees?id=` در صفحه‌های مربوط خوانده می‌شود (درخواست تغییر باز می‌شود، ردیف ضمانت‌نامه علامت می‌خورد).
- توضیح کنترلر برای `brief/latest` می‌گوید «CEO و PMO» ولی سرویس فقط CEO را می‌پذیرد؛ رابط فقط CEO را نشان می‌دهد.
- ورودی `To` ممیزی تمام آن روز را شامل می‌شود (سرور یک روز اضافه می‌کند).
