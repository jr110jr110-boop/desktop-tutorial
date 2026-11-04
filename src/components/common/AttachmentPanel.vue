<script setup lang="ts">
// PB3: one panel for the files of any record (purchase request and line, inquiry, quote, order, receipt, inspection, NCR, operation,
// invoice, payment, petty cash, item, lot ...). Upload with kind / title / revision, list, download, void with a reason, supersede
// with a newer revision, remove before approval, and (on a record downstream of a purchase request) the request's technical
// documents, read-only. A file is never deleted: the panel shows voided and superseded files in the history.
import { computed, ref, watch } from "vue";
import { getAttachmentPanel, removeAttachment, uploadAttachment, voidAttachment } from "@/api/AttachmentAPIs";
import { downloadFile } from "@/api/InventoryAPIs";
import { attachmentKinds, defaultAttachmentKind, fileSizeText } from "@/composables/attachments";
import { fail, jalali, ok } from "@/composables/inventory";

const props = defineProps({
  ownerType: { type: Number, required: true },
  ownerId: { type: Number as () => number | null, default: null },
  /** Heading; the panel falls back to «پیوست‌ها». */
  title: { type: String, default: "" },
  /** Only the list: no upload, void or remove (a screen that is print-only or read-only). */
  readonly: { type: Boolean, default: false },
  /** Starts closed (a panel inside a table row). */
  collapsed: { type: Boolean, default: false },
});
const emit = defineEmits(["changed"]);

const panel = ref<any>(null);
const loading = ref(false);
const busy = ref(false);
const open = ref(!props.collapsed);
const form = ref({ kind: defaultAttachmentKind[props.ownerType] || 99, title: "", revision: "", file: null as File | null });
const supersede = ref<any>(null);
const voiding = ref<{ item: any; reason: string } | null>(null);
const fileInput = ref<HTMLInputElement | null>(null);
const showHistory = ref(false);

const items = computed<any[]>(() => panel.value?.items || []);
const current = computed(() => items.value.filter((i) => i.current));
const history = computed(() => items.value.filter((i) => !i.current));
const propagated = computed<any[]>(() => panel.value?.propagated || []);
const canWrite = computed(() => !props.readonly && !!panel.value?.canWrite);
const accept = computed(() => (panel.value?.allowedExtensions || []).join(","));
const countText = computed(() => {
  const own = current.value.length;
  const inherited = propagated.value.reduce((s: number, g: any) => s + g.items.length, 0);
  return inherited ? `${own} + ${inherited} فنی` : String(own);
});

async function load() {
  if (!props.ownerId) {
    panel.value = null;
    return;
  }
  loading.value = true;
  const [data, error] = await getAttachmentPanel(props.ownerType, props.ownerId);
  loading.value = false;
  if (error) {
    panel.value = null;
    // a user who may not read the record has no panel: nothing is shown, no noise
    return;
  }
  panel.value = data;
}

function pick(event: Event) {
  const input = event.target as HTMLInputElement;
  form.value.file = input.files && input.files[0] ? input.files[0] : null;
}

/** The same checks as the API (type list and size cap) so the person is told before the upload. */
function localRefusal(file: File): string | null {
  const dot = file.name.lastIndexOf(".");
  const ext = dot >= 0 ? file.name.slice(dot).toLowerCase() : "";
  const allowed: string[] = panel.value?.allowedExtensions || [];
  if (allowed.length && !allowed.includes(ext)) return `فایل با پسوند «${ext || "بدون پسوند"}» پذیرفته نمی‌شود. پسوندهای مجاز: ${allowed.join("، ")}.`;
  const max = Number(panel.value?.maxBytes) || 0;
  if (max && file.size > max) return `حجم فایل بیش از حد مجاز است (حداکثر ${Math.round(max / 1024 / 1024)} مگابایت).`;
  if (!file.size) return "فایل خالی است.";
  return null;
}

async function upload(supersedesId: number | null = null, file: File | null = null) {
  const f = file || form.value.file;
  if (!f || !props.ownerId) return fail("فایل را انتخاب کنید.");
  const refusal = localRefusal(f);
  if (refusal) return fail(refusal);
  busy.value = true;
  const [, error] = await uploadAttachment({
    ownerType: props.ownerType,
    ownerId: props.ownerId,
    kind: supersedesId ? supersede.value.kind : form.value.kind,
    title: supersedesId ? supersede.value.title : form.value.title,
    revision: supersedesId ? supersede.value.revision : form.value.revision,
    supersedesId,
    file: f,
  });
  busy.value = false;
  if (error) return fail(error);
  ok(supersedesId ? "نسخه جدید جایگزین شد." : "فایل پیوست شد.");
  form.value = { kind: form.value.kind, title: "", revision: "", file: null };
  if (fileInput.value) fileInput.value.value = "";
  supersede.value = null;
  await load();
  emit("changed");
}

