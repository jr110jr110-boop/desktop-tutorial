// Builds mock API answers from the C# DTO classes of the finance module, so that every page is fed with exactly the field names of the API.
import fs from "node:fs";
import path from "node:path";

const API = "/home/claude/backend/src";
const DTO_DIRS = ["ProductionDtos", "OfficeDtos", "FinanceDtos", "QualityDtos"]; const ENT_DIRS = ["Production", "Office", "Finance", "Quality"]; const SVC_DIRS = ["Production", "Office", "Finance", "Quality"]; const CTL_DIRS = ["Production", "Office", "Finance", "Quality"];
const classes = {};
const bases = {};
for (const dd of DTO_DIRS) { const dtoDir = `${API}/ViewModels/NasbNiro/${dd}`;
for (const f of fs.readdirSync(dtoDir)) {
  const text = fs.readFileSync(path.join(dtoDir, f), "utf8");
  const re = /public class (\w+)(?:\s*:\s*(\w+))?\s*\{([\s\S]*?)\n    \}/g;
  let m;
  while ((m = re.exec(text))) {
    const props = [];
    const pre = /public ([\w<>?,\[\] ]+?) (\w+) \{ get; set; \}/g;
    let p;
    while ((p = pre.exec(m[3]))) props.push([p[2], p[1].trim()]);
    if (m[2]) bases[m[1]] = m[2];
    if (!classes[m[1]]) classes[m[1]] = props;
  }
}}

let ENUM = 1;
// every action string of the office records, so the deep click reaches every action button
const ALL_ACTIONS = ["edit", "save", "delete", "submit", "approve", "return", "sign", "register", "send", "receipt", "archive", "cancel", "refer", "verify", "comment", "close", "hold", "submit-minutes", "approve-minutes", "sign-minutes", "done", "reject", "record", "complete", "read"];
export const setEnum = (n) => { ENUM = n; };
const enumNames = new Set();
for (const ed of ENT_DIRS) { const entDir = `${API}/Entities/Model/NasbNiro/${ed}`;
for (const f of fs.readdirSync(entDir)) for (const m of fs.readFileSync(path.join(entDir, f), "utf8").matchAll(/public enum (\w+)/g)) enumNames.add(m[1]); }

function mockType(t, depth) {
  t = t.replace(/\?$/, "");
  if (t === "string") return "نمونه";
  if (["int", "long", "short", "byte"].includes(t)) return 1;
  if (["decimal", "double", "float"].includes(t)) return 1500000;
  if (t === "bool") return process.env.MOCKBOOL === "1";
  if (t === "DateTime" || t === "DateTimeOffset") return "2026-01-15T00:00:00";
  if (t === "Guid") return "11111111-1111-1111-1111-111111111111";
  let m = /^(?:List|IEnumerable|IList|ICollection)<(.+)>$/.exec(t);
  if (m) return depth > 3 ? [] : [mockType(m[1], depth + 1)];
  if (/^Dictionary</.test(t)) return {};
  if (enumNames.has(t)) return ENUM;
  if (classes[t]) return mockClass(t, depth + 1);
  return null;
}
export function mockClass(name, depth = 0) {
  const o = bases[name] && classes[bases[name]] ? mockClass(bases[name], depth) : {};
  for (const [n, t] of classes[name] || []) o[n[0].toLowerCase() + n.slice(1)] = n === "AllowedActions" ? ALL_ACTIONS : depth > 4 ? null : mockType(t, depth);
  return o;
}

// route table from the controllers: "METHOD route" -> { type, paged }
const services = {};
for (const sd of SVC_DIRS) { const svcDir = `${API}/Services/NasbNiro/${sd}`;
for (const f of fs.readdirSync(svcDir)) for (const m of fs.readFileSync(path.join(svcDir, f), "utf8").matchAll(/Task<ResponseDto<(.+?)>> (\w+)\(/g)) services[m[2]] = m[1]; }
export const routes = [];
for (const cd of CTL_DIRS) { const ctlDir = `${API}/WebApi/Controllers/${cd}`;
for (const f of fs.readdirSync(ctlDir)) {
  const text = fs.readFileSync(path.join(ctlDir, f), "utf8");
  const parts = text.split(/\[Route\("/).slice(1);
  for (const part of parts) {
    const prefix = part.slice(0, part.indexOf('"'));
    for (const m of part.matchAll(/\[Http(Get|Post|Delete|Put)(?:\("([^"]*)"\))?\]([\s\S]*?)(?=\[Http|\n    \}\s*\n|$)/g)) {
      const body = m[3];
      const sm = /\.(\w+Async)\(/.exec(body);
      routes.push({ method: m[1].toLowerCase(), path: (prefix + (m[2] ? "/" + m[2] : "")).replace(/^api\/v1\//, ""), service: sm ? sm[1] : null, paged: /ToPaginatedResultSafe/.test(body), file: /File\(/.test(body) });
    }
  }
}
}
function typeOfRoute(r) {
  const t = r.service ? services[r.service] : null;
  return t ? t.replace(/^PagedList<(.+)>$/, "$1") : null;
}
function matchRoute(method, url) {
  const u = url.replace(/^\//, "").split("?")[0].replace(/\/$/, "");
  for (const r of [...routes].sort((a, b) => (a.path.match(/\{/g) || []).length - (b.path.match(/\{/g) || []).length)) {
    if (r.method !== method) continue;
    const re = new RegExp("^" + r.path.replace(/\{[^}]+\}/g, "[^/]+") + "$", "i");
    if (re.test(u)) return r;
  }
  return null;
}
export function answer(method, url) {
  const r = matchRoute(method, url);
  if (!r) return { unknown: true };
  if (r.file) return { file: true };
  const t = typeOfRoute(r);
  let data;
  if (!t) data = {};
  else if (t === "bool") data = true;
  else {
    const m = /^List<(.+)>$/.exec(t);
    if (m) data = [mockType(m[1], 0)];
    else data = mockType(t, 0);
  }
  if (r.paged) data = { data: Array.isArray(data) ? data : [data], currentPage: 1, itemsPerPage: 50, totalItems: 1, totalPages: 1 };
  return { body: { success: true, message: null, data } };
}
if (process.argv[1] && process.argv[1].endsWith("mockdata.mjs")) {
  console.log(Object.keys(classes).length, "classes", routes.length, "routes");
  const unknown = routes.filter((r) => !r.file && !typeOfRoute(r) && r.service);
  console.log("routes without a type:", unknown.map((r) => r.method + " " + r.path + " " + r.service).join("\n"));
  console.log(JSON.stringify(answer("get", "finance/treasury/cash-position").body).slice(0, 400));
}
