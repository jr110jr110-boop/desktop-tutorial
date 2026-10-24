<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import { getInboxCounts, getInbox, inboxAction } from "@/api/OfficeAPIs";
import { usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { inboxKindKeys, inboxKinds, inboxStatuses, urgencies } from "@/composables/office";

// Office inbox. Click = mark read and open the linked page. "Done" only for items a person may close by hand (see canDone).
const router = useRouter();
const filters = ref({ Status: null as number | null, IncludeClosed: false, Kind: null as number | null, SourceModule: "" });
const list = usePagedList(getInbox, filters);
const counts = ref<any>({ unread: 0, open: 0, overdue: 0, byKind: {} });
const busy = ref(false);
const modules: Record<string, string> = { office: "اتوماسیون اداری", finance: "مالی", procurement: "بازرگانی و خرید", quality: "کیفیت", "project-control": "کنترل پروژه", inventory: "انبار" };

/**
 * Mirrors OrgService.IsGateItem / ActAsync of the backend: approvals (3), referrals (2) and meeting actions (6) are closed by their own action,
 * dispatch / leave-record / req-complete tasks and every non-alert item of another module are closed only by the system.
 */
function canDone(i: any): boolean {
  if (i.status !== 0 && i.status !== 1) return false;
  if (i.kind === 3 || i.kind === 2 || i.kind === 6) return false;
  const key: string = i.sourceKey || "";
  if (key.endsWith(":dispatch") || key.startsWith("leave-record:") || key.startsWith("req-complete:")) return false;
  if (i.sourceModule && i.sourceModule !== "office" && i.kind !== 5) return false;
  return true;
}

/** byKind keys arrive lowerCamel (the server camel-cases dictionary keys); the English enum names are accepted too. */
const kindCount = (name: string | number): number => counts.value.byKind?.[name] ?? counts.value.byKind?.[String(name).charAt(0).toLowerCase() + String(name).slice(1)] ?? 0;

async function loadCounts() {
  const [d] = await getInboxCounts();
  if (d) counts.value = d;
}
function reload() {
  list.load();
  loadCounts();
}

async function open(i: any) {
  if (i.status === 0) {
    const [, error] = await inboxAction(i.id, "read");
    if (error) fail(error);
    else {
      i.status = 1;
      loadCounts();
    }
  }
  if (i.link) {
    const link = String(i.link);
    if (link.startsWith("/") && !link.startsWith("//")) router.push(link);
    else if (/^https?:\/\//i.test(link)) window.open(link, "_blank", "noopener,noreferrer");
  }
}
async function act(i: any, action: "read" | "unread" | "done") {
  busy.value = true;
  const [, error] = await inboxAction(i.id, action);
  busy.value = false;
  if (error) return fail(error);
  if (action === "done") ok("انجام شد.");
  reload();
}
const badge = (i: any) => (i.status === 2 ? "ok" : i.status === 3 ? "bad" : i.status === 0 ? "info" : "warn");
const priorityBadge = (p: number) => (p === 2 ? "bad" : p === 1 ? "warn" : "info");

onMounted(reload);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">کارتابل اداری</h1>
        <button class="btn btn-sm btn-outline-secondary" @click="reload">تازه‌سازی</button>
      </div>

      <div class="d-flex flex-wrap gap-2 mb-3">
        <div class="fn-tile warn"><div class="small inv-muted">خوانده‌نشده</div><b class="fn-num">{{ counts.unread }}</b></div>
        <div class="fn-tile info"><div class="small inv-muted">باز</div><b class="fn-num">{{ counts.open }}</b></div>
        <div class="fn-tile bad"><div class="small inv-muted">سررسید گذشته</div><b class="fn-num">{{ counts.overdue }}</b></div>
        <template v-for="(k, name) in inboxKindKeys" :key="name">
          <div v-if="kindCount(name)" class="fn-tile"><div class="small inv-muted">{{ inboxKinds[k] }}</div><b class="fn-num">{{ kindCount(name) }}</b></div>
        </template>
      </div>

      <div class="inv-filters mb-3">
        <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">باز (خوانده‌نشده و خوانده‌شده)</option><option v-for="(t, k) in inboxStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <select v-model="filters.Kind" class="form-select form-select-sm"><option :value="null">همه انواع</option><option v-for="(t, k) in inboxKinds" :key="k" :value="Number(k)">{{ t }}</option></select>
        <select v-model="filters.SourceModule" class="form-select form-select-sm"><option value="">همه ماژول‌ها</option><option v-for="(t, k) in modules" :key="k" :value="k">{{ t }}</option></select>
        <label class="small d-flex align-items-center gap-1"><input v-model="filters.IncludeClosed" type="checkbox" class="form-check-input mt-0" />نمایش بسته‌شده‌ها</label>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>

      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>نوع</th><th>عنوان</th><th>ماژول مبدا</th><th>سررسید</th><th>اولویت</th><th>گیرنده</th><th>زمان ایجاد</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="9" class="text-center inv-muted">موردی نیست.</td></tr>
            <tr v-for="i in list.rows.value" :key="i.id" :class="{ 'fw-bold': i.status === 0 }" style="cursor: pointer" @click="open(i)">
              <td>{{ inboxKinds[i.kind] }}</td>
              <td>{{ i.title }}</td>
              <td>{{ modules[i.sourceModule] || i.sourceModule }}</td>
              <td class="text-nowrap"><span class="inv-ltr">{{ i.dueDateText }}</span> <span v-if="i.overdue" class="inv-badge bad">سررسید گذشته</span></td>
              <td><span :class="priorityBadge(i.priority)" class="inv-badge">{{ urgencies[i.priority] }}</span></td>
              <td>{{ i.recipientPositionTitle || i.recipientRole || "" }}</td>
              <td class="text-nowrap inv-ltr">{{ i.createdAtText }}</td>
              <td><span :class="badge(i)" class="inv-badge">{{ inboxStatuses[i.status] }}</span></td>
              <td class="text-nowrap" @click.stop>
                <button v-if="canDone(i)" :disabled="busy" class="btn btn-sm btn-success me-1" @click="act(i, 'done')">انجام شد</button>
                <button v-if="i.status === 1" :disabled="busy" class="btn btn-sm btn-link p-0" @click="act(i, 'unread')">خوانده‌نشده</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
      <p class="inv-muted small mb-0">تایید، ارجاع، اقدام جلسه و موارد منتظر مرحله بعد (ارسال نامه، ثبت مرخصی، انجام درخواست، دروازه‌های سایر ماژول‌ها) فقط با انجام کار مربوطه بسته می‌شوند.</p>
    </div>
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
