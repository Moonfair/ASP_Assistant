import { readFile, mkdir, writeFile } from 'node:fs/promises';

const root = new URL('../', import.meta.url);
const read = (file) => readFile(new URL(file, root), 'utf8');
const [html, css, client, account, editor, desktop, share, diagramFormat, diagramEditor, catalog, portraits, gearIcons, captcha, auth, worker] = await Promise.all(
  ['web/index.html', 'web/style.css', 'web/app.js', 'web/account.js', 'web/editor.js', 'web/desktop-import.js', 'web/share-format.js',
   'web/diagram-format.js', 'web/diagram-editor.js', 'web/catalog.json', 'web/portraits.json', 'web/gear-icons.json',
   'server/captcha.js', 'server/auth.js', 'server/index.js'].map(read)
);
const dist = new URL('dist/server/', root);
await mkdir(dist, { recursive: true });
await writeFile(new URL('index.js', dist), [
  'const pages = ' + JSON.stringify({ '/': html, '/style.css': css,
    '/app.js': [share, diagramFormat, diagramEditor, account, editor, desktop, client].join('\n') }) + ';',
  'const catalog = ' + catalog.trim() + ';',
  'const portraits = ' + portraits.trim() + ';',
  'const gearIcons = ' + gearIcons.trim() + ';',
  share, captcha, auth, worker
].join('\n'));
console.log('Built dist/server/index.js');
