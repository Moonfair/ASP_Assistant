const $ = (selector, root = document) => root.querySelector(selector);
const icon = (name) => '<svg aria-hidden="true"><use href="#' + name + '"/></svg>';
const state = { covenant: '', page: 1, badges: {}, request: 0, user: null, mailAvailable: null };
const modal = $('#modal');
const modalContent = $('#modal-content');
const cards = $('#cards');
const query = $('#query');
const source = $('#source');
const sort = $('#sort');
const rated = $('#rated');
const count = $('#result-count');
const pages = $('#pagination');
const toastDownload = $('#toast-download');
toastDownload.href = ASP_DOWNLOAD_URL;
let activeDetail = null, modalVersion = 0, toastTimer, searchTimer;
let captchaQueue = Promise.resolve();
let openingDesktop = false;

async function api(path, body, method = 'POST') {
  const response = await fetch('/api/' + path, body === undefined ? {} : {
    method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
  });
  let data;
  try { data = await response.json(); }
  catch { throw new Error('暂时无法读取服务器响应，请稍后重试。'); }
  if (!response.ok) {
    const error = new Error(data.error || '提交失败，请稍后重试。');
    error.code = data.code;
    throw error;
  }
  return data;
}

function showToast(message, showDownload = false) {
  const toast = $('#toast');
  $('#toast-message').textContent = message;
  toastDownload.hidden = !showDownload;
  toast.hidden = false;
  clearTimeout(toastTimer);
  if (!showDownload) toastTimer = setTimeout(() => { toast.hidden = true; }, 3000);
}

async function useLineup(button, shortPath, status = showToast) {
  if (!isWindowsDesktop(navigator)) {
    status('当前平台暂不支持，请在 Windows PC 端使用。');
    return;
  }
  if (openingDesktop) return;
  openingDesktop = true;
  button.disabled = true;
  try {
    const link = new URL(shortPath, location.origin).href;
    const [uri] = await Promise.all([desktopImportUri(link), navigator.clipboard.writeText(link)]);
    if (!button.isConnected) return;
    status('阵容短链接已复制，正在打开 ASP Assistant…', true);
    const stayedOnPage = await openDesktopProtocol(uri);
    if (!button.isConnected) return;
    if (stayedOnPage) location.assign(ASP_DOWNLOAD_URL);
    else status('阵容短链接已复制。若客户端未打开，可前往官网下载。', true);
  } catch {
    if (button.isConnected) status('无法复制短链接，请检查剪贴板权限，或在阵容详情手动复制。');
  } finally {
    button.disabled = false;
    openingDesktop = false;
  }
}

function portrait(member) {
  const name = escape(member.name);
  return '<span class="portrait" title="' + name + '">' + (member.image
    ? '<img src="' + escape(member.image) + '" alt="' + name + '" loading="lazy">'
    : escape([...member.name][0])) + '</span>';
}

function renderActiveCovenants(covenants) {
  return `<div class="lineup-active-covenants" title="按阵容中列出的干员、精锐状态和装备计算">
    <span class="lineup-active-label">已激活盟约</span>
    ${covenants?.length ? covenants.map(name => `<span class="tag">${escape(name)}</span>`).join('') : '<span>无</span>'}
  </div>`;
}

