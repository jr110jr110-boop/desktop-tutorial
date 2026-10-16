<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import ItemPicker from "@/components/inventory/ItemPicker.vue";
import PrintForm from "@/components/print/PrintForm.vue";
import { getStockCount, saveCountLines, stockCountAction } from "@/api/InventoryAPIs";
import { countKinds, countStatuses, fail, jalali, num, ok } from "@/composables/inventory";
import { hasRole, InventoryRoles } from "@/composables/roles";

// Stock count: counter enters quantities, differences are recounted, the warehouse supervisor and the planning manager approve,
// then the differences are posted by adjustment documents (the API creates them).
const route = useRoute();
const count = ref<any>(null);
const busy = ref(false);
const found = ref<any[]>([]);
const onlyDifferences = ref(false);
const printMode = ref<"sheet" | "variance">("variance");

const editable = computed(() => count.value && count.value.status === 0);
const canCount = hasRole(InventoryRoles.counts);
const canApproveWarehouse = hasRole(InventoryRoles.approveWarehouse);
const canApprovePlanning = hasRole(InventoryRoles.approvePlanning);

/** Difference of a line: recount wins over the first count; null when nothing is counted yet. */
function diff(l: any): number | null {
  const counted = l.recountedQuantity ?? l.countedQuantity;
  return counted == null || counted === "" ? null : Number(counted) - Number(l.systemQuantity);
}

const visible = computed(() => (count.value?.lines || []).filter((l: any) => !onlyDifferences.value || (diff(l) !== null && diff(l) !== 0)));
const differences = computed(() => (count.value?.lines || []).filter((l: any) => diff(l) !== null && diff(l) !== 0));

async function load() {
  const [data, error] = await getStockCount(Number(route.params.id));
  if (error) return fail(error);
  count.value = data;
}

async function save(silent = false): Promise<boolean> {
  const lines: any[] = count.value.lines.map((l: any) => ({
    lineId: l.id,
    countedQuantity: l.countedQuantity === "" ? null : l.countedQuantity,
    recountedQuantity: l.recountedQuantity === "" ? null : l.recountedQuantity,
    note: l.note,
  }));
  found.value
    .filter((f) => f.item)
    .forEach((f) => lines.push({ lineId: null, itemId: f.item.id, countedQuantity: f.countedQuantity, recountedQuantity: null, note: f.note || "پیدا شده در شمارش" }));
  busy.value = true;
  const [data, error] = await saveCountLines({ id: count.value.id, lines });
  busy.value = false;
  if (error) {
    fail(error);
    return false;
  }
  count.value = data;
  found.value = [];
  if (!silent) ok("شمارش ذخیره شد.");
  return true;
}

async function act(action: string) {
  if (action === "submit" && !(await save(true))) return;
  if (action === "post" && !confirm("اختلاف‌ها با سند تعدیل در موجودی ثبت شود؟")) return;
  if (action === "cancel" && !confirm("شمارش ابطال شود؟")) return;
  busy.value = true;
  const [data, error] = await stockCountAction(count.value.id, action);
  busy.value = false;
  if (error) return fail(error);
  count.value = data;
  ok("ثبت شد.");
}

async function print(mode: "sheet" | "variance") {
  printMode.value = mode;
  await nextTick();
  window.print();
}

onMounted(load);
</script>

