<script lang="ts" setup>
import type Bom from "@/types/Bom";
import type TableValue from "@/types/TableValue";
import type ListCustomGetParam from "@/types/ListCustomGetParam";
import { ref } from "vue";

import KsList from "@/components/common/KsList.vue";
import KsModal from "@/components/common/KsModal.vue";
import KsButton from "@/components/common/KsButton.vue";
import CodeBranchIcon from "@/components/icons/CodeBranchIcon.vue";
import BallPileIcon from "@/components/icons/BallPileIcon.vue";
import { Colors } from "@/assets/colors";

const props = defineProps({
  assemblyId: {
    type: String,
    required: true,
  },
});

const tableHeads: string[] = ["عنوان", "تعداد", "واحد", "جمع درصد وزنی"];

const tableValues: TableValue[] = [
  {
    propName: "title",
    type: "text",
    clickable: true,
  },
  {
    propName: "count",
    type: "si-number",
  },
  {
    propName: "unit",
    type: "text",
  },
  {
    propName: "totalWeightPercent",
    type: "text",
  },
];

const getParams: ListCustomGetParam[] = [
  {
    propName: "AssemblyId",
    value: props.assemblyId,
  },
];

const previewModal = ref({
  visible: false,
  data: null as Bom,
});

const showPreviewModal = (data: Bom) => {
  previewModal.value.data = data;
  previewModal.value.visible = true;
};
</script>

<template>
  <div>
    <KsList
      :custom-get-params="getParams"
      :table-heads="tableHeads"
      :table-values="tableValues"
      :update-route-queries="{ assemblyId }"
      api-delete-url="api/v1/Boms/delete-bom-collection"
      api-fetch-url="api/v1/Boms"
      has-back-btn
      list-title="لیست Bom"
      search-terms="عنوان"
      update-route-name="WorkOrderBomUpdate"
      @onValueClick="(val) => showPreviewModal(val.data)">
      <template #listIcon>
        <BallPileIcon :fill="Colors.black" class="ms-3" width="24" />
      </template>
      <template #custom-action-btn="{ rowData }">
        <li
          class="d-flex flex-row-reverse align-items-center gap-3"
          @click="
            $router.push({
              name: 'WorkOrderProcessList',
              params: { bomId: rowData.id },
            })
          ">
          <CodeBranchIcon :fill="Colors.black" width="16" />
          <span class="text-small">فرآیندهای تامین</span>
        </li>
      </template>
    </KsList>

    <!--   PREVIEW MODAL   -->
    <KsModal v-model="previewModal.visible" blur>
      <div
        class="p-1 position-relative d-flex flex-column justify-content-between h-100 w-100 overflow-hidden">
        <div
          class="ps-3"
          style="overflow-y: auto; max-width: 380px; height: 65vh">
          <div class="d-flex flex-column gap-2" dir="rtl">
            <span class="text-medium fw-bold">
              {{ previewModal.data.title }}
            </span>
            <hr class="d-none d-md-block" />
            <div class="d-flex flex-column gap-4" dir="ltr">
              <div class="d-flex flex-column align-items-end gap-4">
                <div
                  class="flex-1 d-flex flex-row-reverse justify-content-between gap-4 align-items-center">
                  <span class="text-small item-title">:تعداد</span>
                  <span class="text-small">{{ previewModal.data.count }}</span>
                </div>
                <div
                  class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                  <span class="text-small item-title">:واحد</span>
                  <span class="text-small">{{ previewModal.data.unit }}</span>
                </div>
                <div
                  class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                  <span class="text-small item-title">:جنس</span>
                  <span class="text-small">{{
                    previewModal.data.material
                  }}</span>
                </div>
                <div
                  class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                  <span class="text-small item-title">:استاندارد</span>
                  <span class="text-small">{{
                    previewModal.data.standard
                  }}</span>
                </div>
                <div
                  class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                  <span class="text-small item-title">:قطر</span>
                  <span class="text-small">{{
                    previewModal.data.diagonal
                  }}</span>
                </div>
                <div
                  class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                  <span class="text-small item-title">:طول</span>
                  <span class="text-small">{{ previewModal.data.length }}</span>
                </div>
                <div
                  class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                  <span class="text-small item-title">:عرض</span>
                  <span class="text-small">{{ previewModal.data.width }}</span>
                </div>
                <div
                  class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                  <span class="text-small item-title">:ارتفاع</span>
                  <span class="text-small">{{ previewModal.data.height }}</span>
                </div>
                <div
                  class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                  <span class="text-small item-title">:وزن ناخالص</span>
                  <span class="text-small">{{
                    previewModal.data.notWetWeight
                  }}</span>
                </div>
                <div
                  class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                  <span class="text-small item-title">:وزن خالص</span>
                  <span class="text-small">{{
                    previewModal.data.wetWeight
                  }}</span>
                </div>
                <div
                  class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                  <span class="text-small item-title">:جمع درصد وزنی</span>
                  <span class="text-small">
                    {{
                      parseFloat(previewModal.data.totalWeightPercent).toFixed(
                        2
                      )
                    }}
                  </span>
                </div>
                <div class="flex-1 d-flex flex-column gap-1 align-items-end">
                  <span class="text-small item-title">:توضیحات</span>
                  <span class="text-small">{{
                    previewModal.data.description
                  }}</span>
                </div>
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
  width: 180px;
  font-weight: bold;
  text-align: right;
}
</style>
