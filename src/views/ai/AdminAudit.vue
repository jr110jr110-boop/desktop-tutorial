<script setup lang="ts">
// «ممیزی»: the append-only audit of the AI (questions, tool calls, unverified and degraded answers, failovers, admin changes, seat runs, decisions on notes, evaluations).
// Read: PMO and CEO. The rows are never changed or deleted.
import { onMounted, ref } from "vue";
import AiAdminTabs from "@/components/ai/AiAdminTabs.vue";
import FnDate from "@/components/fn/FnDate.vue";
import QPager from "@/components/quality/QPager.vue";
import { getAiAudit } from "@/api/AiAPIs";
import { auditKinds, seatTitleOf, seatTitles } from "@/composables/ai";
import { usePagedList } from "@/composables/fn";

const filters = ref<any>({ Kind: null, SeatCode: null, UserId: null, From: null, To: null });
const list = usePagedList(getAiAudit, filters, 50);
const open = ref<number | null>(null);
const seatCodes = Object.keys(seatTitles);

function search() {
  if (filters.value.UserId === "" || filters.value.UserId === undefined) filters.value.UserId = null;
  open.value = null;
  return list.search();
}
onMounted(list.load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">مدیریت هوش مصنوعی</h1></div>
      <AiAdminTabs />
      <div class="inv-toolbar"><h2 class="inv-title">ممیزی</h2></div>
      <div class="inv-filters mb-3">
        <div>
          <label class="form-label small mb-0">نوع رویداد</label>
          <select v-model="filters.Kind" class="form-select form-select-sm">
            <option :value="null">همه</option>
            <option v-for="(t, k) in auditKinds" :key="k" :value="k">{{ t }}</option>
          </select>
        </div>
        <div>
          <label class="form-label small mb-0">دستیار</label>
          <select v-model="filters.SeatCode" class="form-select form-select-sm">
            <option :value="null">همه</option>
            <option v-for="c in seatCodes" :key="c" :value="c">{{ seatTitles[c] }}</option>
          </select>
        </div>
        <div><label class="form-label small mb-0">شناسه کاربر</label><input v-model.number="filters.UserId" class="form-control form-control-sm inv-ltr" min="1" type="number" @keyup.enter="search" /></div>
        <div><label class="form-label small mb-0">از تاریخ</label><FnDate v-model="filters.From" /></div>
        <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="filters.To" /></div>
        <button class="btn btn-sm btn-secondary" @click="search">جستجو</button>
      </div>

      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>زمان</th><th>رویداد</th><th>دستیار</th><th>کاربر</th><th>مدل</th><th>تأخیر (ثانیه)</th><th>شرح</th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="7" class="text-center inv-muted">{{ list.loading.value ? "در حال بارگذاری..." : "رویدادی نیست." }}</td></tr>
            <template v-for="e in list.rows.value" :key="e.id">
              <tr role="button" @click="open = open === e.id ? null : e.id">
                <td>{{ e.atText }}</td>
                <td>{{ auditKinds[e.kind] || e.kind }}</td>
                <td>{{ e.seatCode ? seatTitleOf(e.seatCode) : "-" }}</td>
                <td>{{ e.userName || e.userId }}</td>
                <td class="inv-ltr">{{ e.model || "-" }}</td>
                <td>{{ e.latencyMs != null ? (e.latencyMs / 1000).toFixed(1) : "-" }}</td>
                <td class="detail-cell">{{ e.detail }}</td>
              </tr>
              <tr v-if="open === e.id">
                <td colspan="7" class="bg-light">
                  <div class="detail-full">{{ e.detail }}</div>
                  <div v-if="e.promptVersionId" class="inv-muted mt-1">نسخه دستور سیستم (شناسه): {{ e.promptVersionId }}</div>
                  <div v-if="e.promptHash" class="inv-muted inv-ltr">hash: {{ e.promptHash }}</div>
                </td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :page-size="list.pageSize" :total="list.total.value" @go="list.go" />
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style scoped>
.detail-cell {
  max-width: 420px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.detail-full {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  line-height: 1.7;
}
</style>
