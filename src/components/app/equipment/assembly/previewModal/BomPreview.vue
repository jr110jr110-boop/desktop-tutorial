<script setup lang="ts">
import { useEquipmentAssembly } from "@/store";

const { previewModal } = useEquipmentAssembly.value;
</script>

<template>
  <div class="d-flex flex-column gap-2" dir="rtl">
    <span class="text-medium fw-bold">
      {{ previewModal.bom.title }}
    </span>
    <hr class="d-none d-md-block" />
    <div class="d-flex flex-column gap-4" dir="ltr">
      <div class="d-flex flex-column align-items-end gap-4">
        <div
          class="flex-1 d-flex flex-row-reverse justify-content-between gap-4 align-items-center">
          <span class="text-small item-title">:تعداد</span>
          <span class="text-small">
            {{ previewModal.bom.count }}
          </span>
        </div>
        <div class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
          <span class="text-small item-title">:واحد</span>
          <span class="text-small">
            {{ previewModal.bom.unit }}
          </span>
        </div>
        <div class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
          <span class="text-small item-title">:جنس</span>
          <span class="text-small">
            {{ previewModal.bom.material }}
          </span>
        </div>
        <div class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
          <span class="text-small item-title">:استاندارد</span>
          <span class="text-small">
            {{ previewModal.bom.standard }}
          </span>
        </div>
        <div class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
          <span class="text-small item-title">:قطر</span>
          <span class="text-small">
            {{ previewModal.bom.diagonal }}
          </span>
        </div>
        <div class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
          <span class="text-small item-title">:طول</span>
          <span class="text-small">
            {{ previewModal.bom.length }}
          </span>
        </div>
        <div class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
          <span class="text-small item-title">:عرض</span>
          <span class="text-small">
            {{ previewModal.bom.width }}
          </span>
        </div>
        <div class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
          <span class="text-small item-title">:ارتفاع</span>
          <span class="text-small">
            {{ previewModal.bom.height }}
          </span>
        </div>
        <div class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
          <span class="text-small item-title">:وزن ناخالص</span>
          <span class="text-small">
            {{ previewModal.bom.notWetWeight }}
          </span>
        </div>
        <div class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
          <span class="text-small item-title">:وزن خالص</span>
          <span class="text-small">
            {{ previewModal.bom.wetWeight }}
          </span>
        </div>
        <div class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
          <span class="text-small item-title">:جمع درصد وزنی</span>
          <span class="text-small">
            {{ parseFloat(previewModal.bom.totalWeightPercent).toFixed(2) }}
          </span>
        </div>
        <div class="flex-1 d-flex flex-column gap-1 align-items-end">
          <span class="text-small item-title">:توضیحات</span>
          <span class="text-small">
            {{ previewModal.bom.description }}
          </span>
        </div>
      </div>
    </div>
  </div>
</template>
