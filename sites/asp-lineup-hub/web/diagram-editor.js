const diagramWidth = DIAGRAM_CARD.width;
const diagramHeight = DIAGRAM_CARD.height;
let diagramArrowSequence = 0;

function diagramBounds(positions) {
  return [...positions.values()].reduce((bounds, point) => ({
    width: Math.max(bounds.width, point.x + diagramWidth + 18),
    height: Math.max(bounds.height, point.y + diagramHeight + 18)
  }), { width: 184, height: 154 });
}

function diagramEdgeRoute(from, to, horizontal, manual, lane) {
  const dx = to.x - from.x, dy = to.y - from.y;
  const w = diagramWidth, h = diagramHeight;
  if (manual && (Math.abs(dx) > w || Math.abs(dy) > h)) {
    const t = 0.5 / Math.max(Math.abs(dx) / w, Math.abs(dy) / h);
    return [[from.x + w / 2 + dx * t, from.y + h / 2 + dy * t],
      [to.x + w / 2 - dx * t, to.y + h / 2 - dy * t]];
  }
  if (horizontal) {
    const a = [from.x + w, from.y + h / 2], b = [to.x, to.y + h / 2];
    return dx === 280 ? [a, b] : [a, [a[0] + 8, a[1]], [a[0] + 8, lane], [b[0] - 8, lane], [b[0] - 8, b[1]], b];
  }
  const a = [from.x + w / 2, from.y + h], b = [to.x + w / 2, to.y];
  return dy === 186 ? [a, b] : [a, [a[0], a[1] + 10], [lane, a[1] + 10], [lane, b[1] - 10], [b[0], b[1] - 10], b];
}

function diagramScene(graph, positions, catalog, arrowId, selection = null, connecting = null, dragged = null, editable = false) {
  const bounds = diagramBounds(positions);
  let outside = 0;
  const edges = graph.edges.map(({ from, to, label }, index) => {
    const lane = (graph.horizontal ? bounds.height : bounds.width) + 6 + outside * 18;
    const points = diagramEdgeRoute(positions.get(from), positions.get(to), graph.horizontal,
      graph.positions.has(from) || graph.positions.has(to) || dragged === from || dragged === to, lane);
    if (points.length > 2) outside++;
    const path = points.map(pair => pair.join(',')).join(' ');
    const a = points[Math.floor((points.length - 1) / 2)], b = points[Math.floor(points.length / 2)];
    const name = graph.nodes.get(from).name + ' → ' + graph.nodes.get(to).name;
    return `<g class="diagram-edge${selection?.edge === index ? ' selected' : ''}" data-edge="${index}"
        ${editable ? `role="button" tabindex="0" aria-label="${escape(name)}"` : ''}>
        <title>${escape(label ? name + ' · ' + label : name)}</title>
        <polyline class="diagram-edge-line" points="${path}" marker-end="url(#${arrowId})"/>
        ${editable ? `<polyline class="diagram-edge-hit" points="${path}"/>` : ''}
        ${label ? `<text x="${(a[0] + b[0]) / 2}" y="${(a[1] + b[1]) / 2}" class="diagram-edge-label">${escape(diagramShortText(label, 14))}</text>` : ''}
      </g>`;
  }).join('');
  if (outside) {
    if (graph.horizontal) bounds.height += 28 + outside * 18;
    else bounds.width += 50 + outside * 18;
  }
  const nodes = [...graph.nodes.values()].map(({ id, kind, name }) => {
    const { x, y } = positions.get(id);
    const item = kind === 'gear' ? catalog.equipment[name] : catalog.operators[name];
    const note = graph.notes.get(id) ?? '';
    const notes = Array.from(note);
    const lines = [notes.slice(0, 12).join(''), diagramShortText(notes.slice(12).join(''), 12)].filter(Boolean);
    return `<g class="diagram-node${graph.highlights.has(id) ? ' highlighted' : ''}${selection?.node === id ? ' selected' : ''}${connecting === id ? ' connecting' : ''}"
        transform="translate(${x} ${y})" data-node="${id}" ${editable ? `role="button" tabindex="0" aria-label="${escape(name)}"` : ''}>
      <title>${escape(note ? name + ' · ' + note : name)}</title>
      <rect class="diagram-card" width="${diagramWidth}" height="${diagramHeight}" rx="7"/>
      ${item?.image ? `<image x="52" y="9" width="44" height="44" href="${escape(item.image)}"/>`
        : `<text class="diagram-placeholder" x="74" y="42">${kind === 'gear' ? '装备' : '干员'}</text>`}
      <text class="diagram-name" x="74" y="70">${escape(diagramShortText(name, 9))}</text>
      ${lines.map((line, index) => `<text class="diagram-note" x="74" y="${88 + index * 15}">${escape(line)}</text>`).join('')}
      </g>`;
  }).join('');
  return { bounds, markup: `<defs><marker id="${arrowId}" viewBox="0 0 8 8" refX="8" refY="4"
    markerWidth="8" markerHeight="8" orient="auto" markerUnits="userSpaceOnUse"><path d="M0 0 8 4 0 8Z"/></marker></defs>${edges}${nodes}` };
}

