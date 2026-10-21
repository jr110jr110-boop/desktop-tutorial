<script setup lang="ts">
import { nextTick, onMounted, ref } from "vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import AssetHandoverPrint from "@/components/fn/print/AssetHandoverPrint.vue";
import DepreciationSchedulePrint from "@/components/fn/print/DepreciationSchedulePrint.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import { getAssetHandover, getAssetMovement, getDepreciationSchedule, getEmployeeDirectory } from "@/api/FnAPIs";
import { camelize, jalaliNow, money, monthNames, parseAmount, printSheet } from "@/composables/fn";
import { fail } from "@/composables/inventory";

// Asset reports: depreciation schedule, movement of cost and accumulated depreciation by class, and the handover list of a custodian.
const tab = ref("schedule");
const tabs = [{ key: "schedule", title: "جدول استهلاک" }, { key: "movement", title: "گردش دارایی‌ها" }, { key: "handover", title: "تحویل اموال" }];
const year = ref(String(jalaliNow().year));
const schedule = ref<any>(null);
const movement = ref<any>(null);
const handover = ref<any>(null);
const custodian = ref<any>(null);
const custodianText = ref("");
const printWhat = ref<"" | "schedule" | "handover">("");

const rowsOf = (d: any): any[] => (Array.isArray(d) ? d : d?.data || []);
const searchEmployees = async (t: string) => rowsOf((await getEmployeeDirectory(t))[0]);
const employeeLabel = (e: any) => `${e.name}${e.jobTitle ? " - " + e.jobTitle : ""}`;

async function loadSchedule() {
  const y = parseAmount(year.value);
  if (!y) return fail("سال را وارد کنید.");
  const [d, error] = await getDepreciationSchedule(y);
  if (error) return fail(error);
  schedule.value = camelize(d);
}
async function loadMovement() {
  const y = parseAmount(year.value);
  if (!y) return fail("سال را وارد کنید.");
  const [d, error] = await getAssetMovement(y);
  if (error) return fail(error);
  movement.value = camelize(d);
}
async function loadHandover() {
  if (!custodian.value && !custodianText.value.trim()) return fail("امین دارایی را انتخاب کنید یا نام او را بنویسید.");
  const [d, error] = await getAssetHandover(custodian.value?.id || null, custodian.value ? undefined : custodianText.value.trim());
  if (error) return fail(error);
  handover.value = camelize(d);
}
async function print(what: "schedule" | "handover") {
  printWhat.value = what;
  await nextTick();
  printSheet();
}
onMounted(loadSchedule);
</script>

