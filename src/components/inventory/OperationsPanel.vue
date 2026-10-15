<script setup lang="ts">
// Manufacturing steps of one serial piece or one lot: start / finish, result, QC decision, attached files.
import { ref } from "vue";
import { addOperation, addOperationFile, applyRouting, downloadFile, qcOperation, updateOperation, uploadFile } from "@/api/InventoryAPIs";
import { fail, jalali, ok, operationStatuses, qcStatuses } from "@/composables/inventory";

const props = defineProps({
  operations: { type: Array as () => any[], default: () => [] },
  serialUnitId: { type: Number, default: null },
  lotId: { type: Number, default: null },
  machines: { type: Array as () => any[], default: () => [] },
  readonly: { type: Boolean, default: false },
});
const emit = defineEmits(["changed"]);

const actionTitles: Record<string, string> = { start: "شروع", finish: "پایان", edit: "ویرایش" };
const editing = ref<any>(null);
const qc = ref<any>(null);
const adding = ref<any>(null);
const busy = ref(false);

function stepClass(op: any) {
  if (op.status === 3 || op.qcStatus === 3) return "rejected";
  if (op.status === 2 && op.qcStatus !== 1) return "done";
  if (op.status === 1 || op.qcStatus === 1) return "active";
  return "";
}

function qcClass(s: number) {
  return s === 2 ? "ok" : s === 3 ? "bad" : s === 4 ? "warn" : s === 1 ? "info" : "";
}

async function act(op: any, action: string, extra: any = {}) {
  busy.value = true;
  const [, error] = await updateOperation({ id: op.id, action, ...extra });
  busy.value = false;
  if (error) return fail(error);
  emit("changed");
}

function openEdit(op: any, action: string) {
  editing.value = { op, action, operatorName: op.operatorName || "", machineId: op.machineId, result: op.result || "", notes: op.notes || "" };
}

async function saveEdit() {
  const e = editing.value;
  await act(e.op, e.action, { operatorName: e.operatorName, machineId: e.machineId, result: e.result, notes: e.notes });
  editing.value = null;
}

async function saveQc() {
  busy.value = true;
  const [, error] = await qcOperation({ id: qc.value.op.id, status: qc.value.status, note: qc.value.note });
  busy.value = false;
  if (error) return fail(error);
  ok("نتیجه QC ثبت شد.");
  qc.value = null;
  emit("changed");
}

async function attach(op: any, event: Event) {
  const input = event.target as HTMLInputElement;
  const file = input.files && input.files[0];
  input.value = "";
  if (!file) return;
  busy.value = true;
  const [id, error] = await uploadFile(file, `${op.title} - ${file.name}`);
  if (error) {
    busy.value = false;
    return fail(error);
  }
  const [, error2] = await addOperationFile({ partOperationId: op.id, documentId: id, title: file.name });
  busy.value = false;
  if (error2) return fail(error2);
  ok("فایل پیوست شد.");
  emit("changed");
}

async function routing() {
  busy.value = true;
  const [, error] = await applyRouting({ serialUnitId: props.serialUnitId, lotId: props.lotId });
  busy.value = false;
  if (error) return fail(error);
  ok("مسیر ساخت اعمال شد.");
  emit("changed");
}

function openAdd() {
  const last = props.operations.length ? Math.max(...props.operations.map((o: any) => o.sequence)) : 0;
  adding.value = { title: "", sequence: last + 10, requiresQc: false, machineId: null, notes: "" };
}

async function saveAdd() {
  busy.value = true;
  const [, error] = await addOperation({ ...adding.value, serialUnitId: props.serialUnitId, lotId: props.lotId });
  busy.value = false;
  if (error) return fail(error);
  adding.value = null;
  emit("changed");
}
</script>

