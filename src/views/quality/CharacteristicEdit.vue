<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import ReasonDialog from "@/components/quality/ReasonDialog.vue";
import { charListAction, downloadCharList, downloadCharTemplate, getCharList, getCharListForRevision, importCharacteristics, saveCharList } from "@/api/QualityAPIs";
import { fail, jalali, num, ok } from "@/composables/inventory";
import { charClasses, charListStatuses, instrumentClasses } from "@/composables/quality";

// Package B (H5): ONE characteristic list of a drawing revision. Draft: engineering edits it (or imports the Excel template); a second person approves it; it is released with the drawing revision and never changes
// afterwards (a correction is a new version with a reason). The inspection forms pick from the released list; nobody types a nominal or a tolerance there.
const route = useRoute();
const router = useRouter();
const list = ref<any>(null);
const revisionId = ref<number>(Number(route.query.revisionId) || 0);
const busy = ref(false);
const dialog = ref<null | { action: string; title: string; required: boolean; label: string }>(null);
const preview = ref<any>(null);
const file = ref<File | null>(null);
const editable = computed(() => !list.value || list.value.status === 0);
const can = (a: string) => (list.value?.actions || []).includes(a);

function blankLine(i: number) {
  return { no: String(i + 1), name: "", nominal: 0, tolPlus: 0, tolMinus: 0, unit: "mm", class: 1, instrumentClass: 99, method: "", samplePercent: null, operationSeq: null, stageCode2: "" };
}
function setList(data: any) {
  list.value = { ...data, items: (data.items || []).map((x: any) => ({ ...x })) };
  revisionId.value = data.revisionId;
}
async function load() {
  if (route.params.id !== "new") {
    const [data, error] = await getCharList(Number(route.params.id));
    if (error) return fail(error);
    return setList(data);
  }
  if (revisionId.value) {
    const [data] = await getCharListForRevision(revisionId.value);
    if (data) {
      router.replace({ name: "QualityCharacteristicEdit", params: { id: data.id } });
      return setList(data);
    }
  }
  list.value = { id: null, status: 0, version: 1, revisionId: revisionId.value, items: [blankLine(0)], actions: ["save"], reason: "" };
}

function payload() {
  return {
    id: list.value.id || null, revisionId: revisionId.value, reason: list.value.reason || null, rowVersion: list.value.rowVersion || null,
    items: list.value.items.map((x: any) => ({
      no: x.no, name: x.name, nominal: Number(x.nominal) || 0, tolPlus: Number(x.tolPlus) || 0, tolMinus: Number(x.tolMinus) || 0, unit: x.unit, class: Number(x.class),
      instrumentClass: Number(x.instrumentClass), method: x.method || null, samplePercent: x.samplePercent === "" || x.samplePercent === null ? null : Number(x.samplePercent),
      operationSeq: x.operationSeq === "" || x.operationSeq === null ? null : Number(x.operationSeq), stageCode2: x.stageCode2 || null,
    })),
  };
}
async function save() {
  busy.value = true;
  const [data, error] = await saveCharList(payload());
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  if (route.params.id === "new") router.replace({ name: "QualityCharacteristicEdit", params: { id: data.id } });
  setList(data);
}
async function newVersion(reason: string) {
  busy.value = true;
  const [data, error] = await saveCharList({ revisionId: revisionId.value, reason, items: list.value.items.map((x: any) => ({ ...x })) });
  busy.value = false;
  dialog.value = null;
  if (error) return fail(error);
  ok("نسخه جدید (پیش‌نویس) ساخته شد.");
  router.replace({ name: "QualityCharacteristicEdit", params: { id: data.id } });
  setList(data);
}
async function act(action: string, note?: string) {
  busy.value = true;
  const [data, error] = await charListAction(list.value.id, action, note, list.value.rowVersion);
  busy.value = false;
  dialog.value = null;
  if (error) return fail(error);
  ok("انجام شد.");
  setList(data);
}
function ask(action: string) {
  const titles: Record<string, string> = { return: "برگشت فهرست به تهیه‌کننده", void: "ابطال فهرست", "new-version": "نسخه جدید فهرست (اصلاح)" };
  dialog.value = { action, title: titles[action], required: true, label: action === "new-version" ? "دلیل اصلاح" : "دلیل" };
}
function confirmDialog(text: string) {
  if (!dialog.value) return;
  if (dialog.value.action === "new-version") return newVersion(text);
  return act(dialog.value.action, text);
}
function onFile(e: Event) {
  file.value = (e.target as HTMLInputElement).files?.[0] || null;
  preview.value = null;
}
async function runImport(previewOnly: boolean) {
  if (!file.value) return fail("فایل xlsx را انتخاب کنید.");
  busy.value = true;
  const [data, error] = await importCharacteristics({ revisionId: revisionId.value, listId: list.value?.id || undefined, reason: list.value?.reason || undefined, preview: previewOnly }, file.value);
  busy.value = false;
  if (error) return fail(error);
  preview.value = data;
  if (!previewOnly && data.listId) {
    ok("فهرست پیش‌نویس ساخته شد.");
    router.replace({ name: "QualityCharacteristicEdit", params: { id: data.listId } });
    const [l] = await getCharList(data.listId);
    if (l) setList(l);
  }
}
async function fileAction(fn: () => Promise<string | null>) {
  const error = await fn();
  if (error) fail(error);
}
onMounted(load);
</script>

