export const Colors = {
  primary: "#1A5CFFFF" as string,
  primaryLight: "#68808D" as string,
  primaryLighter: "#a6bdc4" as string,
  secondary: "#5B3AFF" as string,
  success: "rgb(23, 201, 100)" as string,
  danger: "#F2135D" as string,
  warn: "rgb(255,136,0)" as string,
  secondaryLight: "rgba(91,58,255,0.14)" as string,
  greyLight: "#F1F1F1" as string,
  white: "#ffffff" as string,
  black: "#000000" as string,
  textColor:
    localStorage.getItem("theme") === "darkMode"
      ? "#000000"
      : ("#ffffff" as string),
};
