<script setup lang="ts">
// Engineering document control (EDMS): documents of a part / valve / tag, the tag register, my release notices, distribution lists and the approver matrix, and the tender register.
import { onMounted, ref, watch } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import EdmsDocPanel from "@/components/pc/edms/EdmsDocPanel.vue";
import TenderRegister from "@/components/pc/edms/TenderRegister.vue";
import { acknowledgeNotice, getApprovers, getDistribution, getNotices, getPartDocuments, getTags, saveApprover, saveDistribution, saveTag } from "@/api/EdmsAPIs";
import { downloadFile } from "@/api/InventoryAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, ok } from "@/composables/inventory";
import { pcContractId } from "@/composables/projectControl";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

const tab = ref("part");
const tabs = [["part", "اسناد یک قطعه / تگ"], ["tags", "ثبت تگ تجهیز"], ["notices", "ابلاغ‌های انتشار من"], ["distribution", "فهرست توزیع"], ["matrix", "ماتریس تایید رشته‌ها"], ["tender", "مدارک مناقصه"]];
const canMatrix = hasRole(ProjectControlRoles.edmsMatrix);
const canManage = hasRole(ProjectControlRoles.edmsManagers);
const canTag = hasRole(ProjectControlRoles.dcc);
const statusText: Record<number, string> = { 1: "پیش‌نویس", 2: "در حال بررسی", 3: "تاییدشده", 4: "منتشرشده", 5: "جایگزین‌شده", 6: "منسوخ", 7: "As-Built" };

// ---- part / tag
const q = ref({ partNumber: "", stageCode2: "", tagId: null as number | null, itemId: null as number | null });
const found = ref<any[] | null>(null);
const panel = ref<number | null>(null);
async function search() {
  const [d, e] = await getPartDocuments(q.value);
  if (e) return fail(e);
  found.value = d || [];
}

// ---- tags
const tags = ref<any[]>([]);
const tagForm = ref<any>(null);
async function loadTags() {
  if (!pcContractId.value) { tags.value = []; return; }
  const [d, e] = await getTags(pcContractId.value);
  if (e) return fail(e);
  tags.value = d || [];
}
async function saveTagForm() {
  const [, e] = await saveTag({ ...tagForm.value, contractId: pcContractId.value });
  if (e) return fail(e);
  ok("ذخیره شد.");
  tagForm.value = null;
  loadTags();
}

// ---- notices
const notices = ref<any[]>([]);
async function loadNotices() {
  const [d, e] = await getNotices({ mineOnly: true, openOnly: false, pageSize: 100 });
  if (e) return fail(e);
  notices.value = rowsOf(d);
}
async function ack(n: any) {
  const [, e] = await acknowledgeNotice(n.id);
  if (e) return fail(e);
  ok("دریافت تایید شد.");
  loadNotices();
}

