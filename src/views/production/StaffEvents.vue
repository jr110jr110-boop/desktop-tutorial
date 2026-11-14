<script setup lang="ts">
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import QPager from "@/components/quality/QPager.vue";
import { getEmployeeDirectory } from "@/api/FnAPIs";
import { getStaffEvents, getStaffSummary, saveStaffEvent, staffEventAction } from "@/api/ShopAPIs";
import { camelize, usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";

// Staff performance events (MfStaffEvents): evaluation (score 1..5), disciplinary note, quality feedback, goal, commendation. Written by a supervisor, decided by HR or the production manager
// (never the writer or the subject). Everybody else sees only his own approved events.
const kinds: Record<number, string> = { 1: "ارزیابی", 2: "انضباطی", 3: "بازخورد کیفی", 4: "هدف", 5: "تشویق" };
const statuses: Record<number, string> = { 0: "پیش‌نویس", 1: "منتظر تصمیم", 2: "تأییدشده", 3: "ردشده" };
const writer = hasRole(["SHOPSUPERVISOR", "PRODUCTIONMANAGER", "HR", "QC"]);
const reader = hasRole(["SHOPSUPERVISOR", "PRODUCTIONMANAGER", "HR", "QC", "CEO", "PMO"]);
const filters = ref({ Kind: null as number | null, Status: null as number | null, EmployeeId: null as number | null, Mine: reader ? null : true });
const list = usePagedList(getStaffEvents, filters);
const employee = ref<any>(null);
const f = ref<any>(null);
const busy = ref(false);
const summary = ref<any[] | null>(null);

async function searchEmployees(text: string) {
  const [d] = await getEmployeeDirectory(text);
  return camelize(d) || [];
}
const empLabel = (e: any) => (e ? `${e.name}${e.jobTitle ? " - " + e.jobTitle : ""}` : "");
function pickFilter(e: any) {
  employee.value = e;
  filters.value.EmployeeId = e ? e.id : null;
}
function edit(r?: any) {
  f.value = r
    ? { id: r.id, employee: { id: r.employeeId, name: r.employeeName }, kind: r.kind, eventDate: String(r.eventDate).substring(0, 10), title: r.title, description: r.description || "", score: r.score, rowVersion: r.rowVersion }
    : { id: null, employee: null, kind: hasRole(["QC"]) && !hasRole(["SHOPSUPERVISOR", "PRODUCTIONMANAGER", "HR"]) ? 3 : 1, eventDate: null, title: "", description: "", score: null, rowVersion: null };
}
async function save() {
  const x = f.value;
  if (!x.employee || !x.eventDate) return fail("کارمند و تاریخ را وارد کنید.");
  busy.value = true;
  const [, error] = await saveStaffEvent({ id: x.id, employeeId: x.employee.id, kind: x.kind, eventDate: x.eventDate + "T00:00:00", title: x.title, description: x.description || null, score: x.kind === 1 ? Number(x.score) || null : null, rowVersion: x.rowVersion });
  busy.value = false;
  if (error) return fail(error);
  ok("رویداد ذخیره شد (پیش‌نویس؛ برای تصمیم ارسال کنید).");
  f.value = null;
  list.load();
}
async function act(r: any, action: string) {
  let note: string | null = null;
  if (action === "reject") {
    note = prompt("دلیل رد:");
    if (!note) return;
  }
  if (action === "delete" && !confirm("پیش‌نویس حذف شود؟")) return;
  const [, error] = await staffEventAction({ id: r.id, action, note, rowVersion: r.rowVersion });
  if (error) return fail(error);
  list.load();
}
async function loadSummary() {
  const to = new Date();
  to.setDate(to.getDate() + 1);
  const from = new Date();
  from.setDate(from.getDate() - 90);
  const iso = (d: Date) => d.toISOString().substring(0, 10);
  const [d, error] = await getStaffSummary(iso(from), iso(to));
  if (error) return fail(error);
  summary.value = d;
}
const titles: Record<string, string> = { edit: "ویرایش", submit: "ارسال برای تصمیم", delete: "حذف", approve: "تأیید", reject: "رد" };
onMounted(list.load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">رویدادهای عملکرد کارکنان</h1>
        <div class="d-flex gap-2"><button v-if="reader" class="btn btn-sm btn-outline-secondary" @click="loadSummary">خلاصه ۹۰ روز اخیر</button><button v-if="writer" class="btn btn-sm btn-primary" @click="edit()">رویداد تازه</button></div>
      </div>
      <div v-if="list.notice.value" class="alert alert-info small py-2">{{ list.notice.value }}</div>
      <div v-if="reader" class="inv-filters mb-2">
        <div style="min-width: 240px"><label class="form-label small mb-0">کارمند</label><LookupBox :model-value="employee" :fetch="searchEmployees" :label="empLabel" placeholder="نام کارمند" @pick="pickFilter" /></div>
        <div><label class="form-label small mb-0">نوع</label><select v-model="filters.Kind" class="form-select form-select-sm"><option :value="null">همه</option><option v-for="(t, k) in kinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div><label class="form-label small mb-0">وضعیت</label><select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه</option><option v-for="(t, k) in statuses" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <div v-if="summary" class="table-responsive mb-3">
        <table class="table table-sm">
          <thead><tr><th>کارمند</th><th>ارزیابی</th><th>میانگین امتیاز</th><th>انضباطی</th><th>بازخورد کیفی</th><th>هدف</th><th>تشویق</th></tr></thead>
          <tbody><tr v-for="s in summary" :key="s.employeeId"><td>{{ s.employeeName }}</td><td>{{ s.evaluations }}</td><td>{{ s.averageScore ?? "-" }}</td><td>{{ s.disciplinary }}</td><td>{{ s.qualityFeedback }}</td><td>{{ s.goals }}</td><td>{{ s.commendations }}</td></tr></tbody>
        </table>
      </div>
      <div class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>تاریخ</th><th>کارمند</th><th>نوع</th><th>عنوان</th><th>امتیاز</th><th>وضعیت</th><th>نویسنده</th><th>تصمیم</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="9" class="text-center inv-muted">رویدادی نیست.</td></tr>
            <tr v-for="r in list.rows.value" :key="r.id">
              <td>{{ r.eventDateText }}</td><td>{{ r.employeeName }}</td><td>{{ kinds[r.kind] }}</td><td>{{ r.title }}<div v-if="r.description" class="inv-muted small">{{ r.description }}</div></td>
              <td>{{ r.score ?? "-" }}</td><td>{{ statuses[r.status] }}</td><td class="small">{{ r.createdByName }}</td><td class="small">{{ r.decidedByName || "-" }}<div v-if="r.decisionNote" class="inv-muted">{{ r.decisionNote }}</div></td>
              <td class="text-nowrap"><button v-for="a in r.allowedActions" :key="a" class="btn btn-sm btn-outline-primary me-1" @click="a === 'edit' ? edit(r) : act(r, a)">{{ titles[a] }}</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>

    <div v-if="f" class="inv-modal-backdrop" @click.self="f = null">
      <div class="inv-modal" style="max-width: 560px">
        <h3 class="inv-title mb-3">رویداد عملکرد</h3>
        <div class="row g-2 small">
          <div class="col-md-12"><label class="form-label mb-0">کارمند</label><LookupBox :model-value="f.employee" :fetch="searchEmployees" :label="empLabel" placeholder="نام کارمند" @pick="(e: any) => (f.employee = e)" /></div>
          <div class="col-md-6"><label class="form-label mb-0">نوع</label><select v-model.number="f.kind" class="form-select form-select-sm"><option v-for="(t, k) in kinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-6"><label class="form-label mb-0">تاریخ</label><FnDate v-model="f.eventDate" /></div>
          <div class="col-md-12"><label class="form-label mb-0">عنوان</label><input v-model="f.title" class="form-control form-control-sm" /></div>
          <div v-if="f.kind === 1" class="col-md-4"><label class="form-label mb-0">امتیاز (۱ تا ۵)</label><select v-model.number="f.score" class="form-select form-select-sm"><option v-for="s in 5" :key="s" :value="s">{{ s }}</option></select></div>
          <div class="col-md-12"><label class="form-label mb-0">شرح</label><textarea v-model="f.description" class="form-control form-control-sm" rows="3" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="f = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
