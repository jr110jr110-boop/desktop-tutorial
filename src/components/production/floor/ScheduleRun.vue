<script setup lang="ts">
import { ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import { runSchedule } from "@/api/ProductionAPIs";
import { camelize } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { hasRole, ProductionRoles } from "@/composables/roles";
import { persianOf } from "@/composables/production";

// Planning run: places the operations of released and running orders on the work-centre calendars and pushes the forecast finish to project control.
const canRun = hasRole(ProductionRoles.scheduleRunners);
const asOf = ref<string | null>(null);
const busy = ref(false);
const result = ref<any>(null);
const expanded = ref<number | null>(null);

async function run() {
  if (!confirm("برنامه‌ریزی دوباره اجرا شود؟ تاریخ‌های برنامه مراحل به‌روز می‌شود.")) return;
  busy.value = true;
  const [d, error] = await runSchedule(asOf.value);
  busy.value = false;
  if (error) return fail(error);
  result.value = camelize(d);
  ok("برنامه‌ریزی انجام شد.");
}
</script>

<template>
  <div>
    <div v-if="canRun" class="inv-filters mb-3">
      <FnDate v-model="asOf" placeholder="از تاریخ (خالی = امروز)" />
      <button :disabled="busy" class="btn btn-sm btn-primary" @click="run">اجرای برنامه‌ریزی</button>
      <span class="inv-muted small">اجرای دوباره همیشه همان نتیجه را برای همان ورودی‌ها می‌دهد.</span>
    </div>
    <p v-else class="inv-muted">اجرای برنامه‌ریزی فقط برای مدیر تولید، سرپرست کارگاه، برنامه‌ریزی و دفتر مدیریت پروژه است.</p>

    <template v-if="result">
      <div class="fn-tiles mb-3">
        <div class="fn-tile"><div class="t">تاریخ مبنا</div><div class="v">{{ persianOf(result.asOfText, result.asOf) }}</div></div>
        <div class="fn-tile"><div class="t">دستور تولید</div><div class="v">{{ result.orders }}</div></div>
        <div class="fn-tile"><div class="t">مراحل برنامه‌ریزی‌شده</div><div class="v">{{ result.operations }}</div></div>
        <div class="fn-tile"><div class="t">دستورهای دیرکرد</div><div :class="{ 'text-danger': result.lateOrders > 0 }" class="v">{{ result.lateOrders }}</div></div>
        <div class="fn-tile"><div class="t">مراحل بدون جای برنامه</div><div :class="{ 'text-danger': result.unscheduled > 0 }" class="v">{{ result.unscheduled }}</div></div>
        <div class="fn-tile"><div class="t">پیش‌بینی ارسال‌شده به کنترل پروژه</div><div class="v">{{ result.forecastsPushed }}</div></div>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>اولویت</th><th>دستور</th><th>تاریخ نیاز</th><th>شروع برنامه</th><th>پایان برنامه</th><th>دیرکرد (روز)</th><th>کیت</th><th>توضیح</th></tr></thead>
          <tbody>
            <tr v-if="!(result.items || []).length"><td colspan="8" class="text-center inv-muted">دستوری برای برنامه‌ریزی نبود.</td></tr>
            <template v-for="o in result.items || []" :key="o.orderId">
              <tr :class="{ 'table-danger': o.isLate }" style="cursor: pointer" @click="expanded = expanded === o.orderId ? null : o.orderId">
                <td>{{ o.priority }}</td>
                <td class="inv-ltr">{{ o.number }}</td>
                <td class="inv-ltr">{{ persianOf(o.needDateText, o.needDate) }}</td>
                <td class="inv-ltr">{{ persianOf(o.plannedStartText, o.plannedStart) }}</td>
                <td class="inv-ltr">{{ persianOf(o.plannedFinishText, o.plannedFinish) }}</td>
                <td class="fn-num">{{ o.isLate ? o.daysLate : "-" }}</td>
                <td><span v-if="o.kitShortage" class="inv-badge warn">کمبود</span><span v-else class="inv-muted">{{ persianOf(o.kitReadyDateText, o.kitReadyDate) }}</span></td>
                <td class="small">{{ o.note }}</td>
              </tr>
              <tr v-if="expanded === o.orderId">
                <td colspan="8">
                  <table class="table table-sm mb-0">
                    <thead><tr><th>برگه کار</th><th>مرحله</th><th>مرکز کار</th><th>ساعت</th><th>شروع</th><th>پایان</th><th>توضیح</th></tr></thead>
                    <tbody>
                      <tr v-for="p in o.operations || []" :key="p.operationId">
                        <td class="inv-ltr">{{ p.jobCard }}</td><td>{{ p.sequence }} - {{ p.title }}</td><td>{{ p.workCenterCode }}</td><td class="fn-num">{{ p.hours }}</td>
                        <td class="inv-ltr">{{ persianOf(p.plannedStartText, p.plannedStart, true) }}</td><td class="inv-ltr">{{ persianOf(p.plannedFinishText, p.plannedFinish, true) }}</td><td class="small">{{ p.note }}</td>
                      </tr>
                    </tbody>
                  </table>
                </td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>
    </template>
  </div>
</template>