function renderCard(lineup) {
  const faction = lineup.covenants[0] ?? '其他阵容';
  const badge = state.badges[faction];
  const attribution = lineup.attribution;
  const skland = attribution && isSklandGuideHref(attribution.url);
  const visible = lineup.members.slice(0, 5).map(portrait).join('');
  const more = lineup.members.length > 5 ? '<span class="portrait more">+' + (lineup.members.length - 5) + '</span>' : '';
  const strategies = lineup.strategyPortraits ?? [];
  const remainingStrategies = lineup.strategyCount - strategies.length;
  const score = lineup.rating === null ? '暂无评分' : lineup.rating.toFixed(1) + ' (' + lineup.ratingCount + ')';
  return `<article class="card"><a class="card-link" href="?lineup=${encodeURIComponent(lineup.id)}" data-lineup="${escape(lineup.id)}">
    <div class="card-band${attribution ? ' has-attribution' : ''}" data-theme="${escape(faction)}">
      <span class="tag">${escape(faction)}</span>
      ${attribution ? '<span class="card-source-badge' + (skland ? ' skland' : '') + '">' + (skland ? '转自森空岛' : '转自 B 站') + '</span>'
        : '<span class="band-code">' + (lineup.sample ? '示例阵容' : '玩家发布') + '</span>'}
      ${badge ? '<img class="band-badge" src="' + escape(badge) + '" alt="" loading="lazy">' : ''}
    </div>
    <div class="card-body"><h3>${escape(lineup.title)}</h3>
      ${attribution?.author === lineup.author ? '' : '<p class="byline">作者 · ' + escape(lineup.author) + '</p>'}
      <div class="members">${visible}${more}</div>
      ${renderActiveCovenants(lineup.activeCovenants)}
      ${strategies.length ? `<div class="card-strategies">
        <span class="card-strategies-label">可用策略</span>${strategies.map(portrait).join('')}
        ${remainingStrategies > 0 ? `<span class="portrait more" aria-label="另有 ${remainingStrategies} 项策略">+${remainingStrategies}</span>` : ''}
      </div>` : ''}
    </div></a>
    ${attribution ? `<div class="card-attribution">
      ${attribution.author ? '<span>原作者 · ' + escape(attribution.author) + '</span>' : ''}
      <a href="${escape(attribution.url)}" target="_blank" rel="noopener noreferrer">${skland ? '森空岛原攻略' : 'B 站原视频'} ${icon('arrow')}</a>
    </div>` : ''}
    <div class="card-foot"><span class="${lineup.rating === null ? '' : 'gold'}">${icon('star')}${score}</span><span aria-label="${lineup.commentCount} 条评论">${icon('chat')}${lineup.commentCount}</span><button class="text-button card-use" type="button" data-use-lineup="${escape(lineup.shortPath)}" aria-label="使用阵容：${escape(lineup.title)}">去使用</button></div>
  </article>`;
}

async function loadLineups() {
  const request = ++state.request;
  const params = new URLSearchParams({
    q: query.value.trim(), covenant: state.covenant, source: source.value,
    rated: rated.checked ? '1' : '0', sort: sort.value, page: state.page
  });
  cards.setAttribute('aria-busy', 'true');
  const active = $('#active-filter');
  active.textContent = state.covenant ? '已筛选 · ' + state.covenant : '';
  try {
    const { items, total, page, pageSize } = await api('lineups?' + params);
    if (request !== state.request) return;
    const max = Math.max(1, Math.ceil(total / pageSize));
    if (page > max) {
      state.page = max;
      return loadLineups();
    }
    count.innerHTML = '找到 <strong>' + total + '</strong> 套阵容';
    cards.innerHTML = items.length ? items.map(renderCard).join('') :
      '<div class="empty">' + icon('search') + '<h3>没有找到阵容</h3><p>试试其他关键词，或清除筛选条件。</p><button type="button" class="button secondary" data-reset>清除筛选</button></div>';
    pages.innerHTML = max > 1 ? `<button class="button secondary compact" type="button" data-page="${page - 1}" ${page <= 1 ? 'disabled' : ''}>上一页</button><span>第 ${page} / ${max} 页</span><button class="button secondary compact" type="button" data-page="${page + 1}" ${page >= max ? 'disabled' : ''}>下一页</button>` : '';
  } catch (error) {
    if (request !== state.request) return;
    count.textContent = '阵容暂时无法载入';
    cards.innerHTML = '<div class="empty"><p>' + escape(error.message) + '</p><button type="button" class="button secondary" data-retry>重新载入</button></div>';
    pages.replaceChildren();
  } finally {
    if (request === state.request) cards.setAttribute('aria-busy', 'false');
  }
}

