<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import QPager from "@/components/quality/QPager.vue";
import CustomerEditor from "./CustomerEditor.vue";
import { getSlCustomer, getSlCustomers } from "@/api/SalesAPIs";
import { usePagedList } from "@/composables/fn";
import { fail } from "@/composables/inventory";
import { customerStatuses, customerTypes, industries, rialText } from "@/composables/sales";
import { SalesRoles, hasRole } from "@/composables/roles";

// Customer list with filters and the profile editor. openId (route /sales/customers/:id) opens that customer.
const props = defineProps({ openId: { type: Number, default: null } });
const emit = defineEmits(["open"]);
const filters = ref({ Status: null as number | null, Type: null as number | null, Contains: "" });
const list = usePagedList(getSlCustomers, filters);
const editing = ref<any>(null);
const creating = ref(false);
const canWrite = hasRole(SalesRoles.masterWriters);
const showCredit = hasRole(SalesRoles.creditViewers);

async function open(id: number) {
  const [d, error] = await getSlCustomer(id);
  if (error) return fail(error);
  editing.value = d;
}
function closed() {
  editing.value = null;
  creating.value = false;
  emit("open", null);
}
function saved() {
  list.load();
  if (creating.value) closed();
}
watch(() => props.openId, (id) => id && open(id));
onMounted(() => {
  list.load();
  if (props.openId) open(props.openId);
});
</script>

<template>
  <div>
    <div class="inv-toolbar">
      <div class="inv-filters">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="نام، کد اقتصادی یا شناسه ملی" @keyup.enter="list.search" />
        <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in customerStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <select v-model="filters.Type" class="form-select form-select-sm"><option :value="null">همه انواع</option><option v-for="(t, k) in customerTypes" :key="k" :value="Number(k)">{{ t }}</option></select>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <button v-if="canWrite" class="btn btn-sm btn-primary" @click="creating = true">مشتری جدید</button>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>نام</th><th>نوع</th><th>صنعت</th><th>شهر</th><th>مسئول فروش</th><th v-if="showCredit">سقف اعتبار</th><th>مخاطبان</th><th>سایت‌ها</th><th>وضعیت</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td :colspan="showCredit ? 10 : 9" class="text-center inv-muted">مشتری پیدا نشد.</td></tr>
          <tr v-for="r in list.rows.value" :key="r.id">
            <td>{{ r.name }}</td><td>{{ customerTypes[r.type] || "-" }}</td><td>{{ industries[r.industry] || "-" }}</td><td>{{ r.city || "-" }}</td><td>{{ r.ownerName || "-" }}</td>
            <td v-if="showCredit">{{ rialText(r.creditLimitRial) }}</td><td>{{ r.contactCount }}</td><td>{{ r.siteCount }}</td>
            <td><span :class="r.status === 2 ? 'ok' : r.status === 3 ? '' : 'warn'" class="inv-badge">{{ customerStatuses[r.status] }}</span></td>
            <td><button class="btn btn-sm btn-outline-primary" @click="editing = r">{{ canWrite ? "پروفایل" : "مشاهده" }}</button></td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    <CustomerEditor v-if="editing || creating" :key="editing?.id || 'new'" :row="editing" @close="closed" @saved="saved" />
  </div>
</template>
