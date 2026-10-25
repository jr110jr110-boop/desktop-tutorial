<script setup lang="ts">
import { onMounted, ref } from "vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import QPager from "@/components/quality/QPager.vue";
import { getSlLinks, getSlUnlinked, linkSlLegacy, linkSlManual } from "@/api/SalesAPIs";
import { camelize, usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { linkMethods, linkSources } from "@/composables/sales";
import { SalesRoles, hasRole } from "@/composables/roles";
import { customerLabel, searchCustomers } from "./pipelineKit";

// Legacy linker: matches the employer text of old contracts and tenders to customers once; leftovers are linked by hand.
const filters = ref({ SourceType: null as number | null, Contains: "" });
const list = usePagedList(getSlLinks, filters);
const unlinked = ref<any[]>([]);
const result = ref<any>(null);
const busy = ref(false);
const picks = ref<Record<string, any>>({});
const canLink = hasRole(SalesRoles.legacyLinkers);
const key = (u: any) => `${u.sourceType}-${u.sourceId}`;

async function loadUnlinked() {
  const [d, error] = await getSlUnlinked();
  if (error) return fail(error);
  unlinked.value = Array.isArray(d) ? d : [];
}
async function run() {
  busy.value = true;
  const [d, error] = await linkSlLegacy();
  busy.value = false;
  if (error) return fail(error);
  result.value = camelize(d);
  ok("تطبیق خودکار انجام شد.");
  list.load();
  loadUnlinked();
}
async function link(u: any) {
  const c = picks.value[key(u)];
  if (!c) return fail({ message: "مشتری را انتخاب کنید." });
  busy.value = true;
  const [, error] = await linkSlManual({ sourceType: u.sourceType, sourceId: u.sourceId, customerId: c.id });
  busy.value = false;
  if (error) return fail(error);
  ok("پیوند ثبت شد.");
  list.load();
  loadUnlinked();
}
onMounted(() => {
  list.load();
  loadUnlinked();
});
</script>

<template>
  <div>
    <div class="inv-toolbar">
      <p class="mb-0 small text-muted">نام کارفرمای قراردادها و مناقصه‌های قدیمی با نام مشتری مطابقت داده می‌شود؛ موارد بدون تطبیق را دستی به مشتری وصل کنید.</p>
      <button v-if="canLink" :disabled="busy" class="btn btn-sm btn-primary" @click="run">اجرای تطبیق خودکار</button>
    </div>
    <div v-if="result" class="alert alert-info py-2 mt-2">
      قرارداد پیوندشده: {{ result.linkedContracts }}، مناقصه پیوندشده: {{ result.linkedTenders }}، قبلاً پیوندشده: {{ result.alreadyLinked }}، بدون تطبیق: {{ (result.unmatched || []).length }}
    </div>

    <h2 class="inv-title mt-3 mb-2">بدون مشتری ({{ unlinked.length }})</h2>
    <div class="table-responsive">
      <table class="table table-sm">
        <thead><tr><th>نوع</th><th>شناسه</th><th>متن کارفرما</th><th v-if="canLink">مشتری</th><th v-if="canLink"></th></tr></thead>
        <tbody>
          <tr v-if="!unlinked.length"><td :colspan="canLink ? 5 : 3" class="text-center inv-muted">موردی بدون مشتری نیست.</td></tr>
          <tr v-for="u in unlinked" :key="key(u)">
            <td>{{ linkSources[u.sourceType] }}</td><td class="inv-ltr">{{ u.sourceId }}</td><td>{{ u.text }}</td>
            <td v-if="canLink" style="min-width: 220px"><LookupBox :model-value="picks[key(u)] || null" :fetch="searchCustomers" :label="customerLabel" placeholder="مشتری" @pick="(c: any) => (picks[key(u)] = c)" /></td>
            <td v-if="canLink"><button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="link(u)">پیوند</button></td>
          </tr>
        </tbody>
      </table>
    </div>

    <h2 class="inv-title mt-3 mb-2">پیوندهای ثبت‌شده</h2>
    <div class="inv-filters mb-2">
      <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="متن یا مشتری" @keyup.enter="list.search" />
      <select v-model="filters.SourceType" class="form-select form-select-sm"><option :value="null">قرارداد و مناقصه</option><option v-for="(t, k) in linkSources" :key="k" :value="Number(k)">{{ t }}</option></select>
      <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>نوع</th><th>متن منبع</th><th>مشتری</th><th>روش</th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="4" class="text-center inv-muted">پیوندی ثبت نشده است.</td></tr>
          <tr v-for="r in list.rows.value" :key="r.id"><td>{{ linkSources[r.sourceType] }}</td><td>{{ r.sourceText }}</td><td>{{ r.customerName }}</td><td>{{ linkMethods[r.method] }}</td></tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
  </div>
</template>
