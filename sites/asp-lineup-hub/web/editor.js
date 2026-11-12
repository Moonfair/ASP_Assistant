let editorCatalogRequest, selfSelectedCatalogRequest;

function getEditorCatalog() {
  editorCatalogRequest ??= api('editor-catalog').catch(error => {
    editorCatalogRequest = null;
    throw error;
  });
  return editorCatalogRequest;
}

function getSelfSelectedCatalog() {
  selfSelectedCatalogRequest ??= api('self-selected-operators').catch(error => {
    selfSelectedCatalogRequest = null;
    throw error;
  });
  return selfSelectedCatalogRequest;
}

async function getEditorCatalogForNames(names) {
  const catalog = await getEditorCatalog();
  const needsSelfSelected = names.some(name => !Object.hasOwn(catalog.operators, name));
  const extra = needsSelfSelected ? await getSelfSelectedCatalog() : {};
  return { ...catalog, operators: { ...catalog.operators, ...extra } };
}

async function showLineupEditor(lineup = null, revision = null, draft = null, openImport = false) {
  const version = ++modalVersion;
  activeDetail = lineup?.id ?? null;
  setDetailUrl(activeDetail);
  const title = lineup ? '编辑阵容' : '发布阵容';
  modalShell(title, '<p class="intro">正在载入干员、装备和策略…</p>', 'LINEUP EDITOR');
  try {
    const content = draft ?? (lineup ? parseLineupShare(lineup.share).content : {
      Name: '', Author: '', Description: '', Members: []
    });
    const catalog = await getEditorCatalogForNames([
      ...content.Members.map(member => member.OperatorName), ...descriptionOperatorNames(content.Description)
    ]);
    if (version !== modalVersion) return;
    renderLineupEditor(title, catalog, content, lineup, revision, openImport);
  } catch (error) {
    if (version !== modalVersion) return;
    modalShell(title, `<p class="form-error" role="alert">${escape(error.message)}</p>
      <button type="button" class="button secondary" id="retry-editor">重试</button>`, 'LINEUP EDITOR');
    $('#retry-editor').addEventListener('click', () => showLineupEditor(lineup, revision, draft, openImport));
  }
}

