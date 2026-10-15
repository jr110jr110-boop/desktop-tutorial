<script lang="ts" setup>
import type ListCustomGetParam from "@/types/ListCustomGetParam";
import type TableValue from "@/types/TableValue";
import type WorkOrder from "@/types/WorkOrder";
import { ref } from "vue";

import KsList from "@/components/common/KsList.vue";
import KsModal from "@/components/common/KsModal.vue";
import KsButton from "@/components/common/KsButton.vue";
import HammerIcon from "@/components/icons/HammerIcon.vue";
import CpuIcon from "@/components/icons/CpuIcon.vue";
import ShoppingCartIcon from "@/components/icons/ShoppingCartIcon.vue";
import ChartLineIcon from "@/components/icons/ChartLineIcon.vue";
import { Colors } from "@/assets/colors";

const props = defineProps({
  contractId: {
    type: String,
    required: true,
  },
});

const tableHeads: string[] = ["عنوان", "کد", "تعداد", "درصد وزنی", "وضعیت"];

const tableValues: TableValue[] = [
  {
    propName: "title",
    type: "text",
    clickable: true,
  },
  {
    propName: "code",
    type: "text",
  },
  {
    propName: "count",
    type: "si-number",
  },
  {
    propName: "weightPercent",
    type: "percent",
  },
  {
    propName: "condition",
    type: "status",
    status: [
      {
        title: "فعال",
        value: 0,
        color: "success",
      },
      {
        title: "موقف شده",
        value: 1,
        color: "dark",
      },
      {
        title: "انجام شده",
        value: 2,
        color: "success",
      },
      {
        title: "لغو شده",
        value: 3,
        color: "danger",
      },
    ],
  },
];

const getParams: ListCustomGetParam[] = [
  {
    propName: "ContractId",
    value: props.contractId,
  },
];

const previewModal = ref({
  visible: false,
  data: null as WorkOrder,
});

const showPreviewModal = (data: WorkOrder) => {
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
      :update-route-queries="{ contractId }"
      api-delete-url="api/v1/WorkOrders/delete-workOrder-collection"
      api-fetch-url="api/v1/WorkOrders"
      has-back-btn
      list-title="دستورات ساخت"
      search-terms="عنوان"
      update-route-name="WorkOrderUpdate"
      @onValueClick="(val) => showPreviewModal(val.data)">
      <template #listIcon>
        <HammerIcon :fill="Colors.black" class="ms-3" width="24" />
      </template>
      <template #custom-action-btn="{ rowData }">
        <li
          class="d-flex flex-row-reverse align-items-center gap-3"
          @click="
            $router.push({
              name: 'WorkOrderProgressStatistic',
              params: { workOrderId: rowData.id },
            })
          ">
          <ChartLineIcon :fill="Colors.black" width="16" />
          <span class="text-small">درصد پیشرفت</span>
        </li>
        <li
          class="d-flex flex-row-reverse align-items-center gap-3"
          @click="
            $router.push({
              name: 'ReportWorkOrderProgress',
              query: { work_order: rowData.id, contract: contractId },
            })
          ">
          <ChartLineIcon :fill="Colors.black" width="16" />
          <span class="text-small">گزارشات قرارداد</span>
        </li>
        <li
          class="d-flex flex-row-reverse align-items-center gap-3"
          @click="
            $router.push({
              name: 'BuyOrderList',
              params: { workOrderId: rowData.id },
              query: { contractId: contractId },
            })
          ">
          <ShoppingCartIcon :fill="Colors.black" width="16" />
          <span class="text-small">فرآیندهای تامین، مونتاژ و ساخت</span>
        </li>
        <li
          class="d-flex flex-row-reverse align-items-center gap-3"
          @click="
            $router.push({
              name: 'WorkOrderSubAssemblyList',
              params: { workOrderId: rowData.id },
            })
          ">
          <CpuIcon :fill="Colors.black" width="16" />
          <span class="text-small">زیر مجموعه</span>
        </li>
      </template>
    </KsList>

    <!--   PREVIEW MODAL   -->
    <KsModal v-model="previewModal.visible" blur>
      <div
        class="p-1 position-relative d-flex flex-column justify-content-between h-100 w-100 overflow-hidden">
        <div class="pe-3" style="overflow-y: auto; max-width: 380px">
          <div class="d-flex flex-column gap-2" dir="rtl">
            <span class="text-medium fw-bold">
              {{ previewModal.data.title }}
            </span>
            <hr class="d-none d-md-block" />
            <div class="d-flex flex-column gap-4" dir="ltr">
              <div class="d-flex flex-column align-items-end gap-4">
                <div
                  class="flex-1 d-flex flex-row-reverse justify-content-between gap-4 align-items-center">
                  <span class="text-small item-title">:کد</span>
                  <span class="text-small">{{ previewModal.data.code }}</span>
                </div>
                <div
                  class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                  <span class="text-small item-title">:تعداد</span>
                  <span class="text-small">{{ previewModal.data.count }}</span>
                </div>
                <div
                  class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                  <span class="text-small item-title">:وضعیت</span>
                  <span class="text-small">
                    {{ previewModal.data.conditionDescription }}
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
  width: 64px;
  font-weight: bold;
  text-align: right;
}
</style>
