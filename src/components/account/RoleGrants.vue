<script lang="ts" setup>
// Sensitive roles that wait for a second PMO user (GET Accounts/role-grants, POST Accounts/role-grants/{id}/approve|reject).
// The API gives a sensitive role (finance manager, payroll, CEO, auditor, HR, secretariat) only after another PMO user approves it:
// not the one who asked for it and not the user who receives it. PMO only: the page that shows this panel is for the PMO.
import { computed, onMounted, ref } from "vue";
import { approveRoleGrant, getRoleGrants, rejectRoleGrant } from "@/api/UserAPIs";
import { failureText } from "@/composables/apiMessage";
import { jalali } from "@/composables/inventory";
import { currentUserId, roleTitles, SensitiveRoles } from "@/composables/roles";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";

import KsButton from "@/components/common/KsButton.vue";
import KsModal from "@/components/common/KsModal.vue";

/** A grant was approved or rejected: the roles of a user may have changed. */
const emit = defineEmits(["changed"]);

const grants = ref<any[]>([]);
const loading = ref(false);
const busy = ref(false);
const confirm = ref<{ visible: boolean; grant: any | null; approve: boolean }>({ visible: false, grant: null, approve: true });

/** The sensitive roles by their Persian titles, for the explanation. */
const sensitiveText = SensitiveRoles.map((role) => roleTitles[role] || role).join("، ");

const titleOf = (role: string) => roleTitles[String(role || "").toUpperCase()] || role;

/** What a request is: a sensitive role, the password reset of a PMO / sensitive-role holder, or taking the PMO role / deleting a PMO user (R1a2 Kind). */
function kindOf(grant: any): "role" | "reset" | "revoke" {
  const kind = String(grant?.kind || "role").toLowerCase();
  return kind === "reset" || kind === "revoke" ? kind : "role";
}

/** The title of the request type that the table shows instead of a bare role name. */
function requestTitle(grant: any): string {
  const kind = kindOf(grant);
  if (kind === "reset") return "بازنشانی رمز عبور";
  if (kind === "revoke") return String(grant.role || "").toUpperCase() === "USER_DELETE" ? "حذف کاربر مدیر (PMO)" : "برداشتن نقش مدیر (PMO)";
  return titleOf(grant.role);
}

/** The signed-in user asked for it: he may take it back (the API accepts the reject call of the requester as a withdrawal). */
const isMine = (grant: any) => currentUserId() !== null && grant.requestedByUserId === currentUserId();

/** The temporary password of an approved reset: shown once, here, and removed when the dialog closes. */
const issued = ref<{ visible: boolean; name: string; password: string }>({ visible: false, name: "", password: "" });

async function load() {
  loading.value = true;
  const [data, error] = await getRoleGrants();
  loading.value = false;
  if (error || !data?.success) {
    showErrorNotification(failureText(data, error, "فهرست درخواست‌های نقش خوانده نشد."));
    return;
  }
  grants.value = Array.isArray(data.data) ? data.data : [];
}

/** Why the signed-in PMO user cannot approve a grant (the API decides with CanApprove; this is the explanation). */
function whyNotApprovable(grant: any): string {
  if (grant.cannotApproveReason) return String(grant.cannotApproveReason);
  const me = currentUserId();
  if (me !== null && grant.requestedByUserId === me) return "شما این درخواست را ثبت کرده‌اید؛ مدیر (PMO) دیگری باید آن را تأیید کند.";
  if (me !== null && grant.userId === me) return "این نقش برای خود شماست؛ مدیر (PMO) دیگری باید آن را تأیید کند.";
  return "تأیید این درخواست برای شما ممکن نیست.";
}

function ask(grant: any, approve: boolean) {
  confirm.value = { visible: true, grant, approve };
}

const confirmText = computed(() => {
  const grant = confirm.value.grant;
  if (!grant) return "";
  const kind = kindOf(grant);
  if (!confirm.value.approve && isMine(grant)) return `درخواست «${requestTitle(grant)}» برای «${grant.displayName}» پس گرفته شود؟`;
  if (kind === "reset")
    return confirm.value.approve
      ? `رمز موقت برای «${grant.displayName}» ساخته شود؟ رمز فقط یک بار به شما نشان داده می‌شود و باید آن را به کاربر بدهید.`
      : `درخواست بازنشانی رمز «${grant.displayName}» رد شود؟`;
  if (kind === "revoke")
    return confirm.value.approve
      ? `«${requestTitle(grant)}» برای «${grant.displayName}» اجرا شود؟`
      : `درخواست «${requestTitle(grant)}» برای «${grant.displayName}» رد شود؟`;
  return confirm.value.approve
    ? `نقش «${titleOf(grant.role)}» به «${grant.displayName}» داده شود؟`
    : `درخواست نقش «${titleOf(grant.role)}» برای «${grant.displayName}» رد شود؟`;
});

