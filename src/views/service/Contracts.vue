<script setup lang="ts">
// LTSA contracts (SvContracts): customer, units, period, scope, visit frequency, price; activation / cancel per API; the visit orders the nightly job drafted (/service/contracts/:id).
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import FnDate from "@/components/fn/FnDate.vue";
import QPager from "@/components/quality/QPager.vue";
import CustomerPick from "@/components/service/CustomerPick.vue";
import { isoDay, listOf, typedNumber } from "@/components/service/svKit";
import { getSvContract, getSvContracts, getSvOrders, getSvUnits, saveSvContract, svContractAction } from "@/api/ServiceAPIs";
import { usePagedList } from "@/composables/fn";
import { fail, ok, rial } from "@/composables/inventory";
import { hasRole, ServiceRoles } from "@/composables/roles";
import { badgeOfServiceContract, badgeOfServiceOrder, contractStatuses, orderStatuses, rialText, serviceActionTitles } from "@/composables/service";

const route = useRoute();
const router = useRouter();
const canWrite = hasRole(ServiceRoles.writers);
const contractId = computed(() => Number(route.params.id) || null);
const customer = ref<any>(null);
const filters = ref<any>({ Status: null, Contains: "" });
const list = usePagedList(getSvContracts, computed(() => ({ ...filters.value, CustomerId: customer.value?.id ?? null })) as any);

const contract = ref<any>(null);
const visits = ref<any[]>([]);
const form = ref<any>(null);
const formCustomer = ref<any>(null);
const customerUnits = ref<any[]>([]);
const busy = ref(false);

async function load(id: number) {
  const [data, error] = await getSvContract(id);
  if (error) return fail(error);
  contract.value = data;
  visits.value = [];
  // the order query has no contract filter: read the orders of the contract's units and keep the visits of this agreement
  const found: any[] = [];
  for (const uid of data.unitIds || []) {
    const [o] = await getSvOrders({ UnitId: uid, PageNumber: 1, PageSize: 200 });
    found.push(...listOf(o).filter((x: any) => x.ltsaContractId === data.id));
  }
  visits.value = found.sort((a, b) => String(a.plannedStart || "").localeCompare(String(b.plannedStart || "")));
}
watch(contractId, (id) => (id ? load(id) : (contract.value = null)));

async function loadUnits(customerId: number | null) {
  customerUnits.value = [];
  if (!customerId) return;
  const [data] = await getSvUnits({ CustomerId: customerId, PageNumber: 1, PageSize: 200 });
  customerUnits.value = listOf(data).filter((u: any) => !u.decommissioned);
}
async function startEdit(c: any | null) {
  form.value = {
    id: c?.id ?? null, title: c?.title || "", customerId: c?.customerId ?? null, startDate: isoDay(c?.startDate), endDate: isoDay(c?.endDate), scope: c?.scope || "",
    visitIntervalMonths: c ? String(c.visitIntervalMonths) : "6", priceRial: c?.priceRial ? String(c.priceRial) : "", unitIds: [...(c?.unitIds || [])], rowVersion: c?.rowVersion || null,
  };
  formCustomer.value = c ? { id: c.customerId, name: c.customerName } : null;
  await loadUnits(form.value.customerId);
}
async function pickCustomer(c: any) {
  form.value.customerId = c?.id ?? null;
  form.value.unitIds = [];
  await loadUnits(form.value.customerId);
}
const toggleUnit = (uid: number) => {
  const a = form.value.unitIds;
  const i = a.indexOf(uid);
  if (i >= 0) a.splice(i, 1);
  else a.push(uid);
};
async function save() {
  const f = form.value;
  if (!String(f.title).trim()) return fail("عنوان قرارداد را وارد کنید.");
  if (!f.customerId) return fail("مشتری را انتخاب کنید.");
  if (!f.startDate || !f.endDate) return fail("تاریخ شروع و پایان را وارد کنید.");
  busy.value = true;
  const [data, error] = await saveSvContract({
    id: f.id, title: f.title, customerId: f.customerId, startDate: f.startDate, endDate: f.endDate, scope: f.scope, visitIntervalMonths: typedNumber(f.visitIntervalMonths) ?? 0,
    priceRial: typedNumber(f.priceRial), unitIds: f.unitIds, rowVersion: f.rowVersion,
  });
  busy.value = false;
  if (error) return fail(error);
  ok("قرارداد ذخیره شد.");
  form.value = null;
  if (data?.id) {
    router.push("/service/contracts/" + data.id);
    await load(data.id);
  }
  list.load();
}
async function act(action: "activate" | "cancel") {
  if (action === "cancel" && !window.confirm("قرارداد لغو شود؟")) return;
  busy.value = true;
  const [data, error] = await svContractAction({ id: contract.value.id, action, rowVersion: contract.value.rowVersion });
  busy.value = false;
  if (error) {
    fail(error);
    return load(contract.value.id);
  }
  contract.value = data;
  ok(serviceActionTitles[action] + " انجام شد.");
  list.load();
  load(data.id);
}

