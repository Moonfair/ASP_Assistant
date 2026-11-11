import { readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';

const read = async (path) => JSON.parse(await readFile(new URL(path, import.meta.url), 'utf8'));
const [previous, { operators }, { equipment }, { coreCovenants }, { strategies }] = await Promise.all([
  read('../web/catalog.json'),
  read('../../../data/operators.json'),
  read('../../../data/equipment.json'),
  read('../../../data/covenants.json'),
  read('../../../data/strategies.json')
]);

async function bundleCatalogIcons(entries, folder) {
  const assets = await Promise.all(entries.map(async ({ name, iconPath, ...details }) => {
    const icon = iconPath && await readFile(new URL('../../../data/' + iconPath, import.meta.url))
      .catch(error => { if (error.code !== 'ENOENT') throw error; });
    const id = icon && createHash('sha256').update(folder === 'gear-icons' ? name : icon).digest('hex').slice(0, 16);
    return { name, details, id, icon };
  }));
  const icons = Object.fromEntries(assets.filter(asset => asset.icon)
    .map(({ id, icon }) => [id, icon.toString('base64')]));
  await writeFile(new URL('../web/' + folder + '.json', import.meta.url), JSON.stringify(icons) + '\n');
  return Object.fromEntries(assets.map(({ name, details, id }) =>
    [name, { ...details, image: id ? '/' + folder + '/' + id + '.png' : null }]));
}

const [gear, strategyCatalog] = await Promise.all([
  bundleCatalogIcons(equipment.map(({ name, tier, iconPath }) => ({ name, tier, iconPath })), 'gear-icons'),
  bundleCatalogIcons(strategies, 'strategy-icons')
]);
const catalog = {
  operators: Object.fromEntries(operators.map(({ name, tier, coreCovenants }) => [
    name, { tier, covenants: coreCovenants ?? [], image: previous.operators[name]?.image ?? null }
  ])),
  equipment: gear,
  covenants: Object.fromEntries(coreCovenants.map(name => [name, previous.covenants[name] ?? null])),
  strategies: strategyCatalog
};
await writeFile(new URL('../web/catalog.json', import.meta.url), JSON.stringify(catalog) + '\n');
