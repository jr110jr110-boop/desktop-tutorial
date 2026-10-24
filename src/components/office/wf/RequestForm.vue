<script setup lang="ts">
// New / edit request: pick a type, fill its dynamic fields (text, number, date as Persian date picker sending ISO, select). Keys of `values` are the lowerCamel field names.
import { computed, ref, watch } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import { getRequestTypes, saveRequest } from "@/api/OfficeAPIs";
import { latinDigits } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import moment from "moment-jalaali";

const props = defineProps({ request: { type: Object, default: null } });
const emit = defineEmits(["close", "saved"]);
const types = ref<any[]>([]);
const typeId = ref<number | null>(props.request ? (props.request as any).typeId : null);
// stored dates come back as Persian text (1405/08/03); the date picker wants ISO
const toIso = (v: any) => (typeof v === "string" && /^\d{4}\/\d{2}\/\d{2}$/.test(v) ? moment(v, "jYYYY/jMM/jDD").format("YYYY-MM-DD") : v);
const dateNames = new Set((((props.request as any)?.fields as any[]) || []).filter((f) => f.type === "date").map((f) => f.name));
const values = ref<Record<string, string>>(Object.fromEntries(Object.entries(((props.request as any)?.values as any) || {}).map(([k, v]) => [k, dateNames.has(k) ? toIso(v) : v])) as Record<string, string>);
const busy = ref(false);
const type = computed(() => types.value.find((t) => t.id === typeId.value) || null);
const fields = computed<any[]>(() => (props.request && !type.value ? (props.request as any).fields : type.value?.fields) || []);

(async () => {
  const [d, error] = await getRequestTypes();
  if (error) return fail(error);
  types.value = (Array.isArray(d) ? d : []).filter((t: any) => t.isActive || t.id === typeId.value);
})();
watch(typeId, (_, old) => {
  if (old !== null && !props.request) values.value = {};
});

function missing(): string | null {
  for (const f of fields.value) if (f.required && !String(values.value[f.name] ?? "").trim()) return `فیلد «${f.label}» الزامی است.`;
  return null;
}
async function save(submit: boolean) {
  if (!typeId.value) return fail({ message: "نوع درخواست را انتخاب کنید." });
  const problem = missing();
  if (problem) return fail({ message: problem });
  const clean: Record<string, string> = {};
  for (const f of fields.value) {
    const v = values.value[f.name];
    if (v !== undefined && v !== null && String(v) !== "") clean[f.name] = f.type === "number" ? latinDigits(String(v)) : String(v);
  }
  busy.value = true;
  const [d, error] = await saveRequest({ id: (props.request as any)?.id || null, typeId: typeId.value, values: clean, submit, rowVersion: (props.request as any)?.rowVersion || null });
  busy.value = false;
  if (error) return fail(error);
  ok(submit ? "درخواست ثبت و برای تایید ارسال شد." : "پیش‌نویس ذخیره شد.");
  emit("saved", d);
}
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal wide">
      <h2 class="inv-title mb-2">{{ request ? "ویرایش درخواست" : "درخواست جدید" }}</h2>
      <div class="row g-2">
        <div class="col-12">
          <label class="form-label small mb-0">نوع درخواست</label>
          <select v-model="typeId" :disabled="!!request" class="form-select form-select-sm"><option :value="null">انتخاب نوع</option><option v-for="t in types" :key="t.id" :value="t.id">{{ t.title }}</option></select>
          <div v-if="type?.targetNote" class="small inv-muted mt-1">{{ type.targetNote }}</div>
          <div v-if="type?.approvalChainTitles?.length" class="small inv-muted">زنجیره تایید: {{ type.approvalChainTitles.join(" ← ") }}</div>
          <div v-else-if="type" class="small inv-muted">تایید با مدیر واحد درخواست‌کننده است.</div>
        </div>
        <div v-for="f in fields" :key="f.name" class="col-12 col-md-6">
          <label class="form-label small mb-0">{{ f.label }}<span v-if="f.required" class="text-danger"> *</span></label>
          <FnDate v-if="f.type === 'date'" :model-value="values[f.name] ? String(values[f.name]).slice(0, 10) : null" @update:model-value="values[f.name] = $event || ''" />
          <select v-else-if="f.type === 'select'" v-model="values[f.name]" class="form-select form-select-sm"><option value="">انتخاب</option><option v-for="o in f.options" :key="o" :value="o">{{ o }}</option></select>
          <input v-else-if="f.type === 'number'" v-model="values[f.name]" class="form-control form-control-sm inv-ltr" inputmode="decimal" />
          <input v-else v-model="values[f.name]" class="form-control form-control-sm" />
        </div>
      </div>
      <div class="d-flex gap-2 justify-content-end mt-3">
        <button class="btn btn-sm btn-outline-secondary" @click="emit('close')">انصراف</button>
        <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="save(false)">ذخیره پیش‌نویس</button>
        <button :disabled="busy" class="btn btn-sm btn-primary" @click="save(true)">ذخیره و ارسال برای تایید</button>
      </div>
    </div>
  </div>
</template>
