<script setup lang="ts">
// Header of a printed report: logo, report title and filters, print date and the user who printed it.
import moment from "moment-jalaali";
import Logo from "@/assets/img/logo.png";

defineProps({
  title: { type: String, required: true },
  subtitle: { type: String, default: "" },
  code: { type: String, default: "" },
  printOnly: { type: Boolean, default: true },
});

function userName(): string {
  try {
    const token = localStorage.getItem("token") || "";
    const part = token.split(".")[1];
    if (!part) return "";
    const json = decodeURIComponent(
      atob(part.replace(/-/g, "+").replace(/_/g, "/"))
        .split("")
        .map((c) => "%" + ("00" + c.charCodeAt(0).toString(16)).slice(-2))
        .join("")
    );
    const claims = JSON.parse(json);
    const pick = (k: string) => Object.keys(claims).find((x) => x === k || x.endsWith("/" + k));
    const key = pick("name") || pick("unique_name") || pick("given_name");
    return key ? String(claims[key]) : "";
  } catch {
    return "";
  }
}

const printedAt = moment().format("jYYYY/jMM/jDD - HH:mm");
const printedBy = userName();
</script>

<template>
  <div :class="{ 'print-only': printOnly }" class="ks-report-header">
    <img :src="Logo" alt="" class="logo" />
    <div class="text-center flex-grow-1">
      <h1 class="title">{{ title }}</h1>
      <div v-if="subtitle" class="subtitle">{{ subtitle }}</div>
    </div>
    <div class="meta">
      <div v-if="code">شماره: <span class="inv-ltr">{{ code }}</span></div>
      <div>تاریخ چاپ: {{ printedAt }}</div>
      <div v-if="printedBy">چاپ‌کننده: {{ printedBy }}</div>
      <div>سامانه یکپارچه PMIS</div>
    </div>
  </div>
</template>
