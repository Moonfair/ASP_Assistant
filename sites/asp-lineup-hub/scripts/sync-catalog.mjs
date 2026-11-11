import { readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';

const read = async (path) => JSON.parse(await readFile(new URL(path, import.meta.url), 'utf8'));
const [previous, { operators }, { equipment }, { coreCovenants }] = await Promise.all([
  read('../web/catalog.json'),
  read('../../../data/operators.json'),
  read('../../../data/equipment.json'),
  read('../../../data/covenants.json')
]);
const gearIcons = {};
const gear = await Promise.all(equipment.map(async ({ name, tier, iconPath }) => {
  const id = createHash('sha256').update(name).digest('hex').slice(0, 16);
  const icon = iconPath && await readFile(new URL('../../../data/' + iconPath, import.meta.url))
    .catch(error => { if (error.code !== 'ENOENT') throw error; });
  if (icon) gearIcons[id] = icon.toString('base64');
  return [name, { tier, image: icon ? '/gear-icons/' + id + '.png' : null }];
}));
const catalog = {
  operators: Object.fromEntries(operators.map(({ name, tier, coreCovenants }) => [
    name, { tier, covenants: coreCovenants ?? [], image: previous.operators[name]?.image ?? null }
  ])),
  equipment: Object.fromEntries(gear),
  covenants: Object.fromEntries(coreCovenants.map(name => [name, previous.covenants[name] ?? null]))
};
await writeFile(new URL('../web/catalog.json', import.meta.url), JSON.stringify(catalog) + '\n');
await writeFile(new URL('../web/gear-icons.json', import.meta.url), JSON.stringify(gearIcons) + '\n');