function covenantButtons() {
  $('#covenants').innerHTML = [['', null], ...Object.entries(state.badges)].map(([covenant, badge]) => {
    const selected = covenant === state.covenant;
    return `<button type="button" class="${selected ? 'selected' : ''}" data-covenant="${escape(covenant)}" aria-pressed="${selected}">${badge ? '<img src="' + escape(badge) + '" alt="">' : icon('grid')}${escape(covenant || '全部盟约')}</button>`;
  }).join('');
}

function refreshResults() {
  clearTimeout(searchTimer);
  state.page = 1;
  loadLineups();
}

function resetFilters() {
  state.covenant = '';
  query.value = '';
  source.value = '';
  sort.value = 'latest';
  rated.checked = false;
  covenantButtons();
  refreshResults();
}

function setDetailUrl(id, shared = false) {
  const url = new URL(location.href);
  url.searchParams.delete('lineup');
  url.searchParams.delete('share');
  if (id) url.searchParams.set(shared ? 'share' : 'lineup', id);
  url.searchParams.delete('compose');
  history.replaceState(null, '', url);
}

function modalShell(title, content, eyebrow) {
  modalContent.innerHTML = `<header class="modal-header"><div><p class="eyebrow">${escape(eyebrow)}</p><h2 id="modal-title">${escape(title)}</h2></div><button type="button" class="icon-button" data-close aria-label="关闭">${icon('close')}</button></header><div class="modal-body">${content}</div>`;
  if (!modal.open) modal.showModal();
  modal.scrollTop = 0;
}

function showGuide() {
  activeDetail = null;
  modalVersion++;
  setDetailUrl(null);
  modalShell('如何发布阵容', `<p class="intro">可以用阵容短链接在网页和 ASP Assistant 之间分享阵容。</p>
    <ol class="guide-steps"><li>在 ASP Assistant 的阵容卡片上点击「分享」，复制短链接。</li>
    <li>点击「发布阵容」直接组建阵容，或在编辑器中粘贴短链接继续修改。</li>
    <li>输入图片验证码并发布。Windows PC 玩家可以点击「去使用」，在 ASP Assistant 中导入整套阵容。</li></ol>
    <button type="button" class="button primary" data-publish>${icon('plus')} 去发布</button>`, 'FIELD GUIDE');
}

function challengeMarkup(prefix) {
  return `<div class="challenge" data-challenge hidden>
    <div data-code-image class="challenge-loading">载入中…</div>
    <label for="${prefix}-code">图片验证码<input id="${prefix}-code" name="answer" class="field" required maxlength="5" minlength="5" pattern="[A-Za-z2-9]{5}" autocomplete="off" autocapitalize="characters" placeholder="输入 5 位字符" disabled></label>
    <button class="icon-button" type="button" data-reload aria-label="换一张验证码" title="换一张验证码">${icon('refresh')}</button>
  </div>`;
}

function bindChallenge(form, action, target = '', resume) {
  const row = $('[data-challenge]', form);
  const image = $('[data-code-image]', form);
  const input = form.elements.answer;
  const submit = $('[type="submit"]', form);
  const error = $('[data-error]', form);
  let challengeId = '';
  let loading = false;
  const message = (text) => { error.textContent = text; error.hidden = !text; };
  async function reload() {
    if (loading || !form.isConnected) return;
    loading = true;
    row.hidden = false;
    challengeId = '';
    submit.disabled = true;
    input.disabled = true;
    input.value = '';
    image.className = 'challenge-loading';
    image.textContent = '载入中…';
    const pending = captchaQueue.then(() => form.isConnected ? api('captcha', { action, target }) : null);
    captchaQueue = pending.catch(() => {});
    try {
      const data = await pending;
      if (!data || !form.isConnected) return;
      challengeId = data.id;
      const picture = new Image();
      picture.src = data.image;
      picture.alt = '由 5 个字符组成的验证码';
      image.className = '';
      image.replaceChildren(picture);
      input.disabled = false;
    } catch (failure) {
      if (!form.isConnected || (action !== 'login' && handleExpiredSession(failure, resume ?? (() => showDetail(target))))) return;
      image.textContent = '载入失败';
      message(failure.message);
    } finally {
      submit.disabled = !challengeId;
      loading = false;
    }
  }
  $('[data-reload]', form).addEventListener('click', () => { message(''); reload(); });
  $('[data-verify]', form)?.addEventListener('click', (event) => {
    event.currentTarget.hidden = true;
    reload().then(() => { if (!input.disabled) input.focus(); });
  });
  return { reload, message, submit, payload: () => ({ id: challengeId, answer: input.value }) };
}

