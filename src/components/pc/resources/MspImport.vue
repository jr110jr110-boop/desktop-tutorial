<script setup lang="ts">
// Import of the resources and assignments of a Microsoft Project file (MSPDI, «Save As» XML) into the OPEN plan of a project (priority 4d-1).
// Steps: choose the file (up to 8 MB) -> preview (nothing is saved: every MS Project resource gets a suggested pool resource, every assignment a status) -> review the mapping -> commit.
// The rates and costs of the file are never imported. The file is not stored, so the commit sends it again together with the reviewed mapping.
import { computed, ref } from "vue";
import { commitMspImport, getResources, MSP_MAX_BYTES, previewMspImport } from "@/api/ResourceAPIs";
import type { MspMapping, MspPreview, Resource, ResourcePlan } from "@/api/ResourceAPIs";
import { faNum } from "@/composables/ceo";
import { fail, ok } from "@/composables/inventory";
import { resourceKinds, resourceLabel, resourceUnits } from "@/composables/resources";

const props = defineProps({ contractId: { type: Number, required: true } });
const emit = defineEmits<{ (e: "done", plan: ResourcePlan | null): void; (e: "close"): void }>();

const xml = ref("");
const fileName = ref("");
const preview = ref<MspPreview | null>(null);
const pool = ref<Resource[]>([]);
const busy = ref(false);
/** per MS Project resource: "" (skip), "r:<id>" an existing resource, "new" create one from the file's name */
const choice = ref<Record<number, string>>({});
const created = ref<Record<number, { code: string; title: string; kind: number; unit: number; capacity: number }>>({});
const resultText = ref("");

const matchText: Record<string, string> = { code: "هم‌کد", title: "هم‌نام", normalized: "هم‌نام (با یکسان‌سازی املا)", none: "بدون پیشنهاد" };
const statusText: Record<string, string> = { ok: "درست", resourceUnmatched: "منبع ناشناخته", activityUnmatched: "فعالیت ناشناخته", zeroWork: "کار صفر", duplicate: "تکراری" };
const statusBadge = (s: string) => (s === "ok" ? "ok" : s === "duplicate" ? "info" : "warn");

async function pick(e: Event) {
  const f = (e.target as HTMLInputElement).files?.[0];
  preview.value = null;
  resultText.value = "";
  if (!f) return;
  if (f.size > MSP_MAX_BYTES) { xml.value = ""; fileName.value = ""; return fail("فایل XML از حد مجاز (۸ مگابایت) بزرگ‌تر است."); }
  xml.value = await f.text();
  fileName.value = f.name;
}

async function run() {
  if (!xml.value) return fail("فایل XML نرم‌افزار Microsoft Project را انتخاب کنید.");
  busy.value = true;
  const [list, listError] = await getResources({ isActive: true });
  if (listError) { busy.value = false; return fail(listError); }
  pool.value = list;
  const [data, error] = await previewMspImport(props.contractId, xml.value);
  busy.value = false;
  if (error) return fail(error);
  preview.value = data;
  choice.value = {};
  created.value = {};
  for (const r of data.resources) {
    choice.value[r.mspUid] = r.suggestedResourceId ? "r:" + r.suggestedResourceId : "";
    created.value[r.mspUid] = { code: r.code || "", title: r.name, kind: r.type === 0 ? 4 : 1, unit: r.type === 0 ? 3 : 1, capacity: 1 };
  }
}

const mapping = computed<MspMapping[]>(() =>
  (preview.value?.resources || []).flatMap((r): MspMapping[] => {
    const c = choice.value[r.mspUid] || "";
    if (c.startsWith("r:")) return [{ mspUid: r.mspUid, resourceId: Number(c.slice(2)), create: null }];
    if (c === "new") { const n = created.value[r.mspUid]; return [{ mspUid: r.mspUid, resourceId: null, create: { code: n.code.trim(), title: n.title.trim(), kind: n.kind, unit: n.unit, capacity: n.capacity, responsibleUnit: null } }]; }
    return [];
  })
);
const problem = computed(() => {
  for (const r of preview.value?.resources || []) {
    if (choice.value[r.mspUid] !== "new") continue;
    const n = created.value[r.mspUid];
    if (!n.code.trim() || !n.title.trim()) return "برای منبع تازه کد و عنوان بنویسید.";
    if (!(n.capacity > 0)) return "ظرفیت منبع تازه باید بیشتر از صفر باشد.";
  }
  return null;
});

