export default interface PreviewTextRow {
  key?: string;
  title: string;
  value?: string;
  prefix?: "تومان" | "روز" | "عدد";
  format?: "percent" | "price";
}
