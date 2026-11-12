function refreshAccountButton() {
  $('#account-button').innerHTML = icon('people') + (state.user ? '我的主页' : '登录 / 注册');
  refreshNotificationButton();
}

function profileRow(lineup, index, view) {
  const title = escape(lineup.title);
  return `<li class="profile-lineup" data-profile-item="${index}">
    <div class="profile-row">
      <div class="profile-row-info">
        <a href="?lineup=${encodeURIComponent(lineup.id)}" data-lineup="${escape(lineup.id)}">${title}</a>
        <p>${view === 'favorites' ? escape(lineup.author) + ' · ' : ''}${escape(dateText(lineup.createdAt))}</p>
      </div>
      <div class="action-line profile-row-actions">${view === 'published' ? `
        <button type="button" class="button secondary compact" data-profile-edit aria-label="编辑 ${title}">编辑</button>
        <button type="button" class="button secondary compact profile-danger" data-profile-delete aria-label="删除 ${title}">删除</button>` : `
        <button type="button" class="button secondary compact" data-profile-unfavorite aria-label="取消收藏 ${title}">取消收藏</button>`}</div>
    </div>
  </li>`;
}

function showAccount(initialView = 'published', initialPage = 1) {
  if (!state.user) { showAuth(() => showAccount(initialView, initialPage)); return; }
  activeDetail = null;
  modalVersion++;
  setDetailUrl(null);
  modalShell('我的主页', `<div id="profile">
    <div class="profile-account">
      <div><p class="intro">当前登录邮箱</p><p class="account-email">${escape(state.user.email)}</p></div>
      <button type="button" class="button secondary compact" id="logout">退出登录</button>
    </div>
    <p class="form-error" id="account-error" role="alert" hidden></p>
    <div class="profile-tabs" role="group" aria-label="我的阵容">
      <button type="button" class="button secondary compact" data-profile-view="published" aria-controls="profile-list">我的发布</button>
      <button type="button" class="button secondary compact" data-profile-view="favorites" aria-controls="profile-list">我的收藏</button>
    </div>
    <p class="profile-status" id="profile-status" role="status" hidden></p>
    <ul class="profile-list" id="profile-list" aria-busy="true"></ul>
    <div class="pagination" id="profile-pages" aria-label="我的阵容分页"></div>
  </div>`, 'ACCOUNT');
  const profile = $('#profile');
  const list = $('#profile-list', profile);
  const pages = $('#profile-pages', profile);
  const status = $('#profile-status', profile);
  let view = initialView, page = initialPage, request = 0, items = [];

  function report(message = '', error = false) {
    status.textContent = message;
    status.hidden = !message;
    status.classList.toggle('form-error', error);
  }

  function resumeAccount() { showAccount(view, page); }

  async function loadPage(nextView, nextPage, message, error = false) {
    const currentRequest = ++request;
    view = nextView;
    page = nextPage;
    report(message, error);
    for (const tab of profile.querySelectorAll('[data-profile-view]')) {
      tab.setAttribute('aria-pressed', String(tab.dataset.profileView === view));
    }
    list.setAttribute('aria-busy', 'true');
    list.innerHTML = '<li class="profile-empty">正在载入阵容…</li>';
    pages.replaceChildren();
    try {
      const params = new URLSearchParams({ view, page });
      const result = await api('me/lineups?' + params);
      if (!profile.isConnected || currentRequest !== request) return;
      const max = Math.max(1, Math.ceil(result.total / result.pageSize));
      if (result.page > max) return loadPage(view, max, message, error);
      items = result.items;
      page = result.page;
      list.innerHTML = items.length ? items.map((item, index) => profileRow(item, index, view)).join('')
        : `<li class="profile-empty">${view === 'published' ? '暂无发布的阵容' : '暂无收藏的阵容'}</li>`;
      if (max > 1) pages.innerHTML = `
        <button type="button" class="button secondary compact" data-profile-page="${page - 1}" ${page <= 1 ? 'disabled' : ''}>上一页</button>
        <span>第 ${page} / ${max} 页</span>
        <button type="button" class="button secondary compact" data-profile-page="${page + 1}" ${page >= max ? 'disabled' : ''}>下一页</button>`;
    } catch (failure) {
      if (!profile.isConnected || currentRequest !== request || handleExpiredSession(failure, resumeAccount)) return;
      report(failure.message, true);
      list.innerHTML = '<li class="profile-empty"><button type="button" class="button secondary compact" data-profile-retry>重新载入</button></li>';
    } finally {
      if (currentRequest === request) list.setAttribute('aria-busy', 'false');
    }
  }

  function closeDeleteConfirmation(row) {
    row.querySelector('.profile-confirm').remove();
    const trigger = row.querySelector('[data-profile-delete]');
    trigger.hidden = false;
    trigger.focus();
  }

  async function actOnLineup(button, item, row) {
    button.disabled = true;
    report();
    const itemPath = 'lineups/' + encodeURIComponent(item.id);
    const deleting = button.hasAttribute('data-profile-confirm-delete');
    const cancel = row.querySelector('[data-profile-cancel-delete]');
    if (cancel) cancel.disabled = true;
    try {
      if (button.hasAttribute('data-profile-edit')) {
        const result = await api(itemPath);
        if (!button.isConnected) return;
        if (!result.canEdit) throw new Error('当前账号无法编辑这个阵容。');
        showLineupEditor(result.lineup, result.revision);
        return;
      }
      if (deleting) {
        await api(itemPath, { revision: item.revision }, 'DELETE');
        loadLineups();
      } else {
        await setLineupFavorite(item.id, false);
      }
      if (profile.isConnected) {
        if (button.isConnected) profile.querySelector('[data-profile-view][aria-pressed="true"]').focus();
        loadPage(view, page, deleting ? '阵容已删除' : '已取消收藏');
      }
    } catch (failure) {
      if (!button.isConnected || handleExpiredSession(failure, resumeAccount)) return;
      if (deleting) closeDeleteConfirmation(row);
      if (failure.code === 'revision_conflict') loadPage(view, page, failure.message, true);
      else report(failure.message, true);
    } finally {
      button.disabled = false;
    }
  }

  profile.addEventListener('click', event => {
    const button = event.target.closest('button');
    if (!button || button.disabled) return;
    if (button.dataset.profileView) { loadPage(button.dataset.profileView, 1); return; }
    if (button.dataset.profilePage) { loadPage(view, Number(button.dataset.profilePage)); return; }
    if (button.hasAttribute('data-profile-retry')) { loadPage(view, page); return; }
    const row = button.closest('[data-profile-item]');
    if (!row) return;
    const item = items[Number(row.dataset.profileItem)];
    if (button.hasAttribute('data-profile-delete')) {
      button.hidden = true;
      row.insertAdjacentHTML('beforeend', `<div class="profile-confirm" role="group" aria-label="确认删除阵容">
        <p>确定删除「${escape(item.title)}」？删除后无法恢复。</p>
        <div class="action-line"><button type="button" class="button secondary compact" data-profile-cancel-delete>取消</button>
          <button type="button" class="button compact profile-delete-confirm" data-profile-confirm-delete>确认删除</button></div>
      </div>`);
      row.querySelector('[data-profile-cancel-delete]').focus();
    } else if (button.hasAttribute('data-profile-cancel-delete')) closeDeleteConfirmation(row);
    else actOnLineup(button, item, row);
  });

  $('#logout').addEventListener('click', async (event) => {
    const button = event.currentTarget;
    button.disabled = true;
    try {
      await api('auth/logout', {});
      state.user = null;
      refreshAccountButton();
      if (button.isConnected) modal.close();
      showToast('已退出登录');
    } catch (failure) {
      if (!button.isConnected) return;
      const error = $('#account-error');
      error.textContent = failure.message;
      error.hidden = false;
      button.disabled = false;
    }
  });
  loadPage(view, page);
}

