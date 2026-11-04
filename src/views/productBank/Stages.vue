<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { adoptPartNumbers, getSettings, getStages, previewCode, saveSetting, saveStage } from "@/api/ProductBankAPIs";
import { fail, ok, printPage } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { PbRoles, stageKinds, stageSymbols } from "@/composables/productBank";

// Stage dictionary (OPC stage codes) and the settings of the part-number coding. The stage item of a part has the code "part number + separator + stage".
const stages = ref<any[]>([]);
const settings = ref<any[]>([]);
const form = ref<any>(null);
const saving = ref(false);
const showInactive = ref(false);
const canEdit = hasRole(PbRoles.planning);
const isAdmin = hasRole(PbRoles.admin);
async function adopt() {
  const [data, error] = await adoptPartNumbers();
  if (error) return fail(error);
  if (data.alreadyDone) return ok("این مرحله قبلاً انجام شده است.");
  ok(`${data.adopted} کالا شماره قطعه گرفت.` + (data.conflicts?.length ? " تعارض‌ها: " + data.conflicts.slice(0, 5).join("؛ ") : ""));
}
const sample = ref<any>(null);
const separator = computed(() => settings.value.find((s) => s.key === "StageSeparator")?.value ?? "-");

async function load() {
  const [list, error] = await getStages(true);
  if (error) return fail(error);
  stages.value = list || [];
  const [s] = await getSettings();
  settings.value = s || [];
  const [p] = await previewCode("16.11.02.001.2", "RL", "Shaft");
  sample.value = p;
}

function edit(row: any | null) {
  form.value = row
    ? { ...row }
    : { id: null, code: "", titleFa: "", titleEn: "", kind: 1, isStockable: false, isActive: true, sortOrder: (stages.value.at(-1)?.sortOrder || 0) + 1 };
}

async function save() {
  saving.value = true;
  const [, error] = await saveStage({ ...form.value, code: String(form.value.code || "").toUpperCase() });
  saving.value = false;
  if (error) return fail(error);
  form.value = null;
  ok("مرحله ذخیره شد.");
  load();
}

async function saveSep(item: any) {
  const [, error] = await saveSetting({ key: item.key, value: item.value });
  if (error) return fail(error);
  ok("تنظیم ذخیره شد.");
  load();
}

const rows = computed(() => stages.value.filter((s) => showInactive.value || s.isActive));
onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="فهرست مراحل OPC" subtitle="کد دو حرفی مراحل ساخت" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">فهرست مراحل OPC و تنظیم کدینگ شماره قطعه</h1>
        <div class="d-flex gap-2 d-print-none">
          <label class="small d-flex align-items-center gap-1"><input v-model="showInactive" type="checkbox" /> نمایش غیرفعال‌ها</label>
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <button v-if="canEdit" class="btn btn-sm btn-primary" @click="edit(null)">+ مرحله</button>
          <button v-if="isAdmin" class="btn btn-sm btn-outline-warning" @click="adopt">ثبت شماره قطعه برای کالاهای قدیمی (یک‌بار)</button>
        </div>
      </div>
      <p class="inv-muted screen-only">
        کد کالای هر مرحله انبارشی از یک قطعه = «شماره قطعه + جداکننده + کد مرحله» است؛ مثلا
        <span class="inv-ltr">{{ sample?.code }}</span>
        ({{ sample?.title }}). ★ فهرست مراحل و جداکننده پیشنهاد برنامه است و باید شرکت تایید کند. مرحله‌ای که «انبارشی» باشد برای هر قطعه کالای جدا می‌گیرد.
      </p>

      <div v-for="s in settings" :key="s.key" class="d-flex gap-2 align-items-center mb-2">
        <span class="small" style="min-width: 260px">{{ s.title }}</span>
        <input v-model="s.value" class="form-control form-control-sm inv-ltr" style="max-width: 120px" :disabled="!canEdit" />
        <button v-if="canEdit" class="btn btn-sm btn-outline-primary py-0" @click="saveSep(s)">ذخیره</button>
        <span class="inv-muted small">{{ s.source }}</span>
      </div>

      <table class="table table-sm table-hover mt-3">
        <thead>
          <tr>
            <th>کد</th>
            <th>نماد</th>
            <th>عنوان فارسی</th>
            <th>عنوان انگلیسی</th>
            <th>نوع</th>
            <th>انبارشی</th>
            <th>فعال</th>
            <th class="d-print-none"></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="s in rows" :key="s.id" :class="{ 'inv-muted': !s.isActive }">
            <td class="inv-ltr fw-bold">{{ s.code }}</td>
            <td>{{ stageSymbols[s.kind] }}</td>
            <td>{{ s.titleFa }}</td>
            <td class="inv-ltr">{{ s.titleEn }}</td>
            <td>{{ stageKinds[s.kind] }}</td>
            <td>{{ s.isStockable ? "بله" : "-" }}</td>
            <td>{{ s.isActive ? "بله" : "خیر" }}</td>
            <td class="d-print-none"><button v-if="canEdit" class="btn btn-sm btn-outline-primary py-0" @click="edit(s)">ویرایش</button></td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal" style="max-width: 560px">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش مرحله" : "مرحله جدید" }}</h2>
        <div class="row g-2">
          <div class="col-4">
            <label class="form-label small mb-0">کد (دو حرف) *</label>
            <input v-model="form.code" class="form-control form-control-sm inv-ltr" maxlength="2" :disabled="!!form.id" />
          </div>
          <div class="col-8">
            <label class="form-label small mb-0">نوع</label>
            <select v-model="form.kind" class="form-select form-select-sm">
              <option v-for="(t, k) in stageKinds" :key="k" :value="Number(k)">{{ stageSymbols[Number(k)] }} {{ t }}</option>
            </select>
          </div>
          <div class="col-6">
            <label class="form-label small mb-0">عنوان فارسی *</label>
            <input v-model="form.titleFa" class="form-control form-control-sm" />
          </div>
          <div class="col-6">
            <label class="form-label small mb-0">عنوان انگلیسی</label>
            <input v-model="form.titleEn" class="form-control form-control-sm inv-ltr" />
          </div>
          <div class="col-4"><label class="small"><input v-model="form.isStockable" type="checkbox" /> انبارشی</label></div>
          <div class="col-4"><label class="small"><input v-model="form.isActive" type="checkbox" /> فعال</label></div>
          <div class="col-4">
            <input v-model.number="form.sortOrder" type="number" class="form-control form-control-sm" placeholder="ترتیب" />
          </div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button :disabled="saving" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
