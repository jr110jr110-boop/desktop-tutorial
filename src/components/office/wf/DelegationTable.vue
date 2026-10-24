<script setup lang="ts">
// List of delegations with approve / reject (pending) and cancel. The API checks who may; its message is shown when a call is refused.
import { ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import { approveDelegation, cancelDelegation, getDelegations, rejectDelegation } from "@/api/OfficeAPIs";
import { usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { can, delegationStatuses } from "@/composables/office";

const props = defineProps({
  /** my office: only delegations of positions I hold, where I am the deputy or that I registered (server filter MineOnly) */
  mine: { type: Boolean, default: false },
  /** show the CurrentOnly filter */
  filterable: { type: Boolean, default: true },
});
const filters = ref({ CurrentOnly: false as boolean | null, MineOnly: props.mine ? true : null });
const list = usePagedList(getDelegations, filters);
const busy = ref(false);

const shown = () => list.rows.value;
const stateOf = (d: any) => (d.status === 2 ? { t: delegationStatuses[2], c: "bad" } : !d.isActive ? { t: "لغوشده", c: "bad" } : d.status === 1 ? { t: delegationStatuses[1], c: "warn" } : d.current ? { t: "فعال (جاری)", c: "ok" } : { t: "تاییدشده (خارج از بازه)", c: "info" });

async function act(d: any, kind: "approve" | "reject" | "cancel") {
  if (kind === "cancel" && !confirm("نیابت لغو شود؟")) return;
  busy.value = true;
  const [, error] = await (kind === "approve" ? approveDelegation : kind === "reject" ? rejectDelegation : cancelDelegation)(d.id);
  busy.value = false;
  if (error) return fail(error);
  ok(kind === "approve" ? "نیابت تایید شد." : kind === "reject" ? "نیابت رد شد." : "نیابت لغو شد.");
  list.load();
}
defineExpose({ reload: list.load });
list.load();
</script>

<template>
  <div>
    <div v-if="filterable" class="inv-filters mb-2">
      <label class="small d-flex align-items-center gap-1"><input v-model="filters.CurrentOnly" type="checkbox" class="form-check-input mt-0" />فقط جاری</label>
      <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>سمت</th><th>جانشین</th><th>از تاریخ</th><th>تا تاریخ</th><th>علت</th><th>ثبت‌کننده</th><th>وضعیت</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!shown().length"><td colspan="8" class="text-center inv-muted">نیابتی نیست.</td></tr>
          <tr v-for="d in shown()" :key="d.id">
            <td>{{ d.positionTitle }}</td><td>{{ d.deputyName }}</td><td class="inv-ltr">{{ d.fromDateText }}</td><td class="inv-ltr">{{ d.toDateText }}</td><td>{{ d.reason }}</td>
            <td>{{ d.createdByName }}</td>
            <td><span :class="stateOf(d).c" class="inv-badge">{{ stateOf(d).t }}</span><div v-if="d.decidedByName" class="small inv-muted">{{ d.decidedByName }} - <span class="inv-ltr">{{ d.decidedAtText }}</span></div></td>
            <td class="text-nowrap">
              <button v-if="can(d, 'approve')" :disabled="busy" class="btn btn-sm btn-success me-1" @click="act(d, 'approve')">تایید</button>
              <button v-if="can(d, 'reject')" :disabled="busy" class="btn btn-sm btn-outline-danger me-1" @click="act(d, 'reject')">رد</button>
              <button v-if="can(d, 'cancel')" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="act(d, 'cancel')">لغو</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
  </div>
</template>
