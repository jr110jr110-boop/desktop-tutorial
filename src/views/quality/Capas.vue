<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import QPager from "@/components/quality/QPager.vue";
import { escalateCapas, getCapas, getOverdueCapas } from "@/api/QualityAPIs";
import { fail, jalali, ok, printPage } from "@/composables/inventory";
import { capaSources, capaStatuses, usePaged } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// Corrective and preventive actions (FO-QA-017, ISO 9001 10.2). CAPAs of Major NCRs and audit findings are drafted by the system.
// Everybody signed in may open the page (R1q D13): the readers of the module see every CAPA, anybody else only the CAPAs he owns or has an action in.
const router = useRouter();
const tab = ref<"all" | "overdue">("all");
const filters = ref({ Status: null as number | null, Source: null as number | null, Contains: "" });
const all = usePaged(getCapas, filters);
const overdue = usePaged(getOverdueCapas, filters);
const canCreate = hasRole(QualityRoles.capaStaff);
const canEscalate = hasRole(QualityRoles.capaEscalate);
const seesAll = hasRole(QualityRoles.readers);
const escalating = ref(false);
// the answer of the last "escalate now": CapaEscalationResultDto { escalated, ceoNotified, firstRun, recorded, skipped }
const escalation = ref<any>(null);

function setTab(t: "all" | "overdue") {
  tab.value = t;
  (t === "all" ? all : overdue).search();
}
const src = () => (tab.value === "all" ? all : overdue);

const escalationText = computed(() => {
  const r = escalation.value;
  if (!r) return "";
  const parts: string[] = [];
  if (r.firstRun) {
    parts.push(
      `این اولین اجرای بررسی دیرکرد بود: سطح دیرکرد ${r.recorded || 0} CAPA که از قبل دیرکرد داشتند فقط ثبت شد و به کسی اعلان نرفت (تا انبوه CAPAهای قدیمی یک‌شبه صندوق تضمین کیفیت و مدیرعامل را پر نکند)؛ از اجرای بعد، دیرکردهای تازه اعلان می‌شوند.`
    );
  } else if (r.escalated) {
    parts.push(`${r.escalated} CAPA به سطح بالاتر دیرکرد رسید و اعلان شد` + (r.ceoNotified ? ` (برای ${r.ceoNotified} مورد به مدیرعامل هم اطلاع داده شد).` : "."));
  } else {
    parts.push("CAPA تازه‌ای به سطح بالاتر دیرکرد نرسید.");
  }
  if (r.skipped) parts.push(`${r.skipped} مورد هم‌زمان با تغییر دیگری بود یا اعلان آن نرسید و در اجرای بعدی (بررسی شبانه ساعت ۰۴:۰۰) دوباره بررسی می‌شود.`);
  return parts.join(" ");
});

async function escalateNow() {
  if (
    !confirm(
      "بررسی دیرکرد CAPAها همین حالا اجرا شود؟ این همان بررسی شبانه است: برای CAPAهای دیرکرددار به تضمین کیفیت و در دیرکرد طولانی به مدیرعامل اعلان می‌رود و هر سطح فقط یک‌بار اعلان می‌شود."
    )
  )
    return;
  escalating.value = true;
  const [data, error] = await escalateCapas();
  escalating.value = false;
  if (error) return fail(error);
  escalation.value = data || {};
  ok("بررسی دیرکرد CAPAها انجام شد.");
  src().load();
}

onMounted(all.load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="اقدام‌های اصلاحی و پیشگیرانه (CAPA)" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">اقدام اصلاحی و پیشگیرانه (CAPA)</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ فهرست</button>
          <button v-if="canEscalate" :disabled="escalating" class="btn btn-sm btn-outline-warning" title="همان بررسی شبانه دیرکرد؛ QA و PMO" @click="escalateNow">
            {{ escalating ? "در حال بررسی…" : "بررسی دیرکرد همین حالا" }}
          </button>
          <button v-if="canCreate" class="btn btn-sm btn-primary" @click="router.push({ name: 'QualityCapaEdit', params: { id: 'new' } })">CAPA جدید</button>
        </div>
      </div>
      <p v-if="!seesAll" class="inv-muted small d-print-none">این فهرست فقط CAPAهایی را نشان می‌دهد که شما مسئول آن یا مسئول یکی از اقدام‌های آن هستید.</p>
      <div v-if="escalation" class="alert alert-info small d-flex justify-content-between align-items-start d-print-none" role="status">
        <span>{{ escalationText }}</span>
        <button class="btn-close ms-2" type="button" aria-label="بستن" @click="escalation = null"></button>
      </div>
      <ul class="nav nav-tabs mb-3 d-print-none">
        <li class="nav-item"><button :class="{ active: tab === 'all' }" class="nav-link" @click="setTab('all')">همه</button></li>
        <li class="nav-item"><button :class="{ active: tab === 'overdue' }" class="nav-link" @click="setTab('overdue')">از موعد گذشته</button></li>
      </ul>
      <div class="inv-filters mb-3 d-print-none">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره یا شرح مساله" @keydown.enter="src().search()" />
        <select v-model="filters.Status" class="form-select form-select-sm" @change="src().search()"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in capaStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <select v-model="filters.Source" class="form-select form-select-sm" @change="src().search()"><option :value="null">همه منبع‌ها</option><option v-for="(t, k) in capaSources" :key="k" :value="Number(k)">{{ t }}</option></select>
        <button class="btn btn-sm btn-secondary" @click="src().search()">جستجو</button>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>منبع</th><th>مساله</th><th>مسئول</th><th>موعد</th><th>موعد اثربخشی</th><th>وضعیت</th></tr></thead>
        <tbody>
          <tr v-if="!src().rows.value.length"><td colspan="7" class="text-center inv-muted">موردی نیست.</td></tr>
          <tr v-for="c in src().rows.value" :key="c.id" :class="{ 'inv-muted': c.status === 5 }" role="button" @click="router.push({ name: 'QualityCapaEdit', params: { id: c.id } })">
            <td class="inv-ltr">{{ c.number }}</td>
            <td>{{ capaSources[c.source] }}</td>
            <td>{{ c.problem }} <span v-if="c.autoDrafted" class="inv-badge info">پیش‌نویس سیستم</span></td>
            <td>{{ c.ownerName || "تعیین نشده" }}</td>
            <td>{{ jalali(c.dueDate) }}</td>
            <td>{{ jalali(c.effectivenessDueDate) }}</td>
            <td><span :class="{ ok: c.status === 4, bad: c.overdue, warn: !c.overdue && c.status < 4 }" class="inv-badge">{{ capaStatuses[c.status] }}</span> <span v-if="c.overdue" class="inv-badge bad">از موعد گذشته</span></td>
          </tr>
        </tbody>
      </table>
      <QPager :page="src().page.value" :total="src().total.value" @go="src().go" />
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