function showAuth(next, back) {
  modalVersion++;
  const backButton = back ? '<button type="button" class="button secondary" id="auth-back">返回阵容</button>' : '';
  const bindBack = () => $('#auth-back')?.addEventListener('click', back);

  function enterEmail(email = '') {
    modalShell('邮箱登录 / 注册', `
      <p class="intro">输入邮箱获取登录验证码。首次验证通过后会自动创建账号。</p>
      <form id="email-form">
        <label for="auth-email">邮箱地址</label>
        <input type="email" id="auth-email" class="field" name="email" required maxlength="254"
          autocomplete="email" placeholder="name@example.com" value="${escape(email)}">
        ${challengeMarkup('auth-image')}
        <p class="form-error" data-error role="alert" hidden></p>
        <div class="action-line"><button type="submit" class="button primary" disabled>发送邮件验证码</button>${backButton}</div>
      </form>`, 'EMAIL ACCESS');
    bindBack();
    const form = $('#email-form');
    const challenge = bindChallenge(form, 'login');
    challenge.reload();
    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      challenge.submit.disabled = true;
      challenge.message('');
      const address = form.elements.email.value.trim();
      try {
        const sent = await api('auth/code', { email: address, captcha: challenge.payload() });
        if (form.isConnected) enterCode(address, sent);
      } catch (failure) {
        if (!form.isConnected) return;
        challenge.message(failure.message);
        await challenge.reload();
      }
    });
  }

  function enterCode(email, sent) {
    modalShell('输入邮件验证码', `
      <p class="intro">6 位验证码已发送至 <strong>${escape(email)}</strong>，${sent.expiresIn / 60} 分钟内有效。</p>
      <form id="code-form">
        <label for="email-code">邮件验证码</label>
        <input type="text" id="email-code" name="code" class="field" inputmode="numeric" autocomplete="one-time-code"
          required pattern="[0-9]{6}" maxlength="6" placeholder="输入 6 位数字">
        <p class="form-error" data-error role="alert" hidden></p>
        <div class="action-line"><button type="submit" class="button primary">验证并登录</button>
          <button type="button" class="button secondary" id="change-email">修改邮箱 / 重新发送</button>${backButton}</div>
      </form>`, 'VERIFY EMAIL');
    bindBack();
    $('#change-email').addEventListener('click', () => enterEmail(email));
    const form = $('#code-form');
    const submit = $('[type="submit"]', form);
    form.elements.code.focus();
    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      const error = $('[data-error]', form);
      submit.disabled = true;
      error.hidden = true;
      try {
        const { user } = await api('auth/verify', { id: sent.id, code: form.elements.code.value });
        state.user = user;
        refreshAccountButton();
        if (!form.isConnected) return;
        showToast('已登录');
        if (next) next();
        else modal.close();
      } catch (failure) {
        error.textContent = failure.message;
        error.hidden = false;
        submit.disabled = false;
      }
    });
  }

  if (state.mailAvailable === false) {
    modalShell('邮箱登录 / 注册', '<p class="intro">邮件验证码暂不可用，请稍后再试。</p>' + backButton, 'EMAIL ACCESS');
    bindBack();
  } else enterEmail();
}

function handleExpiredSession(error, next) {
  if (error.code !== 'login_required') return false;
  state.user = null;
  refreshAccountButton();
  const back = activeDetail ? () => showDetail(activeDetail) : null;
  showAuth(next, back);
  return true;
}