// ---- distribution and matrix
const dist = ref<any[]>([]);
const appr = ref<any[]>([]);
const form = ref<any>(null);
async function loadLists() {
  const [d, e1] = await getDistribution();
  if (e1) return fail(e1);
  dist.value = d || [];
  const [a, e2] = await getApprovers();
  if (e2) return fail(e2);
  appr.value = a || [];
}
async function saveList() {
  const f = form.value;
  const [, e] = await (f.matrix ? saveApprover(f) : saveDistribution(f));
  if (e) return fail(e);
  ok("ذخیره شد.");
  form.value = null;
  loadLists();
}
function refresh() {
  if (tab.value === "tags") loadTags();
  else if (tab.value === "notices") loadNotices();
  else if (tab.value === "distribution" || tab.value === "matrix") loadLists();
}
onMounted(refresh);
watch([tab, pcContractId], refresh);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="کنترل مدارک مهندسی (EDMS)" subtitle="" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <div><h1 class="inv-title screen-only">کنترل مدارک مهندسی</h1><span class="inv-muted d-print-none">بازنگری‌ها، پیوند اسناد به قطعه و تگ، توزیع و ابلاغ انتشار، ماتریس تایید و مدارک مناقصه</span></div>
        <ContractSelect allow-all class="d-print-none" />
      </div>
      <div class="d-flex flex-wrap gap-1 mb-3 d-print-none"><button v-for="t in tabs" :key="t[0]" class="btn btn-sm" :class="tab === t[0] ? 'btn-primary' : 'btn-outline-secondary'" @click="tab = t[0]">{{ t[1] }}</button></div>

      <div v-if="tab === 'part'">
        <div class="inv-filters mb-2">
          <input v-model="q.partNumber" class="form-control form-control-sm inv-ltr" placeholder="شماره قطعه (BOM)" />
          <input v-model="q.stageCode2" class="form-control form-control-sm inv-ltr" maxlength="2" placeholder="مرحله (اختیاری)" style="max-width: 130px" />
          <select v-model="q.tagId" class="form-select form-select-sm" @focus="loadTags"><option :value="null">تگ تجهیز…</option><option v-for="t in tags" :key="t.id" :value="t.id">{{ t.code }}</option></select>
          <button class="btn btn-sm btn-primary" @click="search">نمایش اسناد</button>
        </div>
        <div v-if="found" class="table-responsive">
          <table class="table table-sm">
            <thead><tr><th>شماره</th><th>عنوان</th><th>رشته</th><th>ارتباط</th><th>بازنگری منتشرشده</th><th>آخرین وضعیت</th><th>فایل‌ها</th><th></th></tr></thead>
            <tbody>
              <tr v-if="!found.length"><td colspan="8" class="text-center inv-muted">سندی به این قطعه یا تگ وصل نیست.</td></tr>
              <tr v-for="(d, i) in found" :key="i">
                <td class="inv-ltr">{{ d.documentNumber }}</td><td>{{ d.title }}</td><td>{{ d.discipline }}</td><td class="small">{{ d.viaText }}</td>
                <td><b v-if="d.releasedCode" class="inv-ltr">{{ d.releasedCode }}</b><span v-else class="text-danger small">منتشر نشده</span> <span class="small">{{ jalali(d.releasedAt) }}</span></td>
                <td>{{ d.latestStatus ? statusText[d.latestStatus] : "—" }}</td>
                <td class="small"><div v-for="f in d.files" :key="f.id"><a href="#" @click.prevent="downloadFile(f.documentId, f.fileName)">{{ f.fileName }}</a></div></td>
                <td><button v-if="d.deliverableId" class="btn btn-sm btn-outline-secondary" @click="panel = d.deliverableId">بازنگری‌ها</button></td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <div v-else-if="tab === 'tags'">
        <button v-if="canTag && pcContractId" class="btn btn-sm btn-primary mb-2" @click="tagForm = { code: '', title: '', itemId: null, workOrderId: null, isActive: true }">تگ جدید</button>
        <p v-if="!pcContractId" class="inv-muted">برای دیدن تگ‌ها یک پروژه انتخاب کنید.</p>
        <table v-else class="table table-sm">
          <thead><tr><th>تگ</th><th>عنوان</th><th>کالا</th><th>دستور ساخت</th><th>تعداد سند</th><th>فعال</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!tags.length"><td colspan="7" class="text-center inv-muted">تگی ثبت نشده است.</td></tr>
            <tr v-for="t in tags" :key="t.id"><td class="inv-ltr"><b>{{ t.code }}</b></td><td>{{ t.title }}</td><td class="inv-ltr">{{ t.itemCode }}</td><td class="inv-ltr">{{ t.workOrderCode }}</td><td>{{ t.documentCount }}</td><td>{{ t.isActive ? "بله" : "خیر" }}</td><td><button v-if="canTag" class="btn btn-sm btn-outline-secondary" @click="tagForm = { ...t }">ویرایش</button></td></tr>
          </tbody>
        </table>
      </div>

      <div v-else-if="tab === 'notices'">
        <table class="table table-sm">
          <thead><tr><th>سند</th><th>بازنگری</th><th>ابلاغ</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!notices.length"><td colspan="5" class="text-center inv-muted">ابلاغی نیست.</td></tr>
            <tr v-for="n in notices" :key="n.id">
              <td><span class="inv-ltr">{{ n.documentNumber }}</span> {{ n.title }}</td><td class="inv-ltr">{{ n.revisionCode }}</td><td>{{ jalali(n.notifiedAt, true) }}</td>
              <td><span v-if="n.acknowledgedAt" class="inv-badge ok">تایید شد {{ jalali(n.acknowledgedAt) }}</span><span v-else class="inv-badge" :class="n.overdue ? 'bad' : 'warn'">{{ n.overdue ? "دیرکرد" : "منتظر تایید دریافت" }}</span></td>
              <td><button v-if="!n.acknowledgedAt" class="btn btn-sm btn-outline-success" @click="ack(n)">دریافت شد</button></td>
            </tr>
          </tbody>
        </table>
      </div>

      <div v-else-if="tab === 'distribution'">
        <button v-if="canManage" class="btn btn-sm btn-primary mb-2" @click="form = { matrix: false, discipline: '', userId: null, contractId: null, isActive: true }">ردیف جدید</button>
        <table class="table table-sm">
          <thead><tr><th>رشته</th><th>کاربر</th><th>پروژه</th><th>فعال</th></tr></thead>
          <tbody><tr v-if="!dist.length"><td colspan="4" class="text-center inv-muted">فهرستی ثبت نشده است.</td></tr><tr v-for="r in dist" :key="r.id"><td>{{ r.discipline }}</td><td>{{ r.userName }}</td><td>{{ r.contractId || "همه" }}</td><td>{{ r.isActive ? "بله" : "خیر" }}</td></tr></tbody>
        </table>
      </div>

      <div v-else-if="tab === 'matrix'">
        <p class="inv-muted small">اگر برای رشته‌ای ردیف تعریف شود، فقط همین افراد بررسی و تایید می‌کنند (بدون نیاز به DCC)؛ تهیه‌کننده، بررسی‌کننده و تاییدکننده همیشه سه نفر جدا هستند. ویرایش با PMO است.</p>
        <button v-if="canMatrix" class="btn btn-sm btn-primary mb-2" @click="form = { matrix: true, discipline: '', userId: null, contractId: null, canReview: true, canApprove: false, isActive: true }">ردیف جدید</button>
        <table class="table table-sm">
          <thead><tr><th>رشته</th><th>کاربر</th><th>بررسی</th><th>تایید</th><th>پروژه</th><th>فعال</th></tr></thead>
          <tbody><tr v-if="!appr.length"><td colspan="6" class="text-center inv-muted">ماتریسی ثبت نشده است؛ نقش‌های پیش‌فرض اعمال می‌شود.</td></tr><tr v-for="r in appr" :key="r.id"><td>{{ r.discipline }}</td><td>{{ r.userName }}</td><td>{{ r.canReview ? "بله" : "—" }}</td><td>{{ r.canApprove ? "بله" : "—" }}</td><td>{{ r.contractId || "همه" }}</td><td>{{ r.isActive ? "بله" : "خیر" }}</td></tr></tbody>
        </table>
      </div>

      <TenderRegister v-else-if="tab === 'tender'" />
    </div>

    <EdmsDocPanel v-if="panel" :deliverable-id="panel" @close="panel = null" @changed="search" />
    <div v-if="tagForm" class="inv-modal-backdrop" @click.self="tagForm = null">
      <div class="inv-modal">
        <h3 class="h6">{{ tagForm.id ? "ویرایش تگ" : "تگ جدید" }}</h3>
        <input v-model="tagForm.code" class="form-control form-control-sm inv-ltr mb-2" placeholder="کد تگ (مثلا PV-101)" />
        <input v-model="tagForm.title" class="form-control form-control-sm mb-2" placeholder="عنوان" />
        <input v-model.number="tagForm.itemId" type="number" class="form-control form-control-sm mb-2" placeholder="شناسه‌ی کالا (اختیاری)" />
        <input v-model.number="tagForm.workOrderId" type="number" class="form-control form-control-sm mb-2" placeholder="شناسه‌ی دستور ساخت (اختیاری)" />
        <label class="form-check small"><input v-model="tagForm.isActive" class="form-check-input" type="checkbox" /> فعال</label>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-secondary" @click="tagForm = null">انصراف</button><button :disabled="!tagForm.code" class="btn btn-sm btn-primary" @click="saveTagForm">ذخیره</button></div>
      </div>
    </div>
    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h3 class="h6">{{ form.matrix ? "ماتریس تایید" : "فهرست توزیع" }}</h3>
        <input v-model="form.discipline" class="form-control form-control-sm mb-2" placeholder="رشته (مثلا Mechanical)" />
        <input v-model.number="form.userId" type="number" class="form-control form-control-sm mb-2" placeholder="شناسه‌ی کاربر" />
        <input v-model.number="form.contractId" type="number" class="form-control form-control-sm mb-2" placeholder="شناسه‌ی قرارداد (خالی: همه)" />
        <template v-if="form.matrix"><label class="form-check small"><input v-model="form.canReview" class="form-check-input" type="checkbox" /> بررسی</label><label class="form-check small"><input v-model="form.canApprove" class="form-check-input" type="checkbox" /> تایید</label></template>
        <label class="form-check small"><input v-model="form.isActive" class="form-check-input" type="checkbox" /> فعال</label>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-secondary" @click="form = null">انصراف</button><button :disabled="!form.discipline || !form.userId" class="btn btn-sm btn-primary" @click="saveList">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
