<script setup lang="ts">
// Tender (before P.O.) document register (E1, E2): packages with their own numbering series; the package of a won tender is linked to the project.
import { onMounted, ref } from "vue";
import { getTender, getTenders, saveTender, saveTenderDocument, tenderAction } from "@/api/EdmsAPIs";
import { getContracts, rowsOf as invRows } from "@/api/InventoryAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, ok } from "@/composables/inventory";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

const list = ref<any[]>([]);
const cur = ref<any>(null);
const pkgForm = ref<any>(null);
const docForm = ref<any>(null);
const contracts = ref<any[]>([]);
const contractId = ref<number | null>(null);
const canWrite = hasRole(ProjectControlRoles.tenderWriters);
const statusText: Record<number, string> = { 1: "باز", 2: "برنده", 3: "بازنده", 4: "لغو" };

async function load() {
  const [d, e] = await getTenders({ pageSize: 100 });
  if (e) return fail(e);
  list.value = rowsOf(d);
}
async function open(id: number) {
  const [d, e] = await getTender(id);
  if (e) return fail(e);
  cur.value = d;
}
async function savePkg() {
  const [d, e] = await saveTender(pkgForm.value);
  if (e) return fail(e);
  ok("ذخیره شد.");
  pkgForm.value = null;
  await load();
  cur.value = d;
}
async function saveDoc() {
  const [d, e] = await saveTenderDocument({ ...docForm.value, packageId: cur.value.id });
  if (e) return fail(e);
  ok("ذخیره شد.");
  docForm.value = null;
  cur.value = d;
}
async function act(action: string, extra: any = {}, needReason = false) {
  let reason: string | null = null;
  if (needReason) { reason = window.prompt("علت (حداقل ۳ حرف):"); if (!reason) return; }
  const [d, e] = await tenderAction({ id: cur.value.id, action, reason, ...extra });
  if (e) return fail(e);
  ok("انجام شد.");
  cur.value = d;
  load();
}
onMounted(async () => {
  load();
  const [c] = await getContracts();
  contracts.value = invRows(c);
});
</script>

<template>
  <div class="row g-3">
    <div class="col-lg-4">
      <button v-if="canWrite" class="btn btn-sm btn-primary mb-2" @click="pkgForm = { title: '', tenderCode: '', note: '' }">بسته‌ی مناقصه‌ی جدید</button>
      <table class="table table-sm">
        <thead><tr><th>کد</th><th>عنوان</th><th>وضعیت</th><th>سند</th></tr></thead>
        <tbody>
          <tr v-if="!list.length"><td colspan="4" class="text-center inv-muted">بسته‌ای نیست.</td></tr>
          <tr v-for="p in list" :key="p.id" :class="{ 'table-active': cur?.id === p.id }" style="cursor: pointer" @click="open(p.id)"><td class="inv-ltr">{{ p.code }}</td><td>{{ p.title }}</td><td>{{ statusText[p.status] }}</td><td>{{ p.documentCount }}</td></tr>
        </tbody>
      </table>
    </div>
    <div v-if="cur" class="col-lg-8">
      <div class="d-flex flex-wrap justify-content-between align-items-start gap-2">
        <div>
          <h3 class="h6 mb-0"><span class="inv-ltr">{{ cur.code }}</span> — {{ cur.title }}</h3>
          <span class="small inv-muted">{{ statusText[cur.status] }}<template v-if="cur.contractTitle"> · پروژه: {{ cur.contractTitle }} ({{ jalali(cur.linkedAt) }})</template></span>
        </div>
        <div v-if="canWrite" class="d-flex flex-wrap gap-1">
          <button v-if="cur.status === 1" class="btn btn-sm btn-outline-primary" @click="docForm = { title: '', discipline: '', documentType: '', revision: '00' }">سند جدید</button>
          <button v-if="cur.status === 1" class="btn btn-sm btn-outline-success" @click="act('win')">برنده شدیم</button>
          <button v-if="cur.status === 1" class="btn btn-sm btn-outline-secondary" @click="act('lose', {}, true)">نبردیم</button>
          <button v-if="cur.status === 1" class="btn btn-sm btn-outline-danger" @click="act('cancel', {}, true)">لغو</button>
          <template v-if="cur.status === 2 && !cur.contractId"><select v-model="contractId" class="form-select form-select-sm" style="width: 220px"><option :value="null">پروژه‌ی حاصل…</option><option v-for="c in contracts" :key="c.id" :value="c.id">{{ c.code }} - {{ c.title }}</option></select><button :disabled="!contractId" class="btn btn-sm btn-primary" @click="act('link-contract', { contractId })">اتصال به پروژه</button></template>
        </div>
      </div>
      <table class="table table-sm mt-2">
        <thead><tr><th>شماره</th><th>عنوان</th><th>رشته</th><th>نوع</th><th>ویرایش</th><th>صدور</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!cur.documents.length"><td colspan="7" class="text-center inv-muted">سندی نیست.</td></tr>
          <tr v-for="d in cur.documents" :key="d.id" :class="{ 'text-decoration-line-through text-muted': d.voided }">
            <td class="inv-ltr">{{ d.number }}</td><td>{{ d.title }}<div v-if="d.voidReason" class="small text-danger">ابطال: {{ d.voidReason }}</div></td><td>{{ d.discipline }}</td><td>{{ d.documentType }}</td><td class="inv-ltr">{{ d.revision }}</td><td>{{ jalali(d.issuedAt) }}</td>
            <td class="text-nowrap"><template v-if="canWrite && cur.status === 1 && !d.voided"><button v-if="!d.issuedAt" class="btn btn-sm btn-outline-success me-1" @click="act('issue', { documentId: d.id })">صدور</button><button v-if="!d.issuedAt" class="btn btn-sm btn-outline-secondary me-1" @click="docForm = { ...d }">ویرایش</button><button class="btn btn-sm btn-outline-danger" @click="act('void', { documentId: d.id }, true)">ابطال</button></template></td>
          </tr>
        </tbody>
      </table>
    </div>
    <div v-if="pkgForm" class="inv-modal-backdrop" @click.self="pkgForm = null"><div class="inv-modal"><h3 class="h6">بسته‌ی مناقصه</h3>
      <input v-model="pkgForm.title" class="form-control form-control-sm mb-2" placeholder="عنوان" /><input v-model="pkgForm.tenderCode" class="form-control form-control-sm inv-ltr mb-2" placeholder="کد مناقصه (اختیاری)" /><textarea v-model="pkgForm.note" class="form-control form-control-sm" rows="2" placeholder="یادداشت" />
      <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-secondary" @click="pkgForm = null">انصراف</button><button :disabled="!pkgForm.title" class="btn btn-sm btn-primary" @click="savePkg">ذخیره</button></div></div></div>
    <div v-if="docForm" class="inv-modal-backdrop" @click.self="docForm = null"><div class="inv-modal"><h3 class="h6">سند مناقصه</h3>
      <input v-model="docForm.title" class="form-control form-control-sm mb-2" placeholder="عنوان" /><input v-model="docForm.discipline" class="form-control form-control-sm mb-2" placeholder="رشته" /><input v-model="docForm.documentType" class="form-control form-control-sm mb-2" placeholder="نوع سند" /><input v-model="docForm.revision" class="form-control form-control-sm inv-ltr" placeholder="ویرایش" />
      <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-secondary" @click="docForm = null">انصراف</button><button :disabled="!docForm.title" class="btn btn-sm btn-primary" @click="saveDoc">ذخیره</button></div></div></div>
  </div>
</template>
