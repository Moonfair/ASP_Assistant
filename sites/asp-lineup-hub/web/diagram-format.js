export const DIAGRAM_LIMITS = { diagrams: 8, nodes: 20, edges: 40, position: 16000 };
export const DIAGRAM_CARD = { width: 148, height: 118 };
const diagramFence = '```';
const diagramId = '([A-Za-z_][A-Za-z_0-9]{0,23})';
const diagramNode = new RegExp('^' + diagramId + '\\s*\\[\\s*\\{\\{(op|gear):([^{}]{1,100})\\}\\}\\s*\\]$');
const diagramEdge = new RegExp('^' + diagramId + '\\s*-->\\s*(?:\\|([^|]{0,40})\\|\\s*)?' + diagramId + '$');
const diagramHighlight = new RegExp('^highlight\\s+' + diagramId + '$');
const diagramNote = new RegExp('^note\\s+' + diagramId + '\\s*:\\s*(.{1,60})$', 's');
const diagramPosition = new RegExp('^%% aspa-layout\\s+' + diagramId + '\\s+([0-9]{1,5})\\s+([0-9]{1,5})$');
const diagramHeadings = new Set(['flowchart TD', 'flowchart TB', 'graph TD', 'graph TB', 'flowchart LR', 'graph LR']);

function findDescriptionFences(source) {
  const lines = [...source.matchAll(/([^\r\n]*)(?:\r\n|\r|\n|$)/g)];
  const blocks = [];
  for (let index = 0; index < lines.length; index++) {
    const open = lines[index][1];
    const fence = /^[ \t]*(`{3,}|~{3,})(.*)$/.exec(open);
    if (!fence || (fence[1][0] === '`' && fence[2].includes('`'))) continue;
    const start = lines[index].index;
    const marker = fence[1];
    const body = [];
    while (++index < lines.length) {
      const candidate = lines[index][1].trim();
      if (candidate.length >= marker.length && candidate === marker[0].repeat(candidate.length)) break;
      body.push(lines[index][1]);
    }
    const closed = index < lines.length;
    const close = closed ? lines[index][1] : '';
    const end = closed ? lines[index].index + close.length : source.length;
    blocks.push({ start, end, open, close, marker, kind: fence[2].trim(), lines: body, closed });
  }
  return blocks;
}

export function findDiagramBlocks(source) {
  const fences = findDescriptionFences(source);
  const blocks = fences.filter(({ marker, kind }) => marker === diagramFence && (kind === 'lineup' || kind === 'mermaid'))
    .map(({ start, end, kind, lines, closed }) => ({ start, end, kind, lines, closed }));
  return { blocks, unclosed: fences.some(block => !block.closed) };
}

export function splitDiagramDescription(description) {
  const { blocks } = findDiagramBlocks(description);
  if (!blocks.length) return { prose: description, diagrams: '' };
  let prose = '', cursor = 0;
  for (const block of blocks) {
    prose += description.slice(cursor, block.start);
    cursor = block.end;
  }
  prose += description.slice(cursor);
  return { prose: prose.replace(/^[\r\n]+|[\r\n]+$/g, ''),
    diagrams: blocks.map(block => description.slice(block.start, block.end)).join('\n\n') };
}

export function combineDiagramDescription(prose, source) {
  if (!source.trim()) return prose;
  let code = source.replace(/^[\r\n]+|[\r\n]+$/g, '');
  if (!/^[^\S\r\n]*```/m.test(code)) code = diagramFence + 'lineup\n' + code + '\n' + diagramFence;
  return prose ? prose.replace(/[\r\n]+$/, '') + '\n\n' + code : code;
}

function isOriginalVideoFence(block) {
  return block.closed && block.lines.length === 2
    && /^```aspa-source[ \t]*$/.test(block.open) && /^```[ \t]*$/.test(block.close);
}

export function splitLineupDescription(description) {
  const source = findDescriptionFences(description).find(isOriginalVideoFence);
  let originalAuthor = '', originalVideoUrl = '';
  if (source) {
    [originalAuthor, originalVideoUrl] = source.lines.map(line => line.trim());
    const before = description.slice(0, source.start);
    const after = description.slice(source.end).replace(/^(?:\r\n|\n|\r)(?:\r\n|\n|\r)?/, '');
    description = after ? before + after : before.replace(/[\r\n]+$/, '');
  }
  return { ...splitDiagramDescription(description), originalAuthor, originalVideoUrl };
}

export function combineLineupDescription(prose, diagrams, originalAuthor, originalVideoUrl) {
  const singleLine = value => value.replace(/\r\n|\r|\n/g, ' ').trim();
  const author = singleLine(originalAuthor), url = singleLine(originalVideoUrl);
  if (author || url) {
    const source = ['```aspa-source', author, url, diagramFence].join('\n');
    const putSourceFirst = findDescriptionFences(prose).some(block => !block.closed || isOriginalVideoFence(block));
    prose = !prose ? source : putSourceFirst ? source + '\n\n' + prose : prose.replace(/[\r\n]+$/, '') + '\n\n' + source;
  }
  return combineDiagramDescription(prose, diagrams);
}

function lineupSourceUrlParts(value) {
  const input = value.trim();
  const parts = /^https:\/\/([^/?#]+)(\/[^?#]*)(?:[?#][^\s\\\p{Cc}]*)?$/iu.exec(input);
  return parts ? { input, host: parts[1].toLowerCase().replace(/:443$/, ''), path: parts[2] } : null;
}

function isBilibiliVideo({ host, path }) {
  if (host === 'b23.tv') return /^\/[A-Za-z0-9]+\/?$/.test(path);
  return ['bilibili.com', 'www.bilibili.com', 'm.bilibili.com'].includes(host)
    && /^\/video\/(?:BV[A-Za-z0-9]+|av[0-9]+)\/?$/.test(path);
}

export function bilibiliVideoHref(value) {
  const parts = lineupSourceUrlParts(value);
  return parts && isBilibiliVideo(parts) ? new URL(parts.input).href : '';
}

export function lineupSourceHref(value) {
  const parts = lineupSourceUrlParts(value);
  if (!parts) return '';
  if (isBilibiliVideo(parts)) return new URL(parts.input).href;
  if (parts.host !== 'game.skland.com' || parts.path !== '/arknights/stronghold-guide/detail') return '';
  const url = new URL(parts.input);
  const hasRawGuideId = url.search.slice(1).split('&').some(parameter => /^guideId=[0-9]+$/.test(parameter));
  return hasRawGuideId && url.searchParams.getAll('guideId').length === 1 ? url.href : '';
}

function isSklandGuideHref(href) {
  return href.startsWith('https://game.skland.com/');
}

function escape(value) {
  return String(value ?? '').replace(/[&<>"']/g, c => ({
    '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;'
  }[c]));
}

export function renderDescriptionProse(prose, catalog = {}) {
  const catalogs = { op: catalog.operators, gear: catalog.equipment, strategy: catalog.strategies };
  const tokens = /`[^`\r\n]+`|\\\{\{|\{\{(op|gear|strategy):([^{}\r\n]{1,100})\}\}/g;
  let imageCount = 0;
  function renderLine(line) {
    const heading = /^ {0,3}(#{1,6})[ \t]+(.+)$/.exec(line);
    const text = heading ? heading[2].replace(/[ \t]+#+[ \t]*$/, '').trimEnd() : line;
    let markup = '', cursor = 0;
    for (const match of text.matchAll(tokens)) {
      const name = match[2]?.trim();
      const items = catalogs[match[1]];
      const image = name && items && Object.hasOwn(items, name) && items[name].image;
      markup += escape(text.slice(cursor, match.index));
      if (image && imageCount < 160) {
        imageCount++;
        markup += `<img class="description-icon" src="${escape(image)}" alt="${escape(name)}" title="${escape(name)}" loading="lazy">`;
      } else {
        markup += name ? '[' + escape(name) + ']' : escape(match[0]);
      }
      cursor = match.index + match[0].length;
    }
    markup += escape(text.slice(cursor));
    if (!heading) return markup;
    const level = Math.min(6, Math.max(4, heading[1].length + 2));
    return `<h${level}>${markup}</h${level}>`;
  }

  const renderLines = text => text.split(/\r\n|\r|\n/).map(renderLine).join('\n');
  let html = '', cursor = 0;
  for (const block of findDescriptionFences(prose)) {
    html += renderLines(prose.slice(cursor, block.start)) + escape(prose.slice(block.start, block.end));
    cursor = block.end;
  }
  return html + renderLines(prose.slice(cursor));
}

export function parseDiagram(lines) {
  const graph = {
    nodes: new Map(), edges: [], highlights: new Set(), notes: new Map(),
    comments: [], positions: new Map(), horizontal: false
  };
  const references = [];
  for (const [index, source] of lines.entries()) {
    const line = source.trim();
    const fail = message => { throw new Error(`关系图第 ${index + 1} 行：${message}`); };
    if (!line) continue;
    if (diagramHeadings.has(line)) { graph.horizontal = line.endsWith('LR'); continue; }
    if (line.startsWith('%%')) {
      const position = diagramPosition.exec(line);
      if (position) {
        const [, id, rawX, rawY] = position;
        const x = Number(rawX), y = Number(rawY);
        if (graph.positions.has(id) || x > DIAGRAM_LIMITS.position || y > DIAGRAM_LIMITS.position) fail('节点坐标无效或重复。');
        graph.positions.set(id, { x, y });
        references.push([id, index]);
      } else if (/^%% aspa-layout(?: |$)/.test(line)) fail('节点坐标无效。');
      else if (!/^%% asp-pos(?:\s|$)/.test(line)) graph.comments.push(line);
      continue;
    }
    const node = diagramNode.exec(line);
    if (node) {
      const [, id, kind, name] = node;
      if (graph.nodes.has(id) || graph.nodes.size >= DIAGRAM_LIMITS.nodes) fail('节点编号重复，或超过 20 个节点。');
      graph.nodes.set(id, { id, kind, name: name.trim() });
      continue;
    }
    const edge = diagramEdge.exec(line);
    if (edge) {
      const [, from, label = '', to] = edge;
      if (graph.edges.length >= DIAGRAM_LIMITS.edges) fail('每张图最多支持 40 条连线。');
      graph.edges.push({ from, to, label: label.trim() });
      references.push([from, index], [to, index]);
      continue;
    }
    const highlight = diagramHighlight.exec(line);
    const note = diagramNote.exec(line);
    if (highlight) graph.highlights.add(highlight[1]);
    else if (note) graph.notes.set(note[1], note[2].trim());
    else fail('无法识别，请检查节点、连线或注释的写法。');
    references.push([(highlight ?? note)[1], index]);
  }
  for (const [id, index] of references) {
    if (!graph.nodes.has(id)) throw new Error(`关系图第 ${index + 1} 行：节点 ${id} 未定义。`);
  }
  return graph;
}

export function formatDiagram(graph, kind = 'lineup', newline = '\n') {
  const lines = [diagramFence + kind, graph.horizontal ? 'flowchart LR' : 'flowchart TD', ...graph.comments];
  for (const { id, kind, name } of graph.nodes.values()) lines.push(`${id}[{{${kind}:${name}}}]`);
  for (const { from, to, label } of graph.edges) lines.push(`${from} -->${label ? '|' + label + '|' : ''} ${to}`);
  for (const id of graph.nodes.keys()) {
    if (graph.highlights.has(id)) lines.push('highlight ' + id);
    if (graph.notes.has(id)) lines.push(`note ${id}: ${graph.notes.get(id)}`);
  }
  for (const id of graph.nodes.keys()) {
    const point = graph.positions.get(id);
    if (point) lines.push(`%% aspa-layout ${id} ${point.x} ${point.y}`);
  }
  return [...lines, diagramFence].join(newline);
}

export function arrangeDiagram(graph) {
  const ids = [...graph.nodes.keys()];
  const levels = new Map(ids.map(id => [id, 0]));
  const pending = new Set(ids);
  while (pending.size) {
    const id = ids.find(id => pending.has(id) && !graph.edges.some(edge => edge.to === id && pending.has(edge.from)))
      ?? pending.values().next().value;
    pending.delete(id);
    for (const edge of graph.edges) {
      if (edge.from === id && pending.has(edge.to)) levels.set(edge.to, Math.max(levels.get(edge.to), levels.get(id) + 1));
    }
  }
  const positions = new Map();
  const rows = new Map();
  for (const [id, level] of levels) {
    const index = rows.get(level) ?? 0;
    rows.set(level, index + 1);
    positions.set(id, graph.horizontal ? { x: 18 + level * 280, y: 18 + index * 186 }
      : { x: 18 + index * 176, y: 18 + level * 186 });
  }
  const occupied = [];
  for (const [id, point] of graph.positions) {
    positions.set(id, { ...point });
    occupied.push(point);
  }
  for (const id of ids) {
    if (graph.positions.has(id)) continue;
    const point = positions.get(id);
    while (occupied.some(other => Math.abs(point.x - other.x) < DIAGRAM_CARD.width
        && Math.abs(point.y - other.y) < DIAGRAM_CARD.height)) {
      if (graph.horizontal) point.y += 186;
      else point.x += 176;
    }
    occupied.push(point);
  }
  return positions;
}
