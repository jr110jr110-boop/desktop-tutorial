<script setup lang="ts">
import { onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { decideMapping, getMappings, importMappings, rowsOf, saveMapping, suggestMappings } from "@/api/ProductBankAPIs";
import { fail, jalali, ok, printPage } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { PbRoles, mappingSources, mappingStatuses } from "@/composables/productBank";

// Mapping of the old 14-digit warehouse codes to part numbers (+ stage): drafts come from the Excel lists or from the automatic suggestion; ENGINEERING approves, never the proposer.
const rows = ref<any[]>([]);
const status = ref<number | null>(0);
const text = ref("");
const minConfidence = ref<number | null>(null);
const total = ref(0);
const pageNumber = ref(1);
const pageSize = 50;
const busy = ref(false);
const review = ref<any>(null);
const manual = ref<any>(null);
const pasted = ref("");
const showImport = ref(false);
const canPropose = hasRole(PbRoles.coding);

async function load() {
  const [data, error] = await getMappings({ Status: status.value, Contains: text.value, MinConfidence: minConfidence.value, PageNumber: pageNumber.value, PageSize: pageSize });
  if (error) return fail(error);
  rows.value = rowsOf(data);
  total.value = data?.totalItems ?? rows.value.length;
}

async function suggest() {
  busy.value = true;
  const [data, error] = await suggestMappings();
  busy.value = false;
  if (error) return fail(error);
  ok(`${data.created} پیشنهاد جدید ساخته شد (${data.skipped} مورد رد شد).`);
  status.value = 0;
  load();
}

/** Lines of "legacy code, part number, stage, title" separated by tab or semicolon (pasted from Excel); a comma is part of a title and never splits. */
async function importPasted() {
  const lines = pasted.value.split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
  const list = lines.map((l) => {
    const p = l.split(/[\t;]/).map((x) => x.trim());
    return { legacyCode: p[0], partNumber: p[1], stageCode2: p[2] || null, legacyTitle: p[3] || null };
  });
  busy.value = true;
  const [data, error] = await importMappings(list);
  busy.value = false;
  if (error) return fail(error);
  ok(`${data.created} نگاشت پیش‌نویس ثبت شد.`);
  if (data.errors?.length) fail(data.errors.slice(0, 5).join("؛ "));
  pasted.value = "";
  showImport.value = false;
  load();
}

async function saveManual() {
  busy.value = true;
  const [, error] = await saveMapping(manual.value);
  busy.value = false;
  if (error) return fail(error);
  manual.value = null;
  ok("نگاشت پیش‌نویس ثبت شد.");
  load();
}

async function decide(approve: boolean) {
  busy.value = true;
  const [, error] = await decideMapping({ id: review.value.id, approve, note: review.value.note });
  busy.value = false;
  if (error) return fail(error);
  review.value = null;
  ok(approve ? "نگاشت تایید شد و کد قدیم روی کالا ثبت شد." : "نگاشت رد شد.");
  load();
}

const confClass = (c: number) => (c >= 80 ? "ok" : c >= 60 ? "warn" : "bad");
onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="نگاشت کدهای قدیم به شماره قطعه" :subtitle="status === null ? 'همه' : mappingStatuses[status]" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">بررسی نگاشت کد قدیم ↔ شماره قطعه</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <template v-if="canPropose">
            <button class="btn btn-sm btn-outline-primary" :disabled="busy" @click="suggest">پیشنهاد خودکار از لیست کالاها</button>
            <button class="btn btn-sm btn-outline-primary" @click="showImport = true">ورود از Excel</button>
            <button class="btn btn-sm btn-primary" @click="manual = { legacyCode: '', partNumber: '', stageCode2: '' }">+ نگاشت</button>
          </template>
        </div>
      </div>
      <p class="inv-muted screen-only">
        کد ۱۴ رقمی قدیم همچنان به‌عنوان «کد قدیم» روی کالا می‌ماند. هر نگاشت ابتدا پیش‌نویس است؛ فقط مهندسی (غیر از ثبت‌کننده) آن را تایید می‌کند. درصد اطمینان پیشنهاد
        خودکار از شباهت نام و جنس است و جایگزین نظر مهندسی نیست.
      </p>
      <div class="inv-filters mb-3 d-print-none">
        <select v-model="status" class="form-select form-select-sm" @change="pageNumber = 1; load()">
          <option :value="null">همه</option>
          <option v-for="(t, k) in mappingStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <input v-model="text" class="form-control form-control-sm" placeholder="کد قدیم، شماره قطعه یا نام" @keyup.enter="pageNumber = 1; load()" />
        <input v-model.number="minConfidence" type="number" min="0" max="100" class="form-control form-control-sm" style="max-width: 130px" placeholder="حداقل اطمینان" />
        <button class="btn btn-sm btn-outline-secondary" @click="pageNumber = 1; load()">جستجو</button>
      </div>

      <table class="table table-sm table-hover">
        <thead>
          <tr>
            <th>کد قدیم</th>
            <th>نام کالای قدیم</th>
            <th>شماره قطعه</th>
            <th>مرحله</th>
            <th>نام قطعه</th>
            <th>منبع</th>
            <th>اطمینان</th>
            <th>وضعیت</th>
            <th class="d-print-none"></th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="!rows.length"><td class="text-center inv-muted" colspan="9">موردی نیست.</td></tr>
          <tr v-for="r in rows" :key="r.id">
            <td class="inv-ltr">{{ r.legacyCode }}</td>
            <td>{{ r.legacyTitle }}<div v-if="r.legacyMaterial" class="inv-muted">{{ r.legacyMaterial }}</div></td>
            <td class="inv-ltr">{{ r.partNumber }}</td>
            <td class="inv-ltr">{{ r.stageCode2 || "-" }}</td>
            <td>{{ r.partTitle }}</td>
            <td>{{ mappingSources[r.source] }}</td>
            <td><span class="inv-badge" :class="confClass(r.confidence)">{{ Math.round(r.confidence) }}٪</span></td>
            <td>
              {{ mappingStatuses[r.status] }}
              <div v-if="r.reviewedAt" class="inv-muted">{{ jalali(r.reviewedAt) }}</div>
              <div v-if="r.reviewNote" class="inv-muted">{{ r.reviewNote }}</div>
            </td>
            <td class="d-print-none">
              <button v-if="r.status === 0 && r.canDecide" class="btn btn-sm btn-outline-primary py-0" @click="review = { ...r, note: '' }">بررسی</button>
            </td>
          </tr>
        </tbody>
      </table>
      <div class="d-flex gap-2 d-print-none align-items-center">
        <button class="btn btn-sm btn-outline-secondary" :disabled="pageNumber <= 1" @click="pageNumber--; load()">قبلی</button>
        <span class="small">صفحه {{ pageNumber }} · {{ total }} مورد</span>
        <button class="btn btn-sm btn-outline-secondary" :disabled="pageNumber * pageSize >= total" @click="pageNumber++; load()">بعدی</button>
      </div>
    </div>

    <div v-if="review" class="inv-modal-backdrop" @click.self="review = null">
      <div class="inv-modal" style="max-width: 600px">
        <h2 class="inv-title mb-2">بررسی نگاشت</h2>
        <p class="small mb-1"><span class="inv-ltr">{{ review.legacyCode }}</span> — {{ review.legacyTitle }}</p>
        <p class="small mb-2">
          ← <span class="inv-ltr fw-bold">{{ review.partNumber }}{{ review.stageCode2 ? " (" + review.stageCode2 + ")" : "" }}</span> — {{ review.partTitle }}
          <span class="inv-badge" :class="confClass(review.confidence)">{{ Math.round(review.confidence) }}٪</span>
        </p>
        <label class="form-label small mb-0">توضیح (برای رد الزامی)</label>
        <input v-model="review.note" class="form-control form-control-sm" />
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="review = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="decide(false)">رد</button>
          <button :disabled="busy" class="btn btn-sm btn-success" @click="decide(true)">تایید نگاشت</button>
        </div>
      </div>
    </div>

    <div v-if="manual" class="inv-modal-backdrop" @click.self="manual = null">
      <div class="inv-modal" style="max-width: 520px">
        <h2 class="inv-title mb-2">نگاشت دستی</h2>
        <label class="form-label small mb-0">کد قدیم (۱۴ رقم) *</label>
        <input v-model="manual.legacyCode" class="form-control form-control-sm inv-ltr" />
        <label class="form-label small mb-0 mt-2">شماره قطعه *</label>
        <input v-model="manual.partNumber" class="form-control form-control-sm inv-ltr" placeholder="16.11.01.001.2" />
        <label class="form-label small mb-0 mt-2">کد مرحله (اختیاری)</label>
        <input v-model="manual.stageCode2" class="form-control form-control-sm inv-ltr" maxlength="2" />
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="manual = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="saveManual">ثبت پیش‌نویس</button>
        </div>
      </div>
    </div>

    <div v-if="showImport" class="inv-modal-backdrop" @click.self="showImport = false">
      <div class="inv-modal" style="max-width: 640px">
        <h2 class="inv-title mb-2">ورود نگاشت‌ها از Excel</h2>
        <p class="small inv-muted">هر سطر: کد قدیم ، شماره قطعه ، کد مرحله (اختیاری) ، نام کالای قدیم (اختیاری) — جدا شده با Tab (چسباندن از Excel)، نقطه‌ویرگول یا ویرگول.</p>
        <textarea v-model="pasted" class="form-control form-control-sm inv-ltr" rows="10"></textarea>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="showImport = false">انصراف</button>
          <button :disabled="busy || !pasted.trim()" class="btn btn-sm btn-primary" @click="importPasted">ثبت پیش‌نویس‌ها</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
