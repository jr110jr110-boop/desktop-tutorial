<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import ItemPicker from "@/components/inventory/ItemPicker.vue";
import ReleasePrint from "@/components/quality/print/ReleasePrint.vue";
import WorkOrderSelect from "@/components/quality/WorkOrderSelect.vue";
import { getReleaseChecks, getReleaseNote, releaseNoteAction, saveReleaseNote } from "@/api/QualityAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { printForm, releaseNoteStatuses } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// One release note: the checks per work order are computed by the API (ITP points, open NCRs, required reports, production complete, final inspection, data book).
// Its lines (item, quantity, serial numbers) are what a customer delivery of the work order may carry (R1q D10): the delivery gate looks at them.
const route = useRoute();
const router = useRouter();
const isNew = route.params.id === "new";
const note = ref<any>(null);
const checks = ref<any[]>([]);
const canRelease = ref(false);
const reason = ref("");
const busy = ref(false);
const pmode = ref<"note" | "coc">("note");
async function doPrint(mode: any = null) {
  if (mode !== null) pmode.value = mode;
  await nextTick();
  printForm();
}
const canManage = hasRole(QualityRoles.managers);
const isQc = hasRole(QualityRoles.inspectors);
const draft = () => isNew || note.value?.status === 0;
const editable = computed(() => canManage && !!note.value && (isNew || note.value.status === 0));

// ---------------------------------------------------------------------------------------------- the lines
interface LineRow {
  key: number;
  item: any | null;
  quantity: number | null;
  serialsText: string;
}
let lineKey = 0;
const lines = ref<LineRow[]>([]);
/** ItemTrackingMode.Serial = 2: one serial number per piece, the count equal to the quantity */
const isSerialItem = (l: LineRow) => !!l.item && Number(l.item.trackingMode) === 2;
const parseSerials = (text: string) =>
  (text || "")
    .split(/[\n,،;]+/)
    .map((s) => s.trim())
    .filter((s) => s.length > 0);
const serialCount = (l: LineRow) => parseSerials(l.serialsText).length;
const newLine = (): LineRow => ({ key: ++lineKey, item: null, quantity: null, serialsText: "" });
const addLine = () => lines.value.push(newLine());

function onPick(l: LineRow) {
  // serial numbers belong to a serial item only; the API clears them for the others
  if (!isSerialItem(l)) l.serialsText = "";
}

/** Everything the draft form holds, to tell an unsaved change from the saved note (a change must be saved before the note can be released). */
const snapshot = () => JSON.stringify([note.value?.itemsText || "", note.value?.note || "", lines.value.map((l) => [l.item?.id ?? null, l.quantity, l.serialsText])]);
const savedSnapshot = ref("");
const dirty = computed(() => !!note.value && editable.value && snapshot() !== savedSnapshot.value);
/** the person who saved this draft in this page cannot release it (R1q D22: maker and checker are different people); the API checks the other drafters */
const touchedByMe = ref(false);

function load(dto: any) {
  note.value = dto;
  checks.value = dto.checks || [];
  canRelease.value = dto.canRelease;
  lines.value = (dto.lines || []).map((l: any) => ({
    key: ++lineKey,
    item: { id: l.itemId, code: l.itemCode, title: l.itemTitle, unit: l.unit, trackingMode: (l.serialNumbers || []).length ? 2 : null },
    quantity: l.quantity,
    serialsText: (l.serialNumbers || []).join("\n"),
  }));
  if (!lines.value.length && dto.status === 0) lines.value.push(newLine());
  savedSnapshot.value = snapshot();
}

async function loadChecks() {
  checks.value = [];
  canRelease.value = false;
  if (!note.value.workOrderId) return;
  const [data, error] = await getReleaseChecks(note.value.workOrderId);
  if (error) return fail(error);
  checks.value = data.checks;
  canRelease.value = data.canRelease;
}

async function save() {
  const rows = lines.value;
  for (let i = 0; i < rows.length; i++) {
    const l = rows[i];
    if (!l.item || !l.item.id) return fail(`ردیف ${i + 1}: کالا را انتخاب کنید (ردیف خالی را حذف کنید).`);
    if (!(Number(l.quantity) > 0)) return fail(`ردیف ${i + 1}: مقدار باید بیشتر از صفر باشد.`);
  }
  busy.value = true;
  const [data, error] = await saveReleaseNote({
    id: note.value.id || null,
    workOrderId: note.value.workOrderId,
    itemsText: note.value.itemsText,
    note: note.value.note,
    lines: rows.map((l) => ({ itemId: l.item.id, quantity: Number(l.quantity), serialNumbers: isSerialItem(l) ? parseSerials(l.serialsText) : [] })),
  });
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  touchedByMe.value = true;
  load(data);
  if (isNew) router.replace({ name: "QualityReleaseNoteEdit", params: { id: data.id } });
}

