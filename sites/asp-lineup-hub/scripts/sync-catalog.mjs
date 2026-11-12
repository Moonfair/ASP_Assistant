import { readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';

const read = async (path) => JSON.parse(await readFile(new URL(path, import.meta.url), 'utf8'));
const [previous, { operators }, { operators: selfSelected }, { equipment }, covenantCatalog, { strategies },
  { seasonCode }, skillData, selfSelectedSkillData] = await Promise.all([
  read('../web/catalog.json'),
  read('../../../data/operators.json'),
  read('../../../data/self-selected-operators.json'),
  read('../../../data/equipment.json'),
  read('../../../data/covenants.json'),
  read('../../../data/strategies.json'),
  read('../../../data/source_metadata.json'),
  read('../../../data/operator_skills.json'),
  read('../../../data/self-selected-operator-skills.json')
]);
if (!seasonCode || skillData.season !== seasonCode || selfSelectedSkillData.season !== seasonCode) {
  throw new Error('请先运行 data/fetch_operator_skills.py 同步当前赛季技能。');
}

const { coreCovenants, additionalCovenants, activationRules = {}, jobChangeActivators = [],
  jobChangeEquipment = {} } = covenantCatalog;

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

const [gear, strategyCatalog, selfSelectedCatalog] = await Promise.all([
  bundleCatalogIcons(equipment.map(({ name, tier, iconPath }) => ({ name, tier, iconPath })), 'gear-icons'),
  bundleCatalogIcons(strategies, 'strategy-icons'),
  bundleCatalogIcons(selfSelected.map(({ name, tier, iconPath, coreCovenants, additionalCovenants }) =>
    ({ name, tier, iconPath, covenants: coreCovenants, additionalCovenants, selfSelected: true })), 'self-selected-portraits')
]);
const catalog = {
  operators: Object.fromEntries(operators.map(({ name, tier, coreCovenants, additionalCovenants, equipmentSlotCount }) => [
    name, { tier, covenants: coreCovenants ?? [], image: previous.operators[name]?.image ?? null,
      ...(additionalCovenants?.length ? { additionalCovenants } : {}),
      ...(equipmentSlotCount ? { equipmentSlotCount } : {}) }
  ])),
  equipment: gear,
  covenants: Object.fromEntries(coreCovenants.map(name => [name, previous.covenants[name] ?? null])),
  covenantTags: [...new Set([...coreCovenants, ...additionalCovenants])],
  activationRules,
  jobChangeActivators,
  jobChangeEquipment,
  strategies: strategyCatalog
};
await writeFile(new URL('../web/catalog.json', import.meta.url), JSON.stringify(catalog) + '\n');
await writeFile(new URL('../web/self-selected-operators.json', import.meta.url), JSON.stringify(selfSelectedCatalog) + '\n');
const skillsByName = { ...skillData.operators, ...selfSelectedSkillData.operators };
const operatorVotes = {
  season: seasonCode,
  operators: Object.fromEntries([...operators, ...selfSelected].map(({ name }) => [
    name, (Object.hasOwn(skillsByName, name) ? skillsByName[name] : []).map(skill => skill.slot)
  ]))
};
await writeFile(new URL('../web/operator-vote-catalog.json', import.meta.url), JSON.stringify(operatorVotes) + '\n');