function renderLineupEditor(title, catalog, draft, lineup, revision, openImport) {
  let selfSelectedLoaded = Object.values(catalog.operators).some(op => op.selfSelected);
  const equipment = Object.entries(catalog.equipment).sort(compareCatalogEntries);
  const strategyCatalog = new Map(Object.entries(catalog.strategies ?? {}));
  const equipmentOptions = equipment.map(([name, item]) =>
    `<option value="${escape(name)}">${escape(name)} · ${item.tier} 阶</option>`).join('');
  const covenantOptions = Object.keys(catalog.covenants).map(name =>
    `<option value="${escape(name)}">${escape(name)}</option>`).join('');
  const action = lineup ? 'edit' : 'publish';
  const openAgain = () => showLineupEditor(lineup, revision, draft);
  modalShell(title, `<form class="lineup-editor" id="lineup-editor">
    <p class="intro">在网页组建阵容，或粘贴阵容短链接继续编辑。</p>
    <details class="editor-import" ${openImport ? 'open' : ''}>
      <summary>导入阵容短链接</summary>
      <label class="upload-area">${icon('upload')} 选择阵容文件<input class="visually-hidden" type="file"
        id="editor-file" accept=".txt,.aspa,.json,text/plain,application/json"></label>
      <label for="editor-import-text">阵容短链接</label>
      <input class="field" id="editor-import-text" type="text" inputmode="url" maxlength="256" spellcheck="false"
        placeholder="粘贴阵容短链接">
      <button type="button" class="button secondary" id="editor-import">导入并填入编辑器</button>
      <p class="editor-feedback" id="editor-import-status" role="status" hidden></p>
    </details>
    <div class="editor-meta">
      <label>阵容名称<input class="field" name="Name" maxlength="80" required></label>
      <label>作者<input class="field" name="Author" maxlength="80" required></label>
    </div>
    <section class="original-video" aria-labelledby="editor-original-video-title">
      <h3 id="editor-original-video-title">攻略来源</h3>
      <div class="editor-meta">
        <label>原作者<input class="field" name="originalAuthor" maxlength="${DESCRIPTION_LIMIT}"></label>
        <label>来源链接<input class="field" name="originalVideoUrl" type="text" inputmode="url"
          spellcheck="false" maxlength="${DESCRIPTION_LIMIT}" aria-describedby="editor-original-video-status"></label>
      </div>
      <a class="button secondary compact" id="editor-original-video" target="_blank" rel="noopener noreferrer" hidden><span></span> ${icon('arrow')}</a>
      <p class="editor-feedback form-error" id="editor-original-video-status" role="status" hidden></p>
    </section>
    <details class="editor-covenants">
      <summary id="covenant-tag-summary">盟约标签</summary>
      <div class="editor-covenant-tags" id="editor-covenant-tags"></div>
    </details>
    <div class="editor-columns">
      <section class="editor-picker" aria-label="阵容目录">
        <div class="editor-tabs" role="tablist" aria-label="阵容目录">
          <button type="button" class="button secondary compact" role="tab" id="operator-tab"
            aria-selected="true" aria-controls="operator-panel">干员</button>
          <button type="button" class="button secondary compact" role="tab" id="strategy-tab"
            aria-selected="false" aria-controls="strategy-panel" tabindex="-1">可用策略</button>
        </div>
        <div role="tabpanel" id="operator-panel" aria-labelledby="operator-tab">
          <label>搜索干员<input class="field" name="operatorQuery" type="search" placeholder="干员名称或盟约"></label>
          <label>自选干员<select class="field" name="operatorSelfSelected">
            <option value="no">否</option><option value="yes">是</option></select></label>
          <label>核心盟约<select class="field" name="operatorCovenant">
            <option value="">全部盟约</option>${covenantOptions}</select></label>
          <p class="editor-count" id="picker-count" role="status"></p>
          <div class="editor-operators" id="editor-operators" role="region" aria-label="匹配干员" tabindex="0"></div>
        </div>
        <div role="tabpanel" id="strategy-panel" aria-labelledby="strategy-tab" hidden>
          <label>搜索策略<input class="field" name="strategyQuery" type="search" placeholder="策略名称或发起人"></label>
          <p class="editor-count" id="strategy-count" role="status"></p>
          <div class="editor-strategies" id="editor-strategies"></div>
        </div>
      </section>
      <section class="editor-roster" aria-labelledby="editor-roster-title">
        <h3 id="editor-roster-title">已选干员 <span class="editor-count" id="editor-member-count" aria-live="polite"></span></h3>
        <div id="editor-members"></div>
      </section>
    </div>
    <div class="editor-description">
      <div>
        <label>阵容介绍<textarea class="field" name="Description" maxlength="${DESCRIPTION_LIMIT}" rows="9"
          placeholder="填写打法心得，支持 Markdown 标题和图标标记"></textarea></label>
        <div class="editor-insert-strategy">
          <select class="field" name="strategyIcon" aria-label="选择策略头像"></select>
          <button type="button" class="button secondary compact" id="insert-strategy">插入策略头像</button>
        </div>
      </div>
      ${diagramEditorMarkup()}
    </div>
    <div class="action-line">
      <button type="button" class="button secondary" id="editor-copy">${icon('copy')} 复制短链接</button>
      <button type="button" class="button secondary" id="editor-download">下载分享文件</button>
    </div>
    <input class="field" id="editor-export" aria-label="生成的阵容短链接" readonly hidden>
    <p class="editor-feedback" id="editor-feedback" role="status" hidden></p>
    <div class="editor-publish">
      <p class="intro">发布后，阵容名称、作者和内容对所有访客可见。</p>
      ${state.user ? challengeMarkup('editor') : ''}
      <p class="form-error" data-error role="alert" hidden></p>
      <div class="action-line">${state.user ? `
        <button type="button" class="button secondary" data-verify>获取图片验证码</button>
        <button type="submit" class="button primary" disabled>${lineup ? '保存修改' : '确认发布'}</button>` : `
        <button type="button" class="button primary" id="editor-login">登录后发布</button>`}
        ${lineup ? '<button type="button" class="button secondary" id="editor-back">返回阵容</button>' : ''}
      </div>
    </div>
  </form>`, 'LINEUP EDITOR');

  const form = $('#lineup-editor');
  const feedback = $('#editor-feedback', form);
  const importStatus = $('#editor-import-status', form);
  const output = $('#editor-export', form);
  const picker = $('#editor-operators', form);
  const strategies = $('#editor-strategies', form);
  const covenantTags = $('#editor-covenant-tags', form);
  const covenantTagSummary = $('#covenant-tag-summary', form);
  const strategyPicker = form.elements.strategyIcon;
  const insertStrategy = $('#insert-strategy', form);
  const tabs = [...form.querySelectorAll('.editor-tabs [role="tab"]')];
  const members = $('#editor-members', form);
  const challenge = state.user ? bindChallenge(form, action, lineup?.id ?? '', openAgain) : null;
  const fields = ['Name', 'Author'];
  const diagram = mountDiagramEditor(form, catalog, draft);
  let pending = false;
  let importRequest = 0;

  function report(target, message, failed = false) {
    target.textContent = message;
    target.hidden = !message;
    target.classList.toggle('form-error', failed);
  }

  function toShare() {
    const content = { ...draft, Name: draft.Name.trim(), Author: draft.Author.trim() };
    const share = serializeLineupShare(content);
    validateLineupCatalog(content, catalog.operators, catalog.equipment);
    return share;
  }

  async function loadSelfSelected() {
    if (selfSelectedLoaded) return;
    Object.assign(catalog.operators, await getSelfSelectedCatalog());
    selfSelectedLoaded = true;
  }

  function showSelfSelected() {
    return form.elements.operatorSelfSelected.value === 'yes';
  }

  function needsSelfSelected() {
    return showSelfSelected() || Boolean(form.elements.operatorQuery.value.trim());
  }

  async function refreshPicker() {
    renderPicker();
    if (!needsSelfSelected() || selfSelectedLoaded) return;
    try {
      await loadSelfSelected();
      if (form.isConnected) {
        renderPicker();
        diagram.refreshMembers();
      }
    } catch (error) {
      if (form.isConnected && needsSelfSelected())
        $('#picker-count', form).textContent = '自选干员加载失败。' + error.message;
    }
  }

  function renderPicker() {
    const query = form.elements.operatorQuery.value.trim().normalize('NFKC').toLowerCase();
    const covenant = form.elements.operatorCovenant.value;
    const selected = new Set(draft.Members.map(member => member.OperatorName));
    const matches = Object.entries(catalog.operators).filter(([name, op]) => !selected.has(name)
      && (query || Boolean(op.selfSelected) === showSelfSelected())
      && (!covenant || op.covenants.includes(covenant))
      && [name, ...op.covenants, ...(op.additionalCovenants ?? [])].join(' ').normalize('NFKC').toLowerCase().includes(query))
      .sort((first, second) => Number(Boolean(first[1].selfSelected)) - Number(Boolean(second[1].selfSelected))
        || compareCatalogEntries(first, second));
    const visible = query || showSelfSelected() ? matches : matches.slice(0, 8);
    $('#picker-count', form).textContent = `共 ${matches.length} 名` + (visible.length < matches.length ? '，显示前 8 名' : '');
    picker.innerHTML = visible.map(([name, op]) => `
      <button type="button" class="editor-operator" data-add-operator="${escape(name)}"
        ${draft.Members.length >= LINEUP_LIMIT ? 'disabled' : ''}>
        ${portrait({ name, image: op.image })}<span><strong>${escape(name)}</strong>
        <small>${escape([op.tier ? op.tier + ' 阶' : '特殊', ...op.covenants,
          ...(op.selfSelected ? op.additionalCovenants ?? [] : [])].join(' · '))}</small></span>
        ${icon('plus')}
      </button>`).join('');
  }

  function refreshStrategySelection() {
    const selected = new Set(draft.AvailableStrategies ?? []);
    $('#strategy-count', form).textContent = `已选 ${selected.size} 项`;
    const current = strategyPicker.value;
    const names = [...strategyCatalog.keys()].sort((a, b) => Number(selected.has(b)) - Number(selected.has(a)));
    strategyPicker.innerHTML = '<option value="">选择策略</option>' + names.map(name =>
      `<option value="${escape(name)}">${escape(name)}</option>`).join('');
    strategyPicker.value = strategyCatalog.has(current) ? current : '';
    insertStrategy.disabled = !strategyPicker.value;
  }

  function renderStrategies() {
    const selected = new Set(draft.AvailableStrategies ?? []);
    const choices = new Set([...selected, ...strategyCatalog.keys()]);
    const query = form.elements.strategyQuery.value.trim().normalize('NFKC').toLowerCase();
    strategies.innerHTML = [...choices].filter(name => {
      const sponsor = strategyCatalog.get(name)?.sponsor ?? '';
      return (name + ' ' + sponsor).normalize('NFKC').toLowerCase().includes(query);
    }).map(name => {
      const item = strategyCatalog.get(name);
      return `<label class="editor-operator editor-strategy-choice" title="${escape(item?.description ?? '当前策略目录中缺失')}">
        <input type="checkbox" value="${escape(name)}" ${selected.has(name) ? 'checked' : ''}>
        ${portrait({ name, image: item?.image })}<span><strong>${escape(name)}</strong>
        <small>${escape(item?.sponsor ?? '当前目录中缺失')}</small></span></label>`;
    }).join('') || '<p class="editor-empty">没有匹配的策略。</p>';
  }

  function selectPickerTab(active) {
    for (const tab of tabs) {
      tab.setAttribute('aria-selected', String(tab === active));
      tab.tabIndex = tab === active ? 0 : -1;
      $('#' + tab.getAttribute('aria-controls'), form).hidden = tab !== active;
    }
  }

  function renderMembers() {
    $('#editor-member-count', form).textContent = `${draft.Members.length} / ${LINEUP_LIMIT}`;
    members.innerHTML = draft.Members.map((member, index) => {
      const name = member.OperatorName;
      const op = Object.hasOwn(catalog.operators, name) ? catalog.operators[name] : null;
      const nameLabel = escape(name);
      const slots = Array.from({ length: op?.equipmentSlotCount ?? 2 }, (_, index) => index + 1).map(slot => {
        const value = member['Equipment' + slot];
        const missing = value && !Object.hasOwn(catalog.equipment, value);
        return `<label>装备 ${slot}<select class="field" data-member-field="Equipment${slot}"
          aria-label="${nameLabel} 装备 ${slot}">
          <option value="">无装备</option>${missing ? `<option value="${escape(value)}">${escape(value)}（目录中缺失）</option>` : ''}
          ${equipmentOptions}</select></label>`;
      }).join('');
      return `<article class="editor-member" data-member="${index}">
        <div class="editor-member-head">${portrait({ name, image: op?.image })}
          <strong>${nameLabel}${op ? '' : '（目录中缺失）'}</strong>
          <button type="button" class="button secondary compact" data-remove-operator
            aria-label="移除${nameLabel}">移除</button>
        </div>
        <div class="editor-roles">
          <label><input type="checkbox" data-member-field="IsDamageCore" ${member.IsDamageCore ? 'checked' : ''}> 输出核心</label>
          <label><input type="checkbox" data-member-field="IsStackingCore" ${member.IsStackingCore ? 'checked' : ''}> 叠层核心</label>
          ${op && (op.tier > 0 || op.selfSelected) ? `<label><input type="checkbox" data-member-field="IsElite" ${member.IsElite ? 'checked' : ''}> 精锐</label>` : ''}
        </div>
        <div class="editor-slots">${slots}</div>
      </article>`;
    }).join('') || `<p class="editor-empty">从干员目录添加 1–${LINEUP_LIMIT} 名干员。</p>`;
    members.querySelectorAll('[data-member]').forEach((card, index) => {
      for (const select of card.querySelectorAll('select')) select.value = draft.Members[index][select.dataset.memberField] ?? '';
    });
    diagram.refreshMembers();
    renderPicker();
  }

  function refreshCovenantTagSummary() {
    const count = draft.CovenantTags?.length ?? 0;
    covenantTagSummary.textContent = count ? `盟约标签（已选 ${count}）` : '盟约标签';
  }

  function renderCovenantTags() {
    const selected = new Set(draft.CovenantTags ?? []);
    covenantTags.innerHTML = [...new Set([...selected, ...catalog.covenantTags])].map(name =>
      `<label><input type="checkbox" value="${escape(name)}" ${selected.has(name) ? 'checked' : ''}> ${escape(name)}</label>`
    ).join('');
    refreshCovenantTagSummary();
  }

  function fillFields() {
    for (const field of fields) form.elements[field].value = draft[field] ?? '';
    diagram.load(draft.Description);
    renderMembers();
    renderStrategies();
    refreshStrategySelection();
    renderCovenantTags();
  }

  async function importShare(text, request) {
    const { content } = parseLineupShare(text);
    const names = [...content.Members.map(member => member.OperatorName), ...descriptionOperatorNames(content.Description)];
    if (names.some(name => !Object.hasOwn(catalog.operators, name)))
      await loadSelfSelected();
    if (!form.isConnected || request !== importRequest) return;
    validateLineupCatalog(content, catalog.operators, catalog.equipment);
    Object.assign(draft, { Description: '', AvailableStrategies: [], CovenantTags: [], ...content });
    fillFields();
    report(importStatus, '阵容已导入，可继续编辑。');
  }

  for (const field of fields) form.elements[field].addEventListener('input', event => { draft[field] = event.target.value; });
  form.elements.operatorQuery.addEventListener('input', refreshPicker);
  form.elements.operatorSelfSelected.addEventListener('change', refreshPicker);
  form.elements.operatorCovenant.addEventListener('change', renderPicker);
  for (const tab of tabs) {
    tab.addEventListener('click', () => selectPickerTab(tab));
    tab.addEventListener('keydown', event => {
      if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
      event.preventDefault();
      const next = event.key === 'Home' ? tabs[0] : event.key === 'End' ? tabs.at(-1) : tabs.find(other => other !== tab);
      selectPickerTab(next);
      next.focus();
    });
  }
  form.elements.strategyQuery.addEventListener('input', renderStrategies);
  covenantTags.addEventListener('change', event => {
    const selected = new Set(draft.CovenantTags ?? []);
    const { value, checked } = event.target;
    if (checked) selected.add(value);
    else selected.delete(value);
    draft.CovenantTags = [...selected];
    refreshCovenantTagSummary();
  });
  strategies.addEventListener('change', event => {
    const selected = new Set(draft.AvailableStrategies ?? []);
    const { value, checked } = event.target;
    if (checked) selected.add(value);
    else selected.delete(value);
    draft.AvailableStrategies = [...selected];
    refreshStrategySelection();
  });
  strategyPicker.addEventListener('change', () => { insertStrategy.disabled = !strategyPicker.value; });
  insertStrategy.addEventListener('click', () => {
    const name = strategyPicker.value;
    if (!strategyCatalog.has(name)) return;
    const prose = form.elements.Description;
    prose.setRangeText('{{strategy:' + name + '}}', prose.selectionStart, prose.selectionEnd, 'end');
    prose.dispatchEvent(new Event('input', { bubbles: true }));
    prose.focus({ preventScroll: true });
  });
  picker.addEventListener('click', event => {
    const name = event.target.closest('[data-add-operator]')?.dataset.addOperator;
    if (!name || draft.Members.length >= LINEUP_LIMIT || draft.Members.some(member => member.OperatorName === name)) return;
    draft.Members.push({ OperatorName: name, IsStackingCore: false, IsDamageCore: false, Equipment1: '', Equipment2: '' });
    renderMembers();
    form.elements.operatorQuery.focus({ preventScroll: true });
  });
  members.addEventListener('click', event => {
    const button = event.target.closest('[data-remove-operator]');
    if (!button) return;
    const index = Number(button.closest('[data-member]').dataset.member);
    draft.Members.splice(index, 1);
    renderMembers();
    (members.querySelectorAll('[data-remove-operator]')[Math.min(index, draft.Members.length - 1)]
      ?? form.elements.operatorQuery).focus({ preventScroll: true });
  });
  members.addEventListener('change', event => {
    const control = event.target;
    const field = control.dataset.memberField;
    if (!field) return;
    const index = Number(control.closest('[data-member]').dataset.member);
    if (['Equipment3', 'Equipment4'].includes(field) && !control.value) delete draft.Members[index][field];
    else if (field === 'IsElite' && !control.checked) delete draft.Members[index][field];
    else draft.Members[index][field] = control.type === 'checkbox' ? control.checked : control.value;
    if (control.tagName === 'SELECT') diagram.refreshMembers();
  });
  $('#editor-import', form).addEventListener('click', async () => {
    const request = ++importRequest;
    report(importStatus, '正在读取阵容…');
    try {
      const path = shortLinkPath($('#editor-import-text', form).value, location.origin);
      if (!path) throw new Error('请粘贴本站生成的阵容短链接。');
      const { share } = await api('share/' + path.slice(3));
      await importShare(share, request);
    }
    catch (failure) { if (request === importRequest) report(importStatus, failure.message, true); }
  });
  $('#editor-file', form).addEventListener('change', async event => {
    const input = event.target;
    const file = input.files[0];
    input.value = '';
    if (!file) return;
    const request = ++importRequest;
    try {
      if (file.size > 350_000) throw new Error('文件过大，请选择 ASP Assistant 导出的文本文件。');
      const value = await file.text();
      if (form.isConnected && request === importRequest) await importShare(value, request);
    } catch (failure) { if (request === importRequest) report(importStatus, failure.message, true); }
  });
  $('#editor-copy', form).addEventListener('click', async (event) => {
    const button = event.currentTarget;
    button.disabled = true;
    output.hidden = true;
    try {
      const link = api('share', { share: toShare() }).then(({ shortPath }) => new URL(shortPath, location.origin).href);
      try {
        await copyPendingShortLink(link);
        report(feedback, '已复制短链接，90 天内有效。');
      } catch {
        output.value = await link;
        output.hidden = false;
        output.select();
        report(feedback, '短链接已生成，请手动复制。');
      }
    } catch (failure) { report(feedback, failure.message, true); }
    finally { button.disabled = false; }
  });
  $('#editor-download', form).addEventListener('click', () => {
    try {
      const url = URL.createObjectURL(new Blob([toShare()], { type: 'text/plain;charset=utf-8' }));
      const link = document.createElement('a');
      link.href = url;
      link.download = 'asp-assistant-lineup.aspa';
      link.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
      report(feedback, '分享文件已生成。');
    } catch (failure) { report(feedback, failure.message, true); }
  });
  $('#editor-login', form)?.addEventListener('click', () => showAuth(openAgain, openAgain));
  $('#editor-back', form)?.addEventListener('click', () => showDetail(lineup.id));
  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (!challenge || pending || !challenge.payload().id) return;
    challenge.message('');
    try {
      const share = toShare();
      pending = true;
      challenge.submit.disabled = true;
      const { id } = await api(lineup ? 'lineups/' + lineup.id : 'lineups', {
        share, revision, captcha: challenge.payload()
      }, lineup ? 'PATCH' : 'POST');
      if (!lineup && form.isConnected) resetFilters();
      else loadLineups();
      if (form.isConnected) showDetail(id, lineup ? '阵容已更新' : '阵容已发布');
    } catch (failure) {
      if (!form.isConnected || handleExpiredSession(failure, openAgain)) return;
      challenge.message(failure.message);
      if (failure.code === 'captcha') await challenge.reload();
      else challenge.submit.disabled = false;
    } finally { pending = false; }
  });
  fillFields();
  if (openImport) $('#editor-import-text', form).focus();
}

function compareCatalogEntries([a, first], [b, second]) {
  return first.tier - second.tier || a.localeCompare(b, 'zh-CN');
}
