import type FilterQueryValue from "@/types/FilterQueryValue";

export default interface FilterQuery {
  propName: string;
  title: string;
  type: "select" | "date";
  values?: FilterQueryValue[];
  selected: string | number;
}
