<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getCodingValues, saveCodingValue } from "@/api/InventoryAPIs";
import { codingSchemes, fail, ok, printPage } from "@/composables/inventory";
import { hasRole, InventoryRoles } from "@/composables/roles";

// The coding tables of the procedure WI-WH-002; planning and project control keeps them.
const values = ref<any[]>([]);
const scheme = ref("PartStatus");
const editing = ref<any>(null);
const saving = ref(false);
const canEdit = hasRole(InventoryRoles.planning);

const rows = computed(() => values.value.filter((v) => v.scheme === scheme.value));

async function load() {
  const [data, error] = await getCodingValues("");
  if (error) return fail(error);
  values.value = data || [];
}

function edit(row: any = null) {
  editing.value = row
    ? { ...row }
    : {
        id: null,
        scheme: scheme.value,
        code: "",
        title: "",
        model: "",
        rangeFrom: null,
        rangeTo: null,
        isStockable: false,
        sortOrder: rows.value.length + 1,
        isActive: true,
        description: "",
      };
}

async function save() {
  saving.value = true;
  const [, error] = await saveCodingValue(editing.value);
  saving.value = false;
  if (error) return fail(error);
  editing.value = null;
  ok("ذخیره شد.");
  load();
}

onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="جدول‌های کدینگ کالا (WI-WH-002)" :subtitle="codingSchemes[scheme]" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">جدول‌های کدینگ (WI-WH-002)</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <button v-if="canEdit" class="btn btn-sm btn-primary" @click="edit()">+ مقدار جدید</button>
        </div>
      </div>

      <div class="d-flex flex-wrap gap-1 mb-3 d-print-none">
        <button
          v-for="(title, key) in codingSchemes"
          :key="key"
          class="btn btn-sm"
          :class="scheme === key ? 'btn-primary' : 'btn-outline-primary'"
          @click="scheme = String(key)">
          {{ title }}
        </button>
      </div>
      <p v-if="scheme === 'PartStatus'" class="inv-muted screen-only">
        ★ ستون «انبارشی» تصمیم موقت شرکت است: فقط این مراحل کد کالای جدا می‌گیرند و در انبار نگهداری می‌شوند. بقیه وضعیت‌ها به‌صورت عملیات مسیر ساخت روی
        سریال/بچ ثبت می‌شوند.
      </p>

      <table class="table table-sm table-hover">
        <thead>
          <tr>
            <th>کد</th>
            <th>عنوان</th>
            <th v-if="scheme === 'Assembly'">محدوده</th>
            <th v-if="scheme === 'PartStatus'">انبارشی ★</th>
            <th>توضیح</th>
            <th>فعال</th>
            <th class="d-print-none"></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in rows" :key="row.id">
            <td class="inv-ltr">{{ row.code }}</td>
            <td>{{ row.title }}</td>
            <td v-if="scheme === 'Assembly'" class="inv-ltr">{{ row.rangeFrom }} – {{ row.rangeTo }}</td>
            <td v-if="scheme === 'PartStatus'">{{ row.isStockable ? "✓" : "" }}</td>
            <td class="inv-muted">{{ row.description }}</td>
            <td>{{ row.isActive ? "✓" : "✗" }}</td>
            <td class="d-print-none">
              <button v-if="canEdit" class="btn btn-sm btn-link p-0" @click="edit(row)">ویرایش</button>
            </td>
          </tr>
          <tr v-if="!rows.length">
            <td class="text-center inv-muted" colspan="7">مقداری ثبت نشده است.</td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="editing" class="inv-modal-backdrop" @click.self="editing = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ codingSchemes[editing.scheme] }}</h2>
        <div class="row g-2">
          <div class="col-4">
            <label class="form-label small mb-0">کد *</label>
            <input v-model="editing.code" class="form-control form-control-sm inv-ltr" />
          </div>
          <div class="col-8">
            <label class="form-label small mb-0">عنوان *</label>
            <input v-model="editing.title" class="form-control form-control-sm" />
          </div>
          <template v-if="editing.scheme === 'Assembly'">
            <div class="col-4">
              <label class="form-label small mb-0">از</label>
              <input v-model.number="editing.rangeFrom" class="form-control form-control-sm" type="number" />
            </div>
            <div class="col-4">
              <label class="form-label small mb-0">تا</label>
              <input v-model.number="editing.rangeTo" class="form-control form-control-sm" type="number" />
            </div>
            <div class="col-4">
              <label class="form-label small mb-0">مدل توربین</label>
              <input v-model="editing.model" class="form-control form-control-sm" placeholder="خالی = همه" />
            </div>
          </template>
          <div v-if="editing.scheme === 'PartStatus'" class="col-12">
            <label class="form-check small">
              <input v-model="editing.isStockable" class="form-check-input" type="checkbox" />
              مرحله انبارشی (کد کالای جدا می‌گیرد) ★
            </label>
          </div>
          <div class="col-8">
            <label class="form-label small mb-0">توضیح</label>
            <input v-model="editing.description" class="form-control form-control-sm" />
          </div>
          <div class="col-2">
            <label class="form-label small mb-0">ترتیب</label>
            <input v-model.number="editing.sortOrder" class="form-control form-control-sm" type="number" />
          </div>
          <div class="col-2 d-flex align-items-end">
            <label class="form-check small">
              <input v-model="editing.isActive" class="form-check-input" type="checkbox" />
              فعال
            </label>
          </div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="editing = null">انصراف</button>
          <button :disabled="saving" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
