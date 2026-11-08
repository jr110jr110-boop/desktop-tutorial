<script setup lang="ts">
import { onMounted, ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import ReasonDialog from "@/components/quality/ReasonDialog.vue";
import ItemPicker from "@/components/inventory/ItemPicker.vue";
import { getNoDrawingFlags, noDrawingAction, requestNoDrawing } from "@/api/QualityAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { noDrawingStatuses, usePaged } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// Package B (H6): the «بدون نقشه» flag of a make part. Without a released drawing a part is not released to the shop, unless engineering flagged it here:
// a reason, a second person who activates it, and a written reason to void it. Every step is audited.
const filters = ref({ Status: null as number | null, Contains: "" });
const list = usePaged(getNoDrawingFlags, filters);
const canRequest = hasRole(QualityRoles.engineering);
const item = ref<any>(null);
const reason = ref("");
const busy = ref(false);
const dialog = ref<null | { id: number; action: string; title: string }>(null);

async function request() {
  if (!item.value) return fail("قطعه را انتخاب کنید.");
  busy.value = true;
  const [, error] = await requestNoDrawing(item.value.id, reason.value);
  busy.value = false;
  if (error) return fail(error);
  ok("درخواست ثبت شد؛ نفر دوم باید آن را فعال کند.");
  item.value = null;
  reason.value = "";
  list.search();
}
async function act(id: number, action: string, note?: string) {
  busy.value = true;
  const [, error] = await noDrawingAction(id, action, note);
  busy.value = false;
  dialog.value = null;
  if (error) return fail(error);
  ok("انجام شد.");
  list.load();
}
onMounted(list.load);
</script>

<template>
  <div class="inv-page">
    <div v-if="canRequest" class="inv-card">
      <h1 class="inv-title">پرچم «بدون نقشه» (درخواست)</h1>
      <div class="row g-2">
        <div class="col-md-4"><label class="form-label small mb-0">قطعه</label><ItemPicker v-model="item" /></div>
        <div class="col-md-6"><label class="form-label small mb-0">دلیل (چرا این قطعه نقشه ندارد یا لازم ندارد)</label><input v-model="reason" class="form-control form-control-sm" /></div>
        <div class="col-md-2 d-flex align-items-end"><button :disabled="busy" class="btn btn-sm btn-primary" @click="request">ثبت درخواست</button></div>
      </div>
      <p class="inv-muted small mt-2 mb-0">قطعه‌ای که نقشه منتشرشده دارد پرچم نمی‌گیرد. درخواست‌کننده نمی‌تواند خودش پرچم را فعال کند و صندلی هوش مصنوعی آن را فعال نمی‌کند.</p>
    </div>
    <div class="inv-card">
      <div class="inv-toolbar"><h2 class="inv-title">پرچم‌های «بدون نقشه»</h2></div>
      <div class="inv-filters mb-3">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره قطعه یا دلیل" @keydown.enter="list.search()" />
        <select v-model="filters.Status" class="form-select form-select-sm" @change="list.search()">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in noDrawingStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <button class="btn btn-sm btn-secondary" @click="list.search()">جستجو</button>
      </div>
      <table class="table table-sm">
        <thead><tr><th>قطعه</th><th>دلیل</th><th>وضعیت</th><th>درخواست‌کننده</th><th>تصمیم‌گیرنده</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="6" class="text-center inv-muted">پرچمی نیست.</td></tr>
          <tr v-for="r in list.rows.value" :key="r.id">
            <td class="inv-ltr">{{ r.itemCode || r.partNumber }} <span class="inv-muted">{{ r.itemTitle }}</span></td>
            <td class="small">{{ r.reason }}<div v-if="r.decisionNote" class="text-danger">تصمیم: {{ r.decisionNote }}</div><div v-if="r.voidReason" class="text-danger">ابطال: {{ r.voidReason }}</div></td>
            <td><span :class="{ ok: r.status === 1, warn: r.status === 0, bad: r.status === 2 || r.status === 3 }" class="inv-badge">{{ noDrawingStatuses[r.status] }}</span></td>
            <td class="small">{{ r.requestedByName }}<br />{{ jalali(r.requestedAt) }}</td>
            <td class="small">{{ r.decidedByName }}<br />{{ r.decidedAt ? jalali(r.decidedAt) : "" }}</td>
            <td class="text-nowrap">
              <button v-if="r.actions.includes('approve')" :disabled="busy" class="btn btn-sm btn-success me-1" @click="act(r.id, 'approve')">فعال‌سازی</button>
              <button v-if="r.actions.includes('reject')" :disabled="busy" class="btn btn-sm btn-outline-warning me-1" @click="dialog = { id: r.id, action: 'reject', title: 'رد درخواست «بدون نقشه»' }">رد</button>
              <button v-if="r.actions.includes('void')" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="dialog = { id: r.id, action: 'void', title: 'ابطال پرچم «بدون نقشه»' }">ابطال</button>
            </td>
          </tr>
        </tbody>
      </table>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>
    <ReasonDialog v-if="dialog" :title="dialog.title" :busy="busy" @confirm="(t: string) => act(dialog!.id, dialog!.action, t)" @close="dialog = null" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