function diagramShortText(text, limit) {
  const chars = Array.from(text);
  return chars.length <= limit ? text : chars.slice(0, limit - 1).join('') + '…';
}

function diagramEditorMarkup() {
  return `<section class="diagram-editor" aria-labelledby="diagram-title">
    <div class="diagram-heading"><h3 id="diagram-title">阵容图</h3>
      <button type="button" class="button secondary compact" data-diagram="add">新增流程图</button>
      <button type="button" class="button secondary compact" data-diagram="source" aria-expanded="false">源码</button></div>
    <div data-diagram-source hidden>
      <label>流程图源码<textarea class="field" id="diagram-source" rows="8" spellcheck="false" maxlength="${DESCRIPTION_LIMIT}"></textarea></label>
    </div>
    <label id="diagram-picker-label" hidden>流程图<select id="diagram-select" class="field"></select></label>
    <div data-diagram-editor hidden>
      <div class="diagram-tools">
        <label>方向<select id="diagram-direction" class="field"><option value="TD">从上到下</option><option value="LR">从左到右</option></select></label>
      </div>
      <div class="diagram-tools diagram-add">
        <label>干员<select id="diagram-operator" class="field"></select></label>
        <button type="button" class="button secondary compact" data-diagram="operator">添加干员</button>
        <label>装备<select id="diagram-gear" class="field"></select></label>
        <button type="button" class="button secondary compact" data-diagram="gear">添加装备</button>
      </div>
      <div class="diagram-viewport">
        <svg id="diagram-canvas" xmlns="http://www.w3.org/2000/svg" aria-label="流程图画布" preserveAspectRatio="none"></svg>
        <p id="diagram-empty">选择干员或装备添加节点。</p>
      </div>
      <div class="diagram-actions">
        <button type="button" class="button secondary compact" data-diagram="zoom-out" aria-label="缩小">−</button>
        <button type="button" class="button secondary compact" data-diagram="zoom-in" aria-label="放大">+</button>
        <button type="button" class="button secondary compact" data-diagram="fit">适应画布</button>
        <button type="button" class="button secondary compact" data-diagram="reset">自动布局</button>
      </div>
      <p class="editor-feedback">拖动节点调整位置，拖动画布空白处平移，双指可缩放。</p>
      <label id="diagram-edge-picker" hidden>选择连线<select class="field" id="diagram-edges"></select></label>
      <div class="diagram-inspector" id="diagram-inspector" hidden>
        <p id="diagram-selection"></p>
        <label><span id="diagram-property-label"></span><input id="diagram-property" class="field"></label>
        <label id="diagram-highlight-label"><input type="checkbox" id="diagram-highlight"> 高亮节点</label>
        <div class="diagram-actions">
          <button type="button" class="button secondary compact" data-diagram="connect">连线</button>
          <button type="button" class="button secondary compact" data-diagram="delete">删除选中项</button>
        </div>
      </div>
    </div>
    <p class="editor-feedback" id="diagram-status" role="status">新增流程图后，可拖动节点、拖动画布和双指缩放。</p>
  </section>`;
}

