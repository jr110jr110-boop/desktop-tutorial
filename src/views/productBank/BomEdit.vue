<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import AttachmentPanel from "@/components/common/AttachmentPanel.vue";
import { AttachmentOwner } from "@/composables/attachments";
import { deleteBomDraft, diffBoms, getBom, getBoms, getProducts, newBomRevision, obsoleteBom, releaseBom, returnBom, rowsOf, saveBom, signBom, submitBom, whereUsed } from "@/api/ProductBankAPIs";
import { fail, jalali, ok, printPage } from "@/composables/inventory";
import { bomStatusClass, bomStatuses, makeOrBuy, makeOrBuyShort, num, revisionText, signatureRoles, signatureTitles } from "@/composables/productBank";

// BOM editor and viewer: tree with group headings, the bar of revisions, the five signatures, the difference between two revisions and where a part is used.
// A draft is edited by its preparer only; a released revision is read-only (a new revision copies it).
const route = useRoute();
const router = useRouter();
const bom = ref<any>(null);
const isNew = computed(() => route.params.id === "new");
const header = ref<any>({ productId: null, assemblyPartNumber: "", title: "", platform: "", issueFor: "", documentDate: "", changeReason: "" });
const rows = ref<any[]>([]); // flat rows: { isHeading, level, itemNumber, partNumber, partName, qty, ... }
const products = ref<any[]>([]);
const revisions = ref<any[]>([]);
const busy = ref(false);
const note = ref("");
const diffWith = ref<number | null>(null);
const diff = ref<any>(null);
const wuPart = ref("");
const wu = ref<any>(null);

const editable = computed(() => isNew.value || !!bom.value?.canEdit);
const status = computed(() => (bom.value ? bom.value.status : 0));
const next = computed(() => bom.value?.nextSignature);

function toRows(dto: any) {
  const lines = [...(dto.lines || [])].sort((a, b) => a.sortOrder - b.sortOrder);
  return lines.map((l: any) => ({ ...l, level: l.isHeading ? (l.parentLineId ? 1 : 0) : 2, isAlternative: !!l.alternativeOfLineId }));
}

async function load() {
  if (isNew.value) {
    bom.value = null;
    rows.value = [{ isHeading: true, level: 0, partName: "" }];
    return;
  }
  const [data, error] = await getBom(Number(route.params.id));
  if (error) return fail(error);
  bom.value = data;
  header.value = { productId: data.productId, assemblyPartNumber: data.assemblyPartNumber, title: data.title, platform: data.platform, issueFor: data.issueFor, documentDate: data.documentDate?.slice(0, 10) || "", changeReason: data.changeReason || "" };
  rows.value = toRows(data);
  const [list] = await getBoms({ Contains: data.assemblyPartNumber, PageSize: 50 });
  revisions.value = rowsOf(list).filter((r: any) => r.assemblyPartNumber === data.assemblyPartNumber).sort((a: any, b: any) => b.revision - a.revision);
  diff.value = null;
}

onMounted(async () => {
  const [p] = await getProducts();
  products.value = p || [];
  load();
});
watch(() => route.params.id, load);

function addHeading(level: number) {
  rows.value.push({ isHeading: true, level, partName: "" });
}
function addLine() {
  const n = Math.max(0, ...rows.value.filter((r) => !r.isHeading).map((r) => r.itemNumber || 0)) + 1;
  rows.value.push({ isHeading: false, level: 2, itemNumber: n, partNumber: "", partName: "", qty: 1, unit: "عدد", makeOrBuy: 1 });
}
function move(i: number, d: number) {
  const j = i + d;
  if (j < 0 || j >= rows.value.length) return;
  const t = rows.value[i];
  rows.value[i] = rows.value[j];
  rows.value[j] = t;
}
function removeRow(i: number) {
  rows.value.splice(i, 1);
}

