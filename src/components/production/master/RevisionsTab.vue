<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { useRouter } from "vue-router";
import LookupBox from "@/components/fn/LookupBox.vue";
import QPager from "@/components/quality/QPager.vue";
import { getRouting, getRoutings, routingAction, saveRouting } from "@/api/ProductionAPIs";
import { camelize, usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { badgeOfRouting, hoursText, inspectionRequirements, productionActionTitles, routingStatuses } from "@/composables/production";
import { ProductionRoles, hasRole } from "@/composables/roles";
import { itemLabel, loadGroups, searchItems } from "./masterKit";

// Routing revisions of an item or item group. A draft needs two different people to approve (production manager, then engineering; the creator may not approve): the buttons follow the role and the state,
// the maker-checker refusals come back as Persian messages from the API and are shown as they are.
const props = defineProps<{ openId: number | null }>();
const router = useRouter();
const filters = ref({ ItemId: null as number | null, ItemGroupId: null as number | null, Status: null as number | null, Contains: "" });
const list = usePagedList(getRoutings, filters);
const item = ref<any>(null);
const groups = ref<any[]>([]);
const detail = ref<any>(null);
const form = ref<null | { item: any; groupId: number | null; note: string }>(null);
const busy = ref(false);
const canWrite = hasRole(ProductionRoles.routingWriters);
const canProduction = hasRole(ProductionRoles.routingApproveProduction);
const canEngineering = hasRole(ProductionRoles.routingApproveEngineering);

function pickItem(i: any) {
  item.value = i;
  filters.value.ItemId = i ? i.id : null;
  if (i) filters.value.ItemGroupId = null;
}
async function loadDetail(id: number | null) {
  detail.value = null;
  if (!id) return;
  const [d, error] = await getRouting(id);
  if (error) return fail(error);
  detail.value = camelize(d);
}
watch(() => props.openId, loadDetail);

function open(r: any) {
  router.push({ name: "MfRouting", params: { id: r.id } });
}
function closeDetail() {
  router.push({ name: "MfRoutings" });
  list.load();
}

async function create() {
  const f = form.value!;
  if (!f.item && !f.groupId) return fail("کالا یا گروه کالا را انتخاب کنید.");
  busy.value = true;
  const [d, error] = await saveRouting({ itemId: f.item ? f.item.id : null, itemGroupId: f.item ? null : f.groupId, note: f.note.trim() || null });
  busy.value = false;
  if (error) return fail(error);
  ok("نسخه پیش‌نویس مسیر ساخت ساخته شد.");
  form.value = null;
  const created = camelize(d);
  if (created?.id) router.push({ name: "MfRouting", params: { id: created.id } });
  else list.load();
}

async function act(action: "approve-production" | "approve-engineering" | "delete") {
  const r = detail.value;
  if (action === "delete" && !confirm("پیش‌نویس این نسخه حذف شود؟")) return;
  busy.value = true;
  const [, error] = await routingAction({ id: r.id, action, rowVersion: r.rowVersion });
  busy.value = false;
  if (error) return fail(error); // the maker-checker and missing-standard refusals are Persian messages of the API
  ok(action === "delete" ? "پیش‌نویس حذف شد." : productionActionTitles[action] + " ثبت شد.");
  if (action === "delete") return closeDetail();
  await loadDetail(r.id);
  list.load();
}

const target = (r: any) => (r.itemCode ? `${r.itemCode} - ${r.itemTitle || ""}` : r.itemGroupCode ? `گروه ${r.itemGroupCode}` : "-");

onMounted(async () => {
  groups.value = await loadGroups();
  list.load();
  loadDetail(props.openId);
});
defineExpose({ reload: list.load });
</script>

<template>
  <div>
    <template v-if="openId">
      <div class="inv-toolbar">
        <h2 class="inv-title">نسخه {{ detail?.revision }} مسیر ساخت <span v-if="detail" class="inv-ltr">{{ target(detail) }}</span></h2>
        <button class="btn btn-sm btn-outline-secondary" @click="closeDetail">بازگشت به فهرست</button>
      </div>
      <p v-if="!detail" class="inv-muted">در حال بارگذاری...</p>
      <template v-else>
        <div class="row g-2 mb-3 small">
          <div class="col-md-3"><b>وضعیت:</b> <span :class="badgeOfRouting(detail.status)" class="inv-badge">{{ routingStatuses[detail.status] }}</span></div>
          <div class="col-md-3"><b>ایجاد:</b> {{ detail.createdByName }} - {{ detail.createdAtText }}</div>
          <div class="col-md-3"><b>تایید تولید:</b> {{ detail.productionApprovedByName ? detail.productionApprovedByName + " - " + detail.productionApprovedAtText : "هنوز تایید نشده" }}</div>
          <div class="col-md-3"><b>تایید مهندسی:</b> {{ detail.engineeringApprovedByName ? detail.engineeringApprovedByName + " - " + detail.engineeringApprovedAtText : "هنوز تایید نشده" }}</div>
          <div v-if="detail.status === 1" class="col-md-6"><b>تاریخ تایید نهایی:</b> {{ detail.approvedAtText }} <span :class="detail.contentMatches ? 'ok' : 'bad'" class="inv-badge">{{ detail.contentMatches ? "مراحل پس از تایید تغییر نکرده" : "مراحل پس از تایید تغییر کرده است" }}</span></div>
          <div v-if="detail.note" class="col-12"><b>توضیح:</b> {{ detail.note }}</div>
        </div>
        <div class="table-responsive">
          <table class="table table-sm">
            <thead><tr><th>ردیف</th><th>مرحله</th><th>مرکز کار</th><th>راه‌اندازی</th><th>اجرا / واحد</th><th>بیرونی</th><th>بازرسی</th><th>استاندارد</th></tr></thead>
            <tbody>
              <tr v-if="!detail.steps?.length"><td colspan="8" class="text-center inv-muted">مرحله‌ای تعریف نشده است.</td></tr>
              <tr v-for="s in detail.steps" :key="s.templateId">
                <td>{{ s.sequence }}</td><td>{{ s.title }}</td><td class="inv-ltr">{{ s.workCenterCode || "-" }}</td><td>{{ hoursText(s.setupHours) }}</td><td>{{ hoursText(s.runHoursPerUnit) }}</td>
                <td>{{ s.isOutside ? "بله" : "خیر" }}</td><td>{{ inspectionRequirements[s.inspectionRequirement] }}<span v-if="s.requiresQc" class="inv-muted"> (QC)</span></td>
                <td><span :class="s.standardId ? 'ok' : 'warn'" class="inv-badge">{{ s.standardId ? "ثبت‌شده" : "ثبت نشده" }}</span></td>
              </tr>
            </tbody>
          </table>
        </div>
        <div v-if="detail.status === 0" class="d-flex flex-wrap gap-2">
          <button v-if="canProduction && !detail.productionApprovedByName" :disabled="busy" class="btn btn-sm btn-success" @click="act('approve-production')">تایید تولید</button>
          <button v-if="canEngineering && !detail.engineeringApprovedByName" :disabled="busy" class="btn btn-sm btn-success" @click="act('approve-engineering')">تایید مهندسی</button>
          <button v-if="canWrite" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act('delete')">حذف پیش‌نویس</button>
        </div>
        <p v-if="detail.status === 0" class="inv-muted mt-2 mb-0">تایید به دو نفر متفاوت نیاز دارد: مدیر تولید و مهندسی؛ سازنده نسخه نمی‌تواند تایید کند و برای تایید باید برای همه مراحل استاندارد ثبت شده باشد.</p>
      </template>
    </template>

    <template v-else>
      <div class="inv-toolbar">
        <div class="inv-filters">
          <div style="min-width: 240px"><label class="form-label small mb-0">کالا</label><LookupBox :model-value="item" :fetch="searchItems" :label="itemLabel" placeholder="کد یا نام کالا" @pick="pickItem" /></div>
          <div><label class="form-label small mb-0">گروه کالا</label><select v-model="filters.ItemGroupId" class="form-select form-select-sm" @change="filters.ItemGroupId && pickItem(null)"><option :value="null">همه گروه‌ها</option><option v-for="g in groups" :key="g.id" :value="g.id">{{ g.code }} - {{ g.title }}</option></select></div>
          <div><label class="form-label small mb-0">وضعیت</label><select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه</option><option v-for="(t, k) in routingStatuses" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
        </div>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="form = { item: null, groupId: null, note: '' }">باز کردن نسخه پیش‌نویس</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>کالا / گروه</th><th>نسخه</th><th>وضعیت</th><th>ایجاد</th><th>تایید تولید</th><th>تایید مهندسی</th><th>تایید نهایی</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="8" class="text-center inv-muted">نسخه‌ای پیدا نشد.</td></tr>
            <tr v-for="r in list.rows.value" :key="r.id">
              <td>{{ target(r) }}</td><td>{{ r.revision }}</td><td><span :class="badgeOfRouting(r.status)" class="inv-badge">{{ routingStatuses[r.status] }}</span></td>
              <td>{{ r.createdAtText }}</td><td>{{ r.productionApprovedAtText || "-" }}</td><td>{{ r.engineeringApprovedAtText || "-" }}</td><td>{{ r.approvedAtText || "-" }}</td>
              <td><button class="btn btn-sm btn-outline-primary" @click="open(r)">مشاهده</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </template>

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal" style="max-width: 520px">
        <h2 class="inv-title mb-2">نسخه پیش‌نویس جدید مسیر ساخت</h2>
        <div class="mb-2"><label class="form-label small mb-0">کالا</label><LookupBox v-model="form.item" :fetch="searchItems" :label="itemLabel" placeholder="کد یا نام کالا" /></div>
        <div v-if="!form.item" class="mb-2"><label class="form-label small mb-0">یا گروه کالا</label><select v-model="form.groupId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="g in groups" :key="g.id" :value="g.id">{{ g.code }} - {{ g.title }}</option></select></div>
        <div class="mb-2"><label class="form-label small mb-0">توضیح تغییرات</label><textarea v-model="form.note" class="form-control form-control-sm" rows="2"></textarea></div>
        <p class="inv-muted mb-0">مراحل نسخه از مراحل ثبت‌شده کالا یا گروه برداشته می‌شود.</p>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="create">ایجاد</button></div>
      </div>
    </div>
  </div>
</template>
