<script setup lang="ts">
// The optional tender or project a draft letter belongs to. Its code becomes a part of the letter number: INN-<code>-<yyyy>-<n> (no code and no extra dash without a link).
// Only a draft is edited here (the parent shows this form for drafts only); once a letter is numbered the server freezes the link. At most one of tender and project.
import { computed, onMounted, ref, watch } from "vue";
import PickSelect from "./PickSelect.vue";
import { getLinkContracts, getLinkTenders } from "@/api/OfficeAPIs";
import { getTerms } from "@/api/ProjectControlAPIs";
import { jalaliNow } from "@/composables/fn";
import { fa } from "./letterKit";

const props = defineProps<{ form: any; kind: number }>();
const tenders = ref<any[]>([]);
const contracts = ref<any[]>([]);
const projectCodes = ref<Record<number, string>>({});
const mode = ref<"none" | "tender" | "contract">(props.form.tenderId ? "tender" : props.form.contractId ? "contract" : "none");

const tenderOptions = computed(() => tenders.value.map((t) => ({ value: t.id, text: `${t.code}${t.description ? " - " + t.description : t.company ? " - " + t.company : ""}` })));
const contractOptions = computed(() => contracts.value.map((c) => ({ value: c.id, text: `${c.code}${c.title ? " - " + c.title : ""}` })));
/** The code that goes into the number: the code of the tender, or the project code of project control (the contract code when the project has none yet). */
const code = computed(() => {
  if (props.form.tenderId) return tenders.value.find((t) => t.id === props.form.tenderId)?.code || "";
  if (props.form.contractId) return projectCodes.value[props.form.contractId] || contracts.value.find((c) => c.id === props.form.contractId)?.code || "";
  return "";
});
const kindPrefix = computed(() => (props.kind === 1 ? "INN-و-" : props.kind === 3 ? "INN-د-" : "INN-"));
const example = computed(() => fa(`${kindPrefix.value}${code.value ? code.value + "-" : ""}${jalaliNow().year}-n`));

function setMode(m: "none" | "tender" | "contract") {
  mode.value = m;
  if (m !== "tender") props.form.tenderId = null;
  if (m !== "contract") props.form.contractId = null;
}
watch(() => props.form.contractId, async (id) => {
  if (!id || projectCodes.value[id]) return;
  const [terms] = await getTerms(id);
  const pc = terms?.projectCode ?? terms?.ProjectCode;
  if (pc) projectCodes.value = { ...projectCodes.value, [id]: String(pc) };
}, { immediate: true });
watch(() => [props.form.tenderId, props.form.contractId], () => {
  if (props.form.tenderId) mode.value = "tender";
  else if (props.form.contractId) mode.value = "contract";
});
onMounted(async () => {
  [tenders.value, contracts.value] = await Promise.all([getLinkTenders(), getLinkContracts()]);
});
</script>

<template>
  <div>
    <div class="d-flex gap-3 flex-wrap align-items-center mb-1">
      <label class="small d-flex align-items-center gap-1"><input :checked="mode === 'none'" type="radio" @change="setMode('none')" /> بدون پیوند</label>
      <label class="small d-flex align-items-center gap-1"><input :checked="mode === 'tender'" type="radio" @change="setMode('tender')" /> مناقصه</label>
      <label class="small d-flex align-items-center gap-1"><input :checked="mode === 'contract'" type="radio" @change="setMode('contract')" /> پروژه (قرارداد)</label>
    </div>
    <div v-if="mode === 'tender'" style="max-width: 32rem"><PickSelect v-model="form.tenderId" :options="tenderOptions" placeholder="انتخاب مناقصه" /></div>
    <div v-else-if="mode === 'contract'" style="max-width: 32rem"><PickSelect v-model="form.contractId" :options="contractOptions" placeholder="انتخاب پروژه" /></div>
    <div v-if="code" class="small mt-1">کد در شماره نامه: <b class="inv-ltr">{{ fa(code) }}</b> <span class="inv-muted">(نمونه شماره: <span class="inv-ltr">{{ example }}</span>)</span></div>
    <div v-else-if="mode !== 'none'" class="small inv-muted mt-1">رکورد انتخاب‌شده کد ندارد؛ بدون کد در شماره نامه نمی‌آید و سرور ذخیره را نمی‌پذیرد.</div>
    <div class="small inv-muted mt-1">پیوند فقط تا پیش از شماره‌گذاری قابل تغییر است و بعد از آن ثابت می‌ماند. شماره‌ی ترتیبی برای هر نوع نامه و هر سال شمسی از ۱ شروع می‌شود، نه برای هر مناقصه یا پروژه.</div>
  </div>
</template>
