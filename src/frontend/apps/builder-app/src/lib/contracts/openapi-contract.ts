/**
 * The API description (docs/api/openapi.json) as TypeScript: the types of a contract's schemas, and a check that a
 * value has a schema's shape. The contract tests use both: the committed types must be what the description gives
 * (regenerate with `STARLIGHTS_UPDATE_CONTRACTS=1 npm test -w apps/builder-app -- contracts`), and what the web app
 * produces in a contract's shape (its own calculation traces) must pass the check.
 */

export interface Schema {
  type?: string | string[];
  enum?: string[];
  properties?: Record<string, Schema>;
  required?: string[];
  items?: Schema;
  oneOf?: Schema[];
  $ref?: string;
  pattern?: string;
  format?: string;
}

export interface OpenApiDocument {
  components: { schemas: Record<string, Schema> };
}

const refName = (ref: string) => ref.replace(/^#\/components\/schemas\//, "");

/** The schema's types without "string" where the server writes a number (ASP.NET also reads numbers from strings). */
function typesOf(s: Schema): string[] {
  const types = Array.isArray(s.type) ? s.type : s.type ? [s.type] : [];
  return types.some((t) => t === "integer" || t === "number") ? types.filter((t) => t !== "string") : types;
}

function tsType(s: Schema, refs: string[]): string {
  if (s.$ref) {
    const name = refName(s.$ref);
    if (!refs.includes(name)) refs.push(name);
    return name;
  }
  if (s.oneOf) return s.oneOf.map((o) => tsType(o, refs)).join(" | ");
  if (s.enum) return s.enum.map((v) => JSON.stringify(v)).join(" | ");
  return typesOf(s)
    .map((t) => (t === "integer" || t === "number" ? "number" : t === "array" ? `${wrap(tsType(s.items ?? {}, refs))}[]` : t === "object" ? "Record<string, unknown>" : t))
    .join(" | ") || "unknown";
}

const wrap = (t: string) => (t.includes(" ") ? `(${t})` : t);

/** TypeScript for the named schemas and every schema they refer to, in the order they are reached. */
export function contractTypes(document: OpenApiDocument, roots: string[], header: string): string {
  const schemas = document.components.schemas;
  const names = [...roots];
  const out: string[] = [header.trimEnd(), ""];
  for (let i = 0; i < names.length; i++) {
    const name = names[i];
    const s = schemas[name];
    if (!s) throw new Error(`The API description has no schema ${name}`);
    if (s.enum) {
      out.push(`export type ${name} = ${tsType(s, names)};`, "");
      out.push(`export const ${name}Values = [${s.enum.map((v) => JSON.stringify(v)).join(", ")}] as const satisfies readonly ${name}[];`, "");
      continue;
    }
    out.push(`export interface ${name} {`);
    for (const [property, p] of Object.entries(s.properties ?? {})) {
      out.push(`  ${property}${s.required?.includes(property) ? "" : "?"}: ${tsType(p, names)};`);
    }
    out.push("}", "");
  }
  return out.join("\n");
}

/**
 * Whether a value has the schema's shape: its type, its required properties, enum values and the referred schemas,
 * all the way down. Returns the problems found ("trace.steps[0].operation: \"plus\" is not one of …"); none when it fits.
 */
export function checkShape(document: OpenApiDocument, name: string, value: unknown): string[] {
  const problems: string[] = [];
  const check = (s: Schema, v: unknown, at: string): void => {
    if (s.$ref) return check(document.components.schemas[refName(s.$ref)] ?? {}, v, at);
    if (s.oneOf) {
      const fits = s.oneOf.map((o) => {
        const before = problems.length;
        check(o, v, at);
        return problems.splice(before).length === 0;
      });
      if (!fits.some(Boolean)) problems.push(`${at}: fits none of its forms`);
      return;
    }
    if (s.enum) {
      if (!s.enum.includes(v as string)) problems.push(`${at}: ${JSON.stringify(v)} is not one of ${s.enum.join(", ")}`);
      return;
    }
    const types = typesOf(s);
    const kind = v === null ? "null" : Array.isArray(v) ? "array" : Number.isInteger(v) ? "integer" : typeof v;
    if (types.length && !types.some((t) => t === kind || (t === "number" && kind === "integer"))) {
      problems.push(`${at}: ${JSON.stringify(v)} is not ${types.join(" or ")}`);
      return;
    }
    if (kind === "array") (v as unknown[]).forEach((item, i) => check(s.items ?? {}, item, `${at}[${i}]`));
    if (kind === "object" && s.properties) {
      const o = v as Record<string, unknown>;
      for (const r of s.required ?? []) if (o[r] === undefined) problems.push(`${at}.${r}: missing`);
      for (const [property, p] of Object.entries(s.properties)) if (o[property] !== undefined) check(p, o[property], `${at}.${property}`);
      for (const property of Object.keys(o)) if (!(property in s.properties)) problems.push(`${at}.${property}: not in the contract`);
    }
  };
  check({ $ref: `#/components/schemas/${name}` }, value, name);
  return problems;
}
