<script setup lang="ts">
// Protected-labour monthly aggregate (SvLabour): FINANCEMANAGER / CEO post the labour of closed service orders as one accounting event per ended Persian month.
// The history of posted months comes from GET service/labour/history.
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import { getSvLabourHistory, runSvLabourAggregate } from "@/api/ServiceAPIs";
import { fail, ok } from "@/composables/inventory";
import { dateTimeText, periodText, rialText } from "@/composables/service";

const asOf = ref<string | null>(null);
const busy = ref(false);
const result = ref<any>(null);
const history = ref<any[]>([]);
const error = ref("");

async function loadHistory() {
  const [data, err] = await getSvLabourHistory();
  if (err) return fail(err);
  history.value = Array.isArray(data) ? data : [];
}
async function run() {
  busy.value = true;
  error.value = "";
  const [data, err] = await runSvLabourAggregate(asOf.value);
  busy.value = false;
  if (err) {
    error.value = err?.message || String(err);
    return fail(err);
  }
  result.value = data;
  await loadHistory();
  ok(data?.periods?.length ? "ثبت ماهانه انجام شد." : "ماهی برای ثبت نبود.");
}
onMounted(loadHistory);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <h1 class="inv-title mb-2">ثبت ماهانهٔ دستمزد محرمانه</h1>
      <p class="inv-muted">
        دستمزد دستورهای خدمات بسته‌شده به‌صورت یک رویداد حسابداری برای هر ماه شمسی پایان‌یافته و بدون شماره دستور ثبت می‌شود، تا نرخ ساعتی یک نفر از آن قابل استخراج نباشد. اجرای دوباره بی‌خطر است؛ هر دستور فقط یک بار می‌آید.
        فقط مدیر مالی و مدیرعامل می‌توانند اجرا کنند.
      </p>
      <div class="d-flex gap-2 align-items-end flex-wrap mb-3">
        <div><label class="form-label small mb-0">تا روز (خالی = امروز)</label><FnDate v-model="asOf" /></div>
        <button :disabled="busy" class="btn btn-sm btn-primary" @click="run">اجرای ثبت ماهانه</button>
      </div>
      <p v-if="error" class="alert alert-danger py-1 small">{{ error }}</p>

      <template v-if="result">
        <h3 class="inv-title mb-2">نتیجهٔ این اجرا</h3>
        <p v-if="!(result.periods || []).length" class="inv-muted">ماهی با دستمزد معلق برای ثبت نبود.</p>
        <table v-else class="table table-sm">
          <thead><tr><th>دوره</th><th>تعداد دستور</th><th>مبلغ رویداد (ریال)</th><th>مبلغ هزینه (ریال)</th></tr></thead>
          <tbody>
            <tr v-for="p in result.periods" :key="p.period"><td>{{ p.periodText || periodText(p.period) }}</td><td>{{ p.orders }}</td><td>{{ rialText(p.eventAmount) }}</td><td>{{ rialText(p.costAmount) }}</td></tr>
          </tbody>
        </table>
      </template>

      <h3 class="inv-title mt-3 mb-2">سابقهٔ ماه‌های ثبت‌شده</h3>
      <table class="table table-sm">
        <thead><tr><th>دوره</th><th>تعداد دستور</th><th>مبلغ رویداد (ریال)</th><th>مبلغ هزینه (ریال)</th><th>شناسهٔ رویداد حسابداری</th><th>زمان ثبت</th></tr></thead>
        <tbody>
          <tr v-if="!history.length"><td colspan="6" class="text-center inv-muted">سابقه‌ای نیست.</td></tr>
          <tr v-for="h in history" :key="h.period"><td>{{ h.periodText || periodText(h.period) }}</td><td>{{ h.orders }}</td><td>{{ rialText(h.eventAmount) }}</td><td>{{ rialText(h.costAmount) }}</td><td class="inv-ltr">{{ h.eventId ?? "-" }}</td><td>{{ dateTimeText(h.postedAt) }}</td></tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style lang="scss" src="../fn/fn.scss"></style>