<template>
  <div>
    <div class="inv-toolbar">
      <h3 class="inv-title" style="font-size: 1rem">مراحل ساخت و کنترل کیفیت</h3>
      <div v-if="!readonly" class="d-flex gap-2">
        <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="routing">اعمال مسیر ساخت</button>
        <button class="btn btn-sm btn-outline-secondary" @click="openAdd">+ مرحله</button>
      </div>
    </div>
    <div v-if="!operations.length" class="inv-muted">مرحله‌ای ثبت نشده است. «اعمال مسیر ساخت» مراحل تعریف‌شده برای این کالا یا گروه آن را اضافه می‌کند.</div>
    <div v-else class="inv-timeline">
      <div v-for="op in operations" :key="op.id" :class="stepClass(op)" class="step">
        <div class="d-flex flex-wrap gap-2 align-items-center">
          <b>{{ op.sequence }}. {{ op.title }}</b>
          <span class="inv-badge" :class="{ ok: op.status === 2, info: op.status === 1, bad: op.status === 3 }">{{ operationStatuses[op.status] }}</span>
          <span v-if="op.qcStatus" class="inv-badge" :class="qcClass(op.qcStatus)">{{ qcStatuses[op.qcStatus] }}</span>
          <span v-if="op.lotNumber && serialUnitId" class="inv-muted">(مرحله بچ {{ op.lotNumber }})</span>
        </div>
        <div class="small">
          <span v-if="op.startedAt">شروع {{ jalali(op.startedAt, true) }}</span>
          <span v-if="op.finishedAt"> · پایان {{ jalali(op.finishedAt, true) }}</span>
          <span v-if="op.operatorName"> · اپراتور: {{ op.operatorName }}</span>
          <span v-if="op.machineTitle"> · دستگاه: {{ op.machineTitle }}</span>
          <span v-if="op.workOrderCode"> · دستور ساخت {{ op.workOrderCode }}</span>
        </div>
        <div v-if="op.result" class="small">نتیجه: {{ op.result }}</div>
        <div v-if="op.notes" class="small inv-muted">{{ op.notes }}</div>
        <div v-if="op.qcAt" class="small">QC: {{ op.qcByName }} {{ jalali(op.qcAt, true) }} <span v-if="op.qcNote">— {{ op.qcNote }}</span></div>
        <div v-if="op.files.length" class="small">
          پیوست‌ها:
          <a v-for="f in op.files" :key="f.id" class="ms-2" href="#" @click.prevent="downloadFile(f.documentId, f.title)">{{ f.title }}</a>
        </div>
        <div v-if="!readonly" class="d-flex flex-wrap gap-2 mt-1">
          <button v-if="op.status === 0" :disabled="busy" class="btn btn-sm btn-outline-primary py-0" @click="openEdit(op, 'start')">شروع</button>
          <button v-if="op.status === 1" :disabled="busy" class="btn btn-sm btn-outline-success py-0" @click="openEdit(op, 'finish')">پایان</button>
          <button v-if="op.status >= 2 && op.qcStatus !== 2" :disabled="busy" class="btn btn-sm btn-outline-secondary py-0" @click="act(op, 'reopen')">بازگشایی</button>
          <button :disabled="busy" class="btn btn-sm btn-outline-secondary py-0" @click="openEdit(op, 'edit')">ویرایش</button>
          <button v-if="op.status === 2 && op.qcStatus === 1" class="btn btn-sm btn-warning py-0" @click="qc = { op, status: 2, note: '' }">تصمیم QC</button>
          <label class="btn btn-sm btn-outline-secondary py-0 mb-0">
            پیوست فایل
            <input class="d-none" type="file" @change="attach(op, $event)" />
          </label>
        </div>
      </div>
    </div>

    <div v-if="editing" class="inv-modal-backdrop" @click.self="editing = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-3">{{ editing.op.title }} — {{ actionTitles[editing.action] }}</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small">اپراتور</label><input v-model="editing.operatorName" class="form-control form-control-sm" /></div>
          <div class="col-md-6">
            <label class="form-label small">دستگاه</label>
            <select v-model="editing.machineId" class="form-select form-select-sm">
              <option :value="null">-</option>
              <option v-for="m in machines" :key="m.id" :value="m.id">{{ m.title }}</option>
            </select>
          </div>
          <div class="col-12"><label class="form-label small">نتیجه / اندازه‌گیری</label><input v-model="editing.result" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-label small">یادداشت</label><textarea v-model="editing.notes" class="form-control form-control-sm" rows="2"></textarea></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="editing = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="saveEdit">ثبت</button>
        </div>
      </div>
    </div>

    <div v-if="qc" class="inv-modal-backdrop" @click.self="qc = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-3">تصمیم QC — {{ qc.op.title }}</h2>
        <div class="d-flex gap-3 mb-2">
          <div v-for="s in [2, 4, 3]" :key="s" class="form-check">
            <input :id="'qc' + s" v-model="qc.status" :value="s" class="form-check-input" type="radio" />
            <label :for="'qc' + s" class="form-check-label small">{{ qcStatuses[s] }}</label>
          </div>
        </div>
        <textarea v-model="qc.note" class="form-control form-control-sm" placeholder="توضیح (برای رد یا تایید مشروط الزامی است)" rows="3"></textarea>
        <p v-if="qc.status === 3" class="small text-danger mt-1 mb-0">رد QC مرحله را «رد شد» می‌کند؛ برای اصلاح، مرحله را بازگشایی کنید یا مرحله جدید اضافه کنید.</p>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="qc = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="saveQc">ثبت</button>
        </div>
      </div>
    </div>

    <div v-if="adding" class="inv-modal-backdrop" @click.self="adding = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-3">مرحله جدید</h2>
        <div class="row g-2">
          <div class="col-md-3"><label class="form-label small">ترتیب</label><input v-model.number="adding.sequence" class="form-control form-control-sm" type="number" /></div>
          <div class="col-md-9"><label class="form-label small">عنوان *</label><input v-model="adding.title" class="form-control form-control-sm" /></div>
          <div class="col-md-6">
            <label class="form-label small">دستگاه</label>
            <select v-model="adding.machineId" class="form-select form-select-sm">
              <option :value="null">-</option>
              <option v-for="m in machines" :key="m.id" :value="m.id">{{ m.title }}</option>
            </select>
          </div>
          <div class="col-md-6 form-check mt-4 ps-5"><input id="addQc" v-model="adding.requiresQc" class="form-check-input" type="checkbox" /><label class="form-check-label small" for="addQc">نیاز به تایید QC</label></div>
          <div class="col-12"><label class="form-label small">یادداشت</label><input v-model="adding.notes" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="adding = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="saveAdd">افزودن</button>
        </div>
      </div>
    </div>
  </div>
</template>
