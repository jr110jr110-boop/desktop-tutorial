import * as XLSX from "xlsx";
import { jaliToIsoSafe } from "@/composables/fnExcelDates";
import { latinDigits, parseAmount } from "@/composables/fn";

// Reading an Excel sheet for the accounting imports (opening balances, bank statements, attendance).

export interface ExcelField {
  key: string;
  label: string;
  /** header words (lower case, Persian ی/ک) that map a column to the field */
  words: string[];
  required?: boolean;
}

export interface ExcelSheet {
  fileName: string;
  headers: string[];
  rows: any[][];
  map: Record<string, number>;
}

function normalize(text: any): string {
  return String(text ?? "").trim().toLowerCase().replace(/ي/g, "ی").replace(/ك/g, "ک").replace(/\s+/g, " ");
}

/** Reads the first sheet of an xlsx file and maps the columns to the fields by the header words. */
export async function readExcel(file: File, fields: ExcelField[]): Promise<ExcelSheet> {
  const book = XLSX.read(await file.arrayBuffer(), { type: "array", cellDates: false });
  const sheet = book.Sheets[book.SheetNames[0]];
  // raw: false keeps the cells as they are shown, so a code with leading zeros stays a text
  const rows: any[][] = XLSX.utils.sheet_to_json(sheet, { header: 1, raw: false, defval: null, blankrows: false });
  if (rows.length < 2) throw new Error("فایل ردیفی ندارد.");
  const headers = rows[0].map((h) => String(h ?? "").trim());
  const map: Record<string, number> = {};
  fields.forEach((f) => {
    let index = headers.findIndex((h) => f.words.includes(normalize(h)));
    if (index < 0) index = headers.findIndex((h) => f.words.some((w) => normalize(h).includes(w)));
    map[f.key] = index;
  });
  return { fileName: file.name, headers, rows: rows.slice(1), map };
}

export function cellText(sheet: ExcelSheet, row: any[], key: string): string | null {
  const index = sheet.map[key];
  if (index === undefined || index < 0) return null;
  const v = row[index];
  return v === null || v === undefined || String(v).trim() === "" ? null : String(latinDigits(String(v)).trim());
}

export function cellNumber(sheet: ExcelSheet, row: any[], key: string): number | null {
  const t = cellText(sheet, row, key);
  return t === null ? null : parseAmount(t);
}

/** A date cell: Persian text (1404/05/12) or an ISO date; null when it cannot be read. */
export function cellDate(sheet: ExcelSheet, row: any[], key: string): string | null {
  const t = cellText(sheet, row, key);
  return t === null ? null : jaliToIsoSafe(t);
}