function mountDiagramEditor(form, catalog, draft) {
  const root = $('.diagram-editor', form);
  const queryDiagram = selector => $(selector, root);
  const source = queryDiagram('#diagram-source');
  const editor = queryDiagram('[data-diagram-editor]');
  const svg = queryDiagram('#diagram-canvas');
  const status = queryDiagram('#diagram-status');
  const picker = queryDiagram('#diagram-select');
  const direction = queryDiagram('#diagram-direction');
  const inspector = queryDiagram('#diagram-inspector');
  const property = queryDiagram('#diagram-property');
  const highlight = queryDiagram('#diagram-highlight');
  const prose = form.elements.Description;
  const originalAuthorInput = form.elements.originalAuthor;
  const originalVideoInput = form.elements.originalVideoUrl;
  const originalVideoLink = $('#editor-original-video', form);
  const originalVideoStatus = $('#editor-original-video-status', form);
  const arrowId = 'diagram-arrow-' + ++diagramArrowSequence;
  const view = { x: 0, y: 0, scale: 1 };
  let selected = null, connectFrom = null, index = 0;
  let graph = null, positions = new Map(), diagramSource = '', introduction = '';
  let originalAuthor = '', originalVideoUrl = '';
  let gesture = null, repaint = 0;
  const pointers = new Map();
  const control = action => queryDiagram(`[data-diagram="${action}"]`);

  function report(message, failed = false) {
    status.textContent = message;
    status.classList.toggle('form-error', failed);
  }

  function store(proseText, diagramText, author = originalAuthor, video = originalVideoUrl) {
    const description = combineLineupDescription(proseText, diagramText, author, video);
    if (description.length > DESCRIPTION_LIMIT) {
      report('阵容介绍、攻略来源和流程图合计最多支持 12,000 字。', true);
      return false;
    }
    introduction = proseText;
    diagramSource = diagramText;
    originalAuthor = author;
    originalVideoUrl = video;
    draft.Description = description;
    return true;
  }

  function updateOriginalVideoLink(message = '') {
    const href = lineupSourceHref(originalVideoUrl);
    if (href) {
      originalVideoLink.href = href;
      originalVideoLink.firstElementChild.textContent = isSklandGuideHref(href) ? '在森空岛打开' : '在哔哩哔哩观看';
    } else originalVideoLink.removeAttribute('href');
    originalVideoLink.hidden = !href;
    const invalidUrl = Boolean(originalVideoUrl.trim() && !href);
    originalVideoInput.setAttribute('aria-invalid', String(invalidUrl));
    originalVideoStatus.textContent = message || (invalidUrl ? '请输入有效的哔哩哔哩视频或森空岛卫戍协议攻略链接。' : '');
    originalVideoStatus.hidden = !originalVideoStatus.textContent;
  }

  function updateOriginalVideo() {
    const author = originalAuthorInput.value;
    const video = originalVideoInput.value;
    let message = '';
    if (/^`{3,}$/.test(author.trim()) || /^`{3,}$/.test(video.trim())) {
      message = '原作者和来源链接不能使用代码块结束标记。';
    } else if (!store(introduction, diagramSource, author, video)) {
      message = status.textContent;
      report('');
    }
    if (message) {
      originalAuthorInput.value = originalAuthor;
      originalVideoInput.value = originalVideoUrl;
    }
    updateOriginalVideoLink(message);
  }

  function blocksForEditing() {
    const result = findDiagramBlocks(diagramSource);
    if (!result.blocks.length && diagramSource.trim() && !/^[^\S\r\n]*```/m.test(diagramSource)) {
      result.blocks.push({ start: 0, end: diagramSource.length, kind: 'lineup',
        closed: true, lines: diagramSource.split(/\r\n|\n|\r/) });
    }
    return result;
  }

  function applyView() {
    const { width, height } = svg.getBoundingClientRect();
    if (width && height) svg.setAttribute('viewBox', `${view.x} ${view.y} ${width / view.scale} ${height / view.scale}`);
  }

  function fit() {
    const { width, height } = svg.getBoundingClientRect();
    if (!width || !height) return;
    const bounds = graph ? diagramScene(graph, positions, catalog, arrowId).bounds : diagramBounds(positions);
    view.scale = Math.min(1, Math.max(0.015, Math.min(width / bounds.width, height / bounds.height)));
    view.x = (bounds.width - width / view.scale) / 2;
    view.y = (bounds.height - height / view.scale) / 2;
    applyView();
  }

  function draw() {
    if (!graph) return;
    svg.innerHTML = diagramScene(graph, positions, catalog, arrowId, selected, connectFrom, gesture?.node, true).markup;
    queryDiagram('#diagram-empty').hidden = graph.nodes.size > 0;
    applyView();
  }

  function drawSoon() {
    if (repaint) return;
    repaint = requestAnimationFrame(() => { repaint = 0; draw(); });
  }

  function renderInspector() {
    const node = selected?.node ? graph.nodes.get(selected.node) : null;
    const edge = selected?.edge !== undefined ? graph.edges[selected.edge] : null;
    const edges = queryDiagram('#diagram-edges');
    edges.innerHTML = '<option value="">选择连线</option>' + graph.edges.map(({ from, to, label }, n) =>
      `<option value="${n}">${escape(graph.nodes.get(from).name + ' → ' + graph.nodes.get(to).name + (label ? ' · ' + label : ''))}</option>`).join('');
    edges.value = edge ? String(selected.edge) : '';
    queryDiagram('#diagram-edge-picker').hidden = !graph.edges.length;
    inspector.hidden = !node && !edge;
    control('reset').disabled = !graph.positions.size;
    control('operator').disabled = graph.nodes.size >= DIAGRAM_LIMITS.nodes;
    control('gear').disabled = graph.nodes.size >= DIAGRAM_LIMITS.nodes;
    if (!node && !edge) return;
    queryDiagram('#diagram-selection').textContent = node ? node.name : graph.nodes.get(edge.from).name + ' → ' + graph.nodes.get(edge.to).name;
    queryDiagram('#diagram-property-label').textContent = node ? '节点注释' : '连线文字';
    property.maxLength = node ? 60 : 40;
    property.value = node ? graph.notes.get(node.id) ?? '' : edge.label;
    queryDiagram('#diagram-highlight-label').hidden = !node;
    highlight.checked = Boolean(node && graph.highlights.has(node.id));
    control('connect').hidden = !node;
    control('connect').textContent = connectFrom ? '取消连线' : '连线';
  }

  function render(refit = false) {
    const { blocks, unclosed } = blocksForEditing();
    index = Math.min(index, Math.max(0, blocks.length - 1), DIAGRAM_LIMITS.diagrams - 1);
    const block = blocks[index];
    graph = null;
    try {
      if (block && !block.closed) throw new Error('流程图代码块缺少结束标记。');
      if (block) graph = parseDiagram(block.lines);
    } catch (error) { report(error.message + ' 可切换到源码修正。', true); }
    control('add').disabled = unclosed || blocks.length >= DIAGRAM_LIMITS.diagrams;
    if (!block && unclosed) report('请先补全流程图代码块的结束标记。', true);
    const sourceMode = !queryDiagram('[data-diagram-source]').hidden;
    editor.hidden = !graph || sourceMode;
    queryDiagram('#diagram-picker-label').hidden = !blocks.length || sourceMode;
    picker.innerHTML = blocks.slice(0, DIAGRAM_LIMITS.diagrams).map((_, n) =>
      `<option value="${n}">流程图 ${n + 1}</option>`).join('');
    picker.value = String(index);
    if (!graph) return;
    direction.value = graph.horizontal ? 'LR' : 'TD';
    positions = arrangeDiagram(graph);
    renderInspector();
    draw();
    if (refit) fit();
  }

  function commit() {
    const { blocks } = blocksForEditing();
    const block = blocks[index];
    const newline = diagramSource.includes('\r\n') ? '\r\n' : '\n';
    const next = diagramSource.slice(0, block.start) + formatDiagram(graph, block.kind, newline) + diagramSource.slice(block.end);
    if (store(introduction, next)) source.value = next;
    render();
  }

  function choose(target) {
    if (target?.node && connectFrom) {
      if (connectFrom === target.node) { report('请选择另一个节点作为连线终点。'); return; }
      const existing = graph.edges.findIndex(edge => edge.from === connectFrom && edge.to === target.node);
      if (existing !== -1) { choose({ edge: existing }); return; }
      if (graph.edges.length >= DIAGRAM_LIMITS.edges) { report('每张图最多支持 40 条连线。', true); return; }
      graph.edges.push({ from: connectFrom, to: target.node, label: '' });
      connectFrom = null;
      selected = { edge: graph.edges.length - 1 };
      report('连线已添加。');
      commit();
      if (!graph.positions.size) fit();
      return;
    }
    connectFrom = null;
    selected = target;
    report('');
    renderInspector();
    draw();
  }

  function refreshMembers() {
    for (const [selector, items, names] of [
      ['#diagram-operator', catalog.operators, draft.Members.map(member => member.OperatorName)],
      ['#diagram-gear', catalog.equipment, draft.Members.flatMap(memberEquipmentSlots)]
    ]) {
      const select = queryDiagram(selector);
      const selection = select.value;
      const own = [...new Set(names.filter(name => Object.hasOwn(items, name)))];
      const rest = Object.entries(items).sort(compareCatalogEntries).map(([name]) => name).filter(name => !own.includes(name));
      const options = values => values.map(name => `<option value="${escape(name)}">${escape(name)}</option>`).join('');
      select.innerHTML = (own.length ? `<optgroup label="当前阵容">${options(own)}</optgroup>` : '')
        + `<optgroup label="目录">${options(rest)}</optgroup>`;
      if (selection && Object.hasOwn(items, selection)) select.value = selection;
    }
  }

  function zoomAt(x, y, scale, anchor = null) {
    const rect = svg.getBoundingClientRect();
    const point = anchor ?? { x: view.x + (x - rect.left) / view.scale, y: view.y + (y - rect.top) / view.scale };
    view.scale = Math.min(2.5, Math.max(0.015, scale));
    view.x = point.x - (x - rect.left) / view.scale;
    view.y = point.y - (y - rect.top) / view.scale;
    applyView();
  }

  function centerZoom(factor) {
    const rect = svg.getBoundingClientRect();
    zoomAt(rect.left + rect.width / 2, rect.top + rect.height / 2, view.scale * factor);
  }

  function addNode(kind) {
    if (graph.nodes.size >= DIAGRAM_LIMITS.nodes) return;
    const name = queryDiagram(kind === 'gear' ? '#diagram-gear' : '#diagram-operator').value;
    if (!name) return;
    let number = 1;
    while (graph.nodes.has('N' + number)) number++;
    const id = 'N' + number;
    graph.nodes.set(id, { id, kind, name });
    selected = { node: id };
    connectFrom = null;
    commit();
    fit();
  }

  root.addEventListener('click', event => {
    const action = event.target.closest('[data-diagram]')?.dataset.diagram;
    if (!action) return;
    if (action === 'add') {
      const { blocks, unclosed } = blocksForEditing();
      if (unclosed || blocks.length >= DIAGRAM_LIMITS.diagrams) return;
      const existing = combineDiagramDescription('', diagramSource);
      const next = (existing ? existing + '\n\n' : '') + formatDiagram(parseDiagram([]));
      if (!store(introduction, next)) return;
      source.value = next;
      index = blocks.length;
      selected = connectFrom = null;
      report('');
      render(true);
    } else if (action === 'source') {
      const host = queryDiagram('[data-diagram-source]');
      host.hidden = !host.hidden;
      control('source').setAttribute('aria-expanded', String(!host.hidden));
      control('source').textContent = host.hidden ? '源码' : '画布';
      if (!host.hidden) source.focus();
      render(host.hidden);
    } else if (!graph) return;
    else if (action === 'operator' || action === 'gear') addNode(action === 'gear' ? 'gear' : 'op');
    else if (action === 'zoom-in' || action === 'zoom-out') centerZoom(action === 'zoom-in' ? 1.25 : 0.8);
    else if (action === 'fit') fit();
    else if (action === 'reset') { graph.positions.clear(); commit(); fit(); }
    else if (action === 'connect') {
      connectFrom = connectFrom ? null : selected.node;
      report(connectFrom ? '点选另一个节点作为连线终点。' : '');
      renderInspector();
      draw();
    } else if (action === 'delete') {
      if (selected?.node) {
        const id = selected.node;
        graph.nodes.delete(id);
        graph.edges = graph.edges.filter(edge => edge.from !== id && edge.to !== id);
        graph.highlights.delete(id);
        graph.notes.delete(id);
        graph.positions.delete(id);
      } else if (selected?.edge !== undefined) graph.edges.splice(selected.edge, 1);
      selected = connectFrom = null;
      commit();
    }
  });

  picker.addEventListener('change', () => { index = Number(picker.value); selected = connectFrom = null; report(''); render(true); });
  direction.addEventListener('change', () => { graph.horizontal = direction.value === 'LR'; commit(); fit(); });
  queryDiagram('#diagram-edges').addEventListener('change', event => {
    choose(event.target.value ? { edge: Number(event.target.value) } : null);
  });
  source.addEventListener('input', () => {
    if (!store(introduction, source.value)) { source.value = diagramSource; return; }
    selected = connectFrom = null;
    report('');
    render();
  });
  prose.addEventListener('input', () => {
    if (!store(prose.value, diagramSource)) prose.value = introduction;
    else if (status.classList.contains('form-error')) { report(''); render(); }
  });
  originalAuthorInput.addEventListener('input', updateOriginalVideo);
  originalVideoInput.addEventListener('input', updateOriginalVideo);
  property.addEventListener('keydown', event => {
    if (event.key === 'Enter') { event.preventDefault(); property.blur(); }
  });
  property.addEventListener('change', () => {
    if (!graph || !selected) return;
    const value = property.value.trim();
    if (selected.node) {
      if (value) graph.notes.set(selected.node, value);
      else graph.notes.delete(selected.node);
    } else if (value.includes('|')) {
      report('连线文字不能包含 |。', true);
      property.value = graph.edges[selected.edge].label;
      return;
    } else graph.edges[selected.edge].label = value;
    report('');
    commit();
  });
  highlight.addEventListener('change', () => {
    if (!selected?.node) return;
    if (highlight.checked) graph.highlights.add(selected.node);
    else graph.highlights.delete(selected.node);
    commit();
  });

  function hitTarget(element) {
    const node = element.closest('[data-node]');
    const edge = element.closest('[data-edge]');
    return node ? { node: node.dataset.node } : edge ? { edge: Number(edge.dataset.edge) } : null;
  }

  function pinchPoint() {
    const [a, b] = pointers.values();
    return { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2, distance: Math.max(1, Math.hypot(a.x - b.x, a.y - b.y)) };
  }

  svg.addEventListener('pointerdown', event => {
    if (!graph || pointers.size >= 2 || (event.button !== 0 && event.button !== 1)) return;
    event.preventDefault();
    const target = event.button === 1 ? null : hitTarget(event.target);
    if (document.activeElement === property) property.blur();
    svg.setPointerCapture(event.pointerId);
    const point = { x: event.clientX, y: event.clientY };
    pointers.set(event.pointerId, point);
    if (pointers.size === 2) {
      positions = arrangeDiagram(graph);
      const center = pinchPoint();
      const rect = svg.getBoundingClientRect();
      gesture = { pinch: true, scale: view.scale, distance: center.distance,
        anchor: { x: view.x + (center.x - rect.left) / view.scale, y: view.y + (center.y - rect.top) / view.scale } };
      drawSoon();
      return;
    }
    const node = connectFrom ? null : target?.node;
    gesture = { start: point, target, node, origin: node ? { ...positions.get(node) } : { x: view.x, y: view.y }, moved: false };
  });
  svg.addEventListener('pointermove', event => {
    if (!pointers.has(event.pointerId) || !gesture) return;
    const point = { x: event.clientX, y: event.clientY };
    pointers.set(event.pointerId, point);
    if (gesture.pinch && pointers.size === 2) {
      const center = pinchPoint();
      zoomAt(center.x, center.y, gesture.scale * center.distance / gesture.distance, gesture.anchor);
      return;
    }
    const dx = point.x - gesture.start.x, dy = point.y - gesture.start.y;
    if (!gesture.moved && Math.hypot(dx, dy) < 5) return;
    gesture.moved = true;
    if (gesture.node) {
      const clamp = value => Math.max(0, Math.min(DIAGRAM_LIMITS.position, Math.round(value)));
      positions.set(gesture.node, { x: clamp(gesture.origin.x + dx / view.scale), y: clamp(gesture.origin.y + dy / view.scale) });
      drawSoon();
    } else {
      view.x = gesture.origin.x - dx / view.scale;
      view.y = gesture.origin.y - dy / view.scale;
      applyView();
    }
  });
  function endPointer(event, canceled) {
    if (!pointers.delete(event.pointerId) || !gesture) return;
    if (pointers.size) {
      gesture = { start: pointers.values().next().value, origin: { x: view.x, y: view.y }, moved: true };
      return;
    }
    const ended = gesture;
    gesture = null;
    if (canceled) { positions = arrangeDiagram(graph); draw(); return; }
    if (ended.node && ended.moved) {
      // Freeze the other cards so exporting a drag cannot shift their displayed positions.
      graph.positions = new Map([...positions].map(([id, point]) => [id, { ...point }]));
      selected = { node: ended.node };
      commit();
    } else if (!ended.moved && !ended.pinch) choose(ended.target);
  }
  svg.addEventListener('pointerup', event => endPointer(event, false));
  svg.addEventListener('pointercancel', event => endPointer(event, true));
  svg.addEventListener('lostpointercapture', event => endPointer(event, true));
  svg.addEventListener('keydown', event => {
    if (!['Enter', ' '].includes(event.key)) return;
    event.preventDefault();
    choose(hitTarget(event.target));
  });
  svg.addEventListener('wheel', event => {
    if (!event.ctrlKey) return;
    event.preventDefault();
    zoomAt(event.clientX, event.clientY, view.scale * (event.deltaY < 0 ? 1.12 : 0.89));
  }, { passive: false });
  new ResizeObserver(() => applyView()).observe(svg);
  return {
    load(description) {
      const split = splitLineupDescription(description ?? '');
      prose.value = introduction = split.prose;
      source.value = diagramSource = split.diagrams;
      originalAuthorInput.value = originalAuthor = split.originalAuthor;
      originalVideoInput.value = originalVideoUrl = split.originalVideoUrl;
      updateOriginalVideoLink();
      selected = connectFrom = null;
      index = 0;
      report('');
      render(true);
    },
    refreshMembers
  };
}

async function renderPublishedDescription(container, { prose, diagrams }, operatorNames) {
  const { blocks } = findDiagramBlocks(diagrams);
  if (!blocks.length && !/\{\{(?:op|gear|strategy):/.test(prose)) return;
  const catalog = await getEditorCatalogForNames([
    ...operatorNames, ...descriptionOperatorNames(prose), ...descriptionOperatorNames(diagrams)
  ]).catch(() => ({ equipment: {}, operators: {} }));
  if (!container.isConnected) return;
  const plots = blocks.slice(0, DIAGRAM_LIMITS.diagrams).map((block, index) => {
    try {
      if (!block.closed) throw new Error('流程图代码块缺少结束标记。');
      const graph = parseDiagram(block.lines);
      if (!graph.nodes.size) return '';
      const arrowId = 'diagram-arrow-' + ++diagramArrowSequence;
      const { markup, bounds } = diagramScene(graph, arrangeDiagram(graph), catalog, arrowId);
      const title = blocks.length > 1 ? `流程图 ${index + 1}` : '流程图';
      return `<figure class="diagram-preview"><figcaption>${title}</figcaption><div class="diagram-preview-scroll">
        <svg xmlns="http://www.w3.org/2000/svg" role="img" aria-label="${title}" viewBox="0 0 ${bounds.width} ${bounds.height}"
          width="${bounds.width}" height="${bounds.height}">${markup}</svg></div></figure>`;
    } catch (error) {
      return `<p class="form-error">${escape(error.message)}</p><pre class="description">${escape(diagrams.slice(block.start, block.end))}</pre>`;
    }
  }).join('');
  container.innerHTML = (prose ? `<div class="description">${renderDescriptionProse(prose, catalog)}</div>` : '')
    + (plots ? `<div class="description-diagrams">${plots}</div>` : '');
}