/** The flat rows become the API lines: a heading of level 1 belongs to the latest heading of level 0, a part line to the latest heading. */
function toSave() {
  const lines: any[] = [];
  let lastHeading = -1;
  let lastTop = -1;
  const mainOf = new Map<number, number>(); // item number -> index of its main line; an alternative points at it (one line, no double counting)
  for (const r of rows.value) {
    if (r.isHeading) {
      lines.push({ isHeading: true, partName: r.partName, parentIndex: r.level === 1 && lastTop >= 0 ? lastTop : null });
      lastHeading = lines.length - 1;
      if (r.level === 0) lastTop = lastHeading;
    } else {
      const alt = r.isAlternative && r.itemNumber && mainOf.has(r.itemNumber) ? mainOf.get(r.itemNumber)! : null;
      if (!r.isAlternative && r.itemNumber) mainOf.set(r.itemNumber, lines.length);
      lines.push({
        alternativeOfIndex: alt,
        itemNumber: r.itemNumber || null, partNumber: r.partNumber, partName: r.partName, qty: Number(r.qty) || 0, unit: r.unit || "عدد", standard: r.standard, material: r.material,
        rawDiameter: r.rawDiameter ?? null, rawLength: r.rawLength ?? null, rawWidth: r.rawWidth ?? null, rawThickness: r.rawThickness ?? null, netWeight: r.netWeight ?? null, grossWeight: r.grossWeight ?? null,
        scrapFactor: r.scrapFactor ?? null, makeOrBuy: Number(r.makeOrBuy) || 2, description: r.description, childBomId: r.childBomId ?? null, parentIndex: lastHeading >= 0 ? lastHeading : null,
      });
    }
  }
  return lines;
}

async function run(fn: () => Promise<any>, message: string, after?: (data: any) => void) {
  busy.value = true;
  const [data, error] = await fn();
  busy.value = false;
  if (error) return fail(error);
  ok(message);
  note.value = "";
  if (after) after(data);
  else load();
}

const save = () =>
  run(() => saveBom({ id: bom.value?.id ?? null, ...header.value, documentDate: header.value.documentDate || null, lines: toSave() }), "BOM ذخیره شد.", (d) => {
    if (isNew.value) router.replace({ name: "ProductBankBomEdit", params: { id: d.id } });
    else load();
  });
const submit = () => run(() => submitBom(bom.value.id), "BOM برای بررسی ارسال شد.");
const sendBack = () => run(() => returnBom(bom.value.id, note.value), "BOM به پیش‌نویس برگشت.");
const sign = () => run(() => signBom(bom.value.id, next.value, note.value), "امضا ثبت شد.");
const release = () => run(() => releaseBom(bom.value.id), "نسخه منتشر شد.");
const revise = () => run(() => newBomRevision(bom.value.id, note.value), "نسخهٔ جدید ساخته شد.", (d) => router.push({ name: "ProductBankBomEdit", params: { id: d.id } }));
const obsolete = () => run(() => obsoleteBom(bom.value.id, note.value), "نسخه باطل شد.");
const removeDraft = () => run(() => deleteBomDraft(bom.value.id), "پیش‌نویس حذف شد.", () => router.push({ name: "ProductBankBoms" }));

