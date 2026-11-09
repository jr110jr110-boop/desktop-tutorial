<script setup lang="ts">
import { onMounted, ref } from "vue";
import { getNcAttachment, getPosts, getRevisionCam, importCamTools, saveBase64, setRevisionCam, uploadNcAttachment } from "@/api/GuardAPIs";
import { fileToBase64 } from "@/api/DncAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, ok } from "@/composables/inventory";
import { attachmentKinds } from "@/composables/guard";
import { sizeText } from "@/composables/dnc";

// P2 part E: what a revision was made with. Before it can be sent for approval it names an approved post processor of its controller family, carries the simulation report (CAM or an
// independent G-code simulator) and the CAM tool list; the tool list is what the guard compares with the tool table of the machine at every start.
const props = defineProps<{ revisionId: number }>();
const emit = defineEmits(["changed"]);
const c = ref<any>(null);
const posts = ref<any[]>([]);
const busy = ref(false);
const csv = ref("");
const form = ref({ postProcessorId: null as number | null, camSystem: "" });

async function load() {
  const [d, error] = await getRevisionCam(props.revisionId);
  if (error) return fail(error);
  c.value = d;
  form.value = { postProcessorId: d.postProcessorId ?? null, camSystem: d.camSystem || "" };
  if (d.editable && !posts.value.length) {
    const [p] = await getPosts({ status: 2, pageSize: 200 });
    posts.value = rowsOf(p);
  }
}
onMounted(load);

async function run(task: () => Promise<[any, any]>, message: string) {
  busy.value = true;
  const [d, error] = await task();
  busy.value = false;
  if (error) return fail(error);
  c.value = d;
  ok(message);
  emit("changed");
}
const saveHead = () => run(() => setRevisionCam({ revisionId: props.revisionId, postProcessorId: form.value.postProcessorId, camSystem: form.value.camSystem.trim() || null }), "پست‌پروسسور و نرم‌افزار CAM ثبت شد.");
async function pick(ev: Event, kind: number) {
  const file = (ev.target as HTMLInputElement).files?.[0];
  if (!file) return;
  const content = await fileToBase64(file);
  await run(() => uploadNcAttachment({ revisionId: props.revisionId, kind, fileName: file.name, contentBase64: content }), attachmentKinds[kind] + " پیوست شد.");
  (ev.target as HTMLInputElement).value = "";
}
async function pickCsv(ev: Event) {
  const file = (ev.target as HTMLInputElement).files?.[0];
  if (file) csv.value = await file.text();
}
const saveTools = () => run(() => importCamTools({ revisionId: props.revisionId, csv: csv.value }), "فهرست ابزار CAM ثبت شد.");
async function download(a: any) {
  const [d, error] = await getNcAttachment(a.id);
  if (error) return fail(error);
  saveBase64(d.fileName, d.contentBase64);
}
</script>

<template>
  <div v-if="c" class="rc">
    <div :class="c.missing.length ? 'warn' : 'ok'" class="rc-state">
      <b>{{ c.missing.length ? "مدارک CAM کامل نیست" : "مدارک CAM کامل است" }}</b>
      <ul v-if="c.missing.length" class="mb-0 small"><li v-for="(m, i) in c.missing" :key="i">{{ m }}</li></ul>
    </div>
    <div class="row g-2 small mt-1">
      <div class="col-md-5">
        <label class="form-label mb-0">پست‌پروسسور تأییدشده</label>
        <select v-if="c.editable" v-model="form.postProcessorId" class="form-select form-select-sm"><option :value="null">—</option><option v-for="p in posts" :key="p.id" :value="p.id">{{ p.code }} نسخه {{ p.version }} · {{ p.camSystem }} · {{ p.controllerFamilyText }}</option></select>
        <div v-else>{{ c.postProcessorText || "—" }}</div>
      </div>
      <div class="col-md-4">
        <label class="form-label mb-0">نرم‌افزار CAM</label>
        <input v-if="c.editable" v-model="form.camSystem" class="form-control form-control-sm inv-ltr" placeholder="PowerMill" />
        <div v-else class="inv-ltr">{{ c.camSystem || "—" }}</div>
      </div>
      <div v-if="c.editable" class="col-md-3 d-flex align-items-end"><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveHead">ثبت</button></div>
    </div>

    <div class="mt-3">
      <div class="fw-bold small mb-1">پیوست‌ها</div>
      <div v-if="!c.attachments.length" class="inv-muted small">پیوستی نیست.</div>
      <div v-for="a in c.attachments" :key="a.id" class="small">
        <span class="inv-badge" :class="a.id === c.simulationAttachmentId ? 'ok' : ''">{{ a.kindText }}</span>
        <a href="#" class="inv-ltr" @click.prevent="download(a)">{{ a.fileName }}</a> <span class="inv-muted">{{ sizeText(a.sizeBytes) }} · {{ a.uploadedByName }} · {{ a.uploadedAtText }}</span>
      </div>
      <div v-if="c.editable" class="d-flex gap-2 mt-2 flex-wrap">
        <label class="btn btn-sm btn-outline-primary mb-0">گزارش شبیه‌سازی<input class="d-none" type="file" @change="pick($event, 1)" /></label>
        <label class="btn btn-sm btn-outline-secondary mb-0">برگه راه‌اندازی<input class="d-none" type="file" @change="pick($event, 2)" /></label>
      </div>
    </div>

    <div class="mt-3">
      <div class="fw-bold small mb-1">فهرست ابزار CAM ({{ c.tools.length }})</div>
      <table v-if="c.tools.length" class="table table-sm small mb-1" style="max-width: 520px">
        <thead><tr><th>T</th><th>نام</th><th>قطر</th><th>طول لازم</th></tr></thead>
        <tbody><tr v-for="t in c.tools" :key="t.toolNumber"><td>{{ t.toolNumber }}</td><td class="inv-ltr">{{ t.name || "" }}</td><td>{{ t.diameter ?? "-" }}</td><td>{{ t.length ?? "-" }}</td></tr></tbody>
      </table>
      <template v-if="c.editable">
        <div class="inv-muted small">خروجی CSV فهرست ابزار نرم‌افزار CAM: ستون شماره ابزار (T یا Tool) و در صورت وجود Name، Diameter (D)، Length (L).</div>
        <div class="d-flex gap-2 align-items-start mt-1 flex-wrap">
          <textarea v-model="csv" class="form-control form-control-sm inv-ltr" rows="3" placeholder="T;Name;D;L&#10;1;EM10;10;70" style="max-width: 420px"></textarea>
          <div class="d-flex flex-column gap-1">
            <label class="btn btn-sm btn-outline-secondary mb-0">از فایل<input accept=".csv,.txt" class="d-none" type="file" @change="pickCsv" /></label>
            <button :disabled="busy || !csv.trim()" class="btn btn-sm btn-primary" @click="saveTools">ثبت فهرست</button>
          </div>
        </div>
      </template>
    </div>
  </div>
</template>

<style scoped>
.rc { padding: 6px 2px; }
.rc-state { border-radius: 6px; padding: 6px 10px; }
.rc-state.ok { background: #e8f5e9; color: #1b5e20; }
.rc-state.warn { background: #fff3e0; color: #7a4100; }
</style>
