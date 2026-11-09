<script setup lang="ts">
import { onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getPrograms, getProgram, defaultProgram, programAction, rowsOf } from "@/api/AuditAPIs";
import { fail, ok, printPage } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { programStatuses, frequencies, shamsiYearNow } from "@/composables/audit";

// Annual audit programme (ISO 19011 clause 5): prepared and submitted by the PMO, approved by the QA manager (another person). A new version supersedes the old one.
const rows = ref<any[]>([]);
const open = ref<any>(null);
const year = ref(shamsiYearNow());
const note = ref("");
const canPrepare = hasRole(["PMO"]);

async function load() {
  const [d, e] = await getPrograms();
  if (e) return fail(e);
  rows.value = rowsOf(d);
}
async function show(id: number) {
  const [d, e] = await getProgram(id);
  if (e) return fail(e);
  open.value = d;
}
async function draft() {
  const [d, e] = await defaultProgram(year.value);
  if (e) return fail(e);
  ok("پیش‌نویس برنامه ساخته شد.");
  await load();
  open.value = d;
}
async function act(action: string) {
  const [d, e] = await programAction(open.value.id, action, note.value);
  if (e) return fail(e);
  open.value = d;
  note.value = "";
  ok("ثبت شد.");
  await load();
}
onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="برنامه‌ی ممیزی داخلی" subtitle="ISO 19011 بند ۵" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">برنامه‌ی ممیزی داخلی</h1>
        <div class="d-flex gap-2 d-print-none">
          <template v-if="canPrepare">
            <input v-model.number="year" class="form-control form-control-sm inv-ltr" style="width: 90px" />
            <button class="btn btn-sm btn-primary" @click="draft">پیش‌نویس برنامه‌ی پیش‌فرض</button>
          </template>
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
        </div>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>سال</th><th>نسخه</th><th>عنوان</th><th>وضعیت</th><th>تنظیم‌کننده</th><th>تصمیم‌گیرنده</th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td class="text-center inv-muted" colspan="6">برنامه‌ای ثبت نشده است.</td></tr>
          <tr v-for="r in rows" :key="r.id" style="cursor: pointer" @click="show(r.id)">
            <td>{{ r.year }}</td><td>{{ r.version }}</td><td>{{ r.title }}</td><td>{{ programStatuses[r.statusValue] }}</td><td>{{ r.preparedByName }}</td><td>{{ r.decidedByName }} {{ r.decidedAtText }}</td>
          </tr>
        </tbody>
      </table>
      <div v-if="open" class="mt-3">
        <h2 class="h6">{{ open.title }} - نسخه‌ی {{ open.version }}</h2>
        <p class="small"><strong>دامنه:</strong> {{ open.scope }}<br /><strong>معیارها:</strong> {{ open.criteria }}<br /><strong>اهداف:</strong> {{ open.objectives }}</p>
        <div class="small inv-muted inv-ltr" v-if="open.contentSha256">SHA-256: {{ open.contentSha256 }}</div>
        <table class="table table-sm">
          <thead><tr><th>فرایند</th><th>عنوان</th><th>دوره</th><th>سررسید نخست</th><th>قواعد</th></tr></thead>
          <tbody><tr v-for="(i, k) in open.items" :key="k"><td>{{ i.processTitle }}</td><td>{{ i.title }}</td><td>{{ frequencies[i.frequency] }}</td><td>{{ i.firstDueText }}</td><td>{{ i.rules }}</td></tr></tbody>
        </table>
        <div v-if="open.allowed?.length" class="d-print-none d-flex gap-2 align-items-center">
          <input v-model="note" class="form-control form-control-sm" placeholder="توضیح (برای رد الزامی است)" />
          <button v-if="open.allowed.includes('submit')" class="btn btn-sm btn-primary" @click="act('submit')">ارسال برای تایید QA</button>
          <button v-if="open.allowed.includes('approve')" class="btn btn-sm btn-success" @click="act('approve')">تایید (QA)</button>
          <button v-if="open.allowed.includes('reject')" class="btn btn-sm btn-outline-danger" @click="act('reject')">رد</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
