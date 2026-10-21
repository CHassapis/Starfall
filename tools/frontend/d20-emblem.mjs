/**
 * Redraws the D20 inside the owner-supplied homepage emblem (public/ornaments/<theme>/home-d20-icon.svg) as a true
 * regular icosahedron; everything else in the picture is kept as supplied.
 *
 * The supplied die was a hexagon 98 units wide and 148 tall (a regular one is 128 × 148), centred 10 units above the
 * compass and with inner lines that are not edges of an icosahedron, which is why it looked warped. Here the die is
 * the icosahedron seen straight onto one face: the outline is a regular hexagon (pointy top and bottom), the face in
 * front is the upright triangle in the middle, joined to the hexagon by 9 more faces (10 visible of 20). With the
 * outline radius R, the front face's corners lie on radius R / φ (φ = golden ratio) in line with every other hexagon
 * corner. R = 64 puts the top and bottom corners on the tips of the north and south compass points (y 64 / 192).
 *
 * Each theme keeps its own gradient definitions byte for byte (the first lines, up to </defs>); the body is shared.
 *   node tools/frontend/d20-emblem.mjs          rewrite the three SVGs
 *   node tools/frontend/d20-emblem.mjs --check  exit 1 if a file differs from what this script writes
 */
import { readFile, writeFile } from "node:fs/promises";

const DIR = "src/frontend/apps/builder-app/public/ornaments";
const THEMES = ["midnight", "porcelain", "slate"];
const C = 128, R = 64, PHI = (1 + Math.sqrt(5)) / 2, r = R / PHI;
const f = (n) => Number(n.toFixed(2));
const at = (radius, deg) => [f(C + radius * Math.cos((deg * Math.PI) / 180)), f(C + radius * Math.sin((deg * Math.PI) / 180))];
// SVG y points down: -90° is straight up
const hex = [-90, -30, 30, 90, 150, -150].map((d) => at(R, d)); // top, upper right, lower right, bottom, lower left, upper left
const [front0, front1, front2] = [-90, 30, 150].map((d) => at(r, d)); // the front face: top, lower right, lower left
const [hT, hUR, hLR, hB, hLL, hUL] = hex;
// the ten visible faces, lit from the upper left (fill opacity of the blue gradient)
const faces = [
  [[front0, front1, front2], 0.3],
  [[front0, hUL, hT], 0.26], [[front0, hT, hUR], 0.16],
  [[front0, front2, hUL], 0.22], [[front0, hUR, front1], 0.12],
  [[front2, hLL, hUL], 0.2], [[front1, hUR, hLR], 0.08],
  [[front2, front1, hB], 0.18], [[front2, hB, hLL], 0.14], [[front1, hLR, hB], 0.1],
];
const pts = (list) => list.map(([x, y]) => `${x},${y}`).join(" ");
const line = (a, b) => `M${a[0]} ${a[1]} L${b[0]} ${b[1]}`;
// the 15 edges inside the outline: the front face, and each of its corners to the three nearest hexagon corners
const inner = [
  line(front0, front1), line(front1, front2), line(front2, front0),
  line(front0, hUL), line(front0, hT), line(front0, hUR),
  line(front1, hUR), line(front1, hLR), line(front1, hB),
  line(front2, hB), line(front2, hLL), line(front2, hUL),
].join(" ");

const body = `    <circle cx="128" cy="128" r="89" stroke="url(#metal)" stroke-width="1.5" stroke-dasharray="2 8" opacity=".67"/>
    <path d="M 66 57 A 92 92 0 0 1 113 35 M 143 35 A 92 92 0 0 1 190 57 M 199 68 A 92 92 0 0 1 221 113 M 221 143 A 92 92 0 0 1 199 190 M 190 199 A 92 92 0 0 1 143 221 M 113 221 A 92 92 0 0 1 66 199 M 57 190 A 92 92 0 0 1 35 143 M 35 113 A 92 92 0 0 1 57 66" stroke="url(#metal)" stroke-width="3.2" opacity=".87"/>
    <circle cx="128" cy="128" r="77" stroke="url(#blue)" stroke-width=".9" stroke-dasharray="74 21" opacity=".9"/>
    <path d="M128 8 L132 44 L128 63 L124 44 Z M128 248 L132 212 L128 193 L124 212 Z M8 128 L44 124 L63 128 L44 132 Z M248 128 L212 124 L193 128 L212 132 Z" fill="url(#metal)" opacity=".95"/>
    <g fill="url(#blue)" stroke="none">${faces.map(([p, o]) => `<polygon points="${pts(p)}" fill-opacity="${o}"/>`).join("")}</g>
    <circle cx="128" cy="128" r="30" fill="url(#star)" opacity=".55"/>
    <path d="${inner}" stroke="url(#blue)" stroke-width="2.2" stroke-linecap="round" opacity=".95"/>
    <polygon points="${pts([front0, front1, front2])}" stroke="url(#metal)" stroke-width="1.6" stroke-linejoin="round" opacity=".8"/>
    <polygon points="${pts(hex)}" stroke="url(#metal)" stroke-width="3.4" stroke-linejoin="round"/>
    <path d="M128 106 Q130.6 125.4 146 128 Q130.6 130.6 128 150 Q125.4 130.6 110 128 Q125.4 125.4 128 106Z" fill="url(#metal)"/>
    <circle cx="128" cy="33" r="4.5" fill="url(#metal)"/><circle cx="223" cy="128" r="4.5" fill="url(#metal)"/><circle cx="128" cy="223" r="4.5" fill="url(#metal)"/><circle cx="33" cy="128" r="4.5" fill="url(#metal)"/>
    <polygon points="53.00,49.00 53.57,54.43 59.00,55.00 53.57,55.57 53.00,61.00 52.43,55.57 47.00,55.00 52.43,54.43" fill="url(#metal)" opacity="1" /><polygon points="203.00,49.00 203.57,54.43 209.00,55.00 203.57,55.57 203.00,61.00 202.43,55.57 197.00,55.00 202.43,54.43" fill="url(#metal)" opacity="1" /><polygon points="53.00,196.00 53.57,201.43 59.00,202.00 53.57,202.57 53.00,208.00 52.43,202.57 47.00,202.00 52.43,201.43" fill="url(#metal)" opacity="1" /><polygon points="203.00,196.00 203.57,201.43 209.00,202.00 203.57,202.57 203.00,208.00 202.43,202.57 197.00,202.00 202.43,201.43" fill="url(#metal)" opacity="1" /></svg>
`;

const check = process.argv.includes("--check");
let differs = false;
for (const theme of THEMES) {
  const file = `${DIR}/${theme}/home-d20-icon.svg`;
  const text = await readFile(file, "utf8");
  const end = text.indexOf("</defs>");
  if (end < 0) throw new Error(`${file}: no </defs>`);
  const next = text.slice(0, end + "</defs>".length) + "\n" + body;
  if (check) {
    if (next !== text) { differs = true; console.log(`${file} differs from the generated emblem`); }
  } else {
    await writeFile(file, next);
    console.log(`${file}: written`);
  }
}
if (differs) process.exit(1);