<template>
  <div v-if="count" class="inv-page">
    <!-- count sheet and difference report: the company has no form code for them yet -->
    <PrintForm
      :title="printMode === 'sheet' ? 'برگه شمارش موجودی' : 'گزارش اختلاف شمارش'"
      :fields="[
        { label: 'شماره', value: count.number, ltr: true },
        { label: 'نوع', value: countKinds[count.kind] },
        { label: 'تاریخ شمارش', value: jalali(count.countDate) },
        { label: 'انبار', value: count.warehouseTitle },
        { label: 'زمان ثبت موجودی سیستم', value: jalali(count.snapshotAt, true) },
        { label: 'کدهای شمارش‌شده', value: count.groupCode || 'همه', ltr: true },
      ]"
      :signatures="[
        { title: 'شمارنده' },
        { title: 'انباردار' },
        { title: 'سرپرست انبار', name: count.warehouseApprovedByName, date: jalali(count.warehouseApprovedAt) },
        { title: 'مدیر برنامه‌ریزی و کنترل پروژه', name: count.planningApprovedByName, date: jalali(count.planningApprovedAt) },
      ]"
      distribution="توزیع نسخ: ۱- انبار ۲- برنامه‌ریزی و کنترل پروژه ۳- مالی">
      <table v-if="printMode === 'sheet'">
        <thead>
          <tr>
            <th>ردیف</th>
            <th>محل</th>
            <th>کد کالا</th>
            <th>شرح</th>
            <th>بچ</th>
            <th>واحد</th>
            <th style="width: 18%">مقدار شمارش</th>
            <th style="width: 18%">شمارش مجدد</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(l, i) in count.lines" :key="l.id">
            <td>{{ i + 1 }}</td>
            <td class="inv-ltr">{{ l.locationCode }}</td>
            <td class="inv-ltr">{{ l.itemCode }}</td>
            <td>{{ l.itemTitle }}</td>
            <td class="inv-ltr">{{ l.lotNumber }}</td>
            <td>{{ l.unit }}</td>
            <td></td>
            <td></td>
          </tr>
        </tbody>
      </table>
      <table v-else>
        <thead>
          <tr>
            <th>ردیف</th>
            <th>محل</th>
            <th>کد کالا</th>
            <th>شرح</th>
            <th>بچ</th>
            <th>موجودی سیستم</th>
            <th>شمارش</th>
            <th>شمارش مجدد</th>
            <th>اختلاف</th>
            <th>توضیح</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(l, i) in differences" :key="l.id">
            <td>{{ i + 1 }}</td>
            <td class="inv-ltr">{{ l.locationCode }}</td>
            <td class="inv-ltr">{{ l.itemCode }}</td>
            <td>{{ l.itemTitle }}</td>
            <td class="inv-ltr">{{ l.lotNumber }}</td>
            <td class="num">{{ num(l.systemQuantity) }}</td>
            <td class="num">{{ num(l.countedQuantity) }}</td>
            <td class="num">{{ num(l.recountedQuantity) }}</td>
            <td class="num">{{ num(diff(l)) }}</td>
            <td>{{ l.note }}</td>
          </tr>
          <tr v-if="!differences.length">
            <td colspan="10" class="text-center">اختلافی نیست.</td>
          </tr>
        </tbody>
      </table>
      <p v-if="count.decreaseDocumentNumber || count.increaseDocumentNumber" class="small mt-2">
        سند تعدیل:
        <span class="inv-ltr">{{ [count.decreaseDocumentNumber, count.increaseDocumentNumber].filter(Boolean).join("، ") }}</span>
      </p>
    </PrintForm>

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">
            {{ countKinds[count.kind] }}
            <span class="inv-ltr ms-2">{{ count.number }}</span>
          </h1>
          <span class="inv-badge">{{ countStatuses[count.status] }}</span>
          <span class="inv-muted ms-2">
            {{ count.warehouseTitle }} · {{ jalali(count.countDate) }} · {{ count.linesCount }} ردیف · {{ count.differencesCount }} اختلاف
          </span>
        </div>
        <div class="d-flex flex-wrap gap-2">
          <router-link :to="{ name: 'InventoryStockCounts' }" class="btn btn-sm btn-outline-secondary">بازگشت</router-link>
          <button class="btn btn-sm btn-outline-secondary" @click="print('sheet')">چاپ برگه شمارش</button>
          <button class="btn btn-sm btn-outline-secondary" @click="print('variance')">چاپ گزارش اختلاف</button>
          <template v-if="editable && canCount">
            <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="save()">ذخیره</button>
            <button :disabled="busy" class="btn btn-sm btn-success" @click="act('submit')">ارسال برای تایید</button>
          </template>
          <template v-if="count.status === 1">
            <button v-if="!count.warehouseApprovedAt && canApproveWarehouse" :disabled="busy" class="btn btn-sm btn-outline-success" @click="act('approve-warehouse')">
              تایید سرپرست انبار
            </button>
            <button v-if="!count.planningApprovedAt && canApprovePlanning" :disabled="busy" class="btn btn-sm btn-outline-success" @click="act('approve-planning')">
              تایید برنامه‌ریزی
            </button>
          </template>
          <button v-if="count.status === 2 && canCount" :disabled="busy" class="btn btn-sm btn-primary" @click="act('post')">ثبت اختلاف در موجودی</button>
          <button v-if="count.status < 3 && canCount" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act('cancel')">ابطال</button>
        </div>
      </div>
      <div class="d-flex flex-wrap gap-2 small">
        <span class="inv-badge" :class="{ ok: count.warehouseApprovedAt }">سرپرست انبار: {{ count.warehouseApprovedByName || "—" }}</span>
        <span class="inv-badge" :class="{ ok: count.planningApprovedAt }">برنامه‌ریزی: {{ count.planningApprovedByName || "—" }}</span>
        <span v-if="count.decreaseDocumentId">
          سند کاهش:
          <router-link :to="{ name: 'InventoryDocumentEdit', params: { id: count.decreaseDocumentId } }" class="inv-ltr">{{ count.decreaseDocumentNumber }}</router-link>
        </span>
        <span v-if="count.increaseDocumentId">
          سند افزایش:
          <router-link :to="{ name: 'InventoryDocumentEdit', params: { id: count.increaseDocumentId } }" class="inv-ltr">{{ count.increaseDocumentNumber }}</router-link>
        </span>
        <label class="form-check ms-auto">
          <input v-model="onlyDifferences" class="form-check-input" type="checkbox" />
          فقط ردیف‌های دارای اختلاف
        </label>
      </div>
    </div>

    <div class="inv-card screen-only">
      <div class="table-responsive">
        <table class="table table-sm table-bordered">
          <thead>
            <tr>
              <th>#</th>
              <th>محل</th>
              <th>کالا</th>
              <th>بچ</th>
              <th>موجودی سیستم</th>
              <th style="width: 8rem">شمارش</th>
              <th style="width: 8rem">شمارش مجدد</th>
              <th>اختلاف</th>
              <th>توضیح</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(l, i) in visible" :key="l.id">
              <td>{{ i + 1 }}</td>
              <td class="inv-ltr">{{ l.locationCode }}</td>
              <td>
                <span class="inv-ltr">{{ l.itemCode }}</span> — {{ l.itemTitle }}
              </td>
              <td class="inv-ltr">{{ l.lotNumber }}</td>
              <td class="num">{{ num(l.systemQuantity) }} {{ l.unit }}</td>
              <td>
                <input v-model.number="l.countedQuantity" :disabled="!editable || !canCount" class="form-control form-control-sm" step="any" type="number" />
              </td>
              <td>
                <input v-model.number="l.recountedQuantity" :disabled="!editable || !canCount || diff(l) === 0" class="form-control form-control-sm" step="any" type="number" />
              </td>
              <td class="num" :class="{ 'text-danger fw-bold': diff(l) }">{{ diff(l) === null ? "" : num(diff(l)) }}</td>
              <td>
                <input v-model="l.note" :disabled="!editable || !canCount" class="form-control form-control-sm" />
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <template v-if="editable && canCount">
        <h2 class="inv-title mt-2 mb-2">کالای پیدا شده که در سیستم نبود</h2>
        <div v-for="(f, i) in found" :key="i" class="d-flex gap-2 mb-1">
          <div style="min-width: 320px">
            <ItemPicker v-model="f.item" />
          </div>
          <input v-model.number="f.countedQuantity" class="form-control form-control-sm" placeholder="مقدار" style="width: 8rem" type="number" />
          <input v-model="f.note" class="form-control form-control-sm" placeholder="توضیح" />
          <button class="btn btn-sm btn-link text-danger" @click="found.splice(i, 1)">×</button>
        </div>
        <button class="btn btn-sm btn-outline-primary" @click="found.push({ item: null, countedQuantity: 0, note: '' })">+ کالای پیدا شده</button>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
