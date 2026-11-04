<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { downloadExcelTemplate, getWarehouses, importExcel } from "@/api/InventoryAPIs";
import { fail, num, ok, today } from "@/composables/inventory";

// PB5: Excel templates and import of receipts, issues and counts: check the rows (preview), then make ONE draft document. The import never confirms anything.
const kinds: Record<string, string> = { receipt: "رسید", issue: "حواله", count: "شمارش (انبارگردانی)", items: "کالاها (فقط قالب)", opening: "موجودی اول دوره (فقط قالب)" };
const kind = ref("receipt");
const warehouses = ref<any[]>([]);
const fields = ref<any>({ WarehouseId: null, DocumentDate: today(), ProviderId: null, MaterialRequestId: null, Description: "" });
const file = ref<File | null>(null);
const result = ref<any>(null);
const router = useRouter();
const importable = () => ["receipt", "issue", "count"].includes(kind.value);

function chooseFile(e: Event) {
  file.value = (e.target as HTMLInputElement).files?.[0] || null;
  result.value = null;
}

async function template() {
  const message = await downloadExcelTemplate(kind.value);
  if (message) fail(message);
}

async function run(preview: boolean) {
  if (!file.value) return fail("فایل Excel را انتخاب کنید.");
  const [data, error] = await importExcel({ Kind: kind.value, ...fields.value, Preview: preview }, file.value);
  if (error) return fail(error);
  result.value = data;
  if (!preview) {
    ok("پیش‌نویس ساخته شد؛ بررسی و تایید با شماست.");
    if (data.draftKind === "count" && data.draftId) router.push({ name: "InventoryStockCountEdit", params: { id: data.draftId } });
    else if (data.draftId) router.push({ name: "InventoryDocumentEdit", params: { id: data.draftId } });
  }
}

onMounted(async () => {
  const [w] = await getWarehouses();
  warehouses.value = w || [];
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">قالب و ورود از Excel</h1></div>
      <div class="inv-filters mb-3">
        <select v-model="kind" class="form-select form-select-sm" @change="result = null">
          <option v-for="(t, k) in kinds" :key="k" :value="k">{{ t }}</option>
        </select>
        <button class="btn btn-sm btn-outline-secondary" @click="template">دریافت قالب</button>
      </div>
      <template v-if="importable()">
        <div class="inv-filters mb-3">
          <select v-model="fields.WarehouseId" class="form-select form-select-sm">
            <option :value="null">انبار</option>
            <option v-for="w in warehouses" :key="w.id" :value="w.id">{{ w.title }}</option>
          </select>
          <input v-model="fields.DocumentDate" class="form-control form-control-sm inv-ltr" type="date" />
          <input v-if="kind === 'receipt'" v-model.number="fields.ProviderId" class="form-control form-control-sm inv-ltr" placeholder="شناسه تامین‌کننده" type="number" />
          <input v-if="kind === 'issue'" v-model.number="fields.MaterialRequestId" class="form-control form-control-sm inv-ltr" placeholder="شناسه درخواست مواد (الزامی)" type="number" />
          <input v-model="fields.Description" class="form-control form-control-sm" placeholder="توضیح" />
          <input accept=".xlsx" class="form-control form-control-sm" type="file" @change="chooseFile" />
          <button class="btn btn-sm btn-secondary" @click="run(true)">بررسی ردیف‌ها</button>
          <button :disabled="!result || result.errorRows > 0" class="btn btn-sm btn-primary" @click="run(false)">ساخت پیش‌نویس</button>
        </div>
      </template>
      <p v-else class="inv-muted small">کالاها و موجودی اول دوره از صفحه‌های خودشان وارد می‌شوند؛ این قالب را پر کنید و همان‌جا بارگذاری کنید.</p>

      <div v-if="result">
        <p>{{ num(result.rowCount) }} ردیف · درست {{ num(result.validRows) }} · خطادار {{ num(result.errorRows) }}</p>
        <ul v-if="result.warnings?.length" class="small inv-muted"><li v-for="w in result.warnings" :key="w">{{ w }}</li></ul>
        <table class="table table-sm">
          <thead><tr><th>ردیف فایل</th><th>کد</th><th>شرح</th><th>مقدار</th><th>قیمت</th><th>نتیجه</th></tr></thead>
          <tbody>
            <tr v-for="r in result.rows" :key="r.row" :class="{ 'table-danger': !r.ok }">
              <td>{{ r.row }}</td><td class="inv-ltr">{{ r.itemCode }}</td><td>{{ r.itemTitle }}</td>
              <td class="text-end inv-ltr">{{ num(r.quantity) }}</td><td class="text-end inv-ltr">{{ num(r.unitPrice) }}</td><td>{{ r.ok ? "درست" : r.message }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>