function showPublish() { showLineupEditor(); }

const dateFormatter = new Intl.DateTimeFormat('zh-CN', { dateStyle: 'medium', timeStyle: 'short' });
function dateText(timestamp) { return dateFormatter.format(new Date(timestamp)); }

function renderRating(data) {
  const value = data.lineup.rating;
  $('#rating-info').innerHTML = value === null ? '暂无评分' :
    icon('star') + value.toFixed(1) + ' / 5 · ' + data.lineup.ratingCount + ' 人评分';
  $('#my-rating').textContent = !state.user ? '登录后可评分，评分会匿名显示。'
    : data.myScore === null ? '请选择 1–5 星，评分会匿名显示。' : '你已评 ' + data.myScore + ' 星，可以重新评分。';
  const picked = $('input[name="score"][value="' + data.myScore + '"]');
  if (picked) picked.checked = true;
}

function renderComments(data) {
  $('#comment-title').textContent = '匿名讨论 · ' + data.lineup.commentCount;
  $('#comment-list').innerHTML = data.comments.length ? data.comments.map(comment =>
    '<li><strong>匿名玩家</strong><time>' + escape(dateText(comment.created_at)) + '</time><p>' + escape(comment.text) + '</p></li>'
  ).join('') : '<li><p>还没有评论，分享你的阵容心得吧。</p></li>';
  const page = data.commentsPage;
  $('#comment-pages').innerHTML =
    (page > 1 ? '<button type="button" class="text-button" data-comments-page="' + (page - 1) + '">上一页</button>' : '') +
    (page * 25 < data.lineup.commentCount ? '<button type="button" class="text-button" data-comments-page="' + (page + 1) + '">下一页</button>' : '');
}

function bindInteraction(form, action, id, submitPayload, afterSubmit) {
  const challenge = bindChallenge(form, action, id);
  form.addEventListener('submit', async (event) => {
    event.preventDefault();
    challenge.message('');
    challenge.submit.disabled = true;
    try {
      const payload = { ...submitPayload(), captcha: challenge.payload() };
      await api('lineups/' + encodeURIComponent(id) + '/' + (action === 'rate' ? 'rating' : 'comments'), payload);
      if (form.isConnected) {
        if (action === 'comment') form.elements.text.value = '';
        form.querySelector('[data-challenge]').hidden = true;
        form.querySelector('[data-verify]').hidden = false;
        form.elements.answer.value = '';
        showToast(action === 'rate' ? '评分已保存' : '评论已发布');
        await afterSubmit();
      }
      loadLineups();
    } catch (failure) {
      if (!form.isConnected || handleExpiredSession(failure, () => showDetail(id))) return;
      challenge.message(failure.message);
      if (failure.code === 'captcha') await challenge.reload();
      else challenge.submit.disabled = false;
    }
  });
}

function setLineupFavorite(id, favorite) {
  return api('lineups/' + encodeURIComponent(id) + '/favorite', { favorite }, 'PUT');
}

