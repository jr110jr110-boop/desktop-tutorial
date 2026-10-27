<script lang="ts" setup>
import type TableValue from "@/types/TableValue";
import { ref } from "vue";
import { convertToJalaliDate } from "@/composables/ConvertToJalaliDate";

import { roleTitles } from "@/composables/roles";

import KsList from "@/components/common/KsList.vue";
import RoleGrants from "@/components/account/RoleGrants.vue";
import KsModal from "@/components/common/KsModal.vue";
import KsButton from "@/components/common/KsButton.vue";
import UsersIcon from "@/components/icons/UsersIcon.vue";
import { Colors } from "@/assets/colors";

const tableHeads: string[] = ["نام", "نام خانوادگی", "ایمیل", "موبایل"];

const tableValues: TableValue[] = [
  {
    propName: "name",
    type: "text",
    clickable: true,
  },
  {
    propName: "lastName",
    type: "text",
  },
  {
    propName: "email",
    type: "text",
  },
  {
    propName: "phoneNumber",
    type: "text",
  },
];

const userDataModal = ref({
  visible: false,
  data: null,
});

// The list is read again after a sensitive role was approved or rejected (the roles of that user changed).
const listKey = ref(0);

/** The roles of a user with their Persian titles; a user can have no role (for example while a sensitive role waits for approval). */
const rolesOf = (user: any): { id: number | string; title: string }[] =>
  (user?.roles || []).map((role: any) => ({
    id: role.id,
    title: roleTitles[String(role.name || "").toUpperCase()] || role.description || role.name,
  }));

const showUserData = (data) => {
  userDataModal.value.data = data;
  userDataModal.value.visible = true;
};

const removeUserDataModal = () => {
  userDataModal.value.visible = false;
  userDataModal.value.data = null;
};
</script>

<template>
  <div>
    <RoleGrants @changed="listKey++" />
    <KsList
      :key="listKey"
      :table-heads="tableHeads"
      :table-values="tableValues"
      api-delete-url="api/v1/Accounts/delete-user-collection"
      api-fetch-url="api/v1/Accounts/get-all-users"
      list-title="کاربران"
      search-terms="عنوان"
      update-route-name="UserUpdate"
      @onValueClick="(val) => showUserData(val.data)">
      <template #listIcon>
        <UsersIcon :fill="Colors.black" class="ms-3" width="24" />
      </template>
    </KsList>
    <KsModal v-model="userDataModal.visible" blur>
      <div class="h-100 overflow-hidden d-flex flex-column gap-2">
        <div class="p-3 flex-fill" style="height: 67vh; overflow-y: auto">
          <div class="d-flex flex-column gap-2" dir="rtl">
            <span class="text-medium fw-bold">
              {{ userDataModal.data.name }} {{ userDataModal.data.lastName }}
            </span>
            <hr class="d-none d-md-block" />
            <div class="d-flex flex-column gap-3">
              <div class="d-flex gap-2 flex-1 align-items-md-center">
                <span class="text-small fw-light row-title"> کد ملی: </span>
                <span class="text-small">
                  {{ userDataModal.data.nationalCode }}
                </span>
              </div>
              <div class="d-flex gap-2 flex-1 align-items-sm-center">
                <span class="text-small fw-light row-title"> تاریخ تولد: </span>
                <span class="text-small">
                  {{ convertToJalaliDate(userDataModal.data.birthDate) }}
                </span>
              </div>
              <div class="d-flex gap-2 flex-1 align-items-md-center">
                <span class="text-small fw-light row-title"> ایمیل: </span>
                <span class="text-small">
                  {{ userDataModal.data.email }}
                </span>
              </div>
              <div class="d-flex gap-2 flex-1 align-items-sm-center">
                <span class="text-small fw-light row-title"> موبایل: </span>
                <span class="text-small">
                  {{ userDataModal.data.phoneNumber }}
                </span>
              </div>
              <div class="d-flex gap-2 flex-1 align-items-md-center">
                <span class="text-small fw-light row-title"> نام کاربری: </span>
                <span class="text-small">
                  {{ userDataModal.data.userName }}
                </span>
              </div>
              <div class="d-flex gap-2 flex-1 align-items-sm-center">
                <span class="text-small fw-light row-title"> سمت: </span>
                <div class="d-flex flex-wrap gap-2">
                  <KsButton
                    v-for="role in rolesOf(userDataModal.data)"
                    :key="role.id"
                    :title="role.title"
                    color="primary"
                    custom-padding="5px 12px"
                    theme="flat" />
                  <span v-if="!rolesOf(userDataModal.data).length" class="text-small">
                    بدون نقش
                  </span>
                </div>
              </div>
              <div class="d-flex gap-2 flex-1 align-items-center">
                <div class="d-flex gap-2 flex-1 align-items-md-center">
                  <span class="text-small fw-light row-title">
                    تاریخ ثبت:
                  </span>
                  <span class="text-small">
                    {{
                      convertToJalaliDate(userDataModal.data.createdDateTime)
                    }}
                  </span>
                </div>
              </div>
              <div class="d-flex gap-2 flex-1 align-items-center">
                <span class="text-small fw-light row-title">
                  کد تایید موبایل:
                </span>
                <span class="text-small">
                  {{ userDataModal.data.mobileConfirmCode }}
                </span>
              </div>
            </div>
          </div>
        </div>
        <div class="d-flex justify-content-end">
          <KsButton
            color="dark"
            theme="flat"
            title="بستن"
            @click="removeUserDataModal">
          </KsButton>
        </div>
      </div>
    </KsModal>
  </div>
</template>

<style lang="scss" scoped>
.row-title {
  width: 120px;
}
</style>
