<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { getEnOverview } from "@/api/EngineeringAPIs";
import EnWait from "@/components/engineering/EnWait.vue";
import { completeness, enPaths, EnRoles, faNum } from "@/composables/engineering";
import { fail } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";

// The overview of the engineering control (GET engineering/overview, counts only). Every card links to the list it counts.
// What waits for the CEO or the engineering manager never expires: it is shown first, with how long it has waited.
const o = ref<any>(null);
const loading = ref(true);
const failed = ref("");
const canInfo = hasRole(EnRoles.infoReaders);

async function load() {
  loading.value = true;
  const [d, e] = await getEnOverview();
  loading.value = false;
  if (e) {
    failed.value = e.message || "خواندن نمای کلی ممکن نشد.";
    return fail(e);
  }
  failed.value = "";
  o.value = d;
}
onMounted(load);

const n = (v: any) => faNum(Number(v || 0), 0);
const dossierTotal = computed(() => (o.value?.platformsByDossierStatus || []).reduce((s: number, x: any) => s + x.count, 0));
const dossierComplete = computed(() => (o.value?.platformsByDossierStatus || []).filter((x: any) => x.status === 3).reduce((s: number, x: any) => s + x.count, 0));
const pct = computed(() => completeness(dossierTotal.value, dossierComplete.value));
const waiting = computed(() => {
  const x = o.value;
  if (!x) return [];
  const rows: { text: string; hours: number | null; to: string }[] = [];
  if (x.waiversAwaitingCeo > 0) rows.push({ text: `${n(x.waiversAwaitingCeo)} درخواست مجوز اضطرار منتظر تصمیم مدیرعامل است`, hours: x.oldestWaiverWaitingHours, to: enPaths.waivers });
  // Who the deviations wait for and the oldest waits come from the server (deviationsAwaitingManager, deviationsAwaitingCeo, oldestDeviationWaitingHours, oldestRegistrationWaitingHours).
  if (x.deviationsAwaiting > 0) rows.push({ text: `${n(x.deviationsAwaiting)} انحراف از استاندارد منتظر تصمیم است (${n(x.deviationsAwaitingManager)} مورد منتظر مدیر مهندسی، ${n(x.deviationsAwaitingCeo)} مورد منتظر مدیرعامل)`, hours: x.oldestDeviationWaitingHours, to: enPaths.deviations });
  if (x.registrationsOpen > 0) rows.push({ text: `${n(x.registrationsOpen)} درخواست ثبت پلتفرم منتظر تصمیم مدیرعامل است`, hours: x.oldestRegistrationWaitingHours, to: enPaths.registrations });
  return rows;
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">مهندسی: نمای کلی</h1>
        <button :disabled="loading" class="btn btn-sm btn-outline-secondary" @click="load">تازه‌سازی</button>
      </div>
      <p v-if="loading && !o" class="inv-muted">در حال خواندن…</p>
      <div v-if="failed" class="en-banner red" role="alert">{{ failed }}</div>

      <template v-if="o">
        <div v-if="!o.controlStarted" class="en-banner blue">کنترل مهندسی هنوز آغاز نشده است{{ o.controlStartText ? ": " + o.controlStartText : "" }}؛ پرونده‌های پیش از آن «قدیمی» شمرده می‌شوند.</div>
        <router-link v-for="w in waiting" :key="w.text" :to="w.to" class="d-block text-decoration-none mb-2">
          <EnWait :hours="w.hours" :text="w.text" />
        </router-link>
        <router-link v-if="o.overdueDebts > 0" :to="{ path: enPaths.debts, query: { overdue: 1 } }" class="d-block text-decoration-none en-banner red" data-test="en-overdue-debts">{{ n(o.overdueDebts) }} بدهی مدارک از موعد گذشته است.</router-link>
        <div v-if="o.casesAtDocumentRisk > 0" :class="['en-banner', o.awardedCasesAtDocumentRisk > 0 ? 'red' : 'amber']" data-test="en-risk">
          {{ n(o.casesAtDocumentRisk) }} پرونده در معرض خطر تحویل مدارک است<span v-if="o.awardedCasesAtDocumentRisk > 0"> که {{ n(o.awardedCasesAtDocumentRisk) }} مورد آن برنده شده و در مهندسی است</span>.
          <router-link :to="enPaths.cases">دیدن پرونده‌ها</router-link>
        </div>

        <h2 class="h6 mt-3">پرونده‌ها بر اساس مرحله</h2>
        <div class="en-tiles">
          <router-link v-for="c in o.casesByStatus" :key="c.key" :to="{ path: enPaths.cases, query: { status: c.key } }" class="en-tile"><div class="t">{{ c.text }}</div><div class="v">{{ n(c.count) }}</div></router-link>
          <p v-if="!o.casesByStatus.length" class="inv-muted">هنوز پرونده‌ای نیست.</p>
        </div>
        <h2 class="h6">بر اساس نوع</h2>
        <div class="en-tiles">
          <router-link v-for="c in o.casesByClassification" :key="c.key" :to="{ path: enPaths.cases, query: { classification: c.key } }" class="en-tile"><div class="t">{{ c.text }}</div><div class="v">{{ n(c.count) }}</div></router-link>
          <p v-if="!o.casesByClassification?.length" class="inv-muted">پرونده‌ای برای تفکیک نوع نیست.</p>
        </div>

        <h2 class="h6">پرونده مدارک پلتفرم‌ها</h2>
        <div class="en-box">
          <div class="d-flex flex-wrap align-items-center gap-3 mb-2">
            <span>{{ n(o.platformsIncomplete) }} ناقص از {{ n(o.platformsTotal) }}</span>
            <div v-if="pct !== null" :class="['en-bar flex-grow-1', pct < 100 ? 'partial' : '']" :title="pct + '٪'"><span :style="{ width: pct + '%' }"></span></div>
            <span v-if="pct !== null" class="small">{{ faNum(pct, 0) }}٪ کامل</span>
            <router-link :to="enPaths.platforms">فهرست پلتفرم‌ها</router-link>
          </div>
          <div class="en-tiles mb-0">
            <router-link v-for="p in o.platformsByDossierStatus" :key="p.status" :to="{ path: enPaths.platforms, query: { status: p.status } }" :class="['en-tile', p.status === 1 && p.count ? 'warn' : '']">
              <div class="t">{{ p.statusText }}</div><div class="v">{{ n(p.count) }}</div><div v-if="p.missingRequired" class="n">{{ n(p.missingRequired) }} مدرک الزامی کم است</div>
            </router-link>
          </div>
        </div>

        <h2 class="h6">تحویل مدارک و بدهی</h2>
        <div class="en-tiles">
          <router-link v-for="h in o.handoversWaiting" :key="h.key" :to="{ path: enPaths.handovers, query: { status: h.key } }" class="en-tile"><div class="t">تحویل: {{ h.text }}</div><div class="v">{{ n(h.count) }}</div></router-link>
          <router-link :to="enPaths.debts" :class="['en-tile', o.openDebts ? 'warn' : '']"><div class="t">بدهی مدارک باز</div><div class="v">{{ n(o.openDebts) }}</div></router-link>
          <router-link :to="{ path: enPaths.debts, query: { overdue: 1 } }" :class="['en-tile', o.overdueDebts ? 'bad' : 'ok']"><div class="t">بدهی گذشته از موعد</div><div class="v">{{ n(o.overdueDebts) }}</div></router-link>
          <router-link :to="enPaths.waivers" :class="['en-tile', o.waiversAwaitingCeo ? 'bad' : '']"><div class="t">مجوز اضطرار منتظر مدیرعامل</div><div class="v">{{ n(o.waiversAwaitingCeo) }}</div></router-link>
          <router-link :to="enPaths.handovers" class="en-tile"><div class="t">ابلاغ بدون تایید دریافت</div><div class="v">{{ n(o.distributionsUnacknowledged) }}</div></router-link>
        </div>

        <h2 class="h6">استاندارد و شفاف‌سازی</h2>
        <div class="en-tiles">
          <router-link :to="enPaths.cases" class="en-tile"><div class="t">شفاف‌سازی باز</div><div class="v">{{ n(o.openClarifications) }}</div></router-link>
          <router-link :to="enPaths.deviations" :class="['en-tile', o.deviationsAwaiting ? 'warn' : '']"><div class="t">انحراف منتظر تصمیم</div><div class="v">{{ n(o.deviationsAwaiting) }}</div><div class="n">{{ n(o.standardsDeviationsOpen) }} انحراف باز</div></router-link>
          <router-link :to="enPaths.gaps" class="en-tile"><div class="t">تفاوت استاندارد باز</div><div class="v">{{ n(o.standardGapsOpen) }}</div></router-link>
          <router-link :to="enPaths.registrations" class="en-tile"><div class="t">ثبت پلتفرم جدید باز</div><div class="v">{{ n(o.registrationsOpen) }}</div></router-link>
          <router-link v-if="canInfo" :to="enPaths.infoRequests" class="en-tile"><div class="t">پرسش واحدها از PMO</div><div class="v">{{ n(o.infoRequestsOpen) }}</div></router-link>
          <div v-else class="en-tile"><div class="t">پرسش واحدها از PMO</div><div class="v">{{ n(o.infoRequestsOpen) }}</div></div>
        </div>
      </template>
    </div>
  </div>
</template>
