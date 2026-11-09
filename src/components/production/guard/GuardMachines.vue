<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import { getGuardMachines, runGuardPoll, saveGuardMachine, testGuardMachine } from "@/api/GuardAPIs";
import { fail, ok } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { GuardRoles, dataLinks, execModes, programStates } from "@/composables/guard";

// The machines of the shop as the control reports them: mode, program state, overrides, program and tool; the health of the data link; the start interlock. Refreshes every 15 s
// while open (one request at a time, none while the tab is hidden). The settings of a link are edited here (production manager, PMO); maintenance tests what the control answers.
const emit = defineEmits<{ (e: "timeline", machineId: number): void }>();
const rows = ref<any[]>([]);
const loaded = ref(false);
const busy = ref(false);
const edit = ref<any>(null);
const test = ref<null | { title: string; ok: boolean; message: string; lines: string[] }>(null);
const canEdit = hasRole(GuardRoles.editors);
const canTest = hasRole(GuardRoles.testers);
const canPoll = hasRole(GuardRoles.supervisors);
let timer: any = null;
let loading = false;

async function load() {
  if (loading || document.hidden) return;
  loading = true;
  const [d, error] = await getGuardMachines().finally(() => (loading = false));
  loaded.value = true;
  if (error) return fail(error);
  rows.value = Array.isArray(d) ? d : [];
}
onMounted(() => {
  load();
  timer = setInterval(load, 15000);
});
onBeforeUnmount(() => clearInterval(timer));

const guarded = computed(() => rows.value.filter((m) => m.guarded));
const others = computed(() => rows.value.filter((m) => !m.guarded));
const lamp = (m: any) => (m.programState === 5 ? "red" : m.programState === 1 ? "green" : m.linkHealthy ? "amber" : "dark");
const feedWidth = (v: number | null) => Math.min(100, Math.max(0, Number(v ?? 0) / 1.5)) + "%";

function openEdit(m: any) {
  edit.value = {
    title: m.machineTitle, machineId: m.machineId, dataLink: m.dataLink ?? 0, dataHost: m.dataHost || "", dataPort: m.dataPort ?? 19000, dataCredentialKey: m.dataCredentialKey || "",
    plcMap: m.plcMap || "", programFolder: m.programFolder || "TNC:\\", resultFile: m.resultFile || "", interlockEnabled: !!m.interlockEnabled, interlockHost: m.interlockHost || "",
    interlockPort: m.interlockPort ?? 502, interlockUnitId: m.interlockUnitId ?? 1, interlockCoil: m.interlockCoil ?? 0, interlockInvert: !!m.interlockInvert,
  };
}
const blank = (s: string) => (s && s.trim() ? s.trim() : null);
async function save() {
  const e = edit.value;
  busy.value = true;
  const [, error] = await saveGuardMachine({
    machineId: e.machineId, dataLink: Number(e.dataLink), dataHost: blank(e.dataHost), dataPort: e.dataLink === 1 ? Number(e.dataPort) || 19000 : null, dataCredentialKey: blank(e.dataCredentialKey),
    plcMap: blank(e.plcMap), programFolder: blank(e.programFolder), resultFile: blank(e.resultFile), interlockEnabled: e.interlockEnabled, interlockHost: blank(e.interlockHost),
    interlockPort: Number(e.interlockPort) || 502, interlockUnitId: Number(e.interlockUnitId) || 0, interlockCoil: Number(e.interlockCoil) || 0, interlockInvert: e.interlockInvert,
  });
  busy.value = false;
  if (error) return fail(error);
  ok("تنظیمات اتصال ماشین ذخیره شد.");
  edit.value = null;
  load();
}
async function runTest(m: any) {
  busy.value = true;
  const [d, error] = await testGuardMachine(m.machineId);
  busy.value = false;
  if (error) return fail(error);
  test.value = { title: m.machineTitle, ok: d.ok, message: d.message, lines: d.lines || [] };
  load();
}
async function poll() {
  busy.value = true;
  const [d, error] = await runGuardPoll();
  busy.value = false;
  if (error) return fail(error);
  ok(`${d.polled} ماشین پرسیده شد؛ ${d.failed} بی‌پاسخ، ${d.activities} رویداد تازه.`);
  load();
}
</script>

