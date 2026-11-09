<script setup lang="ts">
import { onMounted, ref } from "vue";
import { createPost, getPostFile, getPosts, postAction, saveBase64 } from "@/api/GuardAPIs";
import { fileToBase64 } from "@/api/DncAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, ok } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { DncRoles, controllerFamilies } from "@/composables/dnc";
import { postActionTitles, postStatuses } from "@/composables/guard";

// P2 part E: the post processors of the CAM systems. A new version of a code is a draft; another person approves it (never its author); the approved version is the current one and the
// earlier one retires. A program revision is submitted only with a current, approved post processor of its controller family.
const rows = ref<any[]>([]);
const text = ref("");
const busy = ref(false);
const form = ref<any>(null);
const canWrite = hasRole(DncRoles.writers);
const badge = (s: number) => (s === 2 ? "ok" : s === 1 ? "info" : s === 0 ? "warn" : s === 4 ? "bad" : "");

async function load() {
  const [d, error] = await getPosts({ contains: text.value || null, pageSize: 200 });
  if (error) return fail(error);
  rows.value = rowsOf(d);
}
onMounted(load);

function newVersion(p?: any) {
  form.value = p
    ? { code: p.code, title: p.title, camSystem: p.camSystem, controllerFamily: p.controllerFamily, machineId: p.machineId, file: null, changeNote: "" }
    : { code: "", title: "", camSystem: "PowerMill", controllerFamily: 3, machineId: null, file: null, changeNote: "" };
}
async function save() {
  const f = form.value;
  if (!f.file) return fail("فایل پست‌پروسسور را انتخاب کنید.");
  busy.value = true;
  const [, error] = await createPost({ code: f.code.trim(), title: f.title.trim(), camSystem: f.camSystem.trim(), controllerFamily: Number(f.controllerFamily), machineId: f.machineId, fileName: f.file.name, contentBase64: await fileToBase64(f.file), changeNote: f.changeNote || null });
  busy.value = false;
  if (error) return fail(error);
  ok("نسخه تازه پست‌پروسسور ثبت شد (پیش‌نویس).");
  form.value = null;
  load();
}
async function act(p: any, action: string) {
  const reason = action === "reject" ? prompt("دلیل رد:") : null;
  if (action === "reject" && !reason) return;
  busy.value = true;
  const [, error] = await postAction({ id: p.id, action, reason, rowVersion: p.rowVersion });
  busy.value = false;
  if (error) return fail(error);
  ok(postActionTitles[action] + " ثبت شد.");
  load();
}
async function download(p: any) {
  const [d, error] = await getPostFile(p.id);
  if (error) return fail(error);
  saveBase64(d.fileName, d.contentBase64);
}
</script>

<template>
  <div>
    <div class="inv-filters mb-3">
      <input v-model="text" class="form-control form-control-sm" placeholder="کد، عنوان یا نرم‌افزار CAM" style="max-width: 260px" @keyup.enter="load" />
      <button class="btn btn-sm btn-outline-secondary" @click="load">جستجو</button>
      <button v-if="canWrite" class="btn btn-sm btn-primary ms-auto" @click="newVersion()">پست‌پروسسور تازه</button>
    </div>
    <div class="table-responsive">
      <table class="table table-sm">
        <thead><tr><th>کد</th><th>نسخه</th><th>عنوان</th><th>CAM</th><th>کنترلر</th><th>وضعیت</th><th>فایل</th><th>تأیید</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="9" class="text-center inv-muted">پست‌پروسسوری ثبت نشده است.</td></tr>
          <tr v-for="p in rows" :key="p.id">
            <td class="inv-ltr">{{ p.code }}</td>
            <td>{{ p.version }}</td>
            <td>{{ p.title }}<div v-if="p.changeNote" class="inv-muted small">{{ p.changeNote }}</div></td>
            <td class="inv-ltr">{{ p.camSystem }}</td>
            <td>{{ p.controllerFamilyText }}<div v-if="p.machineTitle" class="inv-muted small">{{ p.machineTitle }}</div></td>
            <td><span :class="badge(p.status)" class="inv-badge">{{ postStatuses[p.status] }}</span><div v-if="p.rejectReason" class="small text-danger">{{ p.rejectReason }}</div></td>
            <td class="small"><a class="inv-ltr" href="#" @click.prevent="download(p)">{{ p.fileName }}</a><div class="inv-muted inv-ltr" :title="p.sha256">{{ p.sha256.substring(0, 12) }}…</div></td>
            <td class="small">{{ p.approvedByName || "-" }}<div class="inv-muted">{{ p.approvedAtText }}</div></td>
            <td class="text-nowrap">
              <button v-for="a in p.allowedActions" :key="a" :class="a === 'approve' ? 'btn-success' : a === 'reject' || a === 'withdraw' || a === 'retire' ? 'btn-outline-danger' : 'btn-primary'" :disabled="busy" class="btn btn-sm me-1" @click="act(p, a)">{{ postActionTitles[a] }}</button>
              <button v-if="canWrite && p.status === 2" class="btn btn-sm btn-outline-secondary" @click="newVersion(p)">نسخه تازه</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal" style="max-width: 560px">
        <h3 class="inv-title mb-3">نسخه تازه پست‌پروسسور</h3>
        <div class="row g-2 small">
          <div class="col-md-5"><label class="form-label mb-0">کد</label><input v-model="form.code" class="form-control form-control-sm inv-ltr" placeholder="HH-TNC640" /></div>
          <div class="col-md-7"><label class="form-label mb-0">عنوان</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-md-5"><label class="form-label mb-0">نرم‌افزار CAM</label><input v-model="form.camSystem" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-7"><label class="form-label mb-0">خانواده کنترلر</label><select v-model.number="form.controllerFamily" class="form-select form-select-sm"><option v-for="(t, k) in controllerFamilies" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-12"><label class="form-label mb-0">فایل پست‌پروسسور</label><input class="form-control form-control-sm" type="file" @change="form.file = ($event.target as HTMLInputElement).files?.[0] || null" /></div>
          <div class="col-12"><label class="form-label mb-0">شرح تغییر</label><input v-model="form.changeNote" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ثبت</button></div>
      </div>
    </div>
  </div>
</template>
