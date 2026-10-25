<script setup lang="ts">
// One service request (/service/requests/:id): SLA, actions per state (assign, progress, wait, resolve, close), the service orders made for it and the linked quality complaint.
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import UserSelect from "@/components/quality/UserSelect.vue";
import DateTimeInput from "@/components/service/DateTimeInput.vue";
import SlaCell from "@/components/service/SlaCell.vue";
import { listOf } from "@/components/service/svKit";
import { getSvOrders, getSvRequest, svRequestAction } from "@/api/ServiceAPIs";
import { fail, ok } from "@/composables/inventory";
import { hasRole, QualityRoles, ServiceRoles } from "@/composables/roles";
import { badgeOfPriority, badgeOfRequest, badgeOfServiceOrder, orderStatuses, serviceActionTitles } from "@/composables/service";

const route = useRoute();
const router = useRouter();
const id = computed(() => Number(route.params.id) || 0);
const canWrite = hasRole(ServiceRoles.writers);
const canQuality = hasRole(QualityRoles.readers);
const req = ref<any>(null);
const orders = ref<any[]>([]);
const busy = ref(false);
const pending = ref<null | "assign" | "progress" | "wait" | "resolve" | "close">(null);
const f = ref({ userId: null as number | null, resolution: "", at: null as string | null });

const status = computed(() => req.value?.status || 0);
// open = 1, assigned = 2, in progress = 3, waiting customer = 4, resolved = 5, closed = 6
const available = computed<string[]>(() => {
  const s = status.value;
  if (!canWrite || !s || s === 6) return [];
  if (s === 5) return ["close"];
  return ["assign", "progress", "wait", "resolve"];
});
const openOrders = computed(() => orders.value.filter((o) => o.status !== 4 && o.status !== 5));

