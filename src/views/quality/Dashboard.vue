<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { getQualityDashboard } from "@/api/QualityAPIs";
import { fail, jalali, num } from "@/composables/inventory";
import { alertLevelClass } from "@/composables/quality";

// Quality dashboard: counts and lists are computed by the API from the records (no manual lists).
const router = useRouter();
const data = ref<any>(null);

async function load() {
  const [result, error] = await getQualityDashboard();
  if (error) return fail(error);
  data.value = result;
}

function open(link: string) {
  if (link) router.push(link);
}

const tiles = (d: any) => [
  { title: "NCR باز - عمده", value: num(d.ncrs.major), link: "/inventory/ncr", warn: d.ncrs.major > 0 },
  { title: "NCR باز - جزئی", value: num(d.ncrs.minor), link: "/inventory/ncr" },
  { title: "NCR تعیین‌نشده", value: num(d.ncrs.notClassified), link: "/inventory/ncr", warn: d.ncrs.notClassified > 0 },
  { title: "NCR از مهلت گذشته", value: num(d.ncrs.overdue), link: "/inventory/ncr", warn: d.ncrs.overdue > 0 },
  { title: "CAPA باز", value: num(d.capaOpen), link: "/quality/capa" },
  { title: "CAPA از موعد گذشته", value: num(d.overdueCapas), link: "/quality/capa", warn: d.overdueCapas > 0 },
  { title: "کالیبراسیون سررسیدگذشته", value: num(d.calibrationOverdue), link: "/quality/calibration", warn: d.calibrationOverdue > 0 },
  { title: "کالیبراسیون تا ۳۰ روز آینده", value: num(d.calibrationDue30), link: "/quality/calibration" },
  { title: "موارد خارج از تلرانس باز", value: num(d.outOfToleranceOpen), link: "/quality/calibration", warn: d.outOfToleranceOpen > 0 },
  { title: "نقطه توقف (H) در انتظار", value: num(d.pendingHPoints), link: "/quality/itp", warn: d.pendingHPoints > 0 },
  { title: "مهلت اعلام نقطه شاهد (W)", value: num(d.witnessNoticesDue), link: "/quality/itp" },
  { title: "گواهی جوشکار در حال قطع تداوم", value: num(d.qualificationsLapsing), link: "/quality/personnel", warn: d.qualificationsLapsing > 0 },
  { title: "گواهی منقضی", value: num(d.qualificationsExpired), link: "/quality/personnel" },
  { title: "برگه ترخیص در انتظار", value: num(d.releaseNotesPending), link: "/quality/release-notes" },
  { title: "شکایت مشتری باز", value: num(d.complaintsOpen), link: "/quality/complaints" },
  { title: "شکایت از مهلت پاسخ گذشته", value: num(d.complaintsOverdue), link: "/quality/complaints", warn: d.complaintsOverdue > 0 },
];

onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div v-if="data" class="inv-card">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">داشبورد کیفیت</h1>
          <span class="inv-muted">به‌روز شده: {{ jalali(data.generatedAt, true) }} — همه اعداد از رکوردها محاسبه می‌شود</span>
        </div>
        <button class="btn btn-sm btn-outline-secondary" @click="load">بازخوانی</button>
      </div>

      <div v-if="data.alerts.length" class="mb-3">
        <h2 class="inv-title mb-2">هشدارها</h2>
        <div v-for="a in data.alerts" :key="a.code" :class="alertLevelClass[a.level]" class="alert py-2 mb-1 d-flex justify-content-between">
          <span>{{ a.text }}</span>
          <a v-if="a.link" href="#" @click.prevent="open(a.link)">مشاهده</a>
        </div>
      </div>

      <div class="q-tiles mb-3">
        <button v-for="t in tiles(data)" :key="t.title" :class="{ warn: t.warn }" class="q-tile" @click="open(t.link)">
          <span class="q-value">{{ t.value }}</span>
          <span class="q-title">{{ t.title }}</span>
        </button>
      </div>

      <div class="row g-3">
        <div class="col-lg-6">
          <h2 class="inv-title mb-2">CAPA از موعد گذشته</h2>
          <table class="table table-sm table-hover">
            <thead><tr><th>شماره</th><th>مساله</th><th>مسئول</th><th>موعد</th></tr></thead>
            <tbody>
              <tr v-if="!data.overdueCapaList.length"><td colspan="4" class="text-center inv-muted">موردی نیست.</td></tr>
              <tr v-for="c in data.overdueCapaList" :key="c.id" role="button" @click="router.push('/quality/capa/' + c.id)">
                <td class="inv-ltr">{{ c.number }}</td>
                <td>{{ c.problem }} <span v-if="c.autoDrafted" class="inv-badge info">پیش‌نویس سیستم</span></td>
                <td>{{ c.ownerName || "تعیین نشده" }}</td>
                <td>{{ jalali(c.dueDate) }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <div class="col-lg-6">
          <h2 class="inv-title mb-2">تجهیزات نیازمند کالیبراسیون</h2>
          <table class="table table-sm table-hover">
            <thead><tr><th>کد</th><th>نام</th><th>سررسید</th><th>روز مانده</th></tr></thead>
            <tbody>
              <tr v-if="!data.calibrationList.length"><td colspan="4" class="text-center inv-muted">موردی نیست.</td></tr>
              <tr v-for="e in data.calibrationList" :key="e.id" role="button" @click="router.push('/quality/calibration/' + e.id)">
                <td class="inv-ltr">{{ e.code }}</td>
                <td>{{ e.name }}</td>
                <td>{{ jalali(e.nextDue) || "کالیبره نشده" }}</td>
                <td><span :class="e.daysToDue === null || e.daysToDue < 0 ? 'bad' : 'warn'" class="inv-badge">{{ e.daysToDue === null ? "-" : e.daysToDue }}</span></td>
              </tr>
            </tbody>
          </table>
        </div>
        <div class="col-lg-6">
          <h2 class="inv-title mb-2">گواهی‌های در آستانه قطع یا انقضا</h2>
          <table class="table table-sm table-hover">
            <thead><tr><th>شخص</th><th>نوع</th><th>روش</th><th>تاریخ</th><th>علت</th></tr></thead>
            <tbody>
              <tr v-if="!data.qualificationList.length"><td colspan="5" class="text-center inv-muted">موردی نیست.</td></tr>
              <tr v-for="q in data.qualificationList" :key="q.id" role="button" @click="router.push('/quality/personnel')">
                <td>{{ q.personName }}</td><td>{{ q.kind }}</td><td class="inv-ltr">{{ q.method }}</td><td>{{ jalali(q.date) }}</td><td>{{ q.reason }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <div class="col-lg-6">
          <h2 class="inv-title mb-2">نقطه‌های H و W در انتظار به تفکیک دستور ساخت</h2>
          <table class="table table-sm table-hover">
            <thead><tr><th>دستور ساخت</th><th>H</th><th>W</th><th>نزدیک‌ترین مهلت شاهد</th></tr></thead>
            <tbody>
              <tr v-if="!data.pendingPoints.length"><td colspan="4" class="text-center inv-muted">موردی نیست.</td></tr>
              <tr v-for="p in data.pendingPoints" :key="p.workOrderId" role="button" @click="router.push({ path: '/quality/itp', query: { workOrderId: p.workOrderId } })">
                <td class="inv-ltr">{{ p.workOrderCode }}</td><td>{{ p.hPoints }}</td><td>{{ p.wPoints }}</td><td>{{ jalali(p.earliestWitnessDeadline) }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style scoped>
.q-tiles { display: grid; grid-template-columns: repeat(auto-fill, minmax(190px, 1fr)); gap: 0.6rem; }
.q-tile { display: flex; flex-direction: column; gap: 0.15rem; text-align: right; padding: 0.7rem 0.8rem; border: 1px solid #dfe3e8; border-radius: 8px; background: #fff; }
.q-tile.warn { border-color: #e0a800; background: #fff9e6; }
.q-value { font-size: 1.25rem; font-weight: 700; }
.q-title { font-size: 0.8rem; color: #555; }
</style>