/** What the commit would do with the current mapping, counted on the screen from the preview rows (the server counts again and its numbers are the ones shown after the commit). */
const mappedUids = computed(() => new Set(mapping.value.map((m) => m.mspUid)));
const importable = computed(() => (preview.value?.rows || []).filter((r) => r.status === "ok" || (r.status === "resourceUnmatched" && mappedUids.value.has(r.mspResourceUid))).length);

async function commit() {
  if (!preview.value || problem.value) return;
  busy.value = true;
  const [data, error] = await commitMspImport(props.contractId, xml.value, mapping.value);
  busy.value = false;
  if (error) return fail(error);
  resultText.value = `ثبت شد: ${faNum(data.created, 0)} ردیف تازه، ${faNum(data.updated, 0)} ردیف به‌روز، ${faNum(data.resourcesCreated, 0)} منبع تازه؛ ${faNum(data.skippedResourceUnmatched, 0)} منبع ناشناخته، ${faNum(data.skippedActivityUnmatched, 0)} فعالیت ناشناخته، ${faNum(data.skippedZeroWork, 0)} کار صفر و ${faNum(data.skippedDuplicate, 0)} تکراری نادیده گرفته شد.`;
  ok("ورود از فایل انجام شد.");
  preview.value = null;
  xml.value = "";
  fileName.value = "";
  emit("done", data.plan);
}
</script>

