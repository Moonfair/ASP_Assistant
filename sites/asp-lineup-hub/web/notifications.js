let notificationsInitialized = false;
let notificationUser = null;
let notificationBadgeVersion = 0;

function renderNotificationBadge(unread) {
  const label = unread ? `通知，${unread} 条未读` : '通知';
  const button = $('#notifications-button');
  const badge = $('#notifications-badge');
  const status = $('#notifications-status');
  button.setAttribute('aria-label', label);
  badge.hidden = !unread;
  badge.textContent = unread > 99 ? '99+' : unread || '';
  const announcement = unread ? label : '';
  if (status.textContent !== announcement) status.textContent = announcement;
}

async function fetchNotificationPage(page, user) {
  const badgeVersion = ++notificationBadgeVersion;
  const result = await api('me/notifications?page=' + page);
  if (user === state.user && badgeVersion === notificationBadgeVersion) {
    renderNotificationBadge(result.unread);
  }
  return result;
}

async function refreshNotificationButton() {
  const button = $('#notifications-button');
  if (!notificationsInitialized) {
    button.addEventListener('click', () => showNotifications());
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'visible') refreshNotificationButton();
    });
    setInterval(() => {
      if (document.visibilityState === 'visible' && state.user) refreshNotificationButton();
    }, 60_000);
    notificationsInitialized = true;
  }

  const user = state.user;
  button.hidden = !user;
  if ((!user || user !== notificationUser) && modal.open && $('#notification-panel')) modal.close();
  if (user !== notificationUser) {
    notificationUser = user;
    notificationBadgeVersion++;
    renderNotificationBadge(0);
  }
  if (!user || document.visibilityState !== 'visible') return;
  try {
    await fetchNotificationPage(1, user);
  } catch (failure) {
    if (user === state.user && failure.code === 'login_required') {
      state.user = null;
      refreshAccountButton();
    }
  }
}

async function showNotifications() {
  const user = state.user;
  if (!user) return;
  activeDetail = null;
  const version = ++modalVersion;
  setDetailUrl(null);
  modalShell('通知', `
    <p class="form-error" id="notification-error" role="alert" hidden></p>
    <div id="notification-panel" tabindex="-1"></div>`, 'NOTIFICATIONS');
  const panel = $('#notification-panel');
  const error = $('#notification-error');
  const isCurrent = () => modal.open && version === modalVersion && user === state.user;
  let items = [];
  let busy = false;

  function showError(failure) {
    error.textContent = failure.message;
    error.hidden = false;
  }

  async function loadPage(page, focus = false) {
    busy = true;
    error.hidden = true;
    panel.innerHTML = '<p class="intro" role="status">正在读取通知…</p>';
    try {
      const data = await fetchNotificationPage(page, user);
      if (!isCurrent()) return;
      const lastPage = Math.max(1, Math.ceil(data.total / data.pageSize));
      if (data.page > lastPage) return await loadPage(lastPage, focus);
      items = data.items;
      const entries = items.map((item, index) => `
        <li><button type="button" class="notification-entry${item.read ? '' : ' notification-entry-unread'}" data-notification-index="${index}">
          <span class="notification-entry-meta">
            <strong>${escape(item.title)}</strong>
            ${item.read ? '' : '<span class="notification-unread-label">未读</span>'}
            <time>${escape(dateText(item.createdAt))}</time>
          </span>
          <span class="notification-preview">${escape(item.comment)}</span>
        </button></li>`).join('');
      const pagination = lastPage > 1 ? `<nav class="notification-pages" aria-label="通知分页">
        <button type="button" class="button secondary compact" data-notification-page="${data.page - 1}"${data.page === 1 ? ' disabled' : ''}>上一页</button>
        <span>第 ${data.page} / ${lastPage} 页</span>
        <button type="button" class="button secondary compact" data-notification-page="${data.page + 1}"${data.page >= lastPage ? ' disabled' : ''}>下一页</button>
        </nav>` : '';
      panel.innerHTML = (entries ? `<ul class="notification-list">${entries}</ul>`
        : '<p class="intro" role="status">还没有通知。</p>') + pagination;
      if (focus) panel.focus();
    } catch (failure) {
      if (!isCurrent() || handleExpiredSession(failure, showNotifications)) return;
      showError(failure);
      panel.innerHTML = `<button type="button" class="button secondary compact" data-notification-page="${page}">重试</button>`;
      if (focus) panel.focus();
    } finally {
      busy = false;
    }
  }

  panel.addEventListener('click', async (event) => {
    if (busy || !isCurrent()) return;
    const page = event.target.closest('[data-notification-page]');
    if (page) {
      await loadPage(Number(page.dataset.notificationPage), true);
      return;
    }
    const button = event.target.closest('[data-notification-index]');
    if (!button) return;
    const item = items[Number(button.dataset.notificationIndex)];
    busy = true;
    button.disabled = true;
    error.hidden = true;
    try {
      if (!item.read) {
        const data = await api('me/notifications/' + encodeURIComponent(item.id) + '/read', {}, 'PUT');
        if (user === state.user) {
          notificationBadgeVersion++;
          renderNotificationBadge(data.unread);
        }
      }
    } catch (failure) {
      if (!isCurrent() || handleExpiredSession(failure, showNotifications)) return;
      showError(failure);
      busy = false;
      button.disabled = false;
      button.focus();
      return;
    }
    if (!isCurrent()) return;

    const detailVersion = modalVersion + 1;
    await showDetail(item.lineupId);
    if (!modal.open || modalVersion !== detailVersion || user !== state.user) return;
    const comments = $('#comment-title');
    if (comments) {
      comments.setAttribute('tabindex', '-1');
      comments.focus({ preventScroll: true });
      comments.scrollIntoView({ block: 'start' });
    }
  });

  await loadPage(1);
}