async function decide() {
  const { grant, approve } = confirm.value;
  if (!grant || busy.value) return;
  busy.value = true;
  try {
    const [data, error] = approve ? await approveRoleGrant(grant.id) : await rejectRoleGrant(grant.id);
    confirm.value.visible = false;
    if (error || !data?.success) {
      showErrorNotification(failureText(data, error, approve ? "تأیید نقش انجام نشد." : "رد درخواست انجام نشد."));
    } else {
      if (approve && data.data?.temporaryPassword) {
        issued.value = { visible: true, name: grant.displayName, password: String(data.data.temporaryPassword) };
      }
      showSuccessNotification(data.message || (approve ? "درخواست تأیید شد." : isMine(grant) ? "درخواست پس گرفته شد." : "درخواست رد شد."));
      emit("changed");
    }
    // the request may have been decided or may have ended meanwhile, so the list is always read again
    await load();
  } finally {
    busy.value = false;
  }
}

defineExpose({ reload: load });

onMounted(load);
</script>

<template>
  <div class="role-grants background-color p-3 shadow-sm rounded-3 text-end mb-3" dir="rtl">
    <div class="d-flex justify-content-between align-items-center gap-2 flex-wrap">
      <span class="fw-bold text-large text-color">درخواست‌های در انتظار تأیید مدیر (PMO) دوم</span>
      <KsButton
        :loading="loading"
        color="dark"
        size="mini"
        theme="flat"
        title="بارگذاری دوباره"
        @click="load" />
    </div>

    <p class="text-small mt-2 mb-2">
      این‌ها درخواست‌هایی هستند که یک مدیر (PMO) دوم باید تأیید کند: نقش حساس، بازنشانی رمز یک مدیر یا دارنده نقش حساس، برداشتن نقش PMO و حذف کاربر مدیر.
      نقش‌های حساس ({{ sensitiveText }}) بلافاصله به کاربر داده نمی‌شوند. وقتی یکی از آن‌ها برای کاربری ثبت یا ویرایش می‌شود،
      تا زمانی که یک مدیر (PMO) دیگر آن را تأیید نکند اعمال نمی‌شود. کسی که درخواست را ثبت کرده و کاربری که نقش به او
      داده می‌شود نمی‌توانند خودشان آن را تأیید کنند. درخواستی که تا ۷ روز تأیید نشود باطل می‌شود.
    </p>

    <div v-if="grants.length" class="table-responsive">
      <table class="table table-sm align-middle mb-0">
        <thead>
          <tr>
            <th>کاربر</th>
            <th>درخواست</th>
            <th>درخواست‌دهنده</th>
            <th>تاریخ درخواست</th>
            <th>مهلت تأیید</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="grant in grants" :key="grant.id">
            <td>
              {{ grant.displayName }}
              <span class="text-xSmall d-block" dir="ltr">{{ grant.userName }}</span>
            </td>
            <td class="fw-bold">{{ requestTitle(grant) }}</td>
            <td>{{ grant.requestedByName }}</td>
            <td>{{ jalali(grant.requestedAt, true) }}</td>
            <td>{{ jalali(grant.expiresAt, true) }}</td>
            <td style="min-width: 210px">
              <div class="d-flex gap-2">
                <KsButton
                  :disabled="!grant.canApprove || busy"
                  color="success"
                  size="mini"
                  theme="flat"
                  title="تأیید"
                  @click="ask(grant, true)" />
                <KsButton
                  :disabled="busy"
                  color="danger"
                  size="mini"
                  theme="flat"
                  :title="isMine(grant) ? 'پس گرفتن' : 'رد'"
                  @click="ask(grant, false)" />
              </div>
              <span v-if="!grant.canApprove" class="text-xSmall d-block mt-1">
                {{ whyNotApprovable(grant) }}
              </span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <p v-else class="text-small mb-0">
      {{ loading ? "در حال بارگذاری…" : "درخواست نقش حساسی در انتظار تأیید نیست." }}
    </p>

    <KsModal v-model="confirm.visible" blur>
      <div class="d-flex flex-column gap-3 p-2" style="max-width: 440px">
        <span class="fw-bold text-medium">
          {{ confirm.approve ? "تأیید درخواست" : isMine(confirm.grant) ? "پس گرفتن درخواست" : "رد درخواست" }}
        </span>
        <span class="text-small">{{ confirmText }}</span>
        <div class="d-flex gap-2 justify-content-end">
          <KsButton
            :disabled="busy"
            color="dark"
            theme="flat"
            title="انصراف"
            @click="confirm.visible = false" />
          <KsButton
            :color="confirm.approve ? 'success' : 'danger'"
            :loading="busy"
            theme="flat"
            :title="confirm.approve ? 'تأیید' : isMine(confirm.grant) ? 'پس گرفتن' : 'رد درخواست'"
            @click="decide" />
        </div>
      </div>
    </KsModal>

    <KsModal v-model="issued.visible" blur>
      <div class="d-flex flex-column gap-3 p-2" style="max-width: 440px">
        <span class="fw-bold text-medium">رمز موقت «{{ issued.name }}»</span>
        <span class="text-small">این رمز فقط همین یک بار نشان داده می‌شود؛ آن را به کاربر بدهید. کاربر باید در اولین ورود رمز تازه بگذارد.</span>
        <code class="p-2 text-center" dir="ltr" style="font-size: 1.1rem; user-select: all">{{ issued.password }}</code>
        <div class="d-flex justify-content-end">
          <KsButton color="dark" theme="flat" title="بستن" @click="issued = { visible: false, name: '', password: '' }" />
        </div>
      </div>
    </KsModal>
  </div>
</template>
