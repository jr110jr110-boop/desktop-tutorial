<script setup lang="ts">
// Attendees of a meeting: a position, a user or an external guest. `present` is shown once the meeting is held (or while holding it).
import { ref } from "vue";
import PositionSelect from "./PositionSelect.vue";
import UserPick from "./UserPick.vue";

const props = defineProps({ list: { type: Array as () => any[], required: true }, readOnly: Boolean, showPresent: Boolean });
const kind = ref<"position" | "user" | "external">("position");
const draft = ref<any>({ positionId: null, userId: null, userName: "", externalName: "" });

function add() {
  const d = draft.value;
  if (kind.value === "position" && !d.positionId) return;
  if (kind.value === "user" && !d.userId) return;
  if (kind.value === "external" && !String(d.externalName).trim()) return;
  props.list.push({ id: null, positionId: kind.value === "position" ? d.positionId : null, positionTitle: "", userId: kind.value === "user" ? d.userId : null, userName: kind.value === "user" ? d.userName : "", externalName: kind.value === "external" ? d.externalName : "", present: false });
  draft.value = { positionId: null, userId: null, userName: "", externalName: "" };
}
const onUser = (u: any) => (draft.value.userName = u ? `${u.name || ""} ${u.lastName || ""}`.trim() : "");
const label = (a: any) => a.positionTitle || a.userName || a.externalName || (a.positionId ? "سمت #" + a.positionId : a.userId ? "کاربر #" + a.userId : "-");
const kindOf = (a: any) => (a.positionId ? "سمت" : a.userId ? "کاربر" : "مهمان بیرونی");
</script>

<template>
  <div>
    <div class="table-responsive">
      <table class="table table-sm">
        <thead><tr><th>نوع</th><th>سمت / نام</th><th v-if="showPresent">حضور</th><th>امضای صورتجلسه</th><th v-if="!readOnly"></th></tr></thead>
        <tbody>
          <tr v-if="!list.length"><td :colspan="showPresent ? 5 : 4" class="text-center inv-muted">حاضری ثبت نشده است.</td></tr>
          <tr v-for="(a, i) in list" :key="i">
            <td>{{ kindOf(a) }}</td><td>{{ label(a) }}<span v-if="a.positionId && a.userName" class="inv-muted small"> ({{ a.userName }})</span></td>
            <td v-if="showPresent"><input v-model="a.present" :disabled="readOnly" type="checkbox" class="form-check-input" /></td>
            <td class="inv-ltr">{{ a.signedAtText }}</td>
            <td v-if="!readOnly"><button class="btn btn-sm btn-outline-danger" @click="list.splice(i, 1)">حذف</button></td>
          </tr>
        </tbody>
      </table>
    </div>
    <div v-if="!readOnly" class="inv-filters">
      <select v-model="kind" class="form-select form-select-sm" style="max-width: 9rem"><option value="position">سمت</option><option value="user">کاربر</option><option value="external">مهمان بیرونی</option></select>
      <PositionSelect v-if="kind === 'position'" v-model="draft.positionId" />
      <UserPick v-else-if="kind === 'user'" v-model="draft.userId" @pick="onUser" />
      <input v-else v-model="draft.externalName" class="form-control form-control-sm" placeholder="نام و سازمان مهمان" />
      <button class="btn btn-sm btn-outline-primary" @click="add">افزودن حاضر</button>
    </div>
  </div>
</template>
