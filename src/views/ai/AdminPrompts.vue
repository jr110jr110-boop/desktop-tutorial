<script setup lang="ts">
// «نسخه‌های دستور سیستم»: the versions of the system prompts (one code per use: CHAT and the seats). A new version is saved inactive; activating it is a deliberate act (confirm dialog) and
// is written to the audit. Read: PMO and CEO. Write: PMO.
import { computed, onMounted, ref } from "vue";
import AiAdminTabs from "@/components/ai/AiAdminTabs.vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import { activateAiPrompt, getAiPrompts, newAiPrompt } from "@/api/AiAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { AiRoles, hasRole } from "@/composables/roles";

const canWrite = hasRole(AiRoles.admins);
const all = ref<any[]>([]);
const loading = ref(false);
const code = ref<string>("");
const view = ref<any>(null);
const confirm = ref<any>(null);
const draft = ref<{ code: string; text: string }>({ code: "CHAT", text: "" });
const busy = ref(false);

const codes = computed(() => Array.from(new Set(all.value.map((p) => p.code))));
const rows = computed(() => all.value.filter((p) => p.code === code.value));

async function load() {
  loading.value = true;
  const [data, error] = await getAiPrompts();
  loading.value = false;
  if (error) return fail(error);
  all.value = data || [];
  if (!code.value || !codes.value.includes(code.value)) code.value = codes.value.includes("CHAT") ? "CHAT" : codes.value[0] || "";
  if (code.value) draft.value.code = code.value;
}

function fromVersion(p: any) {
  draft.value = { code: p.code, text: p.text };
  view.value = null;
  window.scrollTo({ top: document.body.scrollHeight, behavior: "smooth" });
}

async function create() {
  const text = draft.value.text.trim();
  if (!text) return fail("متن دستور سیستم را وارد کنید.");
  if (!draft.value.code.trim()) return fail("کد دستور را وارد کنید.");
  busy.value = true;
  const [data, error] = await newAiPrompt({ code: draft.value.code.trim(), text });
  busy.value = false;
  if (error) return fail(error);
  ok("نسخه " + data.version + " ساخته شد (هنوز فعال نیست).");
  draft.value.text = "";
  code.value = data.code;
  load();
}

async function activate() {
  const p = confirm.value;
  confirm.value = null;
  busy.value = true;
  const [, error] = await activateAiPrompt(p.id);
  busy.value = false;
  if (error) return fail(error);
  ok("نسخه " + p.version + " فعال شد.");
  load();
}
onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">مدیریت هوش مصنوعی</h1></div>
      <AiAdminTabs />
      <div class="inv-toolbar">
        <h2 class="inv-title">نسخه‌های دستور سیستم</h2>
        <div class="d-flex gap-2 align-items-center">
          <label class="small mb-0">کد دستور</label>
          <select v-model="code" class="form-select form-select-sm inv-ltr" style="width: 200px" @change="draft.code = code">
            <option v-for="c in codes" :key="c" :value="c">{{ c }}</option>
          </select>
        </div>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>نسخه</th><th>وضعیت</th><th>ساخته‌شده</th><th>فعال‌شده</th><th>طول متن</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!rows.length"><td colspan="6" class="text-center inv-muted">{{ loading ? "در حال بارگذاری..." : "نسخه‌ای نیست." }}</td></tr>
            <tr v-for="p in rows" :key="p.id">
              <td>{{ p.version }}</td>
              <td><span :class="p.isActive ? 'ok' : ''" class="inv-badge">{{ p.isActive ? "فعال" : "غیرفعال" }}</span></td>
              <td>{{ p.createdAtText || jalali(p.createdAt, true) }}</td>
              <td>{{ p.activatedAtText || (p.activatedAt ? jalali(p.activatedAt, true) : "-") }}</td>
              <td>{{ p.text.length }} نویسه</td>
              <td class="d-flex gap-2">
                <button class="btn btn-sm btn-outline-secondary" @click="view = p">مشاهده متن</button>
                <button v-if="canWrite && !p.isActive" :disabled="busy" class="btn btn-sm btn-outline-success" @click="confirm = p">فعال‌سازی</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <div v-if="canWrite" class="inv-card">
      <h2 class="inv-title mb-2">نسخه جدید</h2>
      <div class="row g-2">
        <div class="col-md-4">
          <label class="form-label small mb-0">کد دستور (خالی = CHAT)</label>
          <input v-model="draft.code" class="form-control form-control-sm inv-ltr" list="ai-prompt-codes" />
          <datalist id="ai-prompt-codes"><option v-for="c in codes" :key="c" :value="c" /></datalist>
        </div>
        <div class="col-12">
          <label class="form-label small mb-0">متن دستور سیستم *</label>
          <textarea v-model="draft.text" class="form-control form-control-sm" rows="10"></textarea>
        </div>
      </div>
      <div class="d-flex gap-2 mt-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="create">ساخت نسخه جدید</button><span class="inv-muted align-self-center">نسخه جدید غیرفعال ساخته می‌شود؛ فعال‌سازی جداگانه و با تایید است.</span></div>
    </div>

    <div v-if="view" class="inv-modal-backdrop screen-only" @click.self="view = null">
      <div class="inv-modal wide">
        <div class="d-flex justify-content-between align-items-center mb-2">
          <h2 class="inv-title"><span class="inv-ltr">{{ view.code }}</span> — نسخه {{ view.version }} <span :class="view.isActive ? 'ok' : ''" class="inv-badge">{{ view.isActive ? "فعال" : "غیرفعال" }}</span></h2>
          <div class="d-flex gap-2">
            <button v-if="canWrite" class="btn btn-sm btn-outline-primary" @click="fromVersion(view)">ساخت نسخه جدید از روی این نسخه</button>
            <button class="btn btn-sm btn-secondary" @click="view = null">بستن</button>
          </div>
        </div>
        <pre class="prompt-text">{{ view.text }}</pre>
      </div>
    </div>

    <PromptModal
      v-if="confirm"
      :fields="[]"
      :hint="'با فعال‌سازی نسخه ' + confirm.version + '، دستیار از همین لحظه با این متن کار می‌کند و نسخه فعال قبلی غیرفعال می‌شود.'"
      confirm-text="فعال‌سازی"
      :title="'نسخه ' + confirm.version + ' از ' + confirm.code + ' فعال شود؟'"
      @cancel="confirm = null"
      @confirm="activate" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style scoped>
.prompt-text {
  white-space: pre-wrap;
  background: #f7f8fa;
  border: 1px solid #e9ecef;
  border-radius: 6px;
  padding: 0.7rem;
  max-height: 65vh;
  overflow: auto;
  line-height: 1.8;
  font-family: inherit;
}
</style>
