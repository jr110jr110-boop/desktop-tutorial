<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import {
  downloadRevision, fileToBase64, getProgram, getRevisionDiff, getRuns, getTransfers, receiveProgram, revisionAction, saveProgram, sendProgram, uploadRevision, validateRevision,
} from "@/api/DncAPIs";
import { rowsOf } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { bookingKinds } from "@/composables/production";
import {
  DncRoles, badgeOfRevision, controllerFamilies, diffClass, programStatuses, revisionActionTitles, revisionSources, revisionStatuses, sizeText,
} from "@/composables/dnc";
import { hasRole } from "@/composables/roles";
import TransfersTable from "./TransfersTable.vue";

// One CNC program: its revisions (the author never approves; the check runs against every machine the program may go to), the upload of a new revision, the difference to the revision before,
// the check per machine, the send to a machine and the fetch back from a machine, the transfers and the record of what ran.
const props = defineProps<{ id: number }>();
const emit = defineEmits(["back"]);
const p = ref<any>(null);
const busy = ref(false);
const openFindings = ref<number | null>(null);
const upload = ref<null | { file: File | null; note: string }>(null);
const diff = ref<any>(null);
const checks = ref<any[] | null>(null);
const sendTo = ref<null | { machineId: number | null; targets: any[] }>(null);
const receive = ref<null | { machineId: number | null; programName: string; note: string; targets: any[] }>(null);
const transfers = ref<any[]>([]);
const runs = ref<any[]>([]);
const canWrite = hasRole(DncRoles.writers);
const canSend = hasRole(DncRoles.senders) || hasRole(DncRoles.supervisors);
const canReceive = hasRole(DncRoles.receivers);
const current = computed(() => p.value?.revisions?.find((r: any) => r.id === p.value.currentRevisionId) ?? null);
const open = computed(() => p.value?.revisions?.find((r: any) => r.status === 0 || r.status === 1) ?? null);

async function load() {
  const [d, error] = await getProgram(props.id);
  if (error) return fail(error);
  p.value = d;
  const [t] = await getTransfers({ ProgramId: props.id, PageSize: 20 });
  transfers.value = rowsOf(camelize(t));
  const [r] = await getRuns({ ProgramId: props.id, PageSize: 20 });
  runs.value = rowsOf(camelize(r));
}
watch(() => props.id, load);
onMounted(load);

async function doUpload() {
  const u = upload.value!;
  if (!u.file) return fail("فایل برنامه را انتخاب کنید.");
  if (u.file.size > 4 * 1024 * 1024) return fail("حجم فایل بیش از ۴ مگابایت است.");
  busy.value = true;
  const [d, error] = await uploadRevision({ programId: props.id, fileName: u.file.name, contentBase64: await fileToBase64(u.file), changeNote: u.note.trim() || null });
  busy.value = false;
  if (error) return fail(error);
  upload.value = null;
  d.errorCount ? fail(`نسخه ${d.revision} ثبت شد ولی ${d.errorCount} خطای بررسی دارد؛ پیش از ارسال برای تأیید باید اصلاح شود.`) : ok(`نسخه ${d.revision} ثبت شد.`);
  openFindings.value = d.errorCount || d.warningCount ? d.id : null;
  load();
}

async function act(r: any, action: string) {
  let reason: string | null = null;
  if (action === "reject") {
    reason = prompt("دلیل رد نسخه:");
    if (!reason) return;
  }
  if (action === "withdraw" && !confirm("این نسخه پس گرفته شود؟")) return;
  busy.value = true;
  const [, error] = await revisionAction({ revisionId: r.id, action, reason, rowVersion: r.rowVersion });
  busy.value = false;
  if (error) return fail(error);
  ok(revisionActionTitles[action] + " ثبت شد.");
  load();
}

