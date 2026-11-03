<script setup lang="ts">
// A paged list of requests that a second person decides (maker-checker): the columns come from the page, the buttons of a row come from the page too
// (usually the allowedActions of the API, or a role + status rule). A button can ask for a written reason first (ReasonDialog); the API checks it again and
// its Persian refusal is shown. After an action the list is read again, because the request may have been decided meanwhile by somebody else.
import { computed, onMounted, ref, watch } from "vue";
import QPager from "@/components/quality/QPager.vue";
import ReasonDialog from "@/components/quality/ReasonDialog.vue";
import { usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import type { ApprovalAction, ApprovalColumn } from "./approvalTypes";


const props = defineProps<{
  /** (params) => [data, error]; params carry PageNumber / PageSize and the filters */
  fetcher: (params: any) => Promise<any>;
  filters?: Record<string, any>;
  columns: ApprovalColumn[];
  /** the buttons of a row */
  actions: (row: any) => ApprovalAction[];
  /** run the action: (row, action key, note) => [data, error] */
  run: (row: any, action: string, note: string | null) => Promise<any>;
  /** a stable row key (default id) */
  rowKey?: string;
  emptyText?: string;
  /** the page shows a detail of the row under it (opened with the row button) */
  detail?: boolean;
}>();
const emit = defineEmits<{ (e: "changed"): void }>();

const filters = ref<any>({ ...(props.filters || {}) });
const list = usePagedList(props.fetcher, filters);
const busy = ref(false);
const asking = ref<{ row: any; action: ApprovalAction } | null>(null);
const open = ref<any>(null);
const keyOf = (row: any) => row[props.rowKey || "id"];

function cell(row: any, c: ApprovalColumn): string {
  const v = c.text ? c.text(row) : row[c.key];
  return v === null || v === undefined ? "" : String(v);
}

async function exec(row: any, action: ApprovalAction, note: string | null) {
  busy.value = true;
  const [data, error] = await props.run(row, action.key, note);
  busy.value = false;
  asking.value = null;
  if (error) {
    fail(error);
  } else {
    ok(action.done || "انجام شد.");
    emit("changed");
  }
  // decided meanwhile or not, the list is read again
  void data;
  await list.load();
}
function click(row: any, action: ApprovalAction) {
  if (action.reason) asking.value = { row, action };
  else void exec(row, action, null);
}

watch(
  () => props.filters,
  (value) => {
    filters.value = { ...(value || {}) };
    void list.search();
  },
  { deep: true }
);
defineExpose({ reload: list.load, search: list.search });
onMounted(list.search);
const colCount = computed(() => props.columns.length + 1);
</script>

<template>
  <div>
    <div class="table-responsive">
      <table class="table table-sm table-hover align-middle">
        <thead>
          <tr>
            <th v-for="c in columns" :key="c.key">{{ c.title }}</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="!list.rows.value.length">
            <td :colspan="colCount" class="text-center inv-muted">{{ list.loading.value ? "در حال بارگذاری…" : emptyText || "موردی نیست." }}</td>
          </tr>
          <template v-for="row in list.rows.value" :key="keyOf(row)">
            <tr>
              <td v-for="c in columns" :key="c.key" :class="{ 'inv-ltr': c.ltr }">
                <slot :name="'cell-' + c.key" :row="row" :text="cell(row, c)">
                  <span v-if="c.badge" :class="c.badge(row)" class="inv-badge">{{ cell(row, c) }}</span>
                  <template v-else>{{ cell(row, c) }}</template>
                </slot>
              </td>
              <td class="text-nowrap">
                <button v-if="detail" class="btn btn-sm btn-outline-secondary me-1" @click="open = open === row ? null : row">جزئیات</button>
                <button v-for="a in actions(row)" :key="a.key" :class="a.btn || 'btn-outline-primary'" :disabled="busy" class="btn btn-sm me-1" @click="click(row, a)">{{ a.label }}</button>
              </td>
            </tr>
            <tr v-if="detail && open === row">
              <td :colspan="colCount" class="bg-light"><slot name="detail" :row="row"></slot></td>
            </tr>
          </template>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />

    <ReasonDialog
      v-if="asking"
      :title="asking.action.label"
      :hint="asking.action.hint || ''"
      :required="asking.action.reason !== 'optional'"
      :busy="busy"
      @confirm="(text: string) => exec(asking!.row, asking!.action, text)"
      @close="asking = null" />
  </div>
</template>
