const $ = (selector, root = document) => root.querySelector(selector);
const escape = (value) => String(value ?? '').replace(/[&<>"']/g, c => ({
  '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;'
}[c]));
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

async function useLineup(button, readShare, status = showToast) {
  if (!isWindowsDesktop(navigator)) {
    status('当前平台暂不支持，请在 Windows PC 端使用。');
    return;
  }
  if (openingDesktop) return;
  openingDesktop = true;
  button.disabled = true;
  try {
    const share = Promise.resolve().then(readShare);
    const [uri] = await Promise.all([share.then(desktopImportUri), copyDesktopShare(share)]);
    if (!button.isConnected) return;
    status('阵容已复制，正在打开 ASP Assistant…', true);
    const stayedOnPage = await openDesktopProtocol(uri);
    if (!button.isConnected) return;
    if (stayedOnPage) location.assign(ASP_DOWNLOAD_URL);
    else status('阵容已复制。若客户端未打开，可前往官网下载。', true);
  } catch {
    if (button.isConnected) status('无法复制阵容，请检查剪贴板权限，或在阵容详情复制分享文本。');
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

function renderCard(lineup) {
  const faction = lineup.covenants[0] ?? '其他阵容';
  const badge = state.badges[faction];
  const visible = lineup.members.slice(0, 5).map(portrait).join('');
  const more = lineup.members.length > 5 ? '<span class="portrait more">+' + (lineup.members.length - 5) + '</span>' : '';
  const score = lineup.rating === null ? '暂无评分' : lineup.rating.toFixed(1) + ' (' + lineup.ratingCount + ')';
  return `<article class="card"><a class="card-link" href="?lineup=${encodeURIComponent(lineup.id)}" data-lineup="${escape(lineup.id)}">
    <div class="card-band" data-theme="${escape(faction)}">
      <span class="tag">${escape(faction)}</span>
      <span class="band-code">${lineup.sample ? '示例阵容' : '玩家发布'}</span>
      ${badge ? '<img class="band-badge" src="' + escape(badge) + '" alt="" loading="lazy">' : ''}
    </div>
    <div class="card-body"><h3>${escape(lineup.title)}</h3><p class="byline">作者 · ${escape(lineup.author)}</p><div class="members">${visible}${more}</div></div></a>
    <div class="card-foot"><span class="${lineup.rating === null ? '' : 'gold'}">${icon('star')}${score}</span><span aria-label="${lineup.commentCount} 条评论">${icon('chat')}${lineup.commentCount}</span><button class="text-button card-use" type="button" data-use-lineup="${escape(lineup.id)}" aria-label="使用阵容：${escape(lineup.title)}">去使用</button></div>
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
    count.innerHTML = '找到 <strong>' + total + '</strong> 套阵容';
    cards.innerHTML = items.length ? items.map(renderCard).join('') :
      '<div class="empty">' + icon('search') + '<h3>没有找到阵容</h3><p>试试其他关键词，或清除筛选条件。</p><button type="button" class="button secondary" data-reset>清除筛选</button></div>';
    const max = Math.ceil(total / pageSize);
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

function setDetailUrl(id) {
  const url = new URL(location.href);
  if (id) url.searchParams.set('lineup', id);
  else url.searchParams.delete('lineup');
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
  modalShell('如何发布阵容', `<p class="intro">网站支持 ASP Assistant 的 ASPA-LINEUP/1 和 /2 分享文本。</p>
    <ol class="guide-steps"><li>在 ASP Assistant 的「阵容」页选中一套阵容，点击「导出文本」。</li>
    <li>点击「发布阵容」直接组建阵容，或在编辑器中导入分享文本继续修改。</li>
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

async function showDetail(id, confirmation) {
  activeDetail = id;
  const version = ++modalVersion;
  setDetailUrl(id);
  modalShell('正在载入阵容…', '<p class="intro">正在读取干员和讨论。</p>', 'FORMATION DETAILS');
  try {
    const data = await api('lineups/' + encodeURIComponent(id));
    if (version !== modalVersion) return;
    const lineup = data.lineup;
    const tags = lineup.covenants.map(c => '<span class="tag">' + escape(c) + '</span>').join('');
    const roster = lineup.members.map(member => {
      const roles = [member.damage && '输出核心', member.stacking && '叠层核心'].filter(Boolean);
      const labels = [member.tier && ('等阶 ' + member.tier), ...roles, ...member.equipment].filter(Boolean);
      return '<div class="roster-item">' + portrait(member) + '<div><strong>' + escape(member.name) + '</strong><small>' + escape(labels.join(' · ')) + '</small></div></div>';
    }).join('');
    modalShell(lineup.title, `
      ${confirmation ? '<p class="intro" role="status">' + escape(confirmation) + '</p>' : ''}
      <div class="detail-meta">作者 · ${escape(lineup.author)} ${lineup.sample ? '· 示例阵容' : '· ' + escape(dateText(lineup.createdAt))} ${tags}</div>
      <div class="action-line"><button type="button" class="button primary" id="use-lineup">去使用</button>
      <button type="button" class="button secondary" id="copy-share">${icon('copy')} 复制分享文本</button>
      <button type="button" class="button secondary" id="copy-link">${icon('copy')} 复制阵容链接</button>
      ${data.canEdit ? '<button type="button" class="button primary" id="edit-lineup">编辑阵容</button>' : ''}</div>
      <p class="intro" id="desktop-feedback" role="status" hidden></p>
      <a class="text-button" id="desktop-download" href="${ASP_DOWNLOAD_URL}" hidden>下载 ASP Assistant</a>
      <textarea class="field" id="copy-fallback" aria-label="复制内容" readonly hidden></textarea>
      <h3>阵容干员 · ${lineup.members.length} 名</h3><div class="roster">${roster}</div>
      ${lineup.description ? '<h3>阵容介绍</h3><p class="description">' + escape(lineup.description) + '</p>' : ''}
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
      </section>`, 'FORMATION DETAILS');
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
      useLineup(event.currentTarget, () => lineup.share, (message, opening) => {
        feedback.textContent = message;
        feedback.hidden = false;
        download.hidden = !opening;
      });
    });
    $('#copy-share').addEventListener('click', () => copy(lineup.share, '已复制分享文本'));
    $('#copy-link').addEventListener('click', () => copy(location.href, '已复制阵容链接'));
    $('#edit-lineup')?.addEventListener('click', () => showLineupEditor(lineup, data.revision));
  } catch (error) {
    if (version === modalVersion) modalShell('无法打开阵容', '<p class="intro">' + escape(error.message) + '</p>', 'FORMATION DETAILS');
  }
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
    useLineup(button, async () => {
      const { lineup } = await api('lineups/' + encodeURIComponent(button.dataset.useLineup));
      return lineup.share;
    });
  } else if (target.closest('[data-lineup]') && !event.metaKey && !event.ctrlKey && !event.altKey && !event.shiftKey) {
    event.preventDefault();
    showDetail(target.closest('[data-lineup]').dataset.lineup);
  }
});
modal.addEventListener('close', () => {
  if (modal.open) return;
  if (activeDetail) setDetailUrl(null);
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
const initialId = new URL(location.href).searchParams.get('lineup');
if (initialId) showDetail(initialId);
