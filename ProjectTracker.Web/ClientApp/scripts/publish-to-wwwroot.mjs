// Copies the static export (out/) into ../wwwroot so ASP.NET Core serves the compiled UI.
// (Manual recursion instead of fs.cpSync: cpSync is unreliable on some Windows paths with non-ASCII folders.)
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const here = path.dirname(fileURLToPath(import.meta.url))
const out = path.resolve(here, '..', 'out')
const wwwroot = path.resolve(here, '..', '..', 'wwwroot')

function copyDir(from, to) {
  fs.mkdirSync(to, { recursive: true })
  for (const entry of fs.readdirSync(from, { withFileTypes: true })) {
    const source = path.join(from, entry.name)
    const target = path.join(to, entry.name)
    if (entry.isDirectory()) copyDir(source, target)
    else fs.copyFileSync(source, target)
  }
}

function clearDir(dir) {
  if (!fs.existsSync(dir)) return
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const target = path.join(dir, entry.name)
    if (entry.isDirectory()) {
      clearDir(target)
      fs.rmdirSync(target)
    } else fs.unlinkSync(target)
  }
}

if (!fs.existsSync(out)) throw new Error('out/ not found — run `next build` first')
clearDir(wwwroot)
copyDir(out, wwwroot)
console.log(`Published ${out} -> ${wwwroot}`)
