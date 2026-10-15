<template>
  <div
    class="d-flex flex-column align-items-center shadow-sm rounded-3 py-5 background-color">
    <NotFoundVector class="mt-3" style="width: 350px" />
    <span class="fw-bold text-medium color-primary mt-4"> صفحه یافت نشد </span>
    <span
      class="text-xSmall text-center color-primary mt-2"
      style="width: 250px">
      این صفحه وجود ندارد، در صورت نیاز میتوانید با پشتیبانی تماس بگیرید
    </span>

    <KsButton
      class="mt-4 py-1 px-3"
      color="danger"
      title="بازگشت به داشبورد"
      @click="$router.push({ name: 'Dashboard' })" />
  </div>
</template>

<script lang="ts" setup>
import NotFoundVector from "@/assets/img/NotFoundVector.vue";
import KsButton from "@/components/common/KsButton.vue";
</script>
