<script setup lang="ts">
import logo from "@/assets/img/logo.png";
</script>

<template>
  <div
    class="vh-100 w-100 d-flex flex-column gap-4 justify-content-center align-items-center">
    <div class="d-flex flex-column align-items-center gap-2">
      <img :src="logo" alt="" style="width: 82px" />
      <span class="text-medium fw-bold"> INNCO </span>
      <span class="text-small fw-bold" dir="rtl">
        سامانه مدیریت نصب نیرو ایران
      </span>
    </div>
    <div class="position-relative">
      <div class="custom-loader center" />
    </div>
  </div>
</template>