function bindLineupFavorite(id, initialFavorite) {
  const button = $('#favorite-lineup');
  let favorite = Boolean(state.user && initialFavorite);

  function renderFavorite() {
    button.setAttribute('aria-pressed', String(favorite));
    button.textContent = favorite ? '已收藏' : '收藏';
  }

  async function resumeFavorite(desired) {
    const version = modalVersion;
    let message;
    try {
      const result = await setLineupFavorite(id, desired);
      message = result.favorite ? '已收藏阵容' : '已取消收藏';
    } catch (failure) {
      message = failure.message;
    }
    if (modal.open && version === modalVersion) showDetail(id, message);
  }

  renderFavorite();
  button.addEventListener('click', async () => {
    const desired = !favorite;
    if (!state.user) {
      showAuth(() => resumeFavorite(desired), () => showDetail(id));
      return;
    }
    button.disabled = true;
    try {
      const result = await setLineupFavorite(id, desired);
      if (!button.isConnected) return;
      favorite = result.favorite;
      renderFavorite();
      showToast(favorite ? '已收藏阵容' : '已取消收藏');
    } catch (failure) {
      if (button.isConnected && !handleExpiredSession(failure, () => resumeFavorite(desired))) showToast(failure.message);
    } finally {
      button.disabled = false;
    }
  });
}