async function act(action: string) {
  if (action === "cancel" && !reason.value.trim()) return fail("دلیل لغو را بنویسید.");
  busy.value = true;
  const [data, error] = await releaseNoteAction(note.value.id, action, reason.value);
  busy.value = false;
  if (error) return fail(error);
  load(data);
  ok(action === "release" ? "برگه ترخیص و گواهی انطباق صادر شد." : "برگه لغو شد.");
}

onMounted(async () => {
  if (isNew) {
    note.value = { id: null, workOrderId: route.query.workOrderId ? Number(route.query.workOrderId) : null, itemsText: "", note: "", status: 0, lines: [] };
    lines.value = [newLine()];
    savedSnapshot.value = snapshot();
    loadChecks();
    return;
  }
  const [data, error] = await getReleaseNote(Number(route.params.id));
  if (error) return fail(error);
  load(data);
});
</script>

<template>
  <div v-if="note" class="inv-page">
    <ReleasePrint v-if="!isNew" :note="note" :kind="pmode" />
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">برگه ترخیص <span v-if="note.number" class="inv-ltr ms-2">{{ note.number }}</span></h1>
          <span :class="{ ok: note.status === 1, bad: note.status === 2, warn: note.status === 0 }" class="inv-badge">{{ releaseNoteStatuses[note.status] }}</span>
          <span v-if="note.certificateOfConformityNumber" class="inv-badge info ms-1">گواهی انطباق <span class="inv-ltr">{{ note.certificateOfConformityNumber }}</span></span>
          <span v-if="dirty && !isNew" class="inv-badge warn ms-1">تغییر ذخیره‌نشده</span>
        </div>
        <div class="d-flex flex-wrap gap-2">
          <router-link :to="{ name: 'QualityReleaseNotes' }" class="btn btn-sm btn-outline-secondary">بازگشت</router-link>
          <button v-if="!isNew" class="btn btn-sm btn-outline-secondary" @click="doPrint('note')">چاپ برگه ترخیص</button>
          <button v-if="!isNew && note.status === 1" class="btn btn-sm btn-outline-secondary" @click="doPrint('coc')">چاپ گواهی انطباق</button>
          <router-link v-if="note.workOrderId" :to="{ name: 'QualityDataBook', params: { workOrderId: note.workOrderId } }" class="btn btn-sm btn-outline-secondary">دیتابوک نهایی</router-link>
          <button v-if="draft() && canManage" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
          <button v-if="!isNew && note.status === 0 && isQc" :disabled="busy || !canRelease || dirty || touchedByMe" class="btn btn-sm btn-success" @click="act('release')">صدور ترخیص و گواهی انطباق (مدیر QC)</button>
        </div>
      </div>
      <div class="row g-2">
        <div class="col-md-3">
          <label class="form-label small mb-0">دستور ساخت *</label>
          <WorkOrderSelect v-if="isNew" v-model="note.workOrderId" @change="loadChecks" />
          <div v-else class="small pt-1 inv-ltr">{{ note.workOrderCode }}</div>
        </div>
        <div class="col-md-9"><label class="form-label small mb-0">شرح اقلام (متن آزاد برای چاپ؛ فهرست الزام‌آور در جدول زیر است)</label><textarea v-model="note.itemsText" :disabled="!editable" class="form-control form-control-sm" rows="2"></textarea></div>
        <div class="col-md-12"><label class="form-label small mb-0">توضیح</label><input v-model="note.note" :disabled="!editable" class="form-control form-control-sm" /></div>
      </div>
      <p v-if="note.draftedByName" class="small mt-2 mb-0">تنظیم‌کننده: {{ note.draftedByName }}</p>
      <p v-if="note.releasedByName" class="small mt-1 mb-0">صادرکننده: {{ note.releasedByName }} — {{ jalali(note.releasedAt, true) }}</p>
      <p v-if="note.cancelReason" class="small text-danger mt-2 mb-0">دلیل لغو: {{ note.cancelReason }}</p>
      <p v-if="!isNew && note.status === 0 && isQc && touchedByMe" class="inv-muted small mt-2 mb-0">این برگه را شما ذخیره کرده‌اید؛ تنظیم‌کننده یا ویرایشگر برگه آن را صادر نمی‌کند و مدیر QC دیگری باید امضا کند.</p>
      <p v-else-if="!isNew && note.status === 0 && isQc && dirty" class="inv-muted small mt-2 mb-0">ابتدا تغییرها را ذخیره کنید؛ اما هر که برگه را ذخیره کند دیگر نمی‌تواند همان را صادر کند.</p>
    </div>

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h2 class="inv-title">اقلام ترخیص‌شونده</h2>
        <button v-if="editable" class="btn btn-sm btn-outline-primary" @click="addLine">ردیف جدید</button>
      </div>
      <p class="inv-muted small">
        تحویل به مشتری فقط تا سقف همین فهرست مجاز است: برای هر کالا مقدار، و برای کالای سریالی سریال هر قطعه (هر سریال در یک خط، تعداد سریال‌ها برابر مقدار)؛ سریال باید قطعه‌ای باشد که همین دستور ساخت تولید یا ثبت کرده و ضایعات، برگشتی یا تبدیل‌شده نباشد.
      </p>
      <table class="table table-sm">
        <thead><tr><th style="width: 40px">#</th><th style="min-width: 240px">کالا</th><th style="width: 140px">مقدار</th><th>سریال‌ها</th><th v-if="editable" style="width: 70px"></th></tr></thead>
        <tbody>
          <tr v-if="!lines.length"><td :colspan="editable ? 5 : 4" class="text-center inv-muted">فهرست اقلامی ثبت نشده است.</td></tr>
          <tr v-for="(l, i) in lines" :key="l.key">
            <td>{{ i + 1 }}</td>
            <td>
              <ItemPicker v-if="editable" v-model="l.item" @select="onPick(l)" />
              <template v-else-if="l.item"><span class="inv-ltr">{{ l.item.code }}</span> — {{ l.item.title }}</template>
            </td>
            <td>
              <input v-if="editable" v-model.number="l.quantity" class="form-control form-control-sm inv-ltr" min="0" step="any" type="number" />
              <span v-else class="inv-ltr">{{ l.quantity }}</span>
              <span v-if="l.item && l.item.unit" class="inv-muted small ms-1">{{ l.item.unit }}</span>
            </td>
            <td>
              <template v-if="editable">
                <textarea v-if="isSerialItem(l)" v-model="l.serialsText" class="form-control form-control-sm inv-ltr" placeholder="هر سریال در یک خط" rows="2"></textarea>
                <span v-else-if="l.item" class="inv-muted small">کالای بدون سریال؛ فقط مقدار ثبت می‌شود.</span>
                <div v-if="isSerialItem(l)" :class="serialCount(l) === Number(l.quantity) ? 'inv-muted' : 'text-danger'" class="small">{{ serialCount(l) }} سریال از {{ l.quantity || 0 }}</div>
              </template>
              <span v-else class="inv-ltr small">{{ parseSerials(l.serialsText).join("، ") || "—" }}</span>
            </td>
            <td v-if="editable"><button class="btn btn-sm btn-outline-danger" type="button" @click="lines.splice(i, 1)">حذف</button></td>
          </tr>
        </tbody>
      </table>
    </div>

    <div class="inv-card screen-only">
      <h2 class="inv-title mb-2">کنترل‌های ترخیص (محاسبه‌شده)</h2>
      <p class="inv-muted">{{ note.status === 1 ? "تصویر ثبت‌شده هنگام صدور" : "محاسبه همین لحظه؛ هیچ مورد دستی نیست" }}</p>
      <table class="table table-sm">
        <thead><tr><th style="width: 40px"></th><th>کنترل</th><th>جزئیات</th></tr></thead>
        <tbody>
          <tr v-if="!checks.length"><td colspan="3" class="text-center inv-muted">دستور ساخت را انتخاب کنید.</td></tr>
          <tr v-for="c in checks" :key="c.code">
            <td><span :class="c.passed ? 'ok' : 'bad'" class="inv-badge">{{ c.passed ? "✓" : "✗" }}</span></td>
            <td>{{ c.title }}</td><td class="small">{{ c.detail === "-" ? "" : c.detail }}</td>
          </tr>
        </tbody>
      </table>
      <div v-if="!isNew && note.status === 0 && canManage" class="d-flex gap-2 align-items-end">
        <div class="flex-grow-1"><label class="form-label small mb-0">دلیل لغو</label><input v-model="reason" class="form-control form-control-sm" /></div>
        <button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act('cancel')">لغو برگه</button>
      </div>
      <p class="inv-muted mt-2 mb-0">ترخیص فقط وقتی همه کنترل‌ها قبول و فهرست اقلام کامل باشد ممکن است؛ تحویل به مشتری هم برگه ترخیص صادرشده می‌خواهد و فقط تا سقف اقلام آن مجاز است.</p>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
