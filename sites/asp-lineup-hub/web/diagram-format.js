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

export function findDiagramBlocks(source) {
  const lines = [...source.matchAll(/([^\r\n]*)(?:\r\n|\r|\n|$)/g)];
  const blocks = [];
  let unclosed = false;
  for (let index = 0; index < lines.length; index++) {
    const open = lines[index][1].trim();
    if (!open.startsWith(diagramFence)) continue;
    const start = lines[index].index;
    const kind = open.slice(3).trim();
    const body = [];
    while (++index < lines.length && lines[index][1].trim() !== diagramFence) body.push(lines[index][1]);
    const closed = index < lines.length;
    unclosed ||= !closed;
    if (kind === 'lineup' || kind === 'mermaid') {
      const end = closed ? lines[index].index + lines[index][1].length : source.length;
      blocks.push({ start, end, kind, lines: body, closed });
    }
  }
  return { blocks, unclosed };
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