async function showDetail(id, confirmation, shared = false) {
  activeDetail = shared ? null : id;
  const version = ++modalVersion;
  setDetailUrl(id, shared);
  modalShell('正在载入阵容…', '<p class="intro">正在读取阵容内容。</p>', 'FORMATION DETAILS');
  try {
    const data = await api(shared ? 'share/' + encodeURIComponent(id) + '?preview=1' : 'lineups/' + encodeURIComponent(id));
    if (version !== modalVersion) return;
    const lineup = data.lineup;
    const description = splitLineupDescription(lineup.description ?? '');
    const { prose, diagrams, originalAuthor, originalVideoUrl } = description;
    const sourceHref = lineupSourceHref(originalVideoUrl);
    const sklandGuide = isSklandGuideHref(sourceHref);
    const tags = lineup.covenants.map(c => '<span class="tag">' + escape(c) + '</span>').join('');
    const roster = lineup.members.map(member => {
      const roles = [member.damage && '输出核心', member.stacking && '叠层核心', member.elite && '精锐'].filter(Boolean);
      const labels = [member.tier === 0 ? '特殊' : member.tier && ('等阶 ' + member.tier), ...roles, ...member.equipment].filter(Boolean);
      return '<div class="roster-item">' + portrait(member) + '<div><strong>' + escape(member.name) + '</strong><small>' + escape(labels.join(' · ')) + '</small></div></div>';
    }).join('');
    modalShell(lineup.title, `
      ${confirmation ? '<p class="intro" role="status">' + escape(confirmation) + '</p>' : ''}
      <div class="detail-meta">作者 · ${escape(lineup.author)} ${shared ? '· 分享快照' : lineup.sample ? '· 示例阵容' : '· ' + escape(dateText(lineup.createdAt))} ${tags}</div>
      ${originalAuthor || originalVideoUrl ? `<section class="original-video" aria-labelledby="original-video-title">
        <h3 id="original-video-title">${sklandGuide ? '原攻略 · 转自森空岛' : sourceHref ? '原视频 · 转自 B 站' : '攻略来源'}</h3>
        <div class="original-video-details">
          ${originalAuthor ? `<p>原作者 · ${escape(originalAuthor)}</p>` : ''}
          ${sourceHref ? `<a class="button secondary compact" href="${escape(sourceHref)}" target="_blank" rel="noopener noreferrer">${sklandGuide ? '在森空岛打开' : '在哔哩哔哩观看'} ${icon('arrow')}</a>`
            : originalVideoUrl ? `<p>${escape(originalVideoUrl)}</p>` : ''}
        </div>
      </section>` : ''}
      <div class="action-line"><button type="button" class="button primary" id="use-lineup">去使用</button>
      <button type="button" class="button secondary" id="copy-link">${icon('copy')} 复制短链接</button>
      ${shared ? '' : '<button type="button" class="button secondary favorite-action" id="favorite-lineup" aria-pressed="false">收藏</button>'}
      ${data.canEdit ? '<button type="button" class="button primary" id="edit-lineup">编辑阵容</button>' : ''}</div>
      <p class="intro" id="desktop-feedback" role="status" hidden></p>
      <a class="text-button" id="desktop-download" href="${ASP_DOWNLOAD_URL}" hidden>下载 ASP Assistant</a>
      <textarea class="field" id="copy-fallback" aria-label="复制内容" readonly hidden></textarea>
      <h3>阵容干员 · ${lineup.members.length} 名</h3><div class="roster">${roster}</div>
      ${renderActiveCovenants(lineup.activeCovenants)}
      ${lineup.strategyPortraits?.length ? '<h3>可用策略</h3><div class="detail-meta">' +
        lineup.strategyPortraits.map(({ name, image }) => `<span class="tag strategy-tag">
          ${image ? `<img src="${escape(image)}" alt="" loading="lazy">` : ''}${escape(name)}</span>`).join('') + '</div>' : ''}
      ${prose || diagrams ? `<h3>阵容介绍</h3><div id="lineup-description"${diagrams ? ' class="has-diagrams"' : ''}>
        ${prose ? `<div class="description">${renderDescriptionProse(prose)}</div>` : ''}</div>` : ''}
      ${shared ? '' : `
      <section class="interaction" aria-labelledby="rating-title">
        <h3 id="rating-title">玩家评分</h3><div class="rating-info" id="rating-info"></div><p class="intro" id="my-rating"></p>
        ${state.user ? `<form id="rating-form"><div class="rating-choices" role="group" aria-label="选择评分">
        ${[1, 2, 3, 4, 5].map(score => '<label title="' + score + ' 星"><input type="radio" class="visually-hidden" name="score" value="' + score + '" aria-label="' + score + ' 星" required>' + icon('star') + '</label>').join('')}</div>
        ${challengeMarkup('rating')}<p class="form-error" data-error role="alert" hidden></p>
        <div class="action-line"><button type="button" class="button secondary" data-verify>获取验证码</button><button type="submit" class="button primary" disabled>提交评分</button></div></form>` : '<button type="button" class="button secondary" data-login-detail>登录后评分</button>'}
      </section>
      <section class="interaction" aria-labelledby="comment-title">
        <h3 id="comment-title"></h3>${state.user ? `<form id="comment-form">
        <label class="visually-hidden" for="comment-text">匿名评论</label>
        <textarea class="field" name="text" id="comment-text" maxlength="600" placeholder="分享你的打法心得或提问…" required rows="3"></textarea>
        ${challengeMarkup('comment')}<p class="form-error" data-error role="alert" hidden></p>
        <div class="action-line"><button type="button" class="button secondary" data-verify>获取验证码</button><button type="submit" class="button primary" disabled>匿名发布</button></div></form>` : '<button type="button" class="button secondary" data-login-detail>登录后评论</button>'}
        <ul class="comment-list" id="comment-list"></ul><div class="comment-pages" id="comment-pages"></div>
      </section>`}`, 'FORMATION DETAILS');
    const introduction = $('#lineup-description');
    if (introduction) renderPublishedDescription(introduction, description, lineup.members.map(member => member.name)).catch(() => {});
    if (!shared) {
      bindLineupFavorite(id, data.isFavorite);
      bindPublishedDetailInteractions(id, data, version);
    }
    async function copy(value, success) {
      try {
        await navigator.clipboard.writeText(value);
        if (version === modalVersion) showToast(success);
      } catch {
        if (version !== modalVersion) return;
        const fallback = $('#copy-fallback');
        fallback.hidden = false;
        fallback.value = value;
        fallback.select();
      }
    }
    $('#use-lineup').addEventListener('click', (event) => {
      const feedback = $('#desktop-feedback');
      const download = $('#desktop-download');
      useLineup(event.currentTarget, lineup.shortPath, (message, opening) => {
        feedback.textContent = message;
        feedback.hidden = false;
        download.hidden = !opening;
      });
    });
    $('#copy-link').addEventListener('click', () => copy(new URL(lineup.shortPath, location.origin).href, '已复制阵容短链接'));
    $('#edit-lineup')?.addEventListener('click', () => showLineupEditor(lineup, data.revision));
  } catch (error) {
    if (version === modalVersion) modalShell('无法打开阵容', '<p class="intro">' + escape(error.message) + '</p>', 'FORMATION DETAILS');
  }
}

