<script setup lang="ts">
// The questions of the units to the PMO (a unit user asks, the PMO answers, the asker closes). Only the people who may read this list load it (EnRoles.infoReaders).
// The answer must not contain a price: the server's hint is written in the answer dialog.
import { computed, onMounted, ref, watch } from "vue";
import EnAction from "@/components/engineering/EnAction.vue";
import EnChip from "@/components/engineering/EnChip.vue";
import EnPrompt from "@/components/engineering/EnPrompt.vue";
import { askEnInfo, getEnInfoRequest, getEnInfoRequests, postEnInfoAction } from "@/api/EngineeringAPIs";
import { enPaths, EnRoles, enUnits, faDigits, infoStatuses, runEn, unitName, unitRoles, useEnDialog, useFocusItem } from "@/composables/engineering";
import { hasRole } from "@/composables/roles";

const props = defineProps<{ handoverId?: number | null; caseId?: number | null; highlightId?: number | null }>();
const listed = ref<any[]>([]);
const one = useFocusItem(() => props.highlightId || null, getEnInfoRequest);
const rows = computed(() => one.merge(listed.value));
const loading = ref(false);
async function load() {
  loading.value = true;
  const [d] = await getEnInfoRequests({ HandoverId: props.handoverId, CaseId: props.caseId, PageSize: 100 });
  loading.value = false;
  listed.value = d?.data || [];
  await one.load();
}
onMounted(load);
watch(() => [props.handoverId, props.caseId, props.highlightId], load);
const d = useEnDialog(load);
// The server accepts a question only from a member of the unit asked for: the form offers the user's own units only.
const myUnits = enUnits.filter((u) => hasRole(unitRoles[u.value]));
const act = (r: any, action: string, answer?: string) => postEnInfoAction({ id: r.id, action, answer, rowVersion: r.rowVersion });
function ask() {
  d.ask({
    title: "پرسش از PMO",
    fields: [
      { key: "unit", label: "واحد پرسنده", type: "select", required: true, options: myUnits.map((u) => ({ value: String(u.value), text: u.text })), value: myUnits.length === 1 ? String(myUnits[0].value) : "" },
      { key: "q", label: "پرسش", type: "textarea", required: true, min: 10, max: 2000 },
    ],
    run: (v) => askEnInfo({ handoverId: props.handoverId, caseId: props.caseId, unit: Number(v.unit), question: v.q }),
    okText: "پرسش برای PMO ثبت شد.",
  });
}
const answer = (r: any) =>
  d.ask({
    title: "پاسخ به پرسش " + faDigits(r.number),
    hint: r.answerHint || "پاسخ نباید شامل قیمت باشد.",
    fields: [{ key: "a", label: "پاسخ", type: "textarea", required: true, min: 15, max: 2000 }],
    run: (v) => act(r, "answer", v.a),
    okText: "پاسخ ثبت شد.",
  });
const withdraw = (r: any) =>
  d.ask({ title: "پس‌گرفتن پرسش", fields: [{ key: "a", label: "دلیل", type: "textarea", required: true, min: 15, max: 2000 }], run: (v) => act(r, "void", v.a), okText: "پرسش پس گرفته شد." });
</script>

<template>
  <div data-test="en-info-list">
    <p v-if="loading" class="inv-muted">در حال خواندن…</p>
    <p v-else-if="!rows.length" class="inv-muted">پرسشی ثبت نشده است.</p>
    <div v-for="r in rows" :key="r.id" :class="['en-box', highlightId === r.id ? 'border-primary' : '']">
      <div class="d-flex flex-wrap gap-2 align-items-center mb-1">
        <strong>{{ faDigits(r.number) }}</strong><EnChip :status="r.status" :text="r.statusText || infoStatuses[r.status]" group="info" />
        <span class="small">واحد {{ unitName(r.unit ?? r.unitText) }} — {{ r.askedByName }} {{ faDigits(r.askedAtText || "") }}</span>
        <router-link v-if="r.handoverId" :to="enPaths.handover(r.handoverId)" class="small">تحویل {{ faDigits(r.handoverNumber || "") }}</router-link>
      </div>
      <p class="en-pre mb-1">{{ r.question }}</p>
      <p v-if="r.answer" class="en-pre small"><strong>پاسخ PMO:</strong> {{ r.answer }} <span class="inv-muted">({{ r.answeredByName }} {{ faDigits(r.answeredAtText || "") }})</span></p>
      <EnAction :can="r.canAnswer" :reason="r.answerBlockedReason" :roles="EnRoles.pmo" label="پاسخ" variant="outline-primary" @go="answer(r)" />
      <EnAction :can="r.canClose" :reason="r.closeBlockedReason" :roles="EnRoles.infoReaders" :confirm="`پرسش ${faDigits(r.number)} بسته شود؟ پس از بستن، پاسخ PMO نهایی می‌ماند و پرسش دوباره باز نمی‌شود.`" label="بستن" variant="success" @go="runEn(() => act(r, 'close'), 'پرسش بسته شد.', load)" />
      <EnAction :can="r.canVoid" :reason="r.voidBlockedReason" :roles="EnRoles.infoReaders" label="پس‌گرفتن" variant="outline-secondary" @go="withdraw(r)" />
    </div>
    <button v-if="myUnits.length && (handoverId || caseId)" class="btn btn-sm btn-outline-primary" type="button" @click="ask">پرسش از PMO</button>
    <EnPrompt v-if="d.dlg.value" :busy="d.busy.value" :error="d.error.value" :fields="d.dlg.value.fields" :hint="d.dlg.value.hint" :title="d.dlg.value.title" @cancel="d.close" @confirm="d.confirm" />
  </div>
</template>