async function showDiff(r: any) {
  const [d, error] = await getRevisionDiff(r.id);
  if (error) return fail(error);
  diff.value = { ...d, revision: r.revision };
}
async function check(r: any) {
  const [d, error] = await validateRevision(r.id);
  if (error) return fail(error);
  checks.value = d;
  if (!d.length) fail("هیچ ماشینی با اتصال فعال برای این برنامه تعریف نشده است.");
}
async function download(r: any) {
  const message = await downloadRevision(r.id, r.fileName);
  if (message) fail(message);
}

async function targets(): Promise<any[] | null> {
  if (!current.value) return null;
  const [d, error] = await validateRevision(current.value.id);
  if (error) {
    fail(error);
    return null;
  }
  return d;
}
async function openSend() {
  const t = await targets();
  if (!t) return;
  if (!t.length) return fail("هیچ ماشینی با اتصال فعال برای این برنامه تعریف نشده است.");
  sendTo.value = { machineId: t.length === 1 ? t[0].machineId : null, targets: t };
}
async function doSend() {
  const s = sendTo.value!;
  if (!s.machineId) return fail("ماشین را انتخاب کنید.");
  busy.value = true;
  const [d, error] = await sendProgram({ programId: props.id, machineId: s.machineId });
  busy.value = false;
  if (error) return fail(error);
  sendTo.value = null;
  ok(`برنامه برای ارسال به ماشین ${d.machineTitle} در صف قرار گرفت (نام روی کنترلر: ${d.targetName}).`);
  load();
}
async function openReceive() {
  const t = (await targets()) ?? [];
  receive.value = { machineId: t.length === 1 ? t[0].machineId : null, programName: "", note: "", targets: t };
}
async function doReceive() {
  const r = receive.value!;
  if (!r.machineId) return fail("ماشین را انتخاب کنید.");
  busy.value = true;
  const [, error] = await receiveProgram({ programId: props.id, machineId: r.machineId, programName: r.programName.trim() || null, changeNote: r.note.trim() || null });
  busy.value = false;
  if (error) return fail(error);
  receive.value = null;
  ok("درخواست دریافت ثبت شد؛ پس از دریافت، نسخه پیش‌نویس تازه با تفاوت‌هایش ساخته می‌شود.");
  load();
}
async function setStatus(status: number) {
  if (status === 2 && !confirm("برنامه منسوخ شود؟ دیگر به ماشین ارسال نمی‌شود.")) return;
  busy.value = true;
  const x = p.value;
  const [, error] = await saveProgram({ id: x.id, code: x.code, title: x.title, itemId: x.itemId, operationTemplateId: x.operationTemplateId, workCenterId: x.workCenterId, machineId: x.machineId, controllerFamily: x.controllerFamily, status, note: x.note, rowVersion: x.rowVersion });
  busy.value = false;
  if (error) return fail(error);
  load();
}
</script>

