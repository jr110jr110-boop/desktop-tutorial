export default interface TableValue {
  propName: string;
  type:
    | "text"
    | "image"
    | "date"
    | "status"
    | "si-number"
    | "price"
    | "percent";
  clickable?: boolean;
  status?: {
    title: string;
    value: number | boolean;
    color?:
      | "primary"
      | "secondary"
      | "warn"
      | "danger"
      | "dark"
      | "success"
      | "transparent";
  }[];
  subPropName?: string;
  maxWidth?: number;
  textCenter?: boolean;
}