async function load() {
  const [data, error] = await getSvRequest(id.value);
  if (error) return fail(error);
  req.value = data;
  const [o] = await getSvOrders({ RequestId: id.value, PageNumber: 1, PageSize: 100 });
  orders.value = listOf(o);
}
function start(a: typeof pending.value) {
  pending.value = a;
  f.value = { userId: null, resolution: "", at: null };
}
async function submit() {
  const a = pending.value!;
  if (a === "assign" && !f.value.userId) return fail("کاربر مسئول را انتخاب کنید.");
  if (a === "resolve" && !f.value.resolution.trim()) return fail("شرح راه‌حل را بنویسید.");
  busy.value = true;
  const [data, error] = await svRequestAction({
    id: req.value.id, action: a, userId: a === "assign" ? f.value.userId : null, resolution: a === "resolve" ? f.value.resolution : null, at: a === "close" ? null : f.value.at, rowVersion: req.value.rowVersion,
  });
  busy.value = false;
  if (error) {
    fail(error);
    return load();
  }
  req.value = data;
  pending.value = null;
  ok(serviceActionTitles[a] + " انجام شد.");
  load();
}
watch(id, load);
onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div v-if="req" class="inv-card">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title mb-0">درخواست خدمات <span class="inv-ltr">{{ req.number }}</span></h1>
          <span :class="badgeOfRequest(req.status, req.breached)" class="inv-badge">{{ req.statusText }}</span>
          <span :class="badgeOfPriority(req.priority)" class="inv-badge ms-1">{{ req.priorityText }}</span>
          <span class="inv-badge info ms-1">{{ req.typeText }}</span>
          <span v-if="req.breached" class="inv-badge bad ms-1">تخطی از SLA</span>
          <span v-if="req.productNonconformity" class="inv-badge warn ms-1">عدم انطباق محصول</span>
        </div>
        <button class="btn btn-sm btn-outline-secondary" @click="router.push('/service/requests')">بازگشت به فهرست</button>
      </div>

      <div class="row g-2 small mb-3">
        <div class="col-md-4"><b>مشتری:</b> {{ req.customerName }}</div>
        <div class="col-md-4"><b>واحد:</b> <a v-if="req.unitId" href="#" @click.prevent="router.push('/service/units/' + req.unitId)"><span class="inv-ltr">{{ req.unitTag }}</span></a><span v-else>-</span></div>
        <div class="col-md-4"><b>کانال:</b> {{ req.channel || "-" }}</div>
        <div class="col-md-4"><b>دریافت:</b> {{ req.receivedAtText }}</div>
        <div class="col-md-4"><b>مسئول:</b> {{ req.assignedToName || "-" }}</div>
        <div class="col-md-4"><b>اولین پاسخ:</b> {{ req.firstResponseAtText || "-" }}</div>
        <div class="col-md-4"><b>رفع‌شده در:</b> {{ req.resolvedAtText || "-" }}</div>
        <div class="col-md-8"><b>موعدها:</b> <SlaCell :r="req" /></div>
        <div class="col-12"><b>شرح:</b> {{ req.description }}</div>
        <div v-if="req.resolution" class="col-12"><b>راه‌حل:</b> {{ req.resolution }}</div>
        <div v-if="req.complaintId" class="col-12">
          <b>شکایت مشتری (کیفیت):</b>
          <a v-if="canQuality" href="#" @click.prevent="router.push('/quality/complaints')"><span class="inv-ltr">{{ req.complaintNumber }}</span></a>
          <span v-else class="inv-ltr">{{ req.complaintNumber }}</span>
        </div>
      </div>
      <p v-if="req.slaMissing" class="inv-muted">برای اولویت این درخواست ردیف SLA تعریف نشده است؛ در «تنظیمات» جدول SLA را کامل کنید.</p>

      <div v-if="available.length" class="d-flex gap-2 flex-wrap mb-3">
        <button v-for="a in available" :key="a" :disabled="busy" :class="a === 'resolve' ? 'btn-success' : a === 'close' ? 'btn-secondary' : 'btn-outline-primary'" class="btn btn-sm" @click="start(a as any)">{{ serviceActionTitles[a] }}</button>
        <button v-if="req.status === 1 || req.status === 2 || req.status === 3 || req.status === 4" class="btn btn-sm btn-outline-primary" @click="router.push({ path: '/service/orders', query: { requestId: String(req.id), unitId: req.unitId ? String(req.unitId) : undefined } })">دستور خدمات جدید</button>
      </div>
      <div v-if="pending" class="border rounded p-2 mb-3">
        <h3 class="inv-title mb-2">{{ serviceActionTitles[pending] }}</h3>
        <div class="row g-2">
          <div v-if="pending === 'assign'" class="col-md-4"><label class="form-label small mb-0">کاربر مسئول *</label><UserSelect v-model="f.userId" /></div>
          <div v-if="pending === 'resolve'" class="col-12"><label class="form-label small mb-0">شرح راه‌حل *</label><textarea v-model="f.resolution" class="form-control form-control-sm" rows="3"></textarea></div>
          <div v-if="pending !== 'close'" class="col-12"><label class="form-label small mb-0">زمان وقوع (خالی = اکنون؛ برای ثبت پاسخی که زودتر داده شده)</label><DateTimeInput v-model="f.at" /></div>
        </div>
        <div class="d-flex gap-2 mt-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="submit">تایید</button><button class="btn btn-sm btn-outline-secondary" @click="pending = null">انصراف</button></div>
      </div>
      <p v-if="available.includes('resolve') && openOrders.length" class="inv-muted">رفع درخواست فقط وقتی ممکن است که دستور خدمات بازی برای آن نمانده باشد.</p>

      <h3 class="inv-title mb-2">دستورهای خدمات این درخواست</h3>
      <table class="table table-sm">
        <thead><tr><th>شماره</th><th>عنوان</th><th>وضعیت</th><th>تصمیم</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!orders.length"><td colspan="5" class="text-center inv-muted">دستوری ساخته نشده است.</td></tr>
          <tr v-for="o in orders" :key="o.id">
            <td class="inv-ltr">{{ o.number }}</td><td>{{ o.title }}</td>
            <td><span :class="badgeOfServiceOrder(o.status)" class="inv-badge">{{ orderStatuses[o.status] }}</span></td><td>{{ o.decisionText }}</td>
            <td><button class="btn btn-sm btn-outline-primary" @click="router.push('/service/orders/' + o.id)">باز کردن</button></td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style lang="scss" src="../fn/fn.scss"></style>
