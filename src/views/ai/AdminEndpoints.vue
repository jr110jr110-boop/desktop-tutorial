<script setup lang="ts">
// «سرورهای هوش مصنوعی»: the model servers on the company's own network (chat, embeddings, speech to text, text to speech), their priority (failover order) and last health,
// and the rebuild of the document search index. Read: PMO and CEO. Write: PMO.
import { onMounted, ref } from "vue";
import AiAdminTabs from "@/components/ai/AiAdminTabs.vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import { checkAiEndpoints, getAiEndpoints, rebuildAiIndex, saveAiEndpoint } from "@/api/AiAPIs";
import { endpointKinds } from "@/composables/ai";
import { fail, jalali, ok } from "@/composables/inventory";
import { AiRoles, hasRole } from "@/composables/roles";

const canWrite = hasRole(AiRoles.admins);
const rows = ref<any[]>([]);
const loading = ref(false);
const form = ref<any>(null);
const busy = ref(false);
const checking = ref(false);
const confirmIndex = ref(false);
const indexResult = ref<any>(null);

async function load() {
  loading.value = true;
  const [data, error] = await getAiEndpoints();
  loading.value = false;
  if (error) return fail(error);
  rows.value = data || [];
}

function blank() {
  return { id: null, name: "", kind: 1, url: "", modelName: "", priority: 1, enabled: true, timeoutSeconds: 30 };
}
function edit(r: any) {
  form.value = { id: r.id, name: r.name, kind: r.kind, url: r.url, modelName: r.modelName || "", priority: r.priority, enabled: r.enabled, timeoutSeconds: r.timeoutSeconds };
}
async function save() {
  const f = form.value;
  if (!String(f.name).trim()) return fail("نام سرور را وارد کنید.");
  if (!String(f.url).trim()) return fail("نشانی سرور را وارد کنید.");
  if (!(Number(f.timeoutSeconds) > 0)) return fail("مهلت پاسخ باید بیشتر از صفر باشد.");
  busy.value = true;
  const [, error] = await saveAiEndpoint({ ...f, kind: Number(f.kind), priority: Number(f.priority), timeoutSeconds: Number(f.timeoutSeconds), modelName: f.modelName || null });
  busy.value = false;
  if (error) return fail(error);
  ok("سرور ذخیره شد.");
  form.value = null;
  load();
}
async function check() {
  checking.value = true;
  const [data, error] = await checkAiEndpoints();
  checking.value = false;
  if (error) return fail(error);
  rows.value = data || rows.value;
  ok("بررسی سلامت انجام شد.");
}
async function rebuild() {
  confirmIndex.value = false;
  busy.value = true;
  const [data, error] = await rebuildAiIndex();
  busy.value = false;
  if (error) return fail(error);
  indexResult.value = data;
  ok("نمایه اسناد بازسازی شد.");
}
onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">مدیریت هوش مصنوعی</h1>
      </div>
      <AiAdminTabs />
      <div class="inv-toolbar">
        <h2 class="inv-title">سرورهای هوش مصنوعی</h2>
        <div class="d-flex gap-2">
          <button v-if="canWrite" :disabled="checking" class="btn btn-sm btn-outline-primary" @click="check">
            <span v-if="checking" class="spinner-border spinner-border-sm me-1"></span>بررسی سلامت
          </button>
          <button v-if="canWrite" class="btn btn-sm btn-primary" @click="form = blank()">سرور جدید</button>
        </div>
      </div>
      <p class="inv-muted">اولویت کمتر = ابتدا امتحان می‌شود. اگر سروری از کار بیفتد، سرور فعالِ بعدیِ همان نوع جایگزین می‌شود.</p>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>نام</th><th>نوع</th><th>نشانی</th><th>مدل</th><th>اولویت</th><th>فعال</th><th>مهلت (ثانیه)</th><th>آخرین سلامت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!rows.length"><td colspan="9" class="text-center inv-muted">{{ loading ? "در حال بارگذاری..." : "سروری ثبت نشده است." }}</td></tr>
            <tr v-for="r in rows" :key="r.id">
              <td>{{ r.name }}</td>
              <td>{{ endpointKinds[r.kind] }}</td>
              <td class="inv-ltr">{{ r.url }}</td>
              <td class="inv-ltr">{{ r.modelName || "-" }}</td>
              <td>{{ r.priority }}</td>
              <td><span :class="r.enabled ? 'ok' : 'warn'" class="inv-badge">{{ r.enabled ? "فعال" : "غیرفعال" }}</span></td>
              <td>{{ r.timeoutSeconds }}</td>
              <td>
                <template v-if="r.lastHealthAt">
                  <span :class="r.lastHealthOk ? 'ok' : 'bad'" class="inv-badge">{{ r.lastHealthOk ? "سالم" : "خطا" }}</span>
                  <span class="inv-muted ms-1">{{ jalali(r.lastHealthAt, true) }}</span>
                  <div v-if="!r.lastHealthOk && r.lastError" class="small text-danger inv-ltr">{{ r.lastError }}</div>
                </template>
                <span v-else class="inv-muted">بررسی نشده</span>
              </td>
              <td><button v-if="canWrite" class="btn btn-sm btn-outline-secondary" @click="edit(r)">ویرایش</button></td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <div class="inv-card">
      <div class="inv-toolbar">
        <h2 class="inv-title">نمایه جست‌وجوی اسناد</h2>
        <button v-if="canWrite" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="confirmIndex = true">بازسازی نمایه اسناد</button>
      </div>
      <p class="inv-muted">اسناد کنترل‌شده و صورت‌جلسه‌ها برای پاسخ‌دادن دستیار نمایه می‌شوند. پس از ثبت یا تغییر سرور بردارسازی، نمایه را بازسازی کنید.</p>
      <div v-if="indexResult" class="row g-2">
        <div class="col-6 col-md-3">اسناد کنترل‌شده: <b>{{ indexResult.controlledDocuments }}</b></div>
        <div class="col-6 col-md-3">صورت‌جلسه‌ها: <b>{{ indexResult.meetingMinutes }}</b></div>
        <div class="col-6 col-md-3">قطعه‌های نمایه: <b>{{ indexResult.chunks }}</b></div>
        <div class="col-6 col-md-3">ناموفق: <b :class="indexResult.failed ? 'text-danger' : ''">{{ indexResult.failed }}</b></div>
        <div v-if="indexResult.message" class="col-12 inv-muted">{{ indexResult.message }}</div>
      </div>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش سرور" : "سرور جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">نام *</label><input v-model="form.name" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">نوع *</label>
            <select v-model.number="form.kind" class="form-select form-select-sm"><option v-for="(t, k) in endpointKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-12"><label class="form-label small mb-0">نشانی *</label><input v-model="form.url" class="form-control form-control-sm inv-ltr" placeholder="http://ai-server:8000 (بدون /v1)" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">نام مدل *</label><input v-model="form.modelName" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">اولویت</label><input v-model.number="form.priority" class="form-control form-control-sm" min="0" type="number" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">مهلت پاسخ (ثانیه)</label><input v-model.number="form.timeoutSeconds" class="form-control form-control-sm" min="1" type="number" /></div>
          <div class="col-12"><label class="form-check mb-0"><input v-model="form.enabled" class="form-check-input" type="checkbox" /> <span class="form-check-label">فعال</span></label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        </div>
      </div>
    </div>

    <PromptModal
      v-if="confirmIndex"
      :fields="[]"
      confirm-text="بازسازی"
      hint="نمایه اسناد از نو ساخته می‌شود؛ این کار ممکن است چند دقیقه طول بکشد."
      title="نمایه اسناد بازسازی شود؟"
      @cancel="confirmIndex = false"
      @confirm="rebuild" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