onMounted(() => {
  list.load();
  if (contractId.value) load(contractId.value);
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <!-- list -->
      <template v-if="!contractId && !form">
        <div class="inv-toolbar">
          <h1 class="inv-title">قراردادهای خدمات (LTSA)</h1>
          <button v-if="canWrite" class="btn btn-sm btn-primary" @click="startEdit(null)">قرارداد جدید</button>
        </div>
        <div class="inv-filters mb-3">
          <div><label class="form-label small mb-0">مشتری</label><CustomerPick v-model="customer" /></div>
          <div><label class="form-label small mb-0">وضعیت</label>
            <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه</option><option v-for="(t, k) in contractStatuses" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div><label class="form-label small mb-0">شماره یا عنوان</label><input v-model="filters.Contains" class="form-control form-control-sm" @keyup.enter="list.search" /></div>
          <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
        </div>
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>شماره</th><th>عنوان</th><th>مشتری</th><th>دوره</th><th>واحدها</th><th>بازدید بعدی</th><th>مبلغ (ریال)</th><th>وضعیت</th><th></th></tr></thead>
            <tbody>
              <tr v-if="!list.rows.value.length"><td colspan="9" class="text-center inv-muted">قراردادی نیست.</td></tr>
              <tr v-for="c in list.rows.value" :key="c.id">
                <td class="inv-ltr">{{ c.number }}</td><td>{{ c.title }}</td><td>{{ c.customerName }}</td><td>{{ c.startDateText }} تا {{ c.endDateText }}</td>
                <td>{{ (c.unitTags || []).length }}</td><td>{{ c.nextVisitDateText || "-" }}</td><td>{{ rialText(c.priceRial) }}</td>
                <td><span :class="badgeOfServiceContract(c.status)" class="inv-badge">{{ contractStatuses[c.status] }}</span></td>
                <td><button class="btn btn-sm btn-outline-primary" @click="router.push('/service/contracts/' + c.id)">باز کردن</button></td>
              </tr>
            </tbody>
          </table>
        </div>
        <QPager :page="list.page.value" :total="list.total.value" :page-size="list.pageSize" @go="list.go" />
      </template>

      <!-- form -->
      <template v-else-if="form">
        <div class="inv-toolbar"><h1 class="inv-title">{{ form.id ? "ویرایش قرارداد" : "قرارداد خدمات جدید" }}</h1></div>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">عنوان *</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">مشتری *</label><CustomerPick v-model="formCustomer" :fallback-text="contract?.customerName || ''" @pick="pickCustomer" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">شروع *</label><FnDate v-model="form.startDate" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">پایان *</label><FnDate v-model="form.endDate" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">فاصله بازدیدها (ماه) *</label><input v-model="form.visitIntervalMonths" class="form-control form-control-sm fn-num" inputmode="numeric" /></div>
          <div class="col-md-3">
            <label class="form-label small mb-0">مبلغ (ریال)</label><input v-model="form.priceRial" class="form-control form-control-sm fn-num" inputmode="numeric" />
            <div v-if="typedNumber(form.priceRial)" class="inv-muted">{{ rial(typedNumber(form.priceRial)) }} ریال</div>
          </div>
          <div class="col-12"><label class="form-label small mb-0">دامنه خدمات</label><textarea v-model="form.scope" class="form-control form-control-sm" rows="3"></textarea></div>
          <div class="col-12">
            <label class="form-label small mb-0">واحدهای تحت پوشش *</label>
            <div v-if="!customerUnits.length" class="inv-muted">ابتدا مشتری را انتخاب کنید؛ فقط واحدهای فعال همان مشتری نشان داده می‌شود.</div>
            <div v-for="u in customerUnits" :key="u.id">
              <label class="form-check mb-0"><input :checked="form.unitIds.includes(u.id)" class="form-check-input" type="checkbox" @change="toggleUnit(u.id)" /> <span class="form-check-label"><span class="inv-ltr">{{ u.tag }}</span> {{ u.title }}</span></label>
            </div>
          </div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        </div>
      </template>

      <!-- contract -->
      <template v-else-if="contract">
        <div class="inv-toolbar">
          <div>
            <h1 class="inv-title mb-0">{{ contract.title }} <span class="inv-ltr">{{ contract.number }}</span></h1>
            <span :class="badgeOfServiceContract(contract.status)" class="inv-badge">{{ contractStatuses[contract.status] }}</span>
          </div>
          <button class="btn btn-sm btn-outline-secondary" @click="router.push('/service/contracts')">بازگشت به فهرست</button>
        </div>
        <div v-if="canWrite" class="d-flex gap-2 flex-wrap mb-3">
          <button v-if="contract.status === 1" class="btn btn-sm btn-outline-primary" @click="startEdit(contract)">ویرایش</button>
          <button v-if="contract.status === 1" :disabled="busy" class="btn btn-sm btn-success" @click="act('activate')">فعال‌سازی</button>
          <button v-if="contract.status === 1 || contract.status === 2" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act('cancel')">لغو قرارداد</button>
        </div>
        <p v-if="contract.status === 1 && canWrite" class="inv-muted">فعال‌سازی را نفر دیگری غیر از تنظیم‌کنندهٔ قرارداد انجام می‌دهد؛ قرارداد فعال ویرایش نمی‌شود.</p>
        <div class="row g-2 small mb-3">
          <div class="col-md-4"><b>مشتری:</b> {{ contract.customerName }}</div>
          <div class="col-md-4"><b>دوره:</b> {{ contract.startDateText }} تا {{ contract.endDateText }}</div>
          <div class="col-md-4"><b>فاصله بازدیدها:</b> هر {{ contract.visitIntervalMonths }} ماه</div>
          <div class="col-md-4"><b>بازدید بعدی:</b> {{ contract.nextVisitDateText || "-" }}</div>
          <div class="col-md-4"><b>مبلغ (ریال):</b> {{ rialText(contract.priceRial) }}</div>
          <div class="col-md-4"><b>پیش‌نویس‌های بازدید:</b> {{ contract.draftedVisits }}</div>
          <div class="col-12"><b>دامنه خدمات:</b> {{ contract.scope || "-" }}</div>
          <div class="col-12"><b>واحدها:</b> <a v-for="(uid, i) in contract.unitIds" :key="uid" class="me-3" href="#" @click.prevent="router.push('/service/units/' + uid)"><span class="inv-ltr">{{ contract.unitTags[i] }}</span></a></div>
        </div>
        <h3 class="inv-title mb-2">دستورهای بازدید</h3>
        <table class="table table-sm">
          <thead><tr><th>شماره</th><th>واحد</th><th>عنوان</th><th>برنامه</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!visits.length"><td colspan="6" class="text-center inv-muted">هنوز بازدیدی پیش‌نویس نشده است (کارهای شبانه، به‌اندازهٔ «روز قبل از بازدید» در تنظیمات، دستور را پیش‌نویس می‌کنند).</td></tr>
            <tr v-for="o in visits" :key="o.id">
              <td class="inv-ltr">{{ o.number }}</td><td class="inv-ltr">{{ o.unitTag }}</td><td>{{ o.title }}</td><td>{{ o.plannedStartText || "-" }}</td>
              <td><span :class="badgeOfServiceOrder(o.status)" class="inv-badge">{{ orderStatuses[o.status] }}</span></td>
              <td><button class="btn btn-sm btn-outline-primary" @click="router.push('/service/orders/' + o.id)">باز کردن</button></td>
            </tr>
          </tbody>
        </table>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style lang="scss" src="../fn/fn.scss"></style>