function bindPublishedDetailInteractions(id, data, version) {
  renderRating(data);
  renderComments(data);
  const refresh = async (page = 1, mode = 'both') => {
    try {
      const updated = await api('lineups/' + encodeURIComponent(id) + '?commentsPage=' + page);
      if (version !== modalVersion) return;
      if (mode !== 'comments') renderRating(updated);
      if (mode !== 'rating') renderComments(updated);
    } catch (error) {
      if (version === modalVersion) showToast(error.message);
    }
  };
  const ratingForm = $('#rating-form');
  const commentForm = $('#comment-form');
  if (ratingForm && commentForm) {
    bindInteraction(ratingForm, 'rate', id, () => ({ score: Number(ratingForm.elements.score.value) }), () => refresh(1, 'rating'));
    bindInteraction(commentForm, 'comment', id, () => ({ text: commentForm.elements.text.value }), () => refresh(1, 'comments'));
  }
  for (const button of modal.querySelectorAll('[data-login-detail]')) {
    button.addEventListener('click', () => showAuth(() => showDetail(id), () => showDetail(id)));
  }
  $('#comment-pages').addEventListener('click', (event) => {
    const button = event.target.closest('[data-comments-page]');
    if (button) refresh(Number(button.dataset.commentsPage), 'comments');
  });
}

document.addEventListener('click', (event) => {
  const target = event.target;
  if (!(target instanceof Element)) return;
  if (target.closest('#account-button')) showAccount();
  else if (target.closest('[data-publish]')) showPublish();
  else if (target.closest('[data-guide]')) showGuide();
  else if (target.closest('[data-close]')) modal.close();
  else if (target.closest('#clear-filters, [data-reset]')) resetFilters();
  else if (target.closest('[data-retry]')) loadLineups();
  else if (target.closest('[data-covenant]')) {
    const button = target.closest('[data-covenant]');
    state.covenant = button.dataset.covenant;
    for (const option of $('#covenants').querySelectorAll('button')) {
      const selected = option === button;
      option.classList.toggle('selected', selected);
      option.setAttribute('aria-pressed', selected);
    }
    refreshResults();
  } else if (target.closest('[data-page]')) {
    state.page = Number(target.closest('[data-page]').dataset.page);
    loadLineups();
    $('.toolbar').scrollIntoView({ block: 'nearest' });
  } else if (target.closest('[data-use-lineup]')) {
    const button = target.closest('[data-use-lineup]');
    useLineup(button, button.dataset.useLineup);
  } else if (target.closest('[data-lineup]') && !event.metaKey && !event.ctrlKey && !event.altKey && !event.shiftKey) {
    event.preventDefault();
    showDetail(target.closest('[data-lineup]').dataset.lineup);
  }
});
modal.addEventListener('close', () => {
  if (modal.open) return;
  if (activeDetail || new URLSearchParams(location.search).has('share')) setDetailUrl(null);
  activeDetail = null;
  modalVersion++;
  modalContent.replaceChildren();
});
query.addEventListener('input', () => {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(refreshResults, 180);
});
for (const control of [source, sort, rated]) control.addEventListener('change', refreshResults);
$('#toggle-filters').addEventListener('click', event => {
  const button = event.currentTarget;
  button.setAttribute('aria-expanded', String(button.getAttribute('aria-expanded') === 'false'));
});
await Promise.allSettled([
  api('catalog').then(({ covenants }) => {
    state.badges = covenants;
    covenantButtons();
  }),
  api('auth/session').then(({ user, mailAvailable }) => {
    state.user = user;
    state.mailAvailable = mailAvailable;
    refreshAccountButton();
  })
]);
loadLineups();
const initialParams = new URL(location.href).searchParams;
const initialId = initialParams.get('lineup');
const initialShare = initialParams.get('share');
if (/^u-[A-Za-z0-9_-]{22}$/.test(initialShare ?? '')) showDetail(initialShare, null, true);
else if (initialId) showDetail(initialId);
else if (initialParams.get('compose') === '1') showLineupEditor(null, null, null, true);
