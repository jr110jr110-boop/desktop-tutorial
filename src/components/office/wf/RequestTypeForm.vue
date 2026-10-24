<script setup lang="ts">
// Request type designer: title, form fields (lowerCamel name, label, type, required, options for select), approval chain of positions, target note, active.
import { ref } from "vue";
import { saveRequestType } from "@/api/OfficeAPIs";
import { fail, ok } from "@/composables/inventory";
import { requestFieldTypes } from "@/composables/office";
import PositionSelect from "./PositionSelect.vue";
import { loadPositions } from "./lookups";

const props = defineProps({ type: { type: Object, default: null } });
const emit = defineEmits(["close", "saved"]);
const t: any = props.type;
const f = ref<any>({
  id: t?.id || null,
  title: t?.title || "",
  fields: (t?.fields || []).map((x: any) => ({ name: x.name, label: x.label, type: x.type || "text", required: !!x.required, optionsText: (x.options || []).join("\n") })),
  chain: [...(t?.approvalChain || [])] as number[],
  targetNote: t?.targetNote || "",
  isActive: t ? t.isActive : true,
});
const next = ref<number | null>(null);
const titles = ref<Record<number, string>>({});
const busy = ref(false);
loadPositions().then((all) => all.forEach((p) => (titles.value[p.id] = p.title)));

const addField = () => f.value.fields.push({ name: "", label: "", type: "text", required: false, optionsText: "" });
function move(i: number, d: number) {
  const j = i + d;
  if (j < 0 || j >= f.value.chain.length) return;
  const c = f.value.chain;
  [c[i], c[j]] = [c[j], c[i]];
}
function addStep() {
  if (next.value) f.value.chain.push(next.value);
  next.value = null;
}
async function save() {
  const names = new Set<string>();
  for (const x of f.value.fields) {
    if (!/^[a-z][A-Za-z0-9]*$/.test(x.name)) return fail({ message: `نام فیلد «${x.name || x.label}» باید لاتین و به صورت lowerCamel باشد (مثلا startDate).` });
    if (names.has(x.name)) return fail({ message: `نام فیلد «${x.name}» تکراری است.` });
    names.add(x.name);
    if (!x.label.trim()) return fail({ message: `برچسب فیلد «${x.name}» را بنویسید.` });
  }
  busy.value = true;
  const [d, error] = await saveRequestType({
    id: f.value.id,
    title: f.value.title,
    fields: f.value.fields.map((x: any) => ({ name: x.name, label: x.label, type: x.type, required: x.required, options: x.type === "select" ? x.optionsText.split("\n").map((o: string) => o.trim()).filter(Boolean) : [] })),
    approvalChain: f.value.chain,
    targetNote: f.value.targetNote,
    isActive: f.value.isActive,
  });
  busy.value = false;
  if (error) return fail(error);
  ok("نوع درخواست ذخیره شد.");
  emit("saved", d);
}
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal wide" style="max-height: 90vh; overflow-y: auto">
      <h2 class="inv-title mb-2">{{ f.id ? "ویرایش نوع درخواست" : "نوع درخواست جدید" }}</h2>
      <div class="row g-2">
        <div class="col-12 col-md-8"><label class="form-label small mb-0">عنوان</label><input v-model="f.title" class="form-control form-control-sm" placeholder="مثلا درخواست گواهی اشتغال" /></div>
        <div class="col-12 col-md-4 d-flex align-items-end"><label class="small d-flex align-items-center gap-1"><input v-model="f.isActive" type="checkbox" class="form-check-input mt-0" />فعال</label></div>
        <div class="col-12"><label class="form-label small mb-0">یادداشت مقصد (کار پس از تایید)</label><input v-model="f.targetNote" class="form-control form-control-sm" placeholder="مثلا صدور توسط منابع انسانی" /></div>
      </div>

      <h3 class="h6 mt-3">فیلدهای فرم</h3>
      <div v-for="(x, i) in f.fields" :key="i" class="border rounded p-2 mb-2">
        <div class="row g-2">
          <div class="col-6 col-md-3"><label class="form-label small mb-0">نام (لاتین)</label><input v-model="x.name" class="form-control form-control-sm inv-ltr" placeholder="startDate" /></div>
          <div class="col-6 col-md-4"><label class="form-label small mb-0">برچسب</label><input v-model="x.label" class="form-control form-control-sm" /></div>
          <div class="col-6 col-md-2"><label class="form-label small mb-0">نوع</label><select v-model="x.type" class="form-select form-select-sm"><option v-for="(tt, k) in requestFieldTypes" :key="k" :value="k">{{ tt }}</option></select></div>
          <div class="col-3 col-md-1 d-flex align-items-end"><label class="small d-flex align-items-center gap-1"><input v-model="x.required" type="checkbox" class="form-check-input mt-0" />الزامی</label></div>
          <div class="col-3 col-md-2 d-flex align-items-end gap-1"><button class="btn btn-sm btn-outline-secondary" :disabled="i === 0" @click="f.fields.splice(i - 1, 2, f.fields[i], f.fields[i - 1])">بالا</button><button class="btn btn-sm btn-outline-danger" @click="f.fields.splice(i, 1)">حذف</button></div>
          <div v-if="x.type === 'select'" class="col-12"><label class="form-label small mb-0">گزینه‌ها (هر گزینه در یک سطر)</label><textarea v-model="x.optionsText" class="form-control form-control-sm" rows="3"></textarea></div>
        </div>
      </div>
      <button class="btn btn-sm btn-outline-primary" @click="addField">فیلد جدید</button>

      <h3 class="h6 mt-3">زنجیره تایید (به ترتیب)</h3>
      <p class="inv-muted small mb-1">اگر خالی باشد، مدیر واحد درخواست‌کننده تایید می‌کند.</p>
      <ol class="mb-2">
        <li v-for="(id, i) in f.chain" :key="i" class="mb-1">
          {{ titles[id] || "سمت #" + id }}
          <button class="btn btn-sm btn-link p-0 ms-2" @click="move(i, -1)">بالا</button>
          <button class="btn btn-sm btn-link p-0 ms-1" @click="move(i, 1)">پایین</button>
          <button class="btn btn-sm btn-link text-danger p-0 ms-1" @click="f.chain.splice(i, 1)">حذف</button>
        </li>
      </ol>
      <div class="inv-filters"><PositionSelect v-model="next" /><button class="btn btn-sm btn-outline-primary" @click="addStep">افزودن مرحله</button></div>

      <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="emit('close')">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
    </div>
  </div>
</template>