function startSupersede(item: any) {
  supersede.value = { item, kind: item.kind, title: item.title || "", revision: "", file: null };
}

function pickSupersede(event: Event) {
  const input = event.target as HTMLInputElement;
  supersede.value.file = input.files && input.files[0] ? input.files[0] : null;
}

async function confirmSupersede() {
  if (!supersede.value?.file) return fail("فایل نسخه جدید را انتخاب کنید.");
  await upload(supersede.value.item.id, supersede.value.file);
}

async function confirmVoid() {
  const v = voiding.value;
  if (!v) return;
  if ((v.reason || "").trim().length < 3) return fail("دلیل ابطال را بنویسید.");
  busy.value = true;
  const [, error] = await voidAttachment(v.item.id, v.reason.trim());
  busy.value = false;
  if (error) return fail(error);
  ok("پیوست ابطال شد؛ فایل و سابقه آن نگهداری می‌شود.");
  voiding.value = null;
  await load();
  emit("changed");
}

async function remove(item: any) {
  if (!window.confirm(`«${item.title || item.fileName}» از این رکورد برداشته شود؟ (پیش از تایید؛ در سابقه ثبت می‌شود)`)) return;
  busy.value = true;
  const [, error] = await removeAttachment(item.id);
  busy.value = false;
  if (error) return fail(error);
  ok("پیوست برداشته شد.");
  await load();
  emit("changed");
}

function successor(item: any) {
  const next = items.value.find((i) => i.id === item.supersededById);
  return next ? next.title || next.fileName : "";
}

watch(() => [props.ownerType, props.ownerId], load, { immediate: true });
defineExpose({ load });
</script>

