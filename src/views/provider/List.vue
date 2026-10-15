<script lang="ts" setup>
import type TableValue from "@/types/TableValue";
import type Provider from "@/types/Provider";
import { reactive } from "vue";

import KsList from "@/components/common/KsList.vue";
import KsButton from "@/components/common/KsButton.vue";
import KsModal from "@/components/common/KsModal.vue";
import HandShakeIcon from "@/components/icons/HandShakeIcon.vue";
import { Colors } from "@/assets/colors";

const tableHeads: string[] = [
  "نام",
  "گروه",
  "امتیاز ارزیابی",
  "ارزش",
  "توضیحات",
];

const tableValues: TableValue[] = [
  {
    propName: "name",
    type: "text",
    clickable: true,
  },
  {
    propName: "categoryName",
    type: "text",
  },
  {
    propName: "evaluationScore",
    type: "text",
  },
  {
    propName: "listName",
    type: "text",
  },
  {
    propName: "description",
    type: "text",
  },
];

const previewModal = reactive({
  visible: false,
  data: null as Provider,
});

const onPreviewModalRequest = (data: Provider) => {
  previewModal.data = data;
  previewModal.visible = true;
};
</script>

<template>
  <div>
    <KsList
      :table-heads="tableHeads"
      :table-values="tableValues"
      api-delete-url="api/v1/Provider/delete-provider-collection"
      api-fetch-url="api/v1/Provider"
      list-title="تامین کنندگان"
      search-terms="عنوان"
      update-route-name="ProviderUpdate"
      @onValueClick="(val) => onPreviewModalRequest(val.data)">
      <template #listIcon>
        <HandShakeIcon :fill="Colors.black" class="ms-3" width="24" />
      </template>
    </KsList>

    <!--   PREVIEW MODAL   -->
    <KsModal v-model="previewModal.visible" blur>
      <div
        class="p-1 position-relative d-flex flex-column justify-content-between h-100 w-100 overflow-hidden">
        <div class="pe-3" style="overflow-y: auto; height: 65vh">
          <div class="d-flex flex-column gap-2">
            <span class="text-medium fw-bold text-end">
              اطلاعات تماس
              {{ previewModal.data.name }}
            </span>
            <hr class="d-none d-md-block" />
            <div class="d-flex flex-column gap-1">
              <div
                v-for="(providerContact, index) in previewModal.data
                  .providerContacts"
                :key="index"
                class="d-flex flex-column align-items-end gap-2">
                <div
                  class="flex-1 d-flex flex-row-reverse justify-content-between gap-4 align-items-center">
                  <span class="text-small item-title"
                    >:{{ providerContact.typeDescription }}</span
                  >
                  <span class="text-small">
                    {{ providerContact.data }}
                  </span>
                </div>
                <div
                  class="flex-1 d-flex flex-row-reverse justify-content-between gap-4 align-items-center">
                  <span class="text-small item-title">:نام</span>
                  <span class="text-small" dir="rtl">
                    {{ providerContact.name }}
                  </span>
                </div>
                <span class="flex-1 text-small">:توضیحات</span>
                <span class="text-small" dir="rtl">
                  {{ providerContact.description }}
                </span>
                <hr
                  v-if="index !== previewModal.data.providerContacts.length - 1"
                  class="w-100" />
              </div>
            </div>
          </div>
        </div>

        <div class="d-flex justify-content-end gap-2 mt-4">
          <KsButton
            color="dark"
            theme="flat"
            title="بستن"
            @click="previewModal.visible = false" />
        </div>
      </div>
    </KsModal>
  </div>
</template>

<style lang="scss" scoped>
.item-title {
  width: 92px;
  font-weight: bold;
  text-align: end;
}
</style>
