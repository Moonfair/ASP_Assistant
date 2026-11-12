import { readFile, mkdir, writeFile } from 'node:fs/promises';

const root = new URL('../', import.meta.url);
const read = (file) => readFile(new URL(file, root), 'utf8');
const [html, css, client, account, notifications, editor, desktop, share, diagramFormat, diagramEditor, catalog, portraits,
  gearIcons, strategyIcons, selfSelectedOperators, selfSelectedPortraits, captcha, auth, snapshots, votes, voteCatalog, worker] = await Promise.all(
  ['web/index.html', 'web/style.css', 'web/app.js', 'web/account.js', 'web/notifications.js', 'web/editor.js', 'web/desktop-import.js', 'web/share-format.js',
   'web/diagram-format.js', 'web/diagram-editor.js', 'web/catalog.json', 'web/portraits.json', 'web/gear-icons.json', 'web/strategy-icons.json',
   'web/self-selected-operators.json', 'web/self-selected-portraits.json',
   'server/captcha.js', 'server/auth.js', 'server/share-snapshots.js', 'server/operator-votes.js', 'web/operator-vote-catalog.json', 'server/index.js'].map(read)
);
const dist = new URL('dist/server/', root);
await mkdir(dist, { recursive: true });
await writeFile(new URL('index.js', dist), [
  'const pages = ' + JSON.stringify({ '/': html, '/style.css': css,
    '/app.js': [share, diagramFormat, diagramEditor, account, notifications, editor, desktop, client].join('\n') }) + ';',
  'const catalog = ' + catalog.trim() + ';',
  'const portraits = ' + portraits.trim() + ';',
  'const gearIcons = ' + gearIcons.trim() + ';',
  'const strategyIcons = ' + strategyIcons.trim() + ';',
  'const selfSelectedOperators = ' + selfSelectedOperators.trim() + ';',
  'const selfSelectedPortraits = ' + selfSelectedPortraits.trim() + ';',
  'const operatorVoteCatalog = ' + voteCatalog.trim() + ';',
  share, diagramFormat, captcha, auth, snapshots, votes, worker
].join('\n'));
console.log('Built dist/server/index.js');