async function showDiff() {
  if (!diffWith.value) return (diff.value = null);
  const [data, error] = await diffBoms(diffWith.value, bom.value.id);
  if (error) return fail(error);
  diff.value = data;
}
async function findWhereUsed() {
  const [data, error] = await whereUsed(wuPart.value.trim());
  if (error) return fail(error);
  wu.value = data;
}
const fieldName: Record<string, string> = { Qty: "تعداد", Material: "جنس", Standard: "استاندارد", PartName: "نام", MakeOrBuy: "ساخت/خرید", NetWeight: "وزن خالص", GrossWeight: "وزن ناخالص", Unit: "واحد", Description: "توضیح" };
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader :title="bom ? 'BOM ' + bom.code + ' — نسخه ' + revisionText(bom.revision) : 'BOM جدید'" :subtitle="bom ? bomStatuses[bom.status] : 'پیش‌نویس'" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">
          {{ bom ? bom.title || bom.code : "BOM جدید" }}
          <span v-if="bom" class="inv-badge" :class="bomStatusClass[bom.status]">{{ bomStatuses[bom.status] }}</span>
          <span v-if="bom?.importedSigned" class="inv-badge ok" title="این نسخه با امضای کاغذی در شرکت تایید شده است">امضای کاغذی</span>
        </h1>
        <div class="d-flex gap-2 d-print-none flex-wrap">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <router-link :to="{ name: 'ProductBankBoms' }" class="btn btn-sm btn-outline-secondary">فهرست</router-link>
        </div>
      </div>

      <!-- bar of revisions -->
      <div v-if="revisions.length" class="d-flex gap-2 align-items-center mb-3 flex-wrap d-print-none">
        <span class="small inv-muted">نسخه‌ها:</span>
        <router-link v-for="r in revisions" :key="r.id" :to="{ name: 'ProductBankBomEdit', params: { id: r.id } }" class="btn btn-sm py-0" :class="r.id === bom?.id ? 'btn-primary' : 'btn-outline-secondary'">
          {{ revisionText(r.revision) }} <small>({{ bomStatuses[r.status] }})</small>
        </router-link>
        <span class="small ms-3">مقایسه با:</span>
        <select v-model="diffWith" class="form-select form-select-sm" style="max-width: 160px" @change="showDiff">
          <option :value="null">—</option>
          <option v-for="r in revisions.filter((x) => x.id !== bom?.id)" :key="r.id" :value="r.id">نسخه {{ revisionText(r.revision) }}</option>
        </select>
      </div>

      <!-- signature block -->
      <div v-if="bom" class="row g-2 mb-3">
        <div v-for="s in bom.signatures" :key="s.step" class="col">
          <div class="border rounded p-2 h-100 text-center" :class="s.done ? 'border-success' : next === s.step ? 'border-warning' : ''">
            <div class="small fw-bold">{{ signatureTitles[s.step] }}</div>
            <div class="small inv-muted">{{ signatureRoles[s.step] || "مهندسی یا PMO" }}</div>
            <div v-if="s.done" class="small text-success">{{ bom.importedSigned ? "امضای کاغذی" : (s.userName || "—") }}<br />{{ jalali(s.at, true) }}</div>
            <div v-else-if="bom.importedSigned" class="small inv-muted">روی کاغذ</div>
            <div v-else-if="next === s.step" class="small text-warning">نوبت این امضا</div>
            <div v-else class="small inv-muted">—</div>
          </div>
        </div>
        <div v-if="bom.releasedAt" class="col">
          <div class="border rounded p-2 h-100 text-center border-success">
            <div class="small fw-bold">انتشار</div>
            <div class="small text-success">{{ jalali(bom.releasedAt) }}</div>
          </div>
        </div>
      </div>
      <p v-if="bom?.paperSignatureNote" class="small inv-muted">{{ bom.paperSignatureNote }}</p>
      <p v-if="bom?.changeReason && bom.revision > 0" class="small">دلیل تغییر: {{ bom.changeReason }}</p>

      <!-- header -->
      <div class="row g-2 mb-3">
        <div class="col-3">
          <label class="form-label small mb-0">محصول</label>
          <select v-model="header.productId" class="form-select form-select-sm" :disabled="!editable || !isNew">
            <option :value="null">—</option>
            <option v-for="p in products" :key="p.id" :value="p.id">{{ p.code }} {{ p.title }}</option>
          </select>
        </div>
        <div class="col-3">
          <label class="form-label small mb-0">شماره قطعه مجموعه (والد) *</label>
          <input v-model="header.assemblyPartNumber" class="form-control form-control-sm inv-ltr" :disabled="!editable || !isNew" placeholder="16.11.00.000.2" />
        </div>
        <div class="col-4">
          <label class="form-label small mb-0">عنوان</label>
          <input v-model="header.title" class="form-control form-control-sm" :disabled="!editable" />
        </div>
        <div class="col-2">
          <label class="form-label small mb-0">پلتفرم</label>
          <input v-model="header.platform" class="form-control form-control-sm inv-ltr" :disabled="!editable" />
        </div>
        <div v-if="bom && bom.revision > 0" class="col-12">
          <label class="form-label small mb-0">دلیل تغییر نسخه</label>
          <input v-model="header.changeReason" class="form-control form-control-sm" :disabled="!editable" />
        </div>
      </div>

      <!-- actions -->
      <div class="d-flex gap-2 flex-wrap mb-3 d-print-none align-items-center">
        <button v-if="editable" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        <button v-if="bom?.canSubmit" :disabled="busy" class="btn btn-sm btn-success" @click="submit">ارسال برای بررسی</button>
        <button v-if="bom?.canSign" :disabled="busy" class="btn btn-sm btn-success" @click="sign">امضا: {{ signatureTitles[next] }}</button>
        <button v-if="bom?.canRelease" :disabled="busy" class="btn btn-sm btn-success" @click="release">انتشار نسخه</button>
        <button v-if="bom?.canNewRevision" :disabled="busy || !note.trim()" class="btn btn-sm btn-outline-primary" title="دلیل را در کادر یادداشت بنویسید" @click="revise">نسخهٔ جدید</button>
        <button v-if="status === 1 && bom" :disabled="busy || !note.trim()" class="btn btn-sm btn-outline-warning" @click="sendBack">برگشت به پیش‌نویس</button>
        <button v-if="bom?.canObsolete" :disabled="busy || !note.trim()" class="btn btn-sm btn-outline-danger" @click="obsolete">باطل کردن</button>
        <button v-if="bom?.canEdit && status === 0" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="removeDraft">حذف پیش‌نویس</button>
        <input v-if="bom" v-model="note" class="form-control form-control-sm" style="max-width: 320px" placeholder="یادداشت / دلیل (برای نسخهٔ جدید، برگشت و ابطال لازم است)" />
      </div>

      <!-- lines -->
      <table class="table table-sm table-hover">
        <thead>
          <tr>
            <th style="width: 40px">#</th><th>شماره قطعه</th><th>نام قطعه</th><th>تعداد</th><th>واحد</th><th>استاندارد</th><th>جنس</th>
            <th>Ø × L × W × T خام</th><th>وزن خالص / ناخالص</th><th>ضایعات</th><th>ساخت/خرید</th><th>توضیح</th>
            <th v-if="editable" class="d-print-none"></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(r, i) in rows" :key="i" :class="{ 'table-light fw-bold': r.isHeading }">
            <template v-if="r.isHeading">
              <td></td>
              <td :colspan="editable ? 11 : 11" :style="{ paddingRight: r.level * 18 + 'px' }">
                <input v-if="editable" v-model="r.partName" class="form-control form-control-sm" placeholder="عنوان گروه (مثلا Casing)" />
                <span v-else>{{ r.level ? "▸ " : "" }}{{ r.partName }}</span>
              </td>
            </template>
            <template v-else>
              <td><input v-if="editable" v-model.number="r.itemNumber" type="number" class="form-control form-control-sm px-1" /><span v-else>{{ r.itemNumber }}</span></td>
              <td class="inv-ltr"><input v-if="editable" v-model="r.partNumber" class="form-control form-control-sm" /><span v-else>{{ r.partNumber }}</span>
                <label v-if="editable" class="small d-block"><input v-model="r.isAlternative" type="checkbox" /> جایگزین ردیف هم‌شماره</label><span v-else-if="r.isAlternative" class="inv-badge warn">جایگزین</span></td>
              <td><input v-if="editable" v-model="r.partName" class="form-control form-control-sm" /><span v-else>{{ r.partName }}</span></td>
              <td><input v-if="editable" v-model.number="r.qty" type="number" step="any" class="form-control form-control-sm px-1" style="width: 70px" /><span v-else>{{ num(r.qty) }}</span></td>
              <td><input v-if="editable" v-model="r.unit" class="form-control form-control-sm px-1" style="width: 60px" /><span v-else>{{ r.unit }}</span></td>
              <td class="inv-ltr"><input v-if="editable" v-model="r.standard" class="form-control form-control-sm" /><span v-else>{{ r.standard }}</span></td>
              <td class="inv-ltr"><input v-if="editable" v-model="r.material" class="form-control form-control-sm" /><span v-else>{{ r.material }}</span></td>
              <td class="inv-ltr">
                <span v-if="!editable">{{ [num(r.rawDiameter), num(r.rawLength), num(r.rawWidth), num(r.rawThickness)].filter((x) => x).join(" × ") }}</span>
                <span v-else class="d-flex gap-1">
                  <input v-model.number="r.rawDiameter" type="number" step="any" class="form-control form-control-sm px-1" style="width: 56px" placeholder="Ø" />
                  <input v-model.number="r.rawLength" type="number" step="any" class="form-control form-control-sm px-1" style="width: 56px" placeholder="L" />
                  <input v-model.number="r.rawWidth" type="number" step="any" class="form-control form-control-sm px-1" style="width: 56px" placeholder="W" />
                  <input v-model.number="r.rawThickness" type="number" step="any" class="form-control form-control-sm px-1" style="width: 56px" placeholder="T" />
                </span>
              </td>
              <td class="inv-ltr">
                <span v-if="!editable">{{ num(r.netWeight) }}{{ r.grossWeight !== null && r.grossWeight !== undefined ? " / " + num(r.grossWeight) : "" }}</span>
                <span v-else class="d-flex gap-1">
                  <input v-model.number="r.netWeight" type="number" step="any" class="form-control form-control-sm px-1" style="width: 64px" />
                  <input v-model.number="r.grossWeight" type="number" step="any" class="form-control form-control-sm px-1" style="width: 64px" />
                </span>
              </td>
              <td class="inv-ltr">{{ r.scrapFactorEffective !== null && r.scrapFactorEffective !== undefined ? Math.round(r.scrapFactorEffective * 1000) / 10 + "٪" : "" }}</td>
              <td>
                <select v-if="editable" v-model="r.makeOrBuy" class="form-select form-select-sm px-1"><option v-for="(t, k) in makeOrBuy" :key="k" :value="Number(k)">{{ makeOrBuyShort[Number(k)] }}</option></select>
                <span v-else :title="makeOrBuy[r.makeOrBuy]">{{ makeOrBuyShort[r.makeOrBuy] }}</span>
              </td>
              <td><input v-if="editable" v-model="r.description" class="form-control form-control-sm" /><span v-else class="small">{{ r.description }}</span></td>
            </template>
            <td v-if="editable" class="d-print-none text-nowrap">
              <button class="btn btn-sm py-0" @click="move(i, -1)">↑</button>
              <button class="btn btn-sm py-0" @click="move(i, 1)">↓</button>
              <button class="btn btn-sm py-0 text-danger" @click="removeRow(i)">×</button>
            </td>
          </tr>
        </tbody>
      </table>
      <div v-if="editable" class="d-flex gap-2 d-print-none">
        <button class="btn btn-sm btn-outline-secondary" @click="addHeading(0)">+ عنوان گروه</button>
        <button class="btn btn-sm btn-outline-secondary" @click="addHeading(1)">+ زیرعنوان</button>
        <button class="btn btn-sm btn-outline-secondary" @click="addLine">+ قطعه</button>
      </div>

      <!-- diff -->
      <div v-if="diff" class="mt-4">
        <h2 class="inv-title">تفاوت نسخه {{ diff.fromCode }} با این نسخه</h2>
        <p class="small inv-muted">{{ diff.added.length }} افزوده · {{ diff.removed.length }} حذف‌شده · {{ diff.changed.length }} تغییریافته · {{ diff.unchanged }} بدون تغییر</p>
        <table class="table table-sm">
          <tbody>
            <tr v-for="d in diff.added" :key="'a' + d.partNumber" class="table-success"><td>افزوده</td><td class="inv-ltr">{{ d.partNumber }}</td><td>{{ d.partName }}</td><td>تعداد {{ num(d.qty) }}</td></tr>
            <tr v-for="d in diff.removed" :key="'r' + d.partNumber" class="table-danger"><td>حذف‌شده</td><td class="inv-ltr">{{ d.partNumber }}</td><td>{{ d.partName }}</td><td>تعداد {{ num(d.qty) }}</td></tr>
            <tr v-for="d in diff.changed" :key="'c' + d.partNumber" class="table-warning">
              <td>تغییر</td><td class="inv-ltr">{{ d.partNumber }}</td><td>{{ d.partName }}</td>
              <td><div v-for="f in d.fields" :key="f.field">{{ fieldName[f.field] || f.field }}: <span class="inv-ltr">{{ f.old || "—" }}</span> ← <span class="inv-ltr fw-bold">{{ f.new || "—" }}</span></div></td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- where used -->
      <div class="mt-4 d-print-none">
        <h2 class="inv-title">این قطعه کجا استفاده شده؟</h2>
        <div class="d-flex gap-2 mb-2">
          <input v-model="wuPart" class="form-control form-control-sm inv-ltr" style="max-width: 260px" placeholder="شماره قطعه" @keyup.enter="findWhereUsed" />
          <button class="btn btn-sm btn-outline-primary" :disabled="!wuPart.trim()" @click="findWhereUsed">جستجو</button>
        </div>
        <table v-if="wu" class="table table-sm">
          <thead><tr><th>BOM</th><th>نسخه</th><th>وضعیت</th><th>ردیف</th><th>تعداد</th><th>در آخرین نسخهٔ منتشرشده</th></tr></thead>
          <tbody>
            <tr v-if="!wu.rows.length"><td colspan="6" class="text-center inv-muted">در هیچ BOM استفاده نشده است.</td></tr>
            <tr v-for="r in wu.rows" :key="r.bomId + '-' + r.itemNumber">
              <td><router-link :to="{ name: 'ProductBankBomEdit', params: { id: r.bomId } }" class="inv-ltr">{{ r.bomCode }}</router-link></td>
              <td class="inv-ltr">{{ revisionText(r.revision) }}</td><td>{{ bomStatuses[r.status] }}</td><td>{{ r.itemNumber }}</td><td>{{ num(r.qty) }}</td><td>{{ r.inLatestRelease ? "بله" : "-" }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
    <AttachmentPanel v-if="bom?.id" :owner-type="AttachmentOwner.PbBomHeader" :owner-id="bom.id" title="مدارک فنی BOM (نقشه‌ها و مشخصات) — به درخواست‌های خرید خودکار پیوست می‌شود" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
