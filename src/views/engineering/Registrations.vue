<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRoute } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import EnAction from "@/components/engineering/EnAction.vue";
import EnChip from "@/components/engineering/EnChip.vue";
import EnPrompt from "@/components/engineering/EnPrompt.vue";
import EnWait from "@/components/engineering/EnWait.vue";
import { decideEnRegistration, getEnRegistration, getEnRegistrations } from "@/api/EngineeringAPIs";
import { useFocusItem, enPaths, EnRoles, faDigits, regStatuses, useEnDialog } from "@/composables/engineering";
import { usePagedList } from "@/composables/fn";

// Registration of a New design as a platform: the head of engineering proposes (from the case page), the CEO decides. The request never expires.
const route = useRoute();
const filters = ref({ Status: null as number | null, OnlyOpen: false });
const list = usePagedList(getEnRegistrations, filters, 25);
const one = useFocusItem(() => Number(route.params.id) || null, getEnRegistration);
const reload = async () => { await Promise.all([list.load(), one.load()]); };
onMounted(reload);
watch(() => route.params.id, reload);
const focus = computed(() => Number(route.params.id) || null);
const rows = computed(() => one.merge([...list.rows.value].sort((a, b) => Number(b.id === focus.value) - Number(a.id === focus.value) || a.status - b.status)));
const d = useEnDialog(reload);
function decide(x: any, decision: "approve" | "reject" | "withdraw") {
  d.ask({
    title: decision === "approve" ? "تایید ثبت پلتفرم" : decision === "reject" ? "رد ثبت پلتفرم" : "پس‌گرفتن پیشنهاد",
    fields: [{ key: "n", label: "یادداشت", type: "textarea", required: decision !== "approve", min: decision === "approve" ? undefined : 15, max: 2000 }],
    run: (v) => decideEnRegistration({ id: x.id, decision, note: v.n, rowVersion: x.rowVersion }),
    okText: "تصمیم ثبت شد.",
  });
}
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">ثبت پلتفرم جدید</h1></div>
      <div class="inv-filters mb-3">
        <select v-model="filters.Status" class="form-select form-select-sm" aria-label="وضعیت">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in regStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <label class="small"><input v-model="filters.OnlyOpen" type="checkbox" /> فقط باز</label>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <p v-if="list.loading.value" class="inv-muted">در حال خواندن…</p>
      <p v-else-if="!rows.length" class="inv-muted" data-test="en-empty">درخواست ثبتی نیست؛ مدیر مهندسی از صفحه پرونده‌ای که طراحی جدیدش منتشر شده پیشنهاد می‌دهد.</p>
      <div v-for="r in rows" :key="r.id" :class="['en-box', focus === r.id ? 'border-primary' : '']" data-test="en-registration">
        <div class="d-flex flex-wrap gap-2 align-items-center mb-1">
          <strong>{{ faDigits(r.number) }}</strong><EnChip :status="r.status" :text="r.statusText" group="reg" />
          <router-link :to="enPaths.case(r.caseId)" class="small">پرونده {{ faDigits(r.caseNumber) }}</router-link>
          <router-link :to="enPaths.platform(r.productId, r.referenceCode)" class="small">{{ r.productCode }}</router-link>
        </div>
        <EnWait v-if="r.status === 0" :hours="r.waitingHours" text="پیشنهاد ثبت پلتفرم منتظر تصمیم مدیرعامل است" />
        <dl class="en-kv">
          <dt>استاندارد</dt><dd class="inv-ltr">{{ r.referenceCode }}</dd><dt>مبنا</dt><dd>{{ r.standardBasis }}</dd><dt>توجیه</dt><dd class="en-pre">{{ r.rationale }}</dd>
          <dt>پیشنهاد</dt><dd>{{ r.proposedByName }}، {{ faDigits(r.proposedAtText || "") }}</dd>
          <dt v-if="r.decidedByName">تصمیم</dt><dd v-if="r.decidedByName">{{ r.decidedByName }}، {{ faDigits(r.decidedAtText || "") }}<span v-if="r.decisionNote"> — {{ r.decisionNote }}</span></dd>
        </dl>
        <EnAction :can="r.canDecide" :reason="r.decideBlockedReason" :roles="EnRoles.ceo" label="تایید" variant="success" @go="decide(r, 'approve')" />
        <EnAction :can="r.canDecide" :reason="r.decideBlockedReason" :roles="EnRoles.ceo" label="رد" variant="outline-danger" @go="decide(r, 'reject')" />
        <EnAction :can="r.canWithdraw" :reason="r.withdrawBlockedReason" :roles="EnRoles.manager" label="پس‌گرفتن" variant="outline-secondary" @go="decide(r, 'withdraw')" />
      </div>
      <QPager :page="list.page.value" :page-size="25" :total="list.total.value" @go="list.go" />
    </div>
    <EnPrompt v-if="d.dlg.value" :busy="d.busy.value" :error="d.error.value" :fields="d.dlg.value.fields" :hint="d.dlg.value.hint" :title="d.dlg.value.title" @cancel="d.close" @confirm="d.confirm" />
  </div>
</template>
