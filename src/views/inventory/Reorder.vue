<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { draftReorder, getGroups, getReorderList, runWarehouseAlerts } from "@/api/InventoryAPIs";
import { fail, num, ok } from "@/composables/inventory";

// PB5: reorder point list (ROP = daily use x lead time + safety stock) and a DRAFT purchase request; a person reviews and submits it.
const rows = ref<any[]>([]);
const groups = ref<any[]>([]);
const filters = ref<any>({ ItemGroupId: null, Platform: "", Contains: "" });
const chosen = ref<number[]>([]);
const loading = ref(false);
const router = useRouter();
const canDraft = computed(() => chosen.value.length > 0);

async function load() {
  loading.value = true;
  const [data, error] = await getReorderList(filters.value);
  loading.value = false;
  if (error) return fail(error);
  rows.value = data || [];
  chosen.value = [];
}

async function draft() {
  const [data, error] = await draftReorder(chosen.value);
  if (error) return fail(error);
  ok(`پیش‌نویس درخواست خرید ${data.requestNumber} با ${num(data.lines)} ردیف ساخته شد؛ بررسی و ارسال با شماست.`);
  router.push({ name: "ProcurementRequests" }).catch(() => undefined);
}

async function runAlerts() {
  const [data, error] = await runWarehouseAlerts();
  if (error) return fail(error);
  ok(`هشدارها اجرا شد: امانی ${num((data?.consignmentAlerts ?? 0))}، منتظر QC ${num(data?.qcAlerts ?? 0)}، ارجاع به QA و برنامه‌ریزی ${num(data?.qcEscalations ?? 0)}.`);
}

onMounted(async () => {
  const [g] = await getGroups();
  groups.value = g || [];
  load();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">نقطه سفارش مجدد</h1>
        <div class="d-flex gap-2">
          <button class="btn btn-sm btn-outline-secondary" @click="runAlerts">اجرای هشدارهای امانی و بازرسی ورودی</button>
          <button :disabled="!canDraft" class="btn btn-sm btn-primary" @click="draft">پیش‌نویس درخواست خرید از کالاهای انتخاب‌شده</button>
        </div>
      </div>
      <div class="inv-filters mb-3">
        <select v-model="filters.ItemGroupId" class="form-select form-select-sm" @change="load">
          <option :value="null">همه گروه‌ها</option>
          <option v-for="g in groups" :key="g.id" :value="g.id">{{ g.code }} - {{ g.title }}</option>
        </select>
        <input v-model="filters.Platform" class="form-control form-control-sm" placeholder="سکو" />
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="کد یا شرح" @keydown.enter="load" />
        <button class="btn btn-sm btn-secondary" @click="load">نمایش</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th></th><th>کد</th><th>شرح</th><th>واحد</th><th>موجودی</th><th>در سفارش</th><th>مصرف روزانه</th><th>زمان تامین (روز)</th><th>موجودی احتیاطی</th><th>نقطه سفارش</th><th>مقدار پیشنهادی</th><th>درخواست باز</th></tr></thead>
          <tbody>
            <tr v-for="r in rows" :key="r.itemId">
              <td><input v-model="chosen" :disabled="r.openRequest" :value="r.itemId" type="checkbox" /></td>
              <td class="inv-ltr">{{ r.itemCode }}</td><td>{{ r.itemTitle }}</td><td>{{ r.unit }}</td>
              <td class="text-end inv-ltr">{{ num(r.onHand) }}</td><td class="text-end inv-ltr">{{ num(r.onOrder) }}</td>
              <td class="text-end inv-ltr">{{ num(r.dailyUse) }}</td><td class="text-end">{{ num(r.leadDays) }}</td>
              <td class="text-end inv-ltr">{{ num(r.safetyStock) }}<span v-if="r.fromGroupDefault" class="inv-muted"> (پیش‌فرض گروه)</span></td>
              <td class="text-end inv-ltr">{{ num(r.reorderPoint) }}</td><td class="text-end inv-ltr fw-bold">{{ num(r.suggestedQuantity) }}</td>
              <td>{{ r.openRequest ? "دارد" : "" }}</td>
            </tr>
            <tr v-if="!rows.length && !loading"><td class="inv-muted text-center" colspan="12">کالایی زیر نقطه سفارش نیست.</td></tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>