<template>
  <div>
    <div class="d-flex justify-content-between align-items-center mb-2">
      <p class="gd-muted m-0">به‌روزرسانی خودکار هر ۱۵ ثانیه</p>
      <button v-if="canPoll" :disabled="busy" class="gd-btn" @click="poll">پرسش همین حالا از کنترلرها</button>
    </div>
    <div v-if="!loaded" class="gd-empty">در حال دریافت…</div>
    <div v-else-if="!guarded.length" class="gd-panel gd-empty">
      <p class="mb-1">هنوز هیچ ماشینی به کنترلر خود وصل نشده است.</p>
      <p class="mb-0">برای ماشین‌های هایدن‌هاین اتصال LSV2 را تعریف کنید؛ از آن پس برنامه، ابزار و حالت کار از خود کنترلر خوانده می‌شود.</p>
    </div>
    <ul v-else class="gm-list">
      <li v-for="m in guarded" :key="m.machineId" :class="{ alarm: m.programState === 5 }" class="gm-card">
        <div class="gm-top">
          <div class="tower" aria-hidden="true">
            <span :class="{ on: lamp(m) === 'red' }" class="lamp red"></span>
            <span :class="{ on: lamp(m) === 'amber' }" class="lamp amber"></span>
            <span :class="{ on: lamp(m) === 'green' }" class="lamp green"></span>
          </div>
          <div class="gm-name">
            <b>{{ m.machineTitle }}</b>
            <div class="gd-muted">{{ m.workCenterCode || "" }} · {{ dataLinks[m.dataLink] }}</div>
          </div>
          <span :class="m.linkHealthy ? 'ok' : 'bad'" class="gd-chip" :title="m.dataError || ''">{{ m.linkHealthy ? "ارتباط برقرار" : "ارتباط قطع" }}</span>
        </div>

        <dl class="gm-facts">
          <div><dt>حالت کار</dt><dd>{{ execModes[m.execMode] }}</dd></div>
          <div><dt>برنامه</dt><dd :class="{ bad: m.programState === 5 }">{{ programStates[m.programState] }}</dd></div>
          <div class="wide"><dt>برنامه انتخاب‌شده</dt><dd class="gd-ltr">{{ m.selectedProgram || "—" }}<span v-if="m.blockNumber" class="gd-muted"> · N{{ m.blockNumber }}</span></dd></div>
          <div><dt>ابزار</dt><dd class="gd-ltr">{{ m.toolNumber !== null && m.toolNumber !== undefined ? "T" + m.toolNumber : "—" }}</dd></div>
          <div><dt>اسپیندل</dt><dd>{{ m.spindleOverride ?? "—" }}٪</dd></div>
        </dl>
        <div class="gm-feed" :title="'Override پیشروی ' + (m.feedOverride ?? '—') + '٪'">
          <span class="gd-muted">پیشروی</span>
          <span class="gm-bar"><span :class="{ low: (m.feedOverride ?? 100) <= 50 }" :style="{ width: feedWidth(m.feedOverride) }"></span><i style="inset-inline-start: 66.6%"></i></span>
          <b>{{ m.feedOverride ?? "—" }}٪</b>
        </div>

        <div v-if="m.interlockEnabled" :class="m.startEnabled ? 'free' : 'locked'" class="gm-lock">
          <span class="gm-key" aria-hidden="true">{{ m.startEnabled ? "◉" : "⦸" }}</span>
          <div>
            <b>{{ m.startEnabled ? "استارت آزاد" : "استارت قفل" }}</b>
            <div class="small">{{ m.interlockError ? "فرمان به رله نرسید: " + m.interlockError : m.startEnabled ? "کار ثبت‌شده و بررسی‌شده روی ماشین باز است." : m.startBlockReason || "" }}</div>
          </div>
        </div>
        <div v-else class="gm-lock none"><span class="gm-key" aria-hidden="true">○</span><div class="small">قفل استارت نصب نشده؛ فقط ثبت و هشدار.</div></div>

        <div class="gm-foot">
          <span class="gd-muted small">آخرین داده: {{ m.dataSeenAtText || "هنوز" }}</span>
          <span class="gm-actions">
            <button class="gd-btn" @click="emit('timeline', m.machineId)">خط زمانی</button>
            <button v-if="canTest" :disabled="busy" class="gd-btn" @click="runTest(m)">آزمون اتصال</button>
            <button v-if="canEdit" class="gd-btn" @click="openEdit(m)">تنظیمات</button>
          </span>
        </div>
      </li>
    </ul>

    <section v-if="others.length" class="gd-panel mt-3">
      <h2 class="h6 mb-2">ماشین‌های بدون اتصال داده <span class="gd-muted">(فقط ثبت دستی؛ گیت‌های کنترلر روی آن‌ها اجرا نمی‌شود)</span></h2>
      <div class="gm-others">
        <span v-for="m in others" :key="m.machineId" class="gm-other">{{ m.machineTitle }} <button v-if="canEdit" class="gd-btn" @click="openEdit(m)">تعریف اتصال</button></span>
      </div>
    </section>

    <div v-if="edit" class="inv-modal-backdrop" @click.self="edit = null">
      <div class="inv-modal gd" style="max-width: 720px">
        <h3 class="h5 mb-3">اتصال داده و قفل استارت — {{ edit.title }}</h3>
        <div class="row g-2 small">
          <div class="col-md-6"><label class="form-label mb-0">نوع اتصال</label><select v-model.number="edit.dataLink" class="form-select form-select-sm"><option v-for="(t, k) in dataLinks" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <template v-if="edit.dataLink === 1">
            <div class="col-md-4"><label class="form-label mb-0">نشانی IP کنترلر</label><input v-model="edit.dataHost" class="form-control form-control-sm gd-ltr" placeholder="192.168.10.21" /></div>
            <div class="col-md-2"><label class="form-label mb-0">درگاه</label><input v-model="edit.dataPort" class="form-control form-control-sm gd-ltr" /></div>
            <div class="col-md-4"><label class="form-label mb-0">نام کلید رمز PLC</label><input v-model="edit.dataCredentialKey" class="form-control form-control-sm gd-ltr" placeholder="tnc-r1" /></div>
            <div class="col-md-8 gd-muted mt-4">رمز اینجا نوشته نمی‌شود؛ در تنظیمات سرور زیر <span class="gd-ltr">Dnc:Credentials:&lt;نام&gt;:Password</span> نگه‌داری می‌شود.</div>
            <div class="col-12"><label class="form-label mb-0">نشانی‌های PLC (برای کنترلرهای قدیمی TNC 426/430؛ از سازنده ماشین)</label><input v-model="edit.plcMap" class="form-control form-control-sm gd-ltr" placeholder="exec=W100;state=W102;feed=W104;spindle=W106;tool=W108" /></div>
            <div class="col-md-6"><label class="form-label mb-0">پوشه برنامه‌ها روی کنترلر</label><input v-model="edit.programFolder" class="form-control form-control-sm gd-ltr" placeholder="TNC:\nc_prog\" /></div>
            <div class="col-md-6"><label class="form-label mb-0">فایل نتیجه سیکل‌های اندازه‌گیری (اختیاری)</label><input v-model="edit.resultFile" class="form-control form-control-sm gd-ltr" placeholder="TNC:\RESULT.TXT" /></div>
          </template>
          <div v-else-if="edit.dataLink === 2" class="col-12 gd-muted">جعبه لبه کنار ماشین با کلید گیت‌وی همان ماشین (در صفحه DNC) وضعیت، حالت کار، override و اثر انگشت برنامه را می‌فرستد.</div>
        </div>
        <fieldset :disabled="edit.dataLink === 0" class="gm-fieldset mt-3">
          <legend class="h6">قفل استارت (رله روی فعال‌ساز استارت NC)</legend>
          <div class="form-check mb-2"><input id="gm-il" v-model="edit.interlockEnabled" class="form-check-input" type="checkbox" /><label class="form-check-label" for="gm-il">قفل استارت فعال است</label></div>
          <div v-if="edit.interlockEnabled" class="row g-2 small">
            <div class="col-md-4"><label class="form-label mb-0">نشانی ماژول رله (Modbus TCP)</label><input v-model="edit.interlockHost" class="form-control form-control-sm gd-ltr" placeholder="192.168.10.60" /></div>
            <div class="col-md-2"><label class="form-label mb-0">درگاه</label><input v-model="edit.interlockPort" class="form-control form-control-sm gd-ltr" /></div>
            <div class="col-md-2"><label class="form-label mb-0">شناسه واحد</label><input v-model="edit.interlockUnitId" class="form-control form-control-sm gd-ltr" /></div>
            <div class="col-md-2"><label class="form-label mb-0">شماره خروجی</label><input v-model="edit.interlockCoil" class="form-control form-control-sm gd-ltr" /></div>
            <div class="col-md-2 form-check mt-4"><input id="gm-inv" v-model="edit.interlockInvert" class="form-check-input" type="checkbox" /><label class="form-check-label" for="gm-inv">سیم‌کشی معکوس</label></div>
            <div class="col-12 gm-warning">رله را برق‌کار ماشین و با تأیید سازنده فقط در مدار «اجازه استارت NC» قرار می‌دهد، هرگز در مدار توقف اضطراری. قطع رله برنامه در حال اجرا را متوقف نمی‌کند؛ فقط استارت بعدی را می‌بندد. واچ‌داگ ماژول را روی خاموش‌شدن خروجی در قطع ارتباط با سرور تنظیم کنید.</div>
          </div>
        </fieldset>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="gd-btn" @click="edit = null">انصراف</button>
          <button :disabled="busy" class="gd-btn solid" @click="save">ذخیره</button>
        </div>
      </div>
    </div>

    <div v-if="test" class="inv-modal-backdrop" @click.self="test = null">
      <div class="inv-modal gd" style="max-width: 560px">
        <h3 class="h5 mb-2">آزمون اتصال — {{ test.title }}</h3>
        <p :class="test.ok ? 'text-success' : 'text-danger'" class="fw-bold">{{ test.message }}</p>
        <ul class="gm-caps">
          <li v-for="(l, i) in test.lines" :key="i" :class="l.startsWith('✔') ? 'ok' : l.startsWith('✘') ? 'bad' : ''">{{ l }}</li>
        </ul>
        <div class="text-end"><button class="gd-btn" @click="test = null">بستن</button></div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.gm-list { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 14px; }
