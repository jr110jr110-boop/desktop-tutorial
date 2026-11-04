<script setup lang="ts">
// The generic dashboard: KPI cards, charts and tables of one dashboard answer. Every widget links to the page it comes from (a drill-down); money that the role may not see is shown as a dash.
import DashChart from "@/components/dashboards/DashChart.vue";

defineProps<{ dashboard: any }>();
const toneClass = (t: string) => (t === "ok" ? "ok" : t === "warn" ? "warn" : t === "bad" ? "bad" : "info");
const fmt = (v: number | null | undefined) => (v === null || v === undefined ? "—" : Math.abs(v) >= 1000 ? Math.round(v).toLocaleString("fa-IR") : Number(v.toFixed(2)).toLocaleString("fa-IR"));
</script>

<template>
  <div>
    <p v-if="dashboard.pricesMasked" class="inv-muted small mb-2">مبالغ ریالی برای نقش شما پنهان است؛ شاخص‌ها و تعدادها نمایش داده می‌شود.</p>
    <div class="dash-kpis">
      <component :is="k.link ? 'router-link' : 'div'" v-for="k in dashboard.kpis" :key="k.key" :to="k.link || undefined" class="dash-kpi" :class="toneClass(k.tone)">
        <span class="dash-kpi-title">{{ k.title }}</span>
        <b class="dash-kpi-value">{{ k.masked ? "—" : fmt(k.value) }}</b>
        <span v-if="k.unit && !k.masked" class="dash-kpi-unit">{{ k.unit }}</span>
      </component>
    </div>
    <div class="dash-grid">
      <section v-for="c in dashboard.charts" :key="dashboard.key + c.key" class="dash-card">
        <header>
          <h3 class="h6 mb-0">{{ c.title }}</h3>
          <router-link v-if="c.link" :to="c.link" class="small">فهرست</router-link>
        </header>
        <DashChart :chart="c" />
      </section>
      <section v-for="t in dashboard.tables" :key="dashboard.key + t.key" class="dash-card dash-wide">
        <header>
          <h3 class="h6 mb-0">{{ t.title }}</h3>
          <router-link v-if="t.link" :to="t.link" class="small">همه</router-link>
        </header>
        <div class="table-responsive">
          <table class="table table-sm mb-0">
            <thead><tr><th v-for="c in t.columns" :key="c">{{ c }}</th></tr></thead>
            <tbody>
              <tr v-if="!t.rows.length"><td :colspan="t.columns.length" class="text-center inv-muted">موردی نیست.</td></tr>
              <tr v-for="(r, i) in t.rows" :key="i">
                <td v-for="(cell, j) in r" :key="j"><router-link v-if="j === 0 && t.rowLinks && t.rowLinks[i]" :to="t.rowLinks[i]">{{ cell }}</router-link><template v-else>{{ cell }}</template></td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>
    </div>
  </div>
</template>

<style scoped>
.dash-kpis { display: grid; grid-template-columns: repeat(auto-fill, minmax(190px, 1fr)); gap: 12px; margin-bottom: 16px; }
.dash-kpi { display: flex; flex-direction: column; gap: 2px; padding: 12px 14px; border: 1px solid var(--bs-border-color, #dee2e6); border-inline-start: 4px solid #2563eb; border-radius: 8px; background: var(--bs-body-bg, #fff); color: inherit; text-decoration: none; }
.dash-kpi.ok { border-inline-start-color: #059669; }
.dash-kpi.warn { border-inline-start-color: #d97706; }
.dash-kpi.bad { border-inline-start-color: #dc2626; }
.dash-kpi-title { font-size: 0.8rem; opacity: 0.75; }
.dash-kpi-value { font-size: 1.5rem; line-height: 1.2; }
.dash-kpi-unit { font-size: 0.75rem; opacity: 0.6; }
.dash-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); gap: 16px; }
.dash-card { border: 1px solid var(--bs-border-color, #dee2e6); border-radius: 8px; padding: 12px; background: var(--bs-body-bg, #fff); min-width: 0; }
.dash-card header { display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 8px; }
.dash-wide { grid-column: 1 / -1; }
</style>
