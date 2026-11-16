<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRoute } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import EnAction from "@/components/engineering/EnAction.vue";
import EnChip from "@/components/engineering/EnChip.vue";
import EnPrompt from "@/components/engineering/EnPrompt.vue";
import EnWait from "@/components/engineering/EnWait.vue";
import { decideEnDeviation, getEnDeviation, getEnDeviations } from "@/api/EngineeringAPIs";
import { useFocusItem, deviationStatuses, enPaths, EnRoles, faDigits, useEnDialog } from "@/composables/engineering";
import { usePagedList } from "@/composables/fn";

// Standards deviations (not applicable, reference-list loosening, emergency). Two approvals in order: the head of engineering, then the CEO; nobody is skipped and the request never expires.
const route = useRoute();
const filters = ref({ Status: null as number | null, OnlyOpen: false });
const list = usePagedList(getEnDeviations, filters, 25);
const one = useFocusItem(() => Number(route.params.id) || null, getEnDeviation);
const reload = async () => { await Promise.all([list.load(), one.load()]); };
onMounted(reload);
watch(() => route.params.id, reload);
const focus = computed(() => Number(route.params.id) || null);
const rows = computed(() => one.merge([...list.rows.value].sort((a, b) => Number(b.id === focus.value) - Number(a.id === focus.value) || Number(a.status > 1) - Number(b.status > 1) || a.status - b.status)));
const d = useEnDialog(reload);
function decide(x: any, decision: "approve" | "reject") {
  d.ask({
    title: (decision === "approve" ? "تایید انحراف " : "رد انحراف ") + faDigits(x.number),
    fields: [{ key: "n", label: "یادداشت", type: "textarea", required: decision === "reject", min: decision === "reject" ? 15 : undefined, max: 2000 }],
    run: (v) => decideEnDeviation({ id: x.id, decision, note: v.n, rowVersion: x.rowVersion }),
    okText: "تصمیم ثبت شد.",
  });
}
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">انحراف از استاندارد</h1></div>
      <div class="inv-filters mb-3">
        <select v-model="filters.Status" class="form-select form-select-sm" aria-label="وضعیت">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in deviationStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <label class="small"><input v-model="filters.OnlyOpen" type="checkbox" /> فقط باز</label>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <p v-if="list.loading.value" class="inv-muted">در حال خواندن…</p>
      <p v-else-if="!rows.length" class="inv-muted" data-test="en-empty">انحرافی ثبت نشده است.</p>
      <div v-for="x in rows" :id="'dev' + x.id" :key="x.id" :class="['en-box', focus === x.id ? 'border-primary' : '']" data-test="en-deviation">
        <div class="d-flex flex-wrap gap-2 align-items-center mb-1">
          <strong>{{ faDigits(x.number) }}</strong>
          <span class="inv-badge info">{{ x.kindText }}</span>
          <EnChip :status="x.status" :text="x.statusText" group="deviation" />
          <router-link v-if="x.caseId" :to="enPaths.case(x.caseId)" class="small">پرونده {{ faDigits(x.caseNumber || "") }}</router-link>
          <router-link v-if="x.productId" :to="enPaths.platform(x.productId, x.standardCode)" class="small">{{ x.productCode }}</router-link>
        </div>
        <EnWait v-if="x.status === 0 || x.status === 1" :hours="x.waitingHours" :text="x.status === 0 ? 'منتظر تصمیم مدیر مهندسی' : 'منتظر تصمیم مدیرعامل'" />
        <dl class="en-kv">
          <dt>استاندارد / بند</dt><dd class="inv-ltr">{{ x.standardCode }} {{ x.standardClause ? "— " + x.standardClause : "" }}</dd>
          <dt>الزام</dt><dd class="en-pre">{{ x.requirement }}</dd>
          <dt>دستور</dt><dd class="en-pre">{{ x.instruction }}</dd>
          <dt>درخواست‌کننده</dt><dd>{{ x.requestedByName }}، {{ faDigits(x.requestedAtText || "") }}</dd>
          <dt>۱) مدیر مهندسی</dt><dd>{{ x.managerApprovedByName ? x.managerApprovedByName + "، " + faDigits(x.managerApprovedAtText || "") : "—" }}</dd>
          <dt>۲) مدیرعامل</dt><dd>{{ x.ceoApprovedByName ? x.ceoApprovedByName + "، " + faDigits(x.ceoApprovedAtText || "") : "—" }}</dd>
          <dt v-if="x.rejectedByName">رد</dt><dd v-if="x.rejectedByName">{{ x.rejectedByName }}</dd>
          <dt v-if="x.decisionNote">یادداشت</dt><dd v-if="x.decisionNote" class="en-pre">{{ x.decisionNote }}</dd>
        </dl>
        <EnAction :can="x.canDecide" :reason="x.decideBlockedReason" :roles="[...EnRoles.manager, ...EnRoles.ceo]" label="تایید" variant="success" @go="decide(x, 'approve')" />
        <EnAction :can="x.canDecide" :reason="x.decideBlockedReason" :roles="[...EnRoles.manager, ...EnRoles.ceo]" label="رد" variant="outline-danger" @go="decide(x, 'reject')" />
      </div>
      <QPager :page="list.page.value" :page-size="25" :total="list.total.value" @go="list.go" />
    </div>
    <EnPrompt v-if="d.dlg.value" :busy="d.busy.value" :error="d.error.value" :fields="d.dlg.value.fields" :hint="d.dlg.value.hint" :title="d.dlg.value.title" @cancel="d.close" @confirm="d.confirm" />
  </div>
</template>