<template>
  <div>
    <div class="inv-toolbar">
      <h2 class="inv-title">برنامه <span class="inv-ltr">{{ p?.code }}</span> {{ p?.title }}</h2>
      <button class="btn btn-sm btn-outline-secondary" @click="emit('back')">بازگشت به فهرست</button>
    </div>
    <p v-if="!p" class="inv-muted">در حال بارگذاری...</p>
    <template v-else>
      <div class="row g-2 mb-3 small">
        <div class="col-md-3"><b>قطعه:</b> {{ p.itemCode ? p.itemCode + " - " + p.itemTitle : "-" }}</div>
        <div class="col-md-3"><b>مرحله:</b> {{ p.operationTitle || "-" }}</div>
        <div class="col-md-2"><b>مرکز کار:</b> <span class="inv-ltr">{{ p.workCenterCode || "همه" }}</span></div>
        <div class="col-md-2"><b>ماشین:</b> {{ p.machineTitle || "هم‌خانواده" }}</div>
        <div class="col-md-2"><b>کنترلر:</b> {{ controllerFamilies[p.controllerFamily] }}</div>
        <div class="col-md-3"><b>نسخه جاری:</b> {{ p.currentRevision ?? "ندارد" }} <span v-if="p.currentApprovedAtText" class="inv-muted">({{ p.currentApprovedAtText }})</span></div>
        <div class="col-md-3"><b>وضعیت:</b> <span :class="p.status === 1 ? 'ok' : ''" class="inv-badge">{{ programStatuses[p.status] }}</span></div>
      </div>
      <div class="d-flex flex-wrap gap-2 mb-3">
        <button v-if="canWrite && p.status === 1 && !open" class="btn btn-sm btn-primary" @click="upload = { file: null, note: '' }">بارگذاری نسخه تازه</button>
        <button v-if="canSend && p.status === 1 && current" :disabled="busy" class="btn btn-sm btn-success" @click="openSend">ارسال نسخه جاری به ماشین</button>
        <button v-if="canReceive && p.status === 1 && !open" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="openReceive">دریافت برنامه از ماشین</button>
        <button v-if="canWrite && p.status === 1" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="setStatus(2)">منسوخ کردن</button>
        <button v-if="canWrite && p.status === 2" :disabled="busy" class="btn btn-sm btn-outline-success" @click="setStatus(1)">فعال کردن دوباره</button>
      </div>

      <h3 class="h6">نسخه‌ها</h3>
      <div class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>نسخه</th><th>وضعیت</th><th>فایل</th><th>منبع</th><th>تفاوت با قبلی</th><th>بررسی</th><th>نویسنده</th><th>تأیید / رد</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!p.revisions?.length"><td colspan="9" class="text-center inv-muted">هنوز نسخه‌ای بارگذاری نشده است.</td></tr>
            <template v-for="r in p.revisions" :key="r.id">
              <tr>
                <td>{{ r.revision }}</td>
                <td><span :class="badgeOfRevision(r.status)" class="inv-badge">{{ revisionStatuses[r.status] }}</span></td>
                <td class="inv-ltr small">{{ r.fileName }}<div class="inv-muted">{{ sizeText(r.sizeBytes) }} · {{ r.lineCount }} خط{{ r.programNumber ? " · " + r.programNumber : "" }}</div></td>
                <td>{{ revisionSources[r.source] }}</td>
                <td><template v-if="r.baseRevision">+{{ r.diffAdded }} / -{{ r.diffRemoved }} <span class="inv-muted">(نسبت به {{ r.baseRevision }})</span></template><span v-else>-</span></td>
                <td><button :class="r.errorCount ? 'btn-outline-danger' : r.warningCount ? 'btn-outline-warning' : 'btn-outline-success'" class="btn btn-sm" @click="openFindings = openFindings === r.id ? null : r.id">{{ r.errorCount ? r.errorCount + " خطا" : r.warningCount ? r.warningCount + " هشدار" : "بدون ایراد" }}</button></td>
                <td>{{ r.authorName }}<div class="inv-muted">{{ r.createdAtText }}</div></td>
                <td>
                  <template v-if="r.approvedByName">{{ r.approvedByName }}<div class="inv-muted">{{ r.approvedAtText }}</div></template>
                  <template v-else-if="r.rejectedByName">{{ r.rejectedByName }}<div class="inv-muted">{{ r.rejectReason }}</div></template>
                  <span v-else-if="r.submittedAtText" class="inv-muted">ارسال: {{ r.submittedAtText }}</span>
                  <span v-else>-</span>
                </td>
                <td class="text-nowrap">
                  <button v-for="a in r.allowedActions" :key="a" :class="a === 'approve' ? 'btn-success' : a === 'reject' || a === 'withdraw' ? 'btn-outline-danger' : 'btn-primary'" :disabled="busy" class="btn btn-sm me-1" @click="act(r, a)">{{ revisionActionTitles[a] }}</button>
                  <button class="btn btn-sm btn-outline-secondary me-1" @click="showDiff(r)">تفاوت</button>
                  <button class="btn btn-sm btn-outline-secondary me-1" @click="check(r)">بررسی با ماشین‌ها</button>
                  <button class="btn btn-sm btn-outline-secondary" @click="download(r)">دانلود</button>
                </td>
              </tr>
              <tr v-if="openFindings === r.id">
                <td colspan="9">
                  <div v-if="r.changeNote" class="small mb-1"><b>شرح تغییر:</b> {{ r.changeNote }}</div>
                  <div v-if="!r.findings.length" class="inv-muted small">برنامه با همه ماشین‌های هدف سازگار است.</div>
                  <ul class="small mb-0">
                    <li v-for="(f, i) in r.findings" :key="i" :class="f.severity === 'error' ? 'text-danger' : 'text-warning'">{{ f.text }}</li>
                  </ul>
                  <div class="inv-muted small mt-1 inv-ltr">SHA-256: {{ r.sha256 }}</div>
                </td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>
      <p class="inv-muted small">تأیید نسخه با شخصی غیر از نویسنده (یا درخواست‌کننده دریافت از ماشین) انجام می‌شود؛ پس از تأیید، محتوای نسخه تغییرناپذیر است و اصلاح یعنی نسخه تازه.</p>

      <h3 class="h6 mt-3">انتقال‌ها</h3>
      <TransfersTable :rows="transfers" @changed="load" />

      <h3 class="h6 mt-3">سابقه اجرا</h3>
      <div class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>زمان شروع</th><th>عملیات</th><th>ماشین</th><th>نسخه</th><th>نوع کار</th><th>اپراتور</th></tr></thead>
          <tbody>
            <tr v-if="!runs.length"><td colspan="6" class="text-center inv-muted">هنوز اجرایی ثبت نشده است.</td></tr>
            <tr v-for="r in runs" :key="r.id"><td>{{ r.startedAtText }}</td><td>{{ r.operationName }}</td><td>{{ r.machineTitle }}</td><td>{{ r.revision }}</td><td>{{ bookingKinds[r.kind] }}</td><td>{{ r.employeeName || "-" }}</td></tr>
          </tbody>
        </table>
      </div>
    </template>

    <div v-if="upload" class="inv-modal-backdrop" @click.self="upload = null">
      <div class="inv-modal" style="max-width: 520px">
        <h3 class="inv-title mb-3">بارگذاری نسخه تازه</h3>
        <input class="form-control form-control-sm mb-2" type="file" @change="upload.file = ($event.target as HTMLInputElement).files?.[0] || null" />
        <label class="form-label small mb-0">شرح تغییر</label>
        <input v-model="upload.note" class="form-control form-control-sm" />
        <p class="inv-muted small mt-2 mb-0">فایل با همه ماشین‌هایی که برنامه به آن‌ها می‌رود بررسی می‌شود (نویسه‌ها، محورها، دور، ابزار، پایان برنامه). حداکثر ۴ مگابایت.</p>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="upload = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="doUpload">بارگذاری</button></div>
      </div>
    </div>

    <div v-if="diff" class="inv-modal-backdrop" @click.self="diff = null">
      <div class="inv-modal" style="max-width: 900px">
        <h3 class="inv-title mb-2">تفاوت نسخه {{ diff.revision }}<span v-if="diff.baseRevision"> با نسخه {{ diff.baseRevision }}</span></h3>
        <p class="small mb-2">{{ diff.added }} خط افزوده، {{ diff.removed }} خط حذف‌شده<span v-if="diff.approximate"> (تقریبی: دو فایل بسیار متفاوت‌اند)</span><span v-else-if="diff.truncated"> (فهرست کامل نمایش داده نمی‌شود)</span></p>
        <pre class="dnc-diff inv-ltr"><div v-for="(l, i) in diff.lines" :key="i" :class="diffClass(l)">{{ l }}</div><div v-if="!diff.lines.length && diff.baseRevisionId" class="inv-muted">تفاوتی برای نمایش نیست.</div><div v-if="!diff.baseRevisionId" class="inv-muted">این نخستین نسخه است.</div></pre>
        <div class="d-flex justify-content-end mt-2"><button class="btn btn-sm btn-outline-secondary" @click="diff = null">بستن</button></div>
      </div>
    </div>

    <div v-if="checks" class="inv-modal-backdrop" @click.self="checks = null">
      <div class="inv-modal" style="max-width: 760px">
        <h3 class="inv-title mb-3">بررسی برنامه با ماشین‌ها</h3>
        <div v-for="c in checks" :key="c.machineId" class="mb-3">
          <b>{{ c.machineTitle }}</b> <span class="inv-ltr inv-muted">{{ c.targetName || "" }}</span>
          <span :class="c.errorCount ? 'bad' : c.warningCount ? 'warn' : 'ok'" class="inv-badge ms-1">{{ c.errorCount ? c.errorCount + " خطا" : c.warningCount ? c.warningCount + " هشدار" : "سازگار" }}</span>
          <ul class="small mb-0"><li v-for="(f, i) in c.findings" :key="i" :class="f.severity === 'error' ? 'text-danger' : 'text-warning'">{{ f.text }}</li></ul>
        </div>
        <div class="d-flex justify-content-end"><button class="btn btn-sm btn-outline-secondary" @click="checks = null">بستن</button></div>
      </div>
    </div>

    <div v-if="sendTo" class="inv-modal-backdrop" @click.self="sendTo = null">
      <div class="inv-modal" style="max-width: 520px">
        <h3 class="inv-title mb-3">ارسال نسخه {{ current?.revision }} به ماشین</h3>
        <div v-for="t in sendTo.targets" :key="t.machineId" class="form-check">
          <input :id="'snd' + t.machineId" v-model="sendTo.machineId" :disabled="t.errorCount > 0" :value="t.machineId" class="form-check-input" type="radio" />
          <label :for="'snd' + t.machineId" class="form-check-label">{{ t.machineTitle }} <span class="inv-ltr inv-muted">{{ t.targetName }}</span><span v-if="t.errorCount" class="text-danger small"> — {{ t.findings[0]?.text }}</span></label>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="sendTo = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-success" @click="doSend">ارسال</button></div>
      </div>
    </div>

    <div v-if="receive" class="inv-modal-backdrop" @click.self="receive = null">
      <div class="inv-modal" style="max-width: 520px">
        <h3 class="inv-title mb-3">دریافت برنامه از ماشین</h3>
        <label class="form-label small mb-0">ماشین</label>
        <select v-model="receive.machineId" class="form-select form-select-sm mb-2"><option :value="null">انتخاب کنید</option><option v-for="t in receive.targets" :key="t.machineId" :value="t.machineId">{{ t.machineTitle }}</option></select>
        <label class="form-label small mb-0">نام برنامه روی کنترلر (خالی: همان نام نسخه جاری)</label>
        <input v-model="receive.programName" class="form-control form-control-sm inv-ltr mb-2" />
        <label class="form-label small mb-0">علت دریافت (شرح تغییر)</label>
        <input v-model="receive.note" class="form-control form-control-sm" />
        <p class="inv-muted small mt-2 mb-0">برای اتصال سریال، خروجی برنامه (Punch / Output) را روی کنترلر شروع کنید.</p>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="receive = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="doReceive">ثبت درخواست</button></div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.dnc-diff { max-height: 60vh; overflow: auto; background: var(--bs-light, #f8f9fa); padding: 8px; font-size: 12px; direction: ltr; text-align: left; }
.dnc-add { background: #e6f4ea; color: #1e6b34; }
.dnc-del { background: #fdecea; color: #a12622; }
.dnc-gap { color: #888; }
</style>
