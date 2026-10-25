<script setup lang="ts">
// Service requests (SvRequests): tickets with SLA due times, breach flags, priority, type and status; a new ticket opens the detail page.
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import CustomerPick from "@/components/service/CustomerPick.vue";
import DateTimeInput from "@/components/service/DateTimeInput.vue";
import SlaCell from "@/components/service/SlaCell.vue";
import { listOf } from "@/components/service/svKit";
import { getSvRequests, getSvUnits, saveSvRequest } from "@/api/ServiceAPIs";
import { usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { hasRole, ServiceRoles } from "@/composables/roles";
import { badgeOfPriority, badgeOfRequest, priorities, requestStatuses, requestTypes } from "@/composables/service";

const router = useRouter();
const canWrite = hasRole(ServiceRoles.writers);
const customer = ref<any>(null);
const filters = ref<any>({ Status: null, Priority: null, OpenOnly: true, Breached: null, Contains: "" });
const list = usePagedList(getSvRequests, computed(() => ({ ...filters.value, CustomerId: customer.value?.id ?? null })) as any);
const form = ref<any>(null);
const formCustomer = ref<any>(null);
const units = ref<any[]>([]);
const busy = ref(false);

function blank() {
  return { customerId: null as number | null, unitId: null as number | null, channel: "", receivedAt: null as string | null, description: "", type: 1, priority: 3, productNonconformity: false };
}
async function pickCustomer(c: any) {
  form.value.customerId = c?.id ?? null;
  form.value.unitId = null;
  units.value = [];
  if (!c) return;
  const [data] = await getSvUnits({ CustomerId: c.id, PageNumber: 1, PageSize: 200 });
  units.value = listOf(data);
}
async function create() {
  const f = form.value;
  if (!f.customerId) return fail("مشتری را انتخاب کنید.");
  if (!String(f.description).trim()) return fail("شرح درخواست را بنویسید.");
  busy.value = true;
  const [data, error] = await saveSvRequest({ ...f, receivedAt: f.receivedAt || null });
  busy.value = false;
  if (error) return fail(error);
  ok("درخواست ثبت شد.");
  form.value = null;
  if (data?.id) router.push("/service/requests/" + data.id);
  else list.load();
}
onMounted(list.load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">درخواست‌های خدمات</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="form = blank(); formCustomer = null; units = []">درخواست جدید</button>
      </div>
      <div class="inv-filters mb-3">
        <div><label class="form-label small mb-0">مشتری</label><CustomerPick v-model="customer" /></div>
        <div><label class="form-label small mb-0">وضعیت</label>
          <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه</option><option v-for="(t, k) in requestStatuses" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div><label class="form-label small mb-0">اولویت</label>
          <select v-model="filters.Priority" class="form-select form-select-sm"><option :value="null">همه</option><option v-for="(t, k) in priorities" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div><label class="form-label small mb-0">شماره یا شرح</label><input v-model="filters.Contains" class="form-control form-control-sm" @keyup.enter="list.search" /></div>
        <label class="form-check mb-0"><input v-model="filters.OpenOnly" class="form-check-input" type="checkbox" :true-value="true" :false-value="null" /> <span class="form-check-label">فقط باز</span></label>
        <label class="form-check mb-0"><input v-model="filters.Breached" class="form-check-input" type="checkbox" :true-value="true" :false-value="null" /> <span class="form-check-label">تخطی از SLA</span></label>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>مشتری</th><th>واحد</th><th>نوع</th><th>اولویت</th><th>دریافت</th><th>SLA</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="9" class="text-center inv-muted">درخواستی نیست.</td></tr>
            <tr v-for="r in list.rows.value" :key="r.id">
              <td class="inv-ltr">{{ r.number }}</td><td>{{ r.customerName }}</td><td class="inv-ltr">{{ r.unitTag || "-" }}</td><td>{{ r.typeText }}</td>
              <td><span :class="badgeOfPriority(r.priority)" class="inv-badge">{{ r.priorityText }}</span></td>
              <td>{{ r.receivedAtText }}</td>
              <td><SlaCell :r="r" /></td>
              <td>
                <span :class="badgeOfRequest(r.status, r.breached)" class="inv-badge">{{ r.statusText }}</span>
                <span v-if="r.breached" class="inv-badge bad ms-1">تخطی SLA</span>
              </td>
              <td><button class="btn btn-sm btn-outline-primary" @click="router.push('/service/requests/' + r.id)">باز کردن</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" :page-size="list.pageSize" @go="list.go" />
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">درخواست خدمات جدید</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">مشتری *</label><CustomerPick v-model="formCustomer" @pick="pickCustomer" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">واحد نصب‌شده</label>
            <select v-model="form.unitId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="u in units" :key="u.id" :value="u.id">{{ u.tag }} - {{ u.title }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">نوع</label>
            <select v-model="form.type" class="form-select form-select-sm"><option v-for="(t, k) in requestTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">اولویت</label>
            <select v-model="form.priority" class="form-select form-select-sm"><option v-for="(t, k) in priorities" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">کانال دریافت</label><input v-model="form.channel" class="form-control form-control-sm" placeholder="تلفن، ایمیل، ..." /></div>
          <div class="col-12"><label class="form-label small mb-0">زمان دریافت (خالی = اکنون؛ ساعت شروع SLA)</label><DateTimeInput v-model="form.receivedAt" /></div>
          <div class="col-12"><label class="form-label small mb-0">شرح *</label><textarea v-model="form.description" class="form-control form-control-sm" rows="3"></textarea></div>
          <div class="col-12">
            <label class="form-check mb-0"><input v-model="form.productNonconformity" class="form-check-input" type="checkbox" /> <span class="form-check-label">عدم انطباق محصول</span></label>
            <div class="inv-muted">نوع «شکایت» یا عدم انطباق محصول، به‌طور خودکار شکایت مشتری در کیفیت می‌سازد و دو سند به هم پیوند می‌خورند.</div>
          </div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="create">ثبت</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style lang="scss" src="../fn/fn.scss"></style>