<template>
  <div class="border rounded p-3 mb-3 d-print-none" role="region" aria-label="ورود از MS Project">
    <div class="d-flex flex-wrap gap-2 align-items-center mb-2">
      <b>ورود منابع و تخصیص‌ها از MS Project</b>
      <button type="button" class="btn btn-sm btn-outline-secondary ms-auto" @click="emit('close')">بستن</button>
    </div>
    <p class="small inv-muted mb-2">در MS Project گزینه‌ی «Save As ← XML Format» را بزنید و فایل را اینجا بدهید (حداکثر ۸ مگابایت). فایل ذخیره نمی‌شود؛ پس از بازبینی نگاشت، ثبت می‌شود.</p>
    <div class="d-flex flex-wrap gap-2 align-items-center mb-2">
      <input type="file" accept=".xml,text/xml,application/xml" class="form-control form-control-sm" style="max-width: 320px" aria-label="فایل XML" @change="pick" />
      <button type="button" :disabled="busy || !xml" class="btn btn-sm btn-primary" @click="run">پیش‌نمایش</button>
      <span v-if="fileName" class="small inv-muted inv-ltr">{{ fileName }}</span>
    </div>
    <p v-if="resultText" class="small text-success mb-0" role="status">{{ resultText }}</p>

    <template v-if="preview">
      <div class="alert alert-warning py-2 small" role="note">{{ preview.ratesNote }}</div>
      <div v-if="preview.keepNote" class="alert alert-info py-2 small" role="note">{{ preview.keepNote }}</div>
      <p v-for="(w, i) in preview.warnings" :key="i" class="small text-danger mb-1">{{ w }}</p>
      <div class="d-flex flex-wrap gap-2 mb-2">
        <span class="inv-badge ok">درست: {{ faNum(preview.counts.ok, 0) }}</span>
        <span class="inv-badge warn">منبع ناشناخته: {{ faNum(preview.counts.resourceUnmatched, 0) }}</span>
        <span class="inv-badge warn">فعالیت ناشناخته: {{ faNum(preview.counts.activityUnmatched, 0) }}</span>
        <span class="inv-badge warn">کار صفر: {{ faNum(preview.counts.zeroWork, 0) }}</span>
        <span class="inv-badge info">تکراری: {{ faNum(preview.counts.duplicate, 0) }}</span>
        <span class="inv-muted small">از {{ faNum(preview.counts.assignments, 0) }} تخصیص فایل</span>
      </div>

      <b class="small">منابع فایل و نگاشت به منابع شما</b>
      <div class="table-responsive mb-2">
        <table class="table table-sm align-middle">
          <thead><tr><th>منبع در فایل</th><th>تخصیص</th><th>پیشنهاد</th><th>نگاشت</th></tr></thead>
          <tbody>
            <tr v-for="r in preview.resources" :key="r.mspUid">
              <td>{{ r.name }} <span v-if="r.code" class="inv-ltr inv-muted">({{ r.code }})</span></td>
              <td>{{ faNum(r.assignmentCount, 0) }}</td>
              <td><span :class="r.matchKind === 'none' ? 'inv-muted' : ''">{{ matchText[r.matchKind] || r.matchKind }}</span></td>
              <td style="min-width: 260px">
                <select v-model="choice[r.mspUid]" class="form-select form-select-sm" :aria-label="'نگاشت ' + r.name">
                  <option value="">نادیده بگیر</option>
                  <option value="new">منبع تازه بساز</option>
                  <option v-for="p in pool" :key="p.id" :value="'r:' + p.id">{{ resourceLabel(p) }}</option>
                </select>
                <div v-if="choice[r.mspUid] === 'new'" class="row g-1 mt-1">
                  <div class="col-4"><input v-model="created[r.mspUid].code" class="form-control form-control-sm" placeholder="کد" aria-label="کد" /></div>
                  <div class="col-8"><input v-model="created[r.mspUid].title" class="form-control form-control-sm" placeholder="عنوان" aria-label="عنوان" /></div>
                  <div class="col-4"><select v-model.number="created[r.mspUid].kind" class="form-select form-select-sm" aria-label="نوع"><option v-for="(n, k) in resourceKinds" :key="k" :value="Number(k)">{{ n }}</option></select></div>
                  <div class="col-4"><select v-model.number="created[r.mspUid].unit" class="form-select form-select-sm" aria-label="واحد"><option v-for="(n, k) in resourceUnits" :key="k" :value="Number(k)">{{ n }}</option></select></div>
                  <div class="col-4"><input v-model.number="created[r.mspUid].capacity" type="number" min="0" step="any" class="form-control form-control-sm" aria-label="ظرفیت" /></div>
                  <div class="col-12 small inv-muted">نرخی برای منبع تازه ثبت نمی‌شود؛ نرخ مصوب از مسیر نرخ استاندارد می‌آید.</div>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <details>
        <summary class="small">ردیف‌های تخصیص ({{ faNum(preview.rows.length, 0) }})</summary>
        <div class="table-responsive">
          <table class="table table-sm">
            <thead><tr><th>کار در فایل</th><th>فعالیت</th><th>منبع</th><th>کار (ساعت)</th><th>مقدار</th><th>وضعیت</th></tr></thead>
            <tbody>
              <tr v-for="(r, i) in preview.rows" :key="i">
                <td>{{ r.taskName }}<span v-if="r.wbs" class="inv-ltr inv-muted"> ({{ r.wbs }})</span></td>
                <td class="inv-ltr">{{ r.activityCode || "—" }}</td>
                <td>{{ r.mspResourceName }}</td>
                <td>{{ faNum(r.workHours, 2) }}</td>
                <td>{{ r.status === "ok" ? faNum(r.quantity, 4) + (r.unit ? " " + resourceUnits[r.unit] : "") : "—" }}</td>
                <td><span class="inv-badge" :class="statusBadge(r.status)">{{ statusText[r.status] || r.status }}</span><span class="small inv-muted"> {{ r.statusText }}</span></td>
              </tr>
            </tbody>
          </table>
        </div>
      </details>

      <p v-if="problem" class="small text-danger">{{ problem }}</p>
      <div class="d-flex flex-wrap gap-2 align-items-center mt-2">
        <button type="button" :disabled="busy || !!problem || importable === 0" class="btn btn-sm btn-success" @click="commit">ثبت در طرح منابع ({{ faNum(importable, 0) }} ردیف)</button>
        <span v-if="importable === 0" class="small inv-muted">ردیفی برای ثبت نیست؛ نگاشت منابع را کامل کنید.</span>
      </div>
    </template>
  </div>
</template>
