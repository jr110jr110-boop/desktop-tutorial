<script setup lang="ts">
import { onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import CodeCheck from "@/components/inventory/CodeCheck.vue";
import { getCodingValues, getItemRequests, reviewItemRequest, saveItemRequest } from "@/api/InventoryAPIs";
import { fail, itemRequestStatuses, jalali, ok, printPage, trackingModes } from "@/composables/inventory";
import { hasRole, InventoryRoles } from "@/composables/roles";

// Requests for new item codes: engineering asks, planning and project control approves and gives the code.
const requests = ref<any[]>([]);
const groups = ref<any[]>([]);
const status = ref<number | null>(null);
const form = ref<any>(null);
const review = ref<any>(null);
const saving = ref(false);
const canRequest = hasRole(InventoryRoles.itemRequest);
const canReview = hasRole(InventoryRoles.planning);

async function load() {
  const [data, error] = await getItemRequests(status.value);
  if (error) return fail(error);
  requests.value = data || [];
}

function newRequest() {
  form.value = {
    id: null,
    title: "",
    groupCode: "02",
    proposedCode: "",
    technicalCode: "",
    material: "",
    standard: "",
    mescCode: "",
    unit: "عدد",
    trackingMode: 2,
    specification: "",
    reason: "",
  };
}

async function send() {
  saving.value = true;
  const [, error] = await saveItemRequest(form.value);
  saving.value = false;
  if (error) return fail(error);
  form.value = null;
  ok("درخواست ثبت شد و برای برنامه‌ریزی ارسال شد.");
  load();
}

function openReview(row: any) {
  review.value = { row, id: row.id, code: row.proposedCode || (row.groupCode === "08" ? row.mescCode : ""), note: "" };
}

async function decide(approve: boolean) {
  saving.value = true;
  const [data, error] = await reviewItemRequest({ id: review.value.id, approve, code: review.value.code, note: review.value.note });
  saving.value = false;
  if (error) return fail(error);
  review.value = null;
  ok(approve ? `کالا با کد ${data.itemCode} تعریف شد.` : "درخواست رد شد.");
  load();
}

onMounted(async () => {
  const [values] = await getCodingValues("Group");
  groups.value = values || [];
  load();
});
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="درخواست‌های کد کالا" :subtitle="status === null ? 'همه' : itemRequestStatuses[status]" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">درخواست کد کالا (WI-WH-002)</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <button v-if="canRequest" class="btn btn-sm btn-primary" @click="newRequest">+ درخواست کد</button>
        </div>
      </div>
      <p class="inv-muted screen-only">
        طبق دستورالعمل، فرمت کد را مهندسی تعریف می‌کند و کد را برنامه‌ریزی و کنترل پروژه می‌دهد. کد تکراری و کد خارج از جدول‌ها پذیرفته نمی‌شود. ★ فقط مراحل
        انبارشی قطعه (خام ریخته‌گری، رافینگ، ماشینکاری نهایی، قطعه نهایی، رنگ‌شده) کد جدا می‌گیرند.
      </p>
      <div class="inv-filters mb-3">
        <select v-model="status" class="form-select form-select-sm" @change="load">
          <option :value="null">همه</option>
          <option v-for="(t, k) in itemRequestStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
      </div>

      <table class="table table-sm table-hover">
        <thead>
          <tr>
            <th>شماره</th>
            <th>تاریخ</th>
            <th>درخواست‌کننده</th>
            <th>شرح</th>
            <th>گروه</th>
            <th>کد پیشنهادی</th>
            <th>شماره نقشه / MESC</th>
            <th>وضعیت</th>
            <th class="d-print-none"></th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="!requests.length">
            <td class="text-center inv-muted" colspan="9">درخواستی نیست.</td>
          </tr>
          <tr v-for="r in requests" :key="r.id">
            <td class="inv-ltr">{{ r.number }}</td>
            <td>{{ jalali(r.createDate) }}</td>
            <td>{{ r.creatorName }}</td>
            <td>
              {{ r.title }}
              <div v-if="r.reason" class="inv-muted">{{ r.reason }}</div>
            </td>
            <td class="inv-ltr">{{ r.groupCode }}</td>
            <td class="inv-ltr">{{ r.proposedCode }}</td>
            <td class="inv-ltr">{{ r.technicalCode || r.mescCode }}</td>
            <td>
              <span class="inv-badge" :class="r.status === 1 ? 'ok' : r.status === 2 ? 'bad' : 'warn'">{{ itemRequestStatuses[r.status] }}</span>
              <div v-if="r.itemCode" class="inv-ltr small">{{ r.itemCode }}</div>
              <div v-if="r.reviewNote" class="inv-muted">{{ r.reviewNote }}</div>
            </td>
            <td class="d-print-none">
              <button v-if="r.status === 0 && canReview" class="btn btn-sm btn-outline-primary py-0" @click="openReview(r)">بررسی</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal" style="max-width: 680px">
        <h2 class="inv-title mb-2">درخواست کد کالای جدید</h2>
        <div class="row g-2">
          <div class="col-8">
            <label class="form-label small mb-0">شرح کالا *</label>
            <input v-model="form.title" class="form-control form-control-sm" />
          </div>
          <div class="col-4">
            <label class="form-label small mb-0">گروه *</label>
            <select v-model="form.groupCode" class="form-select form-select-sm">
              <option v-for="g in groups" :key="g.code" :value="g.code">{{ g.code }} - {{ g.title }}</option>
            </select>
          </div>
          <div v-if="form.groupCode === '08'" class="col-6">
            <label class="form-label small mb-0">کد MESC *</label>
            <input v-model="form.mescCode" class="form-control form-control-sm inv-ltr" placeholder="16.10.05.001.2" />
          </div>
          <div v-else class="col-6">
            <label class="form-label small mb-0">کد پیشنهادی (۱۴ رقم)</label>
            <input v-model="form.proposedCode" class="form-control form-control-sm inv-ltr" />
          </div>
          <div class="col-6">
            <label class="form-label small mb-0">شماره نقشه{{ form.groupCode === "02" ? " *" : "" }}</label>
            <input v-model="form.technicalCode" class="form-control form-control-sm inv-ltr" />
          </div>
          <div class="col-12">
            <CodeCheck :code="form.groupCode === '08' ? form.mescCode : form.proposedCode" />
          </div>
          <div class="col-4">
            <label class="form-label small mb-0">واحد *</label>
            <input v-model="form.unit" class="form-control form-control-sm" />
          </div>
          <div class="col-4">
            <label class="form-label small mb-0">نوع ردیابی</label>
            <select v-model="form.trackingMode" class="form-select form-select-sm">
              <option v-for="(t, k) in trackingModes" :key="k" :value="Number(k)">{{ t }}</option>
            </select>
          </div>
          <div class="col-4">
            <label class="form-label small mb-0">جنس</label>
            <input v-model="form.material" class="form-control form-control-sm" />
          </div>
          <div class="col-6">
            <label class="form-label small mb-0">استاندارد</label>
            <input v-model="form.standard" class="form-control form-control-sm" />
          </div>
          <div class="col-6">
            <label class="form-label small mb-0">مشخصات</label>
            <input v-model="form.specification" class="form-control form-control-sm" />
          </div>
          <div class="col-12">
            <label class="form-label small mb-0">دلیل / پروژه</label>
            <input v-model="form.reason" class="form-control form-control-sm" />
          </div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button :disabled="saving" class="btn btn-sm btn-primary" @click="send">ثبت درخواست</button>
        </div>
      </div>
    </div>

    <div v-if="review" class="inv-modal-backdrop" @click.self="review = null">
      <div class="inv-modal" style="max-width: 640px">
        <h2 class="inv-title mb-2">بررسی درخواست {{ review.row.number }}</h2>
        <p class="small mb-2">
          {{ review.row.title }} · نقشه <span class="inv-ltr">{{ review.row.technicalCode || "-" }}</span> · {{ review.row.unit }} ·
          {{ trackingModes[review.row.trackingMode] }}
        </p>
        <label class="form-label small mb-0">کد نهایی</label>
        <input v-model="review.code" class="form-control form-control-sm inv-ltr" />
        <CodeCheck :code="review.code" />
        <label class="form-label small mb-0 mt-2">توضیح (برای رد الزامی)</label>
        <input v-model="review.note" class="form-control form-control-sm" />
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="review = null">انصراف</button>
          <button :disabled="saving" class="btn btn-sm btn-outline-danger" @click="decide(false)">رد</button>
          <button :disabled="saving" class="btn btn-sm btn-success" @click="decide(true)">تایید و تعریف کالا</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