<template>
  <div class="inv-page">
    <DepreciationSchedulePrint v-if="printWhat === 'schedule' && schedule" :data="schedule" />
    <AssetHandoverPrint v-if="printWhat === 'handover' && handover" :data="handover" />

    <div class="inv-card screen-only">
      <div class="inv-toolbar"><h1 class="inv-title">گزارش‌های دارایی ثابت</h1></div>
      <PcTabs v-model="tab" :tabs="tabs" />

      <template v-if="tab !== 'handover'">
        <div class="inv-filters mb-3">
          <div><label class="form-label small mb-0">سال شمسی</label><input v-model="year" class="form-control form-control-sm fn-num" style="width: 6rem" /></div>
          <button class="btn btn-sm btn-secondary align-self-end" @click="tab === 'schedule' ? loadSchedule() : loadMovement()">نمایش</button>
          <button v-if="tab === 'schedule' && schedule" class="btn btn-sm btn-outline-secondary align-self-end" @click="print('schedule')">چاپ</button>
        </div>
      </template>

      <div v-if="tab === 'schedule' && schedule" class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>رده</th><th v-for="m in monthNames" :key="m">{{ m }}</th><th>جمع</th></tr></thead>
          <tbody>
            <tr v-for="c in schedule.classes" :key="c.classCode"><td>{{ c.classCode }}</td><td v-for="(v, i) in c.months" :key="i" class="fn-num">{{ money(v) }}</td><td class="fn-num">{{ money(c.total) }}</td></tr>
            <tr v-if="!schedule.classes?.length"><td :colspan="14" class="text-center inv-muted">استهلاک تاییدشده‌ای در این سال نیست.</td></tr>
          </tbody>
          <tfoot><tr class="fn-total"><td :colspan="13">جمع سال</td><td class="fn-num">{{ money(schedule.total) }}</td></tr></tfoot>
        </table>
      </div>

      <div v-if="tab === 'movement' && movement" class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>رده</th><th>بهای اول سال</th><th>افزایش</th><th>واگذاری</th><th>بهای پایان</th><th>انباشته اول سال</th><th>استهلاک سال</th><th>انباشته واگذاری</th><th>انباشته پایان</th><th>ارزش دفتری</th></tr></thead>
          <tbody>
            <tr v-for="r in movement.rows" :key="r.classCode"><td>{{ r.classTitle }}</td><td class="fn-num">{{ money(r.openingCost) }}</td><td class="fn-num">{{ money(r.additions) }}</td><td class="fn-num">{{ money(r.disposals) }}</td><td class="fn-num">{{ money(r.closingCost) }}</td><td class="fn-num">{{ money(r.openingAccumulated) }}</td><td class="fn-num">{{ money(r.depreciation) }}</td><td class="fn-num">{{ money(r.disposalAccumulated) }}</td><td class="fn-num">{{ money(r.closingAccumulated) }}</td><td class="fn-num">{{ money(r.netBookValue) }}</td></tr>
          </tbody>
          <tfoot v-if="movement.total"><tr class="fn-total"><td>جمع</td><td class="fn-num">{{ money(movement.total.openingCost) }}</td><td class="fn-num">{{ money(movement.total.additions) }}</td><td class="fn-num">{{ money(movement.total.disposals) }}</td><td class="fn-num">{{ money(movement.total.closingCost) }}</td><td class="fn-num">{{ money(movement.total.openingAccumulated) }}</td><td class="fn-num">{{ money(movement.total.depreciation) }}</td><td class="fn-num">{{ money(movement.total.disposalAccumulated) }}</td><td class="fn-num">{{ money(movement.total.closingAccumulated) }}</td><td class="fn-num">{{ money(movement.total.netBookValue) }}</td></tr></tfoot>
        </table>
      </div>

      <template v-if="tab === 'handover'">
        <div class="inv-filters mb-3">
          <div style="min-width: 16rem"><label class="form-label small mb-0">امین دارایی</label><LookupBox v-model="custodian" :fetch="searchEmployees" :label="employeeLabel" placeholder="نام کارمند" /></div>
          <div><label class="form-label small mb-0">یا نام (برای غیر کارمند)</label><input v-model="custodianText" class="form-control form-control-sm" /></div>
          <button class="btn btn-sm btn-secondary align-self-end" @click="loadHandover">نمایش</button>
          <button v-if="handover" class="btn btn-sm btn-outline-secondary align-self-end" @click="print('handover')">چاپ صورت‌جلسه</button>
        </div>
        <div v-if="handover" class="table-responsive">
          <p class="small">{{ handover.custodianName }} {{ handover.custodianTitle }}</p>
          <table class="table table-sm">
            <thead><tr><th>کد</th><th>شرح</th><th>سریال</th><th>محل</th><th>بها (ریال)</th></tr></thead>
            <tbody>
              <tr v-if="!handover.assets?.length"><td colspan="5" class="text-center inv-muted">دارایی‌ای در عهده این فرد نیست.</td></tr>
              <tr v-for="a in handover.assets" :key="a.id"><td class="inv-ltr">{{ a.code }}</td><td>{{ a.title }}</td><td class="inv-ltr">{{ a.serial }}</td><td>{{ a.location }}</td><td class="fn-num">{{ money(a.cost) }}</td></tr>
            </tbody>
          </table>
        </div>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
