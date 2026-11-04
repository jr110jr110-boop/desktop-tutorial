<script setup lang="ts">
import { onMounted, ref } from "vue";
import { deleteCeiling, getAssets, getCeilings, getConversions, getWarehouses, saveAsset, saveCeiling, saveConversion } from "@/api/InventoryAPIs";
import { assetStatuses, fail, jalali, ok, openLabels, labelCode, rial } from "@/composables/inventory";
import { useRouter } from "vue-router";

// PB5: assets register (with "A/" labels), unit conversions (used by the project balance) and consumption ceilings per project.
const tab = ref<"assets" | "conversions" | "ceilings">("assets");
const router = useRouter();
const warehouses = ref<any[]>([]);
const assets = ref<any[]>([]);
const conversions = ref<any[]>([]);
const ceilings = ref<any[]>([]);
const asset = ref<any>({ tag: "", title: "", warehouseId: null, cost: null, status: 1 });
const conversion = ref<any>({ itemId: null, fromUnit: "", toUnit: "", factor: null });
const ceiling = ref<any>({ itemId: null, workOrderId: null, maxQuantity: null, note: "" });

async function load() {
  if (tab.value === "assets") {
    const [d, e] = await getAssets({ PageNumber: 1, PageSize: 100 });
    if (e) return fail(e);
    assets.value = d?.data || d || [];
  } else if (tab.value === "conversions") {
    const [d, e] = await getConversions();
    if (e) return fail(e);
    conversions.value = d || [];
  } else {
    const [d, e] = await getCeilings();
    if (e) return fail(e);
    ceilings.value = d || [];
  }
}

function show(t: "assets" | "conversions" | "ceilings") {
  tab.value = t;
  load();
}

async function addAsset() {
  const [, e] = await saveAsset({ ...asset.value });
  if (e) return fail(e);
  ok("دارایی ثبت شد.");
  asset.value = { tag: "", title: "", warehouseId: null, cost: null, status: 1 };
  load();
}

async function addConversion() {
  const [, e] = await saveConversion({ ...conversion.value });
  if (e) return fail(e);
  ok("تبدیل واحد ثبت شد.");
  load();
}

async function addCeiling() {
  const [, e] = await saveCeiling({ ...ceiling.value });
  if (e) return fail(e);
  ok("سقف مصرف ثبت شد.");
  load();
}

async function removeCeiling(id: number) {
  const [, e] = await deleteCeiling(id);
  if (e) return fail(e);
  load();
}

function printLabels() {
  openLabels(assets.value.map((a) => ({ code: labelCode.asset(a.tag), title: a.title, lines: [a.tag, a.warehouseTitle || ""] })), router);
}

onMounted(async () => {
  const [w] = await getWarehouses();
  warehouses.value = w || [];
  load();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">اموال، تبدیل واحد و سقف مصرف</h1>
        <div class="btn-group btn-group-sm">
          <button :class="tab === 'assets' ? 'btn-primary' : 'btn-outline-primary'" class="btn" @click="show('assets')">اموال</button>
          <button :class="tab === 'conversions' ? 'btn-primary' : 'btn-outline-primary'" class="btn" @click="show('conversions')">تبدیل واحد</button>
          <button :class="tab === 'ceilings' ? 'btn-primary' : 'btn-outline-primary'" class="btn" @click="show('ceilings')">سقف مصرف پروژه</button>
        </div>
      </div>

      <template v-if="tab === 'assets'">
        <div class="inv-filters mb-3">
          <input v-model="asset.tag" class="form-control form-control-sm inv-ltr" placeholder="شماره اموال (برچسب)" />
          <input v-model="asset.title" class="form-control form-control-sm" placeholder="عنوان" />
          <select v-model="asset.warehouseId" class="form-select form-select-sm">
            <option :value="null">انبار / محل</option>
            <option v-for="w in warehouses" :key="w.id" :value="w.id">{{ w.title }}</option>
          </select>
          <select v-model="asset.status" class="form-select form-select-sm">
            <option v-for="(t, k) in assetStatuses" :key="k" :value="Number(k)">{{ t }}</option>
          </select>
          <input v-model.number="asset.cost" class="form-control form-control-sm inv-ltr" placeholder="بهای خرید (ریال)" type="number" />
          <button class="btn btn-sm btn-primary" @click="addAsset">ثبت</button>
          <button class="btn btn-sm btn-outline-secondary" @click="printLabels">چاپ برچسب</button>
        </div>
        <table class="table table-sm">
          <thead><tr><th>برچسب</th><th>عنوان</th><th>انبار</th><th>وضعیت</th><th>تاریخ خرید</th><th>بها</th></tr></thead>
          <tbody>
            <tr v-for="a in assets" :key="a.id">
              <td class="inv-ltr">{{ a.labelCode }}</td><td>{{ a.title }}</td><td>{{ a.warehouseTitle }}</td><td>{{ assetStatuses[a.status] }}</td>
              <td>{{ jalali(a.purchaseDate) }}</td><td class="text-end inv-ltr">{{ a.cost === null || a.cost === undefined ? "محرمانه" : rial(a.cost) }}</td>
            </tr>
          </tbody>
        </table>
      </template>

      <template v-else-if="tab === 'conversions'">
        <div class="inv-filters mb-3">
          <input v-model.number="conversion.itemId" class="form-control form-control-sm inv-ltr" placeholder="شناسه کالا (خالی: همه)" type="number" />
          <input v-model="conversion.fromUnit" class="form-control form-control-sm" placeholder="از واحد" />
          <input v-model="conversion.toUnit" class="form-control form-control-sm" placeholder="به واحد" />
          <input v-model.number="conversion.factor" class="form-control form-control-sm inv-ltr" placeholder="ضریب" type="number" />
          <button class="btn btn-sm btn-primary" @click="addConversion">ثبت</button>
        </div>
        <table class="table table-sm">
          <thead><tr><th>کالا</th><th>از</th><th>به</th><th>ضریب</th></tr></thead>
          <tbody><tr v-for="c in conversions" :key="c.id"><td>{{ c.itemCode || "همه کالاها" }}</td><td>{{ c.fromUnit }}</td><td>{{ c.toUnit }}</td><td class="inv-ltr">{{ c.factor }}</td></tr></tbody>
        </table>
      </template>

      <template v-else>
        <div class="inv-filters mb-3">
          <input v-model.number="ceiling.itemId" class="form-control form-control-sm inv-ltr" placeholder="شناسه کالا" type="number" />
          <input v-model.number="ceiling.workOrderId" class="form-control form-control-sm inv-ltr" placeholder="شناسه دستور کار" type="number" />
          <input v-model.number="ceiling.maxQuantity" class="form-control form-control-sm inv-ltr" placeholder="سقف مقدار" type="number" />
          <input v-model="ceiling.note" class="form-control form-control-sm" placeholder="توضیح" />
          <button class="btn btn-sm btn-primary" @click="addCeiling">ثبت</button>
        </div>
        <table class="table table-sm">
          <thead><tr><th>کالا</th><th>دستور کار</th><th>سقف</th><th>توضیح</th><th></th></tr></thead>
          <tbody>
            <tr v-for="c in ceilings" :key="c.id">
              <td>{{ c.itemCode }}</td><td>{{ c.workOrderCode }}</td><td class="inv-ltr">{{ c.maxQuantity }}</td><td>{{ c.note }}</td>
              <td><button class="btn btn-sm btn-outline-danger" @click="removeCeiling(c.id)">حذف</button></td>
            </tr>
          </tbody>
        </table>
      </template>
    </div>
  </div>
</template>