<template>
  <div v-if="list" class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">فهرست مشخصه‌های نقشه <span class="inv-ltr ms-2">{{ list.documentNumber }}</span> <span class="inv-ltr">{{ list.revisionCode }}</span></h1>
          <span :class="{ ok: list.status === 3, warn: list.status === 1 || list.status === 2, bad: list.status === 5 }" class="inv-badge">{{ charListStatuses[list.status] }}</span>
          <span class="inv-muted ms-2">نسخه {{ list.version }}</span>
          <span v-if="list.documentTitle" class="inv-muted ms-2">{{ list.documentTitle }}</span>
        </div>
        <div class="d-flex flex-wrap gap-2">
          <router-link :to="{ name: 'QualityCharacteristics' }" class="btn btn-sm btn-outline-secondary">بازگشت</router-link>
          <button v-if="list.id" class="btn btn-sm btn-outline-secondary" @click="fileAction(() => downloadCharList(list.id))">خروجی Excel</button>
          <button v-if="editable" class="btn btn-sm btn-outline-secondary" @click="fileAction(downloadCharTemplate)">الگوی Excel</button>
          <button v-if="can('save') || !list.id" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
          <button v-if="can('submit')" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="act('submit')">ارسال برای تایید</button>
          <button v-if="can('approve')" :disabled="busy" class="btn btn-sm btn-success" @click="act('approve')">تایید</button>
          <button v-if="can('return')" :disabled="busy" class="btn btn-sm btn-outline-warning" @click="ask('return')">برگشت</button>
          <button v-if="can('void')" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="ask('void')">ابطال</button>
          <button v-if="can('new-version')" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="ask('new-version')">نسخه جدید (اصلاح)</button>
        </div>
      </div>
      <p v-if="list.status === 3" class="inv-muted">این فهرست با نقشه منتشر شده و تغییر نمی‌کند؛ برای اصلاح، «نسخه جدید» را بزنید (با دلیل، تایید نفر دوم).</p>
      <p v-else-if="list.status === 2" class="inv-muted">تایید شد ({{ list.approvedByName }}، {{ jalali(list.approvedAt) }}). همراه با انتشار بازنگری نقشه آزاد می‌شود. شناسه محتوا: <span class="inv-ltr">{{ list.contentHash }}</span></p>
      <p v-else-if="list.status === 1" class="inv-muted">در انتظار تایید نفر دوم (تهیه‌کننده یا ویرایش‌کننده فهرست نمی‌تواند آن را تایید کند).</p>
      <p v-if="list.returnNote" class="text-danger small">برگشت: {{ list.returnNote }}</p>
      <p v-if="list.voidReason" class="text-danger small">ابطال: {{ list.voidReason }}</p>
      <div v-if="editable && (list.version > 1 || !list.items.length) || list.reason" class="mb-2"><label class="form-label small mb-0">{{ list.items.length ? "دلیل این نسخه" : "دلیل «بدون مشخصه قابل اندازه‌گیری» (دست‌کم ۱۵ نویسه)" }}</label><input v-model="list.reason" :disabled="!editable" class="form-control form-control-sm" /><span v-if="!list.items.length" class="small inv-muted">فهرست خالی همان مسیر دو نفره تهیه و تایید را می‌گذراند؛ برای قطعه‌ای که فقط با گواهی و بررسی چشمی پذیرفته می‌شود.</span></div>

      <div v-if="editable" class="inv-filters mb-3">
        <input type="file" accept=".xlsx" class="form-control form-control-sm" @change="onFile" />
        <button class="btn btn-sm btn-outline-secondary" :disabled="busy" @click="runImport(true)">پیش‌نمایش فایل</button>
        <button class="btn btn-sm btn-outline-primary" :disabled="busy" @click="runImport(false)">ساخت پیش‌نویس از فایل</button>
      </div>
      <div v-if="preview" class="mb-3">
        <span class="inv-badge" :class="preview.errorRows ? 'bad' : 'ok'">{{ preview.validRows }} ردیف درست، {{ preview.errorRows }} ردیف خطا از {{ preview.rowCount }}</span>
        <ul v-if="preview.errorRows" class="small mt-1 mb-0"><li v-for="r in preview.rows.filter((x: any) => !x.ok)" :key="r.row">ردیف {{ r.row }} ({{ r.no }} {{ r.name }}): {{ r.message }}</li></ul>
      </div>

      <div class="table-responsive">
        <table class="table table-sm align-middle">
          <thead><tr><th>شماره</th><th>مشخصه</th><th>اسمی</th><th>+ تلرانس</th><th>− تلرانس</th><th>حدود</th><th>واحد</th><th>رده</th><th>کلاس ابزار</th><th>روش</th><th>نمونه‌برداری ٪</th><th>گام OPC</th><th>کد مرحله</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.items.length"><td colspan="14" class="text-center inv-muted">مشخصه‌ای نیست.</td></tr>
            <tr v-for="(x, i) in list.items" :key="i">
              <template v-if="editable">
                <td><input v-model="x.no" class="form-control form-control-sm inv-ltr" style="width: 70px" /></td>
                <td><input v-model="x.name" class="form-control form-control-sm" /></td>
                <td><input v-model="x.nominal" class="form-control form-control-sm inv-ltr" type="number" step="any" style="width: 90px" /></td>
                <td><input v-model="x.tolPlus" class="form-control form-control-sm inv-ltr" type="number" step="any" min="0" style="width: 80px" /></td>
                <td><input v-model="x.tolMinus" class="form-control form-control-sm inv-ltr" type="number" step="any" min="0" style="width: 80px" /></td>
                <td class="inv-ltr small">{{ num((Number(x.nominal) || 0) - (Number(x.tolMinus) || 0)) }} … {{ num((Number(x.nominal) || 0) + (Number(x.tolPlus) || 0)) }}</td>
                <td><input v-model="x.unit" class="form-control form-control-sm inv-ltr" style="width: 60px" /></td>
                <td><select v-model.number="x.class" class="form-select form-select-sm"><option v-for="(t, k) in charClasses" :key="k" :value="Number(k)">{{ t }}</option></select></td>
                <td><select v-model.number="x.instrumentClass" class="form-select form-select-sm"><option v-for="(t, k) in instrumentClasses" :key="k" :value="Number(k)">{{ t }}</option></select></td>
                <td><input v-model="x.method" class="form-control form-control-sm" /></td>
                <td><input v-model="x.samplePercent" class="form-control form-control-sm inv-ltr" type="number" min="0" max="100" placeholder="پیش‌فرض رده" style="width: 90px" /></td>
                <td><input v-model="x.operationSeq" class="form-control form-control-sm inv-ltr" type="number" min="1" style="width: 70px" /></td>
                <td><input v-model="x.stageCode2" class="form-control form-control-sm inv-ltr" maxlength="2" style="width: 55px" /></td>
                <td><button class="btn btn-sm btn-outline-danger" @click="list.items.splice(i, 1)">حذف</button></td>
              </template>
              <template v-else>
                <td class="inv-ltr">{{ x.no }}</td><td>{{ x.name }}</td><td class="inv-ltr">{{ num(x.nominal) }}</td><td class="inv-ltr">+{{ num(x.tolPlus) }}</td><td class="inv-ltr">-{{ num(x.tolMinus) }}</td>
                <td class="inv-ltr small">{{ num(x.lower) }} … {{ num(x.upper) }}</td><td class="inv-ltr">{{ x.unit }}</td><td>{{ x.classText }}</td><td>{{ x.instrumentText }}</td><td>{{ x.method }}</td>
                <td class="inv-ltr">{{ x.samplePercent }}</td><td class="inv-ltr">{{ x.operationSeq }}</td><td class="inv-ltr">{{ x.stageCode2 }}</td><td></td>
              </template>
            </tr>
          </tbody>
        </table>
      </div>
      <button v-if="editable" class="btn btn-sm btn-outline-primary" @click="list.items.push(blankLine(list.items.length))">مشخصه جدید</button>
      <p class="inv-muted small mt-2 mb-0">رده بحرانی و عمده همیشه الزامی‌اند و پیش‌فرض نمونه‌برداری آن‌ها از تنظیمات است (بحرانی ۱۰۰٪)؛ رده جزئی فقط وقتی الزامی است که درصد نمونه‌برداری بالای صفر باشد. گام OPC همان شماره ردیف مسیر ساخت است که مشخصه در آن بازرسی می‌شود؛ خالی = بازرسی نهایی / ورودی.</p>
    </div>
    <ReasonDialog v-if="dialog" :title="dialog.title" :label="dialog.label" :busy="busy" @confirm="confirmDialog" @close="dialog = null" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
