import moment from "moment-jalaali";

export const convertToJalaliDate = (gregorianDate) => {
  if (!gregorianDate) return "-";

  moment.loadPersian({ dialect: "persian-modern" });
  return moment(gregorianDate, "YYYY-MM-DDTHH:mm").format(
    "jDD jMMMM jYYYY | HH:mm"
  );
};
