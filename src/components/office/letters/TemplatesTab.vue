<script setup lang="ts">
// Letter templates: title, kind, header / body / footer, approval chain (positions in order) and signer.
import { computed, onMounted, ref } from "vue";
import PickSelect from "./PickSelect.vue";
import RichBox from "./RichBox.vue";
import { getLetterTemplates, saveLetterTemplate } from "@/api/OfficeAPIs";
import { letterKinds } from "@/composables/office";
import { fail, ok } from "@/composables/inventory";
import { loadAllPositions, positionText } from "./letterKit";

const rows = ref<any[]>([]);
const positions = ref<any[]>([]);
const edit = ref<any>(null);
const busy = ref(false);
const pick = ref<number | null>(null);

const chainOptions = computed(() => positions.value.filter((p) => !(edit.value?.approvalChain || []).includes(p.id)).map((p) => ({ value: p.id, text: positionText(p) })));
const signerOptions = computed(() => positions.value.filter((p) => p.canSign).map((p) => ({ value: p.id, text: positionText(p) })));
const titleOf = (id: number) => positionText(positions.value.find((p) => p.id === id)) || String(id);

async function load() {
  const [data, error] = await getLetterTemplates({ PageNumber: 1, PageSize: 200 });
  if (error) return fail(error);
  rows.value = Array.isArray(data) ? data : data?.data || [];
}
function start(t?: any) {
  edit.value = t ? { ...t, approvalChain: [...(t.approvalChain || [])] } : { id: null, title: "", letterKind: null, headerHtml: "", footerHtml: "", bodyHtml: "", approvalChain: [], signerPositionId: null, isActive: true };
}
function addChain() {
  if (pick.value && !edit.value.approvalChain.includes(pick.value)) edit.value.approvalChain.push(pick.value);
  pick.value = null;
}
function move(i: number, d: number) {
  const c = edit.value.approvalChain;
  const j = i + d;
  if (j < 0 || j >= c.length) return;
  [c[i], c[j]] = [c[j], c[i]];
}
async function submit() {
  const e = edit.value;
  if (!String(e.title || "").trim()) return fail("عنوان قالب را وارد کنید.");
  busy.value = true;
  const [, error] = await saveLetterTemplate({ id: e.id || null, title: e.title.trim(), letterKind: e.letterKind || null, headerHtml: e.headerHtml || null, footerHtml: e.footerHtml || null, bodyHtml: e.bodyHtml || null, approvalChain: e.approvalChain, signerPositionId: e.signerPositionId || null, isActive: e.isActive });
  busy.value = false;
  if (error) return fail(error);
  ok("قالب ذخیره شد.");
  edit.value = null;
  load();
}
onMounted(async () => {
  load();
  positions.value = (await loadAllPositions()).filter((p) => p.isActive !== false);
});
</script>

<template>
  <div>
    <div class="d-flex justify-content-end mb-2"><button class="btn btn-sm btn-primary" @click="start()">قالب جدید</button></div>
    <div class="table-responsive">
      <table class="table table-sm">
        <thead><tr><th>عنوان</th><th>نوع</th><th>زنجیره تایید</th><th>امضاکننده</th><th>وضعیت</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="6" class="text-center inv-muted">قالبی نیست.</td></tr>
          <tr v-for="t in rows" :key="t.id">
            <td>{{ t.title }}</td>
            <td>{{ t.letterKind ? letterKinds[t.letterKind] : "همه انواع" }}</td>
            <td>{{ (t.approvalChain || []).map(titleOf).join(" ← ") || "-" }}</td>
            <td>{{ t.signerPositionId ? titleOf(t.signerPositionId) : "-" }}</td>
            <td><span :class="t.isActive ? 'ok' : 'bad'" class="inv-badge">{{ t.isActive ? "فعال" : "غیرفعال" }}</span></td>
            <td><button class="btn btn-sm btn-link" @click="start(t)">ویرایش</button></td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="edit" class="inv-modal-backdrop" @click.self="edit = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">{{ edit.id ? "ویرایش قالب" : "قالب جدید" }}</h2>
        <div class="row g-2 mb-2">
          <div class="col-md-6"><label class="form-label small mb-0">عنوان *</label><input v-model="edit.title" class="form-control form-control-sm" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">نوع نامه</label><select v-model="edit.letterKind" class="form-select form-select-sm"><option :value="null">همه انواع</option><option v-for="(t, k) in letterKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-3 d-flex align-items-end"><label class="small d-flex align-items-center gap-1"><input v-model="edit.isActive" type="checkbox" /> فعال</label></div>
        </div>
        <label class="form-label small mb-0">سربرگ</label>
        <RichBox v-model="edit.headerHtml" min-height="4rem" />
        <label class="form-label small mb-0 mt-2">متن پیش‌فرض</label>
        <RichBox v-model="edit.bodyHtml" min-height="10rem" />
        <label class="form-label small mb-0 mt-2">پاورقی</label>
        <RichBox v-model="edit.footerHtml" min-height="4rem" />
        <div class="row g-2 mt-1">
          <div class="col-md-6">
            <label class="form-label small mb-0">امضاکننده پیش‌فرض</label>
            <PickSelect v-model="edit.signerPositionId" :options="signerOptions" placeholder="بدون امضاکننده پیش‌فرض" />
          </div>
          <div class="col-md-6">
            <label class="form-label small mb-0">زنجیره تایید (به ترتیب)</label>
            <ol v-if="edit.approvalChain.length" class="mb-1">
              <li v-for="(id, i) in edit.approvalChain" :key="id">
                {{ titleOf(id) }}
                <button class="btn btn-sm btn-link p-0 mx-1" @click="move(i, -1)">بالا</button>
                <button class="btn btn-sm btn-link p-0 mx-1" @click="move(i, 1)">پایین</button>
                <button class="btn btn-sm btn-link text-danger p-0 mx-1" @click="edit.approvalChain.splice(i, 1)">حذف</button>
              </li>
            </ol>
            <div class="d-flex gap-2 align-items-start">
              <div style="flex: 1"><PickSelect v-model="pick" :options="chainOptions" placeholder="افزودن سمت" /></div>
              <button class="btn btn-sm btn-outline-primary" @click="addChain">افزودن</button>
            </div>
          </div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="edit = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="submit">ذخیره</button>
        </div>
      </div>
    </div>
  </div>
</template>