.gm-card { background: var(--panel); border: 1px solid var(--rule); border-radius: 12px; padding: 14px 16px; display: flex; flex-direction: column; gap: 10px; }
.gm-card.alarm { border-color: var(--red); }
.gm-top { display: grid; grid-template-columns: 22px 1fr auto; gap: 10px; align-items: start; }
.gm-name b { font-size: 1.02rem; }
.tower { display: flex; flex-direction: column; gap: 3px; padding-top: 2px; }
.lamp { width: 16px; height: 13px; border-radius: 3px; background: var(--dark-lamp); opacity: 0.55; }
.lamp.on { opacity: 1; }
.lamp.red.on { background: var(--red); box-shadow: 0 0 9px 1px rgba(210, 64, 53, 0.55); }
.lamp.amber.on { background: var(--amber); box-shadow: 0 0 9px 1px rgba(217, 154, 30, 0.5); }
.lamp.green.on { background: var(--green); box-shadow: 0 0 9px 1px rgba(46, 158, 87, 0.5); }
.gm-facts { display: grid; grid-template-columns: repeat(3, 1fr); gap: 6px 10px; margin: 0; }
.gm-facts .wide { grid-column: span 3; }
.gm-facts dt { font-size: 0.74rem; color: var(--muted); font-weight: 500; }
.gm-facts dd { margin: 0; font-weight: 700; }
.gm-facts dd.bad { color: var(--red); }
.gm-feed { display: grid; grid-template-columns: auto 1fr auto; gap: 8px; align-items: center; font-size: 0.85rem; }
.gm-bar { position: relative; height: 8px; border-radius: 4px; background: var(--page); overflow: hidden; }
.gm-bar span { position: absolute; inset-block: 0; inset-inline-start: 0; background: var(--navy); border-radius: 4px; }
.gm-bar span.low { background: var(--green); }
.gm-bar i { position: absolute; top: -2px; bottom: -2px; width: 2px; background: var(--muted); }
.gm-lock { display: grid; grid-template-columns: 30px 1fr; gap: 8px; align-items: center; border-radius: 10px; padding: 8px 10px; background: var(--page); }
.gm-lock.free { box-shadow: inset 4px 0 0 var(--green); }
.gm-lock.locked { box-shadow: inset 4px 0 0 var(--red); }
.gm-lock.none { color: var(--muted); }
.gm-key { font-size: 1.4rem; line-height: 1; text-align: center; }
.gm-lock.free .gm-key { color: var(--green); }
.gm-lock.locked .gm-key { color: var(--red); }
.gm-foot { display: flex; justify-content: space-between; align-items: center; gap: 8px; flex-wrap: wrap; border-top: 1px solid var(--rule); padding-top: 8px; }
.gm-actions { display: flex; gap: 6px; flex-wrap: wrap; }
.gm-others { display: flex; flex-wrap: wrap; gap: 8px; }
.gm-other { display: inline-flex; align-items: center; gap: 6px; background: var(--page); border-radius: 8px; padding: 4px 10px; }
.gm-fieldset { border: 1px solid var(--rule); border-radius: 10px; padding: 10px 14px; }
.gm-fieldset legend { float: none; width: auto; padding: 0 6px; margin: 0; font-size: 0.92rem; }
.gm-warning { background: #fff6e0; color: #6b4a00; border-radius: 8px; padding: 8px 10px; font-size: 0.82rem; }
.gm-caps { list-style: none; padding: 0; font-size: 0.88rem; }
.gm-caps li { padding: 3px 0; }
.gm-caps li.ok { color: var(--green); }
.gm-caps li.bad { color: var(--red); }
@media (prefers-reduced-motion: no-preference) {
  .lamp.red.on { animation: blink 1.2s steps(2, jump-none) infinite; }
}
@keyframes blink { 50% { opacity: 0.45; box-shadow: none; } }
</style>