<template>
  <div v-if="ownerId && (panel || loading)" class="inv-card attachment-panel screen-only">
    <div class="inv-toolbar">
      <h2 class="inv-title" role="button" @click="open = !open">
        {{ title || "پیوست‌ها" }}
        <span class="inv-badge">{{ countText }}</span>
        <span v-if="panel?.locked" class="inv-badge warn" title="رکورد تایید شده است؛ پیوست فقط با ذکر دلیل ابطال یا با نسخه جدید جایگزین می‌شود">تایید شده: فقط ابطال / جایگزینی</span>
        <span class="inv-muted small">{{ open ? "▲" : "▼" }}</span>
      </h2>
    </div>

    <div v-if="open">
      <!-- the technical documents of the purchase request: links, never copies -->
      <div v-for="g in propagated" :key="g.sourceId" class="mb-3 attachment-inherited">
        <div class="small fw-bold mb-1">{{ g.sourceTitle }} <span class="inv-muted">(فقط مشاهده؛ ویرایش از صفحه درخواست خرید)</span></div>
        <table class="table table-sm table-bordered mb-0">
          <thead><tr><th>نوع</th><th>عنوان</th><th>بازنگری</th><th>محل</th><th>فایل</th></tr></thead>
          <tbody>
            <tr v-for="i in g.items" :key="i.id">
              <td>{{ i.kindTitle }}</td>
              <td>{{ i.title }}</td>
              <td class="inv-ltr">{{ i.revision }}</td>
              <td>{{ i.ownerTitle }} <span class="inv-ltr">{{ i.ownerNumber }}</span></td>
              <td><a href="#" @click.prevent="downloadFile(i.documentId, i.fileName)">{{ i.fileName }}</a> <span class="inv-muted small">{{ fileSizeText(i.sizeBytes) }}</span></td>
            </tr>
          </tbody>
        </table>
      </div>

      <p v-if="!current.length && !loading" class="inv-muted small mb-2">فایلی پیوست نشده است.</p>
      <div v-if="current.length" class="table-responsive">
        <table class="table table-sm table-bordered mb-2">
          <thead><tr><th>نوع</th><th>عنوان</th><th style="width: 5rem">بازنگری</th><th>فایل</th><th>بارگذاری</th><th style="width: 12rem"></th></tr></thead>
          <tbody>
            <tr v-for="i in current" :key="i.id + '-' + i.documentId">
              <td>{{ i.kindTitle }}</td>
              <td>{{ i.title }} <span v-if="i.legacy" class="inv-badge">قدیمی</span></td>
              <td class="inv-ltr">{{ i.revision }}</td>
              <td><a href="#" @click.prevent="downloadFile(i.documentId, i.fileName)">{{ i.fileName }}</a> <span class="inv-muted small">{{ fileSizeText(i.sizeBytes) }}</span></td>
              <td class="small">{{ i.uploadedByName }} <span class="inv-muted">{{ jalali(i.uploadedAt, true) }}</span></td>
              <td class="text-nowrap">
                <button v-if="canWrite && i.canSupersede" class="btn btn-sm btn-link p-0 me-2" @click="startSupersede(i)">نسخه جدید</button>
                <button v-if="canWrite && i.canVoid" class="btn btn-sm btn-link text-danger p-0 me-2" @click="voiding = { item: i, reason: '' }">ابطال</button>
                <button v-if="canWrite && i.canRemove" class="btn btn-sm btn-link text-danger p-0" @click="remove(i)">حذف</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div v-if="history.length" class="mb-2">
        <button class="btn btn-sm btn-link p-0" @click="showHistory = !showHistory">{{ showHistory ? "پنهان کردن" : "نمایش" }} سابقه (باطل‌شده و جایگزین‌شده): {{ history.length }}</button>
        <table v-if="showHistory" class="table table-sm table-bordered mt-1 mb-0 text-muted">
          <thead><tr><th>نوع</th><th>عنوان</th><th>بازنگری</th><th>فایل</th><th>وضعیت</th></tr></thead>
          <tbody>
            <tr v-for="i in history" :key="i.id">
              <td>{{ i.kindTitle }}</td>
              <td>{{ i.title }}</td>
              <td class="inv-ltr">{{ i.revision }}</td>
              <td><a href="#" @click.prevent="downloadFile(i.documentId, i.fileName)">{{ i.fileName }}</a></td>
              <td class="small">
                <span v-if="i.voided">باطل‌شده توسط {{ i.voidedByName }} ({{ jalali(i.voidedAt, true) }}): {{ i.voidReason }}</span>
                <span v-else-if="i.supersededById">جایگزین‌شده با «{{ successor(i) }}»</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div v-if="canWrite" class="row g-2 align-items-end">
        <div class="col-md-2">
          <label class="form-label small mb-0">نوع مدرک</label>
          <select v-model.number="form.kind" class="form-select form-select-sm">
            <option v-for="(t, k) in attachmentKinds" :key="k" :value="Number(k)">{{ t }}</option>
          </select>
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">عنوان</label>
          <input v-model="form.title" class="form-control form-control-sm" maxlength="300" />
        </div>
        <div class="col-md-1">
          <label class="form-label small mb-0">بازنگری</label>
          <input v-model="form.revision" class="form-control form-control-sm inv-ltr" maxlength="30" />
        </div>
        <div class="col-md-4">
          <label class="form-label small mb-0">فایل <span class="inv-muted">(Excel، Word، PDF، تصویر، DWG، DXF، STEP؛ تا {{ Math.round((panel?.maxBytes || 0) / 1024 / 1024) }} مگابایت)</span></label>
          <input ref="fileInput" :accept="accept" class="form-control form-control-sm" type="file" @change="pick" />
        </div>
        <div class="col-md-2">
          <button :disabled="busy || !form.file" class="btn btn-sm btn-primary w-100" @click="upload()">پیوست</button>
        </div>
      </div>
    </div>

    <div v-if="supersede" class="inv-modal-backdrop" @click.self="supersede = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">نسخه جدید برای «{{ supersede.item.title || supersede.item.fileName }}»</h2>
        <p class="inv-muted small">فایل قبلی در سابقه می‌ماند و روی رکوردهای بعدی فقط نسخه جدید دیده می‌شود.</p>
        <div class="row g-2">
          <div class="col-md-8"><label class="form-label small mb-0">عنوان</label><input v-model="supersede.title" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">بازنگری</label><input v-model="supersede.revision" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-12"><label class="form-label small mb-0">فایل</label><input :accept="accept" class="form-control form-control-sm" type="file" @change="pickSupersede" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="supersede = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="confirmSupersede">جایگزین شود</button>
        </div>
      </div>
    </div>

    <div v-if="voiding" class="inv-modal-backdrop" @click.self="voiding = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">ابطال پیوست «{{ voiding.item.title || voiding.item.fileName }}»</h2>
        <p class="inv-muted small">فایل حذف نمی‌شود؛ با دلیل، نام شما و زمان در سابقه می‌ماند.</p>
        <label class="form-label small mb-0">دلیل ابطال</label>
        <input v-model="voiding.reason" class="form-control form-control-sm" maxlength="1000" />
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="voiding = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-danger" @click="confirmVoid">ابطال</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../../views/inventory/inventory.scss"></style>
